// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace SomaFinder;

internal static class OptimizationVerification
{
    public static int Run(string[] args)
    {
        string report=System.IO.Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);var root=System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-optimization-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var store=new Store(root+"/data",root+"/config",root+"/state");var docs=root+"/docs";Directory.CreateDirectory(docs);var ws=new Workspace{Name="legacy",ConfigPath=store.ConfigDirectory+"/legacy.yml",Roots=new(){new(){Name="retained-id",Path=docs}}};store.SaveWorkspace(ws);Directory.CreateDirectory(store.StateDirectory);File.WriteAllText(store.StateDirectory+"/legacy.sqlite","retained fixture bytes");
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false};prefs.Volumes[docs]=Native.Identify(docs)!;var app=new FinderApplication(store,prefs,true);int failures=0;var records=new List<object>();
        app.Startup+=async(_,_)=>
        {
            void Check(bool value,string message){if(!value)throw new Exception(message);}
            async Task Test(string name,Func<Task> action){try{await action();records.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine("FAIL "+name+" "+ex.Message);}}
            try
            {
                await Test("automatic grouping preserves old root IDs and databases, isolates unknown devices",()=>
                {
                    string configHash=Store.Digest(ws.ConfigPath),indexHash=Store.Digest(store.StateDirectory+"/legacy.sqlite");var list=store.ReadWorkspaces();var disks=new Dictionary<string,string>();var groups=new Dictionary<string,string>();
                    var chosen=RootGrouping.Choose(list,disks,groups,_=>"device:A","device:A",store.ConfigDirectory);Check(chosen.Name=="legacy"&&chosen.Roots[0].Name=="retained-id","Existing root moved");var different=RootGrouping.Choose(list,disks,groups,_=>"device:A","device:B",store.ConfigDirectory);Check(different.Name!="legacy","Different disk merged");var same=RootGrouping.Choose(list,disks,groups,_=>"device:A","device:A",store.ConfigDirectory);Check(ReferenceEquals(chosen,same),"Same disk created another group");
                    var unknown=RootGrouping.Choose(list,disks,groups,_=>"device:A","volume:unknown",store.ConfigDirectory);Check(unknown.Name!=chosen.Name&&unknown.Name!=different.Name,"Unknown identity merged");Check(Store.Digest(ws.ConfigPath)==configHash&&Store.Digest(store.StateDirectory+"/legacy.sqlite")==indexHash,"Grouping altered saved data");return Task.CompletedTask;
                });
                await Test("duplicate files merge by volume-relative path while distinct files remain",()=>
                {
                    string other=root+"/alias";prefs.Volumes[other]=new(prefs.Volumes[docs].Volume,prefs.Volumes[docs].RelativePath,"not-a-persistent-key");var a=new Hit{Workspace="first",RootPath=docs,FullPath=docs+"/same.md",MatchMode="lexical"};var b=new Hit{Workspace="second",RootPath=other,FullPath=other+"/same.md",MatchMode="hybrid"};var c=new Hit{Workspace="second",RootPath=other,FullPath=other+"/different.md",MatchMode="hybrid"};var hits=app.Backend.Deduplicate(new[]{a,b,c});Check(hits.Count==2&&hits.Contains(b)&&!hits.Contains(a),"Duplicate or lower priority result retained");return Task.CompletedTask;
                });
                await Test("root progress distinguishes files, vector chunks and unavailable failure counts",()=>
                {
                    var text=RootGrouping.ProgressText(new(100,80,3,200,120,DateTimeOffset.UtcNow));Check(text.Contains("80/100 文件")&&text.Contains("提取失败 3")&&text.Contains("120/200 分块")&&text.Contains("失败 —"),"Progress units conflated");Check(RootGrouping.ProgressText(new(10,0,0,0,0,DateTimeOffset.UtcNow)).Contains("待建立"),"Zero denominator shown as completed");return Task.CompletedTask;
                });
                await Test("rich summary bolds positive submitted terms and keeps literal source text",()=>
                {
                    var summary=new SummaryTextView{Text="needle NEEDLE <tag>\nsecond line",HighlightQuery="needle -second"};Check(summary.BoldRuns==2&&summary.Text.Contains("<tag>"),"Literal highlight failed");Check(new TextRange(summary.Document.ContentStart,summary.Document.ContentEnd).Text.Contains("<tag>"),"Summary interpreted as markup");summary.HighlightQuery="second";Check(summary.BoldRuns==1,"Highlight did not refresh");summary.HighlightQuery="needle NOT second -\"second line\"";Check(summary.BoldRuns==2,"Negative phrase or NOT operand was highlighted");return Task.CompletedTask;
                });
                await Test("first finger then second finger starts pinch and returns to one-finger mode",()=>
                {
                    double scale=1;var tracker=new PinchTracker(factor=>scale*=factor);tracker.Down(1,new Point(0,0));Check(!tracker.Move(1,new Point(0,5))&&scale==1,"One finger zoomed");tracker.Down(2,new Point(100,5));Check(tracker.Move(2,new Point(150,5))&&Math.Abs(scale-1.5)<.01,"Second finger did not enable zoom");tracker.Move(2,new Point(75,5));Check(Math.Abs(scale-.75)<.01,"Pinch-in failed");tracker.Up(2);Check(!tracker.Move(1,new Point(0,20)),"Single touch remained captured as zoom");return Task.CompletedTask;
                });
                await Test("actual WPF routed touch sequence reaches the summary through its scroll host",async()=>
                {
                    app.NewWindow();var window=app.Windows.OfType<SearchWindow>().Single();var view=CloudVerification.Descendants<SummaryTextView>(window).Single();view.Text=string.Join("\n",Enumerable.Repeat("needle text for touch scrolling",80));await Task.Delay(100);var source=(HwndSource)PresentationSource.FromVisual(view);double original=view.Document.FontSize;
                    var first=new VirtualTouch(301,view,source);var second=new VirtualTouch(302,view,source);
                    try{first.Down(new Point(40,100));second.Down(new Point(140,100));second.Move(new Point(200,100));await Task.Delay(50);Check(window.SummaryZoom>1.3&&view.Document.FontSize>original*1.3,"Real routed touch did not change document font: "+window.SummaryZoom+" / "+view.Document.FontSize);second.Move(new Point(120,100));Check(window.SummaryZoom<1&&view.Document.FontSize<original,"Real routed pinch-in failed");}
                    finally{first.Up();second.Up();window.Close();}
                });
                await Test("settings expose directory scope and per-root progress, not workspace controls",async()=>
                {
                    var settings=new SettingsWindow(app);settings.Show();await Task.Delay(80);var labels=CloudVerification.Descendants<TextBlock>(settings).Select(t=>t.Text).ToList();var buttons=CloudVerification.Descendants<Button>(settings).Select(b=>b.Content as string).ToList();Check(!buttons.Contains("创建工作区")&&labels.Any(x=>x.Contains("关键词："))&&labels.Any(x=>x.Contains("语义：")),"Directory progress or hidden workspace controls wrong");Check(CloudVerification.Descendants<TabItem>(settings).Any(t=>(string?)t.Header=="索引范围"),"Scope tab missing");
                    var content=(FrameworkElement)settings.Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var drawing=backdrop.RenderOpen())drawing.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,content.ActualWidth,content.ActualHeight));bitmap.Render(backdrop);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(report)!,"index-scope.png")))encoder.Save(output);settings.Close();
                });
            }
            catch(Exception ex){failures++;records.Add(new{name="suite",passed=false,error=ex.ToString()});}
            finally{Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests=records,scope="Targeted WPF and routing tests with synthetic touch devices, simulated persistent disk identities and isolated fixture files. No physical touchscreen or USB hardware acceptance."},new JsonSerializerOptions{WriteIndented=true}));app.Backend.Dispose();app.Shutdown(failures==0?0:1);}
        };return app.Run();
    }
    sealed class VirtualTouch:TouchDevice
    {
        readonly FrameworkElement target;readonly UIElement rootVisual;Point point;TouchAction action;
        public VirtualTouch(int id,FrameworkElement target,PresentationSource source):base(id){this.target=target;rootVisual=(UIElement)source.RootVisual;SetActiveSource(source);}
        public void Down(Point value){point=value;action=TouchAction.Down;Activate();ReportDown();}
        public void Move(Point value){point=value;action=TouchAction.Move;ReportMove();}
        public void Up(){if(!IsActive)return;action=TouchAction.Up;ReportUp();Deactivate();}
        public override TouchPoint GetTouchPoint(IInputElement? relativeTo){var pos=target.TranslatePoint(point,relativeTo as UIElement??rootVisual);return new TouchPoint(this,pos,new Rect(pos,new Size(2,2)),action);}
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo)=>new(){GetTouchPoint(relativeTo)};
    }
}
