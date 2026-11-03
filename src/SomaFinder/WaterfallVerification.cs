// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO.Compression;
using System.Windows.Threading;

namespace SomaFinder;

internal static class WaterfallVerification
{
    public static int Run(string[] args)
    {
        string report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        string root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-waterfall-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,DefaultDetailLayout=DetailLayout.Both};var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),prefs,true);
        var records=new List<object>();var timings=new List<object>();int failures=0;
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;
            void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
            async Task Test(string name,Func<Task> action){try{await action();Check(CloudSandbox.Errors.Count==0,string.Join('\n',CloudSandbox.Errors));records.Add(new{name,passed=true});}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString(),diagnostics=window==null?null:new{window.Width,window.ActualWidth,viewWidth=window.Waterfall.ActualWidth,window.Waterfall.Columns,window.Waterfall.TileWidth,window.Waterfall.RealizedCount,window.Waterfall.RealizedImages,window.Waterfall.ActiveTouches,window.FileInfoText,selected=window.SelectedHit?.Name,details=window.VerificationDetails.Mode.ToString()}});}}
            async Task Wait(Func<bool> ready,int ms=15000){var sw=Stopwatch.StartNew();while(!ready()&&sw.ElapsedMilliseconds<ms)await Task.Delay(40);Check(ready(),"Timed out waiting for UI result");}
            Hit Hit(string file,string snippet="",string mode="lexical")=>new(){RootPath=root,Workspace="fixture",FullPath=Path.Combine(root,file),VirtualPath="soma://fixture/"+file,Snippet=snippet,MatchMode=mode};
            async Task<ResultThumbnail> Thumb(Hit hit,string query="")=>await ResultThumbnails.Get(hit,query,prefs,CancellationToken.None)??throw new Exception("No thumbnail reply");
            void SaveImage(BitmapSource bitmap,string name){using var output=File.Create(Path.Combine(Path.GetDirectoryName(report)!,name+".png"));output.Write(ThumbnailWorker.Png(bitmap));}
            void Screenshot(string name){window!.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);SaveImage(bitmap,name);}
            try
            {
                prefs.Volumes[root]=Native.Identify(root)??throw new Exception("Cloud volume identity unavailable");
                File.WriteAllText(root+"/notes.txt",string.Join('\n',Enumerable.Range(1,80).Select(i=>i==55?"distinct evidence about emerald sea lighthouse":$"Unrelated line {i}")));
                Pdf(root+"/pages.pdf",new[]{"Introduction to the document","distinct evidence about emerald sea lighthouse","Unrelated appendix"});
                File.WriteAllText(root+"/broken.pdf","not a pdf");
                var pixels=new byte[320*480*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=180;pixels[i+1]=130;pixels[i+2]=40;pixels[i+3]=255;}var image=BitmapSource.Create(320,480,96,96,PixelFormats.Bgra32,null,pixels,320*4);File.WriteAllBytes(root+"/portrait.png",ThumbnailWorker.Png(image));
                Zip(root+"/document.docx",new(){["word/document.xml"]="<w:document xmlns:w='w'><w:body><w:p><w:r><w:t>Introduction</w:t></w:r></w:p><w:p><w:r><w:t>distinct evidence about emerald sea lighthouse</w:t></w:r></w:p></w:body></w:document>"});
                Zip(root+"/slides.pptx",new(){["ppt/presentation.xml"]="<p:presentation xmlns:p='p' xmlns:r='r'><p:sldIdLst><p:sldId id='5' r:id='second'/><p:sldId id='9' r:id='first'/></p:sldIdLst></p:presentation>",["ppt/_rels/presentation.xml.rels"]="<Relationships><Relationship Id='first' Target='slides/slide1.xml'/><Relationship Id='second' Target='slides/slide2.xml'/></Relationships>",["ppt/slides/slide1.xml"]="<s><p><t>distinct evidence about emerald sea lighthouse</t></p></s>",["ppt/slides/slide2.xml"]="<s><p><t>Unrelated cover slide</t></p></s>"});
                Zip(root+"/sheet.xlsx",new(){["xl/workbook.xml"]="<workbook xmlns:r='r'><sheets><sheet name='Travel' r:id='sheet'/></sheets></workbook>",["xl/_rels/workbook.xml.rels"]="<Relationships><Relationship Id='sheet' Target='worksheets/sheet1.xml'/></Relationships>",["xl/sharedStrings.xml"]="<sst><si><t>distinct evidence about emerald sea lighthouse</t></si></sst>",["xl/worksheets/sheet1.xml"]="<worksheet><sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>Heading</t></is></c></row><row r='20'><c r='B20' t='s'><v>0</v></c></row></sheetData></worksheet>"});
                File.WriteAllText(root+"/caption.srt","1\n00:00:00,200 --> 00:00:01,200\nUnrelated opening words\n\n2\n00:00:02,200 --> 00:00:03,800\ndistinct evidence about emerald sea lighthouse\n\n3\n00:00:04,200 --> 00:00:05,800\nUnrelated closing words\n");
                await Task.Run(()=>VideoThumbnail.Run("ffmpeg",new[]{"-v","error","-y","-f","lavfi","-i","color=c=red:s=320x180:r=15:d=2","-f","lavfi","-i","color=c=green:s=320x180:r=15:d=2","-f","lavfi","-i","color=c=blue:s=320x180:r=15:d=2","-i",root+"/caption.srt","-filter_complex","[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]","-map","[v]","-map","3:s:0","-c:v","libx264","-threads","1","-c:s","srt",root+"/timed.mkv"},10000,20000));
                await Task.Run(()=>VideoThumbnail.Run("ffmpeg",new[]{"-v","error","-y","-i",root+"/timed.mkv","-map","0:v:0","-c","copy",root+"/untimed.mkv"},10000,10000));File.Copy(root+"/caption.srt",root+"/untimed.srt");

                await Test("PDF chooses real matching page and falls back to actual first page",async()=>
                {
                    var value=await Thumb(Hit("pages.pdf","distinct evidence about emerald sea lighthouse","vector"),"coastal trip");Check(value.Image!=null&&value.Page==2&&value.Caption.Contains("命中"),"Wrong semantic evidence page");SaveImage(value.Image!,"pdf-match-page-2");
                    var first=await Thumb(Hit("pages.pdf","no such evidence","vector"),"no match");Check(first.Image!=null&&first.Page==1&&first.Caption=="首页","No-match PDF did not render page one");SaveImage(first.Image!,"pdf-first-page");
                });
                await Test("text and office formats show source excerpts with honest location labels",async()=>
                {
                    foreach(var file in new[]{"notes.txt","document.docx","slides.pptx","sheet.xlsx"})
                    {
                        var value=await Thumb(Hit(file,"distinct evidence about emerald sea lighthouse","vector"),"coastal trip");Check(value.Image!=null&&value.Text.Contains("emerald sea"),file+" lost matching excerpt");Check(value.Caption.Contains("内容片段")||file=="notes.txt",file+" pretends to be original page");
                        if(file=="slides.pptx")Check(value.Page==2,"Slide order ignored presentation relationships");if(file=="sheet.xlsx")Check(value.Text.Contains("B20"),"Spreadsheet cell context missing");SaveImage(value.Image!,"document-"+Path.GetExtension(file).TrimStart('.'));
                    }
                    var opening=await Thumb(Hit("notes.txt"),"");Check(opening.Text.StartsWith("Unrelated line 1")&&opening.Caption=="开头内容","Text fallback not beginning");
                });
                await Test("embedded unique subtitle evidence produces the correct timestamp and actual green frame",async()=>
                {
                    var value=await Thumb(Hit("timed.mkv","distinct evidence about emerald sea lighthouse","vector"),"coastal trip");Check(value.VerifiedTime&&value.Seconds is >2.2 and <3.8&&value.Image!=null,"Missing verified subtitle time");var color=Center(value.Image!);Check(color.G>color.R+40&&color.G>color.B+40,"Thumbnail is not from green matching scene");SaveImage(value.Image!,"video-match-00-03");
                });
                await Test("external same-name subtitles and bare keywords cannot invent video timing",async()=>
                {
                    var external=await Thumb(Hit("untimed.mkv","distinct evidence about emerald sea lighthouse"),"emerald");Check(!external.VerifiedTime&&external.Seconds==null&&external.Image!=null&&external.Caption=="视频封面","External same-name subtitle trusted");
                    var shortMatch=await Thumb(Hit("timed.mkv","sea"),"sea");Check(!shortMatch.VerifiedTime&&shortMatch.Seconds==null,"Single keyword claimed exact time");
                    var cues=new[]{new ContentLocation.Cue(0,1,"distinct evidence about emerald sea lighthouse"),new ContentLocation.Cue(3,4,"distinct evidence about emerald sea lighthouse")};Check(ContentLocation.LocateCue(cues,cues[0].Text)==null,"Repeated evidence considered unique");
                    bool invalid=false;try{ContentLocation.ParseVtt("00:00:01.000 --> 00:00:25.000\nmatched text",6);}catch(InvalidDataException){invalid=true;}Check(invalid,"Out-of-duration cue accepted");
                    Check(ContentLocation.LocateCue(new[]{new ContentLocation.Cue(0,1,"distinct evidence about"),new ContentLocation.Cue(20,21,"emerald sea lighthouse")},"distinct evidence about emerald sea lighthouse")==null,"Unrelated distant cues concatenated");
                });
                await Test("cache reuse, query changes and modified files produce correct independent thumbnails",async()=>
                {
                    var h=Hit("notes.txt","distinct evidence about emerald sea lighthouse");int before=ResultThumbnails.WorkerStarts;await Thumb(h,"unique cache query");int once=ResultThumbnails.WorkerStarts;await Thumb(h,"unique cache query");Check(once>before&&ResultThumbnails.WorkerStarts==once,"Cache miss on identical request");
                    File.AppendAllText(h.FullPath,"\nUpdated tail");await Thumb(h,"unique cache query");Check(ResultThumbnails.WorkerStarts>once,"Modified file reused stale cache");
                    prefs.Volumes[root]=prefs.Volumes[root] with{Volume="different-volume"};try{Check(await ResultThumbnails.Get(h,"unique cache query",prefs,CancellationToken.None)==null,"Offline/swapped media returned cached thumbnail");}finally{prefs.Volumes[root]=Native.Identify(root)!;}
                });
                await Test("worker failure timeout and cancellation leave application and later previews usable",async()=>
                {
                    var broken=await Thumb(Hit("broken.pdf"));Check(broken.Image==null,"Corrupt PDF fabricated a page");
                    var timeout=await ResultThumbnails.Worker(new(root+"/pages.pdf","","","",null),CancellationToken.None,1);Check(timeout.Png==null,"One-ms deadline ignored");
                    using var cancel=new CancellationTokenSource();cancel.Cancel();bool caught=false;try{await ResultThumbnails.Get(Hit("pages.pdf"),"cancel",prefs,cancel.Token);}catch(OperationCanceledException){caught=true;}Check(caught,"Cancelled request continued");
                    var good=await Thumb(Hit("portrait.png"));Check(good.Image!=null&&good.Image.PixelHeight>good.Image.PixelWidth&&Math.Max(good.Image.PixelWidth,good.Image.PixelHeight)<=640,"Image aspect ratio or decode budget lost after worker failure");Check(ResultThumbnails.ActiveWorkers==0,"Worker remained after completion");
                });

                app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();window.EnableCloudLayoutFixture();window.Width=1400;window.Height=800;window.Left=0;window.Top=0;
                var scope=new Dictionary<string,bool>{{root,true}};var real=new[]{Hit("portrait.png"),Hit("timed.mkv","distinct evidence about emerald sea lighthouse","vector"),Hit("pages.pdf","distinct evidence about emerald sea lighthouse"),Hit("document.docx","distinct evidence about emerald sea lighthouse"),Hit("notes.txt","distinct evidence about emerald sea lighthouse")};
                var hits=real.Concat(Enumerable.Range(0,4995).Select(i=>Hit("not-present-"+i+".png"))).ToArray();
                window.SeedResults(hits,scope);window.SetResultQuery("emerald sea");window.SetView(2);await Task.Delay(250);
                await Test("waterfall fills results width with nonoverlapping varied-height virtualized cards",async()=>
                {
                    await Wait(()=>window.Waterfall.RealizedCount>0);var view=window.Waterfall;Check(view.Bounds.Count==5000&&view.RealizedCount<120,"Result controls not virtualized");
                    await Wait(()=>view.RealizedImages>=2);var bounds=view.Bounds.Take(30).ToArray();Check(bounds.Select(b=>b.Height).Distinct().Count()>1,"Not a masonry layout");Check(bounds.All(b=>b.Left>=0&&b.Right<=view.ActualWidth+1),"Masonry overflows viewport");
                    for(int a=0;a<bounds.Length;a++)for(int b=a+1;b<bounds.Length;b++)Check(!bounds[a].IntersectsWith(bounds[b]),"Cards overlap");Screenshot("waterfall-dark");
                });
                await Test("three views preserve selection sorting and results without querying Soma",async()=>
                {
                    long requests=app.Backend.RequestCount;window.Waterfall.Select(real[0]);foreach(int view in new[]{0,1,2,1,0,2}){window.SetView(view);await Task.Delay(80);Check(ReferenceEquals(window.SelectedHit,real[0]),"View switch lost selected file");}
                    window.SetSort(1);await Task.Delay(80);Check(window.DisplayedHits.Count==5000&&ReferenceEquals(window.SelectedHit,real[0]),"Sorting changed result or selection");Check(app.Backend.RequestCount==requests,"View operation made backend query");
                });
                await Test("pinch changes column count and one-finger pan scrolls without selecting or searching",async()=>
                {
                    window.SetView(2);window.Waterfall.Zoom(160);await Task.Delay(80);var view=window.Waterfall;int columns=view.Columns;var selected=window.SelectedHit;long requests=app.Backend.RequestCount;
                    var a=new Finger(901,view);var b=new Finger(902,view);
                    try{a.Down(new Point(40,140));b.Down(new Point(140,140));b.Move(new Point(270,140));await Task.Delay(80);Check(view.TileWidth>300&&view.Columns<columns,"Pinch-out did not increase tiles/reduce columns");b.Move(new Point(105,140));await Task.Delay(80);Check(view.TileWidth<200,"Pinch-in did not shrink tiles");}finally{a.Up();b.Up();}
                    view.ScrollTo(300);await Task.Delay(70);double before=view.Offset;var pan=new Finger(903,view);try{pan.Down(new Point(45,200));pan.Move(new Point(45,120));await Task.Delay(80);Check(view.Offset>before+60,"Single-finger pan failed");}finally{pan.Up();}
                    Check(view.ActiveTouches==0&&ReferenceEquals(selected,window.SelectedHit)&&app.Backend.RequestCount==requests,"Gesture changed selection/search or retained capture");
                });
                await Test("Win32 native injected pinch and swipe reach waterfall through actual OS input routing",async()=>await WaterfallNativeInput.Verify(window));
                await Test("resizing, themes and floating preview keep waterfall in available results area",async()=>
                {
                    foreach(double width in new[]{1800d,900,600,1400}){window.MinWidth=320;window.Width=width;await Task.Delay(100);Check(window.Waterfall.Bounds.Take(100).All(b=>b.Right<=window.Waterfall.ActualWidth+1),"Tile overflow after resize "+width);}
                    prefs.SearchLayout.Theme="Light";app.ApplyAppearance();await Task.Delay(80);window.Waterfall.ScrollTo(0);Screenshot("waterfall-light");prefs.SearchLayout.Theme="Dark";app.ApplyAppearance();
                });
                await Test("verified video time appears in summary and same-time image appears in content preview",async()=>
                {
                    window.SeedResults(real,scope);window.SetResultQuery("coastal trip");window.SetView(2);window.Waterfall.Select(real[1],true);if(!window.VerificationDetails.RightExpanded)window.VerificationDetails.ToggleRight();await Wait(()=>window.FileInfoText.Contains("匹配字幕片段 · 00:03"));window.VerificationDetails.SelectTab(DetailKind.Preview);
                    await Wait(()=>CloudVerification.Descendants<ImagePreviewView>(window.PreviewSurface).Any());var video=CloudVerification.Descendants<ImagePreviewView>(window.PreviewSurface).Single();var color=Center(video.Bitmap);Check(color.G>color.R+40&&color.G>color.B+40,"Preview differs from matching frame");
                    window.VerificationDetails.Float(DetailKind.Preview);await Task.Delay(80);Check(ReferenceEquals(video,CloudVerification.Descendants<ImagePreviewView>(window.VerificationDetails.FloatingWindow(DetailKind.Preview)!).Single()),"Floating replaced matching preview");window.VerificationDetails.Dock(DetailKind.Preview);Screenshot("video-summary-and-preview");
                    window.VerificationSelect(0);await Task.Delay(80);Check(!window.FileInfoText.Contains("匹配字幕片段"),"Video time leaked to another file");
                });
                await Test("background decoding keeps dispatcher responsive and obeys worker and memory cache bounds",async()=>
                {
                    int ticks=0,inputTicks=0;double maxGap=0,maxInputLatency=0,maxBackgroundGap=0;var sw=Stopwatch.StartNew();double prior=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};timer.Tick+=(_,_)=>{double now=sw.Elapsed.TotalMilliseconds;maxGap=Math.Max(maxGap,now-prior);prior=now;ticks++;};
                    var operations=new Dictionary<DispatcherOperation,(double Started,string Callback)>();var slowOperations=new List<object>();
                    var callbackField=typeof(DispatcherOperation).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).FirstOrDefault(f=>typeof(Delegate).IsAssignableFrom(f.FieldType));
                    DispatcherHookEventHandler started=(_,e)=>{var callback=callbackField?.GetValue(e.Operation) as Delegate;operations[e.Operation]=(sw.Elapsed.TotalMilliseconds,callback==null?e.Operation.ToString()!:callback.Method.DeclaringType?.FullName+"."+callback.Method.Name);};
                    DispatcherHookEventHandler ended=(_,e)=>{if(operations.Remove(e.Operation,out var item)){double ms=sw.Elapsed.TotalMilliseconds-item.Started;if(ms>80)slowOperations.Add(new{ms,priority=e.Operation.Priority.ToString(),callback=item.Callback});}};
                    app.Dispatcher.Hooks.OperationStarted+=started;app.Dispatcher.Hooks.OperationCompleted+=ended;
                    using var probeStop=new CancellationTokenSource();
                    var background=Task.Run(async()=>{double last=sw.Elapsed.TotalMilliseconds;try{while(!probeStop.IsCancellationRequested){await Task.Delay(25,probeStop.Token);double now=sw.Elapsed.TotalMilliseconds;maxBackgroundGap=Math.Max(maxBackgroundGap,now-last);last=now;double posted=now;await app.Dispatcher.InvokeAsync(()=>{maxInputLatency=Math.Max(maxInputLatency,sw.Elapsed.TotalMilliseconds-posted);inputTicks++;},DispatcherPriority.Input,probeStop.Token);}}catch(OperationCanceledException){}});
                    var pauseBefore=GC.GetTotalPauseDuration();timer.Start();
                    try{await Task.WhenAll(Enumerable.Range(0,8).Select(i=>Thumb(Hit("pages.pdf","distinct evidence about emerald sea lighthouse"),"parallel request "+i)));}
                    finally{timer.Stop();probeStop.Cancel();await background;app.Dispatcher.Hooks.OperationStarted-=started;app.Dispatcher.Hooks.OperationCompleted-=ended;}
                    timings.Add(new{scenario="8 uncached PDF previews",elapsedMs=sw.ElapsedMilliseconds,ticks,maxDispatcherGapMs=maxGap,inputTicks,maxInputLatencyMs=maxInputLatency,maxBackgroundGapMs=maxBackgroundGap,gcPauseMs=(GC.GetTotalPauseDuration()-pauseBefore).TotalMilliseconds,slowOperations,peakWorkers=ResultThumbnails.PeakWorkers,cacheBytes=ResultThumbnails.CacheBytes});
                    Check(ticks>=10&&maxGap<750,"UI dispatcher stalled during decoding: "+maxGap);Check(ResultThumbnails.PeakWorkers<=2&&ResultThumbnails.ActiveWorkers==0,"Unbounded or leaked workers");Check(ResultThumbnails.CacheBytes<=48*1024*1024,"Thumbnail cache budget exceeded");
                });
                // This fixture uses genuine published workers and decoders, synthetic source files,
                // and routed WPF contacts. It does not claim physical touchscreen acceptance.
            }
            catch(Exception ex){failures++;records.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally
            {
                window?.Close();app.Backend.Dispose();Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests=records,timings,scope="Only waterfall and content-aware thumbnails. Real PDFium/FFmpeg and published subprocesses, synthetic PDF/Office/video files, WPF gesture injection; no physical touch, no local files or Soma indexing."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);
            }
        };
        return app.Run();
    }
    static (byte R,byte G,byte B) Center(BitmapSource bitmap){var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var buffer=new byte[4];converted.CopyPixels(new Int32Rect(converted.PixelWidth/2,converted.PixelHeight/2,1,1),buffer,4,0);return(buffer[2],buffer[1],buffer[0]);}
    static void Zip(string path,Dictionary<string,string> files){using var archive=ZipFile.Open(path,ZipArchiveMode.Create);foreach(var pair in files){using var writer=new StreamWriter(archive.CreateEntry(pair.Key).Open());writer.Write(pair.Value);}}
    static void Pdf(string path,string[] pages)
    {
        var objects=new List<string>{"<< /Type /Catalog /Pages 2 0 R >>","", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"};var kids=new List<string>();
        foreach(string text in pages){int page=objects.Count+1,content=page+1;kids.Add(page+" 0 R");objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 480 640] /Resources << /Font << /F1 3 0 R >> >> /Contents {content} 0 R >>");string stream=$"BT /F1 14 Tf 25 550 Td ({text}) Tj ET";objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream");}
        objects[1]=$"<< /Type /Pages /Count {pages.Length} /Kids [{string.Join(' ',kids)}] >>";var result=new StringBuilder("%PDF-1.4\n");var offsets=new List<int>{0};for(int i=0;i<objects.Count;i++){offsets.Add(Encoding.ASCII.GetByteCount(result.ToString()));result.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n");}int xref=Encoding.ASCII.GetByteCount(result.ToString());result.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");foreach(int at in offsets.Skip(1))result.Append(at.ToString("D10")+" 00000 n \n");result.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");File.WriteAllText(path,result.ToString(),Encoding.ASCII);
    }
    sealed class Finger:TouchDevice
    {
        readonly UIElement target,root;Point point;bool active;TouchAction action;
        internal Finger(int id,UIElement target):base(id){this.target=target;var source=PresentationSource.FromVisual(target)!;root=(UIElement)source.RootVisual;SetActiveSource(source);}
        internal void Down(Point p){point=target.TranslatePoint(p,root);action=TouchAction.Down;Activate();active=true;ReportDown();}
        internal void Move(Point p){point=target.TranslatePoint(p,root);action=TouchAction.Move;ReportMove();}
        internal void Up(){if(!active)return;action=TouchAction.Up;ReportUp();Deactivate();active=false;}
        public override TouchPoint GetTouchPoint(IInputElement? relativeTo){Point p=root.TranslatePoint(point,relativeTo as UIElement??root);return new(this,p,new Rect(p,new Size(2,2)),action);}
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo)=>new(){GetTouchPoint(relativeTo)};
    }
}
