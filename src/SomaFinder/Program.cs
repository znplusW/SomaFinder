// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.IO.Pipes;
using Forms=System.Windows.Forms;

namespace SomaFinder;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if(Environment.GetEnvironmentVariable(ModelMemoryVerification.FixtureVariable)!=null&&(args.Contains("--model-memory-router-fixture")||System.IO.Path.GetFileName(Environment.ProcessPath).Equals("soma.exe",StringComparison.OrdinalIgnoreCase)))return ModelMemoryVerification.Fixture(args);
        if(args.Contains("--thumbnail-worker")){RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;return ThumbnailWorker.Run();}
        try
        {
            // Scope the rendering compatibility workaround to this WPF process.
            // Must precede every HwndSource, including the global hotkey window.
            RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
            if(args.Contains("--verify-model-memory"))return ModelMemoryVerification.Run(args);
            if(args.Contains("--verify-public-release"))return PublicReleaseVerification.Run(args);
            if(args.Contains("--verify-floating-windows"))return FloatingWindowVerification.Run(args);
            if(args.Contains("--verify-waterfall-resize"))return WaterfallResizeVerification.Run(args);
            if(args.Contains("--verify-pane-toggles"))return PaneToggleVerification.Run(args);
            if(args.Contains("--verify-waterfall"))return WaterfallVerification.Run(args);
            if(args.Contains("--verify-icon-size"))return IconSizeVerification.Run(args);
            if(args.Contains("--verify-tray-exit"))return TrayExitVerification.Run(args);
            if(args.Any(a=>a.StartsWith("--verify-lifetime-")))return LifetimeVerification.Run(args);
            if(args.Contains("--verify-responsive"))return ResponsiveVerification.Run(args);
            if(args.Contains("--verify-native-touch"))return NativeTouchVerification.Run(args);
            if(args.Contains("--verify-tray-target"))return TrayVerification.RunTarget(args);
            if(args.Contains("--verify-summary-density"))return SummaryDensityVerification.Run(args);
            if(args.Contains("--verify-pause-icons"))return PauseIconVerification.Run(args);
            if(args.Contains("--verify-image-preview"))return ImagePreviewVerification.Run(args);
            if(args.Contains("--verify-summary-touch"))return SummaryTouchVerification.Run(args);
            if(args.Contains("--verify-tray-owner"))return TrayVerification.Run(args);
            if(args.Contains("--verify-discovery")) return DiscoveryVerification.Run(args);
            if(args.Contains("--verify-backends")) return BackendVerification.Run(args);
            if(args.Contains("--verify-optimizations"))return OptimizationVerification.Run(args);
            if(args.Contains("--verify-search-scenario"))return SearchScenarioVerification.Run(args);
            if(args.Contains("--verify-scheduling"))return SchedulingVerification.Run(args);
            if(args.Contains("--verify-design")) return DesignVerification.Run(args);
            if(args.Contains("--verify-policy")) return PolicyVerification.Run(args);
            if(args.Contains("--verify-cloud")) return CloudVerification.Run(args);
            if(args.Contains("--verify-new")) return UpdateVerification.Run(args);
            if(args.Contains("--verify")) return Verification.Run(args);
            using var mutex=new Mutex(true,"Local\\SomaFinder-"+Environment.UserName,out var first);
            if(!first)
            {
                using var client=new NamedPipeClientStream(".","SomaFinder-"+Environment.UserName,PipeDirection.Out);client.Connect(3000);return 0;
            }
            var store=new Store(args.FirstOrDefault(x=>x.StartsWith("--data-dir="))?[11..]);var prefs=store.Load();
            var app=new FinderApplication(store,prefs);app.Run();return 0;
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"Soma Finder 无法启动",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
    }
}
public sealed class FinderApplication : Application
{
    public Store Store {get;}
    public Backend Backend {get;}
    readonly StableTrayIcon tray;
    readonly TaskbarStatusIcons taskbarIcons;
    readonly List<SearchWindow> windows=new();
    readonly SearchTitles searchTitles=new();
    SearchWindow? recent;SettingsWindow? settings;
    readonly System.Windows.Threading.DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(10)};
    readonly CancellationTokenSource stop=new();
    readonly HwndSource hotkeySource;
    bool quitting;
    public FinderApplication(Store store,Preferences prefs,bool headless=false)
    {
        Store=store;Backend=new(store,prefs);ShutdownMode=ShutdownMode.OnExplicitShutdown;ThemeMode=SearchWindow.ParseTheme(prefs.SearchLayout.Theme);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml") });
        Resources[SystemFonts.MessageFontFamilyKey]=new FontFamily("Segoe UI Variable, Microsoft YaHei UI");
        hotkeySource=new HwndSource(new HwndSourceParameters("SomaFinderHotkey"){Width=0,Height=0,WindowStyle=0});hotkeySource.AddHook(Hook);
        tray=new StableTrayIcon(hotkeySource);
        taskbarIcons=new TaskbarStatusIcons(this,()=>Backend.TrayState.Paused);
        tray.DoubleClick+=()=>ActivateRecent();
        tray.ContextMenuRequested+=ShowTrayMenu;
        tray.Failed+=message=>UI.Error("托盘图标暂不可用："+message+"\n请保持搜索窗口打开，完整解压后重新启动程序。");
        Exit+=(_,_)=>{taskbarIcons.Dispose();tray.Dispose();};
        Backend.Changed+=()=>Dispatcher.BeginInvoke(()=>{UpdateTray();foreach(var w in windows)w.UpdateStatus();settings?.UpdateStatus();});
        if(!headless)Microsoft.Win32.SystemEvents.PowerModeChanged+=(_,e)=>Dispatcher.BeginInvoke(async()=>{if(e.Mode==Microsoft.Win32.PowerModes.Resume)Backend.Resumed();try{await Backend.Pulse();}catch(Exception ex){UI.Error(ex.Message);}});
        if(!headless) Startup+=async(_,_)=>
        {
            NewWindow();RegisterHotkey();_=Listen();_=tray.ShowAsync();
            try{if(await ConfigureBackend()){await Backend.Initialize();foreach(var w in windows)w.ReloadTree();}else Backend.AwaitingBackendSelection();}catch(Exception ex){UI.Error(ex.Message);}
            timer.Tick+=async(_,_)=>{try{await Backend.Pulse();}catch(Exception ex){UI.Error(ex.Message);}};timer.Start();
        };
        DispatcherUnhandledException+=(_,e)=>{UI.Error(CloudSandbox.Active?e.Exception.ToString():e.Exception.Message);e.Handled=true;};
    }
    internal async Task<bool> ConfigureBackend()
    {
        var prefs=Backend.Prefs;var catalog=await BackendDiscovery.Scan(prefs);string? explicitPath=null;
        if(!catalog.Valid.Any())
        {
            var prompt=BackendDiscovery.CreateMissingPrompt(recent,()=>
            {
                var picker=new Microsoft.Win32.OpenFileDialog{Title="选择已部署的 Soma 后端",Filter="Soma 后端|soma.exe;soma-windows-x64.exe|可执行文件|*.exe"};if(picker.ShowDialog()==true)explicitPath=picker.FileName;
            },()=>{});prompt.ShowDialog();
            if(prompt.Tag is string found)catalog=await BackendDiscovery.Scan(prefs,new[]{found});
            else if(explicitPath!=null)catalog=await BackendDiscovery.Scan(prefs,new[]{explicitPath});
            else return false;
            if(!catalog.Valid.Any()){UI.Error("未能识别可用的 Soma 后端，请先完成部署。");return false;}
        }
        string? chosen=explicitPath!=null&&catalog.Valid.Any(c=>Native.SameFilePath(c.Path,explicitPath))?explicitPath:null;
        if(chosen==null&&BackendDiscovery.NeedsChoice(prefs,catalog))
        {
            var selector=new BackendSelectionWindow(recent,prefs,catalog);selector.ShowDialog();chosen=selector.SelectedPath;if(chosen==null)return false;
        }
        chosen??=catalog.Valid.FirstOrDefault(c=>Native.SameFilePath(c.Path,prefs.SomaExe))?.Path??catalog.Valid.First().Path;
        BackendDiscovery.Remember(prefs,chosen,catalog.Valid.Select(c=>c.Path));Backend.Save();return true;
    }
    async Task Listen()
    {
        while(!stop.IsCancellationRequested)
        {
            try{using var pipe=new NamedPipeServerStream("SomaFinder-"+Environment.UserName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.WaitForConnectionAsync(stop.Token);await Dispatcher.InvokeAsync(ActivateRecent);}
            catch(OperationCanceledException){break;}catch(Exception){await Task.Delay(500);}
        }
    }
    public void NewWindow()
    {
        var scope=recent?.Scope()??Backend.Prefs.LastSelection;
        var window=new SearchWindow(this,scope);windows.Add(window);recent=window;
        window.Activated+=(_,_)=>MarkRecent(window);
        window.Closed+=(_,_)=>{Backend.Prefs.LastSelection=window.Scope();try{Backend.Save();}catch{} windows.Remove(window);searchTitles.Remove(window);RefreshSearchTitles();if(recent==window)recent=windows.LastOrDefault();};
        window.Show();
    }
    public void MarkRecent(SearchWindow window){if(!quitting&&windows.Contains(window))recent=window;}
    public void SubmitSearchTitle(SearchWindow window,string query){searchTitles.Submit(window,query);RefreshSearchTitles();if(!windows.Contains(window))window.UpdateSearchTitle(searchTitles.Title(window));}
    void RefreshSearchTitles(){foreach(var window in windows)window.UpdateSearchTitle(searchTitles.Title(window));}
    public void ActivateRecent(){if(quitting)return;if(recent==null){NewWindow();return;}recent.Show();recent.RestoreAuxiliaries();if(recent.WindowState==WindowState.Minimized)recent.WindowState=WindowState.Normal;recent.Activate();}
    public async Task<bool> ClosingWindow(SearchWindow window)
    {
        if(quitting)return true;
        if(windows.Count==1&&Backend.Prefs.ExitOnLastWindow){await Quit(true);return quitting;}
        return true;
    }
    public void Settings()
    {
        if(settings!=null){settings.Activate();return;}
        settings=new SettingsWindow(this);settings.Closed+=(_,_)=>settings=null;settings.Show();
    }
    public void ApplyAppearance()
    {
        var theme=SearchWindow.ParseTheme(Backend.Prefs.SearchLayout.Theme);
        // WPF reloads Fluent resource dictionaries even for an unchanged ThemeMode.
        // A density-only save must update the current cards without rebuilding them.
        if(ThemeMode!=theme)ThemeMode=theme;
        foreach(var window in Windows.OfType<Window>()){if(window.ThemeMode!=theme)window.ThemeMode=theme;if(window is SearchWindow search)search.ApplyTheme();}
    }
    public void ConfigChanged(bool indexChanged=true){if(indexChanged){Backend.Reload();foreach(var w in windows)w.ReloadTree();}RegisterHotkey();}
    internal ContextMenu CreateTrayMenu(DialogLocation? location=null)
    {
        var menu=new ContextMenu();menu.Items.Add(UI.Menu("显示最近搜索窗口",()=>ActivateRecent()));
        menu.Items.Add(UI.Menu("显示所有搜索窗口",()=>{foreach(var w in windows){w.Show();w.RestoreAuxiliaries();w.WindowState=WindowState.Normal;}if(windows.Count==0)NewWindow();}));
        menu.Items.Add(UI.Menu("设置",Settings));menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem{Header=Backend.Message+" · "+Backend.Progress,IsEnabled=false});
        var toggle=UI.Menu(Backend.CanResume?"恢复索引":"暂停至下次启动",async()=>{try{if(Backend.CanResume){Backend.Continue();await Backend.Pulse();}else await Backend.StopSession();}catch(Exception ex){UI.Error(ex.Message);}});toggle.ToolTip="可手动恢复；退出并重新启动 Soma Finder 后，按设置恢复索引。关闭搜索窗口或睡眠恢复不会解除。";menu.Items.Add(toggle);menu.Items.Add(new Separator());menu.Items.Add(UI.Menu("退出 Soma Finder",()=>Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(async()=>await Quit(false,location)))));return menu;
    }
    ContextMenu? currentTrayMenu;
    DialogLocation? currentTrayLocation;
    TrayMenuHost? trayMenuHost;
    void ShowTrayMenu(System.Drawing.Point click)
    {
        if(quitting)return;
        if(trayMenuHost==null){trayMenuHost=new TrayMenuHost();Exit+=(_,_)=>trayMenuHost.Close();}
        currentTrayLocation=new DialogLocation(click);currentTrayMenu=CreateTrayMenu(currentTrayLocation);trayMenuHost.Open(currentTrayMenu);
    }
    public Task Quit(bool lastWindow)=>Quit(lastWindow,null);
    internal async Task Quit(bool lastWindow,DialogLocation? location)
    {
        if(quitting)return;
        if(Backend.Indexing)
        {
            var result=UI.Confirm("正在建立索引，建议保持 Soma Finder 在后台运行，以完成索引。\n\n退出将中断当前任务，部分未完成内容下次可能需要重新处理。", "退出 Soma Finder", "继续后台运行", "仍然退出",location);
            if(result!=true)
            {
                if(result==false){if(lastWindow){var w=recent;if(w!=null)_=Dispatcher.BeginInvoke(()=>{w.SkipExitOnce=true;w.Close();});}else foreach(var w in windows){w.HideAuxiliaries();w.Hide();}}
                return;
            }
        }
        settings?.PersistDraft();quitting=true;timer.Stop();stop.Cancel();
        try{await Backend.StopAll();}catch(Exception ex){quitting=false;Backend.AbortExit();timer.Start();UI.Error("未能结束 Soma，程序继续保留。\n"+ex.Message);return;}
        taskbarIcons.ClearForExit();Native.UnregisterHotKey(hotkeySource.Handle,1);tray.Dispose();Backend.Dispose();Shutdown();
    }
    public void RegisterHotkey()
    {
        Native.UnregisterHotKey(hotkeySource.Handle,1);
        try
        {
            var converter=new System.Windows.Input.KeyGestureConverter();var gesture=(System.Windows.Input.KeyGesture)converter.ConvertFromString(Backend.Prefs.Hotkey)!;
            uint mods=0x4000;var m=gesture.Modifiers;if(m.HasFlag(System.Windows.Input.ModifierKeys.Alt))mods|=1;if(m.HasFlag(System.Windows.Input.ModifierKeys.Control))mods|=2;if(m.HasFlag(System.Windows.Input.ModifierKeys.Shift))mods|=4;if(m.HasFlag(System.Windows.Input.ModifierKeys.Windows))mods|=8;
            if(!Native.RegisterHotKey(hotkeySource.Handle,1,mods,(uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(gesture.Key)))throw new IOException("快捷键已被占用。");
        }catch(Exception ex){UI.Error("全局快捷键未生效："+ex.Message);}
    }
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
    {if(msg==0x312){if(Backend.Prefs.HotkeyNewWindow)NewWindow();else ActivateRecent();handled=true;}return IntPtr.Zero;}
    void UpdateTray(){var state=Backend.TrayState;tray.Paused=state.Paused;taskbarIcons.Refresh();var prefix="Soma Finder · ";var progress=Backend.Progress.Length>35?Backend.Progress[^35..]:Backend.Progress;var tail=" · "+progress;var available=Math.Max(0,63-prefix.Length-tail.Length);var phase=state.Phase.Length>available?state.Phase[..available]:state.Phase;tray.Text=prefix+phase+tail;}
    internal TaskbarStatusIcons VerificationTaskbarIcons=>CloudSandbox.Active?taskbarIcons:throw new InvalidOperationException();
    internal StableTrayIcon VerificationTray=>CloudSandbox.Active?tray:throw new InvalidOperationException();
    internal ContextMenu? VerificationTrayMenu=>CloudSandbox.Active?currentTrayMenu:throw new InvalidOperationException();
    internal DialogLocation? VerificationTrayLocation=>CloudSandbox.Active?currentTrayLocation:throw new InvalidOperationException();
}
public static class UI
{
    public static void Error(string text){if(CloudSandbox.Active){CloudSandbox.Errors.Add(text);return;}MessageBox.Show(text,"Soma Finder",MessageBoxButton.OK,MessageBoxImage.Information);}
    public static Button Button(string text,Action action){var b=new Button{Content=text,Padding=new Thickness(14,7,14,7),Margin=new Thickness(4),MinHeight=34};b.Click+=(_,_)=>action();return b;}
    public static MenuItem Menu(string text,Action action){var m=new MenuItem{Header=text};m.Click+=(_,_)=>action();return m;}
    public static TextBlock Text(string text,double size=14)=>new(){Text=text,FontSize=size,Margin=new Thickness(4),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    public static TextBox Input(string text="",double width=double.NaN)=>new(){Text=text,Width=width,Margin=new Thickness(4),Padding=new Thickness(8,5,8,5),MinHeight=34,VerticalContentAlignment=VerticalAlignment.Center};
    public static CheckBox Check(string text,bool value,string? tip=null)=>new(){Content=text,IsChecked=value,Margin=new Thickness(4,8,4,8),ToolTip=tip};
    public static bool? Confirm(string text,string title,string cancel,string accept)=>Confirm(text,title,cancel,accept,null);
    internal static bool? Confirm(string text,string title,string cancel,string accept,DialogLocation? location)
    {
        bool? answer=null;var w=new Window{Title=title,Width=480,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ThemeMode=Application.Current?.ThemeMode??ThemeMode.System,Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(x=>x.IsActive),ShowInTaskbar=false};
        if(location!=null)DialogPlacement.Prepare(w,location);
        var panel=new StackPanel{Margin=new Thickness(20)};panel.Children.Add(Text(text));var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};
        w.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;w.Close();}};
        var no=Button(cancel,()=>{answer=false;w.Close();});no.IsDefault=true;var yes=Button(accept,()=>{answer=true;w.Close();});row.Children.Add(no);row.Children.Add(yes);panel.Children.Add(row);w.Content=panel;w.ShowDialog();return answer;
    }
    public static string? Prompt(string title,string initial="")
    {
        string? result=null;var w=new Window{Title=title,Width=500,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ThemeMode=Application.Current?.ThemeMode??ThemeMode.System,Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(x=>x.IsActive)};
        var p=new StackPanel{Margin=new Thickness(20)};var input=Input(initial);p.Children.Add(input);p.Children.Add(Button("确定",()=>{result=input.Text;w.Close();}));w.Content=p;w.ShowDialog();return result;
    }
    public static int SaveChoice(Window owner)
    {
        int choice=0;var w=new Window{Title="未保存的设置",Owner=owner,Width=480,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ThemeMode=Application.Current?.ThemeMode??ThemeMode.System,ShowInTaskbar=false};
        var panel=new StackPanel{Margin=new Thickness(20)};panel.Children.Add(Text("设置有未保存的修改。"));var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
        var edit=Button("继续编辑",()=>w.Close());edit.IsDefault=true;row.Children.Add(edit);row.Children.Add(Button("舍弃",()=>{choice=2;w.Close();}));row.Children.Add(Button("保存",()=>{choice=1;w.Close();}));panel.Children.Add(row);w.Content=panel;w.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;w.Close();}};w.ShowDialog();return choice;
    }
}
