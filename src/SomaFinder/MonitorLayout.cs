// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SomaFinder;

// Native window/work-area rectangles share physical pixel coordinates. WPF minima
// alone use DIPs; never divide a mixed-DPI virtual desktop's absolute origin.
internal sealed class MonitorLayout : IDisposable
{
    readonly Window window;readonly Action? changed;HwndSource? source;bool disposed,moving,fitting;
    readonly DispatcherTimer settle=new(){Interval=TimeSpan.FromMilliseconds(180)};
    public MonitorLayout(Window window,Action? changed=null)
    {
        this.window=window;this.changed=changed;window.SourceInitialized+=Initialized;window.Closed+=Closed;
        window.LocationChanged+=LocationChanged;window.DpiChanged+=DpiChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplaysChanged;
        settle.Tick+=(_,_)=>{settle.Stop();if(!moving)Update();};
        if(new WindowInteropHelper(window).Handle!=0)Initialized(null,EventArgs.Empty);
    }
    internal static void Attach(Window window)=>_ = new MonitorLayout(window);
    void Initialized(object? sender,EventArgs e){source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source?.AddHook(Hook);Queue();}
    void LocationChanged(object? sender,EventArgs e){if(!fitting)Queue();}
    void DpiChanged(object sender,DpiChangedEventArgs e)=>Queue();
    void DisplaysChanged(object? sender,EventArgs e){if(!disposed&&!window.Dispatcher.HasShutdownStarted)window.Dispatcher.BeginInvoke(Queue);}
    void Queue(){if(disposed)return;settle.Stop();settle.Start();}
    nint Hook(nint hwnd,int message,nint wParam,nint lParam,ref bool handled)
    {if(message==0x231)moving=true;else if(message==0x232){moving=false;Queue();}else if(message is 0x2e0 or 0x7e)Queue();return 0;}
    void Update(){if(disposed||fitting)return;fitting=true;try{Fit(window);changed?.Invoke();}finally{fitting=false;}}
    internal static Rect FitPixels(Rect requested,Rect area)=>new(
        Math.Clamp(requested.X,area.Left,area.Right-Math.Min(requested.Width,area.Width)),
        Math.Clamp(requested.Y,area.Top,area.Bottom-Math.Min(requested.Height,area.Height)),
        Math.Min(requested.Width,area.Width),Math.Min(requested.Height,area.Height));
    internal static double MinimumWidth(double workWidthPixels,double dpi)=>Math.Min(600,workWidthPixels*96/Math.Max(96,dpi)/2);
    internal static void Fit(Window window)
    {
        var h=new WindowInteropHelper(window).Handle;if(h==0)return;
        var monitor=MonitorFromWindow(h,2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfoW(monitor,ref info)||!GetWindowRect(h,out var r))return;
        uint dpi=GetDpiForWindow(h);if(dpi==0)dpi=96;
        double availableWidth=info.Work.Right-info.Work.Left,availableHeight=info.Work.Bottom-info.Work.Top;
        window.MinWidth=Math.Min(window is SearchWindow?600:320,MinimumWidth(availableWidth,dpi));window.MinHeight=Math.Min(420,availableHeight*96/dpi);
        if(!window.IsVisible||window.WindowState!=WindowState.Normal)return;
        var area=new Rect(info.Work.Left,info.Work.Top,availableWidth,availableHeight);
        var before=new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);var next=FitPixels(before,area);
        if(next!=before)SetWindowPos(h,0,(int)next.X,(int)next.Y,(int)next.Width,(int)next.Height,0x14);
    }
    void Closed(object? sender,EventArgs e)=>Dispose();
    public void Dispose(){if(disposed)return;disposed=true;settle.Stop();source?.RemoveHook(Hook);window.SourceInitialized-=Initialized;window.Closed-=Closed;window.LocationChanged-=LocationChanged;window.DpiChanged-=DpiChanged;Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplaysChanged;}
    [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]static extern nint MonitorFromWindow(nint h,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfoW(nint h,ref MonitorInfo info);
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint h,out NativeRect rectangle);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")]static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
}
