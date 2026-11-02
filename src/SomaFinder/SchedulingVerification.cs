// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net;
using System.Net.Sockets;
namespace SomaFinder;

internal static class SchedulingVerification
{
    public static int Run(string[] args)
    {
        var report=System.IO.Path.GetFullPath(args.Single(x=>x.StartsWith("--report="))[9..]);
        var root=System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-scheduling-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var store=new Store(root+"/data",root+"/config",root+"/state");Directory.CreateDirectory(store.StateDirectory);
        foreach(var name in new[]{"alpha","beta"}){var path=root+"/"+name;Directory.CreateDirectory(path);store.SaveWorkspace(new Workspace{Name=name,ConfigPath=store.ConfigDirectory+"/"+name+".yml",Roots=new(){new(){Name="docs",Path=path}}});File.WriteAllText(store.StateDirectory+"/"+name+".sqlite","");}
        var app=new FinderApplication(store,new Preferences{StartupSync=false,PeriodicSync=false},true);int failures=0;var records=new List<object>();
        app.Startup+=async(_,_)=>
        {
            using var server=new Server(store,app.Backend.Workspaces);
            foreach(var workspace in app.Backend.Workspaces)app.Backend.VerificationPort(workspace,server.Port);
            async Task Test(string name,Func<Task> action){try{await action();records.Add(new{name,passed=true});}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});}Console.WriteLine(name+" "+(failures==0?"completed":"recorded"));}
            void Check(bool value,string message){if(!value)throw new Exception(message);}
            var ws=app.Backend.Workspaces[0];var scope=new List<string>{ws.Roots[0].Path};var state=scope.ToDictionary(x=>x,_=>true);
            Task<List<Hit>> Query(string mode,CancellationToken token=default,Action<SearchUpdate>? update=null)=>app.Backend.Search("needle",mode,scope,20,state,null,token,update);
            try
            {
                await Test("cancelled model wait retains its slot while keyword requests bypass it",async()=>
                {
                    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);server.Model=async()=>{entered.TrySetResult();await release.Task;};
                    using var cancel=new CancellationTokenSource();var heavy=Query("hybrid",cancel.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));cancel.Cancel();bool cancelled=false;try{await heavy;}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"UI cancellation did not return");
                    int count=server.Models;var queued=Query("vector");await Task.Delay(100);Check(server.Models==count&&!queued.IsCompleted,"Cancelled UI wait released active model slot");
                    var light=await Query("lexical").WaitAsync(TimeSpan.FromSeconds(5));Check(light.Count==1,"Keyword was blocked behind model");release.TrySetResult();await queued.WaitAsync(TimeSpan.FromSeconds(10));Check(server.ModelMaximum==1,"Model requests overlapped");server.Model=null;
                });
                await Test("keyword requests have bounded parallelism and single workspace skips census",async()=>
                {
                    int files=server.Files;server.KeywordDelay=150;await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Query("lexical")));Check(server.KeywordMaximum==2,"Expected two light lanes: "+server.KeywordMaximum);Check(server.Files==files,"Single workspace performed quota census");server.KeywordDelay=0;
                });
                await Test("incremental workspace results respect global quota and reuse census cache",async()=>
                {
                    int files=server.Files;server.ObservedLimits.Clear();server.KeywordDelay=80;var paths=app.Backend.Workspaces.SelectMany(w=>w.Roots.Select(r=>r.Path)).ToList();var selection=paths.ToDictionary(x=>x,_=>true);var batches=new List<SearchUpdate>();
                    var results=await app.Backend.Search("needle","lexical",paths,7,selection,null,default,x=>{if(x.Hits!=null)batches.Add(x);});Check(batches.Count==2&&batches[0].Completed==1&&batches[0].Total==2,"No incremental workspace batch");Check(server.ObservedLimits.Sum()==7&&server.ObservedLimits.All(n=>n>=1),"Global quota exceeded or empty share");int once=server.Files;Check(once-files==2,"Unexpected census size");await app.Backend.Search("needle","lexical",paths,7,selection);Check(server.Files==once,"Repeated census not cached");Check(results.Count==2,"Workspace results lost");server.KeywordDelay=0;
                });
                await Test("hybrid publishes keyword batches first, replaces them without duplicates and shares the same global quota",async()=>
                {
                    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var preliminary=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var paths=app.Backend.Workspaces.SelectMany(w=>w.Roots.Select(r=>r.Path)).ToList();var selection=paths.ToDictionary(x=>x,_=>true);var batches=new List<SearchUpdate>();server.ObservedRequests.Clear();server.Model=async()=>{entered.TrySetResult();await release.Task;};
                    try
                    {
                        var search=app.Backend.Search("needle","hybrid",paths,7,selection,null,default,x=>{if(x.Hits==null)return;batches.Add(x);if(x.Hits.Count==2&&x.Hits.All(h=>h.MatchMode=="lexical"))preliminary.TrySetResult();});
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await preliminary.Task.WaitAsync(TimeSpan.FromSeconds(5));Check(!search.IsCompleted,"Hybrid did not wait for held model");release.TrySetResult();var results=await search.WaitAsync(TimeSpan.FromSeconds(10));
                        Check(results.Count==2&&results.All(h=>h.MatchMode=="hybrid"),"Hybrid replacement lost a range or kept preliminary duplicates");Check(batches.All(x=>x.Hits!.Count<=2&&x.Hits.DistinctBy(app.Backend.ResultKey,StringComparer.OrdinalIgnoreCase).Count()==x.Hits.Count),"Progress batches duplicated files");
                        foreach(var command in new[]{"search.lexical","search.hybrid"}){var requests=server.ObservedRequests.Where(x=>x.Command==command).ToList();Check(requests.Count==2&&requests.Sum(x=>x.Limit)==7&&requests.All(x=>x.Limit>=1),"Incorrect per-stage global quota: "+command);}
                    }
                    finally{release.TrySetResult();server.Model=null;}
                });
                await Test("timeout returns to caller while active model work remains serialized",async()=>
                {
                    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);server.Model=async()=>{entered.TrySetResult();await release.Task;};app.Backend.SearchWaitLimit=TimeSpan.FromMilliseconds(300);
                    var slow=Query("hybrid");await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));bool timeout=false;try{await slow;}catch(TimeoutException){timeout=true;}Check(timeout,"Wait timeout absent");app.Backend.SearchWaitLimit=TimeSpan.FromMinutes(1);var next=Query("vector");int count=server.Models;await Task.Delay(60);Check(server.Models==count,"Timed-out request released model slot");release.TrySetResult();await next.WaitAsync(TimeSpan.FromSeconds(5));server.Model=null;
                });
                await Test("service transition counts active model reads after UI cancellation",async()=>
                {
                    Check(server.ModelMaximum==1&&server.KeywordMaximum<=2,"Read lanes exceeded configured bounds");var menu=app.CreateTrayMenu();Check(menu.Items.OfType<System.Windows.Controls.MenuItem>().Any(x=>(string?)x.Header=="暂停至下次启动"),"Tray label missing");await app.Backend.Pause(.2);var pausedMenu=app.CreateTrayMenu();Check(pausedMenu.Items.OfType<System.Windows.Controls.MenuItem>().Any(x=>(string?)x.Header=="恢复索引"),"Resume action missing");app.Backend.Continue();Check(!app.Backend.CanResume,"Resume left a hold");
                });
            }
            catch(Exception ex){failures++;records.Add(new{name="suite",passed=false,error=ex.ToString()});}
            finally{Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests=records,scope="Actual frontend adapter HTTP requests against a controlled loopback API, not a Soma model throughput benchmark."},new JsonSerializerOptions{WriteIndented=true}));app.Backend.Dispose();app.Shutdown(failures==0?0:1);}
        };
        return app.Run();
    }
    sealed class Server : IDisposable
    {
        readonly HttpListener listener=new();readonly Store store;readonly Dictionary<string,Workspace> workspaces;
        int activeModels,activeKeyword;public int Models,ModelMaximum,KeywordMaximum,Files,KeywordDelay;
        public readonly List<int> ObservedLimits=new();public readonly List<(string Command,int Limit)> ObservedRequests=new();public Func<Task>? Model;public int Port{get;}
        public Server(Store store,List<Workspace> workspaces)
        {
            this.store=store;this.workspaces=workspaces.ToDictionary(w=>w.Name);var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();Port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();listener.Prefixes.Add($"http://127.0.0.1:{Port}/");listener.Start();_=Listen();
        }
        async Task Listen(){try{while(listener.IsListening){var context=await listener.GetContextAsync();_=Handle(context);}}catch(HttpListenerException){}catch(ObjectDisposedException){}}
        async Task Handle(HttpListenerContext c)
        {
            try
            {
                using var doc=await JsonDocument.ParseAsync(c.Request.InputStream);var request=doc.RootElement;string command=request.GetProperty("command").GetString()!;
                // Each workspace is sent to a distinct URL path by the verification port hook.
                var name=c.Request.Url!.AbsolutePath.Split('/').Last();if(!workspaces.ContainsKey(name))name="alpha";var ws=workspaces[name];object data;
                object Item()=>new{project="docs",path="sentinel.md",virtualPath="soma://docs/sentinel.md",status="ready",snippet="raw sentinel snippet"};
                if(command=="status")data=new{workspace=ws.Name,databaseFile=store.StateDirectory+"/"+ws.Name+".sqlite",configFile=ws.ConfigPath};
                else if(command=="project.files"){Interlocked.Increment(ref Files);data=new{files=new[]{Item()}};}
                else
                {
                    int n=request.GetProperty("options").GetProperty("limit").GetInt32();lock(ObservedLimits){ObservedLimits.Add(n);ObservedRequests.Add((command,n));}
                    bool heavy=command!="search.lexical";
                    if(heavy){int active=Interlocked.Increment(ref activeModels);Interlocked.Increment(ref Models);ModelMaximum=Math.Max(ModelMaximum,active);try{if(Model!=null)await Model();}finally{Interlocked.Decrement(ref activeModels);}}
                    else{int active=Interlocked.Increment(ref activeKeyword);KeywordMaximum=Math.Max(KeywordMaximum,active);try{await Task.Delay(KeywordDelay);}finally{Interlocked.Decrement(ref activeKeyword);}}
                    data=new{results=new[]{Item()}};
                }
                var bytes=JsonSerializer.SerializeToUtf8Bytes(new{success=true,data});c.Response.ContentType="application/json";c.Response.ContentLength64=bytes.Length;await c.Response.OutputStream.WriteAsync(bytes);
            }
            catch(Exception ex){try{c.Response.StatusCode=500;await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(ex.Message));}catch{}}
            finally{c.Response.Close();}
        }
        public void Dispose(){listener.Close();}
    }
}
