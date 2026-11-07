// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

// Product screenshots contain synthetic fixture data, never a user's files.
internal static class PublicReleaseVerification
{
    public static int Run(string[] args)
    {
        string report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        string root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-demo-"+Guid.NewGuid().ToString("N")[..8]);CloudSandbox.Initialize(root);
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,DefaultDetailLayout=DetailLayout.Both};
        var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),prefs,true);var tests=new List<object>();int failures=0;
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;
            void Check(bool ok,string why){if(!ok)throw new Exception(why);}
            async Task Test(string name,Func<Task> action){try{await action();Check(CloudSandbox.Errors.Count==0,string.Join('\n',CloudSandbox.Errors));tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString()});}}
            async Task Wait(Func<bool> ready){var clock=Stopwatch.StartNew();while(!ready()&&clock.ElapsedMilliseconds<15000)await Task.Delay(40);Check(ready(),"Timed out waiting for demo UI");}
            void Screen(string name)
            {
                window!.UpdateLayout();var surface=(FrameworkElement)window.Content;
                var status=CloudVerification.Descendants<TextBlock>(window).First(t=>t.Text.StartsWith("索引：")||t.Text.StartsWith("演示资料 · 合成搜索结果"));status.Text="演示资料 · 合成搜索结果，未连接后端";
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(report)!,name+".png"),ThumbnailWorker.Png(bitmap));
            }
            try
            {
                await Test("public binary ships complete original and third-party license notices",()=>
                {
                    Check(typeof(Program).Assembly.GetName().Version==new Version(0,6,7,0),"Wrong public binary version");
                    foreach(string path in new[]{"Documents/LICENSE.txt","Documents/Licensing.txt","Documents/Guides/INSTALL.en.md","Documents/Guides/INSTALL.zh-CN.md","Documents/LICENSES/FFmpeg/COPYING.GPLv3","Documents/LICENSES/FFmpeg/dependency-sources.json"})Check(File.Exists(Path.Combine(AppContext.BaseDirectory,path)),"Missing public release document: "+path);
                    Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Documents/LICENSE.txt")).Contains("GNU GENERAL PUBLIC LICENSE"),"Missing GPLv3 text");
                    Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Documents/Licensing.txt")).Contains("commercial"),"Missing commercial policy");return Task.CompletedTask;
                });
                await Test("settings about page exposes public version tested backend and licensing action",async()=>
                {
                    var settings=new SettingsWindow(app);settings.Show();await Task.Delay(70);
                    try
                    {
                        CloudVerification.Descendants<TabControl>(settings).Single().SelectedIndex=3;await Task.Delay(70);
                        var text=string.Join('\n',CloudVerification.Descendants<TextBlock>(settings).Select(t=>t.Text));
                        Check(text.Contains("0.6.8")&&text.Contains("0.10.7")&&text.Contains("GPLv3")&&text.Contains("不附带担保"),"About/legal notice missing");
                        Check(CloudVerification.Descendants<Button>(settings).Any(b=>Equals(b.Content,"查看许可与商业授权 / Licensing")),"Licensing action missing");
                    }
                    finally{settings.Close();}
                });
                string folder=root+"/海边旅行";Directory.CreateDirectory(folder);prefs.Volumes[folder]=Native.Identify(folder)!;
                var titles=new[]{"海边旅行·日落","海岸步道·旅行照片","海边旅行·灯塔"};var names=Enumerable.Range(0,9).Select(i=>titles[i%3]+"-"+(i/3+1)+".png").ToArray();
                for(int i=0;i<names.Length;i++)
                {
                    var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(168,215,239)),null,new Rect(0,0,480,300));
                        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255,219,134)),null,new Point(350-i%3*75,78),27,27);
                        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(29+i*5),133,157)),null,new Rect(0,135,480,165));
                        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(226,206,163)),null,Geometry.Parse("M0,235 C160,175 220,290 480,230 L480,300 L0,300 Z"));
                    }
                    var bitmap=new RenderTargetBitmap(480,300,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);File.WriteAllBytes(Path.Combine(folder,names[i]),ThumbnailWorker.Png(bitmap));
                }
                var hits=names.Select((name,i)=>new Hit{Workspace="demo",RootPath=folder,FullPath=Path.Combine(folder,name),VirtualPath="soma://demo/"+name,Snippet=new[]{"海边旅行的日落时刻，沿海岸步道散步，远处是灯塔。","旅行照片记录了蓝色海面、沙滩与傍晚的阳光。","海边旅行时经过的灯塔与安静海岸。"}[i%3],MatchMode="lexical",Bytes=new FileInfo(Path.Combine(folder,name)).Length,Created=DateTime.Now,Modified=DateTime.Now,Availability="原文件可用 · 演示资料"}).ToArray();
                app.Backend.Workspaces.Add(new Workspace{Name="demo",ConfigPath=root+"/config/demo.yml",Roots=new(){new Root{Name="travel",Path=folder}}});
                app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();window.EnableCloudLayoutFixture();window.MinWidth=1640;window.Width=1640;window.Height=800;
                var scope=new Dictionary<string,bool>{{folder,true}};window.SetResultQuery("海边 旅行");window.SeedResults(hits,scope);
                CloudVerification.Descendants<TextBox>(window).Single(t=>AutomationProperties.GetName(t)=="搜索内容").Text="海边 旅行";window.VerificationSelect(0);
                await Wait(()=>CloudVerification.Descendants<ImagePreviewView>(window.PreviewSurface).Any());
                await Test("synthetic showcase renders three result views without issuing another search",async()=>
                {
                    long requests=app.Backend.RequestCount;
                    await Task.WhenAll(hits.Select(hit=>ResultThumbnails.Get(hit,hit.ThumbnailQuery,prefs,CancellationToken.None)));
                    window.SetView(1);await Wait(()=>CloudVerification.Descendants<Image>(window.ActiveListSurface).Count(image=>image.Source!=null)>=3);Screen("search-summary");
                    window.SetView(0);await Task.Delay(150);Screen("search-details");
                    window.SetView(2);await Wait(()=>window.Waterfall.RealizedCount>=6&&window.Waterfall.RealizedImages==window.Waterfall.RealizedCount);Screen("search-masonry");
                    Check(app.Backend.RequestCount==requests&&window.DisplayedHits.Count==names.Length,"Showcase changed search data or queried Soma");
                });
            }
            catch(Exception ex){failures++;tests.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally
            {
                if(window!=null){window.SkipExitOnce=true;window.Close();}app.Backend.Dispose();
                Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,scope="Public package and legal UI checks; synthetic client-only screenshots, not actual semantic retrieval or physical hardware acceptance. No Soma/backend/index."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);
            }
        };return app.Run();
    }
}
