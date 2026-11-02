// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

public enum DetailLayout { SummaryOnly, PreviewOnly, Both }

public sealed class Preferences
{
    public const int InitialSearchBreadth=50;
    public string SomaExe { get; set; } = "";
    public string ConfirmedSomaExe { get; set; } = "";
    public List<string> KnownBackendPaths { get; set; } = new();
    public bool StartupSync { get; set; } = true;
    public bool PeriodicSync { get; set; }
    public double IntervalMinutes { get; set; } = 60;
    public bool BatteryBlocksAuto { get; set; } = true;
    public bool BatteryStopsAuto { get; set; } = true;
    public SearchLayout SearchLayout { get; set; } = new();
    public Dictionary<string,string> VolumeDisks {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string,string> AutoGroups {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    public bool LowMemoryBlocksAuto { get; set; } = true;
    public bool LowerPriority { get; set; } = true;
    public bool ExitOnLastWindow { get; set; }
    public bool HotkeyNewWindow { get; set; }
    public string Hotkey { get; set; } = "Alt+E";
    public int DefaultSearchBreadth { get; set; } = InitialSearchBreadth;
    public DetailLayout DefaultDetailLayout { get; set; } = DetailLayout.SummaryOnly;
    public bool ResumeMissed { get; set; } = true;
    public DateTimeOffset? PauseUntil { get; set; }
    public List<string> LastScope { get; set; } = new();
    public Dictionary<string,bool> LastSelection { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTimeOffset> LastFinished { get; set; } = new();
    public Dictionary<string, VolumeBinding> Volumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsPaused(DateTimeOffset now) => PauseUntil > now;
}
public sealed class SearchLayout
{
    public double ThumbnailWidth {get;set;}=200;
    public double TreeWidth {get;set;}=190;
    public double SummaryWidth {get;set;}=270;
    public double PreviewWidth {get;set;}=300;
    public bool Comfortable {get;set;}
    public string MatchStyle {get;set;}="Bold";
    public string Theme {get;set;}="System";
    public List<ResultColumn> Columns {get;set;}=new();
}
public sealed class ResultColumn
{
    public string Key {get;set;}="";
    public double Width {get;set;}
    public int Order {get;set;}
    public bool Visible {get;set;}=true;
}
public static class Selection
{
    public static Dictionary<string,bool> RestrictToRoots(IReadOnlyDictionary<string,bool> state,IEnumerable<string> roots)
    {
        var paths=roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();var result=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in paths)result[path]=Includes(state,path);
        foreach(var pair in state)if(paths.Any(r=>Rules.Contains(r,pair.Key)))result[pair.Key]=pair.Value;
        return result;
    }
    public static bool Includes(IReadOnlyDictionary<string,bool> state,string path) => state.Where(kv=>Rules.Contains(kv.Key,path)).OrderByDescending(kv=>kv.Key.Length).Select(kv=>(bool?)kv.Value).FirstOrDefault()??false;
    public static bool Expanded(IReadOnlyDictionary<string,bool> current,IReadOnlyDictionary<string,bool> prior) => current.Keys.Concat(prior.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Any(p=>Includes(current,p)&&!Includes(prior,p));
    public static List<string> Positive(IReadOnlyDictionary<string,bool> state)=>state.Where(x=>x.Value).Select(x=>x.Key).ToList();
}
public sealed record VolumeBinding(string Volume, string RelativePath, string PhysicalDisk);
public sealed class Root
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Include { get; set; } = new() { "**/*" };
    public List<string> Exclude { get; set; } = new();
    public bool DefaultSearch { get; set; } = true;
    public bool IgnoreFiles { get; set; } = true;
    public string Description { get; set; } = "";
    public override string ToString() => Path;
}
public sealed class Workspace
{
    public string Name { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string Hash { get; set; } = "";
    public List<Root> Roots { get; set; } = new();
    public override string ToString() => Roots.Count==0?"暂无目录":string.Join("、",Roots.Select(r=>{var name=System.IO.Path.GetFileName(r.Path.TrimEnd('\\','/'));return name.Length>0?name:r.Path;}).Take(2))+(Roots.Count>2?$" 等{Roots.Count}个目录":"");
}
public sealed class Hit : INotifyPropertyChanged
{
    public string Workspace { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string VirtualPath { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string Name => System.IO.Path.GetFileName(FullPath);
    public string Folder => System.IO.Path.GetDirectoryName(FullPath) ?? "";
    public string Type => System.IO.Path.GetExtension(FullPath).ToLowerInvariant();
    public string Snippet { get; set; } = "";
    public string MatchMode {get;set;}="";
    public int? SourceLine {get;set;}
    [System.Text.Json.Serialization.JsonIgnore] public string ThumbnailQuery {get;set;}="";
    [System.Text.Json.Serialization.JsonIgnore] public string PreviewCaption {get;set;}="";
    [System.Text.Json.Serialization.JsonIgnore] public string PreviewNote {get;set;}="";
    [System.Text.Json.Serialization.JsonIgnore] public double? MatchSeconds {get;set;}
    [System.Text.Json.Serialization.JsonIgnore] public bool VerifiedTime {get;set;}
    public int Rank { get; set; }
    public long? Bytes { get; set; }
    public DateTime? Created { get; set; }
    public DateTime? Modified { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public System.Windows.Media.Imaging.BitmapSource? Thumbnail {get;set;}
    public string Availability {get;set;}="正在核对原文件";
    public string Size => Bytes is null ? "—" : $"{Bytes / 1024d:N1} KB";
    public string Creation => Created?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string Modification => Modified?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string Glyph => Rules.TypeMatches(Type,"图片")?"\uE91B":Rules.TypeMatches(Type,"音频")?"\uE8D6":Rules.TypeMatches(Type,"视频")?"\uE714":Rules.TypeMatches(Type,"压缩包")?"\uF012":Rules.TypeMatches(Type,"代码")?"\uE943":"\uE8A5";
    public string ListSnippet => string.IsNullOrWhiteSpace(Snippet)?"Soma 未返回摘要":Snippet.Replace('\r',' ').Replace('\n',' ');
    public string FileInfo => Type+" · "+Size+" · "+Availability;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
}
public sealed record Counts(long Documents, long Ready, long Pending, long Failed, long Chunks, long Embeddings);

public sealed record RootIndexProgress(long Documents,long Lexical,long Failed,long Chunks,long Vectors,DateTimeOffset ReadAt);

public static class Rules
{
    public static string Remaining(TimeSpan time){var minutes=Math.Max(0,(long)Math.Ceiling(time.TotalMinutes));return minutes>=60?$"{minutes/60} 小时 {minutes%60} 分钟":$"{minutes} 分钟";}
    public static string Normalize(string path) => System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
    public static bool Contains(string parent, string child)
    {
        var p = Normalize(parent); var c = Normalize(child);
        return c.Equals(p, StringComparison.OrdinalIgnoreCase) || c.StartsWith(p + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    public static bool InScope(string path, IEnumerable<string> scope) => scope.Any(s => Contains(s, path));
    public static bool Expanded(IEnumerable<string> now, IEnumerable<string> searched) => now.Any(p => !searched.Any(s => Contains(s, p)));
    public static int[] Allocate(IReadOnlyList<long> weights, int limit)
    {
        if (weights.Count == 0) return Array.Empty<int>();
        limit = Math.Max(limit, weights.Count);
        var shares = new double[weights.Count];
        var active = Enumerable.Range(0, weights.Count).ToList(); double remaining = limit;
        while (active.Count > 0)
        {
            var total = active.Sum(i => (double)Math.Max(1, weights[i]));
            var small = active.Where(i => remaining * Math.Max(1, weights[i]) / total < 1).ToArray();
            if (small.Length == 0) { foreach (var i in active) shares[i] = remaining * Math.Max(1, weights[i]) / total; break; }
            foreach (var i in small) { shares[i] = 1; remaining--; active.Remove(i); }
        }
        var result = shares.Select(x => (int)Math.Floor(x)).ToArray();
        foreach (var i in Enumerable.Range(0, result.Length).OrderByDescending(i => shares[i] - result[i]).Take(limit - result.Sum())) result[i]++;
        return result;
    }
    public static bool TypeMatches(string extension, string filter)
    {
        if (filter == "全部") return true;
        var list = filter switch
        {
            "文档" => ".txt .md .pdf .doc .docx .xls .xlsx .ppt .pptx .rtf .odt .csv",
            "图片" => ".jpg .jpeg .png .gif .bmp .webp .heic .tif .tiff",
            "视频" => ".mp4 .mkv .avi .mov .wmv .webm .m4v",
            "音频" => ".mp3 .wav .flac .aac .m4a .ogg .wma",
            "压缩包" => ".zip .rar .7z .tar .gz",
            "代码" => ".cs .py .js .ts .tsx .html .css .json .yml .yaml .java .cpp .c .h .xml .ps1",
            _ => filter.Replace(';', ' ').Replace(',', ' ')
        };
        return list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(x => (x.StartsWith('.') ? x : "." + x).Equals(extension, StringComparison.OrdinalIgnoreCase));
    }
}
