// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SomaFinder;

public static class RootGrouping
{
    public static Workspace Choose(List<Workspace> workspaces,Dictionary<string,string> disks,Dictionary<string,string> groups,Func<Root,string?> rootDisk,string key,string configDirectory)
    {
        if(groups.TryGetValue(key,out var name)&&workspaces.FirstOrDefault(w=>w.Name==name) is Workspace mapped&&mapped.Roots.All(r=>rootDisk(r)==key))return mapped;
        // Preserve legacy databases and root identifiers. Never move or merge indexed roots.
        var existing=workspaces.Where(w=>w.Roots.Count>0&&w.Roots.All(r=>rootDisk(r)==key)).OrderByDescending(w=>w.Roots.Count).ThenBy(w=>w.Name,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if(existing!=null){groups[key]=existing.Name;return existing;}
        var id="finder-"+Guid.NewGuid().ToString("N")[..12];var created=new Workspace{Name=id,ConfigPath=System.IO.Path.Combine(configDirectory,id+".yml")};workspaces.Add(created);groups[key]=id;return created;
    }
    public static string ProgressText(RootIndexProgress? progress)
    {
        if(progress==null)return "关键词：待获取\n语义：待获取";
        var p=progress;string semantic=p.Chunks==0?"待建立":$"{p.Vectors}/{p.Chunks} 分块";
        return $"关键词：{p.Lexical}/{p.Documents} 文件 · 提取失败 {p.Failed}\n语义：{semantic} · 失败 —";
    }
    public static string? KnownDisk(Preferences prefs,Root root)
    {var volume=prefs.Volumes.GetValueOrDefault(root.Path)?.Volume;return volume!=null&&prefs.VolumeDisks.TryGetValue(volume,out var key)?key:null;}
    public static string DiskKey(VolumeBinding binding)
    {
        // A volume GUID remains safe when a USB bridge supplies no trustworthy device serial.
        var fallback="volume:"+binding.Volume;
        try
        {
            using var handle=OpenVolume(binding.Volume.TrimEnd('\\'),0,7,IntPtr.Zero,3,0,IntPtr.Zero);
            if(handle.IsInvalid)return fallback;
            var query=new byte[12];var buffer=new byte[4096];
            if(!QueryDevice(handle,0x002D1400,query,query.Length,buffer,buffer.Length,out int size,IntPtr.Zero)||size<36)return fallback;
            string At(int offset){int start=BitConverter.ToInt32(buffer,offset);if(start<=0||start>=size)return "";int end=Array.IndexOf(buffer,(byte)0,start,size-start);return Encoding.ASCII.GetString(buffer,start,(end<0?size:end)-start).Trim();}
            var serial=At(24);if(serial.Length<4||serial.Distinct().Count()<3)return fallback;
            var identity=At(12)+"|"+At(16)+"|"+serial;
            return "device:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        }
        catch{return fallback;}
    }
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)] static extern Microsoft.Win32.SafeHandles.SafeFileHandle OpenVolume(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",EntryPoint="DeviceIoControl",SetLastError=true)] static extern bool QueryDevice(Microsoft.Win32.SafeHandles.SafeFileHandle handle,uint code,byte[] input,int inSize,byte[] output,int outSize,out int bytes,IntPtr overlapped);
}
