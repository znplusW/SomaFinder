// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

internal static class SummaryDensityVerification
{
    public static int Run(string[] args)
    {
        var report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-summary-density-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var store=new Store(root+"/data",root+"/config",root+"/state");
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false};prefs.SearchLayout.Comfortable=false;prefs.SearchLayout.Theme="Light";
        var app=new FinderApplication(store,prefs,true);int failures=0;var records=new List<object>();
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;
            void Check(bool value,string error){if(!value)throw new Exception(error);}
            async Task Test(string name,Func<Task> action){try{await action();records.Add(new{name,passed=true});}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});}}
            try
            {
                app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();
                var scope=new Dictionary<string,bool>{{"C:/cloud-fixture-docs",true}};
                var hits=Enumerable.Range(1,6).Select(i=>new Hit{Workspace="fixture",RootPath="C:/cloud-fixture-docs",FullPath="C:/cloud-fixture-docs/样例文件_"+i+".pdf",VirtualPath="soma://fixture/"+i,Rank=i,Snippet="这是云端合成摘要，用于比较紧凑与舒适密度。\n保留原有文件名、路径和两行摘要。"}).ToList();
                window.SeedResults(hits,scope);window.SetView(true);window.VerificationSelect(0);await Task.Delay(200);window.UpdateLayout();
                double compactGap=0,compactHeight=0;
                Border[] Cards()=>CloudVerification.Descendants<Border>(window.ActiveListSurface).Where(b=>(string?)b.Tag=="SummaryResultCard").OrderBy(b=>b.TranslatePoint(new Point(),window.ActiveListSurface).Y).Take(2).ToArray();
                double Gap(Border[] cards)=>cards[1].TranslatePoint(new Point(),window.ActiveListSurface).Y-cards[0].TranslatePoint(new Point(0,cards[0].ActualHeight),window.ActiveListSurface).Y;
                void Screenshot(string name)
                {
                    var view=window.ActiveListSurface;var image=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32);
                    image.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(Path.GetDirectoryName(report)!,name+".png"));encoder.Save(output);
                }
                await Test("compact summary cards have a small nonzero rendered gap",()=>
                {
                    var cards=Cards();Check(cards.Length==2,"Two consecutive summary cards not rendered");compactGap=Gap(cards);compactHeight=cards[0].ActualHeight;
                    Check(Math.Abs(compactGap-4)<.75,"Compact rendered gap must be 4 DIP: "+compactGap);
                    Check(cards[0].Padding.Top==7&&compactHeight>92,"Compact content padding or thumbnail changed unexpectedly");
                    Screenshot("summary-compact");records.Add(new{name="compact geometry",gap=compactGap,cardHeight=compactHeight});return Task.CompletedTask;
                });
                await Test("comfortable changes existing cards live and compact restores without changing results",async()=>
                {
                    var chosen=window.SelectedHit;var before=window.DisplayedHits.ToArray();var originalCards=Cards();long requests=app.Backend.RequestCount;
                    // Same live appearance application called after saving the settings checkbox.
                    prefs.SearchLayout.Comfortable=true;app.ApplyAppearance();await Task.Delay(120);window.UpdateLayout();
                    var cards=Cards();Check(cards.Length==2,"Cards disappeared after density update");double comfyGap=Gap(cards);
                    Check(Math.Abs(comfyGap-8)<.75&&comfyGap>compactGap,"Comfortable gap did not become 8 DIP: "+comfyGap);
                    Check(cards[0].Padding.Top==10&&cards[0].ActualHeight>compactHeight,"Comfortable padding did not change visible content");
                    Check(ReferenceEquals(cards[0],originalCards[0]),"Density rebuilt the selected card instead of updating resources");
                    Check(ReferenceEquals(chosen,window.SelectedHit)&&before.SequenceEqual(window.DisplayedHits)&&requests==app.Backend.RequestCount,"Density changed selection, results or submitted a request");
                    Screenshot("summary-comfortable");records.Add(new{name="comfortable geometry",gap=comfyGap,cardHeight=cards[0].ActualHeight});
                    prefs.SearchLayout.Comfortable=false;app.ApplyAppearance();await Task.Delay(80);window.UpdateLayout();
                    Check(Math.Abs(Gap(Cards())-compactGap)<.75,"Compact gap was not restored");
                });
            }
            catch(Exception ex){failures++;records.Add(new{name="suite",passed=false,error=ex.ToString()});}
            finally{window?.Close();Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests=records,scope="Only rendered summary-card spacing and live density toggle in cloud WPF. Synthetic results; no Soma backend, search or index tests."},new JsonSerializerOptions{WriteIndented=true}));app.Backend.Dispose();app.Shutdown(failures==0?0:1);}
        };
        return app.Run();
    }
}
