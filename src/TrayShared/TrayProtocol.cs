// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System;
using System.Runtime.InteropServices;

namespace SomaFinder.Tray;

// Product identity, never derive these values from a version, executable path or process id.
internal static class TrayProtocol
{
    internal static readonly Guid IconId=new("6e2738c9-48fb-41d1-9a3e-c54ef936409b");
    internal const string WindowClass="SomaFinder.StableTray.v1";
    internal const string CallbackName="SomaFinder.StableTray.Callback.v1";
    internal const string Executable="SomaFinder.TrayHost.exe";
    internal const uint ShellCallback=0x8001,CopyData=0x004a;
    internal const nint Connect=0x53465431,Tooltip=0x53465432,Close=0x53465433,Appearance=0x53465434;
    internal const int Activate=1,Context=2;
    [StructLayout(LayoutKind.Sequential)] internal struct CopyPacket{internal nint Tag;internal int Bytes;internal nint Data;}
    [StructLayout(LayoutKind.Sequential)] internal struct IconIdentifier{internal uint Size;internal nint Window;internal uint Id;internal Guid Guid;}
    [StructLayout(LayoutKind.Sequential)] internal struct Rect{internal int Left,Top,Right,Bottom;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern nint FindWindowW(string name,string? title);
    [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")]internal static extern bool PostMessageW(nint window,uint message,nint wparam,nint lparam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern nint SendMessageTimeoutW(nint window,uint message,nint wparam,ref CopyPacket packet,uint flags,uint milliseconds,out nint result);
    [DllImport("shell32.dll")]internal static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier,out Rect rect);
    internal static bool IsRegistered(){var id=new IconIdentifier{Size=(uint)Marshal.SizeOf<IconIdentifier>(),Guid=IconId};return Shell_NotifyIconGetRect(ref id,out _)==0;}
}
