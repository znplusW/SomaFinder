// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SomaFinder.Tray;

namespace SomaFinder;

// This mode starts only the real tray path. It never initializes or searches a Soma backend.
internal static class TrayVerification
{
    public static int Run(string[] args)
    {
        string Arg(string name)=>args.Single(a=>a.StartsWith(name+"="))[(name.Length+1)..];
        var report=Path.GetFullPath(Arg("--report"));var root=Path.GetFullPath(Arg("--fixture"));
        bool hold=args.Contains("--hold"),restart=args.Contains("--restart-host");
        CloudSandbox.Initialize(root);
        var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),new Preferences{StartupSync=false,PeriodicSync=false},true);
        var tests=new List<object>();int failures=0;object? snapshot=null;
        app.Startup+=async(_,_)=>
        {
            void Check(bool condition,string message){if(!condition)throw new Exception(message);}
            async Task Wait(Func<bool> condition){for(int i=0;i<200&&!condition();i++)await Task.Delay(50);Check(condition(),"Timed out waiting for tray state");}
            async Task Test(string name,Func<Task> action){try{await action();tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString(),foreground=GetForegroundWindow().ToInt64(),menuOpen=app.VerificationTrayMenu?.IsOpen,searchWindows=app.Windows.OfType<SearchWindow>().Count(),mouseCapture=System.Windows.Input.Mouse.Captured?.GetType().FullName});}}
            object? RegistrySnapshot()
            {
                using var top=Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
                Check(top!=null,"Windows notification settings registry is unavailable");
                var rows=new List<object>();
                foreach(var key in top.GetSubKeyNames())
                {
                    using var item=top.OpenSubKey(key);
                    var path=item?.GetValue("ExecutablePath") as string;
                    if(path==null||!Native.SameFilePath(Environment.ExpandEnvironmentVariables(path),StableTrayIcon.HostPath))continue;
                    rows.Add(new{key,path,promoted=item?.GetValue("IsPromoted"),guid=item?.GetValue("IconGuid")?.ToString()});
                }
                Check(rows.Count==1,"Expected exactly one Windows tray settings entry: "+rows.Count);
                return new{available=rows.Count>0,count=rows.Count,rows};
            }
            int HostPid(){TrayProtocol.GetWindowThreadProcessId(app.VerificationTray.HostWindow,out var pid);return (int)pid;}
            var tray=app.VerificationTray;Process? outside=null;
            try
            {
                await Test("fixed GUID registers through the fixed per-user executable, not the portable version path",async()=>
                {
                    await tray.ShowAsync();
                    Check(CloudSandbox.Errors.Count==0,string.Join("\n",CloudSandbox.Errors));
                    await Wait(()=>TrayProtocol.IsRegistered());
                    Check(TrayProtocol.IconId==Guid.Parse("6e2738c9-48fb-41d1-9a3e-c54ef936409b"),"Product GUID changed");
                    Check(StableTrayIcon.HostPath==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SomaFinder","TrayHost","SomaFinder.TrayHost.exe"),"Permanent host path contract changed");
                    using var host=Process.GetProcessById(HostPid());
                    Check(Native.SameFilePath(host.MainModule!.FileName!,StableTrayIcon.HostPath),"Tray owned by version directory");
                    Check(!Native.SameFilePath(host.MainModule.FileName!,Environment.ProcessPath!),"Main executable owns the icon");
                });
                if(failures>0)return;
                int originalPid=HostPid();
                await Test("200 progress tooltip updates keep the same icon identity and host process",()=>
                {
                    for(int i=0;i<200;i++)tray.Text="Soma Finder · 提取文件内容 · "+i+"/200";
                    Check(HostPid()==originalPid&&TrayProtocol.IsRegistered(),"Tooltip changed icon identity");
                    return Task.CompletedTask;
                });
                await Test("native double click activates one recent search window",async()=>
                {
                    TrayProtocol.PostMessageW(tray.HostWindow,TrayProtocol.ShellCallback,0,(nint)((1<<16)|0x203));
                    await Wait(()=>app.Windows.OfType<SearchWindow>().Count()==1);
                    var selected=app.Windows.OfType<SearchWindow>().Single();var handle=new WindowInteropHelper(selected).Handle;
                    var foreground=GetForegroundWindow();TrayProtocol.GetWindowThreadProcessId(foreground,out var foregroundPid);
                    tests.Add(new{name="activation observation",selected.IsActive,selected.IsVisible,nativeVisible=IsWindowVisible(handle),window=handle.ToInt64(),foreground=foreground.ToInt64(),foregroundPid});
                    await Wait(()=>app.Windows.OfType<SearchWindow>().Single().IsActive);
                    TrayProtocol.PostMessageW(tray.HostWindow,TrayProtocol.ShellCallback,0,(nint)((1<<16)|0x203));await Task.Delay(100);
                    Check(app.Windows.OfType<SearchWindow>().Count()==1,"Double click created another search window");
                });
                await Test("native context callback opens the existing tray menu and index controls",async()=>
                {
                    TrayProtocol.PostMessageW(tray.HostWindow,TrayProtocol.ShellCallback,0,(nint)((1<<16)|0x7b));
                    await Wait(()=>app.VerificationTrayMenu?.IsOpen==true);
                    var names=app.VerificationTrayMenu!.Items.OfType<MenuItem>().Select(x=>x.Header as string).ToList();
                    Check(names.Contains("退出 Soma Finder")&&names.Contains("暂停至下次启动")&&names.Contains("设置"),"Tray menu actions lost");
                    app.VerificationTrayMenu.IsOpen=false;
                });
                await Test("pause badge follows policies and preserves native icon identity",()=>TrayBadgeVerification.Verify(app));
                await Test("outside click dismisses tray menu with every search window closed",async()=>
                {
                    foreach(var window in app.Windows.OfType<SearchWindow>().ToArray())window.Close();
                    // SearchWindow completes its async closing policy on the Dispatcher.
                    await Wait(()=>!app.Windows.OfType<SearchWindow>().Any());
                    Check(!app.Windows.OfType<SearchWindow>().Any(),"Search windows were not closed");
                    var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Normal};
                    start.ArgumentList.Add("--verify-tray-target");start.ArgumentList.Add("--fixture="+root+"-outside");start.ArgumentList.Add("--parent="+Environment.ProcessId);
                    outside=Process.Start(start)??throw new Exception("External click target did not start");
                    await Wait(()=>{outside.Refresh();return outside.MainWindowHandle!=0;});
                    await OpenForInput();
                    GetWindowRect(outside.MainWindowHandle,out var bounds);Click(bounds.Left+60,bounds.Top+70);
                    await Wait(()=>app.VerificationTrayMenu?.IsOpen!=true);
                    await Wait(()=>GetForegroundWindow()==outside.MainWindowHandle);
                    Check(!app.Windows.OfType<SearchWindow>().Any(),"Opening tray menu reopened a search window");
                });
                await Test("Escape dismisses the tray menu after an external click and repeated reopening",async()=>
                {
                    for(int i=0;i<3;i++)
                    {
                        await OpenForInput();Key(0x1b);
                        await Wait(()=>app.VerificationTrayMenu?.IsOpen!=true);
                    }
                });
                await Test("native menu item click still activates a search window and closes the popup",async()=>
                {
                    await OpenForInput();var item=app.VerificationTrayMenu!.Items.OfType<MenuItem>().First();
                    var point=item.PointToScreen(new Point(item.ActualWidth/2,item.ActualHeight/2));Click((int)point.X,(int)point.Y);
                    await Wait(()=>app.Windows.OfType<SearchWindow>().Count()==1);
                    await Wait(()=>app.Windows.OfType<SearchWindow>().Single().IsActive&&app.VerificationTrayMenu?.IsOpen!=true);
                });
                async Task OpenForInput()
                {
                    Check(outside!=null&&!outside.HasExited,"External target is unavailable");
                    // The fixture foreground process delegates activation, as Explorer does for a
                    // genuine tray click. Dismissal itself uses real SendInput on a different PID.
                    var granted=SendMessageW(outside.MainWindowHandle,0x8001,Environment.ProcessId,0);
                    SetCursorPos(GetSystemMetrics(0)-30,GetSystemMetrics(1)-80);
                    TrayProtocol.PostMessageW(tray.HostWindow,TrayProtocol.ShellCallback,0,(nint)((1<<16)|0x7b));
                    await Wait(()=>app.VerificationTrayMenu?.IsOpen==true);await Task.Delay(120);
                    Check(app.VerificationTrayMenu?.IsOpen==true,"Menu closed before input");
                    tests.Add(new{name="menu input preparation",foregroundGrant=granted.ToInt64(),foreground=GetForegroundWindow().ToInt64(),target=outside.MainWindowHandle.ToInt64(),popup=(PresentationSource.FromVisual(app.VerificationTrayMenu!) as HwndSource)?.Handle.ToInt64(),keyboardFocus=app.VerificationTrayMenu!.IsKeyboardFocusWithin,mouseCapture=System.Windows.Input.Mouse.Captured?.GetType().FullName});
                }
                await Test("taskbar recreation notifications reuse the same GUID",async()=>
                {
                    // Use real persisted policy here. Window closing saves preferences and emits
                    // Changed, so a directly injected tray flag is not a stable fixture state.
                    app.Backend.Prefs.PauseUntil=DateTimeOffset.UtcNow.AddHours(1);app.Backend.Save();
                    await app.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check(app.Backend.TrayState.Paused&&tray.Paused,"Persisted pause policy did not reach the tray");TrayBadgeVerification.AssertPausedNative(app);
                    uint message=TrayProtocol.RegisterWindowMessageW("TaskbarCreated");
                    for(int i=0;i<3;i++){TrayProtocol.PostMessageW(tray.HostWindow,message,0,0);await Task.Delay(100);Check(TrayProtocol.IsRegistered(),"Icon missing after taskbar notification");TrayBadgeVerification.AssertPausedNative(app);}
                    Check(HostPid()==originalPid,"Taskbar notification replaced host");
                });
                if(restart)await Test("an unexpectedly terminated tray host recovers at the same path and GUID",async()=>
                {
                    using(var process=Process.GetProcessById(HostPid())){Check(Native.SameFilePath(process.MainModule!.FileName!,StableTrayIcon.HostPath),"Refusing to terminate unrelated process");process.Kill();await process.WaitForExitAsync();}
                    tray.Text="Soma Finder · 恢复托盘 · 200/200";
                    await Wait(()=>tray.HostWindow!=0&&HostPid()!=originalPid&&TrayProtocol.IsRegistered());TrayBadgeVerification.AssertPausedNative(app);
                });
                snapshot=new
                {
                    main=Environment.ProcessPath,version=FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion,
                    host=StableTrayIcon.HostPath,hostVersion=FileVersionInfo.GetVersionInfo(StableTrayIcon.HostPath).FileVersion,
                    hostPid=HostPid(),hostWindow=tray.HostWindow.ToInt64(),guid=TrayProtocol.IconId,
                    notificationSettings=RegistrySnapshot(),shellRegistered=TrayProtocol.IsRegistered()
                };
                if(hold&&failures==0)
                {
                    Store.Atomic(report,JsonSerializer.Serialize(new{ready=true,snapshot,tests}));
                    await Task.Delay(TimeSpan.FromSeconds(90));
                }
                await Test("normal exit removes the live icon and terminates its helper",async()=>
                {
                    int pid=HostPid();tray.Dispose();
                    await Wait(()=>!TrayProtocol.IsRegistered());
                    await Wait(()=>{try{using var process=Process.GetProcessById(pid);return process.HasExited;}catch(ArgumentException){return true;}});
                });
                Check(CloudSandbox.Errors.Count==0,string.Join("\n",CloudSandbox.Errors));
            }
            catch(Exception ex){failures++;tests.Add(new{name="suite",passed=false,error=ex.ToString()});}
            finally
            {
                if(outside!=null){if(!outside.HasExited){outside.CloseMainWindow();await Task.WhenAny(outside.WaitForExitAsync(),Task.Delay(3000));if(!outside.HasExited)outside.Kill();}outside.Dispose();}
                Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,snapshot,tests,scope="Tray identity and callbacks only, actual cloud Windows Shell; no Soma backend or index tests."},new JsonSerializerOptions{WriteIndented=true}));
                tray.Dispose();app.Backend.Dispose();app.Shutdown(failures==0?0:1);
            }
        };
        return app.Run();
    }
    // Separate-process click surface; only reachable on hosted Windows with an isolated fixture.
    public static int RunTarget(string[] args)
    {
        string Arg(string name)=>args.Single(a=>a.StartsWith(name+"="))[(name.Length+1)..];
        CloudSandbox.Initialize(Arg("--fixture"));using var parent=Process.GetProcessById(int.Parse(Arg("--parent")));
        var app=new Application();var window=new Window{Title="Soma Finder cloud outside-click target",Width=190,Height=140,Left=30,Top=30,Content=new TextBlock{Text="Cloud input target",Margin=new Thickness(10)}};
        window.SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).AddHook((nint hwnd,int message,nint wp,nint lp,ref bool handled)=>
        {if(message==0x8001){handled=true;return AllowSetForegroundWindow((uint)wp)?1:0;}return 0;});
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>{if(parent.HasExited){timer.Stop();app.Shutdown();}};timer.Start();
        return app.Run(window);
    }
    static void Click(int x,int y)
    {
        if(!SetCursorPos(x,y))throw new Exception("SetCursorPos failed");
        var events=new[]{new Input{Data=new InputData{Mouse=new MouseInput{Flags=2}}},new Input{Data=new InputData{Mouse=new MouseInput{Flags=4}}}};
        if(SendInput(2,events,Marshal.SizeOf<Input>())!=2)throw new Exception("SendInput mouse failed");
    }
    static void Key(ushort key)
    {
        var events=new[]{new Input{Type=1,Data=new InputData{Keyboard=new KeyboardInput{Key=key}}},new Input{Type=1,Data=new InputData{Keyboard=new KeyboardInput{Key=key,Flags=2}}}};
        if(SendInput(2,events,Marshal.SizeOf<Input>())!=2)throw new Exception("SendInput keyboard failed");
    }
    [StructLayout(LayoutKind.Sequential)]struct Input{internal uint Type;internal InputData Data;}
    [StructLayout(LayoutKind.Explicit)]struct InputData{[FieldOffset(0)]internal MouseInput Mouse;[FieldOffset(0)]internal KeyboardInput Keyboard;}
    [StructLayout(LayoutKind.Sequential)]struct MouseInput{internal int X,Y;internal uint Data,Flags,Time;internal nuint Extra;}
    [StructLayout(LayoutKind.Sequential)]struct KeyboardInput{internal ushort Key,Scan;internal uint Flags,Time;internal nuint Extra;}
    [StructLayout(LayoutKind.Sequential)]struct Rect{internal int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern uint SendInput(uint count,Input[] input,int size);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint window,out Rect rectangle);
    [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll")]static extern nint SendMessageW(nint window,uint message,nint wp,nint lp);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsWindowVisible(nint window);
}
