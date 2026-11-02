// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace SomaFinder;

internal sealed class BackendSelectionWindow : Window
{
    readonly DataGrid list=new(){AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,SelectionUnit=DataGridSelectionUnit.FullRow,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,MinHeight=160};
    readonly TextBlock notice=UI.Text("");readonly Button accept;readonly Preferences prefs;readonly List<string> extra=new();
    public string? SelectedPath {get;private set;}
    public BackendSelectionWindow(Window? owner,Preferences prefs,BackendCatalog catalog)
    {
        this.prefs=prefs;Owner=owner;Title="选择 Soma 后端";Width=Math.Min(1000,SystemParameters.WorkArea.Width*.94);Height=Math.Min(580,SystemParameters.WorkArea.Height*.9);MinWidth=Math.Min(720,SystemParameters.WorkArea.Width);MinHeight=Math.Min(400,SystemParameters.WorkArea.Height);WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;ThemeMode=Application.Current?.ThemeMode??ThemeMode.System;FontSize=14;
        var panel=new Grid{Margin=new Thickness(20)};panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new());panel.RowDefinitions.Add(new(){Height=GridLength.Auto});panel.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=panel;
        panel.Children.Add(UI.Text("请选择 Soma Finder 使用的后端",22));
        var hint=UI.Text("检测到多份时请选择其中一份。建议日常只使用一份 Soma 后端，避免重复运行或不同版本共用索引造成冲突。确认所选版本可正常使用后，可自行清理其他副本。",13);Grid.SetRow(hint,1);panel.Children.Add(hint);
        var pathColumn=new DataGridTextColumn{Header="程序完整路径",Binding=new Binding(nameof(BackendCandidate.Path)),Width=new DataGridLength(1,DataGridLengthUnitType.Star),MinWidth=320};
        list.SizeChanged+=(_,_)=>pathColumn.Width=new DataGridLength(Math.Max(320,list.ActualWidth-190-155-32));
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(4,8,4,8)));pathColumn.ElementStyle=textStyle;list.Columns.Add(pathColumn);
        list.Columns.Add(new DataGridTextColumn{Header="版本",Binding=new Binding(nameof(BackendCandidate.Version)),Width=190,ElementStyle=textStyle});list.Columns.Add(new DataGridTextColumn{Header="状态",Binding=new Binding(nameof(BackendCandidate.State)),Width=155,ElementStyle=textStyle});
        AutomationProperties.SetAutomationId(list,"SomaFinder.BackendCandidates");Grid.SetRow(list,2);panel.Children.Add(list);Grid.SetRow(notice,3);panel.Children.Add(notice);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};Grid.SetRow(buttons,4);panel.Children.Add(buttons);
        buttons.Children.Add(UI.Button("打开所在文件夹",()=>{if(list.SelectedItem is BackendCandidate candidate)try{Process.Start(new ProcessStartInfo(System.IO.Path.GetDirectoryName(candidate.Path)!){UseShellExecute=true});}catch(Exception ex){notice.Text=ex.Message;}}));
        buttons.Children.Add(UI.Button("手动选择",async()=>
        {
            var picker=new Microsoft.Win32.OpenFileDialog{Title="选择已部署的 Soma 后端",Filter="Soma 后端|soma.exe;soma-windows-x64.exe|可执行文件|*.exe"};if(picker.ShowDialog(this)!=true)return;
            extra.Add(picker.FileName);await Refresh(picker.FileName);
        }));
        buttons.Children.Add(UI.Button("重新检测",async()=>await Refresh((list.SelectedItem as BackendCandidate)?.Path)));
        buttons.Children.Add(UI.Button("稍后设置",Close));
        accept=UI.Button("使用所选后端",()=>
        {
            if(list.SelectedItem is not BackendCandidate candidate||!candidate.IsSoma||!File.Exists(candidate.Path)){notice.Text="请选择可用的 Soma 后端。";return;}
            SelectedPath=candidate.Path;Close();
        });AutomationProperties.SetAutomationId(accept,"SomaFinder.UseBackend");buttons.Children.Add(accept);
        list.SelectionChanged+=(_,_)=>{accept.IsEnabled=list.SelectedItem is BackendCandidate c&&c.IsSoma;};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}};
        Apply(catalog,prefs.SomaExe);
    }
    void Apply(BackendCatalog catalog,string? preferred)
    {
        var items=catalog.Items.Select(c=>c with{Current=Native.SameFilePath(c.Path,prefs.SomaExe)}).ToList();list.ItemsSource=items;
        list.SelectedItem=items.FirstOrDefault(c=>preferred!=null&&Native.SameFilePath(c.Path,preferred))??items.FirstOrDefault(c=>c.IsSoma);
        notice.Text=(catalog.Warning.Length>0?catalog.Warning+"\n":"")+(catalog.Items.Count==0?"未找到后端，可手动选择已部署的程序。":"按程序路径统计；同一程序的多个工作区进程不算多份安装。")+"\n检测已保存位置、常见目录、PATH 与运行实例；其他位置可手动选择。\n选择后不会自动删除其他副本或结束其他进程。";
        accept.IsEnabled=list.SelectedItem is BackendCandidate c&&c.IsSoma;
    }
    async Task Refresh(string? preferred)
    {
        IsEnabled=false;notice.Text="正在查找后端和读取版本…";
        try{Apply(await BackendDiscovery.Scan(prefs,extra),preferred);}catch(Exception ex){notice.Text="检测失败："+ex.Message;}finally{IsEnabled=true;}
    }
}
