// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SomaFinder;

// Only viewport cards are real WPF controls. Layout metadata remains cheap even
// when the user asks Soma for many results; zoom never issues another search.
internal sealed class MasonryResults : Grid
{
    // Keep visibility fixed. Themes that reserve a scrollbar gutter must not
    // change the viewport width in response to the height of the resized tiles.
    readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Visible,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false};
    readonly Canvas canvas=new(){ClipToBounds=true,Background=Brushes.Transparent};
    readonly Dictionary<int,Card> cards=new();readonly Dictionary<Hit,double> ratios=new();
    readonly Dictionary<int,(TouchDevice Device,Point Point,Point Start)> touches=new();
    readonly DispatcherTimer zoomIdle=new(){Interval=TimeSpan.FromMilliseconds(200)};
    IReadOnlyList<Hit> items=Array.Empty<Hit>();Rect[] rectangles=Array.Empty<Rect>();string query="";Preferences? prefs;
    Hit? selected;double preferred=200,offsetTarget,layoutWidth=double.NaN,pendingViewportY;bool arranging,releasing,gestureMoved,pinched,layoutDirty=true;
    DispatcherOperation? pendingLayout;
    internal event Action<Hit?>? SelectionChanged;internal event Action? OpenRequested;internal event Action<double>? ZoomChanged;
    internal int Columns{get;private set;}internal int RealizedCount=>cards.Count;internal double TileWidth=>preferred;internal double Offset=>scroll.VerticalOffset;
    internal int RealizedImages=>cards.Values.Count(c=>c.HasImage);
    internal IReadOnlyList<Rect> Bounds=>rectangles;internal int ActiveTouches=>touches.Count;internal FrameworkElement TouchSurface=>this;
    internal MasonryResults()
    {
        Children.Add(scroll);scroll.Content=canvas;Focusable=true;ClipToBounds=true;Background=Brushes.Transparent;
        scroll.ScrollChanged+=(_,e)=>{if(!arranging){if(Math.Abs(e.ViewportWidthChange)>.01)ResizeLayout();else if(pendingLayout==null)Realize();}};
        SizeChanged+=(_,e)=>{if(e.WidthChanged)ResizeLayout();else if(pendingLayout==null)Realize();};
        IsVisibleChanged+=(_,_)=>{if(IsVisible)QueueLayout();else{CancelLayout();ClearCards();}};Unloaded+=(_,_)=>{CancelLayout();ClearCards();ReleaseTouches();};
        // Theme/template replacement can unload and reload an equally-sized
        // visible control without raising SizeChanged or IsVisibleChanged.
        Loaded+=(_,_)=>QueueLayout();
        zoomIdle.Tick+=(_,_)=>{zoomIdle.Stop();foreach(var card in cards.Values)card.Load();};
        PreviewTouchDown+=Down;PreviewTouchMove+=Move;PreviewTouchUp+=Up;LostTouchCapture+=(_,e)=>{if(!releasing)touches.Remove(e.TouchDevice.Id);};
        Stylus.SetIsPressAndHoldEnabled(this,false);Stylus.SetIsFlicksEnabled(this,false);
        PreviewMouseWheel+=(_,e)=>{if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){Zoom(preferred*(e.Delta>0?1.12:1/1.12),e.GetPosition(this).Y);e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Enter){OpenRequested?.Invoke();e.Handled=true;return;}
            int index=selected==null?-1:items.ToList().IndexOf(selected);int next=e.Key switch{Key.Right=>index+1,Key.Left=>index-1,Key.Down=>index+Math.Max(1,Columns),Key.Up=>index-Math.Max(1,Columns),Key.Home=>0,Key.End=>items.Count-1,_=>index};
            if(next!=index&&items.Count>0){Select(items[Math.Clamp(next,0,items.Count-1)],true);e.Handled=true;}
        };
    }
    internal void SetItems(IReadOnlyList<Hit> values,string search,Preferences preferences)
    {
        bool changed=!items.SequenceEqual(values)||query!=search;items=values;query=search;prefs=preferences;
        if(changed){layoutDirty=true;ClearCards();rectangles=Array.Empty<Rect>();offsetTarget=0;scroll.ScrollToVerticalOffset(0);var live=values.ToHashSet();foreach(var key in ratios.Keys.Where(k=>!live.Contains(k)).ToArray())ratios.Remove(key);}
        double width=Math.Clamp(preferences.SearchLayout.ThumbnailWidth,120,420);layoutDirty|=width!=preferred;preferred=width;QueueLayout();
    }
    internal void Select(Hit? hit,bool scrollIntoView=false)
    {
        bool changed=!ReferenceEquals(selected,hit);selected=hit;foreach(var card in cards.Values)card.Select(ReferenceEquals(card.Hit,hit));
        if(scrollIntoView&&hit!=null){int index=items.ToList().IndexOf(hit);if(index>=0&&index<rectangles.Length){var rect=rectangles[index];if(rect.Top<scroll.VerticalOffset)scroll.ScrollToVerticalOffset(rect.Top);else if(rect.Bottom>scroll.VerticalOffset+scroll.ViewportHeight)scroll.ScrollToVerticalOffset(rect.Bottom-scroll.ViewportHeight);}}
        if(changed)SelectionChanged?.Invoke(hit);
    }
    internal void ScrollTo(double offset){offsetTarget=offset;scroll.ScrollToVerticalOffset(offset);}
    internal void Zoom(double width,double viewportY=0)
    {
        preferred=Math.Clamp(width,120,420);if(prefs!=null)prefs.SearchLayout.ThumbnailWidth=preferred;
        layoutDirty=true;QueueLayout(viewportY);zoomIdle.Stop();zoomIdle.Start();ZoomChanged?.Invoke(preferred);
    }
    void QueueLayout(double? viewportY=null)
    {
        if(!IsVisible)return;
        if(viewportY.HasValue)pendingViewportY=viewportY.Value;else if(pendingLayout==null)pendingViewportY=0;
        if(pendingLayout!=null)return;
        // Coalesce arriving thumbnails and gestures; a real viewport resize is
        // applied synchronously so old card bounds cannot spill into other panes.
        pendingLayout=Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>{pendingLayout=null;Layout(pendingViewportY);}));
    }
    void CancelLayout(){pendingLayout?.Abort();pendingLayout=null;}
    void ResizeLayout(){double anchor=pendingLayout==null?0:pendingViewportY;CancelLayout();Layout(anchor);}
    void Layout(double viewportY)
    {
        if(arranging||!IsVisible||scroll.ViewportWidth<1||ActualWidth<1)return;
        // ViewportWidth can still describe the previous arrange during SizeChanged.
        double width=Math.Min(scroll.ViewportWidth,ActualWidth);
        if(!layoutDirty&&Math.Abs(width-layoutWidth)<.01){Realize();return;}
        arranging=true;double? anchoredOffset=null;
        try
        {
            int anchorIndex=-1;double fraction=0;
            if(rectangles.Length>0){double y=scroll.VerticalOffset+viewportY;anchorIndex=Enumerable.Range(0,rectangles.Length).FirstOrDefault(i=>rectangles[i].Bottom>=y,-1);if(anchorIndex>=0)fraction=(y-rectangles[anchorIndex].Top)/Math.Max(1,rectangles[anchorIndex].Height);}
            const double gap=8;layoutWidth=width;layoutDirty=false;
            int next=Math.Max(1,(int)Math.Floor((width-gap)/(preferred+gap)));
            if(Columns>0&&next!=Columns)
            {
                // Drag coordinates can fluctuate by a few DIPs (also across DPI
                // rounding). Require a deliberate 12-DIP crossing in either
                // direction before changing columns; tiles still fill the width.
                int stable=Math.Max(1,(int)Math.Floor((width-gap+(next<Columns?12:-12))/(preferred+gap)));
                next=next<Columns?Math.Min(Columns,stable):Math.Max(Columns,stable);
            }
            Columns=next;double tile=Math.Max(1,(width-gap*(Columns+1))/Columns);
            var heights=Enumerable.Repeat(gap,Columns).ToArray();rectangles=new Rect[items.Count];
            for(int i=0;i<items.Count;i++)
            {
                int column=Array.IndexOf(heights,heights.Min());double aspect=ratios.GetValueOrDefault(items[i],(Rules.TypeMatches(items[i].Type,"文档")||Rules.TypeMatches(items[i].Type,"代码"))?0.774:1.5);
                double height=tile/Math.Clamp(aspect,.25,4)+54;rectangles[i]=new(gap+column*(tile+gap),heights[column],tile,height);heights[column]+=height+gap;
            }
            canvas.Width=width;canvas.Height=Math.Max(1,heights.Max());
            if(anchorIndex>=0&&anchorIndex<rectangles.Length){offsetTarget=Math.Max(0,rectangles[anchorIndex].Top+fraction*rectangles[anchorIndex].Height-viewportY);anchoredOffset=offsetTarget;scroll.ScrollToVerticalOffset(offsetTarget);}
        }
        finally{arranging=false;}
        Realize(anchoredOffset);
    }
    void Realize(double? plannedOffset=null)
    {
        if(!IsVisible||prefs==null||rectangles.Length!=items.Count)return;
        double current=Math.Min(plannedOffset??scroll.VerticalOffset,Math.Max(0,canvas.Height-scroll.ViewportHeight));double top=Math.Max(0,current-120),bottom=current+Math.Max(200,scroll.ViewportHeight)+120;
        var wanted=Enumerable.Range(0,items.Count).Where(i=>rectangles[i].Bottom>=top&&rectangles[i].Top<=bottom).Take(120).ToHashSet();
        foreach(var index in cards.Keys.Where(i=>!wanted.Contains(i)).ToArray()){cards[index].Dispose();canvas.Children.Remove(cards[index]);cards.Remove(index);}
        foreach(int index in wanted)
        {
            if(!cards.TryGetValue(index,out var card))
            {
                card=new Card(items[index],query,prefs,(hit)=>{Focus();Select(hit);},()=>OpenRequested?.Invoke(),(hit,ratio)=>
                {
                    if(!items.Contains(hit)||ratios.TryGetValue(hit,out double prior)&&Math.Abs(prior-ratio)<.01)return;ratios[hit]=ratio;layoutDirty=true;QueueLayout();
                });cards[index]=card;canvas.Children.Add(card);card.Select(ReferenceEquals(card.Hit,selected));
            }
            var rect=rectangles[index];Canvas.SetLeft(card,rect.X);Canvas.SetTop(card,rect.Y);card.Width=rect.Width;card.Height=rect.Height;
            if(touches.Count<2&&!zoomIdle.IsEnabled)card.Load();
        }
    }
    void ClearCards(){foreach(var card in cards.Values)card.Dispose();cards.Clear();canvas.Children.Clear();}
    void Down(object? sender,TouchEventArgs e)
    {
        e.Handled=true;var point=e.GetTouchPoint(this).Position;if(touches.Count==0){gestureMoved=false;pinched=false;offsetTarget=scroll.VerticalOffset;}
        touches[e.TouchDevice.Id]=(e.TouchDevice,point,point);if(touches.Count>=2)pinched=true;e.TouchDevice.Capture(this);
    }
    void Move(object? sender,TouchEventArgs e)
    {
        if(!touches.TryGetValue(e.TouchDevice.Id,out var previous))return;e.Handled=true;
        var before=touches.Values.Take(2).Select(t=>t.Point).ToArray();var point=e.GetTouchPoint(this).Position;touches[e.TouchDevice.Id]=(e.TouchDevice,point,previous.Start);
        if((point-previous.Start).Length>7)gestureMoved=true;
        if(before.Length==1){offsetTarget=Math.Clamp(offsetTarget+previous.Point.Y-point.Y,0,Math.Max(0,canvas.Height-scroll.ViewportHeight));scroll.ScrollToVerticalOffset(offsetTarget);return;}
        var after=touches.Values.Take(2).Select(t=>t.Point).ToArray();double distance=(before[1]-before[0]).Length;
        if(distance>8)Zoom(preferred*(after[1]-after[0]).Length/distance,(after[0].Y+after[1].Y)/2);
    }
    void Up(object? sender,TouchEventArgs e)
    {
        if(!touches.Remove(e.TouchDevice.Id,out var previous))return;e.Handled=true;
        releasing=true;try{e.TouchDevice.Capture(null);}finally{releasing=false;}
        if(touches.Count==0)
        {
            if(!gestureMoved&&!pinched){var point=previous.Point;point.Y+=scroll.VerticalOffset;int index=Array.FindIndex(rectangles,r=>r.Contains(point));if(index>=0)Select(items[index]);}
            Realize();
        }
    }
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e){if(e.StylusDevice?.TabletDevice.Type==TabletDeviceType.Touch){e.Handled=true;return;}base.OnPreviewMouseDown(e);}
    protected override void OnPreviewMouseUp(MouseButtonEventArgs e){if(e.StylusDevice?.TabletDevice.Type==TabletDeviceType.Touch){e.Handled=true;return;}base.OnPreviewMouseUp(e);}
    void ReleaseTouches(){releasing=true;try{foreach(var touch in touches.Values.ToArray())touch.Device.Capture(null);}finally{touches.Clear();releasing=false;}}

    sealed class Card:Border,IDisposable
    {
        internal Hit Hit{get;}readonly Image image=new(){Stretch=Stretch.Uniform};readonly TextBlock caption=UI.Text("",11);readonly Preferences prefs;readonly string query;
        internal bool HasImage=>image.Source!=null;
        readonly Action<Hit,double> changed;CancellationTokenSource? pending;string stamp="";bool disposed;
        internal Card(Hit hit,string query,Preferences prefs,Action<Hit> select,Action open,Action<Hit,double> resized)
        {
            Hit=hit;this.query=query;this.prefs=prefs;changed=resized;CornerRadius=new CornerRadius(5);BorderThickness=new Thickness(1);ClipToBounds=true;
            SetResourceReference(BackgroundProperty,"CardBackgroundFillColorDefaultBrush");SetResourceReference(BorderBrushProperty,"ControlStrokeColorDefaultBrush");ToolTip=hit.FullPath;
            var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(52)});Child=grid;
            var picture=new Grid();picture.Children.Add(new TextBlock{Text=hit.Glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=40,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(54,149,202))});picture.Children.Add(image);grid.Children.Add(picture);
            if(Rules.TypeMatches(hit.Type,"视频")){var tag=new Border{Background=new SolidColorBrush(Color.FromArgb(190,20,20,20)),CornerRadius=new CornerRadius(3),Padding=new Thickness(5,2,5,2),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(5),Child=new TextBlock{Text="▶ 视频",Foreground=Brushes.White,FontSize=11}};picture.Children.Add(tag);}
            var labels=new StackPanel{Margin=new Thickness(7,3,7,3)};labels.Children.Add(new TextBlock{Text=hit.Name,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis});caption.TextTrimming=TextTrimming.CharacterEllipsis;caption.Opacity=.7;labels.Children.Add(caption);Grid.SetRow(labels,1);grid.Children.Add(labels);
            MouseLeftButtonDown+=(_,e)=>{select(hit);if(e.ClickCount==2)open();e.Handled=true;};MouseRightButtonDown+=(_,_)=>select(hit);
            Hit.PropertyChanged+=Changed;
        }
        void Changed(object? sender,PropertyChangedEventArgs e){if(stamp!=ResultThumbnails.Stamp(Hit,query))Load();}
        internal void Select(bool selected){if(selected)BorderBrush=new SolidColorBrush(Color.FromRgb(0,146,208));else SetResourceReference(BorderBrushProperty,"ControlStrokeColorDefaultBrush");BorderThickness=new Thickness(selected?2:1);}
        internal async void Load()
        {
            if(disposed)return;string next=ResultThumbnails.Stamp(Hit,query);if(next==stamp)return;stamp=next;pending?.Cancel();var cancel=pending=new();
            try{var value=await ResultThumbnails.Get(Hit,query,prefs,cancel.Token);if(disposed||cancel.IsCancellationRequested||stamp!=next)return;image.Source=value?.Image;caption.Text=value?.Caption??"预览不可用";if(value?.Image is {} bitmap)changed(Hit,(double)bitmap.PixelWidth/bitmap.PixelHeight);}
            catch(OperationCanceledException){}
        }
        public void Dispose(){disposed=true;pending?.Cancel();Hit.PropertyChanged-=Changed;image.Source=null;}
    }
}
