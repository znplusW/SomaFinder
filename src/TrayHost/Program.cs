// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Globalization;
using System.Runtime.InteropServices;
using SomaFinder.Tray;

internal static class Program
{
    static nint window,owner,ownerProcess,icon;
    static nint pauseIcon;
    static bool paused;
    static uint ownerPid;
    static bool added;
    static string tooltip="Soma Finder";
    static readonly long started=Environment.TickCount64;
    static readonly WindowProcedure procedure=WndProc;
    static readonly uint taskbarCreated=TrayProtocol.RegisterWindowMessageW("TaskbarCreated");
    static readonly uint callback=TrayProtocol.RegisterWindowMessageW(TrayProtocol.CallbackName);
    [STAThread] static int Main()
    {
        // Keep both the registered GUID and its owning executable path stable across releases.
        using var singleton=new Mutex(true,@"Local\SomaFinder.StableTray.v1",out bool first);
        if(!first)return 0;
        var instance=GetModuleHandleW(null);
        var cls=new WindowClass{Size=(uint)Marshal.SizeOf<WindowClass>(),Instance=instance,Name=TrayProtocol.WindowClass,Procedure=Marshal.GetFunctionPointerForDelegate(procedure)};
        if(RegisterClassExW(ref cls)==0)return 2;
        icon=LoadImageW(0,Path.Combine(AppContext.BaseDirectory,"SomaFinder.ico"),1,32,32,0x10);
        if(icon==0)return 3;
        pauseIcon=PauseBadge.Create(icon);
        if(pauseIcon==0){DestroyIcon(icon);return 5;}
        window=CreateWindowExW(0,TrayProtocol.WindowClass,"Soma Finder",0x80000000,0,0,0,0,0,0,instance,0);
        if(window==0){DestroyIcon(icon);DestroyIcon(pauseIcon);return 4;}
        SetTimer(window,1,1000,0);
        while(GetMessageW(out var message,0,0,0)>0){TranslateMessage(ref message);DispatchMessageW(ref message);}
        if(ownerProcess!=0)CloseHandle(ownerProcess);
        DestroyIcon(icon);
        DestroyIcon(pauseIcon);
        return 0;
    }
    static bool Alive(nint process)=>process!=0&&WaitForSingleObject(process,0)==0x102;
    static void Forward(int command,nint parameter)
    {
        if(!Alive(ownerProcess))return;
        // The shell delivered input to this process; let the frontend activate its window.
        AllowSetForegroundWindow(ownerPid);
        TrayProtocol.PostMessageW(owner,callback,command,parameter);
    }
    static bool ConnectOwner(nint sender,string value)
    {
        var parts=value.Split('\n',3);
        if(parts.Length!=3||!uint.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out var pid)||!long.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out var created))return false;
        TrayProtocol.GetWindowThreadProcessId(sender,out var actualPid);
        if(actualPid!=pid||sender==0)return false;
        if(owner!=0&&owner!=sender&&Alive(ownerProcess))return false;
        var process=OpenProcess(0x00101000,false,pid);
        if(process==0)return false;
        if(!GetProcessTimes(process,out long actualCreated,out _,out _,out _)||actualCreated!=created||!Alive(process)){CloseHandle(process);return false;}
        if(ownerProcess!=0)CloseHandle(ownerProcess);
        owner=sender;ownerPid=pid;ownerProcess=process;tooltip=parts[2];
        return Publish();
    }
    static unsafe NotifyIconData Data(uint flags)
    {
        var data=new NotifyIconData{Size=(uint)sizeof(NotifyIconData),Window=window,Id=1,Flags=flags|0x20,Callback=TrayProtocol.ShellCallback,Icon=paused?pauseIcon:icon,Guid=TrayProtocol.IconId};
        var text=tooltip.AsSpan(0,Math.Min(tooltip.Length,127));text.CopyTo(new Span<char>(data.Tip,128));
        return data;
    }
    static bool Publish()
    {
        var data=Data(0x1|0x2|0x4|0x80);
        bool success=Shell_NotifyIconW(added?1u:0u,ref data);
        // A repeated TaskbarCreated notification must still address the very same GUID.
        if(!success)success=Shell_NotifyIconW(added?0u:1u,ref data);
        added=success;
        if(success){data.Version=4;Shell_NotifyIconW(4,ref data);}
        return success;
    }
    static nint WndProc(nint hwnd,uint message,nint wp,nint lp)
    {
        if(message==TrayProtocol.CopyData)
        {
            var packet=Marshal.PtrToStructure<TrayProtocol.CopyPacket>(lp);
            if(packet.Bytes<2||packet.Bytes>2048||(packet.Bytes&1)!=0||packet.Data==0)return 0;
            var value=Marshal.PtrToStringUni(packet.Data,packet.Bytes/2)?.TrimEnd('\0')??"";
            if(packet.Tag==TrayProtocol.Connect)return ConnectOwner(wp,value)?1:0;
            TrayProtocol.GetWindowThreadProcessId(wp,out var senderPid);
            if(wp!=owner||senderPid!=ownerPid||!Alive(ownerProcess))return 0;
            if(packet.Tag==TrayProtocol.Tooltip){tooltip=value;return Publish()?1:0;}
            if(packet.Tag==TrayProtocol.Appearance){if(value!="paused"&&value!="normal")return 0;paused=value=="paused";return Publish()?1:0;}
            if(packet.Tag==TrayProtocol.Close){DestroyWindow(hwnd);return 1;}
            return 0;
        }
        if(message==TrayProtocol.ShellCallback)
        {
            int action=(int)((long)lp&0xffff);
            if(action==0x203||action==0x401)Forward(TrayProtocol.Activate,0);
            else if(action==0x7b)Forward(TrayProtocol.Context,wp);
            return 0;
        }
        if(message==0x7f)return paused?pauseIcon:icon; // WM_GETICON: exposes the actual current native icon.
        if(message==taskbarCreated){added=false;if(Alive(ownerProcess))Publish();return 0;}
        if(message==0x113){if(ownerProcess!=0?!Alive(ownerProcess):Environment.TickCount64-started>10000)DestroyWindow(hwnd);return 0;}
        if(message==0x11)return 1;
        if(message==0x16&&wp!=0){DestroyWindow(hwnd);return 0;}
        if(message==0x2){if(added){var data=Data(0);Shell_NotifyIconW(2,ref data);}added=false;PostQuitMessage(0);return 0;}
        return DefWindowProcW(hwnd,message,wp,lp);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate nint WindowProcedure(nint hwnd,uint msg,nint wp,nint lp);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass{internal uint Size,Style;internal nint Procedure;internal int ExtraClass,ExtraWindow;internal nint Instance,Icon,Cursor,Background;internal string? Menu;internal string Name;internal nint SmallIcon;}
    [StructLayout(LayoutKind.Sequential)]struct Message{internal nint Window;internal uint Id;internal nuint Wparam;internal nint Lparam;internal uint Time;internal int X,Y;internal uint Private;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]unsafe struct NotifyIconData{internal uint Size;internal nint Window;internal uint Id,Flags,Callback;internal nint Icon;internal fixed char Tip[128];internal uint State,Mask;internal fixed char Info[256];internal uint Version;internal fixed char Title[64];internal uint InfoFlags;internal Guid Guid;internal nint Balloon;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern nint GetModuleHandleW(string? name);
    [DllImport("kernel32.dll")]static extern nint OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")]static extern bool GetProcessTimes(nint process,out long created,out long exit,out long kernel,out long user);
    [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(nint handle,uint milliseconds);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern ushort RegisterClassExW(ref WindowClass cls);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern nint CreateWindowExW(uint ex,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern nint LoadImageW(nint instance,string name,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll")]static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]static extern nuint SetTimer(nint window,nuint id,uint milliseconds,nint procedure);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetMessageW(out Message message,nint window,uint min,uint max);
    [DllImport("user32.dll")]static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern nint DefWindowProcW(nint window,uint message,nint wp,nint lp);
    [DllImport("user32.dll")]static extern void PostQuitMessage(int code);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern bool Shell_NotifyIconW(uint command,ref NotifyIconData data);
}
