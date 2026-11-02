// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace SomaFinder;

public sealed partial class SearchWindow
{
    readonly MasonryResults waterfall=new();
    readonly ListBox summaryResults=new(){SelectionMode=SelectionMode.Single,BorderThickness=new Thickness(0)};
    readonly ComboBox sortChoice=new(){Width=176,Margin=new Thickness(4)},viewChoice=new(){Width=108,Margin=new Thickness(4)};
    readonly TextBlock emptyState=UI.Text("",14),fileInfo=UI.Text("",12);
    ContextMenu? columnsMenu;
    Button rangeSearch=null!;bool bindingResults,syncSort;Grid resultArea=null!;
    readonly Stopwatch searchClock=new();
    readonly DispatcherTimer waitTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly DispatcherTimer layoutTimer=new(){Interval=TimeSpan.FromMilliseconds(450)};
    readonly string[] sortKeys={"",nameof(Hit.Name),nameof(Hit.Folder),nameof(Hit.Bytes),nameof(Hit.Type),nameof(Hit.Created),nameof(Hit.Modified)};
    Button reverseSort=null!;
    ScrollViewer? surface;
    DockPanel treePanel=null!;AdjacentDivider treeSplit=null!,resultSplit=null!;Button treeToggle=null!,detailsToggle=null!;
    bool treeVisible=true,resizing;MonitorLayout? monitorLayout;
    internal PaneWidths? CurrentWidths {get;private set;}
    internal Grid PanelGrid=>panels;
    internal void ToggleTree()=>treeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal Button AllDetailsToggle=>detailsToggle;
    internal void SetResultQuery(string value){resultQuery=value;ShowSelection();}
    internal bool SummaryView=>viewChoice.SelectedIndex==1;
    internal bool WaterfallView=>viewChoice.SelectedIndex==2;
    internal MasonryResults Waterfall=>waterfall;
    internal string FileInfoText=>fileInfo.Text;
    internal IReadOnlyList<Hit> DisplayedHits=>results.ItemsSource is IEnumerable<Hit> hits?hits.ToList():new List<Hit>();
    internal Hit? SelectedHit=>results.SelectedItem as Hit;
    internal double ActiveListWidth=>WaterfallView?waterfall.ActualWidth:SummaryView?summaryResults.ActualWidth:results.ActualWidth;
    internal FrameworkElement ActiveListSurface=>WaterfallView?waterfall:SummaryView?summaryResults:results;
    internal FrameworkElement PreviewSurface=>preview;
    internal bool RangeNeedsSearch=>rangeSearch.Visibility==Visibility.Visible;
    internal SearchLayout LayoutPrefs=>app.Backend.Prefs.SearchLayout;
    internal void PersistPanelLayout()=>QueueLayoutSave(false,true,true);
    internal void EnableCloudLayoutFixture(){if(!CloudSandbox.Active)throw new InvalidOperationException();monitorLayout?.Dispose();monitorLayout=null;}
    internal void SetView(bool summary)=>viewChoice.SelectedIndex=summary?1:0;
    internal void SetView(int view)=>viewChoice.SelectedIndex=view;
    internal void SetSort(int field,bool reverse=false){syncSort=true;sortChoice.SelectedIndex=field;syncSort=false;descending=reverse;sortOriginal=field==0;sortField=sortKeys[field];SyncSortControl();ApplyFilter();}
    internal DataGrid ResultGrid=>results;
    internal void SeedResults(IEnumerable<Hit> hits,IReadOnlyDictionary<string,bool> scope){selection=new(scope,StringComparer.OrdinalIgnoreCase);snapshot=Scope();candidates=hits.ToList();browsing=false;UpdateChecks(tree.Items);ApplyFilter();}
    internal void SetScope(IReadOnlyDictionary<string,bool> scope){selection=new(scope,StringComparer.OrdinalIgnoreCase);ApplyFilter();}

    void BuildSurface()
    {
        Title="Soma Finder";Width=Math.Min(1500,SystemParameters.WorkArea.Width*.94);Height=Math.Min(860,SystemParameters.WorkArea.Height*.9);
        MinWidth=Math.Min(600,SystemParameters.WorkArea.Width/2);MinHeight=Math.Min(420,SystemParameters.WorkArea.Height);
        WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=new FontFamily("Segoe UI Variable, Microsoft YaHei UI");FontSize=14;
        ThemeMode=ParseTheme(app.Backend.Prefs.SearchLayout.Theme);
        var frame=new DockPanel();frame.SetResourceReference(Panel.BackgroundProperty,"ApplicationBackgroundBrush");Content=frame;
        var menu=new Menu{Padding=new Thickness(8,2,8,2)};var file=new MenuItem{Header="窗口"};
        file.Items.Add(UI.Menu("新建搜索窗口",app.NewWindow));file.Items.Add(UI.Menu("设置",app.Settings));file.Items.Add(new Separator());file.Items.Add(UI.Menu("退出 Soma Finder",async()=>await app.Quit(false)));menu.Items.Add(file);
        var display=new MenuItem{Header="视图"};display.Items.Add(UI.Menu("详细列表",()=>SetView(false)));display.Items.Add(UI.Menu("摘要列表",()=>SetView(true)));display.Items.Add(UI.Menu("瀑布流",()=>SetView(2)));display.Items.Add(UI.Menu("选择显示列…",()=>{if(columnsMenu!=null){columnsMenu.PlacementTarget=results;columnsMenu.IsOpen=true;}}));display.Items.Add(new Separator());
        display.Items.Add(UI.Menu("紧凑密度",()=>SetDensity(false)));display.Items.Add(UI.Menu("舒适密度",()=>SetDensity(true)));menu.Items.Add(display);
        menu.Items.Add(UI.Menu("设置",app.Settings));DockPanel.SetDock(menu,Dock.Top);frame.Children.Add(menu);
        var top=new Grid{Margin=new Thickness(12,6,12,4)};top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var queryHost=new Grid();query.FontSize=17;query.MinHeight=42;query.Padding=new Thickness(12,6,12,6);query.ToolTip="Enter 搜索；留空可浏览已收录文件";
        System.Windows.Automation.AutomationProperties.SetName(query,"搜索内容");
        var placeholder=UI.Text("描述你要找的文件内容…",16);placeholder.Margin=new Thickness(18,0,0,0);placeholder.Opacity=.6;placeholder.IsHitTestVisible=false;
        query.TextChanged+=(_,_)=>placeholder.Visibility=query.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        queryHost.Children.Add(query);queryHost.Children.Add(placeholder);
        query.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter&&!e.IsRepeat&&e.ImeProcessedKey==Key.None){e.Handled=true;await Search();}};
        search=UI.Button("搜索",async()=>await Search());search.MinHeight=42;search.MinWidth=96;search.SetResourceReference(StyleProperty,"AccentButtonStyle");Grid.SetColumn(search,1);top.Children.Add(queryHost);top.Children.Add(search);DockPanel.SetDock(top,Dock.Top);frame.Children.Add(top);
        var filters=new WrapPanel{Margin=new Thickness(12,0,12,4)};
        mode.Width=116;mode.ItemsSource=new[]{"混合搜索","关键词搜索","语义搜索"};mode.SelectedIndex=0;
        type.ItemsSource=new[]{"全部","文档","图片","音频","视频","压缩包","代码","自定义"};type.SelectedIndex=0;
        foreach(var item in new UIElement[]{mode,Labelled("类型",type),custom,Labelled("搜索广度",limit)})filters.Children.Add(item);
        custom.Visibility=Visibility.Collapsed;custom.ToolTip="例如 pdf;docx 或 .pdf .docx";limit.ToolTip="所选目录的合计候选额度，按文件数量分配；范围较多时会自动提高最低额度。文件类型仅筛选返回结果。";
        sortChoice.ItemsSource=new[]{"Soma 排序","名称","路径","大小","类型","创建日期","修改时间"};sortChoice.SelectedIndex=0;
        sortChoice.SelectionChanged+=(_,_)=>{if(syncSort)return;sortOriginal=sortChoice.SelectedIndex==0;sortField=sortKeys[Math.Max(0,sortChoice.SelectedIndex)];descending=false;SyncSortControl();ApplyFilter();};
        reverseSort=IconButton("\uE74A",()=>{if(sortOriginal)return;descending=!descending;SyncSortControl();ApplyFilter();},"切换升序／降序；Soma 排序保持原始顺序");
        filters.Children.Add(sortChoice);filters.Children.Add(reverseSort);
        viewChoice.ItemsSource=new[]{"详细列表","摘要列表","瀑布流"};viewChoice.SelectedIndex=0;viewChoice.ToolTip="切换显示方式，不重新搜索";
        viewChoice.SelectionChanged+=(_,_)=>{if(resultArea==null)return;var selected=results.SelectedItem;results.Visibility=SummaryView||WaterfallView?Visibility.Collapsed:Visibility.Visible;summaryResults.Visibility=SummaryView?Visibility.Visible:Visibility.Collapsed;waterfall.Visibility=WaterfallView?Visibility.Visible:Visibility.Collapsed;waterfall.SetItems(DisplayedHits,resultQuery,app.Backend.Prefs);if(selected!=null){summaryResults.SelectedItem=selected;waterfall.Select((Hit)selected,true);if(SummaryView)summaryResults.ScrollIntoView(selected);else if(!WaterfallView)results.ScrollIntoView(selected);}};filters.Children.Add(viewChoice);cancelWait=UI.Button("取消等待",()=>searchWait?.Cancel());cancelWait.Visibility=Visibility.Collapsed;lexicalAlternative=UI.Button("改用关键词搜索",async()=>{searchWait?.Cancel();mode.SelectedIndex=1;await Search();});lexicalAlternative.Visibility=Visibility.Collapsed;filters.Children.Add(cancelWait);filters.Children.Add(lexicalAlternative);
        DockPanel.SetDock(filters,Dock.Top);frame.Children.Add(filters);
        rangeSearch=UI.Button("按当前范围搜索",async()=>await Search());rangeSearch.Visibility=Visibility.Collapsed;
        var notice=new DockPanel{Margin=new Thickness(12,0,12,2)};DockPanel.SetDock(rangeSearch,Dock.Right);notice.Children.Add(rangeSearch);hint.FontSize=12;notice.Children.Add(hint);DockPanel.SetDock(notice,Dock.Top);frame.Children.Add(notice);
        type.SelectionChanged+=(_,_)=>{custom.Visibility=type.SelectedIndex==7?Visibility.Visible:Visibility.Collapsed;ApplyFilter();};custom.TextChanged+=(_,_)=>ApplyFilter();
        var footer=new Grid{Margin=new Thickness(12,5,12,7)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        status.FontSize=count.FontSize=12;status.TextTrimming=TextTrimming.CharacterEllipsis;hint.TextWrapping=TextWrapping.Wrap;count.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(count,1);footer.Children.Add(status);footer.Children.Add(count);DockPanel.SetDock(footer,Dock.Bottom);frame.Children.Add(footer);
        var prefs=app.Backend.Prefs.SearchLayout;treeWidth=SafeWidth(prefs.TreeWidth,190,140,380);
        foreach(var column in new[]{new ColumnDefinition{Width=new GridLength(treeWidth),MinWidth=140},new ColumnDefinition{Width=new GridLength(40)},new ColumnDefinition{Width=new GridLength(320),MinWidth=120},new ColumnDefinition{Width=new GridLength(ResponsiveLayout.Divider)},new ColumnDefinition(),new ColumnDefinition(),new ColumnDefinition()})panels.ColumnDefinitions.Add(column);
        treePanel=new DockPanel();var treeTitle=UI.Text("搜索范围",15);treeTitle.FontWeight=FontWeights.SemiBold;treeTitle.Margin=new Thickness(12,10,4,6);DockPanel.SetDock(treeTitle,Dock.Top);treePanel.Children.Add(treeTitle);treePanel.Children.Add(tree);tree.Margin=new Thickness(4);panels.Children.Add(treePanel);
        treeSplit=new AdjacentDivider(panels,0,2,()=>QueueLayoutSave(true,false));Grid.SetColumn(treeSplit,1);panels.Children.Add(treeSplit);
        treeToggle=IconButton("\uE76B",()=>{treeVisible=!treeVisible;treePanel.Visibility=treeVisible?Visibility.Visible:Visibility.Collapsed;treeSplit.IsEnabled=treeVisible;treeToggle.Content=Glyph(treeVisible?"\uE76B":"\uE76C");ResizeSurface();},"展开／收起目录树；边线可拖动调整宽度");treeToggle.Width=36;treeToggle.Height=40;treeToggle.SetResourceReference(Control.BackgroundProperty,"ApplicationBackgroundBrush");Grid.SetColumn(treeToggle,1);panels.Children.Add(treeToggle);
        var listPanel=new DockPanel();more=UI.Button("加载更多已收录文件",()=>{browsePage++;ApplyFilter();_=Metadata(candidates.Take(browsePage*500).ToList(),requestVersion);});more.Visibility=Visibility.Collapsed;DockPanel.SetDock(more,Dock.Bottom);listPanel.Children.Add(more);
        resultArea=new Grid();resultArea.Children.Add(results);resultArea.Children.Add(summaryResults);waterfall.Visibility=Visibility.Collapsed;resultArea.Children.Add(waterfall);emptyState.Margin=new Thickness(28);emptyState.VerticalAlignment=VerticalAlignment.Center;emptyState.HorizontalAlignment=HorizontalAlignment.Center;emptyState.MaxWidth=420;emptyState.IsHitTestVisible=false;resultArea.Children.Add(emptyState);listPanel.Children.Add(resultArea);Grid.SetColumn(listPanel,2);panels.Children.Add(listPanel);
        resultSplit=new AdjacentDivider(panels,2,4,()=>QueueLayoutSave(false,true));Grid.SetColumn(resultSplit,3);panels.Children.Add(resultSplit);
        snippet.Document.TextAlignment=TextAlignment.Left;snippet.VerticalContentAlignment=VerticalAlignment.Top;snippet.Padding=new Thickness(14);snippet.FontSize=14;
        var summaryBody=new DockPanel();var operations=new WrapPanel{Margin=new Thickness(8,0,8,6)};operations.Children.Add(UI.Button("打开文件",()=>OpenSelected(false)));operations.Children.Add(UI.Button("所在位置",()=>OpenSelected(true)));DockPanel.SetDock(operations,Dock.Top);summaryBody.Children.Add(operations);
        fileInfo.Margin=new Thickness(14,2,14,8);fileInfo.Opacity=.8;DockPanel.SetDock(fileInfo,Dock.Top);summaryBody.Children.Add(fileInfo);summaryBody.Children.Add(snippet);zoomReset=UI.Button("100%",()=>{summaryZoom=1;ZoomSummary(1);});zoomReset.ToolTip="恢复摘要字号为 100%；正文支持双指缩放";operations.Children.Add(zoomReset);
        details=new DetailPanels(this,panels,summaryBody,preview,app.Backend.Prefs.DefaultDetailLayout,()=>app.MarkRecent(this),()=>app.Backend.Exiting);
        detailsToggle=DetailPanels.RailButton("\uE76C",()=>details.ToggleAll(),"收起右侧全部视图");
        ToolTipService.SetShowOnDisabled(detailsToggle,true);
        detailsToggle.VerticalAlignment=VerticalAlignment.Center;detailsToggle.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumn(detailsToggle,3);panels.Children.Add(detailsToggle);
        details.Reparenting+=snippet.PreserveViewport;
        details.Changed+=()=>{ResizeSurface();if(!closing)_=Preview();};
        surface=new ScrollViewer{Content=panels,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};surface.SizeChanged+=(_,_)=>ResizeSurface();frame.Children.Add(surface);ResizeSurface();
        ConfigureResults();ConfigureInputs();waterfall.SelectionChanged+=hit=>{if(bindingResults)return;results.SelectedItem=hit;};waterfall.OpenRequested+=()=>OpenSelected(false);waterfall.ZoomChanged+=_=>{layoutTimer.Stop();layoutTimer.Start();};ResultThumbnails.Updated+=ThumbnailUpdated;ApplyDensity();ApplyTheme();monitorLayout=new MonitorLayout(this,()=>ResizeSurface());
        waitTimer.Tick+=(_,_)=>{if(loadingSearch)UpdateStatus();else waitTimer.Stop();};layoutTimer.Tick+=(_,_)=>{layoutTimer.Stop();SaveLayout();};
        Closing+=async(_,e)=>{if(closing||app.Backend.Exiting)return;if(SkipExitOnce){SkipExitOnce=false;closing=true;return;}e.Cancel=true;if(await app.ClosingWindow(this)){closing=true;requestVersion++;previewVersion++;if(!app.Backend.Exiting)_=Dispatcher.BeginInvoke(()=>Close());}};
        Closed+=(_,_)=>{ResultThumbnails.Updated-=ThumbnailUpdated;selectionThumbnail?.Cancel();monitorLayout?.Dispose();closing=true;requestVersion++;previewVersion++;searchWait?.Cancel();waitTimer.Stop();layoutTimer.Stop();SaveLayout();};ReloadTree();Loaded+=(_,_)=>{ApplyTheme();query.Focus();};
    }
    static FrameworkElement Labelled(string label,UIElement input){var p=new StackPanel{Orientation=Orientation.Horizontal};p.Children.Add(UI.Text(label,12));p.Children.Add(input);return p;}
    internal static TextBlock Glyph(string text)=>new(){Text=text,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=15,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
    static Button IconButton(string icon,Action action,string tip){var b=UI.Button("",action);b.Content=Glyph(icon);b.Padding=new Thickness(6);b.MinWidth=28;b.Margin=new Thickness(1);b.ToolTip=tip;System.Windows.Automation.AutomationProperties.SetName(b,tip);return b;}
    internal static double SafeWidth(double value,double fallback,double min,double max)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback;
    internal static ThemeMode ParseTheme(string value)=>value=="Light"?ThemeMode.Light:value=="Dark"?ThemeMode.Dark:ThemeMode.System;
    internal void ApplyTheme()
    {
        var theme=ParseTheme(app.Backend.Prefs.SearchLayout.Theme);if(ThemeMode!=theme)ThemeMode=theme;details?.ApplyTheme(theme);ApplyDensity();snippet.MatchStyle=LayoutPrefs.MatchStyle;summaryResults.Resources["MatchStyle"]=LayoutPrefs.MatchStyle;
        // Row and text backgrounds must follow the live palette rather than a style
        // captured before the window switched from a light theme to a dark theme.
        results.SetResourceReference(DataGrid.RowBackgroundProperty,"ApplicationBackgroundBrush");
        results.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,"ApplicationBackgroundBrush");
        foreach(var control in new Control[]{results,summaryResults,snippet})
        {control.SetResourceReference(Control.BackgroundProperty,"ApplicationBackgroundBrush");control.SetResourceReference(Control.ForegroundProperty,"TextFillColorPrimaryBrush");}
    }
    void SetDensity(bool comfortable){app.Backend.Prefs.SearchLayout.Comfortable=comfortable;app.Backend.Save();foreach(var window in app.Windows.OfType<SearchWindow>())window.ApplyDensity();}
    void ApplyDensity(){bool comfy=app.Backend.Prefs.SearchLayout.Comfortable;results.RowHeight=comfy?40:30;summaryResults.Resources["SummaryCardMargin"]=new Thickness(4,comfy?4:2,4,comfy?4:2);summaryResults.Resources["SummaryCardPadding"]=new Thickness(comfy?12:10,comfy?10:7,comfy?12:10,comfy?10:7);}
    void ResizeSurface()
    {
        if(resizing||surface==null||surface.ActualWidth<1)return;resizing=true;
        try
        {
            var available=ResponsiveLayout.Calculate(surface.ActualWidth,treeVisible,treeWidth,details.PreferredLeftWidth,details.PreferredRightWidth,details.MinimumLeft,details.MinimumRight);
            var w=details.AllCollapsed?ResponsiveLayout.Calculate(surface.ActualWidth,treeVisible,treeWidth,details.PreferredLeftWidth,details.PreferredRightWidth,details.MinimumLeft,details.MinimumRight,true):available;
            CurrentWidths=w;panels.MinWidth=0;panels.Width=surface.ActualWidth;
            panels.ColumnDefinitions[0].MinWidth=Math.Min(140,w.Tree);panels.ColumnDefinitions[0].Width=new GridLength(w.Tree);
            panels.ColumnDefinitions[1].Width=new GridLength(40);panels.ColumnDefinitions[2].MinWidth=Math.Min(300,w.Results);panels.ColumnDefinitions[2].Width=new GridLength(w.Results);
            panels.ColumnDefinitions[3].Width=new GridLength(ResponsiveLayout.Divider);
            resultSplit.IsEnabled=w.Mode!=DetailMode.Hidden;
            details.ApplyResponsive(w);
            detailsToggle.IsEnabled=w.Mode!=DetailMode.Hidden||available.Mode!=DetailMode.Hidden;
            detailsToggle.Content=Glyph(w.Mode==DetailMode.Hidden?"\uE76B":"\uE76C");((TextBlock)detailsToggle.Content).FontSize=20;
            detailsToggle.ToolTip=detailsToggle.IsEnabled?(w.Mode==DetailMode.Hidden?"展开右侧视图（恢复收起前布局）":"收起右侧全部视图"):"窗口较窄，请放大窗口后展开右侧视图";
            System.Windows.Automation.AutomationProperties.SetName(detailsToggle,(string)detailsToggle.ToolTip);
        }
        finally{resizing=false;}
    }
    void QueueLayoutSave(bool treeChanged=false,bool detailChanged=false,bool both=false){if(!resizing){if(treeChanged&&treeVisible&&panels.ColumnDefinitions[0].ActualWidth>=100)treeWidth=panels.ColumnDefinitions[0].ActualWidth;if(detailChanged)details.RecordManualWidths(both);}layoutTimer.Stop();layoutTimer.Start();ResizeSurface();}
    void SaveLayout()
    {
        if(results.Columns.Count==0)return;var p=app.Backend.Prefs.SearchLayout;
        p.TreeWidth=treeWidth;
        details.CaptureWidths(p);p.Columns=results.Columns.Select(c=>new ResultColumn{Key=c.SortMemberPath,Width=c.Width.IsAbsolute?c.Width.Value:c.ActualWidth>1?c.ActualWidth:220,Order=c.DisplayIndex,Visible=c.Visibility==Visibility.Visible}).ToList();
        try{app.Backend.Save();}catch(Exception ex){if(!closing)UI.Error("布局未能保存："+ex.Message);}
    }
    void SyncSortControl()
    {
        syncSort=true;sortChoice.SelectedIndex=sortOriginal?0:Math.Max(0,Array.IndexOf(sortKeys,sortField));syncSort=false;
        if(reverseSort!=null){reverseSort.IsEnabled=!sortOriginal;reverseSort.Content=SortIcon();reverseSort.ToolTip=sortOriginal?"Soma 排序保持原始顺序":descending?"当前：降序；点击改为升序":"当前：升序；点击改为降序";}
        foreach(var column in results.Columns)column.SortDirection=!sortOriginal&&column.SortMemberPath==sortField?(descending?ListSortDirection.Descending:ListSortDirection.Ascending):null;
    }
    string RootCaption(string path,bool root)
    {
        var name=System.IO.Path.GetFileName(path.TrimEnd('\\','/'));if(string.IsNullOrEmpty(name))name=path;
        if(root){var same=app.Backend.Workspaces.SelectMany(w=>w.Roots).Count(r=>System.IO.Path.GetFileName(r.Path.TrimEnd('\\','/')).Equals(name,StringComparison.OrdinalIgnoreCase));if(same>1)name+=" · "+System.IO.Path.GetDirectoryName(path);}
        bool offline=root&&(!Directory.Exists(path)||!Native.Verified(path,app.Backend.Prefs));return "\uD83D\uDCC1 "+name+(offline?" · 离线／待核对":"");
    }
    void ShowSelection()
    {
        if(bindingResults)return;var hit=results.SelectedItem as Hit;summaryResults.Tag=resultQuery;snippet.HighlightQuery=resultQuery;snippet.Text=hit==null?"请选择文件查看 Soma 返回的摘要。":string.IsNullOrWhiteSpace(hit.Snippet)?"Soma 未返回摘要。":hit.Snippet;
        details.UpdateFile(hit?.Name,hit?.FullPath);UpdateFileInfo(hit);LoadSelectionLocation(hit);_=Preview();
    }
    CancellationTokenSource? selectionThumbnail;string selectionStamp="";
    void ThumbnailUpdated(Hit hit){if(!closing&&ReferenceEquals(results.SelectedItem,hit))UpdateFileInfo(hit);}
    async void LoadSelectionLocation(Hit? hit)
    {
        string next=hit==null?"":ResultThumbnails.Stamp(hit,resultQuery);if(next==selectionStamp)return;selectionStamp=next;selectionThumbnail?.Cancel();selectionThumbnail=new();
        if(hit==null||!Rules.TypeMatches(hit.Type,"视频"))return;
        try{await ResultThumbnails.Get(hit,resultQuery,app.Backend.Prefs,selectionThumbnail.Token);}catch(OperationCanceledException){}
    }
    void UpdateFileInfo(Hit? hit){fileInfo.Text=hit==null?"":hit.Type+" · "+hit.Size+"\n"+hit.Folder+"\n"+hit.Availability+(hit.VerifiedTime&&hit.MatchSeconds is double time?"\n匹配字幕片段 · "+ContentLocation.Timestamp(time):"");fileInfo.ToolTip=hit?.VerifiedTime==true?hit.PreviewNote:hit?.FullPath;}
    static DataTemplate RowTemplate(string body)=>(DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:local='clr-namespace:SomaFinder;assembly=SomaFinder'>"+body+"</DataTemplate>");
    void ConfigureResults()
    {
        results.Columns.Add(new DataGridTemplateColumn{Header="名称",SortMemberPath=nameof(Hit.Name),Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=180,CellTemplate=RowTemplate("<DockPanel Margin='6,0'><TextBlock Text='{Binding Glyph}' FontFamily='Segoe MDL2 Assets' Margin='0,0,8,0' VerticalAlignment='Center' Foreground='#3695CA'/><TextBlock Text='{Binding Name}' ToolTip='{Binding FullPath}' TextTrimming='CharacterEllipsis' VerticalAlignment='Center'/></DockPanel>")});
        AddColumn("路径",nameof(Hit.Folder),230);AddColumn("大小",nameof(Hit.Size),94,nameof(Hit.Bytes));AddColumn("类型",nameof(Hit.Type),76);AddColumn("创建日期",nameof(Hit.Creation),146,nameof(Hit.Created));AddColumn("修改时间",nameof(Hit.Modification),146,nameof(Hit.Modified));
        var cellStyle=new Style(typeof(DataGridCell));cellStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,0,5,0)));cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Center));results.CellStyle=cellStyle;

        var header=new FrameworkElementFactory(typeof(TextBlock));header.SetBinding(TextBlock.TextProperty,new Binding("Name"));header.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);header.SetValue(FrameworkElement.MarginProperty,new Thickness(8,6,8,4));var group=new DataTemplate{VisualTree=header};results.GroupStyle.Add(new GroupStyle{HeaderTemplate=group});summaryResults.GroupStyle.Add(new GroupStyle{HeaderTemplate=group});
        results.Sorting+=(_,e)=>{e.Handled=true;SortColumn(e.Column);};
        results.MouseDoubleClick+=(_,e)=>{if(FindRow(e.OriginalSource as DependencyObject,typeof(DataGridRow)))OpenSelected(false);};results.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){OpenSelected(false);e.Handled=true;}};
        results.SelectionChanged+=(_,_)=>{if(bindingResults)return;bindingResults=true;summaryResults.SelectedItem=results.SelectedItem;waterfall.Select(results.SelectedItem as Hit);bindingResults=false;ShowSelection();};
        summaryResults.SelectionChanged+=(_,_)=>{if(bindingResults)return;bindingResults=true;results.SelectedItem=summaryResults.SelectedItem;waterfall.Select(results.SelectedItem as Hit);bindingResults=false;ShowSelection();};
        summaryResults.MouseDoubleClick+=(_,e)=>{if(FindRow(e.OriginalSource as DependencyObject,typeof(ListBoxItem)))OpenSelected(false);};summaryResults.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){OpenSelected(false);e.Handled=true;}};
        summaryResults.ItemTemplate=RowTemplate("<Border Tag='SummaryResultCard' Margin='{DynamicResource SummaryCardMargin}' Padding='{DynamicResource SummaryCardPadding}' CornerRadius='6' BorderThickness='1' BorderBrush='{DynamicResource ControlStrokeColorDefaultBrush}'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='104'/><ColumnDefinition Width='12'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Grid Height='92' VerticalAlignment='Top'><Border CornerRadius='4' Background='{DynamicResource CardBackgroundFillColorDefaultBrush}'/><TextBlock Text='{Binding Glyph}' FontFamily='Segoe MDL2 Assets' FontSize='32' Foreground='#3695CA' HorizontalAlignment='Center' VerticalAlignment='Center'/><Image Source='{Binding Thumbnail}' Stretch='Uniform' local:ThumbnailCache.Enabled='True'/></Grid><StackPanel Grid.Column='2'><TextBlock Text='{Binding Name}' FontWeight='SemiBold' FontSize='15' TextTrimming='CharacterEllipsis' ToolTip='{Binding FullPath}'/><TextBlock Text='{Binding Folder}' Opacity='.75' FontSize='12' TextTrimming='CharacterEllipsis' Margin='0,5,0,6' ToolTip='{Binding FullPath}'/><TextBlock local:MatchText.Text='{Binding ListSnippet}' local:MatchText.Query='{Binding Tag, RelativeSource={RelativeSource AncestorType=ListBox}}' local:MatchText.StyleName='{DynamicResource MatchStyle}' TextWrapping='Wrap' TextTrimming='CharacterEllipsis' MaxHeight='44' LineHeight='22'/></StackPanel></Grid></Border>");
        var listStyle=new Style(typeof(ListBoxItem),TryFindResource(typeof(ListBoxItem)) as Style);listStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));listStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty,VerticalAlignment.Stretch));listStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));listStyle.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0)));listStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0)));listStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,0d));summaryResults.ItemContainerStyle=listStyle;
        summaryResults.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,ScrollBarVisibility.Disabled);summaryResults.SetValue(ScrollViewer.CanContentScrollProperty,true);summaryResults.SetValue(VirtualizingPanel.IsVirtualizingProperty,true);summaryResults.SetValue(VirtualizingPanel.IsVirtualizingWhenGroupingProperty,true);summaryResults.SetValue(VirtualizingPanel.VirtualizationModeProperty,VirtualizationMode.Recycling);results.SetValue(VirtualizingPanel.IsVirtualizingWhenGroupingProperty,true);results.SetValue(ScrollViewer.CanContentScrollProperty,true);summaryResults.Visibility=Visibility.Collapsed;results.ToolTip="方向键选择文件，Enter 打开原文件";summaryResults.ToolTip="方向键选择文件，Enter 打开原文件";
        var context=new ContextMenu();context.Items.Add(UI.Menu("打开文件",()=>OpenSelected(false)));context.Items.Add(UI.Menu("打开所在位置",()=>OpenSelected(true)));context.Items.Add(UI.Menu("复制完整路径",()=>{if(results.SelectedItem is Hit h)Clipboard.SetText(h.FullPath);}));results.ContextMenu=context;
        var summaryMenu=new ContextMenu();summaryMenu.Items.Add(UI.Menu("打开文件",()=>OpenSelected(false)));summaryMenu.Items.Add(UI.Menu("打开所在位置",()=>OpenSelected(true)));summaryMenu.Items.Add(UI.Menu("复制完整路径",()=>{if(results.SelectedItem is Hit h)Clipboard.SetText(h.FullPath);}));summaryResults.ContextMenu=summaryMenu;var waterfallMenu=new ContextMenu();waterfallMenu.Items.Add(UI.Menu("打开文件",()=>OpenSelected(false)));waterfallMenu.Items.Add(UI.Menu("打开所在位置",()=>OpenSelected(true)));waterfallMenu.Items.Add(UI.Menu("复制完整路径",()=>{if(results.SelectedItem is Hit h)Clipboard.SetText(h.FullPath);}));waterfall.ContextMenu=waterfallMenu;
        var rowStyle=new Style(typeof(DataGridRow),TryFindResource(typeof(DataGridRow)) as Style);rowStyle.Setters.Add(new Setter(ToolTipProperty,new Binding(nameof(Hit.Availability))));results.RowStyle=rowStyle;
        var columnMenu=new ContextMenu();columnsMenu=columnMenu;foreach(var column in results.Columns){var item=new MenuItem{Header=column.Header,IsCheckable=true,IsChecked=true};item.Click+=(_,_)=>{if(!item.IsChecked&&results.Columns.Count(c=>c.Visibility==Visibility.Visible)==1){item.IsChecked=true;return;}column.Visibility=item.IsChecked?Visibility.Visible:Visibility.Collapsed;QueueLayoutSave();};columnMenu.Items.Add(item);}
        var headerStyle=new Style(typeof(DataGridColumnHeader),TryFindResource(typeof(DataGridColumnHeader)) as Style);headerStyle.Setters.Add(new Setter(ContextMenuProperty,columnMenu));results.ColumnHeaderStyle=headerStyle;
        var saved=app.Backend.Prefs.SearchLayout.Columns;foreach(var c in results.Columns){var layout=saved.FirstOrDefault(x=>x.Key==c.SortMemberPath);if(layout==null)continue;c.Width=SafeWidth(layout.Width,c.SortMemberPath==nameof(Hit.Name)?220:140,60,1600);c.Visibility=layout.Visible?Visibility.Visible:Visibility.Collapsed;}
        if(!results.Columns.Any(c=>c.Visibility==Visibility.Visible))results.Columns[0].Visibility=Visibility.Visible;
        foreach(var c in results.Columns.OrderBy(c=>saved.FirstOrDefault(x=>x.Key==c.SortMemberPath)?.Order??99).ToList())c.DisplayIndex=Math.Clamp(saved.FirstOrDefault(x=>x.Key==c.SortMemberPath)?.Order??c.DisplayIndex,0,results.Columns.Count-1);
        columnMenu.Opened+=(_,_)=>{for(int i=0;i<results.Columns.Count;i++)((MenuItem)columnMenu.Items[i]).IsChecked=results.Columns[i].Visibility==Visibility.Visible;};
        results.ColumnReordered+=(_,_)=>QueueLayoutSave();results.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>QueueLayoutSave()));SyncSortControl();
    }
    void AddColumn(string title,string path,double width,string? sort=null){var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Left));style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(path)));style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center));results.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(path),Width=width,MinWidth=60,SortMemberPath=sort??path,ElementStyle=style});}
    static bool FindRow(DependencyObject? source,Type type){while(source!=null){if(type.IsInstanceOfType(source))return true;source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);}return false;}
}
