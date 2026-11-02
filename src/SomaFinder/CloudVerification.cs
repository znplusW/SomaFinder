// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace SomaFinder;
internal static class CloudVerification
{
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        var seen=new HashSet<DependencyObject>();var stack=new Stack<DependencyObject>();stack.Push(root);
        while(stack.Count>0){var item=stack.Pop();if(!seen.Add(item))continue;if(item is T match)yield return match;foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())stack.Push(child);if(item is Visual)for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)stack.Push(VisualTreeHelper.GetChild(item,i));}
    }
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static int Run(string[] args)
    {
        string Arg(string name)=>args.First(x=>x.StartsWith(name+"="))[(name.Length+1)..];
        CloudSandbox.Initialize(Arg("--cloud-root"));Environment.SetEnvironmentVariable("SOMAFINDER_TEST","1");
        var suite=new Suite(CloudSandbox.Root,Arg("--soma"),Arg("--disk-script"),System.IO.Path.GetFullPath(Arg("--report")));
        return suite.Run();
    }
    sealed class Suite
    {
        readonly string root,exe,diskScript,report,reportDir;
        readonly List<object> records=new();int failures;int cliNumber;bool rebuilt;
        readonly Store store;readonly FinderApplication app;readonly Workspace alpha,beta;
        public Suite(string root,string exe,string diskScript,string report)
        {
            this.root=root;this.exe=exe;this.diskScript=diskScript;this.report=report;reportDir=System.IO.Path.GetDirectoryName(report)!;Directory.CreateDirectory(reportDir);
            store=new Store(System.IO.Path.Combine(root,"app/data"),System.IO.Path.Combine(root,"app/config/soma"),System.IO.Path.Combine(root,"app/state/soma"));
            alpha=Workspace("cloud-alpha","alpha");beta=Workspace("cloud-beta","beta");
            File.WriteAllText(System.IO.Path.Combine(alpha.Roots[0].Path,"battery.md"),"Battery endurance report. 笔记本电池续航测试，办公使用十二小时。此文件用于云端测试。");
            File.WriteAllText(System.IO.Path.Combine(alpha.Roots[0].Path,"obsolete.md"),"obsolete sentinel content");File.WriteAllText(System.IO.Path.Combine(beta.Roots[0].Path,"sentinel.md"),"sentinel beta must survive rebuilding alpha");
            store.SaveWorkspace(alpha);store.SaveWorkspace(beta);
            var prefs=new Preferences{SomaExe=exe,StartupSync=false,PeriodicSync=false,BatteryBlocksAuto=false,LowMemoryBlocksAuto=false,LowerPriority=true};
            app=new FinderApplication(store,prefs,true);
        }
        Workspace Workspace(string name,string child)
        {
            var docs=System.IO.Path.Combine(root,"documents",child);Directory.CreateDirectory(docs);
            return new(){Name=name,ConfigPath=System.IO.Path.Combine(root,"app/config/soma",name+".yml"),Roots=new(){new(){Name=child,Path=docs}}};
        }
        public int Run()
        {
            app.Startup+=async(_,_)=>
            {
                try{await Execute();}catch(Exception ex){failures++;records.Add(new{name="suite initialization",status="failed",error=ex.ToString()});}
                finally
                {
                    try{await app.Backend.StopAll();}catch(Exception ex){failures++;records.Add(new{name="backend cleanup",status="failed",error=ex.ToString()});}
                    if(CloudSandbox.Errors.Count>0){failures++;records.Add(new{name="unexpected UI error dialogs",status="failed",errors=CloudSandbox.Errors.ToArray()});}
                    foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();
                    Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),version=typeof(Program).Assembly.GetName().Version?.ToString(),date=DateTimeOffset.UtcNow,failures,tests=records,hardware="Virtual disks are real OS VHDX mount events; geometry fuzz is simulated; no physical USB, real multi-monitor hotplug, or human input acceptance claimed"},new JsonSerializerOptions{WriteIndented=true}));
                    app.Shutdown(failures==0?0:1);
                }
            };
            return app.Run();
        }
        async Task Test(string name,Func<Task> action,string kind="integration")
        {
            var started=DateTimeOffset.UtcNow;
            Console.WriteLine("START "+name);
            try{await action();records.Add(new{name,status="passed",kind,seconds=(DateTimeOffset.UtcNow-started).TotalSeconds});Console.WriteLine("PASS "+name);}
            catch(Exception ex){failures++;records.Add(new{name,status="failed",kind,error=ex.ToString()});Console.WriteLine("FAIL "+name+" "+ex.Message);}
            Store.Atomic(System.IO.Path.Combine(reportDir,"cloud-progress.json"),JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests=records},new JsonSerializerOptions{WriteIndented=true}));
        }
        async Task Cli(Store target,string workspace,params string[] args)
        {
            var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("--workspace");start.ArgumentList.Add(workspace);foreach(var arg in args)start.ArgumentList.Add(arg);
            start.Environment["XDG_CONFIG_HOME"]=System.IO.Path.GetDirectoryName(target.ConfigDirectory)!;start.Environment["XDG_STATE_HOME"]=System.IO.Path.GetDirectoryName(target.StateDirectory)!;
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));}catch{process.Kill(true);throw;}
            await File.WriteAllTextAsync(System.IO.Path.Combine(reportDir,"cli-"+(++cliNumber)+".log"),(await output)+"\n"+(await errors));Check(process.ExitCode==0,"Soma CLI failed: "+string.Join(" ",args));
        }
        async Task<int> Count(Backend backend,string name)
        {
            var data=await backend.Rpc(backend.Workspaces.Single(w=>w.Name==name),"status");return data.GetProperty("projects").EnumerateArray().Sum(p=>p.GetProperty("stats").GetProperty("documents").GetInt32());
        }
        async Task Disk(string operation,string device="A")
        {
            var start=new ProcessStartInfo("pwsh.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var value in new[]{"-NoProfile","-File",diskScript,"-Operation",operation,"-Root",root,"-Device",device})start.ArgumentList.Add(value);
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));var details=(await output)+"\n"+(await error);Check(process.ExitCode==0,"Disk operation failed: "+operation+" "+details);
        }
        static Dictionary<string,bool> Scope(Workspace workspace)=>workspace.Roots.ToDictionary(x=>x.Path,_=>true,StringComparer.OrdinalIgnoreCase);
        async Task WaitIdle(Backend backend,TimeSpan timeout)
        {
            var deadline=DateTimeOffset.UtcNow+timeout;while(backend.Busy){if(DateTimeOffset.UtcNow>deadline)throw new TimeoutException("Index task exceeded timeout");await Task.Delay(250);}await Task.Delay(100);
        }
        async Task Execute()
        {
            await Test("real Soma scan of two isolated workspaces",async()=>{await Cli(store,alpha.Name,"system","scan");await Cli(store,beta.Name,"system","scan");await app.Backend.Initialize();Check(await Count(app.Backend,alpha.Name)==2&&await Count(app.Backend,beta.Name)==1,"Fixture scan counts");});
            await Test("official download filename is recognized by process management",async()=>
            {
                Check(System.IO.Path.GetFileName(exe)=="soma-windows-x64.exe","Official filename fixture was renamed");
                var processes=await Native.SomaProcesses(exe);Check(processes.Count(p=>Native.IsServer(p)&&new[]{alpha.Name,beta.Name}.Contains(Native.WorkspaceName(p)))==2,"Running servers were not identified for the official download name");
            });
            await Test("cloud destructive gate rejects paths outside disposable fixture",async()=>
            {
                var bad=new Workspace{Name=alpha.Name,ConfigPath=alpha.ConfigPath,Roots=new(){new(){Name="unsafe",Path=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}}};
                Check(!CloudSandbox.AllowsRebuild(store,bad),"Unsafe root allowed");try{await app.Backend.RunSync(bad,true,true);throw new Exception("Guard accepted unsafe root");}catch(InvalidOperationException){}
            },"safety");
            await Test("actual rebuild button - cancel leaves index unchanged",async()=>{await ClickRebuild(false);Check(await Count(app.Backend,alpha.Name)==2,"Cancel changed alpha");Check(await Count(app.Backend,beta.Name)==1,"Cancel changed beta");},"automated UI routed button and modal");
            await Test("actual rebuild button - confirmation rebuilds selected workspace only",async()=>
            {
                File.Delete(System.IO.Path.Combine(alpha.Roots[0].Path,"obsolete.md"));File.WriteAllText(System.IO.Path.Combine(alpha.Roots[0].Path,"fresh.md"),"fresh replacement sentinel");
                await ClickRebuild(true);await WaitIdle(app.Backend,TimeSpan.FromMinutes(22));Check(CloudSandbox.Errors.Count==0,string.Join("\n",CloudSandbox.Errors));
                var a=app.Backend.Workspaces.Single(w=>w.Name==alpha.Name);var b=app.Backend.Workspaces.Single(w=>w.Name==beta.Name);
                Check(await Count(app.Backend,alpha.Name)==2&&await Count(app.Backend,beta.Name)==1,"Selected workspace boundary");
                var files=await app.Backend.Search("","lexical",Selection.Positive(Scope(a)),20,Scope(a));Check(files.Any(h=>h.Name=="fresh.md")&&!files.Any(h=>h.Name=="obsolete.md"),"Full rebuild did not replace obsolete record");
                var sentinel=await app.Backend.Search("sentinel","lexical",Selection.Positive(Scope(b)),5,Scope(b));Check(sentinel.Count==1,"Other workspace sentinel lost");
                var vectors=await app.Backend.Search("笔记本电池可以使用多久","vector",Selection.Positive(Scope(a)),2,Scope(a));Check(vectors.Any(h=>h.Name=="battery.md"),"Semantic index missing after rebuild");
                rebuilt=true;
            },"real backend destructive rebuild through UI");
            await Test("search while pausing drains current query and retains deadline",async()=>
            {
                Check(rebuilt,"Prerequisite: confirmed rebuild and semantic query must succeed before this case");
                var a=app.Backend.Workspaces.Single(w=>w.Name==alpha.Name);var scope=Scope(a);var search=app.Backend.Search("查找有关电池续航测试的文档","hybrid",Selection.Positive(scope),2,scope);var pause=app.Backend.Pause(.1);
                await Task.WhenAll(search,pause).WaitAsync(TimeSpan.FromMinutes(5));Check((await search).Any(h=>h.Name=="battery.md"),"Active hybrid query was lost");Check(store.Load().IsPaused(DateTimeOffset.UtcNow),"Deadline not persisted");app.Backend.Continue();
            });
            await Test("real API results switch list views without search or body requests",async()=>
            {
                var a=app.Backend.Workspaces.Single(w=>w.Name==alpha.Name);var scope=Scope(a);var hits=await app.Backend.Search("battery","lexical",Selection.Positive(scope),20,scope);Check(hits.Count>0,"Real query returned no fixtures");
                var window=new SearchWindow(app,scope);window.SeedResults(hits,scope);window.VerificationSelect(0);var selected=window.SelectedHit;long before=app.Backend.RequestCount;
                for(int i=0;i<100;i++){window.SetView(i%2==0);await app.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Check(ReferenceEquals(window.SelectedHit,selected),"View lost selection");}
                Check(app.Backend.RequestCount==before,"Switching actual API result views made requests");window.Close();
            });
            await Test("virtual disk unplug, reconnect and exchanged drive letters",DiskTests,"real OS VHDX lifecycle");
            await Test("multi-monitor geometry fuzz: negative origins, removal, small work areas",()=>
            {
                var random=new Random(1701);for(int n=0;n<5000;n++)
                {
                    var areas=n%2==0?new[]{new Rect(0,0,1920,1040),new Rect(-2560,-400,2560,1440)}:new[]{new Rect(0,0,1280,720)};
                    var desired=new Rect(random.Next(-12000,12000),random.Next(-8000,8000),random.Next(320,5000),random.Next(240,3000));var result=WindowPlacement.Recover(desired,areas);
                    Check(areas.Any(a=>{var visible=Rect.Intersect(a,result);return !visible.IsEmpty&&visible.Width>=100&&visible.Height>=60;}),"Window lost across monitor layout");
                }
                return Task.CompletedTask;
            },"simulated monitor geometry");
            records.Add(new{name="physical USB and real multi-monitor hotplug",status="not_available",kind="hardware boundary",actualScreens=Forms.Screen.AllScreens.Select(s=>new{s.DeviceName,s.Primary,s.Bounds}).ToArray(),reason="Hosted VM has no controllable USB hardware or physical display connectors. This is not a passed hardware test."});
            await Test("window and pane lifecycle pressure with resource bounds",Stress,"bounded randomized UI stress");
            await Test("unexpected UI dialogs are reported as failures",()=>{Check(CloudSandbox.Errors.Count==0,string.Join("\n",CloudSandbox.Errors));return Task.CompletedTask;});
        }
        async Task ClickRebuild(bool accept)
        {
            var settings=new SettingsWindow(app);settings.Show();settings.SelectCloudWorkspace(alpha.Name);var tabs=Descendants<TabControl>(settings).Single();tabs.SelectedItem=tabs.Items.OfType<TabItem>().Single(t=>(string?)t.Header=="索引维护");settings.UpdateLayout();
            var button=Descendants<Button>(settings).Single(b=>AutomationProperties.GetAutomationId(b)=="SomaFinder.RebuildWorkspace");Check(button.IsEnabled,"Rebuild button unexpectedly disabled");bool answered=false;Exception? confirmError=null;
            _=app.Dispatcher.BeginInvoke(()=>
            {
                try
                {
                    var modal=app.Windows.OfType<Window>().Single(w=>w.Title=="重建所选范围索引");var text=string.Join("\n",Descendants<TextBlock>(modal).Select(t=>t.Text));
                    Store.Atomic(System.IO.Path.Combine(reportDir,accept?"confirm-rebuild.txt":"cancel-rebuild.txt"),text);
                    var normalized=text.Replace('/','\\');Check(normalized.Contains(Rules.Normalize(alpha.Roots[0].Path),StringComparison.OrdinalIgnoreCase)&&!normalized.Contains(Rules.Normalize(beta.Roots[0].Path),StringComparison.OrdinalIgnoreCase),"Confirmation shows wrong real root range: "+text);
                    var target=Descendants<Button>(modal).Single(b=>(string?)b.Content==(accept?"确认重建":"取消"));target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));answered=true;
                }
                catch(Exception ex){confirmError=ex;foreach(var modal in app.Windows.OfType<Window>().Where(w=>w.Title=="重建所选范围索引").ToArray())modal.Close();}
            },DispatcherPriority.ApplicationIdle);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);settings.Close();if(confirmError!=null)throw confirmError;Check(answered,"Confirmation was not reached");
        }
        async Task DiskTests()
        {
            var manifest=JsonDocument.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(root,"disks.json"))).RootElement;
            var docsA=System.IO.Path.Combine(manifest.GetProperty("ACurrent").GetString()+":\\","docs");var docsB=System.IO.Path.Combine(manifest.GetProperty("BCurrent").GetString()+":\\","docs");Directory.CreateDirectory(docsA);Directory.CreateDirectory(docsB);File.WriteAllText(System.IO.Path.Combine(docsA,"disk-a.md"),"correct-volume-a");File.WriteAllText(System.IO.Path.Combine(docsB,"disk-b.md"),"correct-volume-b");
            var ds=new Store(System.IO.Path.Combine(root,"disks-app/data"),System.IO.Path.Combine(root,"disks-app/config/soma"),System.IO.Path.Combine(root,"disks-app/state/soma"));
            var a=new Workspace{Name="disk-a",ConfigPath=System.IO.Path.Combine(ds.ConfigDirectory,"disk-a.yml"),Roots=new(){new(){Name="root-a",Path=docsA}}};var b=new Workspace{Name="disk-b",ConfigPath=System.IO.Path.Combine(ds.ConfigDirectory,"disk-b.yml"),Roots=new(){new(){Name="root-b",Path=docsB}}};ds.SaveWorkspace(a);ds.SaveWorkspace(b);await Cli(ds,a.Name,"system","scan");await Cli(ds,b.Name,"system","scan");
            using var backend=new Backend(ds,new Preferences{SomaExe=exe,StartupSync=false,PeriodicSync=false,BatteryBlocksAuto=false,LowMemoryBlocksAuto=false});
            try
            {
                await backend.Initialize();var identityA=Native.Identify(docsA)!;var identityB=Native.Identify(docsB)!;Check(identityA.Volume!=identityB.Volume&&identityA.PhysicalDisk!=identityB.PhysicalDisk,"Independent VHD identities missing");
                await Disk("Detach","B");await backend.Pulse();Check(!Native.Verified(docsB,backend.Prefs),"Offline path still verified");Check(await Count(backend,a.Name)==1&&await Count(backend,b.Name)==1,"Offline event destroyed indexed records");
                try{await backend.RunSync(backend.Workspaces.Single(w=>w.Name==b.Name),true);throw new Exception("Offline sync was accepted");}catch(IOException){}
                await Disk("Attach","B");await Disk("Swap");await backend.Pulse();
                var actualA=backend.Workspaces.Single(w=>w.Name==a.Name).Roots[0].Path;var actualB=backend.Workspaces.Single(w=>w.Name==b.Name).Roots[0].Path;
                Check(Native.Identify(actualA)?.Volume==identityA.Volume&&Native.Identify(actualB)?.Volume==identityB.Volume,"Drive-letter swap confused volume identity");
                Check(File.ReadAllText(System.IO.Path.Combine(actualA,"disk-a.md"))=="correct-volume-a"&&File.ReadAllText(System.IO.Path.Combine(actualB,"disk-b.md"))=="correct-volume-b","Wrong disk contents selected");
                for(int i=0;i<3;i++){await Disk("Detach","B");await backend.Pulse();Check(!Native.Verified(actualB,backend.Prefs),"Replug cycle offline identity");await Disk("Attach","B");await backend.Pulse();Check(Native.Verified(actualB,backend.Prefs),"Replug cycle restore identity");}
                var content=string.Join("\n",Enumerable.Range(0,80).Select(n=>"Battery endurance cloud fixture paragraph "+n+" with distinct semantic text for an active indexing interruption test."));
                for(int n=0;n<200;n++)File.WriteAllText(System.IO.Path.Combine(actualA,"active-"+n+".md"),content+"\nDocument "+n);
                var indexing=backend.RunSync(backend.Workspaces.Single(w=>w.Name==a.Name),true);var deadline=DateTimeOffset.UtcNow.AddSeconds(30);while(backend.ActiveIndexProcessId==null&&!indexing.IsCompleted&&DateTimeOffset.UtcNow<deadline)await Task.Delay(20);Check(backend.ActiveIndexProcessId!=null,"No active index process to interrupt");await Disk("Detach","A");await backend.Pulse();try{await indexing;throw new Exception("Unplugged indexing task unexpectedly completed");}catch(IOException){}
                Check(await Count(backend,b.Name)==1,"Interrupting A affected B");Check(File.Exists(System.IO.Path.Combine(ds.StateDirectory,a.Name+".sqlite")),"Index database removed on unplug");await Disk("Attach","A");await backend.Pulse();
            }
            finally{await backend.StopAll();}
        }
        async Task Stress()
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();using var process=Process.GetCurrentProcess();process.Refresh();var handles=process.HandleCount;var memory=process.PrivateMemorySize64;var random=new Random(401);var titles=new SearchTitles();
            for(int i=0;i<150;i++)
            {
                app.Backend.Prefs.DefaultDetailLayout=(DetailLayout)(i%3);var w=new SearchWindow(app,new Dictionary<string,bool>());w.Show();w.SeedVerification();var d=w.VerificationDetails;
                for(int j=0;j<12;j++){if(random.Next(2)==0)d.Swap();else d.ToggleRight();titles.Submit(w,"query-"+random.Next(8));w.UpdateSearchTitle(titles.Title(w));}
                d.Float(DetailKind.Summary);d.Float(DetailKind.Preview);var preview=d.FloatingWindow(DetailKind.Preview)!;preview.Left=100000;preview.Top=100000;DetailPanels.EnsureOnScreen(preview);await Task.Delay(5);Check(preview.Left<100000&&preview.Top<100000,"Detached window lost outside desktop");
                if(i%2==0){d.Dock(DetailKind.Summary,false);d.Swap();}w.Close();await Task.Delay(5);Check(d.IsDisposed&&d.DetachedCount==0,"Floating window leaked");titles.Remove(w);
                if(i%15==0)await app.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            }
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(100);process.Refresh();var handleGrowth=process.HandleCount-handles;var memoryGrowth=process.PrivateMemorySize64-memory;
            records.Add(new{name="stress resource measurements",status="observed",windows=150,layoutOperations=1800,floatingWindows=300,handleGrowth,memoryGrowthBytes=memoryGrowth});Check(handleGrowth<250,"Unbounded handle growth after close cycles");Check(memoryGrowth<350L*1024*1024,"Excessive private-memory growth after close cycles");
        }
    }
}
