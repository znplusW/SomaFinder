// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;
internal static class ResponsiveVerification
{
    public static int Run(string[] args)
    {
        string report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        string root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-responsive-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,DefaultDetailLayout=DetailLayout.Both};
        var store=new Store(root+"/data",root+"/config",root+"/state");var app=new FinderApplication(store,prefs,true);int failures=0;var tests=new List<object>();
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;
            void Check(bool ok,string why){if(!ok)throw new Exception(why);}
            async Task Test(string name,Func<Task> action){try{await action();tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString()});}}
            async Task Size(double width){window!.MinWidth=width;window.Width=width;window.Height=700;await Task.Delay(80);window.UpdateLayout();}
            void Screenshot(string name){var w=window!;var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(w);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(Path.GetDirectoryName(report)!,name+".png"));encoder.Save(output);}
            try
            {
                app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();window.EnableCloudLayoutFixture();
                var scope=new Dictionary<string,bool>{{"C:/cloud-fixture",true}};
                var hits=Enumerable.Range(0,8).Select(i=>new Hit{Workspace="fixture",RootPath="C:/cloud-fixture",FullPath="C:/cloud-fixture/file_"+i+".pdf",VirtualPath="soma://docs/"+i,Bytes=1234+i,Snippet="Needle 中文 needle 摘要文字，保持原始内容。",Rank=i}).ToArray();
                window.SeedResults(hits,scope);window.VerificationSelect(0);window.SetResultQuery("needle 中文 NOT 排除 -忽略");var chosen=window.SelectedHit;
                var details=window.VerificationDetails;var summary=CloudVerification.Descendants<SummaryTextView>(window).Single();
                long requests=app.Backend.RequestCount;double tree=prefs.SearchLayout.TreeWidth,left=details.PreferredLeftWidth,right=details.PreferredRightWidth;
                await Test("initial defaults enable battery interruption and lower priority while explicit old choices survive",async()=>
                {
                    var fresh=new Preferences();Check(fresh.BatteryStopsAuto&&fresh.LowerPriority,"New defaults are off");
                    var old=JsonSerializer.Deserialize<Preferences>("{\"BatteryStopsAuto\":false,\"LowerPriority\":false}")!;Check(!old.BatteryStopsAuto&&!old.LowerPriority,"Saved user choices were overwritten");
                    var settings=new SettingsWindow(app);settings.Show();await Task.Delay(60);CloudVerification.Descendants<TabControl>(settings).Single().SelectedIndex=1;await Task.Delay(60);settings.UpdateLayout();
                    var checkboxes=CloudVerification.Descendants<CheckBox>(settings).ToArray();
                    Check(checkboxes.Single(c=>Equals(c.Content,"切换到电池时，也停止正在进行的自动同步")).IsChecked==true&&checkboxes.Single(c=>Equals(c.Content,"降低索引任务优先级")).IsChecked==true,"Settings controls did not reflect new defaults");settings.Close();
                });
                await Test("width matrix fits all panes inside client and preserves results and preferred widths",async()=>
                {
                    foreach(double width in new[]{3840d,2800,1800,1300,1050,900,780,650,600,480,600,900,1800})
                    {
                        // Geometric/offscreen WPF windows, not a physical monitor connection.
                        window.MinWidth=320;await Size(width);var w=window.CurrentWidths!;
                        double sum=window.PanelGrid.ColumnDefinitions.Sum(c=>c.ActualWidth);
                        Check(Math.Abs(window.ActualWidth-width)<2,"Cloud offscreen width not applied: "+width+"/"+window.ActualWidth);Check(Math.Abs(sum-window.PanelGrid.ActualWidth)<2,"Outer panel exceeds client at "+width+": "+sum+"/"+window.PanelGrid.ActualWidth);
                        Check(w.Results>=120&&w.Tree>0,"Required panes lost readable width");Check(ReferenceEquals(chosen,window.SelectedHit),"Selection lost");
                    }
                    Check(details.PreferredLeftWidth==left&&details.PreferredRightWidth==right&&prefs.SearchLayout.TreeWidth==tree,"Automatic resizing overwrote preferred widths");
                    Check(details.Mode==DetailMode.SideBySide,"Widen did not restore side-by-side");Screenshot("responsive-wide");
                });
                await Test("narrow tabs and two-pane priority restore panel order and preserve floating windows",async()=>
                {
                    await Size(900);Check(details.Mode==DetailMode.Tabs,"Expected summary/preview tabs");details.SelectTab(DetailKind.Preview);Check(details.ContentVisible(DetailKind.Preview),"Preview tab not active");Screenshot("responsive-tabs");
                    await Size(600);Check(details.Mode==DetailMode.Hidden&&!details.ContentVisible(DetailKind.Preview),"Right panes should yield to tree/results");Screenshot("responsive-two-panes");
                    window.ToggleTree();await Size(680);Check(details.Mode==DetailMode.Tabs&&window.CurrentWidths!.Tree==0,"Hidden tree did not free detail space");window.ToggleTree();
                    await Size(1800);details.Float(DetailKind.Summary);await Task.Delay(70);var floating=details.FloatingWindow(DetailKind.Summary);await Size(600);await Size(1800);
                    Check(ReferenceEquals(floating,details.FloatingWindow(DetailKind.Summary))&&floating!.IsVisible,"Resize docked or closed a floating pane");details.Dock(DetailKind.Summary);await Task.Delay(50);
                    Check(details.LeftKind==DetailKind.Summary&&details.RightExpanded,"Wide panel order/state changed");
                });
                await Test("detail columns share left alignment and vertical center",async()=>
                {
                    await Size(1800);window.SetView(false);window.UpdateLayout();
                    var row=(DataGridRow)window.ResultGrid.ItemContainerGenerator.ContainerFromItem(chosen!);row.UpdateLayout();
                    var texts=CloudVerification.Descendants<TextBlock>(row).Where(t=>t.Text==chosen!.Size||t.Text==chosen.Type).ToArray();
                    Check(texts.Length>=2&&texts.All(t=>t.TextAlignment==TextAlignment.Left&&t.VerticalAlignment==VerticalAlignment.Center),"Size/type styles differ");
                    Check(texts.Max(t=>t.TranslatePoint(new Point(),row).Y)-texts.Min(t=>t.TranslatePoint(new Point(),row).Y)<1,"Size/type baselines not aligned");
                });
                await Test("splitter movement changes only the adjacent pair and controls have large opaque glyph buttons",async()=>
                {
                    await Size(1800);var dividers=CloudVerification.Descendants<AdjacentDivider>(window.PanelGrid).OrderBy(d=>Grid.GetColumn(d)).ToArray();
                    foreach(var divider in dividers)
                    {
                        window.UpdateLayout();var before=window.PanelGrid.ColumnDefinitions.Select(c=>c.ActualWidth).ToArray();int column=Grid.GetColumn(divider);int a=column-1,b=column+1;
                        divider.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});divider.Move(12);window.UpdateLayout();
                        var after=window.PanelGrid.ColumnDefinitions.Select(c=>c.ActualWidth).ToArray();Check(Math.Abs(after[a]-before[a]-12)<1&&Math.Abs(after[b]-before[b]+12)<1,"Adjacent widths did not exchange 12 DIP at "+column+": "+string.Join(',',before)+" -> "+string.Join(',',after));
                        Check(Enumerable.Range(0,before.Length).Where(i=>i!=a&&i!=b).All(i=>Math.Abs(after[i]-before[i])<1),"Unrelated pane resized during splitter drag");
                        divider.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(12,0,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});await Task.Delay(30);
                    }
                    var buttons=CloudVerification.Descendants<Button>(details.ControlRail).ToArray();Check(buttons.Length==2&&buttons.All(b=>b.Width>=40&&b.Height>=40&&b.Background?.Opacity==1&&b.Content is TextBlock glyph&&glyph.FontSize>=20),"Small or transparent detail control");
                });
                await Test("both summary surfaces use the chosen match style without requests",async()=>
                {
                    window.SetView(true);window.SetResultQuery("needle 中文 NOT 排除 -忽略");await Task.Delay(60);
                    foreach(string style in new[]{"Bold","Yellow","Bold"})
                    {
                        prefs.SearchLayout.MatchStyle=style;app.ApplyAppearance();await Task.Delay(50);window.UpdateLayout();
                        var cardText=CloudVerification.Descendants<TextBlock>(window.ActiveListSurface).First(t=>MatchText.GetText(t).Contains("Needle"));
                        var rightRuns=summary.Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines.OfType<Run>()).Where(r=>r.Text.Equals("needle",StringComparison.OrdinalIgnoreCase)).ToArray();
                        var cardRuns=cardText.Inlines.OfType<Run>().Where(r=>r.Text.Equals("needle",StringComparison.OrdinalIgnoreCase)).ToArray();
                        Check(rightRuns.Length==2&&cardRuns.Length==2,"Case-insensitive matches missing");
                        Check(rightRuns.Concat(cardRuns).All(r=>style=="Yellow"?r.Background==Brushes.Gold&&r.Foreground==Brushes.Black:r.FontWeight==FontWeights.Bold&&r.Background!=Brushes.Gold),"Surfaces use different styles");
                        Check(ReferenceEquals(chosen,window.SelectedHit)&&app.Backend.RequestCount==requests,"Style switch changed search");
                    }
                    store.Save(prefs);Check(store.Load().SearchLayout.MatchStyle=="Bold","Match style not persisted");
                    Check(!MatchText.Runs("排除 忽略 未搜索", "needle NOT 排除 -忽略","Yellow").Any(r=>r.Background==Brushes.Gold),"Negative/absent words highlighted");
                    foreach(string theme in new[]{"Light","Dark"}){prefs.SearchLayout.Theme=theme;prefs.SearchLayout.MatchStyle="Yellow";app.ApplyAppearance();await Task.Delay(100);Screenshot("matches-"+theme);}
                });
                await Test("preview fit shares the header in main and floating panes",async()=>
                {
                    var image=BitmapSource.Create(80,60,96,96,PixelFormats.Bgr32,null,new byte[80*60*4],80*4);var view=new ImagePreviewView(image,80,60);
                    window.SeedPreview(view);window.UpdateLayout();
                    Check(view.FitButton.Parent is StackPanel actions&&actions.Children.Count==2,"Fit action not beside independent-window action");
                    Check(!view.Children.Contains(view.FitButton),"Image keeps an extra toolbar");details.Float(DetailKind.Preview);await Task.Delay(70);
                    Check(view.FitButton.IsVisible&&Window.GetWindow(view.FitButton)==details.FloatingWindow(DetailKind.Preview),"Fit action did not follow preview window");
                    view.FitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(view.ZoomFactor==1,"Fit action no longer resets");details.Dock(DetailKind.Preview);await Task.Delay(50);
                });
                await Test("mixed DPI and unplug geometry fits target physical work area and permits half-screen",()=>
                {
                    int count=0;
                    foreach(double dpi in new[]{96d,120,144,168,192,240})foreach(var area in new[]{new Rect(0,0,1920,1040),new Rect(-3840,0,3840,2120),new Rect(1920,-1000,2880,1720)})
                    {
                        var fit=MonitorLayout.FitPixels(new Rect(-900,-300,4200,2400),area);Check(area.Contains(fit),"Window outside work area");Check(MonitorLayout.MinimumWidth(area.Width,dpi)<=area.Width*96/dpi/2,"Half-screen blocked by minimum");count++;
                    }
                    Check(count==18,"Incomplete geometry matrix");return Task.CompletedTask;
                });
                await Test("native current-monitor recovery fits an oversized real window",async()=>
                {
                    window.MinWidth=320;await Size(2800);MonitorLayout.Fit(window);await Task.Delay(80);Check(window.ActualWidth<=SystemParameters.WorkArea.Width+2,"Oversized main window did not fit actual cloud monitor");
                });
            }
            catch(Exception ex){failures++;tests.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally{window?.Close();app.Backend.Dispose();Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,scope="Targeted WPF layout, match rendering, preview header and monitor geometry. Resolution/DPI matrix is simulated; one actual cloud monitor recovery. No physical multi-monitor hotplug or backend indexing."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);}
        };
        return app.Run();
    }
}
