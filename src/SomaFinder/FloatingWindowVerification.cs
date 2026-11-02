// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Drawing=System.Drawing;
using Forms=System.Windows.Forms;

namespace SomaFinder;

// Real HWND ownership, foreground and Z-order on the hosted desktop. Button
// actions are WPF routed clicks, not a claim of physical mouse acceptance.
internal static class FloatingWindowVerification
{
    public static int Run(string[] args)
    {
        string report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        string root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-floating-windows-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,DefaultDetailLayout=DetailLayout.Both};
        var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),prefs,true);
        int failures=0;var tests=new List<object>();var observations=new List<object>();
        app.Startup+=async(_,_)=>
        {
            SearchWindow? search=null;
            void Check(bool ok,string why){if(!ok)throw new Exception(why);}
            async Task Wait(Func<bool> condition){var clock=Stopwatch.StartNew();while(!condition()&&clock.ElapsedMilliseconds<6000)await Task.Delay(30);Check(condition(),"Timed out waiting for window state");}
            async Task Test(string name,Func<Task> action)
            {
                try{await action();Check(CloudSandbox.Errors.Count==0,string.Join('\n',CloudSandbox.Errors));tests.Add(new{name,passed=true});}
                catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString(),foreground=GetForegroundWindow().ToInt64()});}
            }
            void Above(Window pane,Window parent,bool active=false)
            {
                nint p=Handle(pane),o=Handle(parent);
                Check(pane.IsVisible&&IsWindowVisible(p)&&ReferenceEquals(pane.Owner,parent),"Floating pane is not visible or has the wrong managed owner");
                Check(GetWindow(p,4)==o,"Native owner is not the corresponding search window");
                Check(IsAbove(p,o),"Main search window covers its floating pane");
                Check(!pane.Topmost&&(GetWindowLongPtrW(p,-20).ToInt64()&8)==0,"Pane became globally topmost");
                if(active)Check(pane.IsActive&&GetForegroundWindow()==p,"New or located floating pane was not activated");
            }
            void Observe(string action,Window pane,Window parent)=>observations.Add(new{action,pane=Handle(pane).ToInt64(),owner=GetWindow(Handle(pane),4).ToInt64(),expectedOwner=Handle(parent).ToInt64(),aboveOwner=IsAbove(Handle(pane),Handle(parent)),active=pane.IsActive,foreground=GetForegroundWindow().ToInt64(),state=pane.WindowState.ToString(),pane.Topmost});
            async Task<Window> ClickPop(DetailKind kind)
            {
                var details=search!.VerificationDetails;if(details.Mode==DetailMode.Tabs)details.SelectTab(kind);await Task.Delay(50);
                string title=kind==DetailKind.Summary?"搜索摘要":"内容预览";
                var label=CloudVerification.Descendants<TextBlock>(search.PanelGrid).Single(t=>t.Text==title);
                var button=CloudVerification.Descendants<Button>((DependencyObject)label.Parent).Single(b=>Equals(b.Content,"独立窗口"));
                Check(button.IsVisible&&button.IsEnabled,"Pop-out button is not reachable");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Wait(()=>details.FloatingWindow(kind)?.IsLoaded==true);await Task.Delay(350);
                return details.FloatingWindow(kind)!;
            }
            void Desktop(string name)
            {
                var area=Forms.SystemInformation.VirtualScreen;
                using var image=new Drawing.Bitmap(area.Width,area.Height);using(var graphics=Drawing.Graphics.FromImage(image))graphics.CopyFromScreen(area.Location,Drawing.Point.Empty,area.Size);
                image.Save(Path.Combine(Path.GetDirectoryName(report)!,name+".png"),Drawing.Imaging.ImageFormat.Png);
            }
            try
            {
                File.WriteAllText(root+"/notes.txt","floating-fixture: 海边旅行计划，沿步道前往观景台。\nSummary and preview stay in their own search window.");
                prefs.Volumes[root]=Native.Identify(root)!;app.NewWindow();search=app.Windows.OfType<SearchWindow>().Single();search.EnableCloudLayoutFixture();search.Width=950;search.Height=690;search.Left=search.Top=20;
                var hit=new Hit{Workspace="fixture",RootPath=root,FullPath=root+"/notes.txt",VirtualPath="soma://fixture/notes.txt",Snippet="海边旅行计划，沿步道前往观景台。"};
                var scope=new Dictionary<string,bool>{{root,true}};search.SeedResults(new[]{hit},scope);search.VerificationSelect(0);
                var details=search.VerificationDetails;details.SelectTab(DetailKind.Summary);
                var summary=CloudVerification.Descendants<SummaryTextView>(search).Single();details.SelectTab(DetailKind.Preview);
                await Wait(()=>search.VerificationPreviewText.Contains("floating-fixture"));
                var preview=((PreviewHost)search.PreviewSurface).Content!;long requests=app.Backend.RequestCount;
                await Test("summary and preview pop-out buttons activate a pane above the corresponding main window",async()=>
                {
                    foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview})
                    {
                        try
                        {
                            var pane=await ClickPop(kind);Above(pane,search,true);Observe("first pop "+kind,pane,search);
                            Check(pane.Title==search.Title&&pane.ShowInTaskbar&&pane.ResizeMode==ResizeMode.CanResize,"Title, taskbar presence or resizing changed");
                        }
                        finally{details.Dock(kind);await Task.Delay(80);}
                        Check(!details.IsDetached(kind),"Docking did not return the pane");
                    }
                });
                await Test("activating the main window cannot cover either pane and unrelated windows remain usable",async()=>
                {
                    await ClickPop(DetailKind.Summary);await ClickPop(DetailKind.Preview);search.Activate();await Task.Delay(180);
                    foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview}){Above(details.FloatingWindow(kind)!,search);Observe("parent activated "+kind,details.FloatingWindow(kind)!,search);}
                    var a=details.FloatingWindow(DetailKind.Summary)!;var b=details.FloatingWindow(DetailKind.Preview)!;
                    a.Left=70;a.Top=100;a.Width=470;a.Height=480;b.Left=300;b.Top=170;b.Width=470;b.Height=480;details.Locate(DetailKind.Preview);await Task.Delay(220);Desktop("owned-floating-windows");
                    var unrelated=new Window{Title="Cloud unrelated application window",Width=450,Height=380,Left=120,Top=80,Content=new TextBlock{Text="Unrelated window can cover Finder panes",Margin=new Thickness(18)}};
                    try
                    {
                        unrelated.Show();unrelated.Activate();await Task.Delay(120);
                        Check(GetForegroundWindow()==Handle(unrelated)&&IsAbove(Handle(unrelated),Handle(a))&&IsAbove(Handle(unrelated),Handle(b)),"Floating panes cover an unrelated foreground window");
                    }
                    finally{unrelated.Close();details.Locate(DetailKind.Preview);}
                });
                await Test("independent minimize maximize and locate keep the correct owner and restore foreground",async()=>
                {
                    foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview})
                    {
                        var pane=details.FloatingWindow(kind)!;pane.WindowState=WindowState.Minimized;await Task.Delay(120);
                        Check(IsIconic(Handle(pane))&&search.WindowState==WindowState.Normal,"Minimizing the pane minimized the parent");
                        details.Locate(kind);await Task.Delay(150);Above(pane,search,true);Check(pane.WindowState==WindowState.Normal,"Locate did not restore minimized pane");
                        pane.WindowState=WindowState.Maximized;await Task.Delay(120);Check(search.WindowState==WindowState.Normal&&pane.WindowState==WindowState.Maximized,"Pane maximize altered the parent");
                        pane.WindowState=WindowState.Normal;details.Float(kind);await Task.Delay(120);Above(pane,search,true);Check(ReferenceEquals(pane,details.FloatingWindow(kind)),"Locating an existing pane created another window");
                    }
                    search.HideAuxiliaries();search.Hide();await Task.Delay(70);Check(!details.FloatingWindow(DetailKind.Summary)!.IsVisible&&!details.FloatingWindow(DetailKind.Preview)!.IsVisible,"Tray hiding left a floating pane visible");
                    app.MarkRecent(search);app.ActivateRecent();await Task.Delay(220);foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview})Above(details.FloatingWindow(kind)!,search);
                    search.WindowState=WindowState.Minimized;await Task.Delay(120);Check(IsIconic(Handle(search))&&details.DetachedCount==2,"Parent minimizing destroyed a floating pane");
                    search.WindowState=WindowState.Normal;await Task.Delay(180);foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview})Above(details.FloatingWindow(kind)!,search);
                });
                await Test("multiple search windows keep separate ownership and closing one preserves the others",async()=>
                {
                    app.NewWindow();var other=app.Windows.OfType<SearchWindow>().Single(w=>w!=search);other.EnableCloudLayoutFixture();other.Width=900;other.Height=660;other.Left=other.Top=40;
                    var child=other.VerificationDetails;
                    try
                    {
                        child.Float(DetailKind.Summary);await Task.Delay(250);var pane=child.FloatingWindow(DetailKind.Summary)!;Above(pane,other,true);
                        Check(!ReferenceEquals(pane.Owner,search),"New search pane inherited another search window's owner");
                        other.Activate();await Task.Delay(80);Above(pane,other);
                        foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview})Check(ReferenceEquals(details.FloatingWindow(kind)!.Owner,search),"Existing pane ownership changed");
                    }
                    finally{other.SkipExitOnce=true;other.Close();await Task.Delay(120);}
                    Check(child.IsDisposed&&child.DetachedCount==0,"Closing one search left an owned pane behind");
                    foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview}){var pane=details.FloatingWindow(kind)!;Check(IsWindow(Handle(pane)),"Another search window's pane was closed");Above(pane,search);}
                });
                await Test("X redocks content and repeated pop-out preserves cached preview selection title and theme",async()=>
                {
                    foreach(string theme in new[]{"Dark","Light"})
                    {
                        prefs.SearchLayout.Theme=theme;app.ApplyAppearance();await Task.Delay(150);
                        foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview})
                        {
                            var previous=details.FloatingWindow(kind)!;nint previousHandle=Handle(previous);previous.Close();await Wait(()=>!details.IsDetached(kind));
                            Check(!IsWindow(previousHandle),"X did not close the floating HWND");
                            var pane=await ClickPop(kind);Above(pane,search,true);search.UpdateSearchTitle("Soma Finder 搜索“海边旅行”");Check(pane.Title==search.Title,"Floating title no longer follows its main window");
                        }
                    }
                    Check(ReferenceEquals(preview,((PreviewHost)search.PreviewSurface).Content)&&ReferenceEquals(summary,CloudVerification.Descendants<SummaryTextView>(details.FloatingWindow(DetailKind.Summary)!).Single()),"Docking recreated summary or cached preview");
                    Check(ReferenceEquals(search.SelectedHit,hit)&&app.Backend.RequestCount==requests,"Pop-out changed selection or issued a Soma request");
                    details.Locate(DetailKind.Preview);await Task.Delay(150);Desktop("owned-floating-light");
                });
                await Test("closing the parent destroys its panes and initially hidden windows can retain ownership",async()=>
                {
                    nint summaryHandle=Handle(details.FloatingWindow(DetailKind.Summary)!),previewHandle=Handle(details.FloatingWindow(DetailKind.Preview)!);
                    search.SkipExitOnce=true;search.Close();await Task.Delay(120);
                    Check(details.IsDisposed&&details.DetachedCount==0&&!IsWindow(summaryHandle)&&!IsWindow(previewHandle),"Parent close leaked owned windows");
                    var hidden=new SearchWindow(app,scope);var d=hidden.VerificationDetails;
                    try
                    {
                        d.Float(DetailKind.Summary,false);d.Float(DetailKind.Preview,false);
                        foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview})Check(ReferenceEquals(d.FloatingWindow(kind)!.Owner,hidden)&&!d.FloatingWindow(kind)!.IsVisible,"Initially hidden pane lost owner or appeared unexpectedly");
                        Check(!hidden.IsVisible,"Preparing hidden ownership displayed the parent");
                    }
                    finally{hidden.SkipExitOnce=true;hidden.Close();}
                    Check(d.IsDisposed&&d.DetachedCount==0,"Hidden owned panes leaked");
                });
            }
            catch(Exception ex){failures++;tests.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally
            {
                foreach(var window in app.Windows.OfType<SearchWindow>().ToArray()){window.SkipExitOnce=true;window.Close();}app.Backend.Dispose();
                Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,observations,scope="Only floating summary/preview ownership, initial activation, native foreground and Z-order, and directly affected window lifecycle. Hosted Windows desktop, WPF routed button clicks and synthetic local text; no physical mouse/multi-monitor acceptance, no Soma/indexing."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);
            }
        };
        return app.Run();
    }
    static nint Handle(Window window)=>new WindowInteropHelper(window).Handle;
    static bool IsAbove(nint a,nint b)
    {
        if(a==0||b==0||a==b)return false;
        int count=0;for(nint p=GetTopWindow(0),previous=0;p!=0&&p!=previous&&count++<4096;previous=p,p=GetWindow(p,2)){if(p==a)return true;if(p==b)return false;}
        return false;
    }
    [DllImport("user32.dll")]static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]static extern nint GetTopWindow(nint parent);
    [DllImport("user32.dll")]static extern nint GetWindow(nint window,uint command);
    [DllImport("user32.dll")]static extern nint GetWindowLongPtrW(nint window,int index);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]static extern bool IsIconic(nint window);
}
