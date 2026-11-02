// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text.RegularExpressions;

namespace SomaFinder;
public static class Native
{
    public static bool SameFilePath(string first,string second)
    {
        if(string.IsNullOrWhiteSpace(first)||string.IsNullOrWhiteSpace(second))return false;
        try{return Rules.Normalize(first).Equals(Rules.Normalize(second),StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}catch(NotSupportedException){return false;}
    }
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetFileInformationByHandleEx(SafeFileHandle file,int kind,byte[] info,uint size);
    public static bool SameStorageFile(string first,string second)
    {
        if(SameFilePath(first,second))return true;
        if(string.IsNullOrWhiteSpace(first)||string.IsNullOrWhiteSpace(second))return false;
        using var left=CreateFile(first,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);using var right=CreateFile(second,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        var a=new byte[24];var b=new byte[24];return !left.IsInvalid&&!right.IsInvalid&&GetFileInformationByHandleEx(left,18,a,24)&&GetFileInformationByHandleEx(right,18,b,24)&&a.SequenceEqual(b);
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool GetVolumePathName(string file, StringBuilder root, int size);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder name, int size);
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus status);
    [StructLayout(LayoutKind.Sequential)] struct PowerStatus { public byte AC, Battery, Percent, Reserved; public uint Life, FullLife; }
    [DllImport("kernel32.dll", CharSet=CharSet.Auto, SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length, Load; public ulong Total, Available, Page, FreePage, Virtual, FreeVirtual, Extended; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
    public static bool? ACPower => GetSystemPowerStatus(out var p)&&p.AC is 0 or 1?p.AC==1:null;
    public static bool OnAC => ACPower==true;
    public static bool EnoughMemory
    {
        get { var s = new MemoryStatus { Length=(uint)Marshal.SizeOf<MemoryStatus>() }; return GlobalMemoryStatusEx(ref s) && s.Available >= Math.Max(2UL*1024*1024*1024, s.Total/10); }
    }
    public static VolumeBinding? Identify(string path)
    {
        var root = new StringBuilder(1024); var volume = new StringBuilder(1024);
        if (!GetVolumePathName(path, root, root.Capacity) || !GetVolumeNameForVolumeMountPoint(root.ToString(), volume, volume.Capacity)) return null;
        string disk="unknown";
        using(var handle=CreateFile(volume.ToString().TrimEnd('\\'),0,3,IntPtr.Zero,3,0,IntPtr.Zero))
        {
            var buf=new byte[1024];
            if(!handle.IsInvalid && DeviceIoControl(handle,0x00560000,IntPtr.Zero,0,buf,(uint)buf.Length,out var count,IntPtr.Zero) && count>=12)
                disk="disk:"+BitConverter.ToUInt32(buf,8);
        }
        return new(volume.ToString(), System.IO.Path.GetRelativePath(root.ToString(),path),disk);
    }
    public static string? FindVolume(VolumeBinding binding)
    {
        var matches=DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>(Drive:d,Id:Identify(d.RootDirectory.FullName))).Where(x=>x.Id?.Volume==binding.Volume).ToList();
        return matches.Count==1 ? System.IO.Path.Combine(matches[0].Drive.RootDirectory.FullName,binding.RelativePath) : null;
    }
    public static bool Verified(string path, Preferences prefs)
    {
        var key=prefs.Volumes.Keys.Where(k=>Rules.Contains(k,path)).OrderByDescending(k=>k.Length).FirstOrDefault();
        if(key is null) return false;
        return Identify(path)?.Volume == prefs.Volumes[key].Volume;
    }
    public sealed record Proc(int Id, int ParentId, string File, string Command,long StartTicks=0,string[]? KnownArguments=null);
    public static async Task<List<Proc>> SomaProcesses(string exe)
        =>(await BackendProcesses(new[]{exe})).Where(p=>SameFilePath(p.File,exe)).ToList();
    public static async Task<List<Proc>> BackendProcesses(IEnumerable<string> knownPaths,IEnumerable<int>? ownerIds=null)
    {
        // Include known renamed executables and the owner of an actual workspace write lock.
        // A Finder process or a model worker is not itself another Soma installation.
        var psi=new ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8 };
        // Filter absent PATH candidates before the bounded environment payload, so a long
        // PATH cannot push an explicitly selected renamed executable out of the query.
        psi.Environment["SOMAFINDER_KNOWN_PATHS"]=JsonSerializer.Serialize(knownPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray());
        psi.Environment["SOMAFINDER_OWNER_IDS"]=JsonSerializer.Serialize((ownerIds??Array.Empty<int>()).ToArray());
        foreach(var a in new[]{"-NoProfile","-NonInteractive","-Command","[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false);$known=ConvertFrom-Json $env:SOMAFINDER_KNOWN_PATHS;$owners=ConvertFrom-Json $env:SOMAFINDER_OWNER_IDS;Get-CimInstance Win32_Process | Where-Object {$_.Name -match '^soma(?:[._-].*)?\\.exe$' -or $known -contains $_.ExecutablePath -or $owners -contains $_.ProcessId} | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine,@{Name='StartTicks';Expression={$_.CreationDate.ToUniversalTime().Ticks}} | ConvertTo-Json -Compress"}) psi.ArgumentList.Add(a);
        using var p=Process.Start(psi)!; var outputTask=p.StandardOutput.ReadToEndAsync(); var errors=p.StandardError.ReadToEndAsync();
        try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12));}catch{try{p.Kill(true);}catch{}throw;}
        var output=await outputTask; await errors;
        if(p.ExitCode!=0) throw new IOException("无法核对 Soma 进程归属，暂缓索引接管。");
        if(string.IsNullOrWhiteSpace(output)) return new();
        using var doc=JsonDocument.Parse(output); var nodes=doc.RootElement.ValueKind==JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : new[]{doc.RootElement};
        if(nodes.Any(x=>x.GetProperty("ExecutablePath").ValueKind!=JsonValueKind.String||x.GetProperty("CommandLine").ValueKind!=JsonValueKind.String))throw new IOException("部分 Soma 进程的归属无法读取，自动索引暂缓。请关闭其他实例后重新检测。");
        return nodes.Where(x=>x.GetProperty("ExecutablePath").GetString() is string file&&!string.IsNullOrWhiteSpace(file))
            .Select(x=>new Proc(x.GetProperty("ProcessId").GetInt32(),x.GetProperty("ParentProcessId").GetInt32(),x.GetProperty("ExecutablePath").GetString()!,x.GetProperty("CommandLine").GetString() ?? "",x.GetProperty("StartTicks").GetInt64())).ToList();
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CommandLineToArgvW(string command,out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static string[] Arguments(Proc p)
    {
        if(p.KnownArguments!=null)return p.KnownArguments;
        if(string.IsNullOrWhiteSpace(p.Command))return Array.Empty<string>();var memory=CommandLineToArgvW(p.Command,out var count);
        if(memory==IntPtr.Zero)throw new IOException("无法解析 Soma 进程参数。");
        try{return Enumerable.Range(1,Math.Max(0,count-1)).Select(i=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory,i*IntPtr.Size))??"").ToArray();}finally{LocalFree(memory);}
    }
    public static string WorkspaceName(Proc p)
    {
        var args=Arguments(p);for(int i=0;i<args.Length;i++){if(args[i]=="--workspace"||args[i]=="-w"){if(i+1<args.Length)return args[i+1];}else if(args[i].StartsWith("--workspace="))return args[i][12..];}return "main";
    }
    static string[] Verbs(Proc p)
    {
        var args=Arguments(p);for(int i=0;i<args.Length;i++){if(args[i]=="--workspace"||args[i]=="-w"){i++;continue;}if(args[i].StartsWith('-'))continue;return args[i..];}return Array.Empty<string>();
    }
    public static bool IsServer(Proc p)=>Verbs(p).FirstOrDefault()=="server";
    public static bool AutoSync(Proc p)=>Arguments(p).Any(a=>a=="--auto-sync"||a.Equals("--auto-sync=true",StringComparison.OrdinalIgnoreCase));
    public static int ServerPort(Proc p)
    {
        var args=Arguments(p);for(int i=0;i<args.Length;i++){if(args[i].StartsWith("--port=")&&int.TryParse(args[i][7..],out var port))return port;if(args[i]=="--port"&&i+1<args.Length&&int.TryParse(args[i+1],out port))return port;}return 8181;
    }
    public static bool IsIndex(Proc p){var args=Verbs(p);return args.FirstOrDefault()=="sync"||(args.Length>1&&args[0]=="system"&&new[]{"scan","extract","embed","clean"}.Contains(args[1]));}
    public static bool Alive(Proc proc)
    {
        try{using var p=Process.GetProcessById(proc.Id);return !p.HasExited&&(proc.StartTicks==0||Math.Abs(p.StartTime.ToUniversalTime().Ticks-proc.StartTicks)<=TimeSpan.TicksPerSecond);}
        catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch{return true;}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessEntry
    {public uint Size,Usage,Id;public UIntPtr Heap;public uint Module,Threads,Parent;public int BasePriority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;}
    [DllImport("kernel32.dll",SetLastError=true)]static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags,uint id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern bool Process32FirstW(SafeFileHandle snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern bool Process32NextW(SafeFileHandle snapshot,ref ProcessEntry entry);
    public static List<Proc> Descendants(Proc owner)
    {
        var result=new List<Proc>();if(!Alive(owner))return result;
        using var snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot.IsInvalid)throw new IOException("无法核对索引子进程归属。");
        var links=new Dictionary<int,int>();var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>(),Name=""};
        if(Process32FirstW(snapshot,ref entry))do{links[(int)entry.Id]=(int)entry.Parent;}while(Process32NextW(snapshot,ref entry));
        var owned=new HashSet<int>{owner.Id};bool added;
        do{added=false;foreach(var pair in links)if(owned.Contains(pair.Value)&&owned.Add(pair.Key))added=true;}while(added);
        foreach(var id in owned.Where(i=>i!=owner.Id))try{using var process=Process.GetProcessById(id);var file=process.MainModule?.FileName;if(file!=null&&!process.HasExited)result.Add(new(id,links[id],file,"",process.StartTime.ToUniversalTime().Ticks));}catch(ArgumentException){}catch(InvalidOperationException){}catch(Win32Exception){}
        return result;
    }
    public static void Stop(Proc proc)
    {
        using var p=Process.GetProcessById(proc.Id);
        if(p.HasExited)return;
        if(p.MainModule?.FileName is not string image||!SameFilePath(image,proc.File)) throw new IOException("进程身份已变化，已取消终止。");
        if(proc.StartTicks!=0&&Math.Abs(p.StartTime.ToUniversalTime().Ticks-proc.StartTicks)>TimeSpan.TicksPerSecond)throw new IOException("进程创建时间已变化，已取消终止。");
        try{p.Kill(true);}catch(InvalidOperationException)when(p.HasExited){}p.WaitForExit(10000);
        if(!p.HasExited) throw new IOException("Soma 仍未退出。");
    }
    public static void BindLifetime(Process p)
    {
        // One kill-on-close job per managed root. Handles remain open until application termination.
        var job=CreateJobObject(IntPtr.Zero,null); if(job==IntPtr.Zero) throw new IOException("无法创建 Soma 进程生命周期组。");
        var info=new JobExtended(); info.Basic.LimitFlags=0x2000;
        var ptr=Marshal.AllocHGlobal(Marshal.SizeOf<JobExtended>());
        try { Marshal.StructureToPtr(info,ptr,false); if(!SetInformationJobObject(job,9,ptr,(uint)Marshal.SizeOf<JobExtended>()) || !AssignProcessToJobObject(job,p.Handle)) { CloseHandle(job); throw new IOException("无法绑定 Soma 进程生命周期。"); } Jobs.Add(job); }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    static readonly List<IntPtr> Jobs=new();
    [StructLayout(LayoutKind.Sequential)] struct JobBasic { public long ProcessTime,JobTime; public uint LimitFlags; public UIntPtr Min,Max; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong Read,Write,Other,ReadBytes,WriteBytes,OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct JobExtended { public JobBasic Basic; public IoCounters Io; public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr security,string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job,int info,IntPtr value,uint size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
