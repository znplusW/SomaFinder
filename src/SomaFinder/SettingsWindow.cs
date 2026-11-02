// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace SomaFinder;
public sealed class SettingsDraft
{
    public List<Workspace> Workspaces {get;set;}=new();
    public string Exe {get;set;}="";
    public string Interval {get;set;}="60";
    public string Hotkey {get;set;}="Alt+E";
    public string DefaultSearchBreadth {get;set;}=Preferences.InitialSearchBreadth.ToString();
    public DetailLayout DefaultDetailLayout {get;set;}=DetailLayout.SummaryOnly;
    public bool Startup {get;set;}=true;
    public bool Periodic {get;set;}
    public bool Battery {get;set;}=true;
    public bool BatteryStop {get;set;}=true;
    public string Theme {get;set;}="System";
    public bool Comfortable {get;set;}
    public string MatchStyle {get;set;}="Bold";
    public bool Memory {get;set;}=true;
    public bool Priority {get;set;}=true;
    public bool ExitLast {get;set;}
    public bool NewWindow {get;set;}
    public bool Resume {get;set;}=true;
    public Dictionary<string,VolumeBinding> NewBindings {get;set;}=new();
    public Dictionary<string,string> VolumeDisks {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,string> AutoGroups {get;set;}=new(StringComparer.OrdinalIgnoreCase);
}
public sealed class SettingsWindow : Window
{
    readonly FinderApplication app;
    readonly ListBox workspaces=new(){MinHeight=80},roots=new(){MinHeight=140};
    readonly ComboBox syncWorkspace=new(){MinWidth=200,Margin=new Thickness(4)},maintenanceWorkspace=new(){MinWidth=200,Margin=new Thickness(4)};
    readonly ComboBox defaultDetailLayout=new(){MinWidth=220,Margin=new Thickness(4)};
    readonly TextBox defaultSearchBreadth=UI.Input(Preferences.InitialSearchBreadth.ToString(),110);
    readonly TextBox path=UI.Input(),includes=UI.Input(),excludes=UI.Input(),description=UI.Input(),exe=UI.Input(),interval=UI.Input("60",100),hotkey=UI.Input("Alt+E",180),pauseHours=UI.Input("2",70);
    readonly CheckBox defaultSearch=UI.Check("默认选择此根目录",true),ignoreFiles=UI.Check("遵守目录的忽略文件规则",true);
    readonly Button cancelSearch,rebuild,pauseToggle;
    readonly TextBlock pauseStatus=UI.Text(""),notice=UI.Text(""),rootLabel=UI.Text(""),rebuildRange=UI.Text("");
    readonly CheckBox startup,periodic,battery,batteryStop,memory,priority,exitLast,newWindow,resume,comfortable;
    readonly ComboBox matchStyle=new(){MinWidth=180,Margin=new Thickness(4)};
    readonly ComboBox themeChoice=new(){MinWidth=180,Margin=new Thickness(4)};
    SettingsDraft draft;Root? current;bool switching,saved,saving;string baseline="";string draftPath;
    public SettingsWindow(FinderApplication application)
    {
        app=application;Title="Soma Finder 设置";Width=Math.Min(980,SystemParameters.WorkArea.Width*0.94);Height=Math.Min(790,SystemParameters.WorkArea.Height*0.92);MinWidth=Math.Min(800,SystemParameters.WorkArea.Width);MinHeight=Math.Min(600,SystemParameters.WorkArea.Height);WindowStartupLocation=WindowStartupLocation.CenterScreen;ThemeMode=SearchWindow.ParseTheme(app.Backend.Prefs.SearchLayout.Theme);FontSize=14;FontFamily=new FontFamily("Segoe UI Variable, Microsoft YaHei UI");
        draft=Capture();draftPath=System.IO.Path.Combine(app.Store.DataDirectory,"settings-draft.json");
        baseline=JsonSerializer.Serialize(draft);
        if(File.Exists(draftPath))
        {try{draft=JsonSerializer.Deserialize<SettingsDraft>(File.ReadAllText(draftPath))??draft;notice.Text="有未保存的修改（草稿尚未应用到 Soma）";}catch(Exception ex){notice.Text="草稿读取失败："+ex.Message;}}
        exe.Text=draft.Exe;interval.Text=draft.Interval;hotkey.Text=draft.Hotkey;
        defaultSearchBreadth.Text=draft.DefaultSearchBreadth;
        defaultDetailLayout.ItemsSource=new[]{"只显示搜索摘要","只显示内容预览","两者都显示"};
        defaultDetailLayout.SelectedIndex=Enum.IsDefined(draft.DefaultDetailLayout)?(int)draft.DefaultDetailLayout:-1;
        defaultSearchBreadth.ToolTip="以后新建的搜索窗口使用此值，每个窗口仍可单独调整。按所选目录文件数量合计分配；范围较多时会自动提高最低额度。";
        System.Windows.Automation.AutomationProperties.SetName(defaultSearchBreadth,"默认搜索广度");
        System.Windows.Automation.AutomationProperties.SetName(defaultDetailLayout,"新建搜索窗口默认显示");
        startup=UI.Check("启动时更新索引",draft.Startup);periodic=UI.Check("定时更新索引",draft.Periodic);
        battery=UI.Check("使用电池时不自动同步",draft.Battery,"电池供电时不启动新的自动同步。默认让当前任务完成；可开启下方选项停止正在进行的自动同步。手动同步不受电池限制；定时暂停仍优先。");
        batteryStop=UI.Check("切换到电池时，也停止正在进行的自动同步",draft.BatteryStop,"已在电池供电时开启也生效。独立索引立即停止，搜索保持可用；共用服务等待当前搜索完成后切换。保留已提交索引，插电后按同步设置更新。手动索引不会自动中断。");
        batteryStop.Margin=new Thickness(28,4,4,8);batteryStop.IsEnabled=draft.Battery;
        battery.Click+=(_,_)=>batteryStop.IsEnabled=battery.IsChecked==true;
        matchStyle.ItemsSource=new[]{"加粗","黄色高亮"};matchStyle.SelectedIndex=draft.MatchStyle=="Yellow"?1:0;
        System.Windows.Automation.AutomationProperties.SetName(matchStyle,"匹配词样式");
        comfortable=UI.Check("舒适显示密度（关闭时使用紧凑密度）",draft.Comfortable);
        themeChoice.ItemsSource=new[]{"跟随系统","浅色","深色"};themeChoice.SelectedIndex=draft.Theme=="Light"?1:draft.Theme=="Dark"?2:0;
        memory=UI.Check("内存不足时不启动新的自动同步",draft.Memory,"可用内存不足时暂缓新的自动同步；正在运行的索引不会暂停，仍可手动同步。定时暂停期间手动同步也不可用。");
        priority=UI.Check("降低索引任务优先级",draft.Priority,"降低索引任务的 CPU 优先级，可能延长索引时间。不限制 GPU 或内存占用。");
        exitLast=UI.Check("关闭最后一个搜索窗口时，自动退出程序",draft.ExitLast);newWindow=UI.Check("全局快捷键新建搜索窗口（关闭时唤起最近窗口）",draft.NewWindow);resume=UI.Check("唤醒后补做错过的自动同步",draft.Resume);resume.ToolTip="电脑睡眠期间错过的自动同步，唤醒后补做一次；仍遵守暂停、电源和内存设置。";
        var frame=new DockPanel{Margin=new Thickness(16)};Content=frame;
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        bottom.Children.Add(UI.Button("取消",()=>{if(File.Exists(draftPath))File.Delete(draftPath);saved=true;Close();}));bottom.Children.Add(UI.Button("保存设置",async()=>{if(await Save()){saved=true;Close();}}));DockPanel.SetDock(bottom,Dock.Bottom);frame.Children.Add(bottom);DockPanel.SetDock(notice,Dock.Top);frame.Children.Add(notice);
        var tabs=new TabControl();frame.Children.Add(tabs);
        var range=new Grid{Margin=new Thickness(12)};range.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(240)});range.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new StackPanel();left.Children.Add(UI.Text("索引目录",20));left.Children.Add(roots);left.Children.Add(UI.Button("添加根目录",AddRoot));left.Children.Add(UI.Button("移除所选根目录",RemoveRoot));range.Children.Add(left);
        var editor=new StackPanel{Margin=new Thickness(16,0,0,0)};Grid.SetColumn(editor,1);range.Children.Add(editor);editor.Children.Add(rootLabel);editor.Children.Add(UI.Text("真实目录"));path.IsReadOnly=true;editor.Children.Add(path);editor.Children.Add(UI.Button("重新定位目录",Relocate));editor.Children.Add(UI.Text("包含规则（每行一个）"));includes.AcceptsReturn=true;includes.Height=90;includes.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;editor.Children.Add(includes);editor.Children.Add(UI.Text("排除规则（每行一个；选择目录可生成规则）"));excludes.AcceptsReturn=true;excludes.Height=120;excludes.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;editor.Children.Add(excludes);editor.Children.Add(UI.Button("选择排除目录",ExcludeFolder));editor.Children.Add(UI.Text("搜索页的目录勾选只过滤搜索结果，不改变这里的长期索引范围。",12));
        editor.Children.Add(defaultSearch);editor.Children.Add(ignoreFiles);editor.Children.Add(UI.Text("根目录说明"));description.AcceptsReturn=true;description.Height=80;editor.Children.Add(description);
        foreach(var input in new[]{includes,excludes,description})input.VerticalContentAlignment=VerticalAlignment.Top;
        tabs.Items.Add(new TabItem{Header="索引范围",Content=new ScrollViewer{Content=range,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var sync=new StackPanel{Margin=new Thickness(16)};sync.Children.Add(startup);sync.Children.Add(periodic);sync.Children.Add(Row(UI.Text("同步间隔（分钟）"),interval));sync.Children.Add(resume);sync.Children.Add(battery);sync.Children.Add(batteryStop);sync.Children.Add(memory);sync.Children.Add(priority);
        sync.Children.Add(new Separator{Margin=new Thickness(0,12,0,12)});sync.Children.Add(UI.Text("临时暂停全部索引",20));
        pauseToggle=UI.Button("暂停索引",async()=>{try{if(app.Backend.CanResume)app.Backend.Continue();else{if(!double.TryParse(pauseHours.Text,out var hours))throw new ArgumentException("请输入有效小时数。");await app.Backend.Pause(hours);}}catch(Exception ex){UI.Error(ex.Message);}UpdateStatus();});
        pauseToggle.ToolTip="暂停全部目录的索引，搜索仍可使用；恢复后遵守电源和内存策略。";
        sync.Children.Add(Row(pauseHours,UI.Text("小时"),pauseToggle));sync.Children.Add(pauseStatus);
        cancelSearch=UI.Button("取消搜索并立即暂停",()=>app.Backend.CancelSearchAndPause());cancelSearch.Visibility=Visibility.Collapsed;sync.Children.Add(cancelSearch);
        sync.Children.Add(new Separator{Margin=new Thickness(0,18,0,14)});sync.Children.Add(UI.Text("手动更新索引",20));
        sync.Children.Add(Row(UI.Text("索引范围"),syncWorkspace,UI.Button("立即更新",async()=>{try{Pull();if(DirectoryChanges().Count>0){notice.Text="目录范围有未保存修改，请先保存设置，再手动更新索引。";return;}if(syncWorkspace.SelectedItem is Workspace ws)await app.Backend.RunSync(app.Backend.Workspaces.First(w=>w.Name==ws.Name),true);}catch(Exception ex){UI.Error(ex.Message);}})));
        tabs.Items.Add(new TabItem{Header="同步与资源",Content=new ScrollViewer{Content=sync,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var general=new StackPanel{Margin=new Thickness(16)};general.Children.Add(UI.Text("Soma 程序位置",18));general.Children.Add(exe);general.Children.Add(UI.Button("选择／重新检测 Soma 后端",async()=>
        {
            try{var catalog=await BackendDiscovery.Scan(app.Backend.Prefs,new[]{exe.Text});var selector=new BackendSelectionWindow(this,app.Backend.Prefs,catalog);selector.ShowDialog();if(selector.SelectedPath is string found)exe.Text=found;await app.Backend.RecheckInstances();notice.Text=app.Backend.InstanceConflict.Length>0?app.Backend.InstanceConflict:"检测完成；后端选择将在保存后生效。";}catch(Exception ex){UI.Error(ex.Message);}
        }));
        general.Children.Add(UI.Text("同一程序可能为不同索引范围运行多个进程，属于正常情况。建议日常只使用一份后端；保留备份副本不影响使用。",12));general.Children.Add(exitLast);general.Children.Add(UI.Text("全局快捷键"));general.Children.Add(hotkey);general.Children.Add(newWindow);
        general.Children.Add(new Separator{Margin=new Thickness(0,12,0,12)});
        general.Children.Add(UI.Text("新建搜索窗口",20));
        general.Children.Add(Row(UI.Text("默认搜索广度"),defaultSearchBreadth));
        general.Children.Add(Row(UI.Text("新建搜索窗口默认显示"),defaultDetailLayout));
        general.Children.Add(UI.Text("仅影响以后新建的搜索窗口；已打开窗口保留各自的搜索广度、面板顺序和显示状态。",12));
        general.Children.Add(Row(UI.Text("外观主题"),themeChoice));general.Children.Add(comfortable);general.Children.Add(Row(UI.Text("匹配词样式"),matchStyle));general.Children.Add(UI.Text("关于与版本",20));general.Children.Add(UI.Text("Soma Finder 0.6.5 · Windows 11 x64 · .NET 10 Fluent\n已测试适配：Soma 0.10.7（重建已在云端临时样例验证）\n当前连接版本："+app.Backend.ConnectedVersion+"\n绿色版设置存放在程序旁 data；现有 Soma 配置、模型和索引继续使用原位置。",14));
        general.Children.Add(UI.Text("Copyright © 2026 znplusW\n开源许可：GPLv3（可按该许可再分发，不附带担保）。\n另提供单独协商的商业许可；闭源分发受约束的衍生作品前请取得授权。",12));
        general.Children.Add(UI.Button("查看许可与商业授权 / Licensing",()=>
        {
            try{Process.Start(new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory,"Documents","Licensing.txt")){UseShellExecute=true});}
            catch(Exception ex){UI.Error("无法打开许可说明："+ex.Message);}
        }));
        tabs.Items.Add(new TabItem{Header="窗口与快捷键",Content=new ScrollViewer{Content=general,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var backendPage=new StackPanel{Margin=new Thickness(16)};
        var aboutStart=general.Children.IndexOf(exe)-1;
        for(int i=0;i<4;i++){var element=general.Children[aboutStart];general.Children.RemoveAt(aboutStart);backendPage.Children.Add(element);}
        var aboutHeader=general.Children.OfType<TextBlock>().First(t=>t.Text=="关于与版本");int aboutIndex=general.Children.IndexOf(aboutHeader);
        while(general.Children.Count>aboutIndex){var element=general.Children[aboutIndex];general.Children.RemoveAt(aboutIndex);backendPage.Children.Add(element);}
        tabs.Items.Add(new TabItem{Header="后端与关于",Content=new ScrollViewer{Content=backendPage,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        var maintenance=new StackPanel{Margin=new Thickness(16)};maintenance.Children.Add(UI.Text("重建所选范围索引",20));maintenance.Children.Add(UI.Text("作用于所选范围中列出的全部目录。清空后重新扫描、提取和生成向量，耗时较长。"));maintenance.Children.Add(rebuildRange);
        rebuild=UI.Button("重建所选范围索引",async()=>
        {
            if(workspaces.SelectedItem is not Workspace ws)return;
            if((CloudSandbox.Active||Environment.GetEnvironmentVariable("SOMAFINDER_TEST")=="1")&&!CloudSandbox.AllowsRebuild(app.Store,ws)){UI.Error("当前测试模式禁止清空或重建非云端临时样例索引。");return;}
            if(app.Backend.Busy||app.Backend.Paused){UI.Error("索引正在执行或已暂停，无法重建。");return;}
            if(UI.Confirm("将清空并重建所选索引范围："+ws+"\n\n涉及真实目录：\n"+string.Join("\n",ws.Roots.Select(r=>r.Path))+"\n\n原文件不会删除。请确认已理解索引将重新建立。","重建所选范围索引","取消","确认重建")!=true)return;
            try{await app.Backend.RunSync(app.Backend.Workspaces.First(w=>w.Name==ws.Name),true,true);}catch(Exception ex){UI.Error(ex.Message);}
        });rebuild.Foreground=Brushes.IndianRed;maintenance.Children.Add(rebuild);tabs.Items.Add(new TabItem{Header="索引维护",Content=maintenance});
        System.Windows.Automation.AutomationProperties.SetAutomationId(rebuild,"SomaFinder.RebuildWorkspace");
        workspaces.SelectionChanged+=(_,_)=>{rebuildRange.Text=workspaces.SelectedItem is Workspace ws?"涉及目录：\n"+string.Join("\n",ws.Roots.Select(r=>r.Path)):"请选择索引范围";};
        roots.SelectionChanged+=(_,_)=>{CommitRoot();current=roots.SelectedItem as Root;if(current!=null)workspaces.SelectedItem=draft.Workspaces.First(w=>w.Roots.Contains(current));ShowRoot();};
        workspaces.ItemsSource=draft.Workspaces;workspaces.SelectedIndex=0;RefreshRoots();UpdateStatus();
        foreach(var selector in new[]{syncWorkspace,maintenanceWorkspace}){selector.ItemsSource=draft.Workspaces;selector.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,new System.Windows.Data.Binding("SelectedItem"){Source=workspaces,Mode=System.Windows.Data.BindingMode.TwoWay});}
        maintenance.Children.Insert(1,Row(UI.Text("目标范围"),maintenanceWorkspace));
        Closing+=async(_,e)=>
        {
            if(saved||app.Backend.Exiting)return;
            if(saving){e.Cancel=true;return;}
            Pull();if(JsonSerializer.Serialize(draft)==baseline)return;
            e.Cancel=true;var decision=UI.SaveChoice(this);
            if(decision==1){if(await Save()){saved=true;Close();}}
            if(decision==2){if(File.Exists(draftPath))File.Delete(draftPath);saved=true;Close();}
        };
    }
    static StackPanel Row(params UIElement[] items){var p=new StackPanel{Orientation=Orientation.Horizontal};foreach(var i in items)p.Children.Add(i);return p;}
    SettingsDraft Capture()
    {
        var p=app.Backend.Prefs;return new SettingsDraft{Workspaces=JsonSerializer.Deserialize<List<Workspace>>(JsonSerializer.Serialize(app.Backend.Workspaces))!,VolumeDisks=new(p.VolumeDisks,StringComparer.OrdinalIgnoreCase),AutoGroups=new(p.AutoGroups,StringComparer.OrdinalIgnoreCase),Exe=p.SomaExe,Interval=p.IntervalMinutes.ToString(),Hotkey=p.Hotkey,DefaultSearchBreadth=p.DefaultSearchBreadth.ToString(),DefaultDetailLayout=p.DefaultDetailLayout,Startup=p.StartupSync,Periodic=p.PeriodicSync,Battery=p.BatteryBlocksAuto,BatteryStop=p.BatteryStopsAuto,Theme=p.SearchLayout.Theme,Comfortable=p.SearchLayout.Comfortable,MatchStyle=p.SearchLayout.MatchStyle,Memory=p.LowMemoryBlocksAuto,Priority=p.LowerPriority,ExitLast=p.ExitOnLastWindow,NewWindow=p.HotkeyNewWindow,Resume=p.ResumeMissed};
    }
    void ShowRoot(){switching=true;path.Text=current?.Path??"";includes.Text=string.Join(Environment.NewLine,current?.Include??new());excludes.Text=string.Join(Environment.NewLine,current?.Exclude??new());description.Text=current?.Description??"";defaultSearch.IsChecked=current?.DefaultSearch??true;ignoreFiles.IsChecked=current?.IgnoreFiles??true;rootLabel.Text=current==null?"选择一个目录":"目录设置";switching=false;}
    void CommitRoot(){if(current==null||switching)return;current.Include=Lines(includes.Text);current.Exclude=Lines(excludes.Text);current.Description=description.Text;current.DefaultSearch=defaultSearch.IsChecked==true;current.IgnoreFiles=ignoreFiles.IsChecked==true;}
    static List<string> Lines(string s)=>s.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();
    void Pull(){CommitRoot();draft.Exe=exe.Text;draft.Interval=interval.Text;draft.Hotkey=hotkey.Text;draft.DefaultSearchBreadth=defaultSearchBreadth.Text;draft.DefaultDetailLayout=(DetailLayout)defaultDetailLayout.SelectedIndex;draft.Startup=startup.IsChecked==true;draft.Periodic=periodic.IsChecked==true;draft.Battery=battery.IsChecked==true;draft.BatteryStop=batteryStop.IsChecked==true;draft.Theme=themeChoice.SelectedIndex==1?"Light":themeChoice.SelectedIndex==2?"Dark":"System";draft.Comfortable=comfortable.IsChecked==true;draft.MatchStyle=matchStyle.SelectedIndex==1?"Yellow":"Bold";draft.Memory=memory.IsChecked==true;draft.Priority=priority.IsChecked==true;draft.ExitLast=exitLast.IsChecked==true;draft.NewWindow=newWindow.IsChecked==true;draft.Resume=resume.IsChecked==true;}
    public void PersistDraft(){Pull();if(JsonSerializer.Serialize(draft)!=baseline)Store.Atomic(draftPath,JsonSerializer.Serialize(draft,new JsonSerializerOptions{WriteIndented=true}));}
    string? PickFolder(){using var dialog=new Forms.FolderBrowserDialog{Description="选择资料目录",UseDescriptionForTitle=true};return dialog.ShowDialog()==Forms.DialogResult.OK?dialog.SelectedPath:null;}
    bool ValidateOverlap(string newPath,Root? excluded=null)
    {
        foreach(var ws in draft.Workspaces)foreach(var r in ws.Roots.Where(r=>r!=excluded))
        {
            if(!Rules.Contains(r.Path,newPath)&&!Rules.Contains(newPath,r.Path))continue;
            var covers=Rules.Contains(r.Path,newPath)?r:null;
            if(covers!=null&&covers.Exclude.Any(x=>x.Replace('\\','/').TrimEnd('*','/')==System.IO.Path.GetRelativePath(r.Path,newPath).Replace('\\','/')))continue;
            UI.Error("目录与已有索引范围重叠："+r.Path+"\n父目录已包含子目录时通常无需重复添加。请先调整范围或排除规则。");return false;
        }return true;
    }
    async void AddRoot()
    {
        var folder=PickFolder();if(folder==null)return;CommitRoot();if(!ValidateOverlap(folder))return;
        try
        {
            var identity=await Task.Run(()=>Native.Identify(folder));if(identity==null){UI.Error("无法识别目录所在卷。");return;}
            var key=await Task.Run(()=>RootGrouping.DiskKey(identity));draft.VolumeDisks[identity.Volume]=key;
            foreach(var prior in draft.Workspaces.SelectMany(w=>w.Roots))
            {
                var binding=draft.NewBindings.GetValueOrDefault(prior.Path)??app.Backend.Prefs.Volumes.GetValueOrDefault(prior.Path);
                if(binding==null){binding=await Task.Run(()=>Native.Identify(prior.Path));if(binding!=null)draft.NewBindings[prior.Path]=binding;}
                if(binding!=null&&!draft.VolumeDisks.ContainsKey(binding.Volume)&&Directory.Exists(prior.Path))draft.VolumeDisks[binding.Volume]=await Task.Run(()=>RootGrouping.DiskKey(binding));
            }
            string? Key(Root root){var binding=draft.NewBindings.GetValueOrDefault(root.Path)??app.Backend.Prefs.Volumes.GetValueOrDefault(root.Path);return binding!=null?draft.VolumeDisks.GetValueOrDefault(binding.Volume):null;}
            var ws=RootGrouping.Choose(draft.Workspaces,draft.VolumeDisks,draft.AutoGroups,Key,key,app.Store.ConfigDirectory);
            var root=new Root{Name="root-"+Guid.NewGuid().ToString("N")[..8],Path=folder};ws.Roots.Add(root);draft.NewBindings[folder]=identity;
            workspaces.Items.Refresh();syncWorkspace.Items.Refresh();maintenanceWorkspace.Items.Refresh();workspaces.SelectedItem=ws;RefreshRoots();roots.SelectedItem=root;
        }
        catch(Exception ex){UI.Error("添加目录失败："+ex.Message);}
    }
    readonly Dictionary<Root,TextBlock> progressLabels=new();
    void RefreshRoots()
    {
        var selected=current;roots.ItemsSource=draft.Workspaces.SelectMany(w=>w.Roots).ToList();
        if(roots.ItemTemplate==null)
        {
            var panel=new FrameworkElementFactory(typeof(StackPanel));var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Path"));label.SetBinding(ToolTipProperty,new System.Windows.Data.Binding("Path"));label.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);panel.AppendChild(label);
            var small=new FrameworkElementFactory(typeof(TextBlock));small.SetValue(TextBlock.FontSizeProperty,11d);small.SetValue(FrameworkElement.MarginProperty,new Thickness(0,4,0,8));small.SetValue(OpacityProperty,.75);small.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);small.SetValue(ToolTipProperty,"关键词失败表示内容提取失败；语义按当前已生成分块统计，后端未提供累计语义失败数。离线时保留上次统计。");
            small.AddHandler(LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is TextBlock text&&text.DataContext is Root root){progressLabels[root]=text;UpdateRootProgress();}}));panel.AppendChild(small);roots.ItemTemplate=new DataTemplate{VisualTree=panel};
        }
        roots.SelectedItem=selected;if(roots.SelectedItem==null&&roots.Items.Count>0)roots.SelectedIndex=0;
    }
    void UpdateRootProgress()
    {
        foreach(var pair in progressLabels.ToArray())
        {
            var ws=draft.Workspaces.FirstOrDefault(w=>w.Roots.Contains(pair.Key));if(ws==null){progressLabels.Remove(pair.Key);continue;}
            pair.Value.Text=RootGrouping.ProgressText(app.Backend.RootProgress(ws.Name,pair.Key.Name))+(Directory.Exists(pair.Key.Path)?"":"\n离线 · 保留上次统计");
        }
    }
    void RemoveRoot()
    {
        if(current==null||workspaces.SelectedItem is not Workspace ws)return;
        if(UI.Confirm("移除根目录："+current.Path+"\n只保存时旧索引可能仍存在，下次成功同步后清理；原文件不会删除。","移除根目录","取消","移除")!=true)return;
        foreach(var owner in draft.Workspaces)owner.Roots.RemoveAll(r=>Rules.Normalize(r.Path).Equals(Rules.Normalize(current.Path),StringComparison.OrdinalIgnoreCase));current=null;RefreshRoots();ShowRoot();
    }
    void Relocate()
    {
        if(current==null)return;var folder=PickFolder();if(folder==null||!ValidateOverlap(folder,current))return;
        if(UI.Confirm("旧路径："+current.Path+"\n新路径："+folder+"\n请确认这是原资料目录的新位置，原规则和根目录标识将保留。","重新定位目录","取消","更新路径")!=true)return;
        var identity=Native.Identify(folder);if(identity==null){UI.Error("无法确认新目录的卷身份。");return;}current.Path=folder;draft.NewBindings[folder]=identity;ShowRoot();roots.Items.Refresh();
    }
    void ExcludeFolder()
    {
        if(current==null)return;var folder=PickFolder();if(folder==null)return;
        if(!Rules.Contains(current.Path,folder)||Rules.Normalize(current.Path)==Rules.Normalize(folder)){UI.Error("请选择当前根目录之下的子目录。");return;}
        excludes.Text+=(excludes.Text.Length>0?Environment.NewLine:"")+System.IO.Path.GetRelativePath(current.Path,folder).Replace('\\','/')+"/**";
    }
    List<Workspace> DirectoryChanges()=>draft.Workspaces.Where(w=>!app.Backend.Workspaces.Any(old=>old.Name==w.Name&&JsonSerializer.Serialize(old.Roots)==JsonSerializer.Serialize(w.Roots))).ToList();
    async Task<bool> Save()
    {
        if(saving)return false;Pull();
        try
        {
            var changed=DirectoryChanges();
            if(!File.Exists(draft.Exe))throw new IOException("请选择有效的 soma.exe。");
            draft.Exe=System.IO.Path.GetFullPath(draft.Exe);app.Backend.ValidateExecutableChange(draft.Exe);
            if(!double.TryParse(draft.Interval,out var minutes)||!double.IsFinite(minutes)||minutes<1||minutes>525600)throw new ArgumentException("同步间隔请输入 1–525600 分钟。");
            if(!int.TryParse(draft.DefaultSearchBreadth,out var breadth)||breadth<1||breadth>100000)throw new ArgumentException("默认搜索广度请输入 1–100000 的整数。");
            if(!Enum.IsDefined(draft.DefaultDetailLayout))throw new ArgumentException("请选择有效的新窗口默认显示方式。");
            _=new System.Windows.Input.KeyGestureConverter().ConvertFromString(draft.Hotkey)??throw new ArgumentException("快捷键格式无效。");
            foreach(var ws in changed)if(Store.Digest(ws.ConfigPath)!=ws.Hash)throw new IOException("索引范围配置已在外部变化，请重新打开设置。");
            var p=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(app.Backend.Prefs))!;
            p.VolumeDisks=new(draft.VolumeDisks,StringComparer.OrdinalIgnoreCase);p.AutoGroups=new(draft.AutoGroups,StringComparer.OrdinalIgnoreCase);BackendDiscovery.Remember(p,draft.Exe);p.IntervalMinutes=minutes;p.Hotkey=draft.Hotkey;p.DefaultSearchBreadth=breadth;p.DefaultDetailLayout=draft.DefaultDetailLayout;p.StartupSync=draft.Startup;p.PeriodicSync=draft.Periodic;p.BatteryBlocksAuto=draft.Battery;p.BatteryStopsAuto=draft.BatteryStop;p.SearchLayout.Theme=draft.Theme;p.SearchLayout.Comfortable=draft.Comfortable;p.SearchLayout.MatchStyle=draft.MatchStyle;p.LowMemoryBlocksAuto=draft.Memory;p.LowerPriority=draft.Priority;p.ExitOnLastWindow=draft.ExitLast;p.HotkeyNewWindow=draft.NewWindow;p.ResumeMissed=draft.Resume;
            foreach(var pair in draft.NewBindings)p.Volumes[pair.Key]=pair.Value;
            saving=true;IsEnabled=false;notice.Text="正在保存设置…";
            await app.Backend.SaveConfiguration(changed,p);
            app.ApplyAppearance();app.Backend.ApplyPriority();app.ConfigChanged(changed.Count>0);baseline=JsonSerializer.Serialize(draft);if(File.Exists(draftPath))File.Delete(draftPath);
            notice.Text="设置已保存";if(!app.Backend.IsInitialized)await app.Backend.Initialize(false);_=RefreshPolicy();return true;
        }
        catch(Exception ex){PersistDraft();notice.Text=ex.Message;UI.Error(ex.Message);return false;}
        finally{saving=false;IsEnabled=true;}
    }
    async Task RefreshPolicy(){try{await app.Backend.Pulse();}catch(Exception ex){notice.Text="设置已保存；同步策略应用失败："+ex.Message;}}
    public void UpdateStatus(){UpdateRootProgress();pauseToggle.Content=app.Backend.CanResume?"恢复索引":"暂停索引";pauseHours.IsEnabled=!app.Backend.CanResume;pauseStatus.Text=app.Backend.CanResume?app.Backend.IndexPolicyReason:"";cancelSearch.Visibility=app.Backend.WaitingForSearch?Visibility.Visible:Visibility.Collapsed;rebuild.IsEnabled=!app.Backend.Busy&&!app.Backend.Paused&&!app.Backend.ChangingConfiguration&&app.Backend.InstanceConflict.Length==0;}
    internal void SelectCloudWorkspace(string name){if(!CloudSandbox.Active)throw new InvalidOperationException();workspaces.SelectedItem=draft.Workspaces.Single(w=>w.Name==name);}
}
