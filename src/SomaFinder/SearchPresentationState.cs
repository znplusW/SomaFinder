// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

public sealed partial class SearchWindow
{
    string searchOutcome="",searchNotice="",searchModeLabel="搜索";
    List<Hit> ReuseHits(IEnumerable<Hit> incoming)
    {
        var old=candidates.GroupBy(app.Backend.ResultKey,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
        return app.Backend.Deduplicate(incoming).Select(hit=>
        {
            if(!old.TryGetValue(app.Backend.ResultKey(hit),out var existing))return hit;
            bool changed=existing.FullPath!=hit.FullPath||existing.Snippet!=hit.Snippet||existing.MatchMode!=hit.MatchMode;
            existing.Workspace=hit.Workspace;existing.VirtualPath=hit.VirtualPath;existing.RootPath=hit.RootPath;existing.FullPath=hit.FullPath;existing.Snippet=hit.Snippet;existing.Rank=hit.Rank;existing.MatchMode=hit.MatchMode;existing.SourceLine=hit.SourceLine;if(changed)ResultThumbnails.ClearLocation(existing);existing.Notify();return existing;
        }).ToList();
    }
    void EndForConfiguration()
    {
        if(!loadingSearch)return;
        searchWait?.Cancel();loadingSearch=false;searchClock.Stop();waitTimer.Stop();searchOutcome="索引配置已变化，搜索等待已取消";failureNotice=searchOutcome;cancelWait.Visibility=System.Windows.Visibility.Collapsed;
    }
    void RefreshSearchPresentation()
    {
        var index="索引："+app.Backend.Message+" · "+app.Backend.Progress;
        status.Text=loadingSearch?$"{searchModeLabel} · {searchStage} · 已等待 {searchClock.Elapsed.TotalSeconds:F0} 秒｜{index}":searchOutcome.Length>0?searchOutcome+"｜"+index:index;
        int visible=DisplayedHits.Count;
        emptyState.Visibility=visible==0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        emptyState.Text=loadingSearch?(searchClock.Elapsed.TotalSeconds>=30?(searchModeLabel=="混合搜索"?"仍在等待 Soma 返回混合结果；关键词结果会优先显示。":"仍在等待 Soma 返回"+searchModeLabel+"结果。可取消等待。 "):"正在搜索…"):
            failureNotice.Length>0?failureNotice:snapshot.Count==0?"选择左侧范围并输入搜索内容；留空可浏览已收录文件。":candidates.Count==0?"本次搜索未返回结果。可调整关键词或搜索范围。":"当前目录与文件类型筛选下没有结果。";
        if(loadingSearch&&searchClock.Elapsed.TotalSeconds>=30&&searchNotice.Length==0){hint.Text=searchModeLabel=="混合搜索"?"混合搜索仍在处理；可查看已返回的关键词结果，或取消等待。":searchModeLabel+"仍在处理；可取消等待。";hint.ToolTip=hint.Text;}
    }
}
