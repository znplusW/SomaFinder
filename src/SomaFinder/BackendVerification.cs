// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SomaFinder;

// Hosted-runner tests use only synthetic documents and stores below this invocation's fixture.
internal static class BackendVerification
{
    public static int Run(string[] args)
    {
        string Arg(string name)=>args.Single(a=>a.StartsWith(name+"="))[(name.Length+1)..];
        CloudSandbox.Initialize(Arg("--cloud-root"));Environment.SetEnvironmentVariable("SOMAFINDER_TEST","1");
        return new Suite(CloudSandbox.Root,Arg("--soma"),Arg("--old-soma"),Arg("--report")).Run();
    }
    sealed class Suite
    {
        readonly string root,exe,oldExe,report;readonly FinderApplication app;readonly Store store;readonly List<object> records=new();readonly List<Process> helpers=new();int failures,sequence;
        public Suite(string root,string exe,string oldExe,string report)
        {
            this.root=root;this.exe=exe;this.oldExe=oldExe;this.report=report;store=NewStore("choice");
            app=new FinderApplication(store,Prefs(),true);
        }
        Preferences Prefs()=>new(){SomaExe=exe,StartupSync=false,PeriodicSync=false,BatteryBlocksAuto=false,LowMemoryBlocksAuto=false,KnownBackendPaths=new(){exe,oldExe}};
        Store NewStore(string child)=>new(System.IO.Path.Combine(root,child,"data"),System.IO.Path.Combine(root,child,"config/soma"),System.IO.Path.Combine(root,child,"state/soma"));
        Workspace AddWorkspace(Store target,string name="main")
        {
            var docs=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target.DataDirectory)!,"docs",name);Directory.CreateDirectory(docs);File.WriteAllText(System.IO.Path.Combine(docs,"sentinel.md"),"retained cloud fixture "+name);
            var ws=new Workspace{Name=name,ConfigPath=System.IO.Path.Combine(target.ConfigDirectory,name+".yml"),Roots=new(){new(){Name="docs",Path=docs}}};target.SaveWorkspace(ws);return ws;
        }
        public int Run()
        {
            app.Startup+=async(_,_)=>
            {
                try{await Execute();}catch(Exception ex){failures++;records.Add(new{name="suite initialization",passed=false,error=ex.ToString()});}
                finally
                {
                    foreach(var helper in helpers)try{if(!helper.HasExited){helper.Kill(true);await helper.WaitForExitAsync();}}catch(Exception ex){failures++;records.Add(new{name="fixture cleanup",passed=false,error=ex.Message});}
                    if(CloudSandbox.Errors.Count>0){failures++;records.Add(new{name="unexpected UI error",passed=false,errors=CloudSandbox.Errors.ToArray()});}
                    foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();
                    Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),version=typeof(Program).Assembly.GetName().Version?.ToString(),failures,tests=records,scope="Real Soma 0.10.7 and 0.10.6, isolated config/state, real UI routed events and Windows advisory byte locks; no local user files, no physical hardware acceptance"},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);
                }
            };return app.Run();
        }
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        async Task Test(string name,Func<Task> action)
        {
            Console.WriteLine("START "+name);var start=DateTimeOffset.UtcNow;
            try{await action();records.Add(new{name,passed=true,seconds=(DateTimeOffset.UtcNow-start).TotalSeconds});Console.WriteLine("PASS "+name);}
            catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine("FAIL "+name+" "+ex.Message);}
        }
        Process Start(string path,Store target,string workspace,params string[] arguments)
        {
            var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"--workspace",workspace}.Concat(arguments))start.ArgumentList.Add(arg);
            start.Environment["XDG_CONFIG_HOME"]=System.IO.Path.GetDirectoryName(target.ConfigDirectory)!;start.Environment["XDG_STATE_HOME"]=System.IO.Path.GetDirectoryName(target.StateDirectory)!;
            var process=Process.Start(start)!;helpers.Add(process);var log=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"backend-helper-"+(++sequence)+".log");
            void Write(string? line){if(line!=null)lock(process)File.AppendAllText(log,line+Environment.NewLine);}
            process.OutputDataReceived+=(_,e)=>Write(e.Data);process.ErrorDataReceived+=(_,e)=>Write(e.Data);process.BeginOutputReadLine();process.BeginErrorReadLine();return process;
        }
        async Task Seed(Store target,Workspace ws)
        {
            var process=Start(exe,target,ws.Name,"system","scan");await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));Check(process.ExitCode==0,"Fixture scan failed");
        }
        async Task<Process> Server(string path,Store target,string workspace)
        {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            var process=Start(path,target,workspace,"server","--port="+port);var identity=new Native.Proc(process.Id,0,path,"",process.StartTime.ToUniversalTime().Ticks,new[]{"--workspace",workspace,"server","--port="+port});
            var deadline=DateTimeOffset.UtcNow.AddSeconds(35);while(DateTimeOffset.UtcNow<deadline){Check(!process.HasExited,"Fixture server exited");if(await BackendInstances.Status(identity)!=null)return process;await Task.Delay(200);}throw new TimeoutException("Fixture server did not become ready");
        }
        async Task AnswerChoice(string button,string? selected=null)
        {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};bool answered=false;Exception? error=null;
            timer.Tick+=(_,_)=>
            {
                var window=app.Windows.OfType<BackendSelectionWindow>().FirstOrDefault();if(window==null)return;timer.Stop();
                try{var grid=CloudVerification.Descendants<DataGrid>(window).Single();if(selected!=null)grid.SelectedItem=grid.Items.OfType<BackendCandidate>().Single(c=>Native.SameFilePath(c.Path,selected));CloudVerification.Descendants<Button>(window).Single(b=>(string?)b.Content==button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));answered=true;}catch(Exception ex){error=ex;window.Close();}
            };
            timer.Start();try{bool configured=await app.ConfigureBackend().WaitAsync(TimeSpan.FromSeconds(50));if(error!=null)throw error;Check(answered,"Backend selection was not shown");Check(configured==(selected!=null),"Cancel/accept startup result wrong");}finally{timer.Stop();}
        }
        async Task Execute()
        {
            await Test("actual different versions and same-name paths are detected independently",async()=>
            {
                var catalog=await BackendDiscovery.Catalog(new[]{exe,oldExe,exe},Array.Empty<Native.Proc>());Check(catalog.Items.Count==2&&catalog.Valid.Count()==2,"Paths were collapsed or missing");Check(catalog.Items.Any(c=>c.Version.Contains("0.10.7"))&&catalog.Items.Any(c=>c.Version.Contains("0.10.6")),"Actual version probe mismatch");
            });
            await Test("cancel startup selection starts no server or index and preserves preferences",async()=>
            {
                var before=JsonSerializer.Serialize(app.Backend.Prefs);await AnswerChoice("稍后设置");Check(!app.Backend.IsInitialized&&JsonSerializer.Serialize(app.Backend.Prefs)==before,"Cancel changed startup state");Check(!(await Native.SomaProcesses(exe)).Any(p=>Native.IsServer(p)||Native.IsIndex(p)),"Cancel started a backend");
            });
            await Test("accepted real UI selection survives reload without a repeat dialog",async()=>
            {
                app.Backend.Prefs.PauseUntil=DateTimeOffset.UtcNow.AddHours(4);await AnswerChoice("使用所选后端",oldExe);var restored=store.Load();Check(Native.SameFilePath(restored.SomaExe,oldExe)&&restored.PauseUntil==app.Backend.Prefs.PauseUntil,"Selection did not persist");
                Check(!BackendDiscovery.NeedsChoice(restored,await BackendDiscovery.Scan(restored)),"Remembered selection prompts again");Check(await app.ConfigureBackend(),"Confirmed startup not accepted");Check(!app.Backend.IsInitialized,"Configuration itself started a backend");
            });
            await Test("missing selected executable asks again even with a single replacement",async()=>
            {
                app.Backend.Prefs.SomaExe=System.IO.Path.Combine(root,"removed-backend","soma.exe");app.Backend.Prefs.ConfirmedSomaExe=app.Backend.Prefs.SomaExe;app.Backend.Prefs.KnownBackendPaths=new(){oldExe};var before=app.Backend.Prefs.SomaExe;
                await AnswerChoice("稍后设置");Check(app.Backend.Prefs.SomaExe==before&&!app.Backend.IsInitialized,"Missing version was silently replaced");
            });
            await Test("renamed backend and paths containing spaces are detected while running",async()=>
            {
                var renamed=System.IO.Path.Combine(root,"手动 backend copy","custom-engine.exe");Directory.CreateDirectory(System.IO.Path.GetDirectoryName(renamed)!);File.Copy(exe,renamed);var target=NewStore("renamed");var ws=AddWorkspace(target,"renamed-fixture");await Seed(target,ws);var process=await Server(renamed,target,ws.Name);
                try{var prefs=Prefs();prefs.KnownBackendPaths.Add(renamed);var catalog=await BackendDiscovery.Scan(prefs);Store.Atomic(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"renamed-discovery.json"),JsonSerializer.Serialize(new{expected=renamed,pid=process.Id,catalog}));Check(catalog.Valid.Any(c=>Native.SameFilePath(c.Path,renamed)&&c.ProcessCount==1),"Known renamed running copy missing: "+JsonSerializer.Serialize(catalog));var padded=Enumerable.Range(0,100).Select(n=>System.IO.Path.Combine(root,"absent-"+n,"soma.exe")).Append(renamed);Check((await Native.BackendProcesses(padded)).Any(p=>p.Id==process.Id),"Absent candidates hid an explicit custom executable");}finally{process.Kill(true);await process.WaitForExitAsync();}
            });
            await Test("same executable with multiple workspace servers is reused as one installation",async()=>
            {
                var target=NewStore("multi-workspace");var a=AddWorkspace(target,"same-exe-a");var b=AddWorkspace(target,"same-exe-b");await Seed(target,a);await Seed(target,b);var pa=await Server(exe,target,a.Name);var pb=await Server(exe,target,b.Name);using var backend=new Backend(target,Prefs());
                try{await backend.Initialize(false);Check(backend.InstanceConflict.Length==0,backend.InstanceConflict);var processes=(await Native.SomaProcesses(exe)).Where(p=>Native.IsServer(p)&&new[]{a.Name,b.Name}.Contains(Native.WorkspaceName(p))).ToList();Check(processes.Count==2,"Duplicate server launched");var catalog=await BackendDiscovery.Catalog(new[]{exe},processes);Check(catalog.Items.Count==1&&catalog.Items[0].ProcessCount==2,"Workspace count mistaken for install count");}finally{await backend.StopAll();}Check(pa.HasExited&&pb.HasExited,"Verified selected services not stopped");
            });
            await Test("different path and version sharing the same index blocks manual and automatic indexing",async()=>
            {
                var target=NewStore("shared");var ws=AddWorkspace(target,"shared-fixture");await Seed(target,ws);var process=await Server(oldExe,target,ws.Name);var prefs=Prefs();prefs.StartupSync=true;prefs.PeriodicSync=true;using var backend=new Backend(target,prefs);var db=System.IO.Path.Combine(target.StateDirectory,ws.Name+".sqlite");string before=Store.Digest(db);
                try
                {
                    await backend.Initialize();Check(backend.InstanceConflict.Contains(oldExe),"Foreign version not identified");Check(backend.ActiveIndexProcessId==null,"Automatic index started despite a conflict");
                    try{await backend.RunSync(backend.Workspaces.Single(),true);throw new Exception("Manual index accepted shared store conflict");}catch(IOException){}
                    await backend.Pulse();Check(backend.ActiveIndexProcessId==null&&Store.Digest(db)==before,"Conflict changed existing index");
                    await backend.Pause(.1);Check(!process.HasExited,"Pause killed unselected backend");
                }finally{await backend.StopAll();}Check(!process.HasExited,"Exit killed unselected backend");process.Kill(true);await process.WaitForExitAsync();
                using var recovered=new Backend(target,Prefs());try{await recovered.Initialize(false);Check(recovered.InstanceConflict.Length==0,"Conflict did not clear after other backend exited");var status=await recovered.Rpc(recovered.Workspaces.Single(),"status");Check(status.GetProperty("projects")[0].GetProperty("stats").GetProperty("documents").GetInt32()==1,"Retained index cannot be read");}finally{await recovered.StopAll();}
            });
            await Test("same executable and workspace name in a different data directory are not adopted or killed",async()=>
            {
                var ours=NewStore("independent-ours");var other=NewStore("independent-other");var a=AddWorkspace(ours,"identical-name");var b=AddWorkspace(other,"identical-name");await Seed(ours,a);await Seed(other,b);var foreign=await Server(exe,other,b.Name);using var backend=new Backend(ours,Prefs());
                try{await backend.Initialize(false);Check(backend.InstanceConflict.Length==0,backend.InstanceConflict);Check(BackendInstances.SameStore(await backend.Rpc(backend.Workspaces.Single(),"status"),ours,a),"Connected to foreign database");}finally{await backend.StopAll();}
                Check(!foreign.HasExited,"Independent store was stopped");foreign.Kill(true);await foreign.WaitForExitAsync();
            });
            await Test("Windows file identity detects a shared database through a hard-link alias",async()=>
            {
                var target=NewStore("file-identity");var ws=AddWorkspace(target,"identity-fixture");await Seed(target,ws);var db=System.IO.Path.Combine(target.StateDirectory,ws.Name+".sqlite");var alias=System.IO.Path.Combine(root,"database-alias.sqlite");
                var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.Environment["SOMAFINDER_DB_SOURCE"]=db;start.Environment["SOMAFINDER_DB_ALIAS"]=alias;
                foreach(var arg in new[]{"-NoProfile","-NonInteractive","-Command","$ErrorActionPreference='Stop';New-Item -ItemType HardLink -Path $env:SOMAFINDER_DB_ALIAS -Target $env:SOMAFINDER_DB_SOURCE | Out-Null"})start.ArgumentList.Add(arg);
                using var process=Process.Start(start)!;await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));Check(process.ExitCode==0,"Hard-link fixture failed: "+await process.StandardError.ReadToEndAsync());
                var foreign=JsonSerializer.SerializeToElement(new{workspace="other-name",databaseFile=alias,configFile=oldExe});Check(!Native.SameFilePath(alias,db)&&Native.SameStorageFile(alias,db),"Aliased database identity not recognized");Check(BackendInstances.TouchesStore(foreign,target,new[]{ws}),"Shared database alias did not conflict");Check(!Native.SameStorageFile(db,ws.ConfigPath),"Unrelated files share an identity");
            });
            await Test("real advisory byte lock blocks unknown writers and clears after owner exits",async()=>
            {
                var target=NewStore("writer");var ws=AddWorkspace(target,"writer-fixture");await Seed(target,ws);var file=System.IO.Path.Combine(target.StateDirectory,"locks",ws.Name+".lock");var ready=System.IO.Path.Combine(root,"writer-ready");
                var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.Environment["SOMAFINDER_TEST_LOCK"]=file;start.Environment["SOMAFINDER_TEST_READY"]=ready;
                foreach(var arg in new[]{"-NoProfile","-NonInteractive","-Command","$ErrorActionPreference='Stop';$taskLock=$env:SOMAFINDER_TEST_LOCK;$taskStream=[IO.FileStream]::new($taskLock,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::ReadWrite);$taskBytes=[Text.Encoding]::UTF8.GetBytes((@{pid=$PID;command='cloud-lock-fixture'}|ConvertTo-Json -Compress));$taskStream.SetLength(0);$taskStream.Write($taskBytes,0,$taskBytes.Length);$taskStream.Flush();$taskStream.Lock([long]::MaxValue-1,1);[IO.File]::WriteAllText($env:SOMAFINDER_TEST_READY,'ready');try{while($true){Start-Sleep -Milliseconds 100}}finally{$taskStream.Dispose()}"})start.ArgumentList.Add(arg);
                var holder=Process.Start(start)!;helpers.Add(holder);var deadline=DateTimeOffset.UtcNow.AddSeconds(20);while(!File.Exists(ready)){if(holder.HasExited)throw new Exception("Lock fixture exited: "+await holder.StandardError.ReadToEndAsync());if(DateTimeOffset.UtcNow>deadline)throw new TimeoutException("Lock fixture did not start");await Task.Delay(100);}
                using var backend=new Backend(target,Prefs());
                try{Check(BackendInstances.Writers(target,new[]{ws}).Any(w=>w.Pid==holder.Id),"Actual locked byte was not detected");await backend.Initialize(false);Check(backend.InstanceConflict.Length>0,"Unknown writer not blocked");await backend.StopAll();Check(!holder.HasExited,"Unknown writer killed");holder.Kill(true);await holder.WaitForExitAsync();var released=Stopwatch.StartNew();var writers=BackendInstances.Writers(target,new[]{ws});while(writers.Count>0&&released.Elapsed<TimeSpan.FromSeconds(5)){await Task.Delay(50);writers=BackendInstances.Writers(target,new[]{ws});}Store.Atomic(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"writer-release.json"),JsonSerializer.Serialize(new{milliseconds=released.ElapsedMilliseconds,writers}));Check(writers.Count==0,"Released lock still blocks: "+JsonSerializer.Serialize(writers));}
                finally{if(!holder.HasExited){holder.Kill(true);await holder.WaitForExitAsync();}}
            });
        }
    }
}
