// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SomaFinder;
public sealed partial class Backend : IDisposable
{
    readonly Store store;
    public Preferences Prefs { get; }
    public List<Workspace> Workspaces { get; private set; } = new();
    readonly Dictionary<string,int> ports=new();
    readonly Dictionary<string,Counts> counts=new();
    readonly Dictionary<string,long> rootCounts=new();
    readonly Dictionary<string,RootIndexProgress> rootProgress=new();
    public RootIndexProgress? RootProgress(string workspace,string root)=>rootProgress.GetValueOrDefault(workspace+"/"+root);
    readonly List<Process> services=new();
    readonly Dictionary<int,Native.Proc> ownedIdentities=new();
    readonly SemaphoreSlim queries=new(2,2), models=new(1,1), lifecycle=new(1,1), indexing=new(1,1), inspection=new(1,1);
    readonly HttpClient http=new() { Timeout=Timeout.InfiniteTimeSpan };
    readonly AsyncLocal<int> readLease=new();
    readonly HashSet<string> due=new();
    readonly Dictionary<string,DateTimeOffset> retryAfter=new();bool manualBatch;int stopEpoch;
    readonly Dictionary<string,DateTimeOffset> resumeNotBefore=new();
    string storageIssue="";string? activeWorkspace;
    List<Native.Proc> external=new();
    Process? indexProcess; int runningQueries,activeRpc; bool draining,exiting,pauseTransition,forcePause,initialized; bool pulseBusy; DateTimeOffset lastInspect=DateTimeOffset.MinValue;
    bool activeIndexManual;readonly HashSet<int> expectedStops=new();
    internal Func<bool?> PowerSource {get;set;}=()=>Native.ACPower;
    public bool WaitingForSearch=>draining&&(runningQueries>0||activeRpc>0);
    public void CancelSearchAndPause(){forcePause=true;}
    readonly Dictionary<int,(Native.Proc Process,Native.Proc Owner,ProcessPriorityClass Original)> priorities=new();
    public int? ActiveIndexProcessId=>indexProcess is{HasExited:false}?indexProcess.Id:null;
    public event Action? Changed;
    public string Message { get; private set; }="正在连接 Soma…";
    public string Progress { get; private set; }="状态待获取";
    public string ConnectedVersion {get;private set;}="未连接";
    public string InstanceConflict {get;private set;}="";
    public bool Paused=>sessionStopped||TimedPaused;
    public bool Indexing=>indexProcess is {HasExited:false} || external.Any(p=>(Native.IsIndex(p)||SharedAuto(p))&&Native.Alive(p));
    public bool Busy=>Indexing || indexing.CurrentCount==0;
    public bool Exiting=>exiting;
    public bool IsInitialized=>initialized;
    public int Generation { get; private set; }
    internal long RequestCount {get;private set;}
    internal long SearchCount {get;private set;}
    internal int InventoryReadCount {get;private set;}
    internal Func<string,Task>? RequestBarrier {get;set;}
    public Backend(Store s,Preferences p) {store=s;Prefs=p;if(!string.IsNullOrWhiteSpace(p.SomaExe))p.SomaExe=System.IO.Path.GetFullPath(p.SomaExe);Reload();}
    public void Reload() { Workspaces=store.ReadWorkspaces(); counts.Clear();rootCounts.Clear();rootProgress.Clear();fileLists.Clear();Generation++; Changed?.Invoke(); }
    async Task<List<Native.Proc>> Inspect()
    {
        await inspection.WaitAsync();
        try
        {
            InventoryReadCount++;
            var owned=Owned();var result=await BackendInstances.Inspect(store,Prefs,Workspaces,owned.Select(p=>p.Id).ToArray());InstanceConflict=result.Conflict;
            return result.Managed.Concat(owned).DistinctBy(p=>p.Id).ToList();
        }
        catch{InstanceConflict="无法完整核对 Soma 进程归属，自动索引暂缓。请关闭其他实例或在设置中重新检测。";throw;}
        finally{inspection.Release();}
    }
    List<Native.Proc> Owned()
    {
        var result=new List<Native.Proc>();
        foreach(var process in services.Concat(indexProcess==null?Array.Empty<Process>():new[]{indexProcess}))
            try{if(!process.HasExited&&ownedIdentities.TryGetValue(process.Id,out var identity)&&Native.Alive(identity))result.Add(identity);}catch(InvalidOperationException){}
        foreach(var id in ownedIdentities.Keys.Except(result.Select(p=>p.Id)).ToArray())ownedIdentities.Remove(id);
        return result;
    }
    public async Task RecheckInstances(){external=await Inspect();lastInspect=DateTimeOffset.UtcNow;Changed?.Invoke();}
    public void AwaitingBackendSelection(){Message="尚未选择 Soma 后端，请在设置中选择";Changed?.Invoke();}
    public void Save() {store.Save(Prefs); Changed?.Invoke();}
    public void ChangeExecutable(string path)
    {
        if(!string.IsNullOrEmpty(Prefs.SomaExe)&&Native.SameFilePath(path,Prefs.SomaExe))return;
        ValidateExecutableChange(path);
        foreach(var proc in external)try{Native.Stop(proc);}catch(ArgumentException){}
        foreach(var service in services.Where(p=>!p.HasExited)){service.Kill(true);service.WaitForExit(10000);}
        ports.Clear();external.Clear();InstanceConflict="";initialized=false;
    }
    public void ValidateExecutableChange(string path)
    {
        if(!Native.SameFilePath(path,Prefs.SomaExe)&&(Busy||runningQueries>0))throw new IOException("请等待当前搜索或索引结束后再更换 Soma 程序。");
    }
    public static bool AutomaticAllowed(Preferences p,DateTimeOffset now,bool ac,bool memory)=>!p.IsPaused(now) && (!p.BatteryBlocksAuto||ac) && (!p.LowMemoryBlocksAuto||memory);
    public async Task Initialize(bool scheduleStartup=true)
    {
        if(File.Exists(System.IO.Path.Combine(store.DataDirectory,"relocation-journal.json")))throw new IOException("发现未完成的目录盘符迁移，请检查 data/relocation-journal.json 与配置备份；为保护索引暂缓接管。");
        if(!File.Exists(Prefs.SomaExe)) throw new FileNotFoundException("请在设置中选择 soma.exe。");
        ConnectedVersion=await BackendDiscovery.Version(Prefs.SomaExe);
        if(!new BackendCandidate(Prefs.SomaExe,ConnectedVersion,0).IsSoma)throw new IOException("所选程序未能识别为 Soma 后端："+ConnectedVersion);
        external=await Inspect();
        await ApplyPendingConfiguration(true);
        foreach(var p in external.Where(Native.IsServer))
        {
            var ws=Native.WorkspaceName(p);if(Workspaces.Any(w=>w.Name==ws))ports[ws]=Native.ServerPort(p);
        }
        // Register only current verified roots. Existing bindings are never overwritten on a mismatch.
        foreach(var root in Workspaces.SelectMany(w=>w.Roots)) if(!Prefs.Volumes.ContainsKey(root.Path) && Directory.Exists(root.Path))
        { var id=Native.Identify(root.Path); if(id!=null) Prefs.Volumes[root.Path]=id; }
        Save();
        if(Paused) await ApplyPause();
        else if(scheduleStartup&&Prefs.StartupSync) foreach(var ws in Workspaces) due.Add(ws.Name);
        initialized=true;await Pulse();
    }
    async Task<int> Port(Workspace ws)
    {
        if(ports.TryGetValue(ws.Name,out var existing)) return existing;
        await lifecycle.WaitAsync();
        try
        {
            if(ports.TryGetValue(ws.Name,out existing)) return existing;
            if(exiting) throw new IOException("程序正在退出。");
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            // Soma enables auto-sync only when its zero-argument flag is present.
            var p=Start(ws,new[]{"server","--port="+port},"server");services.Add(p);ports[ws.Name]=port;
            for(int i=0;i<100;i++)
            {
                if(p.HasExited) throw new IOException("Soma 服务启动失败，请查看 data/logs。");
                try { using var tcp=new TcpClient();await tcp.ConnectAsync(IPAddress.Loopback,port);return port; }
                catch(SocketException) {await Task.Delay(150);}
            }
            throw new TimeoutException("等待 Soma 搜索服务超时。");
        }
        catch {ports.Remove(ws.Name);throw;} finally {lifecycle.Release();}
    }
    Process Start(Workspace ws,IEnumerable<string> args,string kind)
    {
        if(exiting)throw new IOException("程序正在退出。");
        var logDir=System.IO.Path.Combine(store.DataDirectory,"logs");Directory.CreateDirectory(logDir);
        var psi=new ProcessStartInfo(Prefs.SomaExe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=System.IO.Path.GetDirectoryName(Prefs.SomaExe)!};
        psi.Environment["XDG_CONFIG_HOME"]=System.IO.Path.GetDirectoryName(store.ConfigDirectory)!;
        psi.Environment["XDG_STATE_HOME"]=System.IO.Path.GetDirectoryName(store.StateDirectory)!;
        foreach(var arg in new[]{"--workspace",ws.Name}.Concat(args)) psi.ArgumentList.Add(arg);
        var started=ManagedProcess.Start(psi);var proc=started.Process;
        ownedIdentities[proc.Id]=new(proc.Id,Environment.ProcessId,psi.FileName,"",proc.StartTime.ToUniversalTime().Ticks,psi.ArgumentList.ToArray());
        var logPath=System.IO.Path.Combine(logDir,$"{ws.Name}-{kind}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.log");
        void Log(string? line)
        {
            if(line==null)return;
            try{lock(proc){if(File.Exists(logPath)&&new FileInfo(logPath).Length>10*1024*1024)File.Move(logPath,logPath+".previous",true);File.AppendAllText(logPath,line+Environment.NewLine);}}
            catch(IOException){}catch(UnauthorizedAccessException){}
            if(kind=="sync")Message=Phase(line);
        }
        _=ManagedProcess.ReadLines(started.Output,Log);_=ManagedProcess.ReadLines(started.Error,Log);
        return proc;
    }
    static string Phase(string line) {var s=line.ToLowerInvariant();return s.Contains("embed")?"生成语义向量":s.Contains("extract")||s.Contains("ocr")?"提取文件内容":s.Contains("scan")?"扫描文件":s.Contains("pull")?"检查模型与依赖":"正在更新索引";}
    public async Task<JsonElement> Rpc(Workspace ws,string command,object? args=null,object? options=null)
    {
        using var memoryUse=command!="status"?await UseModelResources():null;
        bool tracked=command!="status";
        if(tracked)RequestCount++;
        if(tracked&&draining&&readLease.Value==0)throw new IOException("正在切换搜索服务，请稍后重试。");
        if(tracked)Interlocked.Increment(ref activeRpc);
        try
        {
        if(tracked&&RequestBarrier!=null)await RequestBarrier(command);
        if(command!="status")await Rpc(ws,"status");
        var port=await Port(ws);
        using var response=await http.PostAsJsonAsync($"http://127.0.0.1:{port}/api/run{verificationRoutes.GetValueOrDefault(ws.Name, "")}",new {command,args=args??Array.Empty<string>(),options=options??new{}});
        response.EnsureSuccessStatusCode(); using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if(!doc.RootElement.GetProperty("success").GetBoolean()) throw new IOException(doc.RootElement.GetProperty("error").GetProperty("message").GetString());
        var data=doc.RootElement.GetProperty("data").Clone();
        if(command=="status"&&!BackendInstances.SameStore(data,store,ws)){ports.Remove(ws.Name);throw new IOException("服务的索引范围、配置或索引路径不匹配，已停止使用该连接，请重试。");}
        return data;
        }
        finally{if(tracked)Interlocked.Decrement(ref activeRpc);}
    }
    public Hit ParseHit(Workspace ws,JsonElement item,int rank)
    {
        var virtualPath=item.TryGetProperty("virtualPath",out var vp)?vp.GetString()!:"soma://"+item.GetProperty("project").GetString()+"/"+item.GetProperty("path").GetString();
        var tail=virtualPath.StartsWith("soma://")?virtualPath[7..]:virtualPath;
        int slash=tail.IndexOf('/');var name=slash<0?tail:tail[..slash];var relative=slash<0?"":tail[(slash+1)..];
        var root=ws.Roots.First(r=>r.Name==name);
        var full=System.IO.Path.GetFullPath(System.IO.Path.Combine(root.Path,relative.Replace('/',System.IO.Path.DirectorySeparatorChar)));
        if(!Rules.Contains(root.Path,full))throw new IOException("Soma 返回了范围外路径。");
        return new Hit{Workspace=ws.Name,RootPath=root.Path,VirtualPath=virtualPath,FullPath=full,Rank=rank,SourceLine=item.TryGetProperty("line",out var line)&&line.ValueKind==JsonValueKind.Number&&line.TryGetInt32(out var lineNumber)?lineNumber:null,Bytes=item.TryGetProperty("sizeBytes",out var size)?size.GetInt64():null,Snippet=item.TryGetProperty("snippet",out var snippet)?snippet.GetString()??"":item.TryGetProperty("body",out var body)?body.GetString()??"":""};
    }
    public async Task<string> Body(Hit h)
    {
        var data=await Rpc(Workspaces.First(w=>w.Name==h.Workspace),"get",new[]{h.VirtualPath},new Dictionary<string,object>{{"max-size","128KiB"}});
        return string.Join("\n\n",data.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("body").GetString()));
    }
    public void QueueAll(){foreach(var w in Workspaces)due.Add(w.Name);}
    public async Task ManualAll(IEnumerable<string> names)
    {
        if(manualBatch)throw new IOException("已有一组手动更新正在执行。");manualBatch=true;var epoch=stopEpoch;
        try{foreach(var name in names){if(exiting||Paused||epoch!=stopEpoch)break;var ws=Workspaces.FirstOrDefault(w=>w.Name==name);if(ws!=null)await RunSync(ws,true);}}
        finally{manualBatch=false;}
    }
    public void ApplyPriority()
    {
        foreach(var pair in priorities.ToArray())
        {
            try
            {
                var record=pair.Value;if(Prefs.LowerPriority&&Native.Alive(record.Owner))continue;
                if(Native.Alive(record.Process)){using var p=Process.GetProcessById(pair.Key);if(p.MainModule?.FileName is string image&&Native.SameFilePath(image,record.Process.File))p.PriorityClass=record.Original;}
                priorities.Remove(pair.Key);
            }catch(Exception){Message="部分任务优先级未能更改";}
        }
        if(!Prefs.LowerPriority)return;
        var roots=external.Where(p=>Native.IsIndex(p)&&Native.Alive(p)).ToList();
        if(indexProcess is{HasExited:false}&&!roots.Any(p=>p.Id==indexProcess.Id))roots.Add(new(indexProcess.Id,0,Prefs.SomaExe,"soma.exe sync",indexProcess.StartTime.ToUniversalTime().Ticks));
        foreach(var owner in roots)try
        {
            foreach(var proc in Native.Descendants(owner).Prepend(owner))
            {
                if(!Native.Alive(proc))continue;using var p=Process.GetProcessById(proc.Id);if(p.MainModule?.FileName is not string image||!Native.SameFilePath(image,proc.File))continue;
                if(!priorities.TryGetValue(proc.Id,out var existing)||existing.Process.StartTicks!=proc.StartTicks)priorities[proc.Id]=(proc,owner,p.PriorityClass);
                p.PriorityClass=ProcessPriorityClass.BelowNormal;
            }
        }catch(Exception){Message="部分任务优先级未能更改";}
    }
    public async Task RunSync(Workspace ws,bool manual=false,bool rebuild=false)
    {
        using var memoryUse=await UseModelResources();
        if(rebuild&&(CloudSandbox.Active||Environment.GetEnvironmentVariable("SOMAFINDER_TEST")=="1")&&!CloudSandbox.AllowsRebuild(store,ws))throw new InvalidOperationException("测试模式禁止清空或重建非云端临时样例索引。");
        if(!initialized)throw new IOException("后端尚未完成接管，请稍后操作。");
        if(ws.Roots.Count==0)throw new IOException("请先添加索引目录。");
        if(Paused||pauseTransition||configurationChanging||File.Exists(PendingPath)||exiting)throw new IOException("索引已暂停，请先恢复索引或完成待应用的设置。");
        if(Busy)throw new IOException("已有索引任务正在执行。");
        if(!manual&&!AutomaticAllowed(Prefs,DateTimeOffset.UtcNow,PowerSource()==true,Native.EnoughMemory)) return;
        if(ws.Roots.Any(r=>!Directory.Exists(r.Path)||!Native.Verified(r.Path,Prefs)))throw new IOException("根目录离线或卷身份不匹配，未启动索引。");
        if(!await indexing.WaitAsync(0))throw new IOException("已有索引任务正在执行。");
        try
        {
            if(Paused||configurationChanging||exiting) return;
            await RecheckInstances();
            if(Paused||pauseTransition||configurationChanging||exiting||(!manual&&!AutomaticAllowed(Prefs,DateTimeOffset.UtcNow,PowerSource()==true,Native.EnoughMemory)))return;
            if(InstanceConflict.Length>0)throw new IOException(InstanceConflict);
            if(Indexing)throw new IOException("已有索引任务正在执行。");
            activeWorkspace=ws.Name;activeIndexManual=manual;Message="正在更新索引 · "+ws;
            indexProcess=Start(ws,rebuild?new[]{"system","scan"}:new[]{"sync"},"sync");
            ApplyPriority();
            Changed?.Invoke();await indexProcess.WaitForExitAsync();
            if(expectedStops.Remove(indexProcess.Id))return;
            if(indexProcess.ExitCode!=0)throw new IOException("索引任务中断或失败；保留现有索引，请查看日志。");
            if(Paused||exiting)return;
            if(rebuild) {indexProcess=Start(ws,new[]{"sync"},"sync");ApplyPriority();Changed?.Invoke();await indexProcess.WaitForExitAsync();if(expectedStops.Remove(indexProcess.Id))return;if(indexProcess.ExitCode!=0)throw new IOException("重建后的内容处理未完成。");}
            Prefs.LastFinished[ws.Name]=DateTimeOffset.UtcNow;due.Remove(ws.Name);resumeSync.Remove(ws.Name);fileLists.Clear();retryAfter.Remove(ws.Name);Save();Message="索引更新完成";
        }
        finally {indexProcess=null;activeWorkspace=null;activeIndexManual=false;ApplyPriority();indexing.Release();Changed?.Invoke();}
    }
    public async Task Pause(double hours)
    {
        if(double.IsNaN(hours)||double.IsInfinity(hours)||hours<=0||hours>87600)throw new ArgumentException("请输入大于 0 且不超过 87600 的小时数。");
        stopEpoch++;var old=Prefs.PauseUntil;Prefs.PauseUntil=DateTimeOffset.UtcNow.AddHours(hours);try{Save();}catch{Prefs.PauseUntil=old;throw;}ResetPausedIndexMemory();await ApplyPause();
    }
    bool SharedAuto(Native.Proc process)
    {
        if(!Native.IsServer(process))return false;
        if(Native.Arguments(process).Any(a=>a.Equals("--auto-sync=false",StringComparison.OrdinalIgnoreCase)))return false;
        if(Native.AutoSync(process))return true;
        return false;
    }
    bool StopRequired(bool timed)=>timed?Paused:Prefs.BatteryBlocksAuto&&Prefs.BatteryStopsAuto&&PowerSource()==false;
    Task ApplyPause()=>ApplyIndexStop(true);
    async Task ApplyIndexStop(bool timed,HashSet<string>? only=null,bool configuration=false)
    {
        if(pauseTransition)return;
        pauseTransition=true;stopEpoch++;
        try
        {
            if((!configuration&&!StopRequired(timed))||exiting)return;
            // Owned handles and previously verified identities are sufficient to stop
            // those processes. Spawning CIM/PowerShell during a heavy search can time
            // out, and must not be a prerequisite for pausing our own index worker.
            var processes=Owned().Concat(external.Where(Native.Alive)).DistinctBy(p=>p.Id).ToList();
            var writers=BackendInstances.Writers(store,Workspaces.Where(w=>only==null||only.Contains(w.Name)));
            if(writers.Any(w=>w.Pid==null||!processes.Any(p=>p.Id==w.Pid)))processes=await Inspect();
            if((!configuration&&!StopRequired(timed))||exiting)return;
            processes=processes.Where(p=>only==null||only.Contains(Native.WorkspaceName(p))).ToList();
            // Separate writers can stop immediately while every search service stays alive.
            foreach(var p in processes.Where(Native.IsIndex))
            {
                if(!timed&&(indexProcess==null||p.Id!=indexProcess.Id||activeIndexManual))continue;
                resumeSync.Add(Native.WorkspaceName(p));
                if(indexProcess is {HasExited:false}&&p.Id==indexProcess.Id)expectedStops.Add(p.Id);
                Native.Stop(p);
            }
            var writerIds=(timed||configuration)?writers.Where(w=>w.Pid!=null).Select(w=>w.Pid!.Value).ToHashSet():new HashSet<int>();
            var shared=processes.Where(p=>SharedAuto(p)||((timed||configuration)&&Native.IsServer(p)&&writerIds.Contains(p.Id))).ToList();
            if(shared.Count>0)
            {
                draining=true;
                while((runningQueries>0||activeRpc>0)&&(configuration||StopRequired(timed))&&!exiting&&!forcePause)
                {Message="等待当前搜索完成，随后停止服务内的自动索引";Changed?.Invoke();await Task.Delay(100);}
                if((!configuration&&!StopRequired(timed))||exiting)return;
                foreach(var p in shared)
                {
                    Native.Stop(p);var name=Native.WorkspaceName(p);resumeSync.Add(name);ports.Remove(name);
                    external.RemoveAll(x=>x.Id==p.Id);
                    var ws=Workspaces.FirstOrDefault(w=>w.Name==name);if(ws!=null&&!exiting&&!configuration)await Port(ws);
                }
            }
            external.RemoveAll(p=>!Native.Alive(p));
            Message=configuration?"正在应用目录变更":timed?(sessionStopped?"索引已暂停至下次启动，搜索可用":"索引已暂停，搜索可用"):"使用电池，自动同步已停止，搜索可用";
        }
        finally{draining=false;pauseTransition=false;forcePause=false;ObserveIndexPause();Changed?.Invoke();}
    }
    public void Continue(){var old=Prefs.PauseUntil;bool stopped=sessionStopped;Prefs.PauseUntil=null;sessionStopped=false;try{Save();}catch{Prefs.PauseUntil=old;sessionStopped=stopped;throw;}ResetPausedIndexMemory();Message=IndexPolicyReason.Length>0?IndexPolicyReason:"已恢复索引，按同步设置运行";}
    public void AbortExit(){exiting=false;draining=false;}
    public void Resumed()
    {
        ResetMemoryPolicy();
        if(!Prefs.ResumeMissed){due.Clear();foreach(var ws in Workspaces)resumeNotBefore[ws.Name]=DateTimeOffset.UtcNow.AddMinutes(Prefs.IntervalMinutes);}
    }
    async Task VerifyStorage()
    {
        storageIssue="";var moves=new List<(Workspace Ws,Root Root,string Old,string New,VolumeBinding Binding)>();
        var original=new Dictionary<string,VolumeBinding>(Prefs.Volumes,StringComparer.OrdinalIgnoreCase);
        foreach(var ws in Workspaces)foreach(var root in ws.Roots)
        {
            if(!original.TryGetValue(root.Path,out var binding))continue;
            if(Native.Identify(root.Path)?.Volume==binding.Volume&&Directory.Exists(root.Path))continue;
            var affected=external.Where(p=>(Native.IsIndex(p)||Native.AutoSync(p))&&Native.WorkspaceName(p)==ws.Name).ToList();
            if(activeWorkspace==ws.Name&&indexProcess is{HasExited:false})indexProcess.Kill(true);
            foreach(var proc in affected)try{Native.Stop(proc);}catch(ArgumentException){}
            if(affected.Count>0)external=external.Except(affected).ToList();
            if(Busy){storageIssue="目录离线或盘符变化，等待相关任务退出";continue;}
            var relocated=Native.FindVolume(binding);
            if(relocated!=null&&Directory.Exists(relocated)&&!Rules.Normalize(relocated).Equals(Rules.Normalize(root.Path),StringComparison.OrdinalIgnoreCase))moves.Add((ws,root,root.Path,relocated,binding));
            else storageIssue="部分根目录离线或身份不匹配，自动索引暂缓";
        }
        if(moves.Count==0)return;
        var journal=System.IO.Path.Combine(store.DataDirectory,"relocation-journal.json");
        Store.Atomic(journal,JsonSerializer.Serialize(moves.Select(m=>new{workspace=m.Ws.Name,old=m.Old,path=m.New,binding=m.Binding})));
        foreach(var move in moves)move.Root.Path=move.New;
        foreach(var ws in moves.Select(m=>m.Ws).Distinct())store.SaveWorkspace(ws);
        foreach(var move in moves)Prefs.Volumes.Remove(move.Old);
        foreach(var move in moves)Prefs.Volumes[move.New]=move.Binding;
        Save();File.Delete(journal);Generation++;Message="已核对原卷并更新盘符";Changed?.Invoke();await Task.CompletedTask;
    }
    public async Task Pulse()
    {
        if(pulseBusy||configurationChanging||exiting||!initialized)return;pulseBusy=true;
        try
        {
            if(DateTimeOffset.UtcNow-lastInspect>TimeSpan.FromSeconds(20))
            {external=await Inspect();lastInspect=DateTimeOffset.UtcNow;}
            if(pauseTransition)return;
            if(Paused&&(Indexing||forcePause||BackendInstances.Writers(store,Workspaces).Any(w=>w.Pid is int pid&&external.Any(p=>p.Id==pid&&Native.IsServer(p)))))await ApplyPause();
            else if(!Paused&&Prefs.BatteryBlocksAuto&&Prefs.BatteryStopsAuto&&PowerSource()==false&&((indexProcess is {HasExited:false}&&!activeIndexManual)||external.Any(SharedAuto)))await ApplyIndexStop(false);
            if(pauseTransition)return;
            await VerifyStorage();
            ApplyPriority();
            if(Prefs.PauseUntil!=null&&!TimedPaused){Prefs.PauseUntil=null;Save();}
            if(!Paused && external.Any(SharedAuto) && !external.Any(Native.IsIndex))
            {
                Message="正在接管自动同步服务";
                // Legacy service may be indexing in-process: do not terminate while it owns the writer lock.
                if(!HasWriterLock())
                {
                    var legacy=external.Where(SharedAuto).ToList();if(runningQueries==0&&activeRpc==0){foreach(var p in legacy){Native.Stop(p);var ws=Native.WorkspaceName(p);if(ports.GetValueOrDefault(ws)==Native.ServerPort(p))ports.Remove(ws);}external=external.Except(legacy).ToList();}
                }
                else Message="等待当前任务完成后接管";
            }
            foreach(var ws in Workspaces.Where(w=>w.Roots.Count>0))
            {
                try
                {
                    var data=await Rpc(ws,"status"); long docs=0,ready=0,pending=0,failed=0,chunks=0,embeds=0;
                    foreach(var p in data.GetProperty("projects").EnumerateArray())
                    {if(!p.TryGetProperty("stats",out var s)||s.ValueKind==JsonValueKind.Null)continue;
                        long N(string key)=>s.GetProperty(key).GetInt64();docs+=N("documents");ready+=N("ready");pending+=N("pending");failed+=N("failed");chunks+=N("chunks");embeds+=N("embeddings");rootCounts[ws.Name+"/"+p.GetProperty("name").GetString()]=N("ready");rootProgress[ws.Name+"/"+p.GetProperty("name").GetString()]=new(N("documents"),s.TryGetProperty("lexical",out var lex)?lex.GetInt64():N("ready"),N("failed"),N("chunks"),s.TryGetProperty("vectors",out var vec)?vec.GetInt64():N("embeddings"),DateTimeOffset.UtcNow);}
                    counts[ws.Name]=new(docs,ready,pending,failed,chunks,embeds);
                }catch(HttpRequestException ex){ports.Remove(ws.Name);Message=ws+": "+ex.Message;}
                catch(Exception ex){Message=ws+": "+ex.Message;}
                if(Prefs.PeriodicSync && DateTimeOffset.UtcNow>=resumeNotBefore.GetValueOrDefault(ws.Name,DateTimeOffset.MinValue) && (!Prefs.LastFinished.TryGetValue(ws.Name,out var finish)||DateTimeOffset.UtcNow-finish>=TimeSpan.FromMinutes(Prefs.IntervalMinutes)))due.Add(ws.Name);
            }
            Progress=$"{counts.Values.Sum(x=>x.Ready)}/{counts.Values.Sum(x=>x.Documents)}";
            var active=activeWorkspace??external.Where(Native.IsIndex).Select(Native.WorkspaceName).FirstOrDefault();
            if(active!=null&&counts.TryGetValue(active,out var activeCounts))Progress=(Workspaces.FirstOrDefault(w=>w.Name==active)?.ToString()??"当前范围")+" · "+activeCounts.Ready+"/"+activeCounts.Documents;
            if(Paused) Message=pauseTransition?Message:IndexPolicyReason;
            else if(!Indexing)Message=Prefs.BatteryBlocksAuto&&PowerSource()!=true?"电池供电或供电状态未知，自动同步暂缓":Prefs.LowMemoryBlocksAuto&&!Native.EnoughMemory?"内存不足，自动同步暂缓":"就绪";
            else if(indexProcess==null)Message=external.Any(Native.AutoSync)?"等待当前任务完成后接管":"正在更新索引";
            if(storageIssue!="")Message=storageIssue;
            if(InstanceConflict.Length>0)Message=InstanceConflict.Split('\n')[0]+" · 索引暂缓";
            if(!Prefs.StartupSync&&!Prefs.PeriodicSync) due.Clear();
            if(InstanceConflict.Length==0&&!Paused&&!Busy&&!configurationChanging&&!File.Exists(PendingPath)&&!draining&&!manualBatch&&AutomaticAllowed(Prefs,DateTimeOffset.UtcNow,PowerSource()==true,Native.EnoughMemory))
            {var ws=Workspaces.FirstOrDefault(w=>(due.Contains(w.Name)||resumeSync.Contains(w.Name))&&w.Roots.Count>0&&DateTimeOffset.UtcNow>=retryAfter.GetValueOrDefault(w.Name,DateTimeOffset.MinValue));if(ws!=null)_=RunAuto(ws);}
            await ReclaimIdleModelMemory();
            Changed?.Invoke();
        }
        finally{pulseBusy=false;}
    }
    async Task RunAuto(Workspace ws){try{await RunSync(ws);}catch(Exception ex){retryAfter[ws.Name]=DateTimeOffset.UtcNow.AddMinutes(5);Message=ex.Message;Changed?.Invoke();}}
    bool HasWriterLock()
        =>BackendInstances.Writers(store,Workspaces).Count>0;
    public async Task StopAll()
    {
        using var memoryUse=await UseModelResources();
        exiting=true;draining=true;
        var processes=await Inspect();foreach(var p in processes)try{Native.Stop(p);}catch(ArgumentException){}
    }
    public void Dispose(){http.Dispose();}
}
public sealed class PartialSearchException : Exception
{
    public List<Hit> Hits {get;}
    public List<string> CompletedRoots {get;}
    public PartialSearchException(string message,List<Hit> hits,List<string> roots):base(message){Hits=hits;CompletedRoots=roots;}
}
