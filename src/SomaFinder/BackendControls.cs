// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

public sealed partial class Backend
{
    bool sessionStopped,configurationChanging;
    readonly HashSet<string> resumeSync=new();
    string PendingPath=>System.IO.Path.Combine(store.DataDirectory,"pending-settings.json");
    public bool SessionStopped=>sessionStopped;
    public bool TimedPaused=>Prefs.IsPaused(DateTimeOffset.UtcNow);
    public bool ChangingConfiguration=>configurationChanging;
    public bool CanResume=>sessionStopped||TimedPaused;
    internal IndexTrayState TrayState=>IndexTrayState.Evaluate(Busy,pauseTransition,sessionStopped,Prefs.PauseUntil,DateTimeOffset.UtcNow,Prefs.BatteryBlocksAuto&&PowerSource()!=true,Prefs.LowMemoryBlocksAuto&&!Native.EnoughMemory,Message);
    public async Task StopSession()
    {
        sessionStopped=true;Message="正在停止索引…";Changed?.Invoke();
        while(pauseTransition)await Task.Delay(30);await ApplyIndexStop(true);
    }
    public string IndexPolicyReason=>sessionStopped?"索引已暂停至下次启动":TimedPaused?"索引已暂停 · 剩余 "+Rules.Remaining(Prefs.PauseUntil!.Value-DateTimeOffset.UtcNow):Prefs.BatteryBlocksAuto&&PowerSource()!=true?"电池供电或供电状态未知，自动同步暂缓":Prefs.LowMemoryBlocksAuto&&!Native.EnoughMemory?"内存不足，自动同步暂缓":"";
    public sealed class PendingSettings
    {
        public List<Workspace> Workspaces {get;set;}=new();
        public Preferences Preferences {get;set;}=new();
    }
    void AdoptPreferences(Preferences value)
    {
        foreach(var p in typeof(Preferences).GetProperties().Where(p=>p.CanWrite))p.SetValue(Prefs,p.GetValue(value));
    }
    public async Task SaveConfiguration(List<Workspace> changed,Preferences preferences)
    {
        if(configurationChanging)throw new IOException("正在应用目录变更，请稍候。");
        ValidateExecutableChange(preferences.SomaExe);
        if(InstanceConflict.Length>0&&changed.Count>0)throw new IOException(InstanceConflict);
        if(File.Exists(PendingPath))throw new IOException("存在尚未应用的已保存设置，请重新启动程序完成应用。");
        foreach(var ws in changed)if(Store.Digest(ws.ConfigPath)!=ws.Hash)throw new IOException("工作区配置已被其他程序修改，请重新打开设置。");
        if(changed.Count==0){ChangeExecutable(preferences.SomaExe);store.Save(preferences);AdoptPreferences(preferences);Save();return;}
        Store.Atomic(PendingPath,JsonSerializer.Serialize(new PendingSettings{Workspaces=changed,Preferences=preferences}));
        await ApplyPendingConfiguration(false);
    }
    async Task ApplyPendingConfiguration(bool coldStart)
    {
        if(!File.Exists(PendingPath))return;
        configurationChanging=true;Changed?.Invoke();
        try
        {
            var pending=JsonSerializer.Deserialize<PendingSettings>(File.ReadAllText(PendingPath))??throw new IOException("已保存目录变更无法读取。");
            var names=pending.Workspaces.Select(w=>w.Name).ToHashSet();
            // Preflight every file before stopping or writing anything; never overwrite an external edit.
            foreach(var ws in pending.Workspaces)
            {
                if(!Native.SameFilePath(ws.ConfigPath,System.IO.Path.Combine(store.ConfigDirectory,ws.Name+".yml")))throw new IOException("目录变更的配置位置不匹配。");
                if(Store.Digest(ws.ConfigPath)!=ws.Hash)
                {
                    if(!File.Exists(ws.ConfigPath)||JsonSerializer.Serialize(store.ReadWorkspace(ws.ConfigPath).Roots)!=JsonSerializer.Serialize(ws.Roots))throw new IOException("目录变更应用前，工作区已在外部修改；请检查 pending-settings.json 和配置备份。");
                    ws.Hash=Store.Digest(ws.ConfigPath);
                }
            }
            Message="设置已保存，正在应用目录变更…";Changed?.Invoke();
            await ApplyIndexStop(true,names,true);
            var deadline=DateTimeOffset.UtcNow.AddSeconds(15);
            while(activeWorkspace!=null&&names.Contains(activeWorkspace)&&indexing.CurrentCount==0)
            {if(DateTimeOffset.UtcNow>deadline)throw new IOException("等待索引任务释放资源超时；已保存的变更将在下次启动时重试。");await Task.Delay(30);}
            if(BackendInstances.Writers(store,Workspaces.Where(w=>names.Contains(w.Name))).Count>0)throw new IOException("相关工作区仍有写入锁，已保留设置等待下次应用。");
            // Drain reads before refreshing affected search services, including services caching old roots.
            draining=true;
            while((runningQueries>0||activeRpc>0)&&!exiting){Message="设置已保存，等待当前搜索完成后应用目录变更";Changed?.Invoke();await Task.Delay(80);}
            if(exiting)throw new IOException("程序正在退出；目录变更已保存。");
            var processes=Owned().Concat(external.Where(Native.Alive)).DistinctBy(p=>p.Id).Where(p=>Native.IsServer(p)&&names.Contains(Native.WorkspaceName(p))).ToList();
            foreach(var p in processes){Native.Stop(p);ports.Remove(Native.WorkspaceName(p));external.RemoveAll(x=>x.Id==p.Id);}
            var originals=pending.Workspaces.ToDictionary(w=>w.ConfigPath,w=>File.Exists(w.ConfigPath)?File.ReadAllText(w.ConfigPath):null);
            try
            {
                foreach(var ws in pending.Workspaces)store.SaveWorkspace(ws);
                ChangeExecutable(pending.Preferences.SomaExe);
                if(!coldStart){pending.Preferences.PauseUntil=Prefs.PauseUntil;pending.Preferences.LastFinished=Prefs.LastFinished;}
                store.Save(pending.Preferences);AdoptPreferences(pending.Preferences);
            }
            catch
            {
                foreach(var ws in pending.Workspaces)if(Store.Digest(ws.ConfigPath)==ws.Hash){if(originals[ws.ConfigPath] is string original)Store.Atomic(ws.ConfigPath,original);else if(File.Exists(ws.ConfigPath))File.Delete(ws.ConfigPath);}
                throw;
            }
            File.Delete(PendingPath);Reload();fileLists.Clear();
            if(coldStart)resumeSync.Clear();
            Message="设置已保存";
        }
        finally{draining=false;configurationChanging=false;Changed?.Invoke();}
    }
}
