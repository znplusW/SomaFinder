// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SomaFinder;
internal sealed record DialogLocation(System.Drawing.Point Click)
{
    internal Rect? CloudArea {get;private init;}
    internal uint CloudDpi {get;private init;}=96;
    internal static DialogLocation ForCloud(Rect area,uint dpi)
    {if(!CloudSandbox.Active)throw new InvalidOperationException();return new(new System.Drawing.Point((int)area.Left+10,(int)area.Top+10)){CloudArea=area,CloudDpi=dpi};}
}
internal static class DialogPlacement
{
    internal static (Rect Area,uint Dpi) Resolve(DialogLocation location)
    {
        if(CloudSandbox.Active&&location.CloudArea is Rect simulated)return(simulated,location.CloudDpi);
        var monitor=MonitorFromPoint(new Pt{X=location.Click.X,Y=location.Click.Y},2);var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfoW(monitor,ref info))throw new InvalidOperationException("无法确定托盘所在显示器。");
        uint dpi=96;try{if(GetDpiForMonitor(monitor,0,out var x,out _)==0&&x>0)dpi=x;}catch(DllNotFoundException){}
        return(new Rect(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top),dpi);
    }
    internal static Rect Center(Rect area,Size size)
    {
        double width=Math.Min(Math.Max(1,size.Width),area.Width),height=Math.Min(Math.Max(1,size.Height),area.Height);
        return new(area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height);
    }
    internal static Rect Bounds(Window window)
    {
        if(!GetWindowRect(new WindowInteropHelper(window).Handle,out var r))throw new InvalidOperationException("对话框尚未创建。");
        return new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
    }
    internal static void Prepare(Window window,DialogLocation location)
    {
        window.Owner=null;window.WindowStartupLocation=WindowStartupLocation.Manual;
        double opacity=window.Opacity;window.Opacity=0;
        var target=Resolve(location);window.Width=Math.Min(window.Width,Math.Max(120,target.Area.Width*96/target.Dpi-24));
        window.MaxWidth=target.Area.Width*96/target.Dpi;window.MaxHeight=target.Area.Height*96/target.Dpi;
        void Place()
        {
            if(new WindowInteropHelper(window).Handle==0)return;
            // Resolve again in case the clicked monitor has since been disconnected.
            var area=Resolve(location).Area;var next=Center(area,Bounds(window).Size);
            SetWindowPos(new WindowInteropHelper(window).Handle,0,(int)Math.Round(next.X),(int)Math.Round(next.Y),(int)Math.Round(next.Width),(int)Math.Round(next.Height),0x14);
        }
        window.SourceInitialized+=(_,_)=>Place();window.ContentRendered+=(_,_)=>{Place();window.Opacity=opacity;};
        window.DpiChanged+=(_,_)=>window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(window.IsVisible)Place();}));
    }
    [StructLayout(LayoutKind.Sequential)]struct Pt{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct Rc{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public Rc Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]static extern nint MonitorFromPoint(Pt p,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfoW(nint h,ref MonitorInfo info);
    [DllImport("shcore.dll")]static extern int GetDpiForMonitor(nint monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint h,out Rc rectangle);
    [DllImport("user32.dll")]static extern bool SetWindowPos(nint h,nint after,int x,int y,int width,int height,uint flags);
}
