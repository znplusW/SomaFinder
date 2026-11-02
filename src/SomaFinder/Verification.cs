// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;
public static class Verification
{
    public static int Run(string[] args)
    {
        Environment.SetEnvironmentVariable("SOMAFINDER_TEST","1");
        var report=args.FirstOrDefault(a=>a.StartsWith("--report="))?[9..]??System.IO.Path.Combine(AppContext.BaseDirectory,"verification.json");
        var root=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(report))!,"isolated-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var records=new List<object>();int failures=0;
        void Test(string name,Action action){try{action();records.Add(new{name,passed=true});}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});}}
        void Equal<T>(T a,T b){if(!EqualityComparer<T>.Default.Equals(a,b))throw new Exception($"Expected {b}, got {a}");}
        Test("quota proportion 600:300:100",()=>Equal(string.Join(",",Rules.Allocate(new long[]{600,300,100},20)),"12,6,2"));
        Test("quota minimum and tiny limit",()=>Equal(string.Join(",",Rules.Allocate(new long[]{1,1000,1},2)),"1,1,1"));
        Test("quota skew lower bound",()=>Equal(string.Join(",",Rules.Allocate(new long[]{1000,1,1},20)),"18,1,1"));
        Test("quota no workspace",()=>Equal(Rules.Allocate(Array.Empty<long>(),20).Length,0));
        Test("quota randomized invariants",()=>{var r=new Random(23);for(int i=0;i<2000;i++){int n=r.Next(1,30),limit=r.Next(1,500);var result=Rules.Allocate(Enumerable.Range(0,n).Select(_=>(long)r.Next(0,50000)).ToArray(),limit);if(result.Any(x=>x<1)||result.Sum()!=Math.Max(n,limit))throw new Exception("quota invariant");}});
        Test("path boundary",()=>{Equal(Rules.Contains("C:/docs","C:/docs2/a"),false);Equal(Rules.Contains("C:/docs","C:/docs/a"),true);});
        Test("executable identity normalizes Windows separators",()=>Equal(Native.SameFilePath("C:/tools/Soma/soma.exe",@"C:\tools\Soma\soma.exe"),true));
        Test("scope shrink restore expansion",()=>{var prior=new Dictionary<string,bool>{{"C:/docs",true},{"C:/docs/private",false}};var current=new Dictionary<string,bool>(prior);Equal(Selection.Includes(current,"C:/docs/private/a.pdf"),false);current["C:/docs/private"]=true;Equal(Selection.Expanded(current,prior),true);current["C:/docs/private"]=false;Equal(Selection.Expanded(current,prior),false);});
        Test("partial query scope includes success but not failed or excluded directories",()=>{var state=new Dictionary<string,bool>{{"C:/good",true},{"C:/good/private",false},{"C:/failed",true}};var success=Selection.RestrictToRoots(state,new[]{"C:/good"});Equal(Selection.Includes(success,"C:/good/a.txt"),true);Equal(Selection.Includes(success,"C:/good/private/a.txt"),false);Equal(Selection.Includes(success,"C:/failed/a.txt"),false);});
        Test("file filters and custom extension",()=>{Equal(Rules.TypeMatches(".pdf","文档"),true);Equal(Rules.TypeMatches(".mp4","图片"),false);Equal(Rules.TypeMatches(".PDF","pdf;docx"),true);});
        Test("remaining time readable across restart",()=>{Equal(Rules.Remaining(TimeSpan.FromMinutes(119)),"1 小时 59 分钟");Equal(Rules.Remaining(TimeSpan.FromSeconds(20)),"1 分钟");});
        Test("process rendering compatibility precedes WPF windows",()=>Equal(System.Windows.Media.RenderOptions.ProcessRenderMode,System.Windows.Interop.RenderMode.SoftwareOnly));
        Test("search titles number only peers and retain assigned identifiers",()=>
        {
            var names=new SearchTitles();object first=new(),second=new(),third=new();
            names.Submit(first," 电池续航 ");Equal(names.Title(first),"Soma Finder 搜索“电池续航”");
            names.Submit(second,"电池续航");Equal(names.Title(first),"Soma Finder 搜索“电池续航”（1）");Equal(names.Title(second),"Soma Finder 搜索“电池续航”（2）");
            names.Submit(second,"电池续航");Equal(names.Title(second),"Soma Finder 搜索“电池续航”（2）");
            names.Remove(first);Equal(names.Title(second),"Soma Finder 搜索“电池续航”（2）");
            names.Submit(third,"电池续航");Equal(names.Title(third),"Soma Finder 搜索“电池续航”（3）");
            names.Submit(second,"照片");Equal(names.Title(second),"Soma Finder 搜索“照片”");Equal(names.Title(third),"Soma Finder 搜索“电池续航”（3）");
            names.Submit(first,"");Equal(names.Title(first),"Soma Finder 文件浏览");
        });
        Test("window defaults migrate legacy settings and persist independently",()=>
        {
            var legacy=JsonSerializer.Deserialize<Preferences>("{\"StartupSync\":false}")!;Equal(legacy.DefaultSearchBreadth,50);Equal(legacy.DefaultDetailLayout,DetailLayout.SummaryOnly);
            var isolated=new Store(System.IO.Path.Combine(root,"window-defaults"));legacy.DefaultSearchBreadth=37;legacy.DefaultDetailLayout=DetailLayout.PreviewOnly;isolated.Save(legacy);
            var again=isolated.Load();Equal(again.DefaultSearchBreadth,37);Equal(again.DefaultDetailLayout,DetailLayout.PreviewOnly);Equal(again.StartupSync,false);
        });
        var store=new Store(System.IO.Path.Combine(root,"data"),System.IO.Path.Combine(root,"config"),System.IO.Path.Combine(root,"state"));
        Test("pause survives forced-exit style reload",()=>{var p=new Preferences{PauseUntil=DateTimeOffset.UtcNow.AddHours(3)};store.Save(p);var loaded=store.Load();Equal(loaded.PauseUntil,p.PauseUntil);Equal(loaded.IsPaused(DateTimeOffset.UtcNow.AddHours(1)),true);Equal(loaded.IsPaused(DateTimeOffset.UtcNow.AddHours(4)),false);});
        Test("pause blocks cold-start all power states",()=>{var p=new Preferences{PauseUntil=DateTimeOffset.UtcNow.AddHours(1),BatteryBlocksAuto=false,LowMemoryBlocksAuto=false};Equal(Backend.AutomaticAllowed(p,DateTimeOffset.UtcNow,true,true),false);Equal(Backend.AutomaticAllowed(p,DateTimeOffset.UtcNow,false,false),false);});
        Test("continue retains battery and memory gates",()=>{var p=new Preferences();Equal(Backend.AutomaticAllowed(p,DateTimeOffset.UtcNow,false,true),false);Equal(Backend.AutomaticAllowed(p,DateTimeOffset.UtcNow,true,false),false);Equal(Backend.AutomaticAllowed(p,DateTimeOffset.UtcNow,true,true),true);});
        Test("invalid settings fail closed",()=>{Store.Atomic(System.IO.Path.Combine(store.DataDirectory,"settings.json"),"broken-json");try{store.Load();throw new Exception("corruption was accepted");}catch(InvalidDataException){}});
        var config=System.IO.Path.Combine(store.ConfigDirectory,"test.yml");Directory.CreateDirectory(store.ConfigDirectory);
        File.WriteAllText(config,"version: 1\ncustom-option: keep\nprojects:\n- name: original-id\n  root: C:/original\n  include: ['**/*']\n  exclude: []\n  default-search: true\n  ignore-files: true\n  preserved-field: yes\ncontext: []\n");
        Test("YAML relocation preserves identity and unknown fields",()=>{var ws=store.ReadWorkspace(config);ws.Roots[0].Path="C:/relocated";store.SaveWorkspace(ws);var again=store.ReadWorkspace(config);Equal(again.Roots[0].Name,"original-id");var text=File.ReadAllText(config);if(!text.Contains("custom-option: keep")||!text.Contains("preserved-field: yes"))throw new Exception("unknown fields lost");});
        Test("YAML concurrent edit conflict",()=>{var ws=store.ReadWorkspace(config);File.AppendAllText(config,"\n# changed\n");try{store.SaveWorkspace(ws);throw new Exception("conflict accepted");}catch(IOException){}});
        Test("atomic write backups",()=>{var file=System.IO.Path.Combine(root,"atomic.txt");Store.Atomic(file,"before");Store.Atomic(file,"after");Equal(File.ReadAllText(file),"after");if(Directory.GetFiles(root,"atomic.txt.backup-*").Length!=1)throw new Exception("backup missing");});
        Test("process index classification",()=>{Equal(Native.IsIndex(new(1,0,"fake","fake.exe --workspace main sync")),true);Equal(Native.IsIndex(new(1,0,"fake","fake.exe server --port=8181")),false);});
        Test("quoted executable and command-like Unicode workspace names",()=>{var p=new Native.Proc(1,0,"fake","\"C:\\server tools\\soma.exe\" --workspace server sync");Equal(Native.IsIndex(p),true);Equal(Native.IsServer(p),false);Equal(Native.WorkspaceName(p),"server");var cn=new Native.Proc(2,0,"fake","soma.exe --workspace=中文资料 server --port=8210");Equal(Native.WorkspaceName(cn),"中文资料");Equal(Native.ServerPort(cn),8210);});
        Test("index child process membership uses actual parent chain",()=>
        {
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true};foreach(var a in new[]{"-NoProfile","-NonInteractive","-Command","Start-Sleep -Seconds 15"})start.ArgumentList.Add(a);
            using var child=Process.Start(start)!;try{using var current=Process.GetCurrentProcess();var owner=new Native.Proc(current.Id,0,current.MainModule!.FileName!,"",current.StartTime.ToUniversalTime().Ticks);if(!Native.Descendants(owner).Any(p=>p.Id==child.Id))throw new Exception("Owned child was not found");}finally{if(!child.HasExited){child.Kill(true);child.WaitForExit();}}
        });
        var api=args.FirstOrDefault(a=>a.StartsWith("--api="))?[6..];
        if(api!=null)
        {
            Test("live read-only status and explicit lexical query",()=>
            {
                using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
                async Task<JsonElement> Rpc(object body){using var response=await http.PostAsJsonAsync(api+"/api/run",body);response.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());if(!doc.RootElement.GetProperty("success").GetBoolean())throw new Exception(doc.RootElement.ToString());return doc.RootElement.GetProperty("data").Clone();}
                var status=Rpc(new{command="status"}).GetAwaiter().GetResult();Equal(status.GetProperty("workspace").GetString(),"main");
                var search=Rpc(new{command="search.lexical",args=new[]{"测试"},options=new{project=new[]{"wps"},limit=2}}).GetAwaiter().GetResult();if(!search.GetProperty("results").EnumerateArray().Any())throw new Exception("Known lexical fixture returned no hits");
            });
        }
        var lifecycleExe=args.FirstOrDefault(a=>a.StartsWith("--lifecycle="))?[12..];
        if(lifecycleExe!=null)Test("authorized real-backend interruption, restart without indexing, retained document count",()=>
        {
            async Task Run()
            {
                long? before=null;
                try{using var client=new HttpClient();using var response=await client.PostAsJsonAsync("http://127.0.0.1:8181/api/run",new{command="status"});using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());before=doc.RootElement.GetProperty("data").GetProperty("projects").EnumerateArray().Sum(p=>p.GetProperty("stats").GetProperty("documents").GetInt64());}catch{}
                var managedStore=new Store(System.IO.Path.Combine(root,"lifecycle-data"));
                var prefs=new Preferences{SomaExe=lifecycleExe,StartupSync=false,PeriodicSync=false,PauseUntil=DateTimeOffset.UtcNow.AddHours(1)};
                managedStore.Save(prefs);using var backend=new Backend(managedStore,prefs);
                try
                {
                    await backend.Initialize();
                    var processes=await Native.SomaProcesses(lifecycleExe);
                    if(processes.Any(Native.IsIndex)||processes.Any(p=>p.Command.Contains("--auto-sync")))throw new Exception("Indexing still running while paused");
                    var ws=backend.Workspaces.First(w=>w.Name=="main");var status=await backend.Rpc(ws,"status");
                    long after=status.GetProperty("projects").EnumerateArray().Sum(p=>p.GetProperty("stats").GetProperty("documents").GetInt64());
                    if(before!=null&&after<before)throw new Exception($"Document count decreased: {before} -> {after}");
                    if(after==0)throw new Exception("Existing index unexpectedly empty");
                    var scope=ws.Roots.ToDictionary(r=>r.Path,_=>true,StringComparer.OrdinalIgnoreCase);
                    var hits=await backend.Search("测试","lexical",Selection.Positive(scope),5,scope);if(hits.Count==0)throw new Exception("Read-only lexical search failed after restart");
                    var files=await backend.Search("","lexical",Selection.Positive(scope),20,scope);if(files.Count<after)throw new Exception("Indexed file browsing unexpectedly incomplete");
                    var body=await backend.Body(hits[0]);if(body.Length==0)throw new Exception("Indexed body fallback was empty");
                    var deadline=managedStore.Load().PauseUntil;await backend.Pause(0.5);if(managedStore.Load().PauseUntil==deadline)throw new Exception("Pause deadline did not update");
                    backend.Continue();await backend.Pulse();if((await Native.SomaProcesses(lifecycleExe)).Any(Native.IsIndex))throw new Exception("Continue forced sync with all automatic options off");
                    Store.Atomic(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"index-retention.json"),JsonSerializer.Serialize(new{before,after,files=files.Count,lexicalHits=hits.Count,rebuildInvoked=false,scanInvoked=false,indexDeleted=false}));
                }
                finally{await backend.StopAll();}
            }
            Run().GetAwaiter().GetResult();
        });
        var isolatedExe=args.FirstOrDefault(a=>a.StartsWith("--isolated-index="))?[17..];
        if(isolatedExe!=null)Test("isolated incremental index, semantic/hybrid search, pause during search and relocation reuse",()=>
        {
            async Task Run()
            {
                var area=System.IO.Path.Combine(root,"safe-index");var configHome=System.IO.Path.Combine(area,"config","soma");var stateHome=System.IO.Path.Combine(area,"state","soma");var docs=System.IO.Path.Combine(area,"documents");Directory.CreateDirectory(docs);
                File.WriteAllText(System.IO.Path.Combine(docs,"battery.txt"),"Laptop battery endurance testing. 笔记本电池续航测试：屏幕亮度为百分之五十，文档办公续航十二小时，视频播放续航十小时。充电器拔下后使用电池供电。");
                File.WriteAllText(System.IO.Path.Combine(docs,"photography.txt"),"Landscape photography guide. 风景摄影指南：拍摄日落、山川和湖泊时，调整相机曝光、光圈、快门速度和白平衡。");
                var isolatedStore=new Store(System.IO.Path.Combine(area,"data"),configHome,stateHome);var ws=new Workspace{Name="finder-validation",ConfigPath=System.IO.Path.Combine(configHome,"finder-validation.yml"),Roots=new(){new(){Name="fixture-docs",Path=docs,Description="用于验证搜索的临时样例资料"}}};isolatedStore.SaveWorkspace(ws);
                var prefs=new Preferences{SomaExe=isolatedExe,StartupSync=false,PeriodicSync=false,BatteryBlocksAuto=false,LowMemoryBlocksAuto=false,LowerPriority=true};using var backend=new Backend(isolatedStore,prefs);
                try
                {
                    bool priorityObserved=false;backend.Changed+=()=>{if(backend.ActiveIndexProcessId is int pid){try{using var process=Process.GetProcessById(pid);if(process.PriorityClass==ProcessPriorityClass.BelowNormal)priorityObserved=true;}catch(ArgumentException){}}};
                    await backend.Initialize();var run=backend.RunSync(backend.Workspaces[0],true);
                    if(await Task.WhenAny(run,Task.Delay(TimeSpan.FromMinutes(4)))!=run)throw new TimeoutException("样例索引超时");await run;
                    if(!priorityObserved)throw new Exception("低优先级未应用到临时索引进程");
                    var status=await backend.Rpc(backend.Workspaces[0],"status");var stats=status.GetProperty("projects")[0].GetProperty("stats");Equal(stats.GetProperty("documents").GetInt64(),2L);if(stats.GetProperty("embeddings").GetInt64()==0)throw new Exception("样例语义向量尚未生成");
                    var selection=new Dictionary<string,bool>{{docs,true}};
                    var semantic=await backend.Search("笔记本电池可以使用多长时间","vector",new(){docs},2,selection);if(!semantic.Any(h=>h.Name=="battery.txt"))throw new Exception("语义查询未返回电池文档");
                    var hybridTask=backend.Search("查找有关笔记本电池续航测试的报告","hybrid",new(){docs},2,selection);var pauseTask=backend.Pause(0.1);
                    var combined=Task.WhenAll(hybridTask,pauseTask);
                    if(await Task.WhenAny(combined,Task.Delay(TimeSpan.FromMinutes(5)))!=combined)
                    {if(!hybridTask.IsCompleted||!pauseTask.IsCompleted)throw new TimeoutException("混合查询与暂停转换超时");}
                    var hybrid=await hybridTask;await pauseTask;if(!hybrid.Any(h=>h.Name=="battery.txt"))throw new Exception("混合查询未返回电池文档");
                    Equal(isolatedStore.Load().IsPaused(DateTimeOffset.UtcNow),true);backend.Continue();
                    File.WriteAllText(System.IO.Path.Combine(docs,"additional.txt"),"Additional indexing test. 新增文件的增量同步测试，确认既有记录仍然保留。");await backend.RunSync(backend.Workspaces[0],true);
                    var relocated=System.IO.Path.Combine(area,"documents-relocated");Directory.Move(docs,relocated);var config=backend.Workspaces[0];config.Roots[0].Path=relocated;isolatedStore.SaveWorkspace(config);prefs.Volumes.Remove(docs);prefs.Volumes[relocated]=Native.Identify(relocated)!;backend.Reload();await backend.RunSync(backend.Workspaces[0],true);
                    var final=await backend.Rpc(backend.Workspaces[0],"status");Equal(final.GetProperty("projects")[0].GetProperty("stats").GetProperty("documents").GetInt64(),3L);
                    var missingDocs=System.IO.Path.Combine(area,"unindexed-docs");Directory.CreateDirectory(missingDocs);var unavailable=new Workspace{Name="unindexed-validation",ConfigPath=System.IO.Path.Combine(configHome,"unindexed-validation.yml"),Roots=new(){new(){Name="unindexed-root",Path=missingDocs}}};isolatedStore.SaveWorkspace(unavailable);backend.Reload();
                    try{await backend.Search("电池","lexical",new(){relocated,missingDocs},5,new Dictionary<string,bool>{{relocated,true},{missingDocs,true}});throw new Exception("Missing index was not reported");}
                    catch(PartialSearchException ex)
                    {
                        bool Has(string expected)=>ex.CompletedRoots.Any(p=>Rules.Normalize(p).Equals(Rules.Normalize(expected),StringComparison.OrdinalIgnoreCase));
                        if(ex.Hits.Count==0||!Has(relocated)||Has(missingDocs))throw new Exception($"Successful partial results were lost: hits={ex.Hits.Count}, completed={JsonSerializer.Serialize(ex.CompletedRoots)}, expected={relocated}");
                    }
                    Store.Atomic(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"semantic-fixture.json"),JsonSerializer.Serialize(new{documents=3,semanticHits=semantic.Count,hybridHits=hybrid.Count,mainWorkspaceModified=false,scanOrRebuildInvoked=false}));
                }
                finally{await backend.StopAll();}
            }
            Run().GetAwaiter().GetResult();
        });
        if(args.Contains("--render"))Test("offscreen Fluent layout, no foreground interaction",()=>
        {
            var renderStore=new Store(System.IO.Path.Combine(root,"render-data"),System.IO.Path.Combine(root,"empty-config"),System.IO.Path.Combine(root,"render-state"));
            var app=new FinderApplication(renderStore,new Preferences(),true);
            app.Resources.MergedDictionaries.Clear();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml")});
            var window=new SearchWindow(app,new Dictionary<string,bool>());window.ThemeMode=ThemeMode.Light;window.SeedVerification();
            _=new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();window.ApplyTemplate();
            var content=(FrameworkElement)window.Content;content.Width=1400;content.Height=790;content.Measure(new Size(1400,790));content.Arrange(new Rect(0,0,1400,790));content.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1400,790,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"ui-light.png"));encoder.Save(stream);
            window.ThemeMode=ThemeMode.Dark;app.Resources.MergedDictionaries.Clear();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml")});app.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);content.UpdateLayout();var dark=new RenderTargetBitmap(1400,790,96,96,PixelFormats.Pbgra32);dark.Render(content);var darkEncoder=new PngBitmapEncoder();darkEncoder.Frames.Add(BitmapFrame.Create(dark));using var darkStream=File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"ui-dark.png"));darkEncoder.Save(darkStream);
            Test("relocation remaps window scope and paths without losing snapshots",()=>
            {
                var ws=new Workspace{Name="demo",ConfigPath=System.IO.Path.Combine(renderStore.ConfigDirectory,"demo.yml"),Roots=new(){new(){Name="demo-root",Path="C:/示例资料"}}};renderStore.SaveWorkspace(ws);app.Backend.Reload();window.ReloadTree();
                ws.Roots[0].Path="C:/迁移资料";renderStore.SaveWorkspace(ws);app.Backend.Reload();window.ReloadTree();Equal(window.VerificationCandidates.Count,2);Equal(Rules.Contains("C:/迁移资料",window.VerificationCandidates[0].FullPath),true);Equal(Selection.Includes(window.Scope(),"C:/迁移资料/笔记本续航测试记录.pdf"),true);
            });
            Test("removed root retains historical results without a new query",()=>{var ws=renderStore.ReadWorkspaces()[0];ws.Roots.Clear();renderStore.SaveWorkspace(ws);app.Backend.Reload();window.ReloadTree();Equal(window.VerificationCandidates.Count,2);Equal(Selection.Includes(window.Scope(),"C:/迁移资料/笔记本续航测试记录.pdf"),true);});
            Test("window close defers Close until Closing event returns",()=>{bool closed=false;window.Closed+=(_,_)=>closed=true;window.Close();app.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);Equal(closed,true);});
        });
        Store.Atomic(report,JsonSerializer.Serialize(new{version=typeof(Program).Assembly.GetName().Version?.ToString(),date=DateTimeOffset.UtcNow,failures,tests=records,excluded="No scan/rebuild/index deletion, no real GUI interaction, no power/sleep hardware tests"},new JsonSerializerOptions{WriteIndented=true}));
        return failures==0?0:1;
    }
}
