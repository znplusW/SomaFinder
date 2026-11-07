// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

public sealed partial class Backend
{
    readonly object memorySync=new();
    readonly ModelMemoryPolicy memoryPolicy=new();
    readonly PausedIndexModelPolicy pausedIndexMemoryPolicy=new();
    int memoryUsers;
    bool stopMemoryReclaim;
    TaskCompletionSource? memoryReclaimFinished;
    Dictionary<int,long> memoryWorkerTimes=new();
    internal Func<long> MemoryClock {get;set;}=()=>Environment.TickCount64;
    internal Func<ulong?> AvailableMemory {get;set;}=()=>Native.AvailablePhysicalMemory;
    public string ModelMemoryStatus {get;private set;}="";
    internal object MemorySnapshot=>new{now=MemoryClock(),available=AvailableMemory(),memoryUsers,busy=MemoryBusy,InstanceConflict,policy=memoryPolicy.Snapshot,pausedPolicy=pausedIndexMemoryPolicy.Snapshot,owners=Owned().Select(p=>new{p.Id,p.File,args=Native.Arguments(p)}).ToArray()};

    // Register before waiting, including queued requests. Keep this lease until the
    // real request finishes; cancelling a window's wait must never make it idle.
    async Task<IDisposable> UseModelResources()
    {
        Task wait;
        lock(memorySync)
        {memoryUsers++;memoryPolicy.Activity();stopMemoryReclaim=true;wait=memoryReclaimFinished?.Task??Task.CompletedTask;}
        await wait;
        return new MemoryUse(this);
    }
    sealed class MemoryUse:IDisposable
    {
        Backend? backend;
        internal MemoryUse(Backend value){backend=value;}
        public void Dispose()
        {
            var b=Interlocked.Exchange(ref backend,null);if(b==null)return;
            lock(b.memorySync){b.memoryUsers--;b.memoryPolicy.Activity();}
        }
    }
    bool MemoryBusy=>Volatile.Read(ref memoryUsers)>0||Busy||runningQueries>0||activeRpc>0||draining||pauseTransition||configurationChanging||exiting||!initialized||InstanceConflict.Length>0||File.Exists(PendingPath)||BackendInstances.Writers(store,Workspaces).Count>0;
    bool IndexPauseConfirmed=>Paused&&!Busy&&!pauseTransition&&!configurationChanging&&!draining&&!exiting&&initialized&&InstanceConflict.Length==0&&!File.Exists(PendingPath)&&!HasWriterLock();
    void ResetPausedIndexMemory(){lock(memorySync){pausedIndexMemoryPolicy.Reset();stopMemoryReclaim=true;}}
    void ObserveIndexPause(){lock(memorySync)pausedIndexMemoryPolicy.Observe(Prefs.ReleaseIndexModelsWhenPaused,IndexPauseConfirmed,MemoryClock());}
    void ResetMemoryPolicy(){lock(memorySync){memoryPolicy.Reset();pausedIndexMemoryPolicy.Reset();memoryWorkerTimes.Clear();stopMemoryReclaim=true;}}
    bool ModelWorkersChanged()
    {
        var observed=new Dictionary<int,long>();
        foreach(var owner in Owned().Where(Native.IsServer))
            foreach(var worker in Native.Descendants(owner).Where(p=>p.ParentId!=owner.Id&&System.IO.Path.GetFileName(p.File).Equals("llama-server.exe",StringComparison.OrdinalIgnoreCase)))
            {using var process=Process.GetProcessById(worker.Id);observed[worker.Id]=process.TotalProcessorTime.Ticks;}
        bool changed=observed.Count!=memoryWorkerTimes.Count||observed.Any(p=>!memoryWorkerTimes.TryGetValue(p.Key,out var time)||time!=p.Value);
        memoryWorkerTimes=observed;return changed;
    }
    bool CanContinueMemoryReclaim(bool indexOnly)
    {
        lock(memorySync)return !stopMemoryReclaim&&!MemoryBusy&&(indexOnly
            ?Prefs.ReleaseIndexModelsWhenPaused&&IndexPauseConfirmed&&pausedIndexMemoryPolicy.Ready(MemoryClock())
            :Prefs.ReleaseIdleModelsOnLowMemory&&AvailableMemory() is ulong bytes&&bytes<ModelMemoryPolicy.LowMemoryBytes);
    }
    async Task ReclaimIdleModelMemory()
    {
        bool pausedReady,lowReady=false;
        lock(memorySync)pausedReady=pausedIndexMemoryPolicy.Observe(Prefs.ReleaseIndexModelsWhenPaused,IndexPauseConfirmed,MemoryClock());
        if(Prefs.ReleaseIdleModelsOnLowMemory)
        {
            bool workersChanged;
            try{workersChanged=ModelWorkersChanged();}
            catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException){workersChanged=true;}
            lock(memorySync)lowReady=memoryPolicy.Observe(true,AvailableMemory(),MemoryBusy||workersChanged,MemoryClock());
        }
        else lock(memorySync){memoryPolicy.Reset();memoryWorkerTimes.Clear();}
        if(!lowReady&&!pausedReady)return;
        bool indexOnly=!lowReady;
        bool Allowed()=>CanContinueMemoryReclaim(indexOnly);
        lock(memorySync)
        {
            if(memoryReclaimFinished!=null||MemoryBusy)return;
            memoryReclaimFinished=new(TaskCreationOptions.RunContinuationsAsynchronously);stopMemoryReclaim=false;
            if(indexOnly)pausedIndexMemoryPolicy.Attempted(MemoryClock());else memoryPolicy.Attempted(MemoryClock());
        }
        int released=0;
        try
        {
            // Re-read process identities after the selected timer(s) expire. Do not act on
            // a borrowed server, another backend installation, or an active writer.
            var processes=await Native.BackendProcesses(new[]{Prefs.SomaExe}.Concat(Prefs.KnownBackendPaths));
            if(processes.Any(p=>(Native.IsIndex(p)||Native.AutoSync(p))&&Native.Alive(p))||!Allowed())return;
            var owners=Owned().Where(Native.IsServer).Where(p=>processes.Any(x=>x.Id==p.Id&&Native.SameFilePath(x.File,p.File)&&Native.WorkspaceName(x)==Native.WorkspaceName(p)&&!Native.AutoSync(x))).ToArray();
            foreach(var owner in owners)
            {
                if(!Allowed())break;
                foreach(var runtime in ModelRuntime.Find(owner))
                {
                    if(!Allowed())break;
                    ModelMemoryStatus="正在释放空闲模型内存…";Changed?.Invoke();
                    released+=await runtime.ReleaseIdleModels(Allowed,indexOnly);
                    if(runtime.WasBusy){lock(memorySync)memoryPolicy.Activity();}
                }
                if(indexOnly&&Allowed())released+=await ModelRuntime.ReleaseOcrService(owner,Allowed);
            }
            if(released>0)ModelMemoryStatus=indexOnly?$"已卸载 {released} 个索引专用模型或组件；恢复索引后按需加载。":$"已卸载 {released} 个空闲模型；下次需要时自动加载。";
        }
        catch(Exception ex)when(ex is IOException or System.Net.Http.HttpRequestException or TaskCanceledException or TimeoutException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {ModelMemoryStatus="模型内存回收暂缓："+ex.Message;}
        finally
        {
            TaskCompletionSource? done;
            lock(memorySync){done=memoryReclaimFinished;memoryReclaimFinished=null;}
            if(ModelMemoryStatus=="正在释放空闲模型内存…")ModelMemoryStatus=released>0?$"已卸载 {released} 个空闲模型；下次需要时自动加载。":"模型仍在使用或无法确认空闲，已暂缓回收。";
            done?.TrySetResult();Changed?.Invoke();
        }
    }
}
