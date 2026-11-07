// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SomaFinder;

// Add the backend to a kill-on-close job atomically during CreateProcess. Only
// explicitly listed standard handles are inherited; the lifetime job is not.
internal static class ManagedProcess
{
    internal sealed record Started(Process Process,StreamReader Output,StreamReader Error);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int,nint> jobs=new();
    internal static Started Start(ProcessStartInfo psi)
    {
        nint job=0,attributes=0,jobList=0,handles=0,environment=0,outRead=0,outWrite=0,errRead=0,errWrite=0,input=0;
        var security=new Security{Size=Marshal.SizeOf<Security>(),Inherit=true};bool attributesReady=false;
        try
        {
            job=CreateJobObjectW(0,null);if(job==0)Fail();
            var limits=new Limits();limits.Basic.Flags=0x2000;
            if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<Limits>()))Fail();
            if(!CreatePipe(out outRead,out outWrite,ref security,0)||!CreatePipe(out errRead,out errWrite,ref security,0))Fail();
            if(!SetHandleInformation(outRead,1,0)||!SetHandleInformation(errRead,1,0))Fail();
            input=CreateFileW("NUL",0x80000000,3,ref security,3,0,0);if(input==new nint(-1)){input=0;Fail();}
            nuint bytes=0;InitializeProcThreadAttributeList(0,2,0,ref bytes);attributes=Marshal.AllocHGlobal((nint)bytes);
            if(!InitializeProcThreadAttributeList(attributes,2,0,ref bytes))Fail();
            attributesReady=true;
            jobList=Marshal.AllocHGlobal(nint.Size);Marshal.WriteIntPtr(jobList,job);
            handles=Marshal.AllocHGlobal(nint.Size*3);Marshal.WriteIntPtr(handles,input);Marshal.WriteIntPtr(handles,nint.Size,outWrite);Marshal.WriteIntPtr(handles,2*nint.Size,errWrite);
            if(!UpdateProcThreadAttribute(attributes,0,0x2000d,jobList,(nuint)nint.Size,0,0)||!UpdateProcThreadAttribute(attributes,0,0x20002,handles,(nuint)(3*nint.Size),0,0))Fail();
            string block=string.Join('\0',psi.Environment.OrderBy(e=>e.Key,StringComparer.OrdinalIgnoreCase).Select(e=>e.Key+"="+e.Value))+"\0\0";
            environment=Marshal.StringToHGlobalUni(block);
            var startup=new StartupEx{Info=new Startup{Size=Marshal.SizeOf<StartupEx>(),Flags=0x100,Input=input,Output=outWrite,Error=errWrite},Attributes=attributes};
            var command=new StringBuilder(Quote(psi.FileName)+" "+string.Join(' ',psi.ArgumentList.Select(Quote)));
            if(!CreateProcessW(psi.FileName,command,0,0,true,0x08080400,environment,psi.WorkingDirectory,ref startup,out var result))Fail();
            Process process;
            try{process=Process.GetProcessById(result.Id);process.StartInfo=psi;}
            finally{CloseHandle(result.Thread);CloseHandle(result.Process);}
            int id=process.Id;jobs[id]=job;job=0;
            process.Exited+=(_,_)=>{if(jobs.TryRemove(id,out var owned))CloseHandle(owned);};process.EnableRaisingEvents=true;
            var output=new StreamReader(new FileStream(new SafeFileHandle(outRead,true),FileAccess.Read),psi.StandardOutputEncoding??Encoding.UTF8);outRead=0;
            var error=new StreamReader(new FileStream(new SafeFileHandle(errRead,true),FileAccess.Read),psi.StandardErrorEncoding??Encoding.UTF8);errRead=0;
            return new(process,output,error);
        }
        finally
        {
            if(attributes!=0){if(attributesReady)DeleteProcThreadAttributeList(attributes);Marshal.FreeHGlobal(attributes);}
            foreach(var value in new[]{jobList,handles,environment})if(value!=0)Marshal.FreeHGlobal(value);
            foreach(var value in new[]{outRead,outWrite,errRead,errWrite,input,job})if(value!=0)CloseHandle(value);
        }
    }
    internal static async Task ReadLines(StreamReader reader,Action<string?> receive)
    {using(reader)try{while(await reader.ReadLineAsync() is string line)receive(line);}catch(IOException){}catch(ObjectDisposedException){}}
    internal static string Quote(string value)
    {
        var text=new StringBuilder("\"");int slashes=0;
        foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"')text.Append('\\',slashes*2+1);else text.Append('\\',slashes);text.Append(c);slashes=0;}
        return text.Append('\\',slashes*2).Append('"').ToString();
    }
    static void Fail()=>throw new Win32Exception(Marshal.GetLastWin32Error(),"无法创建绑定到 Soma Finder 生命周期的后端。");
    [StructLayout(LayoutKind.Sequential)]struct Security{public int Size;public nint Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
    [StructLayout(LayoutKind.Sequential)]struct Basic{public long ProcessTime,JobTime;public uint Flags;public nuint Min,Max;public uint Active;public nuint Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)]struct Io{public ulong A,B,C,D,E,F;}
    [StructLayout(LayoutKind.Sequential)]struct Limits{public Basic Basic;public Io Io;public nuint ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Startup{public int Size;public string? Reserved,Desktop,Title;public uint X,Y,Width,Height,CharsX,CharsY,Fill,Flags;public ushort Show,ReservedSize;public nint ReservedData,Input,Output,Error;}
    [StructLayout(LayoutKind.Sequential)]struct StartupEx{public Startup Info;public nint Attributes;}
    [StructLayout(LayoutKind.Sequential)]struct ProcessInfo{public nint Process,Thread;public int Id,ThreadId;}
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern nint CreateJobObjectW(nint sa,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(nint job,int kind,ref Limits limits,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool CreatePipe(out nint read,out nint write,ref Security sa,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetHandleInformation(nint handle,uint mask,uint flags);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern nint CreateFileW(string name,uint access,uint share,ref Security sa,uint mode,uint flags,nint template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool InitializeProcThreadAttributeList(nint list,int count,uint flags,ref nuint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool UpdateProcThreadAttribute(nint list,uint flags,nuint attribute,nint value,nuint size,nint previous,nint returned);
    [DllImport("kernel32.dll")]static extern void DeleteProcThreadAttributeList(nint list);
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern bool CreateProcessW(string app,StringBuilder command,nint processSa,nint threadSa,bool inherit,uint flags,nint environment,string directory,ref StartupEx startup,out ProcessInfo process);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(nint handle);
}
