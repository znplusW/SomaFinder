// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

internal static class PaneToggleVerification
{
    public static int Run(string[] args)
    {
        var report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-pane-toggles-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,DefaultDetailLayout=DetailLayout.Both};
        var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),prefs,true);
        var tests=new List<object>();int failures=0;
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;
            void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            async Task Test(string name,Func<Task> action){try{await action();Check(CloudSandbox.Errors.Count==0,string.Join('\n',CloudSandbox.Errors));tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString(),mode=window?.VerificationDetails.Mode.ToString(),width=window?.ActualWidth});}}
            async Task Size(double width){window!.MinWidth=width;window.Width=width;window.Height=720;await Task.Delay(70);window.UpdateLayout();}
            async Task Click(Button button){Check(button.IsEnabled,"Attempt to click a disabled control");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);window!.UpdateLayout();}
            string Glyph(Button button)=>((TextBlock)button.Content).Text;
            void Fits()
            {
                var grid=window!.PanelGrid;Check(Math.Abs(grid.ColumnDefinitions.Sum(c=>c.ActualWidth)-grid.ActualWidth)<2,"Panels overflow client width");
                var b=window.AllDetailsToggle;var p=b.TranslatePoint(new Point(),grid);Check(p.X>=0&&p.X+b.ActualWidth<=grid.ActualWidth+1,"All-details toggle clipped or unreachable");
                Check(window.CurrentWidths!.Results>=120,"Result pane lost readable width");
            }
            void Screenshot(string name)
            {
                window!.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(Path.GetDirectoryName(report)!,name+".png"));encoder.Save(file);
            }
            try
            {
                File.WriteAllText(root+"/海边旅行计划.txt","沿着海岸步道前往观景台，傍晚在海滩看日落。\nPanel toggle fixture.");
                prefs.Volumes[root]=Native.Identify(root)!;app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();window.EnableCloudLayoutFixture();window.Left=window.Top=0;await Size(1800);
                var hit=new Hit{Workspace="fixture",RootPath=root,FullPath=root+"/海边旅行计划.txt",VirtualPath="soma://fixture/海边旅行计划.txt",Snippet="沿着海岸步道前往观景台，傍晚在海滩看日落。"};
                var scope=new Dictionary<string,bool>{{root,true}};window.SeedResults(new[]{hit},scope);window.VerificationSelect(0);
                for(int i=0;i<100&&!window.VerificationPreviewText.Contains("Panel toggle fixture");i++)await Task.Delay(30);
                Check(window.VerificationPreviewText.Contains("Panel toggle fixture"),"Local synthetic preview did not load");
                var details=window.VerificationDetails;var all=window.AllDetailsToggle;var right=details.RightToggle;
                var tree=window.PanelGrid.Children.OfType<Button>().Single(b=>Grid.GetColumn(b)==1);
                var summary=CloudVerification.Descendants<SummaryTextView>(window).Single();var preview=((PreviewHost)window.PreviewSurface).Content;long requests=app.Backend.RequestCount;

                await Test("initial search breadth is 50 in settings and new windows while saved user values survive",async()=>
                {
                    Check(new Preferences().DefaultSearchBreadth==50&&new SettingsDraft().DefaultSearchBreadth=="50"&&window.VerificationBreadth==50,"Initial search breadth not 50");
                    Check(JsonSerializer.Deserialize<Preferences>("{}")!.DefaultSearchBreadth==50&&JsonSerializer.Deserialize<Preferences>("{\"DefaultSearchBreadth\":20}")!.DefaultSearchBreadth==20,"Missing-field default or saved user value overwritten");
                    var settings=new SettingsWindow(app);settings.Show();await Task.Delay(50);CloudVerification.Descendants<TabControl>(settings).Single().SelectedIndex=2;await Task.Delay(50);
                    try{Check(CloudVerification.Descendants<TextBox>(settings).Single(t=>AutomationProperties.GetName(t)=="默认搜索广度").Text=="50","Settings UI does not show 50");}finally{settings.Close();}
                    var savedStore=new Store(root+"/breadth-data",root+"/breadth-config",root+"/breadth-state");savedStore.Save(new Preferences{StartupSync=false,DefaultSearchBreadth=37});Check(savedStore.Load().DefaultSearchBreadth==37,"Saved breadth overwritten on load");
                    try
                    {
                        foreach(int value in new[]{20,73,-1})
                        {
                            prefs.DefaultSearchBreadth=value;var second=new SearchWindow(app,scope);
                            try{Check(second.VerificationBreadth==(value>0?value:50),"Saved value or invalid fallback incorrect");Check(window.VerificationBreadth==50,"Existing window value changed");}
                            finally{second.SkipExitOnce=true;second.Close();}
                        }
                    }
                    finally{prefs.DefaultSearchBreadth=50;}
                });
                await Test("right controls use right-to-collapse and left-to-expand while tree keeps its original directions",async()=>
                {
                    Check(Glyph(all)=="\uE76C"&&Glyph(right)=="\uE76C"&&Glyph(tree)=="\uE76B","Incorrect initial arrow directions");
                    Check(Grid.GetColumn(all)==3&&Grid.GetColumn(details.ControlRail)==5,"Control placed on wrong divider");
                    await Click(tree);Check(Glyph(tree)=="\uE76C"&&window.CurrentWidths!.Tree==0,"Tree expand action/direction changed");
                    await Click(tree);Check(Glyph(tree)=="\uE76B"&&window.CurrentWidths!.Tree>0,"Tree collapse action/direction changed");Fits();
                    Check(AutomationProperties.GetName(all)=="收起右侧全部视图"&&AutomationProperties.GetName(right).Contains("收起最右侧"),"Accessibility labels do not describe action");
                    prefs.SearchLayout.Theme="Light";app.ApplyAppearance();await Task.Delay(60);Screenshot("pane-open-light");
                });
                await Test("rightmost toggle follows visual order after swapping without hiding the other pane",async()=>
                {
                    await Click(right);Check(!details.RightExpanded&&!details.ContentVisible(DetailKind.Preview)&&details.ContentVisible(DetailKind.Summary),"Right-only collapse hid wrong pane");
                    Check(Glyph(right)=="\uE76B"&&Glyph(all)=="\uE76C"&&right.ToolTip.ToString()!.Contains("展开最右侧的内容预览"),"Collapsed right action wrong");
                    prefs.SearchLayout.Theme="Dark";app.ApplyAppearance();await Task.Delay(50);Screenshot("pane-right-hidden-dark");
                    await Click(right);details.Swap();await Task.Delay(50);await Click(right);
                    Check(details.RightKind==DetailKind.Summary&&!details.ContentVisible(DetailKind.Summary)&&details.ContentVisible(DetailKind.Preview),"Swapped rightmost control targets a fixed content kind");
                    await Click(right);details.Swap();await Task.Delay(50);
                });
                await Test("group toggle restores prior summary-only preview-only and dual layouts including swapped order and widths",async()=>
                {
                    foreach(var state in new[]{(DetailKind.Summary,false),(DetailKind.Preview,false),(DetailKind.Preview,true),(DetailKind.Summary,true)})
                    {
                        if(details.LeftKind!=state.Item1){details.Swap();await Task.Delay(50);}if(details.RightExpanded!=state.Item2)await Click(right);
                        var widths=window.PanelGrid.ColumnDefinitions.Select(c=>c.ActualWidth).ToArray();double before=window.ActiveListWidth;
                        await Click(all);Check(details.AllCollapsed&&details.Mode==DetailMode.Hidden&&!details.ContentVisible(DetailKind.Summary)&&!details.ContentVisible(DetailKind.Preview),"Some right pane remained visible");
                        Check(!details.ControlRail.IsVisible&&all.IsVisible&&Glyph(all)=="\uE76B"&&window.ActiveListWidth>before+100,"Hidden rail not released to results or reopen control missing");
                        Check(all.ToolTip.ToString()!.Contains("恢复收起前布局"),"Restore action not explained");Fits();
                        if(state==(DetailKind.Summary,false))Screenshot("pane-all-hidden-dark");
                        await Click(all);
                        Check(!details.AllCollapsed&&details.LeftKind==state.Item1&&details.RightExpanded==state.Item2&&details.ContentVisible(state.Item1)&&details.ContentVisible(details.RightKind)==state.Item2,"Prior single/dual layout or order was overwritten");
                        Check(widths.Zip(window.PanelGrid.ColumnDefinitions,(beforeWidth,column)=>Math.Abs(beforeWidth-column.ActualWidth)<1).All(s=>s),"Restore changed pane widths");
                        Check(Glyph(right)==(state.Item2?"\uE76C":"\uE76B"),"Restored right arrow describes wrong action");
                        Check(ReferenceEquals(summary,CloudVerification.Descendants<SummaryTextView>(window).Single())&&ReferenceEquals(preview,((PreviewHost)window.PreviewSurface).Content),"Content recreated on toggle");
                        if(!state.Item2)Screenshot(state.Item1==DetailKind.Summary?"pane-restored-summary-only":"pane-restored-preview-only");
                    }
                });
                await Test("detailed summary and waterfall views preserve result selection scope and cached preview without backend requests",async()=>
                {
                    foreach(int view in new[]{0,1,2})
                    {
                        window.SetView(view);await Task.Delay(70);await Click(all);await Click(all);
                        Check(ReferenceEquals(window.SelectedHit,hit)&&window.DisplayedHits.Count==1&&window.Scope().SequenceEqual(scope),"View toggle changed result/selection/scope");
                        Check(ReferenceEquals(preview,((PreviewHost)window.PreviewSurface).Content),"Cached preview replaced");Fits();
                    }
                    Check(app.Backend.RequestCount==requests,"Panel toggles issued a Soma request");window.SetView(0);
                });
                await Test("manual collapse survives resize while automatic narrow tabs and hidden layouts remain reversible",async()=>
                {
                    await Click(all);
                    foreach(double width in new[]{1800d,900,600,480,900,1800}){await Size(width);Check(details.AllCollapsed&&details.Mode==DetailMode.Hidden&&Glyph(all)=="\uE76B"&&all.IsVisible,"Resize overrode manual collapse");Fits();}
                    await Click(all);await Size(900);Check(details.Mode==DetailMode.Tabs&&Glyph(all)=="\uE76C","Tabs mode not preserved");
                    details.SelectTab(DetailKind.Preview);await Task.Delay(40);await Click(all);await Click(all);Check(details.Mode==DetailMode.Tabs&&details.RightExpanded&&details.ActiveTab==DetailKind.Preview&&details.ContentVisible(DetailKind.Preview),"Restore in narrow window lost selected tab");
                    await Size(480);Check(details.Mode==DetailMode.Hidden&&!all.IsEnabled&&Glyph(all)=="\uE76B"&&all.ToolTip.ToString()!.Contains("窗口较窄")&&ToolTipService.GetShowOnDisabled(all),"Too-narrow state silently offers an ineffective action");Fits();
                    await Size(1800);Check(details.Mode==DetailMode.SideBySide&&all.IsEnabled&&!details.AllCollapsed,"Automatic hide persisted after widening");
                });
                await Test("result and detail dividers still resize only their adjacent panes with opaque accessible controls",async()=>
                {
                    foreach(var divider in window.PanelGrid.Children.OfType<AdjacentDivider>().Where(d=>Grid.GetColumn(d)>=3))
                    {
                        window.UpdateLayout();var before=window.PanelGrid.ColumnDefinitions.Select(c=>c.ActualWidth).ToArray();int column=Grid.GetColumn(divider),a=column-1,b=column+1;
                        divider.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});divider.Move(12);window.UpdateLayout();var after=window.PanelGrid.ColumnDefinitions.Select(c=>c.ActualWidth).ToArray();
                        Check(Math.Abs(after[a]-before[a]-12)<1&&Math.Abs(after[b]-before[b]+12)<1,"Adjacent width exchange broken");Check(Enumerable.Range(0,before.Length).Where(i=>i!=a&&i!=b).All(i=>Math.Abs(before[i]-after[i])<1),"Unrelated column resized");
                        divider.RaiseEvent(new DragCompletedEventArgs(12,0,false){RoutedEvent=Thumb.DragCompletedEvent});await Task.Delay(40);
                    }
                    foreach(var theme in new[]{"Light","Dark"}){prefs.SearchLayout.Theme=theme;app.ApplyAppearance();await Task.Delay(50);Check(new[]{all,right}.All(b=>b.Width>=40&&b.Height>=40&&b.Background?.Opacity==1&&((TextBlock)b.Content).FontSize>=20),"Small or transparent control");Fits();}
                });
                await Test("all-details toggle leaves detached windows alive and docking reveals its pane",async()=>
                {
                    details.Float(DetailKind.Preview);await Task.Delay(80);var floating=details.FloatingWindow(DetailKind.Preview)!;Check(!right.IsEnabled,"Detached right toggle should stay disabled");
                    await Click(all);Check(floating.IsVisible&&ReferenceEquals(floating,details.FloatingWindow(DetailKind.Preview))&&details.ContentVisible(DetailKind.Preview),"Collapse-all closed or hid independent window");
                    await Click(all);Check(floating.IsVisible&&details.IsDetached(DetailKind.Preview),"Expand-all forcibly docked independent window");
                    await Click(all);floating.Close();await Task.Delay(120);Check(!details.AllCollapsed&&!details.IsDetached(DetailKind.Preview)&&details.ContentVisible(DetailKind.Preview),"Independent X did not return to visible main pane");
                });
                await Test("toggle state is per search window and repeated transitions keep original tree control",async()=>
                {
                    await Click(all);
                    foreach(var layout in new[]{DetailLayout.SummaryOnly,DetailLayout.PreviewOnly,DetailLayout.Both})
                    {
                        prefs.DefaultDetailLayout=layout;var second=new SearchWindow(app,scope);second.EnableCloudLayoutFixture();second.MinWidth=1800;second.Width=1800;second.Show();await Task.Delay(70);
                        try
                        {
                            var d=second.VerificationDetails;var initialLeft=d.LeftKind;var initialRight=d.RightExpanded;
                            Check(!d.AllCollapsed&&d.ContentVisible(initialLeft),"New window inherited hidden details");Check(details.AllCollapsed,"Opening another window changed first window");
                            await Click(second.AllDetailsToggle);await Click(second.AllDetailsToggle);
                            Check(d.LeftKind==initialLeft&&d.RightExpanded==initialRight&&d.ContentVisible(initialLeft)&&d.ContentVisible(d.RightKind)==initialRight,"Default single/dual configuration not restored");
                        }
                        finally{second.SkipExitOnce=true;second.Close();}
                    }
                    prefs.DefaultDetailLayout=DetailLayout.Both;
                    await Click(all);for(int i=0;i<12;i++){await Click(all);await Click(all);}
                    Check(ReferenceEquals(tree,window.PanelGrid.Children.OfType<Button>().Single(b=>Grid.GetColumn(b)==1))&&Glyph(tree)=="\uE76B","Original tree toggle changed");Check(ReferenceEquals(window.SelectedHit,hit)&&app.Backend.RequestCount==requests,"Repeated toggles changed search");Fits();
                });
            }
            catch(Exception ex){failures++;tests.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally
            {
                if(window!=null){window.SkipExitOnce=true;window.Close();}app.Backend.Dispose();
                Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,scope="Only right-pane arrows and group toggle, directly affected layouts/views/docking. Real published WPF controls with routed clicks and offscreen geometry; no physical input or monitor test. Original tree button checked. No Soma or indexing."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);
            }
        };
        return app.Run();
    }
}
