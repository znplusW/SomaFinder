// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net.Http;
using System.Net.Http.Json;

namespace SomaFinder;

internal sealed record WorkspaceWriter(string Workspace,int? Pid,string Problem="");
internal sealed record InstanceInspection(List<Native.Proc> Managed,string Conflict);

internal static class BackendInstances
{
    // Soma 0.10.7 uses a one-byte advisory lock at this offset, not FileShare.None.
    internal const long LockPosition=long.MaxValue-1;
    public static List<WorkspaceWriter> Writers(Store store,IEnumerable<Workspace> workspaces)
    {
        var result=new List<WorkspaceWriter>();
        foreach(var ws in workspaces)
        {
            var file=System.IO.Path.Combine(store.StateDirectory,"locks",ws.Name+".lock");if(!File.Exists(file))continue;
            try
            {
                using var stream=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);
                bool held=false;try{stream.Lock(LockPosition,1);stream.Unlock(LockPosition,1);}catch(IOException){held=true;}
                if(!held)continue;
                int? pid=null;
                try{var bytes=new byte[(int)Math.Min(stream.Length,65536)];stream.ReadExactly(bytes);using var metadata=JsonDocument.Parse(bytes);if(metadata.RootElement.TryGetProperty("pid",out var value)&&value.TryGetInt32(out var owner))pid=owner;}catch(IOException){}catch(JsonException){}
                result.Add(new(ws.Name,pid));
            }
            catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){result.Add(new(ws.Name,null,"无法核对工作区写锁："+ex.Message));}
        }
        return result;
    }
    internal static bool SameStore(JsonElement status,Store store,Workspace ws)
        =>status.TryGetProperty("workspace",out var name)&&name.GetString()==ws.Name&&status.TryGetProperty("databaseFile",out var db)&&db.ValueKind==JsonValueKind.String&&Native.SameStorageFile(db.GetString()!,System.IO.Path.Combine(store.StateDirectory,ws.Name+".sqlite"))&&status.TryGetProperty("configFile",out var config)&&config.ValueKind==JsonValueKind.String&&Native.SameStorageFile(config.GetString()!,ws.ConfigPath);
    internal static bool HasIdentity(JsonElement status)=>status.TryGetProperty("databaseFile",out var db)&&db.ValueKind==JsonValueKind.String&&status.TryGetProperty("configFile",out var config)&&config.ValueKind==JsonValueKind.String;
    internal static bool TouchesStore(JsonElement status,Store store,IReadOnlyList<Workspace> workspaces)
        =>HasIdentity(status)&&workspaces.Any(ws=>Native.SameStorageFile(status.GetProperty("databaseFile").GetString()!,System.IO.Path.Combine(store.StateDirectory,ws.Name+".sqlite"))||Native.SameStorageFile(status.GetProperty("configFile").GetString()!,ws.ConfigPath));
    public static async Task<JsonElement?> Status(Native.Proc process)
    {
        try
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(3)};using var response=await http.PostAsJsonAsync("http://127.0.0.1:"+Native.ServerPort(process)+"/api/run",new{command="status"});response.EnsureSuccessStatusCode();
            using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if(document.RootElement.GetProperty("success").GetBoolean())return document.RootElement.GetProperty("data").Clone();
        }
        catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException){}
        return null;
    }
    public static async Task<InstanceInspection> Inspect(Store store,Preferences prefs,IReadOnlyList<Workspace> workspaces,IReadOnlyCollection<int> owned)
    {
        var writers=Writers(store,workspaces);var all=await Native.BackendProcesses(new[]{prefs.SomaExe}.Concat(prefs.KnownBackendPaths),writers.Where(w=>w.Pid!=null).Select(w=>w.Pid!.Value));
        var managed=new List<Native.Proc>();var issues=new List<string>();
        var writerOwners=writers.Where(w=>w.Pid!=null).Select(w=>w.Pid!.Value).ToHashSet();
        foreach(var writer in writers)
        {
            if(writer.Pid is int pid&&owned.Contains(pid))continue;
            var process=all.FirstOrDefault(p=>p.Id==writer.Pid);
            if(process!=null&&Native.SameFilePath(process.File,prefs.SomaExe)&&Native.WorkspaceName(process).Equals(writer.Workspace,StringComparison.OrdinalIgnoreCase))managed.Add(process);
            else issues.Add("工作区 "+writer.Workspace+" 正由其他实例写入"+(process!=null?"："+process.File:"（进程身份待核对）"));
        }
        foreach(var process in all)
        {
            if(owned.Contains(process.Id)||writerOwners.Contains(process.Id)||!Native.Alive(process))continue;
            var ws=workspaces.FirstOrDefault(w=>w.Name.Equals(Native.WorkspaceName(process),StringComparison.OrdinalIgnoreCase));
            if(Native.IsServer(process))
            {
                var status=await Status(process);
                if(status is JsonElement value&&HasIdentity(value))
                {
                    if(!TouchesStore(value,store,workspaces))continue;
                    if(ws!=null&&SameStore(value,store,ws)&&Native.SameFilePath(process.File,prefs.SomaExe))managed.Add(process);
                    else issues.Add("其他 Soma 后端正在使用相同的配置或索引："+process.File);
                }
                else if(ws!=null)issues.Add("无法核对其他实例使用的索引："+process.File+"（PID "+process.Id+"）");
            }
            else if(ws!=null&&Native.IsIndex(process))issues.Add("发现正在索引的其他实例，无法确认数据位置："+process.File+"（PID "+process.Id+"）");
        }
        return new(managed.DistinctBy(p=>p.Id).ToList(),issues.Count==0?"":string.Join("\n",issues.Distinct())+"\n请先关闭对应实例，再重新检测；暂缓启动新的索引任务。");
    }
}
