// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Interop;
using SomaFinder.Tray;

namespace SomaFinder;

// Shell binds an unsigned icon GUID to its executable path. This small native host has a
// fixed per-user path, while portable Finder versions and their data stay in their folders.
internal sealed class StableTrayIcon:IDisposable
{
    readonly HwndSource owner;
    readonly uint callback=TrayProtocol.RegisterWindowMessageW(TrayProtocol.CallbackName);
    Task? starting;bool disposed;
    nint host;
    string text="Soma Finder";
    bool paused;
    internal static string HostDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SomaFinder","TrayHost");
    internal static string HostPath=>Path.Combine(HostDirectory,TrayProtocol.Executable);
    internal nint HostWindow=>host;
    internal event Action? DoubleClick;
    internal event Action<System.Drawing.Point>? ContextMenuRequested;
    internal event Action<string>? Failed;
    internal StableTrayIcon(HwndSource owner){this.owner=owner;owner.AddHook(Hook);}
    internal string Text{get=>text;set{text=value;if(host!=0&&!Send(TrayProtocol.Tooltip,text)){host=0;_=ShowAsync();}}}
    internal bool Paused{get=>paused;set{if(paused==value)return;paused=value;if(host!=0&&!Send(TrayProtocol.Appearance,paused?"paused":"normal")){host=0;_=ShowAsync();}}}
    internal Task ShowAsync()=>disposed?Task.CompletedTask:starting is{IsCompleted:false}?starting:starting=Start();
    async Task Start()
    {
        try
        {
            await Task.Run(Deploy);
            if(disposed)return;
            host=TrayProtocol.FindWindowW(TrayProtocol.WindowClass,null);
            if(host==0)
            {
                using var process=Process.Start(new ProcessStartInfo(HostPath){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=HostDirectory})??throw new IOException("无法启动托盘组件。");
                for(int i=0;i<100&&host==0&&!disposed;i++){host=TrayProtocol.FindWindowW(TrayProtocol.WindowClass,null);if(host==0){if(process.HasExited)throw new IOException("托盘组件启动失败："+process.ExitCode);await Task.Delay(50);}}
            }
            if(disposed)return;
            using var self=Process.GetCurrentProcess();
            if(host==0||!Send(TrayProtocol.Connect,self.Id+"\n"+self.StartTime.ToUniversalTime().ToFileTimeUtc()+"\n"+text))throw new IOException("托盘图标注册失败，请先退出其他版本的 Soma Finder。");
            if(!Send(TrayProtocol.Appearance,paused?"paused":"normal"))throw new IOException("托盘状态更新失败。");
        }
        catch(Exception ex){host=0;Failed?.Invoke(ex.Message);}
        finally{starting=null;}
    }
    static void Deploy()
    {
        var source=Path.Combine(AppContext.BaseDirectory,"Components",TrayProtocol.Executable);
        var image=Path.Combine(AppContext.BaseDirectory,"Components","SomaFinder.ico");
        if(!File.Exists(source)||!File.Exists(image))throw new IOException("缺少 Components 托盘组件，请完整解压新版程序。");
        Directory.CreateDirectory(HostDirectory);
        Replace(source,HostPath);Replace(image,Path.Combine(HostDirectory,"SomaFinder.ico"));
    }
    static void Replace(string source,string destination)
    {
        if(File.Exists(destination)&&SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(destination))))return;
        if(destination.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
        {
            for(int i=0;i<50&&TrayProtocol.FindWindowW(TrayProtocol.WindowClass,null)!=0;i++)Thread.Sleep(100);
            if(TrayProtocol.FindWindowW(TrayProtocol.WindowClass,null)!=0)throw new IOException("旧托盘组件仍在使用，请先退出旧版 Soma Finder。");
        }
        var incoming=destination+".incoming-"+Guid.NewGuid().ToString("N");File.Copy(source,incoming);
        if(File.Exists(destination))File.Replace(incoming,destination,destination+".backup-"+Guid.NewGuid().ToString("N"));
        else File.Move(incoming,destination);
    }
    bool Send(nint tag,string value)
    {
        var memory=Marshal.StringToHGlobalUni(value);
        try{var packet=new TrayProtocol.CopyPacket{Tag=tag,Bytes=(value.Length+1)*2,Data=memory};return TrayProtocol.SendMessageTimeoutW(host,TrayProtocol.CopyData,owner.Handle,ref packet,2,2000,out var result)!=0&&result==1;}
        finally{Marshal.FreeHGlobal(memory);}
    }
    nint Hook(nint hwnd,int message,nint wp,nint lp,ref bool handled)
    {
        if((uint)message!=callback||disposed)return 0;
        handled=true;
        if(wp==TrayProtocol.Activate)DoubleClick?.Invoke();
        else if(wp==TrayProtocol.Context)
        {
            long raw=lp.ToInt64();var point=new System.Drawing.Point((short)(raw&0xffff),(short)((raw>>16)&0xffff));
            if(point.X==-1&&point.Y==-1)point=System.Windows.Forms.Cursor.Position;
            ContextMenuRequested?.Invoke(point);
        }
        return 0;
    }
    public void Dispose(){if(disposed)return;disposed=true;if(host!=0)Send(TrayProtocol.Close,"");host=0;owner.RemoveHook(Hook);}
}
