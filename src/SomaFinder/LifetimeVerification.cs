// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;
internal static class LifetimeVerification
{
    internal static int Run(string[] args)
    {
        string Arg(string key)=>args.Single(a=>a.StartsWith(key+"="))[(key.Length+1)..];
        string root=Arg("--fixture");
        if(args.Contains("--verify-lifetime-owner"))CloudSandbox.Initialize(root);
        else if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true"||!File.Exists(root+"/cloud-test-marker.json"))throw new InvalidOperationException("Cloud owner fixture required");
        if(args.Contains("--verify-lifetime-child"))
        {
            Console.OutputEncoding=new UTF8Encoding(false);
            var psi=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};psi.ArgumentList.Add("--verify-lifetime-leaf");psi.ArgumentList.Add("--fixture="+root);
            using var leaf=Process.Start(psi)!;Store.Atomic(root+"/child.json",JsonSerializer.Serialize(new{id=Environment.ProcessId,leaf=leaf.Id,environment=Environment.GetEnvironmentVariable("SOMAFINDER_ARGUMENT_TEST"),args}));
            Console.WriteLine("managed stdout 中文");Console.Error.WriteLine("managed stderr 中文");Thread.Sleep(120000);return 0;
        }
        if(args.Contains("--verify-lifetime-leaf")){Thread.Sleep(120000);return 0;}
        var target=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};target.Environment["SOMAFINDER_ARGUMENT_TEST"]="中文 Unicode";
        foreach(var a in new[]{"--verify-lifetime-child","--fixture="+root,"空 格","引号\"尾斜线\\"})target.ArgumentList.Add(a);
        var child=ManagedProcess.Start(target);_=ManagedProcess.ReadLines(child.Output,line=>File.AppendAllText(root+"/stdout.txt",line+"\n"));_=ManagedProcess.ReadLines(child.Error,line=>File.AppendAllText(root+"/stderr.txt",line+"\n"));
        if(args.Any(a=>a.StartsWith("--soma=")))
        {
            var store=new Store(root+"/data",root+"/config/soma",root+"/state/soma");Directory.CreateDirectory(root+"/docs");
            Store.Atomic(root+"/docs/sentinel.md","Existing committed index must survive owner termination.");
            store.SaveWorkspace(new Workspace{Name="main",ConfigPath=store.ConfigDirectory+"/main.yml",Roots=new(){new(){Name="docs",Path=root+"/docs"}}});
            var seed=new ProcessStartInfo(Arg("--soma")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true};
            seed.Environment["XDG_CONFIG_HOME"]=root+"/config";seed.Environment["XDG_STATE_HOME"]=root+"/state";seed.Environment["XDG_DATA_HOME"]=root+"/models";
            foreach(var argument in new[]{"--workspace","main","system","scan"})seed.ArgumentList.Add(argument);
            var scan=ManagedProcess.Start(seed);_=ManagedProcess.ReadLines(scan.Output,_=>{});_=ManagedProcess.ReadLines(scan.Error,_=>{});
            if(!scan.Process.WaitForExit(30000)||scan.Process.ExitCode!=0)throw new IOException("Cloud synthetic scan failed");
            var backend=new ProcessStartInfo(Arg("--soma")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};
            backend.Environment["XDG_CONFIG_HOME"]=root+"/config";backend.Environment["XDG_STATE_HOME"]=root+"/state";backend.Environment["XDG_DATA_HOME"]=root+"/models";
            backend.ArgumentList.Add("--workspace");backend.ArgumentList.Add("main");backend.ArgumentList.Add("server");backend.ArgumentList.Add("--port="+Arg("--port"));
            var soma=ManagedProcess.Start(backend);_=ManagedProcess.ReadLines(soma.Output,line=>File.AppendAllText(root+"/soma-stdout.txt",line+"\n"));_=ManagedProcess.ReadLines(soma.Error,line=>File.AppendAllText(root+"/soma-stderr.txt",line+"\n"));
            Store.Atomic(root+"/soma.json",JsonSerializer.Serialize(new{id=soma.Process.Id}));
        }
        Store.Atomic(root+"/owner-ready.json",JsonSerializer.Serialize(new{id=Environment.ProcessId,child=child.Process.Id}));Thread.Sleep(120000);return 0;
    }
}
