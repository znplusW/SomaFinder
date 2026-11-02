// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

public sealed record SearchUpdate(string Stage,List<Hit>? Hits=null,List<string>? Roots=null,int Completed=0,int Total=0,string Notice="",bool Final=false);

public sealed partial class Backend
{
    readonly Dictionary<string,(DateTimeOffset At,Task<JsonElement> Value)> fileLists=new();
    readonly Dictionary<string,string> verificationRoutes=new();
    internal void VerificationPort(Workspace workspace,int port){if(!CloudSandbox.Active)throw new InvalidOperationException();ports[workspace.Name]=port;verificationRoutes[workspace.Name]="/"+workspace.Name;initialized=true;}
    internal TimeSpan SearchWaitLimit {get;set;}=TimeSpan.FromMinutes(3);
    async Task<JsonElement> Files(Workspace ws,Root root)
    {
        var key=Generation+"/"+ws.Name+"/"+root.Name+"/"+rootCounts.GetValueOrDefault(ws.Name+"/"+root.Name);
        if(fileLists.TryGetValue(key,out var cached)&&DateTimeOffset.UtcNow-cached.At<TimeSpan.FromSeconds(20))return await cached.Value;
        var task=Rpc(ws,"project.files",new[]{root.Name});fileLists[key]=(DateTimeOffset.UtcNow,task);
        try{return await task;}catch{fileLists.Remove(key);throw;}
    }
    public string ResultKey(Hit hit)
    {
        var binding=Prefs.Volumes.GetValueOrDefault(hit.RootPath);
        if(binding!=null)return binding.Volume+"\0"+System.IO.Path.Combine(binding.RelativePath,System.IO.Path.GetRelativePath(hit.RootPath,hit.FullPath)).Replace('\\','/');
        return Rules.Normalize(hit.FullPath);
    }
    public List<Hit> Deduplicate(IEnumerable<Hit> hits)=>hits.OrderBy(h=>h.MatchMode=="hybrid"?0:1).ThenBy(h=>h.Workspace,StringComparer.OrdinalIgnoreCase).ThenBy(h=>h.Rank).DistinctBy(ResultKey,StringComparer.OrdinalIgnoreCase).ToList();
    public async Task<List<Hit>> Search(string text,string mode,List<string> scope,int limit,IReadOnlyDictionary<string,bool>? state=null,Func<bool>? current=null,CancellationToken cancellationToken=default,Action<SearchUpdate>? progress=null)
    {
        var task=SearchCore(text,mode,scope,limit,state,current,cancellationToken,progress);
        // Cancelling the UI wait must not release a model slot while Soma still computes.
        _=task.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
        return await task.WaitAsync(SearchWaitLimit,cancellationToken);
    }
    async Task<List<Hit>> SearchCore(string text,string mode,List<string> scope,int limit,IReadOnlyDictionary<string,bool>? state,Func<bool>? current,CancellationToken token,Action<SearchUpdate>? progress)
    {
        SearchCount++;
        if(scope.Count==0)return new();
        if(!initialized)throw new IOException("正在连接 Soma，请稍后搜索。");
        if(draining||configurationChanging||File.Exists(PendingPath))throw new IOException("正在切换服务，请稍后搜索。");
        bool heavy=!string.IsNullOrWhiteSpace(text)&&mode!="lexical";
        progress?.Invoke(new("正在准备搜索范围"));
        Interlocked.Increment(ref runningQueries);readLease.Value++;
        var generation=Generation;
        bool Current()=>!token.IsCancellationRequested&&(current==null||current())&&generation==Generation&&!exiting;
        void Check(){if(!Current())throw new OperationCanceledException("搜索已取消或范围配置已变化。");}
        try
        {
            if(draining||configurationChanging||File.Exists(PendingPath))throw new IOException("正在切换搜索服务。");Check();
            var groups=Workspaces.Where(w=>w.Roots.Any(r=>scope.Any(s=>Rules.Contains(r.Path,s)||Rules.Contains(s,r.Path)))).ToList();
            var weights=new long?[groups.Count];var errors=new List<string>();
            var completedRoots=new List<string>();
            progress?.Invoke(new("正在准备搜索范围"));
            // One workspace needs no quota census. Browsing has no search quota at all.
            if(groups.Count==1||string.IsNullOrWhiteSpace(text))Array.Fill(weights,1L);
            else
            {
                using var census=new SemaphoreSlim(2,2);
                await Task.WhenAll(groups.Select(async (ws,i)=>
                {
                    await census.WaitAsync(token);try
                    {
                        Check();long weight=0;
                        foreach(var root in ws.Roots.Where(r=>scope.Any(s=>Rules.Contains(r.Path,s)||Rules.Contains(s,r.Path))))
                        {
                            if(state==null){if(!rootCounts.TryGetValue(ws.Name+"/"+root.Name,out var count))throw new IOException("文件数量统计尚不可用。");weight+=count;}
                            else{var data=await Files(ws,root);Check();foreach(var item in data.GetProperty("files").EnumerateArray()){var hit=ParseHit(ws,item,0);if(Selection.Includes(state,hit.FullPath)&&(!item.TryGetProperty("status",out var status)||status.GetString()=="ready"))weight++;}}
                        }
                        weights[i]=weight;
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception ex){errors.Add(ws.Name+"：统计失败，未检索。"+ex.Message);}
                    finally{census.Release();}
                }));
            }
            Check();var usable=Enumerable.Range(0,weights.Length).Where(i=>weights[i]!=null).ToArray();
            var allocated=Rules.Allocate(usable.Select(i=>weights[i]!.Value).ToArray(),Math.Max(limit,groups.Count)-(groups.Count-usable.Length));
            var quota=new int[groups.Count];for(int j=0;j<usable.Length;j++)quota[usable[j]]=allocated[j];
            var byGroup=new Dictionary<string,List<Hit>>();var notices=new Dictionary<string,string>();int finished=0;
            List<Hit> Combined()=>Deduplicate(groups.Where(w=>byGroup.ContainsKey(w.Name)).SelectMany(w=>byGroup[w.Name]));
            void Publish(string stage,bool final=false)=>progress?.Invoke(new(stage,final?null:Combined(),completedRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),finished,groups.Count,string.Join("；",notices.Values.Distinct()),final));
            async Task<List<Hit>> Fetch(Workspace ws,List<Root> roots,string selectedMode,int share)
            {
                var gate=selectedMode=="lexical"?queries:models;progress?.Invoke(new(selectedMode=="lexical"?"等待关键词搜索":"等待混合搜索通道"));await gate.WaitAsync(token);
                try{Check();progress?.Invoke(new("等待 Soma 返回 · "+(selectedMode=="lexical"?"关键词":"语义／混合")));var data=await Rpc(ws,"search."+selectedMode,new[]{text},new Dictionary<string,object>{{"project",roots.Select(r=>r.Name).ToArray()},{"limit",share}});Check();return data.GetProperty("results").EnumerateArray().Select((item,rank)=>{var hit=ParseHit(ws,item,rank);hit.MatchMode=selectedMode;return hit;}).ToList();}
                finally{gate.Release();}
            }
            await Task.WhenAll(usable.Select(async i=>
            {
                var ws=groups[i];var roots=ws.Roots.Where(r=>scope.Any(s=>Rules.Contains(r.Path,s)||Rules.Contains(s,r.Path))).ToList();
                bool lexicalOK=false,hybridOK=false;List<Hit>? lexical=null,hybrid=null;string? lexicalError=null,hybridError=null;
                void Display()
                {
                    var chosen=hybrid is {Count:>0}?hybrid:lexical;
                    if(chosen!=null){byGroup[ws.Name]=chosen;completedRoots.AddRange(roots.Select(r=>r.Path));if(mode=="hybrid")Publish(hybrid is {Count:>0}?"已收到混合搜索结果":"已显示关键词结果，混合搜索处理中");}
                }
                try
                {
                    Check();
                    if(string.IsNullOrWhiteSpace(text))
                    {
                        var local=new List<Hit>();foreach(var root in roots){var data=await Files(ws,root);Check();foreach(var item in data.GetProperty("files").EnumerateArray())local.Add(ParseHit(ws,item,local.Count));}
                        byGroup[ws.Name]=local;completedRoots.AddRange(roots.Select(r=>r.Path));finished++;Publish("已读取文件列表");return;
                    }
                    async Task Lexical(){try{lexical=await Fetch(ws,roots,"lexical",quota[i]);lexicalOK=true;Display();}catch(OperationCanceledException){throw;}catch(Exception ex){lexicalError=ex.Message;}}
                    async Task Hybrid()
                    {
                        // Only skip when every selected root has a fresh explicit zero-vector statistic.
                        if(roots.All(r=>RootProgress(ws.Name,r.Name) is RootIndexProgress stats&&stats.Vectors==0&&DateTimeOffset.UtcNow-stats.ReadAt<TimeSpan.FromSeconds(20))){hybridError="当前范围尚无可用语义向量";return;}
                        try{hybrid=await Fetch(ws,roots,mode,quota[i]);hybridOK=true;Display();}catch(OperationCanceledException){throw;}catch(Exception ex){hybridError=ex.Message;}
                    }
                    if(mode=="hybrid")await Task.WhenAll(Lexical(),Hybrid());
                    else if(mode=="lexical")await Lexical();
                    else await Hybrid();
                    Check();
                    if(mode=="hybrid"&&hybrid is not {Count:>0}&&lexicalOK)
                    {
                        string reason=hybridError?.Contains("Vector index is empty",StringComparison.OrdinalIgnoreCase)==true||hybridError?.Contains("语义向量")==true?"语义索引暂不可用":hybridError!=null?"混合搜索未完成："+hybridError:"混合搜索未返回结果";
                        notices[ws.Name]=reason+(lexical!.Count>0?"，已保留关键词结果":"；关键词未命中");
                    }
                    if(!lexicalOK&&!hybridOK)errors.Add("检索失败："+(hybridError??lexicalError??"未知错误"));
                    finished++;Publish("已完成 "+finished+"/"+groups.Count+" 个索引范围");
                }
                catch(OperationCanceledException){throw;}
                catch(Exception ex){errors.Add(ex.Message);}
            }));
            Check();var hits=Combined();
            if(errors.Count>0){if(completedRoots.Count==0)throw new IOException(string.Join("\n",errors));throw new PartialSearchException(string.Join("\n",errors),hits,completedRoots);}
            Publish(notices.Count>0?"已显示关键词结果":"搜索完成",true);return hits;

        }
        finally{readLease.Value--;Interlocked.Decrement(ref runningQueries);}
    }
}
