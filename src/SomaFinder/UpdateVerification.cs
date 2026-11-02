// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SomaFinder;

// Focused verification for the 0.2.0 UI change. Never starts a Soma service or index.
internal static class UpdateVerification
{
    public static int Run(string[] args)
    {
        Environment.SetEnvironmentVariable("SOMAFINDER_TEST","1");
        var report=System.IO.Path.GetFullPath(args.FirstOrDefault(x=>x.StartsWith("--report="))?[9..]??"update-verification.json");
        var area=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"ui-fixture-"+Guid.NewGuid().ToString("N"));
        var results=new List<object>();int failures=0;
        void Check(bool condition,string message){if(!condition)throw new Exception(message);}
        void Test(string name,Action action){try{action();results.Add(new{name,passed=true});}catch(Exception ex){failures++;results.Add(new{name,passed=false,error=ex.ToString()});}}
        Test("frontend rendering compatibility set before window creation",()=>Check(RenderOptions.ProcessRenderMode==System.Windows.Interop.RenderMode.SoftwareOnly,"Software rendering not selected"));
        Test("default settings migrate and persist without changing other preferences",()=>
        {
            var p=JsonSerializer.Deserialize<Preferences>("{\"StartupSync\":false}")!;
            Check(p.DefaultSearchBreadth==50&&p.DefaultDetailLayout==DetailLayout.SummaryOnly,"Missing defaults incorrect");
            var store=new Store(System.IO.Path.Combine(area,"prefs"));p.DefaultSearchBreadth=37;p.DefaultDetailLayout=DetailLayout.PreviewOnly;store.Save(p);var loaded=store.Load();
            Check(loaded.DefaultSearchBreadth==37&&loaded.DefaultDetailLayout==DetailLayout.PreviewOnly&&!loaded.StartupSync,"Defaults failed round trip");
        });
        Test("duplicate submitted queries get stable peer numbers",()=>
        {
            var titles=new SearchTitles();object a=new(),b=new(),c=new();titles.Submit(a," 电池续航 ");
            Check(titles.Title(a)=="Soma Finder 搜索“电池续航”","Initial title");titles.Submit(b,"电池续航");
            Check(titles.Title(a).EndsWith("（1）")&&titles.Title(b).EndsWith("（2）"),"Duplicate numbering");
            titles.Submit(b,"电池续航");titles.Remove(a);Check(titles.Title(b).EndsWith("（2）"),"Existing title renumbered");
            titles.Submit(c,"电池续航");Check(titles.Title(c).EndsWith("（3）"),"Identifier reused");
            titles.Submit(b,"照片");Check(titles.Title(b)=="Soma Finder 搜索“照片”","Changed query title");titles.Submit(a,"");Check(titles.Title(a)=="Soma Finder 文件浏览","Browse title");
        });
        var isolated=new Store(System.IO.Path.Combine(area,"data"),System.IO.Path.Combine(area,"config"),System.IO.Path.Combine(area,"state"));
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,DefaultSearchBreadth=37,DefaultDetailLayout=DetailLayout.PreviewOnly};
        var app=new FinderApplication(isolated,prefs,true);
        void Pump()=>app.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        var first=new SearchWindow(app,new Dictionary<string,bool>());
        Test("new windows use defaults but existing windows stay independent",()=>
        {
            Check(first.VerificationBreadth==37&&first.VerificationDetails.LeftKind==DetailKind.Preview&&!first.VerificationDetails.RightExpanded,"Preview default");
            prefs.DefaultSearchBreadth=73;prefs.DefaultDetailLayout=DetailLayout.Both;
            var second=new SearchWindow(app,first.Scope());Check(second.VerificationBreadth==73&&second.VerificationDetails.RightExpanded,"Both default");
            Check(first.VerificationBreadth==37&&!first.VerificationDetails.RightExpanded,"Existing window changed");second.Close();Pump();
            prefs.DefaultSearchBreadth=-1;prefs.DefaultDetailLayout=(DetailLayout)99;
            var fallback=new SearchWindow(app,new Dictionary<string,bool>());Check(fallback.VerificationBreadth==50&&fallback.VerificationDetails.LeftKind==DetailKind.Summary&&!fallback.VerificationDetails.RightExpanded,"Invalid defaults did not fall back");fallback.Close();Pump();
        });
        Test("swap preserves slot visibility and right toggle controls new right pane",()=>
        {
            var d=first.VerificationDetails;d.Swap();Check(d.LeftKind==DetailKind.Summary&&!d.RightExpanded,"Single-pane swap");d.ToggleRight();Check(d.RightExpanded&&d.RightKind==DetailKind.Preview,"Right toggle");d.Swap();Check(d.RightKind==DetailKind.Summary&&d.RightExpanded,"Expanded swap");
        });
        first.SeedVerification();
        Test("both detail contents float once and inherit exact parent title and ownership",()=>
        {
            first.UpdateSearchTitle("Soma Finder 搜索“电池续航”（2）");var d=first.VerificationDetails;d.Float(DetailKind.Summary,false);d.Float(DetailKind.Preview,false);
            Check(d.DetachedCount==2,"Missing floating pane");
            foreach(var kind in new[]{DetailKind.Summary,DetailKind.Preview}){var w=d.FloatingWindow(kind)!;Check(w.Title==first.Title&&ReferenceEquals(w.Owner,first),"Title mismatch or wrong search-window owner");}
            first.UpdateSearchTitle("Soma Finder 搜索“照片”");Check(d.FloatingWindow(DetailKind.Preview)!.Title==first.Title,"Floating title stale");
        });
        Test("floating swaps preserve windows and disable detached right toggle",()=>
        {
            var d=first.VerificationDetails;var window=d.FloatingWindow(DetailKind.Summary);d.Swap();Check(ReferenceEquals(window,d.FloatingWindow(DetailKind.Summary)),"Float recreated by swap");
            Check(!d.RightToggleEnabled,"Detached right toggle still enabled");var open=d.RightExpanded;d.ToggleRight();Check(d.RightExpanded==open,"Detached pane collapsed unexpectedly");
        });
        Test("floating X reattaches and reveals its current right slot",()=>
        {
            var d=first.VerificationDetails;var right=d.RightKind;d.FloatingWindow(right)!.Close();Pump();Check(!d.IsDetached(right)&&d.RightExpanded,"X did not reattach and reveal");
        });
        Test("cached preview survives docking and layout without a backend call",()=>
        {
            var d=first.VerificationDetails;if(d.IsDetached(DetailKind.Preview))d.Dock(DetailKind.Preview,false);
            if(d.LeftKind!=DetailKind.Preview)d.Swap();Pump();Check(first.VerificationPreviewText.Contains("布局操作不重新搜索"),"Preview cache lost");
        });
        Test("focused light and dark layouts render after panel reparenting",()=>
        {
            if(first.VerificationDetails.IsDetached(DetailKind.Summary))first.VerificationDetails.Dock(DetailKind.Summary,false);
            if(!first.VerificationDetails.RightExpanded)first.VerificationDetails.ToggleRight();
            foreach(var theme in new[]{ThemeMode.Light,ThemeMode.Dark})
            {
                app.Resources.MergedDictionaries.Clear();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent."+(theme==ThemeMode.Light?"Light":"Dark")+".xaml")});first.ThemeMode=theme;Pump();
                var content=(FrameworkElement)first.Content;content.Width=1400;content.Height=790;content.Measure(new Size(1400,790));content.Arrange(new Rect(0,0,1400,790));content.UpdateLayout();
                var bitmap=new RenderTargetBitmap(1400,790,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,theme==ThemeMode.Light?"ui-light.png":"ui-dark.png"));encoder.Save(stream);
            }
        });
        Test("closing parent destroys auxiliary windows without reattaching",()=>
        {
            var d=first.VerificationDetails;d.Float(DetailKind.Summary,false);d.Float(DetailKind.Preview,false);first.Close();Pump();Check(d.IsDisposed&&d.DetachedCount==0,"Auxiliary windows survived parent close");
        });
        app.Backend.Dispose();
        Store.Atomic(report,JsonSerializer.Serialize(new{version=typeof(Program).Assembly.GetName().Version?.ToString(),date=DateTimeOffset.UtcNow,failures,tests=results,scope="Only 0.2.0 additions and rendering fix; no backend service, indexing, rebuild or foreground input"},new JsonSerializerOptions{WriteIndented=true}));
        return failures==0?0:1;
    }
}
