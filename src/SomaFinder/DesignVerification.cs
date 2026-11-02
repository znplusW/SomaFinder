// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SomaFinder;
internal static class DesignVerification
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static int Run(string[] args)
    {
        Environment.SetEnvironmentVariable("SOMAFINDER_TEST","1");var report=System.IO.Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var dir=System.IO.Path.GetDirectoryName(report)!;var root=System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-design-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var store=new Store(System.IO.Path.Combine(root,"data"),System.IO.Path.Combine(root,"config"),System.IO.Path.Combine(root,"state"));
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,PauseUntil=DateTimeOffset.UtcNow.AddHours(3),BatteryStopsAuto=true};
        foreach(var pair in new[]{("alpha","C:/云端示例"),("beta","C:/另一个示例"),("same-a","C:/archive/资料"),("same-b","C:/backup/资料")})store.SaveWorkspace(new Workspace{Name=pair.Item1,ConfigPath=System.IO.Path.Combine(store.ConfigDirectory,pair.Item1+".yml"),Roots=new(){new(){Name="docs",Path=pair.Item2}}});
        var app=new FinderApplication(store,prefs,true);var records=new List<object>();int failures=0;var windows=new List<SearchWindow>();
        void Test(string name,Action action){Console.WriteLine("START "+name);try{action();records.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine("FAIL "+name+" "+ex.Message);}Store.Atomic(report,JsonSerializer.Serialize(new{failures,tests=records},new JsonSerializerOptions{WriteIndented=true}));}
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        void Pump(){var frame=new DispatcherFrame();app.Dispatcher.BeginInvoke(()=>frame.Continue=false,DispatcherPriority.ApplicationIdle);Dispatcher.PushFrame(frame);}
        var scope=new Dictionary<string,bool>{{"C:/云端示例",true},{"C:/另一个示例",true}};
        var hits=Enumerable.Range(0,1200).Select(i=>new Hit{Workspace=i%2==0?"alpha":"beta",RootPath=i%2==0?"C:/云端示例":"C:/另一个示例",FullPath=(i%2==0?"C:/云端示例":"C:/另一个示例")+"/"+(i%3==0?"项目/续航测试记录_":"计划/合同资料_")+i+(i%2==0?".pdf":".md"),VirtualPath="soma://docs/"+i,Snippet=i%7==0?"":"Soma 返回的摘要片段：比较办公与视频播放时的电池续航。这是云端合成 API 字段，没有额外生成摘要。",Bytes=i%9==0?null:1200+i,Rank=i,Availability=i%9==0?"原文件离线":"云端合成文件元数据"}).ToList();
        var w=new SearchWindow(app,scope);windows.Add(w);w.Show();w.SeedResults(hits,scope);w.VerificationSelect(20);
        Test("same candidates, selection and original snippets survive both views without requests",()=>
        {
            var before=app.Backend.RequestCount;var chosen=w.SelectedHit;var order=w.DisplayedHits.ToArray();for(int i=0;i<200;i++){w.SetView(i%2==0);Pump();Check(ReferenceEquals(chosen,w.SelectedHit),"Selection changed");Check(order.SequenceEqual(w.DisplayedHits),"Order changed");}
            Check(app.Backend.RequestCount==before,"View switch called backend");Check(w.SelectedHit?.Snippet==chosen?.Snippet,"Original snippet changed");
        });
        Test("toolbar sorting works in summary and detailed lists, original order restores",()=>
        {
            w.SetView(true);w.SetSort(3,true);var sorted=w.DisplayedHits.ToArray();Check(sorted.Where(h=>h.Bytes!=null).Select(h=>h.Bytes!.Value).SequenceEqual(sorted.Where(h=>h.Bytes!=null).Select(h=>h.Bytes!.Value).OrderByDescending(n=>n)),"Size descending incorrect");
            w.SetView(false);Check(sorted.SequenceEqual(w.DisplayedHits),"View lost sorting");w.SetSort(0);Check(w.DisplayedHits.SequenceEqual(hits.OrderBy(h=>h.RootPath,StringComparer.OrdinalIgnoreCase).ThenBy(h=>h.Rank)),"Soma grouped rank not restored");
        });
        Test("scope filtering, expansion notice and windows remain independent",()=>
        {
            var reduced=new Dictionary<string,bool>(scope){["C:/另一个示例"]=false};w.SetScope(reduced);Check(w.DisplayedHits.Count==600,"Scope reduction wrong");var peer=new SearchWindow(app,w.Scope());windows.Add(peer);Check(!peer.SummaryView,"New window inherited view");peer.SeedResults(hits,scope);peer.SetView(true);Check(peer.DisplayedHits.Count==1200&&w.DisplayedHits.Count==600,"Window state crossed");w.SetScope(scope);
            var type=CloudVerification.Descendants<ComboBox>(w).Single(c=>c.Items.Count==8);type.SelectedIndex=3;Check(w.DisplayedHits.Count==0,"Audio filter not retained");type.SelectedIndex=0;
            w.SeedResults(hits,reduced);w.SetScope(scope);Check(w.DisplayedHits.Count==600&&w.RangeNeedsSearch,"Expansion showed unsearched data or hid search action");w.SeedResults(hits,scope);
        });
        Test("column visibility order and widths persist without resetting pause or defaults",()=>
        {
            var grid=w.ResultGrid;grid.Columns[2].Visibility=Visibility.Collapsed;grid.Columns[4].DisplayIndex=1;grid.Columns[1].Width=317;
            w.Close();Pump();var loaded=store.Load();Check(loaded.PauseUntil==prefs.PauseUntil&&loaded.BatteryStopsAuto&&!loaded.StartupSync,"Policy lost during layout save");
            var restored=new SearchWindow(app,scope);windows.Add(restored);Check(restored.ResultGrid.Columns[2].Visibility==Visibility.Collapsed&&restored.ResultGrid.Columns[4].DisplayIndex==1&&Math.Abs(restored.ResultGrid.Columns[1].Width.Value-317)<2,"Column restore failed");
            restored.Show();restored.SeedResults(hits.Take(30),scope);restored.VerificationSelect(1);w=restored;
        });
        Test("layout dimensions and real WPF screenshots: light dark narrow 2.8K and 4K",()=>
        {
            var dimensions=new List<object>();var paths=new[]{System.IO.Path.Combine(root,"项目资料"),System.IO.Path.Combine(root,"技术记录")};
            foreach(var path in paths){Directory.CreateDirectory(path);prefs.Volumes[path]=Native.Identify(path)!;var name="render-"+Array.IndexOf(paths,path);store.SaveWorkspace(new Workspace{Name=name,ConfigPath=System.IO.Path.Combine(store.ConfigDirectory,name+".yml"),Roots=new(){new(){Name="docs",Path=path}}});}app.Backend.Reload();
            var renderScope=paths.ToDictionary(p=>p,_=>true,StringComparer.OrdinalIgnoreCase);
            var renderHits=hits.Take(30).Select((h,i)=>new Hit{Workspace="render",RootPath=paths[i%2],FullPath=System.IO.Path.Combine(paths[i%2],h.Name),VirtualPath=h.VirtualPath,Snippet=h.Snippet,Bytes=h.Bytes,Rank=h.Rank,Availability="云端合成示例"}).ToList();
            var selected=renderHits.First(h=>h.Type==".md");File.WriteAllText(selected.FullPath,"# 云端用于预览的真实临时文本\n\n办公与视频续航测试的合成记录。这里只读取云端临时文件，展示现有文本预览功能。\n\n原始摘要直接使用结果字段，切换列表不会重新搜索。");
            foreach(var scenario in new[]{(Name:"narrow",Pixels:1000,Height:720,Scale:1d),(Name:"28k",Pixels:2880,Height:1800,Scale:1.5),(Name:"4k",Pixels:3840,Height:2160,Scale:2d)})
            foreach(var dark in new[]{false,true})foreach(var summary in new[]{false,true})
            {
                string theme=dark?"Dark":"Light";app.ThemeMode=SearchWindow.ParseTheme(theme);prefs.SearchLayout.Theme=theme;w.ApplyTheme();Pump();
                // Use an unshown instance of the actual window, with the same live app
                // palette. Detaching a shown visual clears native templates and clips
                // to the VM screen; neither is a valid high-resolution layout capture.
                var render=new SearchWindow(app,renderScope);try
                {
                render.SeedResults(renderHits,renderScope);render.SetView(summary);if(!render.VerificationDetails.RightExpanded)render.VerificationDetails.ToggleRight();render.ResultGrid.SelectedItem=selected;
                var ready=Stopwatch.StartNew();while(!render.VerificationPreviewText.Contains("云端用于预览")&&ready.Elapsed<TimeSpan.FromSeconds(5)){Pump();Thread.Sleep(10);}Check(render.VerificationPreviewText.Contains("云端用于预览"),"Actual temporary text preview missing: "+render.VerificationPreviewText);
                var content=(FrameworkElement)render.Content;double width=scenario.Pixels/scenario.Scale,height=scenario.Height/scenario.Scale;content.Width=width;content.Height=height;content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();Pump();
                Check(render.ActiveListWidth>=420,"Result list squeezed: "+render.ActiveListWidth);if(!summary)Check(render.ResultGrid.Columns[0].ActualWidth>=175,"Name column unreadable");
                var color=(render.TryFindResource("ApplicationBackgroundBrush") as SolidColorBrush)?.Color;Check(color!=null&&(dark?color.Value.R<110:color.Value.R>180),"Theme background did not change: "+color);
                if(!summary){var rowColor=(render.ResultGrid.RowBackground as SolidColorBrush)?.Color;Check(rowColor!=null&&(dark?rowColor.Value.R<110:rowColor.Value.R>180),"Row palette did not match theme: "+rowColor);}
                var bitmap=new RenderTargetBitmap(scenario.Pixels,scenario.Height,96*scenario.Scale,96*scenario.Scale,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new SolidColorBrush(dark?Color.FromRgb(25,25,25):Color.FromRgb(244,244,244)),null,new Rect(0,0,width,height));bitmap.Render(visual);bitmap.Render(content);
                int listPixels=0,previewPixels=0;
                if(scenario.Name!="narrow")
                {
                    var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                    int Ink(FrameworkElement element)
                    {
                        var origin=element.TransformToAncestor(content).Transform(new Point(0,0));int matches=0;
                        int left=Math.Max(0,(int)((origin.X+12)*scenario.Scale)),right=Math.Min(bitmap.PixelWidth,(int)((origin.X+element.ActualWidth-12)*scenario.Scale));int top=Math.Max(0,(int)((origin.Y+12)*scenario.Scale)),bottom=Math.Min(bitmap.PixelHeight,(int)((origin.Y+element.ActualHeight-12)*scenario.Scale));
                        for(int y=top;y<bottom;y+=8)for(int x=left;x<right;x+=8){int at=(y*bitmap.PixelWidth+x)*4;if(pixels[at+3]>0&&Math.Max(Math.Abs(pixels[at]-color!.Value.B),Math.Max(Math.Abs(pixels[at+1]-color.Value.G),Math.Abs(pixels[at+2]-color.Value.R)))>40)matches++;}return matches;
                    }
                    listPixels=Ink(render.ActiveListSurface);previewPixels=Ink(render.PreviewSurface);Check(listPixels>20&&previewPixels>20,"Raster contains blank/clipped list or preview: "+listPixels+"/"+previewPixels);
                }
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(System.IO.Path.Combine(dir,"design-"+scenario.Name+"-"+theme+"-"+(summary?"summary":"details")+".png")))encoder.Save(stream);
                dimensions.Add(new{scenario.Name,scenario.Pixels,scenario.Height,scenario.Scale,dipWidth=width,listWidth=render.ActiveListWidth,nameWidth=render.ResultGrid.Columns[0].ActualWidth,summary,dark,background=color.ToString(),listPixels,previewPixels,preview="actual temporary markdown file, no generated preview"});
                }finally{render.Close();Pump();}
            }
            Store.Atomic(System.IO.Path.Combine(dir,"design-layouts.json"),JsonSerializer.Serialize(dimensions,new JsonSerializerOptions{WriteIndented=true}));
        });
        Test("1200-row virtualization and 2500 randomized UI operations are bounded",()=>
        {
            using var process=Process.GetCurrentProcess();GC.Collect();process.Refresh();long start=process.PrivateMemorySize64;
            w.SeedResults(hits,scope);var random=new Random(330);for(int i=0;i<2500;i++){switch(random.Next(4)){case 0:w.SetView(random.Next(2)==0);break;case 1:w.SetSort(random.Next(7),random.Next(2)==0);break;case 2:w.VerificationSelect(random.Next(1200));break;default:w.VerificationDetails.Swap();break;}if(i%100==0)Pump();}
            var list=CloudVerification.Descendants<ListBox>(w).First(l=>l.ItemsSource!=null);Check(VirtualizingPanel.GetIsVirtualizing(list),"Summary virtualization disabled");Check(app.Backend.SearchCount==0,"Synthetic-only interaction submitted a search");Pump();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Pump();process.Refresh();
            records.Add(new{name="design stress metrics",operations=2500,candidates=1200,privateBytesGrowth=process.PrivateMemorySize64-start,managedBytes=GC.GetTotalMemory(true),realizedGridRows=CloudVerification.Descendants<DataGridRow>(w).Count(),realizedSummaryRows=CloudVerification.Descendants<ListBoxItem>(w).Count()});
            Check(process.PrivateMemorySize64-start<350L*1024*1024,"Excessive retained memory");
        });
        Test("adjacent dividers conserve pair width and freeze the other two regions",()=>
        {
            var pane=new SearchWindow(app,scope);windows.Add(pane);pane.EnableCloudLayoutFixture();pane.MinWidth=1800;pane.Width=1800;pane.Show();if(!pane.VerificationDetails.RightExpanded)pane.VerificationDetails.ToggleRight();Pump();
            Check(pane.VerificationDetails.Mode==DetailMode.SideBySide,"Divider fixture must have four readable panes");
            var dividers=CloudVerification.Descendants<AdjacentDivider>(pane).OrderBy(Grid.GetColumn).ToArray();Check(dividers.Length==3,"Missing thin dividers");
            for(int i=0;i<3;i++){Check(dividers[i].IsEnabled,"Divider fixture targeted a disabled divider");dividers[i].RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});var before=pane.RegionWidths.ToArray();double delta=i==0?-12:12;dividers[i].Move(delta);Pump();var after=pane.RegionWidths.ToArray();Check(Math.Abs(after.Sum()-before.Sum())<.01,"Total changed");for(int j=0;j<4;j++)if(j!=i&&j!=i+1)Check(Math.Abs(after[j]-before[j])<.01,"Unrelated region changed");Check(Math.Abs(after[i]-before[i]-delta)<.01,"Adjacent pair did not exchange the permitted width");dividers[i].Move(-delta);Pump();}
            pane.Close();Pump();
        });
        Test("header sort markers persist after rebinding and repeated column clicks",()=>
        {
            w.SeedResults(hits,scope);w.SetView(false);var col=w.ResultGrid.Columns[0];w.SetSort(1);Check(col.SortDirection==System.ComponentModel.ListSortDirection.Ascending,"Ascending marker lost");w.SortColumn(col);Check(col.SortDirection==System.ComponentModel.ListSortDirection.Descending,"Second click did not descend");w.SetScope(scope);Check(col.SortDirection==System.ComponentModel.ListSortDirection.Descending,"Filtering lost marker");w.SortColumn(col);Check(col.SortDirection==System.ComponentModel.ListSortDirection.Ascending,"Third click did not ascend");w.SetSort(0);Check(w.ResultGrid.Columns.All(c=>c.SortDirection==null),"Soma order marked a file column");
        });
        Test("touch scroll zoom state and fractional horizontal wheel routing",()=>
        {
            Check(ScrollViewer.GetPanningMode(w.ResultGrid)==PanningMode.VerticalOnly,"Detailed touch scrolling missing");w.ZoomSummary(2);Check(Math.Abs(w.SummaryZoom-2)<.01,"Zoom failed");w.VerificationDetails.Swap();w.VerificationDetails.Float(DetailKind.Summary);Pump();Check(w.SummaryZoom==2,"Float reset zoom");w.VerificationDetails.Dock(DetailKind.Summary);w.ZoomSummary(.5);Check(w.SummaryZoom==1,"Reset zoom failed");
            var scroll=new ScrollViewer{Width=200,Height=100,Content=new Border{Width=1000,Height=50},HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};scroll.Measure(new Size(200,100));scroll.Arrange(new Rect(0,0,200,100));scroll.UpdateLayout();Check(SearchWindow.HorizontalWheel((Border)scroll.Content,15),"Small horizontal delta ignored");scroll.UpdateLayout();Check(scroll.HorizontalOffset>0,"Horizontal offset unchanged");
        });
        Test("settings footer is save cancel and pause uses one switch with a separate manual section",()=>
        {
            var settings=new SettingsWindow(app);settings.Show();var tabs=CloudVerification.Descendants<TabControl>(settings).Single();tabs.SelectedIndex=1;settings.UpdateLayout();Pump();var labels=CloudVerification.Descendants<Button>(settings).Select(b=>b.Content as string).ToList();Check(labels.Contains("保存设置")&&labels.Contains("取消")&&!labels.Contains("舍弃草稿")&&!labels.Contains("保存并更新索引"),"Footer still has old choices");Check(labels.Count(t=>t=="恢复索引"||t=="暂停索引")==1,"Pause has multiple switches");Check(labels.Contains("立即更新"),"Manual update missing");settings.Close();
        });
        Test("settings retain five groups and battery child option, all file types remain",()=>
        {
            var settings=new SettingsWindow(app);var tabs=CloudVerification.Descendants<TabControl>(settings).Single();Check(tabs.Items.Count==5,"Settings groups missing");var checks=CloudVerification.Descendants<CheckBox>(settings).ToList();
            tabs.SelectedIndex=1;settings.UpdateLayout();Check(CloudVerification.Descendants<CheckBox>(settings).Any(c=>(c.Content as string)=="切换到电池时，也停止正在进行的自动同步"),"Battery suboption missing");settings.Close();
        });
        Test("confirmation and backend choice inherit the selected application theme",()=>
        {
            app.ThemeMode=ThemeMode.Dark;Exception? error=null;
            _=app.Dispatcher.BeginInvoke(()=>{var dialog=app.Windows.OfType<Window>().Single(x=>x.Title=="云端主题验证");try{Check(dialog.ThemeMode==ThemeMode.Dark,"Confirmation ignored theme");}catch(Exception ex){error=ex;}finally{dialog.Close();}},DispatcherPriority.ApplicationIdle);
            UI.Confirm("云端合成确认对话框，仅验证外观。","云端主题验证","取消","确认");if(error!=null)throw error;
            var selector=new BackendSelectionWindow(w,prefs,new BackendCatalog(new List<BackendCandidate>(),""));Check(selector.ThemeMode==ThemeMode.Dark,"Backend selector ignored theme");selector.Close();
        });
        foreach(var window in windows)window.Close();Pump();app.Backend.Dispose();
        Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),version=typeof(Program).Assembly.GetName().Version?.ToString(),failures,tests=records,scope="Synthetic API-shaped results with actual WPF controls and routed state, offscreen raster at specified pixel/DPI sizes. No real display hardware or physical power switching claimed; thumbnail cache reads are visible-row-only and missing cache uses type icons. Touch/WM_MOUSEHWHEEL are state and message-routing checks, not physical touch or Logitech hardware acceptance."},new JsonSerializerOptions{WriteIndented=true}));return failures==0?0:1;
    }
}
