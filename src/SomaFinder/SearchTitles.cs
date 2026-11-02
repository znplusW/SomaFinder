// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

// Only search windows participate. Detail windows use their parent's complete title.
internal sealed class SearchTitles
{
    sealed class Group { public int Next = 1; public bool Numbered; public readonly HashSet<object> Members = new(); }
    sealed record Entry(string Query, int Number);
    readonly Dictionary<string, Group> groups = new(StringComparer.Ordinal);
    readonly Dictionary<object, Entry> entries = new();
    public void Submit(object window, string query)
    {
        query = query.Trim();
        if (entries.TryGetValue(window, out var previous) && previous.Query == query) return;
        Remove(window);
        if (!groups.TryGetValue(query, out var group)) groups[query] = group = new();
        if (group.Members.Count > 0) group.Numbered = true;
        group.Members.Add(window);
        entries[window] = new(query, group.Next++);
    }
    public void Remove(object window)
    {
        if (!entries.Remove(window, out var entry)) return;
        var group = groups[entry.Query];
        group.Members.Remove(window);
        if (group.Members.Count == 0) groups.Remove(entry.Query);
    }
    public string Title(object window)
    {
        if (!entries.TryGetValue(window, out var entry)) return "Soma Finder";
        var text = entry.Query.Length == 0 ? "Soma Finder 文件浏览" : "Soma Finder 搜索“" + entry.Query + "”";
        return text + (groups[entry.Query].Numbered ? "（" + entry.Number + "）" : "");
    }
}
