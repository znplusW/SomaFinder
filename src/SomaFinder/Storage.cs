// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Security.Cryptography;
using YamlDotNet.RepresentationModel;

namespace SomaFinder;
public sealed class Store
{
    public string DataDirectory { get; }
    public string ConfigDirectory { get; }
    public string StateDirectory { get; }
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public Store(string? data = null, string? config = null, string? state = null)
    {
        DataDirectory = data ?? System.IO.Path.Combine(AppContext.BaseDirectory, "data");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ConfigDirectory = config ?? System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? System.IO.Path.Combine(home, ".config"), "soma");
        StateDirectory = state ?? System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? System.IO.Path.Combine(home, ".local", "state"), "soma");
        Directory.CreateDirectory(DataDirectory);
    }
    public Preferences Load()
    {
        var path = System.IO.Path.Combine(DataDirectory, "settings.json");
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) { throw new InvalidDataException("设置文件无法读取。请恢复 data 中的备份，不能忽略已有暂停设置。", ex); }
    }
    public void Save(Preferences prefs) => Atomic(System.IO.Path.Combine(DataDirectory, "settings.json"), JsonSerializer.Serialize(prefs, Json));
    public static void Atomic(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs, new UTF8Encoding(false))) { writer.Write(text); writer.Flush(); fs.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, path + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Digest(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "";
    static string Val(YamlMappingNode node, string key, string fallback = "") => node.Children.TryGetValue(new YamlScalarNode(key), out var v) ? v.ToString() : fallback;
    static List<string> List(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlSequenceNode seq ? seq.Children.Select(x => x.ToString()).ToList() : new();
    public List<Workspace> ReadWorkspaces()
    {
        if (!Directory.Exists(ConfigDirectory)) return new();
        return Directory.GetFiles(ConfigDirectory, "*.yml").OrderBy(x => x).Select(ReadWorkspace).ToList();
    }
    public Workspace ReadWorkspace(string path)
    {
        var yaml = new YamlStream(); using var reader = File.OpenText(path); yaml.Load(reader);
        var map = (YamlMappingNode)yaml.Documents[0].RootNode;
        var result = new Workspace { Name = System.IO.Path.GetFileNameWithoutExtension(path), ConfigPath = path, Hash = Digest(path) };
        if (map.Children.TryGetValue(new YamlScalarNode("projects"), out var nodes) && nodes is YamlSequenceNode seq)
            foreach (var p in seq.Children.Cast<YamlMappingNode>()) result.Roots.Add(new Root { Name = Val(p, "name"), Path = Val(p, "root"), Include = List(p, "include"), Exclude = List(p, "exclude"), DefaultSearch = Val(p, "default-search", "true") == "true", IgnoreFiles = Val(p, "ignore-files", "true") == "true" });
        if(map.Children.TryGetValue(new YamlScalarNode("context"),out var contexts)&&contexts is YamlSequenceNode contextSeq)
            foreach(var root in result.Roots)root.Description=contextSeq.Children.OfType<YamlMappingNode>().Where(n=>Val(n,"project")==root.Name&&Val(n,"path")=="/").Select(n=>Val(n,"text")).FirstOrDefault()??"";
        return result;
    }
    public void SaveWorkspace(Workspace ws)
    {
        if (Digest(ws.ConfigPath) != ws.Hash) throw new IOException("配置已被其他程序修改，请重新加载后再保存。");
        var yaml = new YamlStream();
        if (File.Exists(ws.ConfigPath)) { using var reader = File.OpenText(ws.ConfigPath); yaml.Load(reader); }
        else yaml.Documents.Add(new YamlDocument(new YamlMappingNode { { "version", "1" }, { "context", new YamlSequenceNode() } }));
        var map = (YamlMappingNode)yaml.Documents[0].RootNode;
        var old = map.Children.TryGetValue(new YamlScalarNode("projects"), out var existing) && existing is YamlSequenceNode es ? es.Children.Cast<YamlMappingNode>().ToDictionary(p => Val(p,"name")) : new();
        var seq = new YamlSequenceNode();
        foreach (var root in ws.Roots)
        {
            var p = old.GetValueOrDefault(root.Name) ?? new YamlMappingNode();
            p.Children[new YamlScalarNode("name")] = new YamlScalarNode(root.Name);
            p.Children[new YamlScalarNode("root")] = new YamlScalarNode(root.Path.Replace('\\','/'));
            p.Children[new YamlScalarNode("include")] = new YamlSequenceNode(root.Include.Select(x => new YamlScalarNode(x)));
            p.Children[new YamlScalarNode("exclude")] = new YamlSequenceNode(root.Exclude.Select(x => new YamlScalarNode(x)));
            p.Children[new YamlScalarNode("default-search")] = new YamlScalarNode(root.DefaultSearch ? "true" : "false");
            p.Children[new YamlScalarNode("ignore-files")] = new YamlScalarNode(root.IgnoreFiles ? "true" : "false");
            seq.Add(p);
        }
        map.Children[new YamlScalarNode("projects")] = seq;
        var validNames=ws.Roots.Select(r=>r.Name).ToHashSet();
        var ctx=map.Children.TryGetValue(new YamlScalarNode("context"),out var contextNode)&&contextNode is YamlSequenceNode contextSequence?contextSequence:new YamlSequenceNode();
        foreach(var node in ctx.Children.OfType<YamlMappingNode>().ToArray())
        {
            var project=Val(node,"project");
            if(old.ContainsKey(project)&&!validNames.Contains(project))ctx.Children.Remove(node);
        }
        foreach(var r in ws.Roots)
        {
            var node=ctx.Children.OfType<YamlMappingNode>().FirstOrDefault(n=>Val(n,"project")==r.Name&&Val(n,"path")=="/");
            if(string.IsNullOrWhiteSpace(r.Description)){if(node!=null)ctx.Children.Remove(node);}
            else {if(node==null){node=new YamlMappingNode{{"project",r.Name},{"path","/"}};ctx.Add(node);}node.Children[new YamlScalarNode("text")]=new YamlScalarNode(r.Description);}
        }
        map.Children[new YamlScalarNode("context")]=ctx;
        using var writer = new StringWriter(); yaml.Save(writer, false);
        Atomic(ws.ConfigPath, writer.ToString()); ws.Hash = Digest(ws.ConfigPath);
    }
}
