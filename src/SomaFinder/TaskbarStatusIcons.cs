// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

// Use the actual window icon: Shell overlays are suppressed by small-icon/badge
// settings. Only in-memory icons change; executable and pinned shortcut stay intact.
internal sealed class TaskbarStatusIcons : IDisposable
{
    readonly Application application;
    readonly Func<bool> isPaused;
    readonly HashSet<Window> attached=new();
    readonly Dictionary<Window,(ImageSource? Original,ImageSource Paused)> icons=new();
    bool stopping,disposed;
    static readonly Lazy<ImageSource> defaultIcon=new(LoadOriginal);
    static readonly Lazy<ImageSource> defaultPaused=new(()=>CreatePaused(defaultIcon.Value));

    internal TaskbarStatusIcons(Application application,Func<bool> isPaused)
    {
        this.application=application;this.isPaused=isPaused;
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,
            new RoutedEventHandler(WindowLoaded),true);
    }
    void WindowLoaded(object sender,RoutedEventArgs e)
    {
        if(disposed||sender is not Window window||!application.Windows.Cast<Window>().Contains(window))return;
        Attach(window);Apply(window,!stopping&&isPaused());
    }
    void Attach(Window window)
    {
        if(!attached.Add(window))return;
        icons[window]=(window.Icon,window.Icon==null?defaultPaused.Value:CreatePaused(window.Icon));
        window.IsVisibleChanged+=VisibilityChanged;
        window.Closed+=WindowClosed;
    }
    void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        if(sender is Window window&&!disposed)Apply(window,!stopping&&isPaused());
    }
    void WindowClosed(object? sender,EventArgs e)
    {
        if(sender is not Window window)return;
        Apply(window,false);window.IsVisibleChanged-=VisibilityChanged;window.Closed-=WindowClosed;attached.Remove(window);icons.Remove(window);
    }
    void Apply(Window window,bool paused)
    {
        if(!icons.TryGetValue(window,out var pair))return;
        // Remove a previously assigned Shell badge to avoid two pause marks.
        if(window.TaskbarItemInfo?.Overlay!=null)window.TaskbarItemInfo.Overlay=null;
        var image=paused&&window.IsVisible&&window.ShowInTaskbar?pair.Paused:pair.Original;
        if(!ReferenceEquals(window.Icon,image))window.Icon=image;
    }
    internal bool HasPauseIcon(Window window)=>icons.TryGetValue(window,out var pair)&&ReferenceEquals(window.Icon,pair.Paused);
    internal void Refresh()
    {
        if(disposed)return;
        bool paused=!stopping&&isPaused();
        foreach(var window in application.Windows.OfType<Window>().ToArray()){Attach(window);Apply(window,paused);}
    }
    internal void ClearForExit()
    {
        stopping=true;
        // Clear every member of a taskbar group, otherwise removing one overlay can
        // reveal an earlier window's still-active overlay in that same group.
        foreach(var window in attached.Concat(application.Windows.OfType<Window>()).Distinct().ToArray())Apply(window,false);
    }
    public void Dispose()
    {
        if(disposed)return;ClearForExit();disposed=true;
        foreach(var window in attached){window.IsVisibleChanged-=VisibilityChanged;window.Closed-=WindowClosed;}
        attached.Clear();icons.Clear();
    }
    static ImageSource LoadOriginal()
    {
        using var stream=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Components","SomaFinder.ico"));
        var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames.OrderByDescending(f=>f.PixelWidth).First();frame.Freeze();return frame;
    }
    static ImageSource CreatePaused(ImageSource original)
    {
        // DrawingImage(32x32) is padded, not enlarged, by WPF when the system
        // requests a larger HICON. ICO frames let WPF choose the required size.
        var group=new DrawingGroup();group.Children.Add(new ImageDrawing(original,new Rect(0,0,32,32)));
        var orange=new SolidColorBrush(Color.FromRgb(0xE8,0x79,0x00));var keyline=new Pen(Brushes.White,.65);
        foreach(double x in new[]{21d,27d})group.Children.Add(new GeometryDrawing(orange,keyline,new RectangleGeometry(new Rect(x,20.5,3.5,11),.5,.5)));
        group.Freeze();
        var frames=new List<(int Size,byte[] Bytes)>();
        foreach(int size in new[]{16,20,24,32,40,48,64,96,128,256})
        {
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.PushTransform(new ScaleTransform(size/32d,size/32d));dc.DrawDrawing(group);dc.Pop();}
            var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var png=new MemoryStream();encoder.Save(png);frames.Add((size,png.ToArray()));
        }
        using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
        {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)frames.Count);int offset=6+16*frames.Count;
            foreach(var frame in frames){writer.Write((byte)(frame.Size==256?0:frame.Size));writer.Write((byte)(frame.Size==256?0:frame.Size));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frame.Bytes.Length);writer.Write(offset);offset+=frame.Bytes.Length;}
            foreach(var frame in frames)writer.Write(frame.Bytes);
        }
        stream.Position=0;var icon=new IconBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames.OrderByDescending(f=>f.PixelWidth).First();icon.Freeze();return icon;
    }
}
