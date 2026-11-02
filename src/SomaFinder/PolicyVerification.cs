// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net;
using System.Net.Sockets;
using System.Windows;
namespace SomaFinder;

internal static class PolicyVerification
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static int Run(string[] args)
    {
        string Arg(string key)=>args.Single(a=>a.StartsWith(key+"="))[(key.Length+1)..];CloudSandbox.Initialize(Arg("--cloud-root"));Environment.SetEnvironmentVariable("SOMAFINDER_TEST","1");
        var suite=new Suite(CloudSandbox.Root,Arg("--soma"),Arg("--report"));return suite.Run();
    }
    sealed class Suite
    {
        readonly string root,exe,report;readonly List<object> records=new();readonly FinderApplication app;int failures,serial;readonly List<Process> helpers=new();
        public Suite(string root,string exe,string report){this.root=root;this.exe=exe;this.report=report;app=new FinderApplication(new Store(System.IO.Path.Combine(root,"app/data")),new Preferences{StartupSync=false,PeriodicSync=false},true);}
        public int Run(){app.Startup+=async(_,_)=>{try{await Execute();}catch(Exception ex){failures++;records.Add(new{name="suite",passed=false,error=ex.ToString()});}finally{foreach(var p in helpers)try{if(!p.HasExited){p.Kill(true);await p.WaitForExitAsync();}}catch{}Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),version=typeof(Program).Assembly.GetName().Version?.ToString(),failures,tests=records,scope="Real Soma isolated stores and native process identities. Power supply and delayed HTTP requests are controlled injection, not physical battery hardware tests."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);}};return app.Run();}
        async Task Test(string name,Func<Task> action){Console.WriteLine("START "+name);try{await action();records.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine("FAIL "+name+" "+ex.Message);}}
        async Task<(Store Store,Workspace Workspace,Backend Backend)> Fixture(string name,bool paused=false)
        {
            var path=System.IO.Path.Combine(root,name);var docs=System.IO.Path.Combine(path,"docs");Directory.CreateDirectory(docs);File.WriteAllText(System.IO.Path.Combine(docs,"sentinel.md"),"retained policy sentinel");
            var store=new Store(System.IO.Path.Combine(path,"data"),System.IO.Path.Combine(path,"config/soma"),System.IO.Path.Combine(path,"state/soma"));var ws=new Workspace{Name=name,ConfigPath=System.IO.Path.Combine(store.ConfigDirectory,name+".yml"),Roots=new(){new(){Name="docs",Path=docs}}};store.SaveWorkspace(ws);
            var process=Start(store,ws.Name,"system","scan");await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));Check(process.ExitCode==0,"Fixture scan failed");
            var prefs=new Preferences{SomaExe=exe,StartupSync=false,PeriodicSync=false,BatteryBlocksAuto=true,BatteryStopsAuto=true,LowMemoryBlocksAuto=false,PauseUntil=paused?DateTimeOffset.UtcNow.AddHours(2):null};var backend=new Backend(store,prefs);backend.PowerSource=()=>true;await backend.Initialize(false);return(store,backend.Workspaces.Single(),backend);
        }
        Process Start(Store store,string ws,params string[] args)
        {
            var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in new[]{"--workspace",ws}.Concat(args))start.ArgumentList.Add(a);start.Environment["XDG_CONFIG_HOME"]=System.IO.Path.GetDirectoryName(store.ConfigDirectory)!;start.Environment["XDG_STATE_HOME"]=System.IO.Path.GetDirectoryName(store.StateDirectory)!;
            var p=Process.Start(start)!;helpers.Add(p);var log=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"policy-helper-"+(++serial)+".log");void Log(string? s){if(s!=null)lock(p)File.AppendAllText(log,s+Environment.NewLine);}p.OutputDataReceived+=(_,e)=>Log(e.Data);p.ErrorDataReceived+=(_,e)=>Log(e.Data);p.BeginOutputReadLine();p.BeginErrorReadLine();return p;
        }
        async Task<List<Native.Proc>> Servers(Workspace ws)=>(await Native.SomaProcesses(exe)).Where(p=>Native.IsServer(p)&&Native.WorkspaceName(p)==ws.Name).ToList();
        async Task Active(Backend backend,Task task){var deadline=DateTimeOffset.UtcNow.AddSeconds(25);while(backend.ActiveIndexProcessId==null){if(task.IsCompleted){await task;throw new Exception("Index completed before interrupt fixture");}if(DateTimeOffset.UtcNow>deadline)throw new TimeoutException("Index did not start");await Task.Delay(20);}}
        async Task Retained(Backend backend,Workspace ws){var hits=await backend.Search("","lexical",new(){ws.Roots[0].Path},20,new Dictionary<string,bool>{{ws.Roots[0].Path,true}});Check(hits.Any(h=>h.Name=="sentinel.md"),"Committed sentinel index lost");}
        async Task Execute()
        {
            await Test("idle pause retains server PID port and deadline and accepts searches",async()=>
            {
                var f=await Fixture("policy-idle");using var b=f.Backend;try{var before=(await Servers(f.Workspace)).Single();int inventory=b.InventoryReadCount;await b.Pause(.2);Check(b.InventoryReadCount==inventory,"Idle pause unnecessarily spawned process inventory");var after=(await Servers(f.Workspace)).Single();Check(before.Id==after.Id&&Native.ServerPort(before)==Native.ServerPort(after),"Idle pause restarted search");await Retained(b,f.Workspace);Check(f.Store.Load().PauseUntil==b.Prefs.PauseUntil,"Pause deadline lost");b.Continue();Check(b.Prefs.PauseUntil==null,"Continue did not cancel countdown");}finally{await b.StopAll();}
            });
            await Test("active separate sync interruption preserves server and does not report failure",async()=>
            {
                var f=await Fixture("policy-active");using var b=f.Backend;var release=new TaskCompletionSource();try{var before=(await Servers(f.Workspace)).Single();var task=b.RunSync(f.Workspace,true);await Active(b,task);int index=b.ActiveIndexProcessId!.Value;var entered=new TaskCompletionSource();b.RequestBarrier=command=>{entered.TrySetResult();return release.Task;};var search=b.Search("","lexical",new(){f.Workspace.Roots[0].Path},20,new Dictionary<string,bool>{{f.Workspace.Roots[0].Path,true}});await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await b.Pause(.2);await task.WaitAsync(TimeSpan.FromSeconds(10));Check(!search.IsCompleted,"Separate pause waited for simulated slow request");release.TrySetResult();b.RequestBarrier=null;await search;Check(!Native.Alive(new(index,0,exe,"")),"Index still running");Check((await Servers(f.Workspace)).Single().Id==before.Id,"Search server was restarted");Check(!b.Prefs.LastFinished.ContainsKey(f.Workspace.Name),"Interrupted run recorded completed");await Retained(b,f.Workspace);}finally{release.TrySetResult();await b.StopAll();}
            });
            await Test("battery suboption stops only automatic sync, retains due work and search",async()=>
            {
                var f=await Fixture("policy-battery");using var b=f.Backend;try{var server=(await Servers(f.Workspace)).Single();var task=b.RunSync(f.Workspace);await Active(b,task);b.PowerSource=()=>false;await b.Pulse();await task.WaitAsync(TimeSpan.FromSeconds(10));Check((await Servers(f.Workspace)).Single().Id==server.Id,"Battery restarted separate search");Check(b.Prefs.PauseUntil==null,"Battery created a timer pause");await Retained(b,f.Workspace);var manual=b.RunSync(f.Workspace,true);await Active(b,manual);int pid=b.ActiveIndexProcessId!.Value;await b.Pulse();Check(Native.Alive(new(pid,0,exe,"")),"Battery interrupted manual index");await b.Pause(.1);await manual.WaitAsync(TimeSpan.FromSeconds(10));b.Continue();await b.Pulse();Check(b.ActiveIndexProcessId==null,"Continue bypassed battery gate");}finally{await b.StopAll();}
            });
            await Test("disabled stop option and unknown power do not interrupt running automatic work",async()=>
            {
                var f=await Fixture("policy-permission");using var b=f.Backend;try{b.Prefs.BatteryStopsAuto=false;var task=b.RunSync(f.Workspace);await Active(b,task);int pid=b.ActiveIndexProcessId!.Value;b.PowerSource=()=>false;await b.Pulse();Check(Native.Alive(new(pid,0,exe,"")),"First battery option interrupted active run");b.Prefs.BatteryStopsAuto=true;b.PowerSource=()=>null;await b.Pulse();Check(Native.Alive(new(pid,0,exe,"")),"Unknown power interrupted work");await b.Pause(.1);await task.WaitAsync(TimeSpan.FromSeconds(10));}finally{await b.StopAll();}
            });
            await Test("paused cold start and sleep resume retain timer on every power state",async()=>
            {
                var f=await Fixture("policy-cold",true);using var b=f.Backend;try{var deadline=b.Prefs.PauseUntil;foreach(bool? ac in new bool?[]{true,false,null}){b.PowerSource=()=>ac;b.Resumed();await b.Pulse();Check(b.ActiveIndexProcessId==null&&b.Prefs.PauseUntil==deadline,"Resume bypassed pause");}await Retained(b,f.Workspace);}finally{await b.StopAll();}
            });
            await Test("session stop blocks every index entry and tray resume clears both pauses",async()=>
            {
                var f=await Fixture("policy-session");using var b=f.Backend;try
                {
                    var server=(await Servers(f.Workspace)).Single();var task=b.RunSync(f.Workspace,true);await Active(b,task);await b.StopSession();await task.WaitAsync(TimeSpan.FromSeconds(10));Check(b.SessionStopped&&b.Paused,"Session gate missing");
                    await b.Pause(.2);b.Prefs.PeriodicSync=true;b.QueueAll();foreach(bool? ac in new bool?[]{true,false,null}){b.PowerSource=()=>ac;b.Resumed();await b.Pulse();Check(b.ActiveIndexProcessId==null,"Policy event bypassed session gate");}
                    bool rejected=false;try{await b.RunSync(f.Workspace,true);}catch(IOException){rejected=true;}Check(rejected,"Manual sync bypassed stop");Check((await Servers(f.Workspace)).Single().Id==server.Id,"Separate session stop restarted search");await Retained(b,f.Workspace);
                    b.PowerSource=()=>false;b.Continue();Check(!b.CanResume&&b.Prefs.PauseUntil==null,"Resume did not clear both pauses");await b.Pulse();Check(b.ActiveIndexProcessId==null,"Resume bypassed battery policy");
                }finally{await b.StopAll();}
            });
            await Test("cold launch clears only session stop and retains persisted timer",async()=>
            {
                var f=await Fixture("policy-session-cold");using var b=f.Backend;await b.StopSession();await b.Pause(.3);var deadline=b.Prefs.PauseUntil;await b.StopAll();using var next=new Backend(f.Store,f.Store.Load());next.PowerSource=()=>true;try{await next.Initialize(false);Check(!next.SessionStopped&&next.TimedPaused&&next.Prefs.PauseUntil==deadline,"Cold start session or timer semantics wrong");Check(next.ActiveIndexProcessId==null,"Cold start bypassed timer");}finally{await next.StopAll();}
            });
            await Test("saving an active root edit stops sync preserves index and honors new battery policy",async()=>
            {
                var f=await Fixture("policy-edit-active");using var b=f.Backend;try
                {
                    var task=b.RunSync(f.Workspace,true);await Active(b,task);int old=b.ActiveIndexProcessId!.Value;
                    var changed=JsonSerializer.Deserialize<Workspace>(JsonSerializer.Serialize(b.Workspaces.Single()))!;changed.Roots[0].Exclude.Add("ignored/**");var prefs=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(b.Prefs))!;b.PowerSource=()=>false;
                    await b.SaveConfiguration(new(){changed},prefs);await task.WaitAsync(TimeSpan.FromSeconds(10));Check(!Native.Alive(new(old,0,exe,"")),"Old index survived edit");Check(b.Workspaces.Single().Roots[0].Exclude.Contains("ignored/**"),"Saved roots not applied");await b.Pulse();Check(b.ActiveIndexProcessId==null,"Save bypassed power gate");await Retained(b,b.Workspaces.Single());Check(!File.Exists(System.IO.Path.Combine(f.Store.DataDirectory,"pending-settings.json")),"Completed edit journal retained");
                    b.PowerSource=()=>true;await b.Pulse();var until=DateTimeOffset.UtcNow.AddSeconds(20);while(b.ActiveIndexProcessId==null&&DateTimeOffset.UtcNow<until)await Task.Delay(20);Check(b.ActiveIndexProcessId!=null,"Interrupted normal update was not queued to continue");await b.StopSession();
                }finally{await b.StopAll();}
            });
            await Test("saving an unrelated workspace does not stop the running index",async()=>
            {
                var f=await Fixture("policy-edit-other");using var b=f.Backend;try
                {
                    string docs=System.IO.Path.Combine(root,"other-docs");Directory.CreateDirectory(docs);var other=new Workspace{Name="other-range",ConfigPath=System.IO.Path.Combine(f.Store.ConfigDirectory,"other-range.yml"),Roots=new(){new(){Name="other",Path=docs}}};f.Store.SaveWorkspace(other);b.Reload();
                    var task=b.RunSync(b.Workspaces.Single(w=>w.Name==f.Workspace.Name),true);await Active(b,task);int old=b.ActiveIndexProcessId!.Value;other.Roots[0].Description="edited unrelated range";
                    await b.SaveConfiguration(new(){other},JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(b.Prefs))!);Check(Native.Alive(new(old,0,exe,"")),"Unrelated index was stopped");await b.StopSession();await task.WaitAsync(TimeSpan.FromSeconds(10));
                }finally{await b.StopAll();}
            });
            await Test("idle save never starts indexing and removing the final root retains database",async()=>
            {
                var f=await Fixture("policy-edit-empty");using var b=f.Backend;try
                {
                    var changed=JsonSerializer.Deserialize<Workspace>(JsonSerializer.Serialize(b.Workspaces.Single()))!;changed.Roots.Clear();string db=System.IO.Path.Combine(f.Store.StateDirectory,f.Workspace.Name+".sqlite");long size=new FileInfo(db).Length;
                    await b.SaveConfiguration(new(){changed},JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(b.Prefs))!);await b.Pulse();Check(b.ActiveIndexProcessId==null,"Idle save launched index");Check(File.Exists(db)&&new FileInfo(db).Length>=size,"Removing final root cleared database");Check(b.Workspaces.Single().Roots.Count==0,"Final root not removed");
                }finally{await b.StopAll();}
            });
            await Test("pending saved configuration replays before cold startup and rejects external conflicts",async()=>
            {
                var f=await Fixture("policy-journal");using var b=f.Backend;await b.StopAll();
                var changed=JsonSerializer.Deserialize<Workspace>(JsonSerializer.Serialize(f.Workspace))!;changed.Roots[0].Description="persisted before stop";var prefs=f.Store.Load();prefs.PauseUntil=DateTimeOffset.UtcNow.AddHours(1);var path=System.IO.Path.Combine(f.Store.DataDirectory,"pending-settings.json");Store.Atomic(path,JsonSerializer.Serialize(new Backend.PendingSettings{Workspaces=new(){changed},Preferences=prefs}));
                using(var next=new Backend(f.Store,f.Store.Load())){next.PowerSource=()=>true;try{await next.Initialize(false);Check(next.TimedPaused&&next.Workspaces.Single().Roots[0].Description=="persisted before stop","Pending edit or policy lost");Check(!File.Exists(path)&&next.ActiveIndexProcessId==null,"Pending replay started index");}finally{await next.StopAll();}}
                changed=f.Store.ReadWorkspaces().Single();changed.Roots[0].Description="conflicting pending";Store.Atomic(path,JsonSerializer.Serialize(new Backend.PendingSettings{Workspaces=new(){changed},Preferences=prefs}));File.AppendAllText(changed.ConfigPath,"\n# external modification\n");var hash=Store.Digest(changed.ConfigPath);
                using var conflict=new Backend(f.Store,f.Store.Load());bool denied=false;try{await conflict.Initialize(false);}catch(IOException){denied=true;}finally{await conflict.StopAll();}Check(denied&&Store.Digest(changed.ConfigPath)==hash&&File.Exists(path),"External conflict was overwritten");
            });
            await Test("legacy shared auto-sync waits for an active request then restarts only its workspace",async()=>
            {
                var f=await Fixture("policy-shared");using var b=f.Backend;try
                {
                    // The real native auto-sync service is paused before models finish downloading.
                    var old=(await Servers(f.Workspace)).Single();Native.Stop(old);
                    var legacy=Start(f.Store,f.Workspace.Name,"server","--port="+Native.ServerPort(old),"--auto-sync");
                    var deadline=DateTimeOffset.UtcNow.AddSeconds(25);while(DateTimeOffset.UtcNow<deadline){if(legacy.HasExited)throw new Exception("Legacy auto-sync exited");var p=new Native.Proc(legacy.Id,0,exe,"",legacy.StartTime.ToUniversalTime().Ticks,new[]{"--workspace",f.Workspace.Name,"server","--port="+Native.ServerPort(old),"--auto-sync"});if(await BackendInstances.Status(p)!=null)break;await Task.Delay(100);}
                    await b.RecheckInstances();var entered=new TaskCompletionSource();var release=new TaskCompletionSource();b.RequestBarrier=command=>{entered.TrySetResult();return release.Task;};var search=b.Search("","lexical",new(){f.Workspace.Roots[0].Path},20,new Dictionary<string,bool>{{f.Workspace.Roots[0].Path,true}});await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));var pause=b.Pause(.1);
                    try{var waiting=DateTimeOffset.UtcNow.AddSeconds(20);while(!b.WaitingForSearch&&!pause.IsCompleted&&DateTimeOffset.UtcNow<waiting)await Task.Delay(20);Check(b.WaitingForSearch&&!legacy.HasExited,"Shared service did not wait for active request");}finally{b.RequestBarrier=null;release.TrySetResult();}
                    await Task.WhenAll(search,pause).WaitAsync(TimeSpan.FromSeconds(30));Check(legacy.HasExited,"Shared process not restarted");var server=(await Servers(f.Workspace)).Single();Check(server.Id!=legacy.Id&&!Native.AutoSync(server),"Replacement still auto-syncs");await Retained(b,f.Workspace);
                }finally{await b.StopAll();}
            });
        }
    }
}
