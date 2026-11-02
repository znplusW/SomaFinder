// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SomaFinder;
internal static class DiscoveryVerification
{
    public static int Run(string[] args)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true"||Environment.GetEnvironmentVariable("RUNNER_OS")!="Windows")throw new InvalidOperationException("此验证仅在云端 Windows 运行。");
        var report=System.IO.Path.GetFullPath(args.First(x=>x.StartsWith("--report="))[9..]);var root=System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"discovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var records=new List<object>();int failures=0;
        void Test(string name,Action action){try{action();records.Add(new{name,passed=true});}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        Test("saved, sibling, PATH and registry paths are searched in deterministic order",()=>
        {
            var folder=System.IO.Path.Combine(root,"中文 portable tools");Directory.CreateDirectory(folder);var exe=System.IO.Path.Combine(folder,"soma.exe");File.WriteAllText(exe,"synthetic file, never executed");
            var candidates=BackendDiscovery.Candidates("",root,System.IO.Path.Combine(root,"home"),"\""+folder+"\";"+folder,new[]{exe}).ToList();Check(candidates.Count(x=>Native.SameFilePath(x,exe))==1);Check(candidates.FirstOrDefault(File.Exists)==exe);
            Check(BackendDiscovery.Candidates(exe,root,root,"",Array.Empty<string>()).First()==exe);
        });
        Test("missing backend returns no invented executable path",()=>Check(!BackendDiscovery.Candidates("",root,System.IO.Path.Combine(root,"empty-home"),"",Array.Empty<string>()).Any(File.Exists)));
        Test("official Windows download filename is discoverable without renaming",()=>
        {
            var folder=System.IO.Path.Combine(root,"official-name");Directory.CreateDirectory(folder);var exe=System.IO.Path.Combine(folder,"soma-windows-x64.exe");File.WriteAllText(exe,"synthetic file, never executed");
            Check(BackendDiscovery.Candidates("",folder,System.IO.Path.Combine(root,"empty-home"),"",Array.Empty<string>()).FirstOrDefault(File.Exists)==exe);
        });
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,ThemeMode=ThemeMode.System};int chosen=0,postponed=0;string? opened=null;
        Test("missing backend prompt has a verified download link and deployment instructions",()=>
        {
            var prompt=BackendDiscovery.CreateMissingPrompt(null,()=>chosen++,()=>postponed++,url=>opened=url);
            var text=CloudVerification.Descendants<TextBlock>(prompt).Select(x=>x.Text);Check(string.Join(" ",text).Contains("先下载"));
            var link=CloudVerification.Descendants<TextBlock>(prompt).SelectMany(x=>x.Inlines.OfType<Hyperlink>()).Single();
            link.RaiseEvent(new System.Windows.Navigation.RequestNavigateEventArgs(link.NavigateUri,""){RoutedEvent=Hyperlink.RequestNavigateEvent});Check(opened==BackendDiscovery.DownloadUrl&&opened.StartsWith("https://github.com/AwesomeDog/soma/"));
            CloudVerification.Descendants<Button>(prompt).Single(x=>(string?)x.Content=="选择已部署的 soma.exe").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(chosen==1&&postponed==0);
        });
        Test("postpone remains available without launching a browser or backend",()=>
        {
            opened=null;var prompt=BackendDiscovery.CreateMissingPrompt(null,()=>chosen++,()=>postponed++,url=>opened=url);CloudVerification.Descendants<Button>(prompt).Single(x=>(string?)x.Content=="稍后配置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(postponed==1&&opened==null);
        });
        var a=System.IO.Path.Combine(root,"中文 portable tools","soma.exe");var b=System.IO.Path.Combine(root,"second","soma.exe");Directory.CreateDirectory(System.IO.Path.GetDirectoryName(b)!);File.WriteAllText(b,"synthetic file, never executed");
        var catalog=new BackendCatalog(new(){new(a,"soma 0.10.7",3),new(b,"soma 0.10.6",0)});
        Test("first discovery of two installation paths requires explicit selection",()=>
        {
            Check(BackendDiscovery.NeedsChoice(new Preferences{SomaExe=a},catalog));
            Check(!BackendDiscovery.NeedsChoice(new Preferences{SomaExe=a},new(new(){catalog.Items[0]})));
            Check(catalog.Items[0].ProcessCount==3&&catalog.Items.Count==2);
        });
        Test("selection persists without resetting pause or automatic sync preferences",()=>
        {
            var store=new Store(System.IO.Path.Combine(root,"saved-data"));var prefs=new Preferences{StartupSync=false,PeriodicSync=true,PauseUntil=DateTimeOffset.UtcNow.AddHours(7)};
            BackendDiscovery.Remember(prefs,b,new[]{a,b,a});store.Save(prefs);var restored=store.Load();
            Check(restored.SomaExe==b&&restored.ConfirmedSomaExe==b&&restored.KnownBackendPaths.Count==2&&!restored.StartupSync&&restored.PeriodicSync&&restored.PauseUntil==prefs.PauseUntil);
            Check(!BackendDiscovery.NeedsChoice(restored,catalog));
            File.Delete(b);Check(BackendDiscovery.NeedsChoice(restored,new(new(){catalog.Items[0],new(a+".alternative","soma 0.10.7",0)})));Check(BackendDiscovery.NeedsChoice(restored,new(new(){catalog.Items[0]})));File.WriteAllText(b,"synthetic file, never executed");
        });
        Test("multiple-backend dialog lists versions and running state and cancels without saving",()=>
        {
            var prefs=new Preferences{SomaExe=a};var window=new BackendSelectionWindow(null,prefs,catalog);window.Show();window.UpdateLayout();app.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.UpdateLayout();
            var grid=CloudVerification.Descendants<DataGrid>(window).Single();Check(grid.Items.Count==2&&grid.SelectionMode==DataGridSelectionMode.Single);
            var second=grid.ItemContainerGenerator.ContainerFromIndex(1) as DataGridRow;double? bottom=second?.TranslatePoint(new Point(0,second.ActualHeight),grid).Y;Store.Atomic(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"backend-choice-layout.json"),JsonSerializer.Serialize(new{pathWidth=grid.Columns[0].ActualWidth,gridHeight=grid.ActualHeight,secondRowHeight=second?.ActualHeight,secondRowBottom=bottom}));Check(grid.Columns[0].ActualWidth>=320);Check(second!=null&&second.ActualHeight>0&&bottom<=grid.ActualHeight);
            Check(((BackendCandidate)grid.Items[0]).State.Contains("当前使用")&&((BackendCandidate)grid.Items[0]).State.Contains("3"));
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);var drawing=new System.Windows.Media.DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(SystemColors.WindowBrush,null,new Rect(0,0,bitmap.Width,bitmap.Height));context.DrawImage(bitmap,new Rect(0,0,bitmap.Width,bitmap.Height));}var composite=new System.Windows.Media.Imaging.RenderTargetBitmap(bitmap.PixelWidth,bitmap.PixelHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);composite.Render(drawing);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(composite));using(var stream=File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"backend-selection.png")))encoder.Save(stream);
            CloudVerification.Descendants<Button>(window).Single(x=>(string?)x.Content=="稍后设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(window.SelectedPath==null&&prefs.ConfirmedSomaExe==""&&prefs.SomaExe==a);
        });
        Test("selection action uses the selected row and leaves other copies intact",()=>
        {
            var window=new BackendSelectionWindow(null,new Preferences{SomaExe=a},catalog);var grid=CloudVerification.Descendants<DataGrid>(window).Single();grid.SelectedIndex=1;
            CloudVerification.Descendants<Button>(window).Single(x=>(string?)x.Content=="使用所选后端").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(window.SelectedPath==b&&File.Exists(a)&&File.Exists(b));
        });
        Test("invalid executables cannot be accepted as a backend",()=>
        {
            var window=new BackendSelectionWindow(null,new Preferences(),new(new(){new(a,"未识别为 Soma 后端",0)}));var grid=CloudVerification.Descendants<DataGrid>(window).Single();grid.SelectedIndex=0;
            Check(!CloudVerification.Descendants<Button>(window).Single(x=>(string?)x.Content=="使用所选后端").IsEnabled);window.Close();
        });
        Test("same database with different config or workspace name still conflicts",()=>
        {
            var store=new Store(System.IO.Path.Combine(root,"identity/data"),System.IO.Path.Combine(root,"identity/config"),System.IO.Path.Combine(root,"identity/state"));var workspace=new Workspace{Name="main",ConfigPath=System.IO.Path.Combine(store.ConfigDirectory,"main.yml")};
            JsonElement Status(string name,string db,string config)=>JsonSerializer.SerializeToElement(new{workspace=name,databaseFile=db,configFile=config});var db=System.IO.Path.Combine(store.StateDirectory,"main.sqlite");
            Check(BackendInstances.SameStore(Status("main",db,workspace.ConfigPath),store,workspace));
            Check(!BackendInstances.SameStore(Status("other",db,a),store,workspace)&&BackendInstances.TouchesStore(Status("other",db,a),store,new[]{workspace}));
            Check(!BackendInstances.TouchesStore(Status("main",b,a),store,new[]{workspace}));
            Check(!Native.SameFilePath("","")&&!Native.SameStorageFile("",db));
        });
        Test("old metadata without an active byte-range lock is not an active writer",()=>
        {
            var store=new Store(System.IO.Path.Combine(root,"locks/data"),System.IO.Path.Combine(root,"locks/config"),System.IO.Path.Combine(root,"locks/state"));var workspace=new Workspace{Name="main"};var dir=System.IO.Path.Combine(store.StateDirectory,"locks");Directory.CreateDirectory(dir);
            File.WriteAllText(System.IO.Path.Combine(dir,"main.lock"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,command="soma sync"}));Check(BackendInstances.Writers(store,new[]{workspace}).Count==0);
        });
        Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests=records,mode="Cloud synthetic discovery and real dialog control handlers; no browser launch"},new JsonSerializerOptions{WriteIndented=true}));return failures==0?0:1;
    }
}
