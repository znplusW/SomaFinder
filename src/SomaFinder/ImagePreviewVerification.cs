// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

internal static class ImagePreviewVerification
{
    public static int Run(string[] args)
    {
        string report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-image-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var records=new List<object>();int failures=0;
        app.Startup+=async(_,_)=>
        {
            void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
            async Task Test(string name,Func<Task> action){try{await action();records.Add(new{name,passed=true});}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});}}
            void Screenshot(FrameworkElement target,string name)
            {
                target.UpdateLayout();var raster=new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth),(int)Math.Ceiling(target.ActualHeight),96,96,PixelFormats.Pbgra32);
                var background=new DrawingVisual();using(var dc=background.RenderOpen())dc.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,target.ActualWidth,target.ActualHeight));
                raster.Render(background);raster.Render(target);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(raster));Directory.CreateDirectory(Path.GetDirectoryName(report)!);using var output=File.Create(Path.Combine(Path.GetDirectoryName(report)!,name));png.Save(output);
            }
            var gridPixels=new byte[320*240*4];for(int y=0;y<240;y++)for(int x=0;x<320;x++){int at=(y*320+x)*4;bool line=x%40<2||y%40<2;gridPixels[at]=line?(byte)255:(byte)(80+x/2);gridPixels[at+1]=line?(byte)255:(byte)(40+y/2);gridPixels[at+2]=line?(byte)255:(byte)(30+x/2);gridPixels[at+3]=255;}
            var bitmap=BitmapSource.Create(320,240,96,96,PixelFormats.Bgra32,null,gridPixels,320*4);bitmap.Freeze();
            var view=new ImagePreviewView(bitmap,1600,1200);var host=new Window{Content=view,Width=700,Height=540,ShowInTaskbar=false};
            host.Show();await Task.Delay(100);var surface=view.TouchSurface;
            VirtualTouch? first=null,second=null;
            try
            {
                await Test("EXIF orientations 1 through 8 normalize preview and worker thumbnail pixels",async()=>
                {
                    Directory.CreateDirectory(root);
                    var colors=new[]{Colors.Red,Colors.Lime,Colors.Blue,Colors.Yellow};
                    var pixels=new byte[2400*1600*3];
                    for(int y=0;y<1600;y++)for(int x=0;x<2400;x++)
                    {var color=colors[(y>=800?2:0)+(x>=1200?1:0)];int at=(y*2400+x)*3;pixels[at]=color.B;pixels[at+1]=color.G;pixels[at+2]=color.R;}
                    var source=BitmapSource.Create(2400,1600,96,96,PixelFormats.Bgr24,null,pixels,2400*3);
                    var encoder=new JpegBitmapEncoder{QualityLevel=95};encoder.Frames.Add(BitmapFrame.Create(source));using var jpeg=new MemoryStream();encoder.Save(jpeg);var encoded=jpeg.ToArray();
                    var corners=new[]{new[]{0,1,2,3},new[]{1,0,3,2},new[]{3,2,1,0},new[]{2,3,0,1},new[]{0,2,1,3},new[]{2,0,3,1},new[]{3,1,2,0},new[]{1,3,0,2}};
                    void VerifyPixels(BitmapSource image,int orientation)
                    {
                        var rgb=new FormatConvertedBitmap(image,PixelFormats.Bgr32,null,0);
                        for(int i=0;i<4;i++)
                        {
                            int x=image.PixelWidth*(i%2==0?1:3)/4,y=image.PixelHeight*(i<2?1:3)/4;var sample=new byte[4];rgb.CopyPixels(new Int32Rect(x,y,1,1),sample,4,0);
                            var expected=colors[corners[orientation-1][i]];
                            Check(Math.Abs(sample[0]-expected.B)<20&&Math.Abs(sample[1]-expected.G)<20&&Math.Abs(sample[2]-expected.R)<20,$"Orientation {orientation}, corner {i} incorrect");
                        }
                    }
                    for(int orientation=1;orientation<=8;orientation++)
                    {
                        // Inject a minimal EXIF IFD into real JPEG bytes; no encoder auto-rotation.
                        var exif=new byte[]{255,225,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,(byte)orientation,0,0,0,0,0,0,0};
                        string file=Path.Combine(root,$"orientation-{orientation}.jpg");using(var output=File.Create(file)){output.Write(encoded,0,2);output.Write(exif);output.Write(encoded,2,encoded.Length-2);}
                        var real=await ImagePreviewView.LoadAsync(file,()=>true);host.Content=real;await Task.Delay(30);
                        bool swapped=orientation>=5;Check(real.Bitmap.PixelWidth==(swapped?800:1200)&&real.Bitmap.PixelHeight==(swapped?1200:800),"Incorrect oriented initial dimensions");
                        var bounds=real.ImageBounds;Check(Math.Abs(bounds.Width/bounds.Height-(swapped?2d/3:1.5))<.001,"Preview layout did not use oriented dimensions");VerifyPixels(real.Bitmap,orientation);
                        var reply=await ResultThumbnails.Worker(new ThumbnailRequest(file,"","","lexical",null),CancellationToken.None);
                        Check(reply.Png is {Length:>0},"Worker returned no image");using(var png=new MemoryStream(reply.Png!)){var thumbnail=BitmapDecoder.Create(png,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];VerifyPixels(thumbnail,orientation);Check(Math.Max(thumbnail.PixelWidth,thumbnail.PixelHeight)<=640,"Thumbnail exceeded decode cap");}
                        if(orientation==6)
                        {
                            var target=real.TouchSurface;var center=new Point(target.ActualWidth/2,target.ActualHeight/2);var presentation=PresentationSource.FromVisual(target);var a=new VirtualTouch(721,target,presentation);var b=new VirtualTouch(722,target,presentation);
                            try
                            {
                                a.Down(center+new Vector(-50,0));b.Down(center+new Vector(50,0));b.Move(center+new Vector(150,0));var timer=Stopwatch.StartNew();while(real.Bitmap.PixelHeight<=1200&&timer.Elapsed<TimeSpan.FromSeconds(10))await Task.Delay(50);
                                Check(real.Bitmap.PixelWidth==1600&&real.Bitmap.PixelHeight==2400,"Quality upgrade lost orientation or dimensions");VerifyPixels(real.Bitmap,orientation);real.Fit();Screenshot(real,"image-exif-clockwise.png");
                            }
                            finally{a.Up();b.Up();}
                        }
                        using var exclusive=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);Check(exclusive.Length>0,"Oriented decode retained file handle");
                    }
                    string untagged=Path.Combine(root,"untagged.jpg");File.WriteAllBytes(untagged,encoded);VerifyPixels(OrientedImage.Load(untagged).Bitmap,1);
                    string invalid=Path.Combine(root,"invalid-orientation.jpg");var invalidBytes=File.ReadAllBytes(Path.Combine(root,"orientation-1.jpg"));invalidBytes[30]=9;File.WriteAllBytes(invalid,invalidBytes);VerifyPixels(OrientedImage.Load(invalid).Bitmap,1);
                });
                host.Content=view;await Task.Delay(40);
                await Test("initial image fits viewport with bounds",()=>
                {
                    Check(view.ZoomFactor==1,"New image did not start at fit");var b=view.ImageBounds;
                    Check(b.Left>=-.01&&b.Top>=-.01&&b.Right<=surface.ActualWidth+.01&&b.Bottom<=surface.ActualHeight+.01,"Initial image exceeds viewport");Screenshot(view,"image-fit.png");return Task.CompletedTask;
                });
                await Test("routed two-finger pinch anchors image under centroid",async()=>
                {
                    var center=new Point(surface.ActualWidth/2,surface.ActualHeight/2);var anchor=view.CenterImagePoint;
                    var source=PresentationSource.FromVisual(surface);first=new VirtualTouch(701,surface,source);second=new VirtualTouch(702,surface,source);
                    first.Down(center+new Vector(-60,0));second.Down(center+new Vector(60,0));first.Move(center+new Vector(-120,0));second.Move(center+new Vector(120,0));await Task.Delay(30);
                    Check(Math.Abs(view.ZoomFactor-2)<.02,"Pinch did not double image scale: "+view.ZoomFactor);
                    Check((view.CenterImagePoint-anchor).Length<1,"Pinch centroid drifted");
                    Screenshot(view,"image-zoomed.png");
                });
                await Test("pinch transitions to one-finger drag in both axes without jump",()=>
                {
                    second!.Up();var before=view.ImageBounds;first!.Move(first.Point+new Vector(35,-25));var after=view.ImageBounds;
                    Check(Math.Abs(after.X-before.X-35)<.1&&Math.Abs(after.Y-before.Y+25)<.1,"One-finger pan lost axis or jumped");
                    Check(Math.Abs(view.ZoomFactor-2)<.02,"One-finger pan changed zoom");return Task.CompletedTask;
                });
                await Test("drag clamps all edges and never loses image",()=>
                {
                    first!.Move(new Point(-10000,-10000));var low=view.ImageBounds;
                    Check(Math.Abs(low.Right-surface.ActualWidth)<.1&&Math.Abs(low.Bottom-surface.ActualHeight)<.1,"Far-edge clamp failed");
                    first.Move(new Point(10000,10000));var high=view.ImageBounds;
                    Check(Math.Abs(high.Left)<.1&&Math.Abs(high.Top)<.1,"Near-edge clamp failed");first.Up();return Task.CompletedTask;
                });
                await Test("floating and docking retain zoom and image center",async()=>
                {
                    // Match production's content-control reparenting between independent WPF windows.
                    host.Content=null;var other=new Window{Content=view,Width=host.Width,Height=host.Height,ShowInTaskbar=false};other.Show();await Task.Delay(50);
                    Check(Math.Abs(view.ZoomFactor-2)<.02,"Float reset zoom");var position=view.CenterImagePoint;
                    other.Content=null;other.Close();host.Content=view;await Task.Delay(50);
                    Check(Math.Abs(view.ZoomFactor-2)<.02&&(view.CenterImagePoint-position).Length<1,"Dock reset viewing position");
                });
                await Test("fit button resets scale and position; a new image has independent state",()=>
                {
                    view.FitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(view.ZoomFactor==1,"Fit button did not reset");
                    Check((view.CenterImagePoint-new Point(800,600)).Length<1,"Fit did not recenter");
                    var fresh=new ImagePreviewView(bitmap,1600,1200);Check(fresh.ZoomFactor==1,"New file inherited zoom");return Task.CompletedTask;
                });
                await Test("decode dimensions bound huge, portrait and panoramic images",()=>
                {
                    foreach(var size in new[]{(100000,90000),(10,100000),(100000,10),(1600,1200),(6000,4000)})
                    {
                        var decoded=ImagePreviewView.DecodeSize(size.Item1,size.Item2);Check(decoded.Width<=4096&&decoded.Height<=4096&&(long)decoded.Width*decoded.Height<=8_000_000,"Decode exceeded memory cap");
                        Check(decoded.Width<=size.Item1&&decoded.Height<=size.Item2,"Small image was upsampled");
                    }return Task.CompletedTask;
                });
                await Test("actual file decode upgrades on zoom within cap and releases file handle",async()=>
                {
                    var file=Path.Combine(root,"image.png");Directory.CreateDirectory(root);var pixels=new byte[2400*1600*4];for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
                    var large=BitmapSource.Create(2400,1600,96,96,PixelFormats.Bgra32,null,pixels,2400*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(large));using(var output=File.Create(file))encoder.Save(output);
                    var real=await ImagePreviewView.LoadAsync(file,()=>true);host.Content=real;await Task.Delay(40);
                    Check(real.Bitmap.PixelWidth<=1200,"Initial decode did not remain small");
                    var target=real.TouchSurface;var center=new Point(target.ActualWidth/2,target.ActualHeight/2);var source=PresentationSource.FromVisual(target);
                    var a=new VirtualTouch(711,target,source);var b=new VirtualTouch(712,target,source);
                    try{a.Down(center+new Vector(-50,0));b.Down(center+new Vector(50,0));b.Move(center+new Vector(150,0));var timer=System.Diagnostics.Stopwatch.StartNew();while(real.Bitmap.PixelWidth<=1200&&timer.Elapsed<TimeSpan.FromSeconds(10))await Task.Delay(50);Check(real.Bitmap.PixelWidth==2400,"High-quality decode not applied");Check((long)real.Bitmap.PixelWidth*real.Bitmap.PixelHeight<=8_000_000,"High-quality decode oversized");}
                    finally{a.Up();b.Up();}
                    using var exclusive=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);Check(exclusive.Length>0,"Image handle still locked");
                });
            }
            catch(Exception ex){failures++;records.Add(new{name="suite",passed=false,error=ex.ToString()});}
            finally
            {
                first?.Up();second?.Up();host.Close();Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests=records,scope="Cloud Windows WPF routed synthetic touch, real image decoding and real reparented windows. No physical touch hardware acceptance; no Soma backend or user files used."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);
            }
        };return app.Run();
    }
    sealed class VirtualTouch : TouchDevice
    {
        readonly FrameworkElement target;readonly UIElement rootVisual;TouchAction action;
        public Point Point{get;private set;}
        internal VirtualTouch(int id,FrameworkElement target,PresentationSource source):base(id){this.target=target;rootVisual=(UIElement)source.RootVisual;SetActiveSource(source);}
        public void Down(Point point){Point=point;action=TouchAction.Down;Activate();ReportDown();}
        public void Move(Point point){Point=point;action=TouchAction.Move;ReportMove();}
        public void Up(){if(!IsActive)return;action=TouchAction.Up;ReportUp();Deactivate();}
        public override TouchPoint GetTouchPoint(IInputElement? relativeTo){var position=target.TranslatePoint(Point,relativeTo as UIElement??rootVisual);return new TouchPoint(this,position,new Rect(position,new Size(2,2)),action);}
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo)=>new(){GetTouchPoint(relativeTo)};
    }
}
