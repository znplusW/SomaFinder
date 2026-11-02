// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;

namespace SomaFinder;

internal static class BackendDiscovery
{
    public const string DownloadUrl="https://github.com/AwesomeDog/soma/releases/download/v0.10.7/soma-windows-x64.exe";
    public const string ReleasesUrl="https://github.com/AwesomeDog/soma/releases/tag/v0.10.7";
    public static IEnumerable<string> Candidates(string saved,string appDirectory,string home,string? searchPath,IEnumerable<string>? registered=null)
    {
        var values=new List<string>{saved,System.IO.Path.Combine(appDirectory,"soma.exe"),System.IO.Path.Combine(appDirectory,"..","soma.exe"),System.IO.Path.Combine(appDirectory,"Soma","soma.exe"),System.IO.Path.Combine(home,"Portable App","Soma","soma.exe"),System.IO.Path.Combine(home,"Portable Apps","Soma","soma.exe"),System.IO.Path.Combine(home,"Downloads","soma.exe"),System.IO.Path.Combine(home,"scoop","apps","soma","current","soma.exe")};
        if(registered!=null)values.AddRange(registered);
        foreach(var folder in (searchPath??"").Split(';',StringSplitOptions.RemoveEmptyEntries))
        {try{values.Add(System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')),"soma.exe"));}catch(ArgumentException){}}
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var value in values.SelectMany(value=>value.EndsWith("soma.exe",StringComparison.OrdinalIgnoreCase)?new[]{value,value[..^8]+"soma-windows-x64.exe"}:new[]{value}))
        {
            if(string.IsNullOrWhiteSpace(value))continue;string path;
            try{path=System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));}catch(Exception ex)when(ex is ArgumentException or NotSupportedException or System.Security.SecurityException){continue;}
            if(seen.Add(path))yield return path;
        }
    }
    public static string Find(string saved)=>Candidates(saved,AppContext.BaseDirectory,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetEnvironmentVariable("PATH"),Registered()).FirstOrDefault(File.Exists)??"";
    public static async Task<BackendCatalog> Scan(Preferences prefs,IEnumerable<string>? extra=null)
    {
        var paths=new[]{prefs.SomaExe}.Concat(prefs.KnownBackendPaths).Concat(extra??Array.Empty<string>()).Concat(Candidates(prefs.SomaExe,AppContext.BaseDirectory,Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),Environment.GetEnvironmentVariable("PATH"),Registered())).ToList();
        string warning="";List<Native.Proc> running=new();
        try{running=await Native.BackendProcesses(paths);paths.AddRange(running.Select(p=>p.File));}
        catch(Exception ex){warning="无法完整读取运行状态："+ex.Message;}
        return await Catalog(paths,running,warning);
    }
    internal static async Task<BackendCatalog> Catalog(IEnumerable<string> paths,IReadOnlyList<Native.Proc> running,string warning="")
    {
        var normalized=new List<string>();
        foreach(var value in paths)
        {
            if(string.IsNullOrWhiteSpace(value))continue;
            try{var path=System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));if(File.Exists(path)&&!normalized.Any(x=>Native.SameFilePath(x,path)))normalized.Add(path);}catch(ArgumentException){}catch(NotSupportedException){}
        }
        using var throttle=new SemaphoreSlim(4);
        var items=await Task.WhenAll(normalized.Select(async path=>{await throttle.WaitAsync();try{return new BackendCandidate(path,await Version(path),running.Count(p=>Native.SameFilePath(p.File,path)));}finally{throttle.Release();}}));
        return new(items.ToList(),warning);
    }
    public static async Task<string> Version(string path)
    {
        try
        {
            var start=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("--version");
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(6));}catch(TimeoutException){process.Kill(true);return "无法读取版本（超时）";}
            var text=((await output)+" "+(await error)).Trim();
            if(process.ExitCode!=0||!System.Text.RegularExpressions.Regex.IsMatch(text,@"(?i)\bsoma\s+\d+\.\d+\.\d+"))return "未识别为 Soma 后端";
            return text.Length>180?text[..180]:text;
        }
        catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or InvalidOperationException){return "无法读取版本";}
    }
    public static bool NeedsChoice(Preferences prefs,BackendCatalog catalog)
    {
        var valid=catalog.Valid.ToList();if(valid.Count==0)return false;
        bool current=File.Exists(prefs.SomaExe)&&valid.Any(c=>Native.SameFilePath(c.Path,prefs.SomaExe));
        // Do not silently replace a previously selected version when its file disappears.
        if(!current&&!string.IsNullOrWhiteSpace(prefs.SomaExe))return true;
        return valid.Count>1&&(!current||!Native.SameFilePath(prefs.SomaExe,prefs.ConfirmedSomaExe));
    }
    public static void Remember(Preferences prefs,string path,IEnumerable<string>? discovered=null)
    {
        prefs.SomaExe=System.IO.Path.GetFullPath(path);prefs.ConfirmedSomaExe=prefs.SomaExe;
        prefs.KnownBackendPaths=new[]{prefs.SomaExe}.Concat(discovered??Array.Empty<string>()).Concat(prefs.KnownBackendPaths).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
    }
    static IEnumerable<string> Registered()
    {
        var values=new List<string>();
        foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})
        {
            try{using var key=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\soma.exe");if(key?.GetValue(null) is string value)values.Add(value);}catch(System.Security.SecurityException){}
        }
        return values;
    }
    public static Window CreateMissingPrompt(Window? owner,Action select,Action later,Action<string>? open=null)
    {
        var window=new Window{Title="请先部署 Soma 后端",Width=560,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner,Owner=owner,ThemeMode=Application.Current?.ThemeMode??ThemeMode.System};
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(UI.Text("未找到 Soma 后端",22));
        panel.Children.Add(UI.Text("Soma Finder 需要 Soma 后端才能搜索和建立索引。请先下载 Windows x64 版本并完成部署，再选择 soma.exe。已有模型与索引可继续使用。"));
        var paragraph=new TextBlock{Margin=new Thickness(4,16,4,8),TextWrapping=TextWrapping.Wrap};
        var link=new Hyperlink(new Run("下载已适配的 Soma 0.10.7（Windows x64）")){NavigateUri=new Uri(DownloadUrl)};
        link.RequestNavigate+=(_,e)=>{try{if(open!=null)open(e.Uri.AbsoluteUri);else Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});e.Handled=true;}catch(Exception ex){UI.Error("无法打开下载链接："+ex.Message);}};
        paragraph.Inlines.Add(link);panel.Children.Add(paragraph);
        var url=new TextBox{Text=DownloadUrl,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Margin=new Thickness(4),Background=System.Windows.Media.Brushes.Transparent};panel.Children.Add(url);
        panel.Children.Add(UI.Button("重新查找 Soma",()=>{var found=Find("");if(found.Length==0){UI.Error("仍未找到 Soma。请先完成部署，或手动选择 soma.exe。");return;}window.Tag=found;window.Close();}));
        panel.Children.Add(UI.Button("选择已部署的 soma.exe",()=>{select();window.Close();}));
        panel.Children.Add(UI.Button("稍后配置",()=>{later();window.Close();}));
        window.Content=panel;return window;
    }
}

internal sealed record BackendCandidate(string Path,string Version,int ProcessCount)
{
    public bool Current {get;init;}
    public bool IsSoma=>System.Text.RegularExpressions.Regex.IsMatch(Version,@"(?i)\bsoma\s+\d+\.\d+\.\d+");
    public string State=>(Current?"当前使用 · ":"")+(ProcessCount>0?"运行中（"+ProcessCount+" 个进程）":"未运行");
}
internal sealed record BackendCatalog(List<BackendCandidate> Items,string Warning="")
{
    public IEnumerable<BackendCandidate> Valid=>Items.Where(c=>c.IsSoma);
}
