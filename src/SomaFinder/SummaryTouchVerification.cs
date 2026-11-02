// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;

namespace SomaFinder;

internal static class SummaryTouchVerification
{
    public static int Run(string[] args)
    {
        var report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-summary-touch-"+Guid.NewGuid().ToString("N"));
        CloudSandbox.Initialize(root);
        var store=new Store(root+"/data",root+"/config",root+"/state");
        var app=new FinderApplication(store,new Preferences{StartupSync=false,PeriodicSync=false},true);
        int failures=0;var records=new List<object>();
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;
            SummaryTextView? diagnosticView=null;
            void Check(bool condition,string message){if(!condition)throw new Exception(message);}
            async Task Test(string name,Func<Task> run)
            {
                try{await run();records.Add(new{name,passed=true,diagnostics=diagnosticView?.TouchDiagnostics});Console.WriteLine("PASS "+name);}
                catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString(),diagnostics=diagnosticView?.TouchDiagnostics});Console.WriteLine("FAIL "+name+" "+ex.Message);}
            }
            try
            {
                app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();
                var view=CloudVerification.Descendants<SummaryTextView>(window).Single();
                diagnosticView=view;
                string original=string.Join("\n",Enumerable.Range(1,180).Select(i=>$"Line {i}: needle summary text for finger scrolling and reflow."));
                view.Text=original;view.HighlightQuery="needle";await Task.Delay(120);
                VirtualTouch Touch(int id)=>new(id,view,PresentationSource.FromVisual(view)??throw new Exception("Summary has no presentation source"));
                async Task Scroll(double offset){view.ScrollToVerticalOffset(offset);await Task.Delay(60);view.UpdateLayout();}
                async Task WaitFor(Func<bool> ready){for(int i=0;i<50&&!ready();i++){await Task.Delay(20);view.UpdateLayout();}}

                await Test("one finger scrolls actual document in both directions and preserves selection",async()=>
                {
                    await Scroll(0);view.Selection.Select(view.Document.ContentStart,view.Document.ContentStart.GetPositionAtOffset(12)!);
                    var selected=view.Selection.Text;double font=view.FontSize;
                    double startY=Math.Min(180,view.ActualHeight-30);
                    Check(startY-100>view.Padding.Top,"Summary viewport is too short for the 100 DIP in-view gesture");
                    var finger=Touch(801);
                    try
                    {
                        finger.Down(new Point(60,startY));Check(view.ActiveTouches==1,"In-view finger did not reach summary touch handler");
                        finger.Move(new Point(60,startY-30));finger.Move(new Point(60,startY-60));finger.Move(new Point(60,startY-100));await Task.Delay(60);
                        Check(view.VerticalOffset>=90,"Upward finger did not scroll document: "+view.VerticalOffset);
                        double offset=view.VerticalOffset;finger.Move(new Point(60,startY-50));await Task.Delay(60);
                        Check(view.VerticalOffset<offset-40,"Reverse finger did not scroll back");
                        Check(view.FontSize==font&&view.Selection.Text==selected,"Single finger altered font or text selection");
                    }
                    finally{finger.Up();}
                    Check(view.ActiveTouches==0,"Last touch was retained");
                });

                await Test("pinch changes rendered font and keeps reading anchor near its original height",async()=>
                {
                    await Scroll(400);var a=Touch(811);var b=Touch(812);double initial=view.FontSize;
                    try
                    {
                        a.Down(new Point(40,140));b.Down(new Point(140,140));
                        var anchor=view.GetPositionFromPoint(new Point(110,140),true)??throw new Exception("No text anchor");
                        double before=anchor.GetCharacterRect(LogicalDirection.Forward).Top;
                        b.Move(new Point(180,140));await WaitFor(()=>Math.Abs(anchor.GetCharacterRect(LogicalDirection.Forward).Top-before)<8&&view.FontSize>initial*1.3);
                        Check(view.FontSize>initial*1.3&&view.Document.FontSize>initial*1.3,"Pinch did not enlarge actual text");
                        double drift=Math.Abs(anchor.GetCharacterRect(LogicalDirection.Forward).Top-before);
                        Check(drift<8,"Reading anchor jumped after reflow: "+drift);
                        b.Move(new Point(110,140));await Task.Delay(80);
                        Check(view.FontSize<initial,"Pinch-in did not shrink text");
                    }
                    finally{a.Up();b.Up();}
                });

                await Test("two fingers return directly to one finger scrolling without a jump or stuck zoom",async()=>
                {
                    await Scroll(350);var a=Touch(821);var b=Touch(822);
                    try
                    {
                        a.Down(new Point(40,180));b.Down(new Point(140,180));b.Move(new Point(160,180));await Task.Delay(80);
                        double offset=view.VerticalOffset,font=view.FontSize;b.Up();await Task.Delay(40);
                        Check(view.ActiveTouches==1&&Math.Abs(view.VerticalOffset-offset)<2,"Lifting second finger discarded first finger or jumped");
                        a.Move(new Point(40,150));await Task.Delay(80);
                        Check(Math.Abs(view.VerticalOffset-offset-30)<2,"Remaining finger did not pan by its own delta: "+(view.VerticalOffset-offset));
                        Check(view.FontSize==font,"Remaining finger continued to zoom");
                    }
                    finally{a.Up();b.Up();}
                });

                await Test("document bounds clamp and touch capture loss leaves future gestures usable",async()=>
                {
                    await Scroll(0);var a=Touch(831);
                    try
                    {
                        a.Down(new Point(50,100));a.Move(new Point(50,180));await Task.Delay(60);
                        Check(view.VerticalOffset==0,"Scrolled before document start");
                        a.Capture(null);Check(view.ActiveTouches==0,"Lost capture retained stale touch");
                    }
                    finally{a.Up();}
                    var b=Touch(832);
                    double y=Math.Min(220,view.ActualHeight-20);
                    try{b.Down(new Point(60,y));b.Move(new Point(60,y-100));await Task.Delay(60);Check(view.VerticalOffset>80,"Fresh gesture failed after capture loss");}
                    finally{b.Up();}
                });

                await Test("undocking and redocking release fingers while retaining summary font and source text",async()=>
                {
                    await Scroll(300);double font=view.FontSize;var finger=Touch(841);
                    finger.Down(new Point(50,150));window.VerificationDetails.Float(DetailKind.Summary);await Task.Delay(120);
                    Check(view.ActiveTouches==0,"Detached summary retained active finger");finger.Up();
                    Check(view.FontSize==font&&view.Text==original&&view.BoldRuns==180,"Undocking lost font, text or highlights");
                    Check(view.VerticalOffset>0,"Undocking unexpectedly reset reading to start");
                    var floatingTouch=Touch(842);double before=view.VerticalOffset;
                    try{floatingTouch.Down(new Point(60,220));floatingTouch.Move(new Point(60,160));await Task.Delay(60);Check(view.VerticalOffset>before+40,"Floating summary did not scroll");}
                    finally{floatingTouch.Up();}
                    window.VerificationDetails.Dock(DetailKind.Summary);await WaitFor(()=>view.IsLoaded&&view.ActiveTouches==0&&view.VerticalOffset>0);
                    Check(view.FontSize==font&&view.ActiveTouches==0&&view.VerticalOffset>0,"Redocking lost gesture state or reading position");
                    Check(view.IsReadOnly&&new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text.Contains("Line 180"),"Source document was altered");
                });
            }
            catch(Exception ex){failures++;records.Add(new{name="suite",passed=false,error=ex.ToString()});}
            finally
            {
                window?.Close();Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests=records,scope="Actual WPF RichTextBox routed TouchDevice input in a cloud Windows window. Checks rendered font, scroll offsets, anchor reflow and docking; synthetic touch input is not physical touchscreen acceptance. No Soma backend started."},new JsonSerializerOptions{WriteIndented=true}));
                app.Backend.Dispose();app.Shutdown(failures==0?0:1);
            }
        };
        return app.Run();
    }

    sealed class VirtualTouch:TouchDevice
    {
        readonly FrameworkElement target;readonly UIElement root;Point point;TouchAction action;
        internal VirtualTouch(int id,FrameworkElement target,PresentationSource source):base(id){this.target=target;root=(UIElement)source.RootVisual;SetActiveSource(source);}
        internal void Down(Point value)
        {
            if(!new Rect(0,0,target.ActualWidth,target.ActualHeight).Contains(value))throw new InvalidOperationException($"Touch fixture down {value} is outside {target.ActualWidth}x{target.ActualHeight}");
            point=target.TranslatePoint(value,root);action=TouchAction.Down;Activate();ReportDown();
        }
        internal void Move(Point value){point=target.TranslatePoint(value,root);action=TouchAction.Move;ReportMove();}
        internal void Up(){if(!IsActive)return;action=TouchAction.Up;ReportUp();Deactivate();}
        public override TouchPoint GetTouchPoint(IInputElement? relativeTo){var p=root.TranslatePoint(point,relativeTo as UIElement??root);return new TouchPoint(this,p,new Rect(p,new Size(2,2)),action);}
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo)=>new(){GetTouchPoint(relativeTo)};
    }
}
