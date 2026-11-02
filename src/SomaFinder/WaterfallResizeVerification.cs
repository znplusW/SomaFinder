// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SomaFinder;

// Published WPF controls, synthetic files and routed divider/touch input only.
// Deliberately excludes search, indexing and unrelated media decoder regression.
internal static class WaterfallResizeVerification
{
    public static int Run(string[] args)
    {
        var report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-waterfall-resize-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,DefaultDetailLayout=DetailLayout.Both};
        var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),prefs,true);
        var tests=new List<object>();var observations=new List<object>();int failures=0;
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;
            void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            async Task Test(string name,Func<Task> action){try{await action();Check(CloudSandbox.Errors.Count==0,string.Join('\n',CloudSandbox.Errors));tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString()});}}
            async Task Wait(Func<bool> condition){var watch=Stopwatch.StartNew();while(!condition()&&watch.ElapsedMilliseconds<15000)await Task.Delay(40);Check(condition(),"Timed out waiting for thumbnails");}
            Hit Hit(string name)=>new(){Workspace="fixture",RootPath=root,FullPath=root+"/"+name,VirtualPath="soma://fixture/"+name};
            void Fits(MasonryResults view)
            {
                Check(view.Bounds.All(r=>r.Left>=0&&r.Right<=view.ActualWidth+1),"Card extends past results pane: width="+view.ActualWidth+", right="+(view.Bounds.Count>0?view.Bounds.Max(r=>r.Right):0));
                var first=view.Bounds.Take(50).ToArray();for(int i=0;i<first.Length;i++)for(int j=i+1;j<first.Length;j++)Check(!first[i].IntersectsWith(first[j]),"Cards overlap");
                Check(view.RealizedCount<=120,"Virtualization limit exceeded");
            }
            async Task Quiet(MasonryResults view,string scenario)
            {
                await Task.Delay(180);var canvas=CloudVerification.Descendants<Canvas>(view).Single();var scroller=CloudVerification.Descendants<ScrollViewer>(view).Single();
                var geometry=view.Bounds.ToArray();var controls=canvas.Children.Cast<UIElement>().ToArray();double offset=view.Offset,width=scroller.ViewportWidth;int columns=view.Columns,events=0;
                ScrollChangedEventHandler changed=(_,e)=>{if(Math.Abs(e.ViewportWidthChange)>.1||Math.Abs(e.ExtentHeightChange)>.1)events++;};scroller.ScrollChanged+=changed;
                try{await Task.Delay(350);}finally{scroller.ScrollChanged-=changed;}
                observations.Add(new{scenario,viewWidth=view.ActualWidth,viewportWidth=scroller.ViewportWidth,columns=view.Columns,layoutChangesAfterSettling=events,offsetDrift=Math.Abs(view.Offset-offset),realized=view.RealizedCount});
                Check(events==0&&columns==view.Columns&&Math.Abs(width-scroller.ViewportWidth)<.1&&geometry.SequenceEqual(view.Bounds),"Layout continues changing after divider stopped: "+scenario);
                Check(Math.Abs(view.Offset-offset)<.1&&controls.SequenceEqual(canvas.Children.Cast<UIElement>()),"Scroll position or existing cards churn while idle: "+scenario);Fits(view);
            }
            void Screenshot(string name)
            {
                window!.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(report)!,name+".png"),ThumbnailWorker.Png(bitmap));
            }
            try
            {
                prefs.Volumes[root]=Native.Identify(root)!;
                await Test("overflow boundary settles without scrollbar width feedback near a three-column transition",async()=>
                {
                    var view=new MasonryResults();var host=new Window{Content=view,SizeToContent=SizeToContent.WidthAndHeight,WindowStartupLocation=WindowStartupLocation.Manual,Left=0,Top=0,ThemeMode=ThemeMode.Light};
                    double gutter=SystemParameters.VerticalScrollBarWidth;double width=8+3*(200+8)+gutter+2;view.Width=width;
                    // At this width both scrollbar states have three columns. With Auto,
                    // the larger tiles need a scrollbar, but the smaller tiles do not.
                    double extent(double w,int rows)=>8+rows*((w-8*4)/3/1.5+62);
                    view.Height=(extent(width,2)+extent(width-gutter,2))/2;
                    var scroller=CloudVerification.Descendants<ScrollViewer>(view).Single();int changes=0;bool rescued=false;
                    ScrollChangedEventHandler guard=(_,e)=>{if(Math.Abs(e.ViewportWidthChange)>.1&&++changes>96){rescued=true;scroller.VerticalScrollBarVisibility=ScrollBarVisibility.Visible;}};
                    scroller.ScrollChanged+=guard;
                    try
                    {
                        view.SetItems(Enumerable.Range(0,4).Select(i=>Hit("overflow-"+i+".png")).ToArray(),"",prefs);host.Show();await Task.Delay(700);
                        observations.Add(new{scenario="overflow feedback probe",width,view.Height,widthChanges=changes,emergencyLoopBreak=rescued});
                        Check(!rescued&&changes<=6,"Repeated scrollbar visibility/viewport changes: "+changes);
                        await Quiet(view,"three columns near overflow");
                        foreach(int count in new[]{1,7,10,0,4}){view.SetItems(Enumerable.Range(0,count).Select(i=>Hit("overflow-"+i+".png")).ToArray(),"",prefs);await Quiet(view,"overflow item count "+count);}
                    }
                    finally{scroller.ScrollChanged-=guard;host.Close();}
                });

                // Synthetic images arrive asynchronously with portrait/landscape ratios.
                for(int i=0;i<12;i++)
                {
                    int w=i%2==0?240:120,h=i%2==0?120:240;var pixels=new byte[w*h*4];
                    for(int p=0;p<pixels.Length;p+=4){pixels[p]=(byte)(60+i*12);pixels[p+1]=(byte)(170-i*8);pixels[p+2]=70;pixels[p+3]=255;}
                    File.WriteAllBytes(root+"/tile-"+i+".png",ThumbnailWorker.Png(BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,w*4)));
                }
                app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();window.EnableCloudLayoutFixture();window.MinWidth=2400;window.Width=2400;window.Height=780;window.Left=window.Top=0;
                var hits=Enumerable.Range(0,5000).Select(i=>Hit(i<12?"tile-"+i+".png":"missing-"+i+".png")).ToArray();var scope=new Dictionary<string,bool>{{root,true}};
                window.SeedResults(hits,scope);window.SetView(2);window.Waterfall.Select(hits[0]);await Task.Delay(100);
                var masonry=window.Waterfall;var grid=window.PanelGrid;var divider=grid.Children.OfType<AdjacentDivider>().Single(d=>Grid.GetColumn(d)==3);
                long requests=app.Backend.RequestCount;
                void Begin()=>divider.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});
                void Drag(double width)=>divider.RaiseEvent(new DragDeltaEventArgs(width-grid.ColumnDefinitions[2].ActualWidth,0){RoutedEvent=Thumb.DragDeltaEvent});
                async Task End(){divider.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});await Task.Delay(120);}
                await Test("routed separator dragging crosses column boundaries during asynchronous thumbnail loading",async()=>
                {
                    int ticks=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};timer.Tick+=(_,_)=>ticks++;timer.Start();
                    Begin();double tree=grid.ColumnDefinitions[0].ActualWidth,right=grid.ColumnDefinitions[6].ActualWidth,total=grid.ColumnDefinitions[2].ActualWidth+grid.ColumnDefinitions[4].ActualWidth;var columns=new HashSet<int>();
                    try
                    {
                        foreach(double target in new[]{850d,655,650,645,640,635,630,625,445,440,435,430,425,420,415,630,635,640,645,650,655,850})
                        {
                            Drag(target);await Task.Delay(40);columns.Add(masonry.Columns);Fits(masonry);
                            Check(Math.Abs(tree-grid.ColumnDefinitions[0].ActualWidth)<1&&Math.Abs(right-grid.ColumnDefinitions[6].ActualWidth)<1,"Dragging results divider resized another pane");
                            Check(Math.Abs(total-grid.ColumnDefinitions[2].ActualWidth-grid.ColumnDefinitions[4].ActualWidth)<1,"Adjacent pair width not conserved");
                        }
                        await Wait(()=>masonry.RealizedImages>=4);
                        // A newly decoded aspect ratio is a legitimate layout change.
                        // Observe idle stability only after those actual loads finish.
                        await Task.WhenAll(hits.Take(12).Select(h=>ResultThumbnails.Get(h,"",prefs,CancellationToken.None)));
                        await Quiet(masonry,"divider released at wide width");
                        Check(columns.Count>=3&&ticks>=10,"Did not exercise multiple column transitions or dispatcher stopped");
                    }
                    finally{timer.Stop();await End();}
                    observations.Add(new{scenario="thumbnail arrival during drag",distinctColumnCounts=columns.Order().ToArray(),dispatcherTicks=ticks,peakWorkers=ResultThumbnails.PeakWorkers});
                    Check(ResultThumbnails.PeakWorkers<=2&&ReferenceEquals(window.SelectedHit,hits[0])&&app.Backend.RequestCount==requests,"Resize changed selection, queried Soma or exceeded decoder bound");Screenshot("waterfall-resize-light");
                });
                await Test("small divider jitter at a column threshold cannot repeatedly flip the number of columns",async()=>
                {
                    Begin();
                    try
                    {
                        foreach(int column in new[]{2,3,4})
                        {
                            var scroller=CloudVerification.Descendants<ScrollViewer>(masonry).Single();double gutter=masonry.ActualWidth-scroller.ViewportWidth;
                            double boundary=8+column*(masonry.TileWidth+8)+gutter;Drag(boundary-30);await Task.Delay(70);
                            var counts=new List<int>();
                            for(int i=0;i<14;i++){Drag(boundary+(i%2==0?1:-1));await Task.Delay(35);counts.Add(masonry.Columns);}
                            int changes=counts.Zip(counts.Skip(1),(a,b)=>a==b?0:1).Sum();
                            observations.Add(new{scenario="subpixel and hand jitter at column threshold",column,boundary,columnChanges=changes,counts});
                            Check(changes==0,"Small divider jitter repeatedly changes column count: "+string.Join(',',counts));
                            Drag(boundary+30);await Task.Delay(70);Check(masonry.Columns==column,"Intentional widening did not add a column");
                            counts.Clear();for(int i=0;i<14;i++){Drag(boundary+(i%2==0?-1:1));await Task.Delay(35);counts.Add(masonry.Columns);}
                            Check(counts.All(c=>c==column),"Small reverse jitter repeatedly removes a column");
                            Drag(boundary-30);await Task.Delay(70);Check(masonry.Columns==column-1,"Intentional narrowing did not remove a column");
                        }
                    }
                    finally{await End();}
                });
                await Test("stationary divider near every column boundary leaves loaded cards and scroll position unchanged",async()=>
                {
                    await Wait(()=>ResultThumbnails.ActiveWorkers==0);masonry.ScrollTo(0);await Task.Delay(60);Begin();
                    try
                    {
                        foreach(double width in new[]{419d,425,435,441,627,633,643,649,835,841,851,857}){Drag(width);await Quiet(masonry,"divider width "+width);}
                    }
                    finally{await End();}
                    foreach(string theme in new[]{"Dark","Light","Dark"})
                    {
                        prefs.SearchLayout.Theme=theme;app.ApplyAppearance();await Task.Delay(100);
                        await Wait(()=>masonry.RealizedCount>0&&masonry.RealizedImages>=4);
                        await Quiet(masonry,"theme reload "+theme);
                        Check(masonry.IsVisible&&masonry.RealizedImages>=4,"Theme reload left visible waterfall empty");
                        Check(ReferenceEquals(window.SelectedHit,hits[0]),"Theme reload changed selection");
                    }
                    Screenshot("waterfall-resize-dark");
                });
                await Test("scrolled results retain the visible anchor and selection when column count changes",async()=>
                {
                    Begin();Drag(850);await Task.Delay(100);masonry.Select(hits[300]);masonry.ScrollTo(16000);await Task.Delay(100);
                    try
                    {
                        foreach(double width in new[]{640d,430,850})
                        {
                            double y=masonry.Offset;int index=Enumerable.Range(0,masonry.Bounds.Count).First(i=>masonry.Bounds[i].Bottom>=y);var before=masonry.Bounds[index];double fraction=(y-before.Top)/before.Height;
                            Drag(width);await Task.Delay(140);var after=masonry.Bounds[index];double expected=after.Top+fraction*after.Height;
                            Check(Math.Abs(masonry.Offset-expected)<2,"Visible anchor jumped after changing columns");Check(masonry.Offset>1000&&ReferenceEquals(window.SelectedHit,hits[300]),"Resize reset scrolling or selection");
                            await Quiet(masonry,"scrolled divider width "+width);
                        }
                    }
                    finally{await End();}
                });
                await Test("pinch and swipe still work after resize and hidden views do not restart searches",async()=>
                {
                    double initial=masonry.TileWidth;var a=new Finger(711,masonry);var b=new Finger(712,masonry);
                    try{a.Down(new Point(40,150));b.Down(new Point(140,150));b.Move(new Point(220,150));await Task.Delay(90);Check(masonry.TileWidth>initial*1.5,"Pinch did not resize thumbnails");}finally{b.Up();a.Up();}
                    var pan=new Finger(713,masonry);double offset=masonry.Offset;
                    try{pan.Down(new Point(50,220));pan.Move(new Point(50,140));await Task.Delay(100);Check(masonry.Offset>offset+60,"Swipe did not scroll");}finally{pan.Up();}
                    Check(masonry.ActiveTouches==0,"Touch capture retained");masonry.Zoom(200);await Quiet(masonry,"pinch restored");
                    foreach(int v in new[]{0,1,2}){window.SetView(v);await Task.Delay(80);Check(ReferenceEquals(window.SelectedHit,hits[300]),"View switch changed selection");}
                    await Quiet(masonry,"return to waterfall");Check(app.Backend.RequestCount==requests,"Layout issued a Soma request");
                });
            }
            catch(Exception ex){failures++;tests.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally
            {
                if(window!=null){window.SkipExitOnce=true;window.Close();}app.Backend.Dispose();
                Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,observations,scope="Only waterfall resize stability and directly affected scrolling/zoom/virtualization. Published WPF controls, synthetic images, routed divider and touch input; no physical mouse/touch/monitor acceptance, no Soma or indexing."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);
            }
        };
        return app.Run();
    }
    sealed class Finger:TouchDevice
    {
        readonly UIElement target,root;Point point;bool active;TouchAction action;
        internal Finger(int id,UIElement target):base(id){this.target=target;var source=PresentationSource.FromVisual(target)!;root=(UIElement)source.RootVisual;SetActiveSource(source);}
        internal void Down(Point p){point=target.TranslatePoint(p,root);action=TouchAction.Down;Activate();active=true;ReportDown();}
        internal void Move(Point p){point=target.TranslatePoint(p,root);action=TouchAction.Move;ReportMove();}
        internal void Up(){if(!active)return;action=TouchAction.Up;ReportUp();Deactivate();active=false;}
        public override TouchPoint GetTouchPoint(IInputElement? relativeTo){var p=root.TranslatePoint(point,relativeTo as UIElement??root);return new(this,p,new Rect(p,new Size(2,2)),action);}
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo)=>new(){GetTouchPoint(relativeTo)};
    }
}
