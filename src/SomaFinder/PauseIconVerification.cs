// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SomaFinder.Tray;
using Drawing=System.Drawing;

namespace SomaFinder;

// Only cloud synthetic windows and the real Shell; no Soma service or index is started.
internal static class PauseIconVerification
{
    static List<object>? shellObservations;
    static string evidenceDirectory="";
    public static int Run(string[] args)
    {
        string Arg(string name)=>args.Single(a=>a.StartsWith(name+"="))[(name.Length+1)..];
        string report=Path.GetFullPath(Arg("--report")),root=Path.GetFullPath(Arg("--fixture"));
        evidenceDirectory=Path.GetDirectoryName(report)!;
        CloudSandbox.Initialize(root);
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,BatteryBlocksAuto=false,LowMemoryBlocksAuto=false};
        var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),prefs,true);
        var tests=new List<object>();var observations=new List<object>();shellObservations=observations;int failures=0,normalButtonOrange=-1;var windows=new List<Window>();
        Drawing.Bitmap? normal=null;string normalHash=Store.Digest(Environment.ProcessPath!),ico=Path.Combine(AppContext.BaseDirectory,"Components","SomaFinder.ico"),iconHash=Store.Digest(Path.Combine(AppContext.BaseDirectory,"Components","SomaFinder.ico"));
        string shortcut=Arg("--shortcut"),shortcutHash=Store.Digest(Arg("--shortcut"));
        bool actualPinned=false,exitStartedPaused=false;string pinReason="not attempted";
        void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        async Task Wait(Func<bool> condition){for(int i=0;i<80&&!condition();i++)await Task.Delay(50);Check(condition(),"Timed out waiting for icon state");}
        async Task Test(string name,Func<Task> action){try{await action();tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString()});}}
        string ImagePath(string name)=>Path.Combine(Path.GetDirectoryName(report)!,name+".png");
        app.Exit+=(_,_)=>
        {
            try
            {
                Check(exitStartedPaused,"Exit fixture never entered shutdown with a live pause overlay");
                Check(windows.All(w=>w.Icon==null&&w.TaskbarItemInfo?.Overlay==null),"Exit retained a running-window overlay");
                Check(Store.Digest(Environment.ProcessPath!)==normalHash&&Store.Digest(ico)==iconHash&&Store.Digest(shortcut)==shortcutHash,"Executable/icon/shortcut changed");
                tests.Add(new{name="application exit clears every tracked overlay and leaves original executable/icon/shortcut unchanged",passed=true});
            }
            catch(Exception ex){failures++;tests.Add(new{name="exit cleanup",passed=false,error=ex.ToString()});}
            Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,observations,actualPinned,pinReason,normalButtonOrange,scope="Only native window pause icons, taskbar window synchronization and transient cleanup. Real cloud Windows Shell screenshots and native tray pixels. Context-menu pin is attempted through Shell UI; actualPinned=false is explicitly untested for a real pinned entry. No Soma backend, touch tests or full regression."},new JsonSerializerOptions{WriteIndented=true}));
            Environment.ExitCode=failures==0?0:1;
        };
        app.Startup+=async(_,_)=>
        {
            try
            {
                await Test("normal native icon and running taskbar start without an overlay",async()=>
                {
                    await app.VerificationTray.ShowAsync();app.NewWindow();var first=app.Windows.OfType<SearchWindow>().Single();windows.Add(first);
                    first.WindowState=WindowState.Normal;first.Width=850;first.Height=500;first.Left=30;first.Top=30;
                    await Wait(()=>first.IsLoaded&&TrayProtocol.IsRegistered());await Task.Delay(500);
                    Check(first.Icon==null&&first.TaskbarItemInfo?.Overlay==null,"Normal taskbar unexpectedly paused");normal=NativeIcon(app);normal.Save(ImagePath("tray-normal"));
                    CaptureContext(ImagePath("desktop-normal"),ImagePath("taskbar-normal"),windows);
                    using var button=await TaskButtonBitmap();button.Save(ImagePath("taskbar-button-normal"));normalButtonOrange=OrangePixels(button);
                    observations.Add(new{name="normal Soma taskbar button",orangePixels=normalButtonOrange});
                });
                if(normal==null)return;
                // Pin is a user-facing Shell operation, not a forged registry record.
                try{actualPinned=await TryPin();pinReason=actualPinned?"Shell context menu showed Unpin from taskbar after invocation":"No supported pin command was exposed by the cloud Shell";}
                catch(Exception ex){pinReason="Shell pin unavailable: "+ex.Message;Key(0x1b);}
                observations.Add(new{name="real pin attempt",actualPinned,pinReason});
                if(actualPinned){using var button=await TaskButtonBitmap();normalButtonOrange=OrangePixels(button);button.Save(ImagePath("taskbar-button-pinned-normal"));}
                await Test("paused tray retains original artwork and adds only two orange bars",async()=>
                {
                    prefs.PauseUntil=DateTimeOffset.UtcNow.AddHours(1);app.Backend.Save();
                    await Wait(()=>app.VerificationTray.Paused&&windows.All(w=>app.VerificationTaskbarIcons.HasPauseIcon(w)));
                    using var paused=NativeIcon(app);paused.Save(ImagePath("tray-paused"));
                    int changed=0,orange=0;
                    for(int y=0;y<normal.Height;y++)for(int x=0;x<normal.Width;x++)
                    {
                        int before=normal.GetPixel(x,y).ToArgb(),after=paused.GetPixel(x,y).ToArgb();
                        if(x<20||y<20)Check(before==after,"Base logo changed outside lower-right overlay");
                        if(before!=after)changed++;if(IsOrange(paused.GetPixel(x,y)))orange++;
                    }
                    Check(changed>0&&orange>20,"Missing orange pause overlay");
                    Check(IsOrange(paused.GetPixel(22,25))&&IsOrange(paused.GetPixel(28,25)),"Both bars must be orange");
                    Check(!IsOrange(paused.GetPixel(25,25)),"Pause bars are joined by a filled badge");
                    using var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
                    int rows=key!.GetSubKeyNames().Count(n=>{using var entry=key.OpenSubKey(n);return entry?.GetValue("ExecutablePath") is string p&&Native.SameFilePath(Environment.ExpandEnvironmentVariables(p),StableTrayIcon.HostPath);});
                    Check(rows==1,"Visual change created another tray identity");
                });
                await Test("actual taskbar renders a paused window icon without Shell overlays",async()=>
                {
                    await Task.Delay(700);CaptureContext(ImagePath("desktop-paused"),ImagePath("taskbar-paused"),windows);
                    using var paused=await TaskButtonBitmap();paused.Save(ImagePath("taskbar-button-paused"));
                    Check(windows.All(w=>w.TaskbarItemInfo?.Overlay==null),"Fixture unexpectedly relies on a Shell overlay");
                    foreach(int size in new[]{0,1}){using var native=WindowIcon(windows[0],size);native.Save(ImagePath("window-paused-"+size));Check(OrangePixels(native)>=4,"Native small/large icon did not contain pause bars");}
                    int orange=OrangePixels(paused);observations.Add(new{name="paused Soma taskbar button",orangePixels=orange,normalButtonOrange});
                    Check(normalButtonOrange>=0&&orange>=normalButtonOrange+4,"Actual Soma taskbar button did not gain orange pause pixels without an overlay");
                });
                await Test("minimized running window retains the paused native and taskbar icons",async()=>
                {
                    var first=windows[0];first.WindowState=WindowState.Minimized;
                    try{await Task.Delay(500);using var button=await TaskButtonBitmap();button.Save(ImagePath("taskbar-minimized-paused"));Check(OrangePixels(button)>normalButtonOrange&&app.VerificationTaskbarIcons.HasPauseIcon(first),"Minimizing lost the pause icon");}
                    finally{first.WindowState=WindowState.Normal;}
                });
                await Test("new search and detached windows inherit pause and resume clears all windows",async()=>
                {
                    var first=(SearchWindow)windows[0];app.NewWindow();var second=app.Windows.OfType<SearchWindow>().First(w=>w!=first);windows.Add(second);
                    first.VerificationDetails.Float(DetailKind.Preview);var detached=first.VerificationDetails.FloatingWindow(DetailKind.Preview)!;windows.Add(detached);
                    await Wait(()=>windows.All(w=>w.IsLoaded&&app.VerificationTaskbarIcons.HasPauseIcon(w)));
                    await Task.Delay(500);using(var grouped=await TaskButtonBitmap()){grouped.Save(ImagePath("taskbar-group-paused"));Check(OrangePixels(grouped)>normalButtonOrange,"Grouped taskbar ignored paused window icons");}
                    app.Backend.Continue();await Wait(()=>!app.VerificationTray.Paused&&windows.All(w=>w.Icon==null&&w.TaskbarItemInfo?.Overlay==null));
                    using var restored=NativeIcon(app);Check(Same(normal,restored),"Resume did not restore original native icon");
                    await Task.Delay(500);using var bar=await TaskButtonBitmap();bar.Save(ImagePath("taskbar-button-resumed"));
                    Check(OrangePixels(bar)==normalButtonOrange,"Soma taskbar button retained orange overlay after resume");
                });
                await Test("closing every window leaves tray pause and removes taskbar overlays",async()=>
                {
                    prefs.PauseUntil=DateTimeOffset.UtcNow.AddHours(1);app.Backend.Save();await Wait(()=>windows.All(w=>app.VerificationTaskbarIcons.HasPauseIcon(w)));
                    foreach(var search in app.Windows.OfType<SearchWindow>().ToArray())search.Close();
                    await Wait(()=>!app.Windows.OfType<SearchWindow>().Any()&&!app.Windows.Cast<Window>().Any(w=>w.ShowInTaskbar&&w.IsVisible));
                    Check(windows.All(w=>w.Icon==null&&w.TaskbarItemInfo?.Overlay==null)&&app.VerificationTray.Paused,"Closed window kept overlay or tray lost pause");
                    await Task.Delay(500);CaptureContext(ImagePath("desktop-tray-only"),ImagePath("taskbar-tray-only"),windows);
                    if(actualPinned){using var button=await TaskButtonBitmap();button.Save(ImagePath("taskbar-button-tray-only"));Check(OrangePixels(button)==normalButtonOrange,"Real pinned entry did not restore its base icon");}
                    else Check(await FindTaskButton()==null,"Taskbar still has an unpinned running Soma button after all windows closed");
                });
                await Test("application shutdown starts with a live paused taskbar window",async()=>
                {
                    app.NewWindow();var search=app.Windows.OfType<SearchWindow>().Single();windows.Add(search);
                    await Wait(()=>app.VerificationTaskbarIcons.HasPauseIcon(search));
                    exitStartedPaused=app.VerificationTaskbarIcons.HasPauseIcon(search);
                    Check(exitStartedPaused,"Pause overlay was cleared before actual application shutdown");
                    Check(CloudSandbox.Errors.Count==0,string.Join("\n",CloudSandbox.Errors));
                });
            }
            catch(Exception ex){failures++;tests.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally{normal?.Dispose();app.VerificationTray.Dispose();app.Backend.Dispose();app.Shutdown(failures==0?0:1);}
        };
        app.Run();return failures==0?0:1;
    }
    static Drawing.Bitmap WindowIcon(Window window,int kind)
    {nint handle=SendMessageW(new WindowInteropHelper(window).Handle,0x7f,kind,0);if(handle==0)throw new Exception("Missing native window icon");using var icon=Drawing.Icon.FromHandle(handle);return icon.ToBitmap();}
    static Drawing.Bitmap NativeIcon(FinderApplication app)
    {
        nint image=SendMessageW(app.VerificationTray.HostWindow,0x7f,0,0);if(image==0)throw new Exception("No native icon");
        using var icon=Drawing.Icon.FromHandle(image);return icon.ToBitmap();
    }
    static bool Same(Drawing.Bitmap a,Drawing.Bitmap b){if(a.Size!=b.Size)return false;for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())return false;return true;}
    static bool IsOrange(Drawing.Color c)=>c.R>190&&c.G>75&&c.G<160&&c.B<65;
    static int OrangePixels(Drawing.Bitmap bitmap){int count=0;for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(IsOrange(bitmap.GetPixel(x,y)))count++;return count;}
    static Drawing.Bitmap Capture(Rect r)
    {
        int width=r.Right-r.Left,height=r.Bottom-r.Top;if(width<=0||height<=0)throw new Exception("Invalid taskbar bounds");
        var image=new Drawing.Bitmap(width,height);using var graphics=Drawing.Graphics.FromImage(image);graphics.CopyFromScreen(r.Left,r.Top,0,0,image.Size);return image;
    }
    static void CaptureContext(string desktopPath,string taskbarPath,IEnumerable<Window> windows)
    {
        var shell=FindWindowW("Shell_TrayWnd",null);if(shell==0||!GetWindowRect(shell,out var rect))throw new Exception("No real taskbar");
        using(var bar=Capture(rect))bar.Save(taskbarPath);
        int x=GetSystemMetrics(76),y=GetSystemMetrics(77);
        using(var desktop=Capture(new Rect{Left=x,Top=y,Right=x+GetSystemMetrics(78),Bottom=y+GetSystemMetrics(79)}))desktop.Save(desktopPath);
        var native=new List<object>();EnumChildWindows(shell,(h,_)=>{var name=new StringBuilder(128);GetClassNameW(h,name,128);GetWindowRect(h,out var r);native.Add(new{handle=h.ToInt64(),className=name.ToString(),r.Left,r.Top,r.Right,r.Bottom,visible=IsWindowVisible(h)});return true;},0);
        shellObservations?.Add(new{name="taskbar and application geometry",desktopPath,taskbarPath,rect.Left,rect.Top,rect.Right,rect.Bottom,native,windows=windows.Select(w=>{var h=new WindowInteropHelper(w).Handle;return new{w.Title,w.ShowInTaskbar,w.IsVisible,w.IsActive,handle=h.ToInt64(),nativeVisible=IsWindowVisible(h),style=GetWindowLongPtrW(h,-20).ToInt64()};}).ToArray()});
    }
    static async Task<Drawing.Bitmap> TaskButtonBitmap()
    {
        AutomationElement? button=null;for(int i=0;i<10&&button==null;i++){button=await FindTaskButton();if(button==null)await Task.Delay(200);}
        if(button==null)throw new Exception("No visible Soma taskbar button was found; cannot claim native overlay rendering");
        var b=button.Current.BoundingRectangle;
        shellObservations?.Add(new{name="captured Soma button",buttonName=button.Current.Name,buttonClass=button.Current.ClassName,b.X,b.Y,b.Width,b.Height});
        return Capture(new Rect{Left=(int)b.Left,Top=(int)b.Top,Right=(int)b.Right,Bottom=(int)b.Bottom});
    }
    static async Task<AutomationElement?> FindTaskButton()=>await Task.Run(()=>
    {
        var shell=FindWindowW("Shell_TrayWnd",null);GetWindowRect(shell,out var rect);
        // Modern taskbar XAML may be exposed beneath a different UIA desktop root than
        // the legacy MSTaskList window. Identify the actual Soma button by name+geometry.
        var all=AutomationElement.RootElement.FindAll(TreeScope.Descendants,System.Windows.Automation.Condition.TrueCondition).Cast<AutomationElement>();
        var candidates=new List<AutomationElement>();var seen=new List<object>();
        foreach(var e in all)try
        {
            var c=e.Current;var r=c.BoundingRectangle;
            if(c.IsOffscreen||r.IsEmpty||r.Width<20||r.Width>400||r.Height<20||r.Height>100||r.Top<rect.Top||r.Bottom>rect.Bottom||r.Right>rect.Right-180)continue;
            seen.Add(new{c.Name,c.ClassName,c.AutomationId,type=c.ControlType.ProgrammaticName,r.X,r.Y,r.Width,r.Height});
            if(c.Name.Contains("Soma",StringComparison.OrdinalIgnoreCase)&&(c.ControlType==ControlType.Button||c.ControlType==ControlType.ListItem))candidates.Add(e);
        }catch(ElementNotAvailableException){}
        shellObservations?.Add(new{name="taskbar UIA candidates",seen});
        return candidates.FirstOrDefault();
    }).WaitAsync(TimeSpan.FromSeconds(5));
    static async Task<AutomationElement?> PinCommand(string verb)=>await Task.Run(()=>
    {
        var items=AutomationElement.RootElement.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button))).Cast<AutomationElement>().ToArray();
        var visible=new List<object>();AutomationElement? found=null;
        foreach(var item in items)try
        {
            var c=item.Current;if(c.IsOffscreen)continue;
            if(c.ControlType==ControlType.MenuItem||c.Name.Contains("pin",StringComparison.OrdinalIgnoreCase))visible.Add(new{c.Name,c.ClassName,c.AutomationId});
            if(c.Name.Replace("&","").Trim().Equals(verb,StringComparison.OrdinalIgnoreCase))found=item;
        }catch(ElementNotAvailableException){}
        shellObservations?.Add(new{name="Shell pin command lookup",verb,visible,found=found!=null});return found;
    }).WaitAsync(TimeSpan.FromSeconds(5));
    static async Task<bool> TryPin()
    {
        var button=await FindTaskButton();if(button==null)return false;
        var bounds=button.Current.BoundingRectangle;RightClick((int)(bounds.Left+bounds.Width/2),(int)(bounds.Top+bounds.Height/2));await Task.Delay(500);
        int x=GetSystemMetrics(76),y=GetSystemMetrics(77);
        using(var screen=Capture(new Rect{Left=x,Top=y,Right=x+GetSystemMetrics(78),Bottom=y+GetSystemMetrics(79)}))screen.Save(Path.Combine(evidenceDirectory,"desktop-pin-menu.png"));
        var already=await PinCommand("Unpin from taskbar");if(already!=null){Key(0x1b);return true;}
        var command=await PinCommand("Pin to taskbar");if(command==null){Key(0x1b);return false;}
        if(command.TryGetCurrentPattern(InvokePattern.Pattern,out var pattern))((InvokePattern)pattern).Invoke();
        else{var r=command.Current.BoundingRectangle;LeftClick((int)(r.Left+r.Width/2),(int)(r.Top+r.Height/2));}
        await Task.Delay(600);button=await FindTaskButton();if(button==null)return false;
        bounds=button.Current.BoundingRectangle;RightClick((int)(bounds.Left+bounds.Width/2),(int)(bounds.Top+bounds.Height/2));await Task.Delay(400);
        bool pinned=await PinCommand("Unpin from taskbar")!=null;Key(0x1b);await Task.Delay(200);return pinned;
    }
    static void RightClick(int x,int y)=>Click(x,y,8,16);
    static void LeftClick(int x,int y)=>Click(x,y,2,4);
    static void Click(int x,int y,uint down,uint up){SetCursorPos(x,y);var input=new[]{new Input{Data=new InputData{Mouse=new MouseInput{Flags=down}}},new Input{Data=new InputData{Mouse=new MouseInput{Flags=up}}}};if(SendInput(2,input,Marshal.SizeOf<Input>())!=2)throw new Exception("Mouse injection failed");}
    static void Key(ushort code){var input=new[]{new Input{Type=1,Data=new InputData{Keyboard=new KeyboardInput{Key=code}}},new Input{Type=1,Data=new InputData{Keyboard=new KeyboardInput{Key=code,Flags=2}}}};SendInput(2,input,Marshal.SizeOf<Input>());}
    [StructLayout(LayoutKind.Sequential)]struct Input{internal uint Type;internal InputData Data;}
    [StructLayout(LayoutKind.Explicit)]struct InputData{[FieldOffset(0)]internal MouseInput Mouse;[FieldOffset(0)]internal KeyboardInput Keyboard;}
    [StructLayout(LayoutKind.Sequential)]struct MouseInput{internal int X,Y;internal uint Data,Flags,Time;internal nuint Extra;}
    [StructLayout(LayoutKind.Sequential)]struct KeyboardInput{internal ushort Key,Scan;internal uint Flags,Time;internal nuint Extra;}
    [StructLayout(LayoutKind.Sequential)]struct Rect{internal int Left,Top,Right,Bottom;}
    delegate bool EnumWindow(nint window,nint value);
    [DllImport("user32.dll")]static extern bool EnumChildWindows(nint window,EnumWindow callback,nint value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassNameW(nint window,StringBuilder name,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern nint FindWindowW(string name,string? title);
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint window,out Rect rectangle);
    [DllImport("user32.dll")]static extern nint SendMessageW(nint window,uint message,nint wp,nint lp);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern uint SendInput(uint count,Input[] input,int size);
    [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]static extern nint GetWindowLongPtrW(nint window,int index);
}
