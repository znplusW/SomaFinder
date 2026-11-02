// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace SomaFinder;

internal enum DetailKind { Summary, Preview }

// Content belongs to the search window. Docking only moves its visual parent.
internal sealed class DetailPanels : IDisposable
{
    sealed class Pane
    {
        public required DetailKind Kind;
        public required DockPanel View;
        public required TextBlock FileLabel;
        public required Button PopButton;
        public required StackPanel Placeholder;
        public required StackPanel Actions;
        public Window? Floating;
        public Rect? Bounds;
        public bool Destroying, HiddenByParent;
    }

    readonly SearchWindow owner;
    readonly Grid grid;
    readonly Action activated;
    readonly Func<bool> exiting;
    readonly Pane summary, preview;
    readonly ContentControl leftHost=new(),rightHost=new(),tabContent=new();
    readonly DockPanel tabView=new();readonly StackPanel controls;readonly Button summaryTab,previewTab;
    readonly PreviewHost? previewSource;Button? currentFit;DetailKind activeTab=DetailKind.Summary;
    internal DetailMode Mode {get;private set;}=DetailMode.SideBySide;
    readonly AdjacentDivider splitter;
    readonly Button swapButton,toggleButton;
    readonly Dictionary<DetailKind,double> widths=new(){{DetailKind.Summary,280},{DetailKind.Preview,300}};
    DetailKind left=DetailKind.Summary;
    bool rightOpen,disposed;
    public event Action? Changed;
    public event Action? Reparenting;

    public DetailKind LeftKind=>left;
    public DetailKind RightKind=>left==DetailKind.Summary?DetailKind.Preview:DetailKind.Summary;
    public bool RightExpanded=>rightOpen;
    public bool AllCollapsed {get;private set;}
    public bool RightToggleEnabled=>!disposed&&Get(RightKind).Floating==null;
    public int DetachedCount=>(summary.Floating!=null?1:0)+(preview.Floating!=null?1:0);
    public bool IsDisposed=>disposed;
    public double MinimumLeft=>Get(left).Floating==null?240:62;
    public double MinimumRight=>Get(RightKind).Floating!=null?62:rightOpen?240:0;
    public double PreferredLeftWidth=>Get(left).Floating==null?widths[left]:62;
    public double PreferredRightWidth=>Get(RightKind).Floating!=null?62:rightOpen?widths[RightKind]:0;
    internal DetailKind ActiveTab=>activeTab;
    internal FrameworkElement ControlRail=>controls;
    internal Button RightToggle=>toggleButton;
    public double DockedMinimumWidth=>(Get(left).Floating==null?240:62)+40+(Get(RightKind).Floating!=null?62:rightOpen?240:0);
    public double DockedWidth=>grid.ColumnDefinitions[4].Width.Value+40+grid.ColumnDefinitions[6].Width.Value;
    public void CaptureWidths(SearchLayout layout){layout.SummaryWidth=widths[DetailKind.Summary];layout.PreviewWidth=widths[DetailKind.Preview];}
    public void ApplyTheme(ThemeMode mode){foreach(var pane in new[]{summary,preview})if(pane.Floating!=null&&pane.Floating.ThemeMode!=mode)pane.Floating.ThemeMode=mode;}
    public bool IsDetached(DetailKind kind)=>Get(kind).Floating!=null;
    public Window? FloatingWindow(DetailKind kind)=>Get(kind).Floating;
    public bool ContentVisible(DetailKind kind)
    {
        if(disposed)return false;
        var pane=Get(kind);
        return pane.Floating!=null
            ? !pane.HiddenByParent&&pane.Floating.WindowState!=WindowState.Minimized
            : Mode==DetailMode.Hidden?false:Mode==DetailMode.Tabs?kind==activeTab:kind==left||rightOpen;
    }

    public DetailPanels(SearchWindow owner,Grid grid,UIElement summaryContent,UIElement previewContent,DetailLayout layout,Action activated,Func<bool> exiting)
    {
        this.owner=owner;this.grid=grid;this.activated=activated;this.exiting=exiting;
        widths[DetailKind.Summary]=SearchWindow.SafeWidth(owner.LayoutPrefs.SummaryWidth,270,240,700);
        widths[DetailKind.Preview]=SearchWindow.SafeWidth(owner.LayoutPrefs.PreviewWidth,300,240,800);
        if(!Enum.IsDefined(layout))layout=DetailLayout.SummaryOnly;
        left=layout==DetailLayout.PreviewOnly?DetailKind.Preview:DetailKind.Summary;
        rightOpen=layout==DetailLayout.Both;
        summary=CreatePane(DetailKind.Summary,summaryContent);preview=CreatePane(DetailKind.Preview,previewContent);
        Grid.SetColumn(leftHost,4);Grid.SetColumn(rightHost,6);grid.Children.Add(leftHost);grid.Children.Add(rightHost);
        grid.ColumnDefinitions[5].Width=new GridLength(40);
        splitter=new AdjacentDivider(grid,4,6,()=>owner.PersistPanelLayout());
        Grid.SetColumn(splitter,5);grid.Children.Add(splitter);

        controls=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
        swapButton=RailButton("\uE8AB",Swap,"交换搜索摘要与内容预览的位置");
        toggleButton=RailButton("\uE76B",ToggleRight,"展开最右侧面板");
        controls.Children.Add(swapButton);controls.Children.Add(toggleButton);Grid.SetColumn(controls,5);grid.Children.Add(controls);
        var tabs=new StackPanel{Orientation=Orientation.Horizontal};
        summaryTab=Compact("摘要",()=>SelectTab(DetailKind.Summary),"显示搜索摘要");previewTab=Compact("预览",()=>SelectTab(DetailKind.Preview),"显示内容预览");
        tabs.Children.Add(summaryTab);tabs.Children.Add(previewTab);DockPanel.SetDock(tabs,System.Windows.Controls.Dock.Top);tabView.Children.Add(tabs);tabView.Children.Add(tabContent);
        activeTab=left;previewSource=previewContent as PreviewHost;if(previewSource!=null)previewSource.ContentUpdated+=UpdateFitAction;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        owner.Closed+=OwnerClosed;
        Layout(false);
    }

    static string Name(DetailKind kind)=>kind==DetailKind.Summary?"搜索摘要":"内容预览";
    Pane Get(DetailKind kind)=>kind==DetailKind.Summary?summary:preview;
    static Button Compact(string text,Action action,string tip)
    {
        var button=UI.Button(text,action);button.Padding=new Thickness(6,4,6,4);button.Margin=new Thickness(2);button.MinHeight=30;button.ToolTip=tip;return button;
    }
    internal static Button RailButton(string glyph,Action action,string tip)
    {
        var button=Compact("",action,tip);button.Content=SearchWindow.Glyph(glyph);((TextBlock)button.Content).FontSize=20;
        button.Width=40;button.Height=40;button.Padding=new Thickness(4);button.Margin=new Thickness(2,3,2,3);
        button.SetResourceReference(Control.BackgroundProperty,"ApplicationBackgroundBrush");
        System.Windows.Automation.AutomationProperties.SetName(button,tip);return button;
    }
    void UpdateFitAction()
    {
        if(currentFit!=null)preview.Actions.Children.Remove(currentFit);
        currentFit=(previewSource?.Content as ImagePreviewView)?.FitButton;
        if(currentFit!=null)preview.Actions.Children.Insert(0,currentFit);
    }
    Pane CreatePane(DetailKind kind,UIElement content)
    {
        var view=new DockPanel();
        var header=new DockPanel{Margin=new Thickness(4,2,4,2)};var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,System.Windows.Controls.Dock.Right);header.Children.Add(actions);
        var button=Compact("独立窗口",()=>{if(IsDetached(kind))Dock(kind);else Float(kind);},"在独立窗口中显示此面板");button.FontSize=12;actions.Children.Add(button);var title=UI.Text(Name(kind),15);title.FontWeight=FontWeights.SemiBold;header.Children.Add(title);
        DockPanel.SetDock(header,System.Windows.Controls.Dock.Top);view.Children.Add(header);
        var file=UI.Text("请选择文件",12);file.Margin=new Thickness(8,0,8,6);file.Opacity=.7;file.TextWrapping=TextWrapping.NoWrap;file.TextTrimming=TextTrimming.CharacterEllipsis;DockPanel.SetDock(file,System.Windows.Controls.Dock.Top);view.Children.Add(file);view.Children.Add(content);
        var placeholder=new StackPanel{VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(1,4,1,0)};
        var label=UI.Text(kind==DetailKind.Summary?"摘要\n已独立显示":"预览\n已独立显示",12);label.TextAlignment=TextAlignment.Center;placeholder.Children.Add(label);
        placeholder.Children.Add(Compact("定位",()=>Locate(kind),"显示已独立显示的"+Name(kind)));
        placeholder.Children.Add(Compact("收回主窗口",()=>Dock(kind),"收回到此搜索窗口"));
        return new Pane{Kind=kind,View=view,FileLabel=file,PopButton=button,Placeholder=placeholder,Actions=actions};
    }
    public void RecordManualWidths(bool both)
    {
        if(Mode==DetailMode.Hidden)return;
        if(Mode==DetailMode.Tabs){if(Get(activeTab).Floating==null)widths[activeTab]=Math.Max(240,grid.ColumnDefinitions[4].ActualWidth);return;}
        if(Get(left).Floating==null&&grid.ColumnDefinitions[4].ActualWidth>=160)widths[left]=grid.ColumnDefinitions[4].ActualWidth;
        if(both&&rightOpen&&Get(RightKind).Floating==null&&grid.ColumnDefinitions[6].ActualWidth>=160)widths[RightKind]=grid.ColumnDefinitions[6].ActualWidth;
    }
    void Layout(bool notify=true)
    {
        if(disposed)return;
        Reparenting?.Invoke();
        leftHost.Content=null;rightHost.Content=null;tabContent.Content=null;
        controls.Visibility=Mode==DetailMode.SideBySide?Visibility.Visible:Visibility.Collapsed;
        if(Mode==DetailMode.Tabs)
        {
            leftHost.Content=tabView;leftHost.Visibility=Visibility.Visible;rightHost.Visibility=Visibility.Collapsed;
            var pane=Get(activeTab);tabContent.Content=pane.Floating==null?pane.View:pane.Placeholder;
            summaryTab.FontWeight=activeTab==DetailKind.Summary?FontWeights.Bold:FontWeights.Normal;
            previewTab.FontWeight=activeTab==DetailKind.Preview?FontWeights.Bold:FontWeights.Normal;
        }
        else if(Mode==DetailMode.Hidden){leftHost.Visibility=rightHost.Visibility=Visibility.Collapsed;}
        else
        {
            leftHost.Content=Get(left).Floating==null?Get(left).View:Get(left).Placeholder;
            rightHost.Content=Get(RightKind).Floating==null?Get(RightKind).View:Get(RightKind).Placeholder;
            leftHost.Visibility=Visibility.Visible;rightHost.Visibility=rightOpen||Get(RightKind).Floating!=null?Visibility.Visible:Visibility.Collapsed;
        }
        toggleButton.IsEnabled=Get(RightKind).Floating==null;
        toggleButton.Content=SearchWindow.Glyph(rightOpen?"\uE76C":"\uE76B");((TextBlock)toggleButton.Content).FontSize=20;
        toggleButton.ToolTip=toggleButton.IsEnabled?(rightOpen?"收起":"展开")+"最右侧的"+Name(RightKind):Name(RightKind)+"已独立显示，请先收回";
        System.Windows.Automation.AutomationProperties.SetName(toggleButton,(string)toggleButton.ToolTip);
        splitter.IsEnabled=Mode==DetailMode.SideBySide&&rightOpen&&Get(left).Floating==null&&Get(RightKind).Floating==null;
        splitter.Visibility=Mode==DetailMode.SideBySide?Visibility.Visible:Visibility.Collapsed;
        if(notify)Changed?.Invoke();
    }
    internal void SelectTab(DetailKind kind)
    {
        if(Get(kind).Floating!=null){Locate(kind);return;}
        activeTab=kind;Layout();
    }
    internal void ApplyResponsive(PaneWidths value)
    {
        if(Mode!=value.Mode){if(Mode==DetailMode.SideBySide&&value.Mode==DetailMode.Tabs)activeTab=left;Mode=value.Mode;Layout(false);}
        grid.ColumnDefinitions[4].MinWidth=value.Mode==DetailMode.Hidden?0:value.Mode==DetailMode.Tabs?240:MinimumLeft;
        grid.ColumnDefinitions[5].MinWidth=0;grid.ColumnDefinitions[6].MinWidth=value.Mode==DetailMode.SideBySide?MinimumRight:0;
        grid.ColumnDefinitions[4].Width=new GridLength(value.Left);grid.ColumnDefinitions[5].Width=new GridLength(value.Middle);grid.ColumnDefinitions[6].Width=new GridLength(value.Right);
    }
    public void Swap()
    {
        if(disposed)return;left=RightKind;Layout();
    }
    public void ToggleRight()
    {
        if(!RightToggleEnabled)return;rightOpen=!rightOpen;Layout();
    }
    public void ToggleAll()
    {
        if(disposed)return;
        AllCollapsed=Mode!=DetailMode.Hidden;
        // The group switch changes visibility only; pane order, individual
        // visibility, preferred widths and the active narrow-layout tab survive.
        Layout();
    }
    public void UpdateFile(string? name,string? fullPath=null)
    {
        foreach(var pane in new[]{summary,preview}){pane.FileLabel.Text=string.IsNullOrEmpty(name)?"请选择文件":name;pane.FileLabel.ToolTip=fullPath??name;}
    }
    public void UpdateTitle(string title)
    {
        foreach(var pane in new[]{summary,preview})if(pane.Floating!=null)pane.Floating.Title=title;
    }
    public void Float(DetailKind kind,bool showWindow=true)
    {
        if(disposed)return;var pane=Get(kind);if(pane.Floating!=null){if(showWindow)Locate(kind);return;}
        Reparenting?.Invoke();
        leftHost.Content=null;rightHost.Content=null;tabContent.Content=null;
        // Native ownership keeps this pane above its own search window without
        // making it topmost over other applications or other search windows.
        // EnsureHandle also supports creating an initially hidden pair of windows.
        new WindowInteropHelper(owner).EnsureHandle();
        var floating=new Window{Owner=owner,Title=owner.Title,Width=520,Height=650,MinWidth=320,MinHeight=240,FontFamily=owner.FontFamily,FontSize=owner.FontSize,ThemeMode=owner.ThemeMode,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=true,ShowActivated=true};
        floating.SetResourceReference(Window.BackgroundProperty,"ApplicationBackgroundBrush");
        if(pane.Bounds is Rect bounds&&bounds.Width>=320&&bounds.Height>=240){floating.Left=bounds.Left;floating.Top=bounds.Top;floating.Width=bounds.Width;floating.Height=bounds.Height;}
        else{var area=SystemParameters.WorkArea;floating.Width=Math.Min(floating.Width,area.Width);floating.Height=Math.Min(floating.Height,area.Height);floating.Left=area.Left+(area.Width-floating.Width)/2;floating.Top=area.Top+(area.Height-floating.Height)/2;}
        pane.Floating=floating;pane.PopButton.Content="收回主窗口";pane.PopButton.ToolTip="收回到搜索窗口";
        // One content control remains alive while moving between its two hosts.
        floating.Content=pane.View;MonitorLayout.Attach(floating);
        floating.Activated+=(_,_)=>activated();
        floating.StateChanged+=(_,_)=>Changed?.Invoke();
        floating.ContentRendered+=(_,_)=>EnsureOnScreen(floating);
        floating.Closing+=(_,e)=>
        {
            if(disposed||pane.Destroying||exiting())return;
            e.Cancel=true;
            // Window.Close cannot be reentered from its Closing event.
            _=owner.Dispatcher.BeginInvoke(()=>{if(pane.Floating==floating)Dock(kind,showWindow);});
        };
        floating.Closed+=(_,_)=>
        {
            if(pane.Floating!=floating)return;
            floating.Content=null;pane.Floating=null;pane.PopButton.Content="独立窗口";pane.PopButton.ToolTip="在独立窗口中显示此面板";
            if(!disposed&&!exiting())Layout();
        };
        Layout();if(showWindow){floating.Show();floating.Activate();}
    }
    public void Dock(DetailKind kind,bool revealParent=true)
    {
        if(disposed)return;var pane=Get(kind);var floating=pane.Floating;if(floating==null)return;
        Reparenting?.Invoke();
        var bounds=floating.WindowState==WindowState.Normal?new Rect(floating.Left,floating.Top,floating.Width,floating.Height):floating.RestoreBounds;
        if(!bounds.IsEmpty&&!double.IsNaN(bounds.X)&&!double.IsNaN(bounds.Y))pane.Bounds=bounds;
        pane.Destroying=true;floating.Content=null;
        try{floating.Close();}finally{pane.Destroying=false;}
        AllCollapsed=false;if(kind==RightKind)rightOpen=true;Layout();
        if(revealParent&&!exiting()){owner.Show();if(owner.WindowState==WindowState.Minimized)owner.WindowState=WindowState.Normal;owner.Activate();}
    }
    public void Locate(DetailKind kind)
    {
        if(disposed)return;var pane=Get(kind);if(pane.Floating is not Window floating)return;
        pane.HiddenByParent=false;floating.Show();if(floating.WindowState==WindowState.Minimized)floating.WindowState=WindowState.Normal;EnsureOnScreen(floating);floating.Activate();
    }
    public void HideAuxiliaries()
    {
        foreach(var pane in new[]{summary,preview})if(pane.Floating is Window floating&&floating.IsVisible){pane.HiddenByParent=true;floating.Hide();}
    }
    public void RestoreAuxiliaries()
    {
        foreach(var pane in new[]{summary,preview})if(pane.Floating is Window floating&&pane.HiddenByParent){pane.HiddenByParent=false;floating.Show();EnsureOnScreen(floating);}
    }
    void DisplayChanged(object? sender,EventArgs args)
    {
        if(disposed||owner.Dispatcher.HasShutdownStarted)return;
        _=owner.Dispatcher.BeginInvoke(()=>{if(!disposed)foreach(var pane in new[]{summary,preview})if(pane.Floating is Window floating)EnsureOnScreen(floating);});
    }
    internal static void EnsureOnScreen(Window window)
    {
        MonitorLayout.Fit(window);
    }

    void OwnerClosed(object? sender,EventArgs args)=>Dispose();
    public void Dispose()
    {
        if(disposed)return;disposed=true;if(previewSource!=null)previewSource.ContentUpdated-=UpdateFitAction;Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;owner.Closed-=OwnerClosed;
        foreach(var pane in new[]{summary,preview})if(pane.Floating is Window floating){pane.Destroying=true;floating.Content=null;floating.Close();pane.Floating=null;}
        leftHost.Content=null;rightHost.Content=null;tabContent.Content=null;
    }
}
