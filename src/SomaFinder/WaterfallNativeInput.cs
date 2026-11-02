// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SomaFinder;
internal static class WaterfallNativeInput
{
    internal static async Task Verify(SearchWindow window)
    {
        if(!CloudSandbox.Active)throw new InvalidOperationException();
        window.MinWidth=320;window.Width=1000;window.Height=700;window.Left=0;window.Top=0;window.SetView(2);await Task.Delay(150);
        SetForegroundWindow(new WindowInteropHelper(window).Handle);var view=window.Waterfall;view.Zoom(120);view.ScrollTo(250);await Task.Delay(150);
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        Check(InitializeTouchInjection(4,3),"Touch injection initialization failed");
        Point Screen(double x,double y)=>view.PointToScreen(new Point(x,y));
        double width=view.TileWidth;int columns=view.Columns;Check(columns>=2,"Cloud viewport too narrow for native pinch assertion");
        var a=Screen(35,150);var b=Screen(135,150);var end=Screen(Math.Min(view.ActualWidth-20,275),150);
        Send((1,a,0x10006));await Task.Delay(50);Send((1,a,0x20006),(2,b,0x10006));await Task.Delay(60);
        for(int i=1;i<=8;i++){var point=new Point(b.X+(end.X-b.X)*i/8,b.Y);Send((1,a,0x20006),(2,point,0x20006));await Task.Delay(25);}
        Send((1,a,0x20006),(2,end,0x40000));await Task.Delay(40);Send((1,a,0x40000));await Task.Delay(150);
        Check(view.TileWidth>width*1.5&&view.Columns<columns,"Native two-finger pinch did not resize/reflow thumbnails");
        view.ScrollTo(350);await Task.Delay(100);double before=view.Offset;var selected=window.SelectedHit;var start=Screen(60,210);
        Send((3,start,0x10006));await Task.Delay(50);
        for(int i=1;i<=10;i++){Send((3,new Point(start.X,start.Y-i*8),0x20006));await Task.Delay(25);}
        Send((3,new Point(start.X,start.Y-80),0x40000));await Task.Delay(100);
        Check(view.Offset>before+50&&ReferenceEquals(selected,window.SelectedHit),"Native swipe failed or selected another file");Check(view.ActiveTouches==0,"Native gesture retained capture");
    }
    static void Send(params (uint Id,Point Point,uint Flags)[] points)
    {
        var contacts=points.Select(p=>new Contact{Info=new Pointer{Type=2,Id=p.Id,Flags=p.Flags,Pixel=new Pt{X=(int)p.Point.X,Y=(int)p.Point.Y}},Mask=7,Area=new Rc{Left=(int)p.Point.X-2,Right=(int)p.Point.X+2,Top=(int)p.Point.Y-2,Bottom=(int)p.Point.Y+2},Orientation=90,Pressure=32000}).ToArray();
        if(!InjectTouchInput((uint)contacts.Length,contacts))throw new Exception("InjectTouchInput "+Marshal.GetLastWin32Error()+" contacts="+string.Join(";",points.Select(p=>$"{p.Id}:{p.Point}:{p.Flags:X}"))+" desktop="+System.Windows.Forms.SystemInformation.VirtualScreen);
    }
    [StructLayout(LayoutKind.Sequential)]struct Pt{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct Rc{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct Pointer{public uint Type,Id,Frame,Flags;public nint Device,Target;public Pt Pixel,Himetric,RawPixel,RawHimetric;public uint Time,History;public int Data;public uint Keys;public ulong Performance;public uint Change;}
    [StructLayout(LayoutKind.Sequential)]struct Contact{public Pointer Info;public uint Flags,Mask;public Rc Area,RawArea;public uint Orientation,Pressure;}
    [DllImport("user32.dll",SetLastError=true)]static extern bool InitializeTouchInjection(uint count,uint mode);
    [DllImport("user32.dll",SetLastError=true)]static extern bool InjectTouchInput(uint count,Contact[] contacts);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(nint hwnd);
}
