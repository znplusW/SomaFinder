// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

// Destructive automation is available only inside a newly created hosted-runner fixture.
// Ordinary test modes retain the original unconditional rebuild protection.
internal static class CloudSandbox
{
    public static string Root {get;private set;}="";
    public static bool Active=>Root.Length>0;
    public static readonly List<string> Errors=new();
    public static void Initialize(string root)
    {
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true"||Environment.GetEnvironmentVariable("RUNNER_OS")!="Windows"||Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")!="github-hosted")throw new InvalidOperationException("云端测试仅允许 GitHub 托管 Windows 运行器。");
        var temp=Environment.GetEnvironmentVariable("RUNNER_TEMP")??throw new InvalidOperationException("RUNNER_TEMP 缺失。");
        root=Rules.Normalize(root);
        if(!Rules.Contains(temp,root)||root==Rules.Normalize(temp)||!System.IO.Path.GetFileName(root).StartsWith("SomaFinder-cloud-",StringComparison.Ordinal))throw new InvalidOperationException("测试目录必须是 RUNNER_TEMP 内独立的 SomaFinder-cloud-* 目录。");
        if(File.Exists(System.IO.Path.Combine(root,"cloud-test-marker.json")))throw new InvalidOperationException("云端测试目录不可重用。");
        Directory.CreateDirectory(root);Root=root;
        Store.Atomic(System.IO.Path.Combine(root,"cloud-test-marker.json"),JsonSerializer.Serialize(new{run=Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),nonce=Guid.NewGuid()}));
    }
    public static bool AllowsRebuild(Store store,Workspace workspace)
    {
        if(!Active)return false;
        bool Safe(string path)
        {
            if(!Rules.Contains(Root,path)||Rules.Normalize(path)==Root)return false;
            var p=Rules.Normalize(path);
            while(Rules.Contains(Root,p)&&p!=Root)
            {
                if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)return false;
                p=System.IO.Path.GetDirectoryName(p)!;
            }
            return true;
        }
        return Safe(store.ConfigDirectory)&&Safe(store.StateDirectory)&&Safe(store.DataDirectory)&&Safe(workspace.ConfigPath)&&workspace.Roots.Count>0&&workspace.Roots.All(r=>Safe(r.Path));
    }
}
