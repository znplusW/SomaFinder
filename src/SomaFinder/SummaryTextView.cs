// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace SomaFinder;

// Own touch panning as well as pinching: RichTextBox's mouse-selection promotion and
// its template ScrollViewer must not capture the first finger before a pinch starts.
public sealed class SummaryTextView : RichTextBox
{
    string text="",query="",matchStyle="Bold";
    public string MatchStyle {get=>matchStyle;set{if(matchStyle!=value){matchStyle=value;Render(true);}}}
    readonly Dictionary<int,TouchDevice> devices=new();
    readonly Dictionary<int,Point> positions=new();
    readonly PinchTracker pinch;
    bool capturing,restoring,restoreQueued,touchScrollQueued;
    double savedOffset,touchOffset;
    TextPointer? savedAnchor;double savedAnchorTop;
    readonly Queue<string> touchTrace=new();
    internal int ActiveTouches=>devices.Count;
    internal string TouchDiagnostics=>$"offset={VerticalOffset:F1}, extent={ExtentHeight:F1}, viewport={ViewportHeight:F1}, size={ActualWidth:F1}x{ActualHeight:F1}, font={FontSize:F1}, touches={devices.Count}, requested={touchOffset:F1}, saved={savedOffset:F1}, restoring={restoring}; "+string.Join(" | ",touchTrace);
    void Trace(string phase){if(!CloudSandbox.Active)return;touchTrace.Enqueue($"{phase}: offset={VerticalOffset:F1} touches={devices.Count} target={touchOffset:F1}");while(touchTrace.Count>24)touchTrace.Dequeue();}
    public Action<double>? Zoom {get;set;}
    public SummaryTextView()
    {
        IsReadOnly=true;IsDocumentEnabled=true;BorderThickness=new Thickness(0);VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        Document.PagePadding=new Thickness(0);ScrollViewer.SetPanningMode(this,PanningMode.None);
        pinch=new(ZoomAroundTouch);
        Stylus.SetIsPressAndHoldEnabled(this,false);Stylus.SetIsFlicksEnabled(this,false);
        AddHandler(PreviewMouseDownEvent,new MouseButtonEventHandler(BlockTouchMouse),true);
        AddHandler(PreviewMouseUpEvent,new MouseButtonEventHandler(BlockTouchMouse),true);
        AddHandler(PreviewMouseMoveEvent,new MouseEventHandler((_,e)=>{if(IsTouchMouse(e)&&!IsScrollBar(e.OriginalSource as DependencyObject))e.Handled=true;}),true);
        AddHandler(PreviewTouchDownEvent,new EventHandler<TouchEventArgs>((_,e)=>
        {
            // Leave explicit scrollbar operations to the scrollbar itself.
            if(IsScrollBar(e.OriginalSource as DependencyObject))return;
            var position=e.GetTouchPoint(this).Position;
            if(devices.Count==0)touchOffset=VerticalOffset;
            devices[e.TouchDevice.Id]=e.TouchDevice;positions[e.TouchDevice.Id]=position;
            pinch.Down(e.TouchDevice.Id,position);e.Handled=true;
            capturing=true;try{e.TouchDevice.Capture(this);}finally{capturing=false;}
            Trace("down");
        }),true);
        AddHandler(PreviewTouchMoveEvent,new EventHandler<TouchEventArgs>((_,e)=>
        {
            int id=e.TouchDevice.Id;if(!positions.TryGetValue(id,out var previous))return;
            var position=e.GetTouchPoint(this).Position;positions[id]=position;
            if(!pinch.Move(id,position))SetTouchOffset(touchOffset+previous.Y-position.Y);
            Trace("move");
            e.Handled=true;
        }),true);
        AddHandler(PreviewTouchUpEvent,new EventHandler<TouchEventArgs>((_,e)=>
        {
            if(!devices.ContainsKey(e.TouchDevice.Id))return;
            ForgetTouch(e.TouchDevice.Id);e.Handled=true;
            capturing=true;try{e.TouchDevice.Capture(null);}finally{capturing=false;}
            Trace("up");
        }),true);
        LostTouchCapture+=(_,e)=>{if(!capturing)ForgetTouch(e.TouchDevice.Id);Trace("capture-lost");};
        RequestBringIntoView+=(_,e)=>{if(devices.Count>0||restoring){e.Handled=true;Trace("caret-scroll-suppressed");}};
        Unloaded+=(_,_)=>{if(!restoring){savedOffset=VerticalOffset;restoring=true;}ReleaseTouches();Trace("unloaded");};
        Loaded+=(_,_)=>{Trace("loaded");if(restoring)QueueRestore();};
    }
    static bool IsTouchMouse(MouseEventArgs e)=>e.StylusDevice?.TabletDevice.Type==TabletDeviceType.Touch;
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e){if(IsTouchMouse(e)&&!IsScrollBar(e.OriginalSource as DependencyObject)){e.Handled=true;return;}base.OnPreviewMouseDown(e);}
    protected override void OnPreviewMouseUp(MouseButtonEventArgs e){if(IsTouchMouse(e)&&!IsScrollBar(e.OriginalSource as DependencyObject)){e.Handled=true;return;}base.OnPreviewMouseUp(e);}
    protected override void OnPreviewMouseMove(MouseEventArgs e){if(IsTouchMouse(e)&&!IsScrollBar(e.OriginalSource as DependencyObject)){e.Handled=true;return;}base.OnPreviewMouseMove(e);}
    void BlockTouchMouse(object sender,MouseButtonEventArgs e){if(IsTouchMouse(e)&&!IsScrollBar(e.OriginalSource as DependencyObject))e.Handled=true;}
    internal void PreserveViewport()
    {
        // Called before the owning pane is detached; after detachment the old
        // ScrollViewer may already report zero. Repeated Layout calls keep one snapshot.
        if(restoring||!IsLoaded)return;
        savedOffset=VerticalOffset;savedAnchor=AnchorAt(new Point(Padding.Left+2,Padding.Top+2));
        savedAnchorTop=savedAnchor?.GetCharacterRect(LogicalDirection.Forward).Top??double.NaN;
        restoring=true;ReleaseTouches();Trace("preserve");QueueRestore();
    }
    TextPointer? AnchorAt(Point point)
    {
        if(!IsVisible||!IsLoaded||PresentationSource.FromVisual(this)==null)return null;
        // During reparenting WPF can retain a detached TextBoxView for one layout
        // turn. Pixel offset remains usable while character geometry is unavailable.
        try{return GetPositionFromPoint(point,true);}
        catch(InvalidOperationException){Trace("anchor-awaiting-visual-parent");return null;}
    }
    void QueueRestore()
    {
        if(restoreQueued)return;restoreQueued=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
        {
            restoreQueued=false;if(!restoring||!IsLoaded)return;
            UpdateLayout();ScrollToVerticalOffset(savedOffset);UpdateLayout();
            if(savedAnchor!=null&&double.IsFinite(savedAnchorTop))
            {
                double top=savedAnchor.GetCharacterRect(LogicalDirection.Forward).Top;
                if(double.IsFinite(top)){ScrollToVerticalOffset(VerticalOffset+top-savedAnchorTop);UpdateLayout();}
            }
            savedAnchor=null;restoring=false;Trace("restored");
        }));
    }
    void ForgetTouch(int id){devices.Remove(id);positions.Remove(id);pinch.Up(id);}
    void SetTouchOffset(double value)
    {
        // ScrollToVerticalOffset is deferred. Accumulate moves against the requested
        // offset so several touch events in one frame do not lose their deltas.
        touchOffset=Math.Clamp(value,0,Math.Max(0,ExtentHeight-ViewportHeight));
        ScrollToVerticalOffset(touchOffset);
        // A prior caret/selection request can still be queued inside TextBoxView.
        // Reassert the latest touch intent after it settles, without changing selection.
        if(touchScrollQueued)return;touchScrollQueued=true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>
        {touchScrollQueued=false;if(devices.Count>0&&!restoring){ScrollToVerticalOffset(touchOffset);Trace("settled-pan");}}));
    }
    void ReleaseTouches()
    {
        var current=devices.Values.ToArray();devices.Clear();positions.Clear();pinch.Clear();
        capturing=true;try{foreach(var device in current)if(ReferenceEquals(device.Captured,this))device.Capture(null);}finally{capturing=false;}
    }
    void ZoomAroundTouch(double factor)
    {
        if(Zoom==null||positions.Count<2)return;
        var pair=positions.Values.Take(2).ToArray();var focus=new Point((pair[0].X+pair[1].X)/2,(pair[0].Y+pair[1].Y)/2);
        // Keep the character under the gesture at approximately the same height even
        // when a larger font wraps preceding lines. Never select or alter source text.
        var anchor=AnchorAt(focus);double before=anchor?.GetCharacterRect(LogicalDirection.Forward).Top??double.NaN;
        Zoom(factor);UpdateLayout();
        if(anchor!=null&&double.IsFinite(before))
        {
            var after=anchor.GetCharacterRect(LogicalDirection.Forward).Top;
            if(double.IsFinite(after)){SetTouchOffset(VerticalOffset+after-before);UpdateLayout();}
        }
    }
    static bool IsScrollBar(DependencyObject? target)
    {
        while(target!=null)
        {
            if(target is System.Windows.Controls.Primitives.ScrollBar)return true;
            if(target is SummaryTextView)return false;
            target=target is Visual?VisualTreeHelper.GetParent(target):target is FrameworkContentElement content?content.Parent:LogicalTreeHelper.GetParent(target);
        }
        return false;
    }
    public string Text{get=>text;set{if(text==value)return;text=value;Render();}}
    public string HighlightQuery{get=>query;set{if(query!=value){query=value;Render();}}}
    internal int BoldRuns=>Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines.OfType<Run>()).Count(r=>r.FontWeight==FontWeights.Bold);
    void Render(bool preserve=false)
    {
        var paragraph=new Paragraph{Margin=new Thickness(0)};
        foreach(var run in MatchText.Runs(text,query,matchStyle))paragraph.Inlines.Add(run);
        double offset=VerticalOffset;
        int selectionStart=Document.ContentStart.GetOffsetToPosition(Selection.Start),selectionEnd=Document.ContentStart.GetOffsetToPosition(Selection.End);
        Document.Blocks.Clear();Document.Blocks.Add(paragraph);
        if(preserve&&IsLoaded){var first=Document.ContentStart.GetPositionAtOffset(Math.Max(0,selectionStart));var last=Document.ContentStart.GetPositionAtOffset(Math.Max(0,selectionEnd));if(first!=null&&last!=null)Selection.Select(first,last);ScrollToVerticalOffset(offset);}

    }
}

internal sealed class PinchTracker
{
    readonly Dictionary<int,Point> points=new();readonly Action<double> zoom;double lastDistance;
    public bool Active=>points.Count>=2;
    public PinchTracker(Action<double> zoom)=>this.zoom=zoom;
    double Distance(){var pair=points.Values.Take(2).ToArray();return pair.Length<2?0:(pair[0]-pair[1]).Length;}
    public void Down(int id,Point position){points[id]=position;if(points.Count==2)lastDistance=Distance();}
    public bool Move(int id,Point position){if(!points.ContainsKey(id))return false;points[id]=position;if(!Active)return false;double now=Distance();if(lastDistance>1&&now>1)zoom(now/lastDistance);lastDistance=now;return true;}
    public void Up(int id){points.Remove(id);lastDistance=Distance();}
    public void Clear(){points.Clear();lastDistance=0;}
}
