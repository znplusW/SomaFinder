// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
namespace SomaFinder;

// Optional llama.cpp router adapter. Unsupported or unreadable runtimes are skipped;
// it never kills a process or changes model files, indexes or runtime configuration.
internal sealed class ModelRuntime
{
    readonly Native.Proc owner,router;
    readonly int port;
    internal bool WasBusy {get;private set;}
    static readonly string[] roles={"VISION","OCR_CLEANUP","RERANK","EXPAND","EMBED"};
    internal ModelRuntime(Native.Proc owner,Native.Proc router,int port){this.owner=owner;this.router=router;this.port=port;}
    internal static IEnumerable<ModelRuntime> Find(Native.Proc owner)
    {
        foreach(var child in Native.Descendants(owner).Where(p=>p.ParentId==owner.Id&&System.IO.Path.GetFileName(p.File).Equals("llama-server.exe",StringComparison.OrdinalIgnoreCase)))
        {
            var ports=ListeningPorts(child.Id);if(ports.Count==1)yield return new(owner,child,ports[0]);
        }
    }
    bool Verified()=>Native.Alive(owner)&&Native.Alive(router)&&ListeningPorts(router.Id).SequenceEqual(new[]{port});
    internal async Task<int> ReleaseIdleModels(Func<bool> allowed)
    {
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(3)};
        async Task<JsonElement> Read(string path)
        {
            if(!Verified())throw new IOException("模型服务身份或端口已变化。");
            using var response=await http.GetAsync(path);response.EnsureSuccessStatusCode();
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.Clone();
        }
        var catalog=await Read("/models");
        if(!catalog.TryGetProperty("data",out var data)||data.ValueKind!=JsonValueKind.Array)return 0;
        var entries=data.EnumerateArray().ToArray();
        // Loading, queued or unrecognised model states do not establish idleness.
        if(entries.Any(m=>State(m) is not ("loaded" or "unloaded" or "sleeping" or "failed"))){WasBusy=true;return 0;}
        var loaded=entries.Where(m=>State(m)=="loaded").ToArray();
        if(loaded.Any(m=>!m.TryGetProperty("id",out var id)||!roles.Contains(id.GetString(),StringComparer.Ordinal)))return 0;
        foreach(var model in loaded)
        {
            if(!allowed())return 0;
            var slots=await Read("/slots?model="+Uri.EscapeDataString(model.GetProperty("id").GetString()!)+"&autoload=false");
            if(!IdleSlots(slots)){WasBusy=true;return 0;}
        }
        int count=0;
        foreach(var role in roles.Where(r=>loaded.Any(m=>m.GetProperty("id").GetString()==r)))
        {
            if(!allowed()||!Verified())break;
            // Recheck immediately before each request; the frontend activity gate
            // keeps new Finder searches/index jobs queued until this unload completes.
            if(!IdleSlots(await Read("/slots?model="+role+"&autoload=false"))){WasBusy=true;break;}
            if(!allowed())break;
            using(var response=await http.PostAsJsonAsync("/models/unload",new{model=role}))
            {
                response.EnsureSuccessStatusCode();using var reply=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if(!reply.RootElement.TryGetProperty("success",out var ok)||ok.ValueKind!=JsonValueKind.True)throw new IOException("模型服务未确认卸载。");
            }
            // The router acknowledges before its worker exits. Finish this one
            // unload even when a new user request arrives, then release the gate.
            var until=Stopwatch.StartNew();bool unloaded=false;
            while(until.Elapsed<TimeSpan.FromSeconds(10))
            {
                var current=await Read("/models");
                if(current.TryGetProperty("data",out var models)&&models.ValueKind==JsonValueKind.Array&&models.EnumerateArray().Any(m=>m.TryGetProperty("id",out var id)&&id.GetString()==role&&State(m) is "unloaded" or "failed")){unloaded=true;break;}
                await Task.Delay(100);
            }
            if(!unloaded)throw new TimeoutException("等待空闲模型释放内存超时。");
            count++;
        }
        return count;
    }
    internal static bool IdleSlots(JsonElement slots)=>slots.ValueKind==JsonValueKind.Array&&slots.GetArrayLength()>0&&slots.EnumerateArray().All(s=>s.TryGetProperty("is_processing",out var busy)&&busy.ValueKind==JsonValueKind.False);
    static string? State(JsonElement model)=>model.TryGetProperty("status",out var status)&&status.ValueKind==JsonValueKind.Object&&status.TryGetProperty("value",out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;

    [DllImport("iphlpapi.dll",SetLastError=true)]static extern uint GetExtendedTcpTable(IntPtr table,ref int size,bool ordered,int family,int tableClass,uint reserved);
    internal static List<int> ListeningPorts(int processId)
    {
        int size=0;var first=GetExtendedTcpTable(IntPtr.Zero,ref size,false,2,5,0);
        if(first!=122||size<4)throw new IOException("无法核对模型服务监听端口。");
        var memory=Marshal.AllocHGlobal(size);
        try
        {
            if(GetExtendedTcpTable(memory,ref size,false,2,5,0)!=0)throw new IOException("模型服务监听端口核对失败。");
            int count=Marshal.ReadInt32(memory);if(count<0||count>(size-4)/24)throw new IOException("模型服务端口表无效。");
            var result=new List<int>();
            for(int i=0;i<count;i++)
            {
                int offset=4+i*24;
                if(Marshal.ReadInt32(memory,offset)!=2||Marshal.ReadInt32(memory,offset+20)!=processId||Marshal.ReadInt32(memory,offset+4)!=0x0100007f)continue;
                int networkPort=Marshal.ReadInt32(memory,offset+8);
                result.Add((ushort)IPAddress.NetworkToHostOrder((short)networkPort));
            }
            return result.Distinct().OrderBy(p=>p).ToList();
        }
        finally{Marshal.FreeHGlobal(memory);}
    }
}
