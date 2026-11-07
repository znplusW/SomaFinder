// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace SomaFinder;

internal static class ModelMemoryVerification
{
    internal const string FixtureVariable="SOMAFINDER_MEMORY_FIXTURE";
    internal static int Run(string[] args)
    {
        var report=System.IO.Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-model-memory-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(report)!);
        var store=new Store(root+"/data",root+"/config",root+"/state");var docs=root+"/docs";Directory.CreateDirectory(docs);File.WriteAllText(docs+"/sentinel.md","needle retained fixture");
        var ws=new Workspace{Name="main",ConfigPath=store.ConfigDirectory+"/main.yml",Roots=new(){new(){Name="docs",Path=docs}}};store.SaveWorkspace(ws);
        Directory.CreateDirectory(store.StateDirectory);File.WriteAllText(store.StateDirectory+"/main.sqlite","synthetic retained index bytes");
        var runtime=root+"/runtime";Directory.CreateDirectory(runtime);
        foreach(var name in new[]{"soma.exe","llama-server.exe"})File.Copy(Environment.ProcessPath!,runtime+"/"+name);
        Store.Atomic(root+"/metadata.json",JsonSerializer.Serialize(new{config=ws.ConfigPath,database=store.StateDirectory+"/main.sqlite",state=store.StateDirectory,runtime}));
        Environment.SetEnvironmentVariable(FixtureVariable,root);SetControl(root,new());
        var prefs=new Preferences{SomaExe=runtime+"/soma.exe",StartupSync=false,PeriodicSync=false,LowMemoryBlocksAuto=false};prefs.Volumes[docs]=Native.Identify(docs)!;
        var app=new FinderApplication(store,prefs,true);int failures=0;var records=new List<object>();long now=0;ulong? available=3UL*1024*1024*1024;
        var b=app.Backend;b.MemoryClock=()=>now;b.AvailableMemory=()=>available;
        app.Startup+=async(_,_)=>
        {
            void Check(bool yes,string message){if(!yes)throw new Exception(message);}
            async Task Test(string name,Func<Task> action){try{await action();records.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine("FAIL "+name+" "+ex.Message);}}
            async Task Advance(int seconds){for(int i=0;i<seconds;i+=10){now+=10*1000;await b.Pulse();}}
            async Task Reset(bool enabled=true){b.Prefs.ReleaseIdleModelsOnLowMemory=enabled;b.Resumed();available=3UL*1024*1024*1024;await b.Pulse();}
            async Task Search()=>await b.Search("needle","vector",new(){docs},20,new Dictionary<string,bool>{{docs,true}});
            async Task WaitEvent(string value,int after=0){var watch=Stopwatch.StartNew();while(Events(root,value)<=after){if(watch.Elapsed>TimeSpan.FromSeconds(12))throw new TimeoutException(value);await Task.Delay(25);}}
            try
            {
                await Test("option defaults off and old preferences and draft remain compatible",()=>
                {
                    Check(!new Preferences().ReleaseIdleModelsOnLowMemory,"Default enabled");Check(!JsonSerializer.Deserialize<Preferences>("{}")!.ReleaseIdleModelsOnLowMemory,"Old preferences enabled");Check(!JsonSerializer.Deserialize<SettingsDraft>("{}")!.ReleaseIdleModels,"Old draft enabled");return Task.CompletedTask;
                });
                await Test("continuous two-minute pressure AND fifteen-minute idle, exact boundaries",()=>
                {
                    var p=new ModelMemoryPolicy();bool ready=false;for(long t=0;t<900000;t+=10000)ready=p.Observe(true,1,false,t);Check(!ready,"Early reclaim");Check(p.Observe(true,1,false,900000),"15 minute boundary missed");
                    p=new();for(long t=0;t<=840000;t+=10000)p.Observe(true,ulong.MaxValue,false,t);for(long t=840000;t<960000;t+=10000)Check(!p.Observe(true,1,false,t),"Pressure shorter than 2 minutes");Check(p.Observe(true,1,false,960000),"Independent pressure timer missed");return Task.CompletedTask;
                });
                await Test("memory recovery resets pressure only; busy resets idle; sampling gaps restart evidence",()=>
                {
                    var p=new ModelMemoryPolicy();for(long t=0;t<=900000;t+=10000)p.Observe(true,1,false,t);Check(!p.Observe(true,ModelMemoryPolicy.LowMemoryBytes,false,910000),"Recovered pressure retained");for(long t=920000;t<1040000;t+=10000)Check(!p.Observe(true,1,false,t),"Pressure reset lost");Check(p.Observe(true,1,false,1040000),"Idle timer incorrectly reset by memory recovery");Check(!p.Observe(true,1,true,1050000),"Busy eligible");Check(!p.Observe(true,1,false,1200000),"Unobserved gap counted");Check(!p.Observe(false,1,false,1210000),"Disabled eligible");return Task.CompletedTask;
                });
                await Test("native process ownership and loopback port discover router without waking a model",async()=>
                {
                    await b.Initialize(false);var runtimeData=await WaitRuntime(root);int routerPid=runtimeData.GetProperty("pid").GetInt32(),port=runtimeData.GetProperty("port").GetInt32();
                    Check(ModelRuntime.ListeningPorts(routerPid).SequenceEqual(new[]{port}),"TCP ownership mismatch");Check(Events(root,"unload-start")==0,"Discovery unloaded model");
                });
                await Test("settings checkbox saves opt-in, reopens checked and cancel retains saved value",async()=>
                {
                    var w=new SettingsWindow(app);w.Show();CloudVerification.Descendants<TabControl>(w).Single().SelectedIndex=1;w.UpdateLayout();var toggle=CloudVerification.Descendants<CheckBox>(w).Single(c=>System.Windows.Automation.AutomationProperties.GetAutomationId(c)=="SomaFinder.ReleaseIdleModels");Check(toggle.IsChecked==false,"Checkbox default enabled");toggle.IsChecked=true;
                    CloudVerification.Descendants<Button>(w).Single(c=>c.Content as string=="保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var watch=Stopwatch.StartNew();while(w.IsVisible){if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new TimeoutException("Settings save");await Task.Delay(20);}Check(store.Load().ReleaseIdleModelsOnLowMemory&&b.Prefs.ReleaseIdleModelsOnLowMemory,"Opt-in not saved");
                    var next=new SettingsWindow(app);next.Show();CloudVerification.Descendants<TabControl>(next).Single().SelectedIndex=1;next.UpdateLayout();var restored=CloudVerification.Descendants<CheckBox>(next).Single(c=>System.Windows.Automation.AutomationProperties.GetAutomationId(c)=="SomaFinder.ReleaseIdleModels");Check(restored.IsChecked==true,"Saved checkbox not restored");
                    ((TabControl)CloudVerification.Descendants<TabControl>(next).Single()).SelectedIndex=1;next.UpdateLayout();var image=new RenderTargetBitmap((int)next.ActualWidth,(int)next.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(next);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(System.IO.Path.ChangeExtension(report,"settings.png")))encoder.Save(file);
                    restored.IsChecked=false;CloudVerification.Descendants<Button>(next).Single(c=>c.Content as string=="取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(b.Prefs.ReleaseIdleModelsOnLowMemory,"Cancel applied draft");
                });
                await Test("disabled option never unloads after prolonged low memory and idleness",async()=>
                {await Search();await Reset(false);int before=Events(root,"unload-start");await Advance(1200);Check(Events(root,"unload-start")==before,"Disabled unload");});
                await Test("both timers unload idle models while retaining server PID, keyword data and index bytes",async()=>
                {
                    await Search();await Reset();string digest=Store.Digest(store.StateDirectory+"/main.sqlite");int before=Events(root,"unload-start");await Advance(890);Check(Events(root,"unload-start")==before,"Before 15 minutes");await Advance(10);Check(Events(root,"unload-start")==before+1,"No unload at eligibility: "+b.ModelMemoryStatus+" "+JsonSerializer.Serialize(b.MemorySnapshot));Check(Store.Digest(store.StateDirectory+"/main.sqlite")==digest,"Index modified");
                    var hits=await b.Search("needle","lexical",new(){docs},20);Check(hits.Count==1,"Keyword search lost");
                });
                await Test("next semantic search autoloads model and returns results after reclaim",async()=>
                {int loaded=Events(root,"model-loaded");await Search();Check(Events(root,"model-loaded")==loaded+1,"No demand reload");});
                await Test("active index and its writer lock block reclaim and restart the idle window",async()=>
                {
                    SetControl(root,new());await Reset();int before=Events(root,"unload-start");var index=b.RunSync(b.Workspaces.Single(),true);await WaitEvent("index-start");
                    try{await Advance(1200);Check(!index.IsCompleted&&b.ActiveIndexProcessId!=null,"Index interrupted");Check(Events(root,"unload-start")==before,"Unloaded while indexing");}
                    finally{SetControl(root,new(){ReleaseIndex=true});await index.WaitAsync(TimeSpan.FromSeconds(10));}
                    await b.Pulse();await Advance(890);Check(Events(root,"unload-start")==before,"Index completion did not restart idle");await Advance(10);Check(Events(root,"unload-start")==before+1,"Post-index idle not reclaimed: "+b.ModelMemoryStatus);SetControl(root,new());
                });
                await Test("cancelled UI wait retains active backend lease until real search completes",async()=>
                {
                    await Search();await Reset();int before=Events(root,"unload-start"),entered=Events(root,"semantic-start");SetControl(root,new(){HoldSearch=true});using var cancel=new CancellationTokenSource();var search=b.Search("needle","vector",new(){docs},20,cancellationToken:cancel.Token);await WaitEvent("semantic-start",entered);cancel.Cancel();try{await search;throw new Exception("Wait not cancelled");}catch(OperationCanceledException){}
                    try{await Advance(1200);Check(Events(root,"unload-start")==before,"Cancelled live search reclaimed");}finally{SetControl(root,new());await Task.Delay(150);}
                    await b.Pulse();await Advance(890);Check(Events(root,"unload-start")==before,"Cancelled completion idle not reset");await Advance(10);Check(Events(root,"unload-start")==before+1,"Completed search not reclaimable: "+b.ModelMemoryStatus);
                });
                await Test("new search queues behind accepted unload and aborts additional reclaim",async()=>
                {
                    await Search();await Reset();await Advance(890);int before=Events(root,"unload-start");SetControl(root,new(){HoldUnload=true});now+=10000;var reclaim=b.Pulse();await WaitEvent("unload-start",before);var search=Search();await Task.Delay(100);Check(!search.IsCompleted,"Search bypassed accepted unload");SetControl(root,new());await Task.WhenAll(reclaim,search).WaitAsync(TimeSpan.FromSeconds(12));Check(Events(root,"unload-start")==before+1,"Unexpected extra unload");
                });
                await Test("busy runtime slots and borrowed services are never unloaded",async()=>
                {
                    await Search();await Reset();int before=Events(root,"unload-start");SetControl(root,new(){BusySlots=true});try{await Advance(900);Check(Events(root,"unload-start")==before,"Busy slots unloaded");}finally{SetControl(root,new());}
                    using var borrowed=new Backend(store,new Preferences{SomaExe=prefs.SomaExe,StartupSync=false,PeriodicSync=false,ReleaseIdleModelsOnLowMemory=true});long clock=0;borrowed.MemoryClock=()=>clock;borrowed.AvailableMemory=()=>1;await borrowed.Initialize(false);for(int i=0;i<91;i++){clock+=10000;await borrowed.Pulse();}Check(Events(root,"unload-start")==before,"Borrowed server unloaded");
                });
            }
            catch(Exception ex){failures++;records.Add(new{name="suite",passed=false,error=ex.ToString()});}
            finally
            {
                SetControl(root,new(){ReleaseIndex=true});try{await b.StopAll();}catch(Exception ex){failures++;records.Add(new{name="cleanup",passed=false,error=ex.ToString()});}
                Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),version=typeof(Program).Assembly.GetName().Version?.ToString(),failures,tests=records,scope="Hosted Windows only. Real WPF settings, native process/TCP ownership, real spawned index/writer-lock fixtures and HTTP router protocol; virtual monotonic clock exercises the unchanged 2-minute/15-minute constants. No physical GPU memory or complete Soma/model integration."},new JsonSerializerOptions{WriteIndented=true}));
                var diagnostic=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"model-memory-fixture");Directory.CreateDirectory(diagnostic);
                foreach(var file in Directory.GetFiles(store.DataDirectory,"*.log",SearchOption.AllDirectories).Concat(new[]{root+"/events.txt"}).Where(File.Exists))File.Copy(file,System.IO.Path.Combine(diagnostic,System.IO.Path.GetFileName(file)),true);
                Environment.SetEnvironmentVariable(FixtureVariable,null);b.Dispose();app.Shutdown(failures==0?0:1);
            }
        };return app.Run();
    }

    sealed class Control {public Control(){}public bool HoldSearch{get;set;}public bool HoldUnload{get;set;}public bool BusySlots{get;set;}public bool ReleaseIndex{get;set;}}
    static void SetControl(string root,Control value)=>Store.Atomic(root+"/control.json",JsonSerializer.Serialize(value));
    static Control ReadControl(string root)
    {
        var watch=Stopwatch.StartNew();
        while(true)try
        {using var stream=new FileStream(root+"/control.json",FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);return JsonSerializer.Deserialize<Control>(reader.ReadToEnd())!;}
        catch(IOException ex)when(watch.Elapsed<TimeSpan.FromSeconds(2)&&(ex.HResult&0xffff) is 2 or 32 or 33){Thread.Sleep(5);}
    }
    static int Events(string root,string value)
    {if(!File.Exists(root+"/events.txt"))return 0;using var stream=new FileStream(root+"/events.txt",FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var reader=new StreamReader(stream);return reader.ReadToEnd().Split('\n').Count(x=>x.TrimEnd('\r')==value);}
    static readonly object eventSync=new();
    static void Event(string root,string value){lock(eventSync)File.AppendAllText(root+"/events.txt",value+"\n");}
    static async Task<JsonElement> WaitRuntime(string root){for(int i=0;i<200;i++){if(File.Exists(root+"/runtime.json")){using var d=JsonDocument.Parse(File.ReadAllText(root+"/runtime.json"));return d.RootElement.Clone();}await Task.Delay(50);}throw new TimeoutException("Router readiness");}

    internal static int Fixture(string[] args)
    {
        var root=Environment.GetEnvironmentVariable(FixtureVariable)??throw new InvalidOperationException("Missing fixture root");
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true"||Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")!="github-hosted"||Environment.GetEnvironmentVariable("RUNNER_OS")!="Windows"||!Rules.Contains(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,root)||!System.IO.Path.GetFileName(root).StartsWith("SomaFinder-cloud-model-memory-")||!File.Exists(root+"/cloud-test-marker.json"))throw new InvalidOperationException("Hosted isolated fixture required");
        if(args.Contains("--version")||args.Contains("-V")){Console.WriteLine("soma 0.10.7");return 0;}
        return FixtureAsync(root,args).GetAwaiter().GetResult();
    }
    static async Task<int> FixtureAsync(string root,string[] args)
    {
        using var metadata=JsonDocument.Parse(File.ReadAllText(root+"/metadata.json"));var m=metadata.RootElement;
        if(args.Contains("sync"))
        {
            var directory=m.GetProperty("state").GetString()+"/locks";Directory.CreateDirectory(directory);using var writer=new FileStream(directory+"/main.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);writer.Lock(BackendInstances.LockPosition,1);var bytes=JsonSerializer.SerializeToUtf8Bytes(new{pid=Environment.ProcessId});writer.SetLength(0);writer.Write(bytes);writer.Flush();Event(root,"index-start");try{while(!ReadControl(root).ReleaseIndex)await Task.Delay(50);}finally{writer.Unlock(BackendInstances.LockPosition,1);}return 0;
        }
        if(args.Contains("--model-memory-router-fixture"))
        {
            bool loaded=false;using var router=new MiniServer(async(path,body)=>
            {
                string route=path.Split('?')[0];
                if(route=="/models")return new{data=new[]{new{id="EXPAND",status=new{value=loaded?"loaded":"unloaded"}}}};
                if(route=="/slots")return new[]{new{is_processing=ReadControl(root).BusySlots}};
                if(route=="/models/unload"){Event(root,"unload-start");while(ReadControl(root).HoldUnload)await Task.Delay(10);loaded=false;Event(root,"model-unloaded");return new{success=true};}
                if(route=="/completion"){if(!loaded){loaded=true;Event(root,"model-loaded");}return new{content="fixture"};}
                throw new InvalidOperationException(route);
            });
            Store.Atomic(root+"/runtime.json",JsonSerializer.Serialize(new{pid=Environment.ProcessId,port=router.Port}));await Task.Delay(Timeout.Infinite);return 0;
        }
        if(!args.Contains("server"))return 2;
        bool real=m.TryGetProperty("realExecutable",out var realExecutable)&&realExecutable.ValueKind==JsonValueKind.String;
        var start=new ProcessStartInfo(real?realExecutable.GetString():m.GetProperty("runtime").GetString()+"/llama-server.exe"){UseShellExecute=false,CreateNoWindow=true};
        int realPort=0;
        if(real)
        {
            using(var probe=new TcpListener(IPAddress.Loopback,0)){probe.Start();realPort=((IPEndPoint)probe.LocalEndpoint).Port;}
            foreach(var arg in new[]{"--host","127.0.0.1","--port",realPort.ToString(),"--models-preset",root+"/models.ini","--models-max","5","--models-autoload"})start.ArgumentList.Add(arg);
        }
        else start.ArgumentList.Add("--model-memory-router-fixture");
        using var child=Process.Start(start)!;
        if(real)
        {
            using var health=new HttpClient(new HttpClientHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(1)};bool ready=false;
            for(int i=0;i<200;i++){if(child.HasExited)throw new IOException("Real llama router exited");try{using var response=await health.GetAsync("http://127.0.0.1:"+realPort+"/health");if(response.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}catch(TaskCanceledException){}await Task.Delay(50);}
            if(!ready)throw new TimeoutException("Real llama router not ready");Store.Atomic(root+"/runtime.json",JsonSerializer.Serialize(new{pid=child.Id,port=realPort}));
        }
        int port=int.Parse(args.Single(a=>a.StartsWith("--port="))[7..]);
        using var server=new MiniServer(async(path,body)=>
        {
            using var request=JsonDocument.Parse(body);string command=request.RootElement.GetProperty("command").GetString()!;
            if(command=="status")return new{success=true,data=new{workspace="main",configFile=m.GetProperty("config").GetString(),databaseFile=m.GetProperty("database").GetString(),projects=new[]{new{name="docs",stats=new{documents=1,ready=1,pending=0,failed=0,chunks=1,embeddings=1,lexical=1,vectors=1}}}}};
            if(command!="search.lexical")
            {
                Event(root,"semantic-start");while(ReadControl(root).HoldSearch)await Task.Delay(20);var runtime=await WaitRuntime(root);using var http=new HttpClient(new HttpClientHandler{UseProxy=false});using var response=await http.PostAsJsonAsync("http://127.0.0.1:"+runtime.GetProperty("port").GetInt32()+"/completion",new{model="EXPAND",prompt="hello",n_predict=1});response.EnsureSuccessStatusCode();
            }
            return new{success=true,data=new{results=new[]{new{project="docs",path="sentinel.md",virtualPath="soma://docs/sentinel.md",snippet="needle retained fixture"}}}};
        },port);
        await Task.Delay(Timeout.Infinite);return 0;
    }
    internal static int RunReal(string[] args)=>RunRealAsync(args).GetAwaiter().GetResult();
    static async Task<int> RunRealAsync(string[] args)
    {
        string report=System.IO.Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        string realExecutable=System.IO.Path.GetFullPath(args.Single(a=>a.StartsWith("--llama="))[8..]);
        string tinyModel=System.IO.Path.GetFullPath(args.Single(a=>a.StartsWith("--model="))[8..]);
        var root=System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-model-memory-real-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var records=new List<object>();int failures=0;Backend? backend=null;
        try
        {
            string runtime=root+"/runtime";Directory.CreateDirectory(runtime);File.Copy(Environment.ProcessPath!,runtime+"/soma.exe");
            var store=new Store(root+"/data",root+"/config",root+"/state");string docs=root+"/docs";Directory.CreateDirectory(docs);File.WriteAllText(docs+"/sentinel.md","needle retained fixture");
            var ws=new Workspace{Name="main",ConfigPath=store.ConfigDirectory+"/main.yml",Roots=new(){new(){Name="docs",Path=docs}}};store.SaveWorkspace(ws);Directory.CreateDirectory(store.StateDirectory);File.WriteAllText(store.StateDirectory+"/main.sqlite","synthetic retained index bytes");
            Store.Atomic(root+"/metadata.json",JsonSerializer.Serialize(new{config=ws.ConfigPath,database=store.StateDirectory+"/main.sqlite",state=store.StateDirectory,runtime,realExecutable}));
            Store.Atomic(root+"/models.ini","[EXPAND]\nmodel = "+tinyModel+"\nctx-size = 512\ngpu-layers = 0\nparallel = 1\n");SetControl(root,new());Environment.SetEnvironmentVariable(FixtureVariable,root);
            backend=new(store,new Preferences{SomaExe=runtime+"/soma.exe",StartupSync=false,PeriodicSync=false});await backend.Initialize(false);
            async Task Search()=>await backend.Search("needle","vector",new(){docs},20);
            await Search();var owner=(await Native.SomaProcesses(runtime+"/soma.exe")).Single(Native.IsServer);var runtimeData=await WaitRuntime(root);int routerPid=runtimeData.GetProperty("pid").GetInt32();
            var adapter=ModelRuntime.Find(owner).Single();var tree=Native.Descendants(owner);var before=tree.Where(p=>p.Id!=routerPid&&Native.SameFilePath(p.File,realExecutable)).ToArray();if(before.Length!=1)throw new Exception("Expected one real model worker: "+JsonSerializer.Serialize(tree));
            using var worker=Process.GetProcessById(before[0].Id);long beforeWorkingSet=worker.WorkingSet64,beforePrivate=worker.PrivateMemorySize64;string indexHash=Store.Digest(store.StateDirectory+"/main.sqlite");
            long clock=0;backend.MemoryClock=()=>clock;backend.AvailableMemory=()=>1;backend.Prefs.ReleaseIdleModelsOnLowMemory=true;backend.Resumed();await backend.Pulse();
            for(int i=0;i<180&&Native.Alive(before[0]);i++){clock+=10000;await backend.Pulse();}
            if(Native.Alive(before[0])||!Native.Alive(owner)||!backend.ModelMemoryStatus.Contains("已卸载"))throw new Exception("Real model policy did not release idle worker: "+backend.ModelMemoryStatus);
            records.Add(new{name="production policy unloads actual llama.cpp b10068 worker and retains router",passed=true,oldWorkerPid=before[0].Id,workingSetBefore=beforeWorkingSet,privateBytesBefore=beforePrivate,virtualElapsedMilliseconds=clock});
            var watch=Stopwatch.StartNew();await Search();var after=Native.Descendants(owner).Where(p=>p.Id!=routerPid&&Native.SameFilePath(p.File,realExecutable)).ToArray();if(after.Length!=1||after[0].Id==before[0].Id)throw new Exception("Real model did not autoload in a new worker");
            records.Add(new{name="next real completion autoloads model and completes",passed=true,newWorkerPid=after[0].Id,reloadMilliseconds=watch.Elapsed.TotalMilliseconds});
            if(Store.Digest(store.StateDirectory+"/main.sqlite")!=indexHash)throw new Exception("Index changed during real runtime unload");records.Add(new{name="retained synthetic index bytes unchanged",passed=true});
        }
        catch(Exception ex){failures++;records.Add(new{name="real runtime suite",passed=false,error=ex.ToString()});}
        finally
        {
            if(backend!=null){try{await backend.StopAll();}catch(Exception ex){failures++;records.Add(new{name="cleanup",passed=false,error=ex.ToString()});}backend.Dispose();}
            Environment.SetEnvironmentVariable(FixtureVariable,null);
            Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),version=typeof(Program).Assembly.GetName().Version?.ToString(),failures,tests=records,scope="Real pinned llama.cpp b10068 CPU runtime and SHA256-verified tinyllamas GGUF; real process unload/autoload through production native adapter. Controlled Soma HTTP fixture, not full Soma integration or B390/GPU hardware acceptance."},new JsonSerializerOptions{WriteIndented=true}));
        }
        return failures==0?0:1;
    }
    sealed class MiniServer:IDisposable
    {
        readonly TcpListener listener;readonly Func<string,string,Task<object>> handle;public int Port{get;}
        internal MiniServer(Func<string,string,Task<object>> handle,int port=0){this.handle=handle;listener=new(IPAddress.Loopback,port);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;_=Listen();}
        async Task Listen(){try{while(true){var client=await listener.AcceptTcpClientAsync();_=Serve(client);}}catch(SocketException){}catch(ObjectDisposedException){}}
        async Task Serve(TcpClient client)
        {
            using(client)try
            {
                using var stream=client.GetStream();var header=new List<byte>();var one=new byte[1];
                while(header.Count<16384){if(await stream.ReadAsync(one)==0)return;header.Add(one[0]);if(header.Count>=4&&header.TakeLast(4).SequenceEqual(new byte[]{13,10,13,10}))break;}
                var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");string path=lines[0].Split(' ')[1];byte[] body;
                if(lines.Any(x=>x.StartsWith("Transfer-Encoding:",StringComparison.OrdinalIgnoreCase)&&x.Contains("chunked",StringComparison.OrdinalIgnoreCase)))
                {
                    using var decoded=new MemoryStream();
                    while(true)
                    {
                        int size=Convert.ToInt32((await ReadLine(stream)).Split(';')[0],16);
                        if(size==0){while((await ReadLine(stream)).Length>0){}break;}
                        if(size<0||decoded.Length+size>65536)throw new IOException("Fixture chunks too large");
                        var chunk=new byte[size];await stream.ReadExactlyAsync(chunk);decoded.Write(chunk);if((await ReadLine(stream)).Length!=0)throw new IOException("Fixture chunk terminator");
                    }
                    body=decoded.ToArray();
                }
                else
                {int length=lines.Where(x=>x.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).Select(x=>int.Parse(x[15..].Trim())).FirstOrDefault();if(length<0||length>65536)throw new IOException("Fixture body too large");body=new byte[length];await stream.ReadExactlyAsync(body);}
                var result=await handle(path,Encoding.UTF8.GetString(body));var bytes=JsonSerializer.SerializeToUtf8Bytes(result);var prefix=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: "+bytes.Length+"\r\n\r\n");await stream.WriteAsync(prefix);await stream.WriteAsync(bytes);
            }catch(Exception ex){Console.Error.WriteLine(ex.ToString());}
        }
        static async Task<string> ReadLine(Stream stream)
        {
            var bytes=new List<byte>();var one=new byte[1];
            while(bytes.Count<4096){if(await stream.ReadAsync(one)==0)throw new EndOfStreamException();bytes.Add(one[0]);if(bytes.Count>=2&&bytes[^2]==13&&bytes[^1]==10)return Encoding.ASCII.GetString(bytes.Take(bytes.Count-2).ToArray());}
            throw new IOException("Fixture line too long");
        }
        public void Dispose()=>listener.Stop();
    }
}
