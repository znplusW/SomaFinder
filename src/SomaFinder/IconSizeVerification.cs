// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing=System.Drawing;

namespace SomaFinder;
internal static class IconSizeVerification
{
    public static int Run(string[] args)
    {
        string report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-icon-size-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var store=new Store(root+"/data",root+"/config",root+"/state");var prefs=new Preferences{StartupSync=false,PeriodicSync=false};var app=new FinderApplication(store,prefs,true);
        int failures=0;var tests=new List<object>();var sizes=new List<object>();
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;
            void Check(bool ok,string why){if(!ok)throw new Exception(why);}
            async Task Test(string name,Func<Task> action){try{await action();tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString()});}}
            try
            {
                app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();await Task.Delay(100);
                using var originalStream=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Components","SomaFinder.ico"));
                var original=new IconBitmapDecoder(originalStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames.First();
                var helper=typeof(Window).Assembly.GetType("MS.Internal.AppModel.IconHelper")!.GetMethod("CreateIconHandleFromImageSource",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
                Drawing.Bitmap Native(ImageSource source,int size){using var handle=(SafeHandle)helper.Invoke(null,new object[]{source,new Size(size,size)})!;using var icon=Drawing.Icon.FromHandle(handle.DangerousGetHandle());return icon.ToBitmap();}
                Drawing.Rectangle Logo(Drawing.Bitmap image)
                {
                    var points=new List<Drawing.Point>();for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){if(x>=image.Width*.6&&y>=image.Height*.6)continue;var c=image.GetPixel(x,y);if(c.A>100&&c.R<70&&c.G>65&&c.G<170&&c.B>65&&c.B<170)points.Add(new(x,y));}
                    Check(points.Count>0,"Original logo missing");return Drawing.Rectangle.FromLTRB(points.Min(p=>p.X),points.Min(p=>p.Y),points.Max(p=>p.X)+1,points.Max(p=>p.Y)+1);
                }
                await Test("negative control reproduces fixed 32px drawing shrink in actual WPF icon conversion",()=>
                {
                    var drawing=new DrawingGroup();drawing.Children.Add(new ImageDrawing(original,new Rect(0,0,32,32)));using var bad=Native(new DrawingImage(drawing),64);using var normal=Native(original,64);
                    Check(Logo(bad).Width<Logo(normal).Width*.7,"Old padded-icon shrink was not reproduced");bad.Save(Path.Combine(Path.GetDirectoryName(report)!,"old-64-padded.png"));return Task.CompletedTask;
                });
                prefs.PauseUntil=DateTimeOffset.UtcNow.AddHours(1);app.Backend.Save();for(int i=0;i<50&&!app.VerificationTaskbarIcons.HasPauseIcon(window);i++)await Task.Delay(20);
                await Test("paused native icons retain normal logo proportions at 16 through 128 pixel sizes",()=>
                {
                    Check(window.Icon is BitmapFrame frame&&frame.Decoder is IconBitmapDecoder&&frame.Decoder.Frames.Count>=8,"Pause icon is not a multi-size ICO");
                    using var normal=Native(original,64);var baseline=Logo(normal);
                    foreach(int requested in new[]{16,20,24,32,40,48,64,96,128})
                    {
                        using var paused=Native(window.Icon!,requested);var bounds=Logo(paused);
                        double expectedW=baseline.Width/(double)normal.Width*paused.Width,expectedH=baseline.Height/(double)normal.Height*paused.Height;
                        Check(Math.Abs(bounds.Width-expectedW)<=2.5&&Math.Abs(bounds.Height-expectedH)<=2.5,"Logo shrank/padded at "+requested+": "+bounds+" expected "+expectedW+"x"+expectedH);
                        Check(paused.Width==requested&&paused.Height==requested,"WPF chose incorrect native icon frame");
                        sizes.Add(new{requested,canvas=paused.Width,logoWidth=bounds.Width,logoHeight=bounds.Height,expectedW,expectedH});paused.Save(Path.Combine(Path.GetDirectoryName(report)!,"paused-"+requested+".png"));
                    }
                    return Task.CompletedTask;
                });
                await Test("single-file packaged UI resources configuration and component paths remain portable",async()=>
                {
                    Check(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)==Path.GetDirectoryName(Environment.ProcessPath),"Bundle changed portable base directory");
                    Check(!File.Exists(Path.Combine(AppContext.BaseDirectory,"SomaFinder.dll"))&&!File.Exists(Path.Combine(AppContext.BaseDirectory,"coreclr.dll")),"Loose runtime files remain beside exe");
                    Check(File.Exists(Path.Combine(AppContext.BaseDirectory,"Components","SomaFinder.TrayHost.exe")),"Component layout mismatch");
                    var scope=new Dictionary<string,bool>{{"C:/fixture",true}};window.SeedResults(new[]{new Hit{FullPath="C:/fixture/file.txt",RootPath="C:/fixture",Workspace="fixture",VirtualPath="soma://docs/file.txt",Snippet="needle"}},scope);window.SetView(true);window.SetResultQuery("needle");await Task.Delay(80);
                    Check(CloudVerification.Descendants<TextBlock>(window.ActiveListSurface).Any(t=>MatchText.GetText(t)=="needle"),"Bundled XAML/assembly resources did not load");
                    var ws=new Workspace{Name="packaged",ConfigPath=store.ConfigDirectory+"/packaged.yml",Roots=new(){new(){Name="docs",Path=root+"/docs"}}};store.SaveWorkspace(ws);Check(store.ReadWorkspaces().Any(w=>w.Name=="packaged"),"Bundled YAML dependency failed");
                    store.Save(prefs);Check(store.Load().PauseUntil==prefs.PauseUntil,"Portable preferences failed");
                });
            }
            catch(Exception ex){failures++;tests.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally{window?.Close();app.Backend.Dispose();Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,sizes,scope="Only pause-icon size and this release's single-file startup/resources. Native HICON conversion uses actual WPF consumer at requested pixel sizes, not physical DPI/monitor hardware. No backend or indexing."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);}
        };
        return app.Run();
    }
}
