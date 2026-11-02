// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;
public sealed partial class SearchWindow : Window
{
    readonly FinderApplication app;
    readonly TextBox query=UI.Input(),limit=UI.Input(Preferences.InitialSearchBreadth.ToString(),64),custom=UI.Input("",120);
    readonly ComboBox mode=new(){Width=120,Margin=new Thickness(4)},type=new(){Width=110,Margin=new Thickness(4)};
    readonly TreeView tree=new(){Margin=new Thickness(8)};
    readonly DataGrid results=new(){AutoGenerateColumns=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,CanUserAddRows=false,EnableRowVirtualization=true,EnableColumnVirtualization=false,GridLinesVisibility=DataGridGridLinesVisibility.None,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=34};
    readonly SummaryTextView snippet=new(){Padding=new Thickness(12)};
    readonly PreviewHost preview=new(){Margin=new Thickness(12)};
    readonly TextBlock status=UI.Text("正在连接…"),hint=UI.Text("");
    readonly Grid panels=new();Button search=null!;DetailPanels details=null!;
    Dictionary<string,bool> selection, snapshot=new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string,string> knownRoots=new(StringComparer.OrdinalIgnoreCase);bool sourceChanged;string failureNotice="";
    List<Hit> candidates=new();bool browsing,closing,sortOriginal=true;int requestVersion,previewVersion,configVersion;
    int browsePage=1;Button more=null!; readonly TextBlock count=UI.Text("");string? sortField;bool descending;double treeWidth=230;
    bool loadingSearch;string? previewCacheKey,previewPendingKey;object? previewCacheContent;string resultQuery="";
    public bool SkipExitOnce {get;set;}
    public SearchWindow(FinderApplication application,IReadOnlyDictionary<string,bool> inherited)
    {
        app=application;selection=new(inherited,StringComparer.OrdinalIgnoreCase);limit.Text=(app.Backend.Prefs.DefaultSearchBreadth is >=1 and <=100000?app.Backend.Prefs.DefaultSearchBreadth:Preferences.InitialSearchBreadth).ToString();
        BuildSurface();
    }
    public Dictionary<string,bool> Scope()=>new(selection,StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<Hit> VerificationCandidates=>candidates.AsReadOnly();
    internal DetailPanels VerificationDetails=>details;
    internal int VerificationBreadth=>int.Parse(limit.Text);
    internal string VerificationPreviewText=>(preview.Content as TextBox)?.Text??"";
    internal void VerificationSelect(int index)=>results.SelectedIndex=index;
    public void UpdateSearchTitle(string title){Title=title;details.UpdateTitle(title);}
    public void HideAuxiliaries()=>details.HideAuxiliaries();
    public void RestoreAuxiliaries()=>details.RestoreAuxiliaries();
    public void ReloadTree()
    {
        if(configVersion!=app.Backend.Generation){requestVersion++;previewVersion++;previewCacheKey=null;previewPendingKey=null;previewCacheContent=null;EndForConfiguration();}
        tree.Items.Clear();
        var roots=app.Backend.Workspaces.SelectMany(w=>w.Roots).ToList();
        var current=app.Backend.Workspaces.SelectMany(w=>w.Roots.Select(r=>new{Key=w.Name+"/"+r.Name,Root=r})).ToDictionary(x=>x.Key,x=>x.Root.Path,StringComparer.OrdinalIgnoreCase);
        foreach(var pair in knownRoots)
        {
            if(!current.TryGetValue(pair.Key,out var relocated)||Rules.Normalize(pair.Value).Equals(Rules.Normalize(relocated),StringComparison.OrdinalIgnoreCase))continue;
            void Remap(Dictionary<string,bool> state){foreach(var key in state.Keys.Where(k=>Rules.Contains(pair.Value,k)).ToArray()){var value=state[key];state.Remove(key);state[System.IO.Path.Combine(relocated,System.IO.Path.GetRelativePath(pair.Value,key))]=value;}}
            Remap(selection);Remap(snapshot);
            foreach(var h in candidates.Where(h=>h.Workspace+"/"+h.VirtualPath[7..].Split('/')[0]==pair.Key))
            {h.FullPath=System.IO.Path.Combine(relocated,System.IO.Path.GetRelativePath(pair.Value,h.FullPath));h.RootPath=relocated;h.Bytes=null;h.Created=null;h.Modified=null;h.Notify();}
        }
        if(knownRoots.Count>0&&candidates.Count>0)sourceChanged=true;knownRoots=current;
        var historical=candidates.Select(h=>h.RootPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(p=>!roots.Any(r=>Rules.Contains(r.Path,p))).ToList();
        foreach(var key in selection.Keys.ToArray())if(!roots.Any(r=>Rules.Contains(r.Path,key))&&!historical.Any(p=>Rules.Contains(p,key)))selection.Remove(key);
        foreach(var root in roots)
        {if(!selection.Keys.Any(p=>Rules.Contains(root.Path,p)))selection[root.Path]=root.DefaultSearch;tree.Items.Add(Node(root.Path,true));}
        foreach(var history in historical){var node=Node(history,true);if(node.Header is CheckBox box)box.Content=RootCaption(history,true)+"（已移除）";node.Items.Clear();tree.Items.Add(node);}
        configVersion=app.Backend.Generation;ApplyFilter();if(candidates.Count>0)_=Metadata(candidates.Take(browsing?browsePage*500:candidates.Count).ToList(),requestVersion);
    }
    TreeViewItem Node(string path,bool root=false)
    {
        var box=new CheckBox{Content=RootCaption(path,root),IsChecked=CheckState(path),ToolTip=path,Margin=new Thickness(2,3,2,3)};
        var node=new TreeViewItem{Header=box,Tag=path};node.Items.Add(new TreeViewItem{Header="载入…"});
        box.Click+=(_,_)=>
        {
            foreach(var child in selection.Keys.Where(k=>k!=path&&Rules.Contains(path,k)).ToList())selection.Remove(child);
            selection[path]=box.IsChecked==true;UpdateChecks(tree.Items);browsePage=1;
            ApplyFilter();
        };
        node.Expanded+=async(_,e)=>
        {
            if(e.OriginalSource!=node)return;
            if(node.Items.Count!=1||((node.Items[0] as TreeViewItem)?.Tag!=null))return;
            node.Items.Clear();
            try
            {
                if(!Native.Verified(path,app.Backend.Prefs))throw new IOException("目录离线或卷身份未确认");
                var dirs=await Task.Run(()=>Directory.GetDirectories(path).Where(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)==0).OrderBy(p=>p).ToArray());
                foreach(var dir in dirs)node.Items.Add(Node(dir));
            }catch(Exception ex){node.Items.Add(new TreeViewItem{Header=ex.Message,IsEnabled=false});}
        };
        return node;
    }
    void UpdateChecks(ItemCollection items)
    {
        foreach(var item in items.OfType<TreeViewItem>())if(item.Tag is string path&&item.Header is CheckBox box)
        {box.IsChecked=CheckState(path);UpdateChecks(item.Items);}
    }
    bool? CheckState(string path)
    {
        var self=Selection.Includes(selection,path);
        return selection.Any(kv=>Rules.Contains(path,kv.Key)&&kv.Value!=self)?null:self;
    }
    async Task Search(bool browseOnly=false)
    {
        if(selection.Count==0||!selection.Any(x=>x.Value)){hint.Text="请选择搜索目录";candidates.Clear();ApplyFilter();return;}
        if(!int.TryParse(limit.Text,out var n)||n<1||n>100000){UI.Error("搜索广度请输入 1–100000 的整数。");return;}
        var wsCount=app.Backend.Workspaces.Count(w=>w.Roots.Any(r=>Selection.Positive(selection).Any(s=>Rules.Contains(r.Path,s)||Rules.Contains(s,r.Path))));
        if(wsCount==0){hint.Text="请选择当前仍在索引范围内的目录；历史结果可继续查看。";return;}
        if(n<wsCount){n=wsCount;limit.Text=n.ToString();}
        var thisRequest=++requestVersion;var config=app.Backend.Generation;var selected=Scope();var text=browseOnly?"":query.Text.Trim();var chosenMode=new[]{"hybrid","lexical","vector"}[Math.Max(0,mode.SelectedIndex)];
        searchWait?.Cancel();var wait=new CancellationTokenSource();searchWait=wait;
        app.SubmitSearchTitle(this,text);
        loadingSearch=true;searchOutcome="";searchNotice="";searchModeLabel=chosenMode=="lexical"?"关键词搜索":chosenMode=="hybrid"?"混合搜索":"语义搜索";searchStage="正在准备搜索";searchClock.Restart();waitTimer.Start();cancelWait.Visibility=Visibility.Visible;lexicalAlternative.Visibility=chosenMode=="lexical"||text==""?Visibility.Collapsed:Visibility.Visible;
        failureNotice=candidates.Count>0?"正在搜索；当前仍为上次结果。":"正在搜索…";hint.Text=failureNotice;hint.ToolTip=failureNotice;
        bool firstBatch=true;
        bool Current()=>thisRequest==requestVersion&&!closing&&!wait.IsCancellationRequested&&config==app.Backend.Generation;
        try
        {
            var found=await app.Backend.Search(text,chosenMode,Selection.Positive(selected),n,selected,Current,wait.Token,update=>
            {
                if(!Current())return;searchStage=update.Stage;searchNotice=update.Notice;UpdateStatus();
                if(update.Hits!=null){if(firstBatch){sortOriginal=true;firstBatch=false;}candidates=ReuseHits(update.Hits);snapshot=selected;browsing=text=="";resultQuery=text;failureNotice=update.Notice.Length>0?update.Notice:"正在搜索 · "+update.Stage;sourceChanged=false;ApplyFilter();_=Metadata(candidates.Take(browsing?500:candidates.Count).ToList(),thisRequest);}
            });
            if(!Current())return;
            candidates=ReuseHits(found);snapshot=selected;browsing=text=="";browsePage=1;if(firstBatch)sortOriginal=true;sourceChanged=false;failureNotice=searchNotice;searchOutcome=searchNotice.Length>0?$"搜索结果 {found.Count} 条 · {searchNotice}":$"{searchModeLabel}完成 · 返回 {found.Count} 条 · {searchClock.Elapsed.TotalSeconds:F1} 秒";resultQuery=text;previewCacheKey=null;ApplyFilter();
            _=Metadata(browsing?found.Take(500).ToList():found,thisRequest);
        }
        catch(PartialSearchException ex)
        {if(Current()){searchOutcome=searchModeLabel+"部分完成";candidates=ReuseHits(ex.Hits);snapshot=Selection.RestrictToRoots(selected,ex.CompletedRoots);browsing=text=="";resultQuery=text;previewCacheKey=null;failureNotice="部分范围查询失败："+ex.Message;ApplyFilter();_=Metadata(candidates.Take(browsing?500:candidates.Count).ToList(),thisRequest);}}
        catch(OperationCanceledException){if(thisRequest==requestVersion&&!closing){searchOutcome="已取消搜索等待";failureNotice="已取消等待；Soma 已接收的查询可能仍在后台处理。";hint.Text=failureNotice;}}
        catch(TimeoutException){wait.Cancel();if(thisRequest==requestVersion&&!closing){searchOutcome="等待搜索超时";failureNotice="等待 Soma 超时；后台查询可能仍在处理，可改用关键词搜索。";hint.Text=failureNotice;}}
        catch(Exception ex){if(thisRequest==requestVersion&&!closing){searchOutcome=searchModeLabel+"失败";failureNotice="搜索失败："+ex.Message+(candidates.Count>0?"；当前保留已有结果。":"");hint.Text=failureNotice;hint.ToolTip=failureNotice;}}
        finally {if(thisRequest==requestVersion){loadingSearch=false;searchClock.Stop();cancelWait.Visibility=Visibility.Collapsed;waitTimer.Stop();if(!closing){ApplyFilter();UpdateStatus();}}}
    }

    int filterVersion;
    sealed record FilterRow(Hit Hit,string Path,string Type,string Root,int Rank,IComparable? Key);
    async void ApplyFilter()
    {
        if(type.SelectedItem==null)return;
        int version=++filterVersion;var filter=type.SelectedIndex==7?custom.Text:(string)type.SelectedItem;
        var currentScope=Scope();var searched=new Dictionary<string,bool>(snapshot,StringComparer.OrdinalIgnoreCase);bool original=sortOriginal,reverse=descending;int page=browsePage;bool browse=browsing;
        IComparable? Key(Hit h)=>sortField switch{nameof(Hit.Name)=>h.Name,nameof(Hit.Folder)=>h.Folder,nameof(Hit.Type)=>h.Type,nameof(Hit.Bytes)=>h.Bytes,nameof(Hit.Created)=>h.Created,nameof(Hit.Modified)=>h.Modified,_=>h.Rank};
        var rows=candidates.Select(h=>new FilterRow(h,h.FullPath,h.Type,h.RootPath,h.Rank,Key(h))).ToArray();
        (List<Hit> Visible,List<Hit> Loaded) Compute()
        {
            var filtered=rows.Where(h=>Selection.Includes(currentScope,h.Path)&&Selection.Includes(searched,h.Path)&&Rules.TypeMatches(h.Type,filter)).ToList();
            var shown=browse?filtered.Take(page*500).ToList():filtered.ToList();
            int CompareRows(FilterRow a,FilterRow b){if(a.Key==null)return b.Key==null?0:1;if(b.Key==null)return -1;int n=a.Key is string x&&b.Key is string y?StringComparer.CurrentCultureIgnoreCase.Compare(x,y):a.Key.CompareTo(b.Key);return reverse?-n:n;}
            if(original)shown=shown.OrderBy(h=>h.Root,StringComparer.OrdinalIgnoreCase).ThenBy(h=>h.Rank).ToList();else shown.Sort(CompareRows);
            return(filtered.Select(h=>h.Hit).ToList(),shown.Select(h=>h.Hit).ToList());
        }
        var filtered=rows.Length>5000?await Task.Run(Compute):Compute();
        if(version!=filterVersion||closing)return;var visible=filtered.Visible;var loaded=filtered.Loaded;
        var selected=results.SelectedItem as Hit;bindingResults=true;results.ItemsSource=loaded;summaryResults.ItemsSource=loaded;foreach(var hit in loaded){if(hit.ThumbnailQuery!=resultQuery){hit.ThumbnailQuery=resultQuery;ResultThumbnails.ClearLocation(hit);}}waterfall.SetItems(loaded,resultQuery,app.Backend.Prefs);
        if(sortOriginal){var view=CollectionViewSource.GetDefaultView(results.ItemsSource);view.SortDescriptions.Clear();if(loaded.Count>0)view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Hit.RootPath)));}
        if(selected!=null&&loaded.Contains(selected)){results.SelectedItem=selected;summaryResults.SelectedItem=selected;waterfall.Select(selected);}else waterfall.Select(null);bindingResults=false;SyncSortControl();ShowSelection();
        more.Visibility=browsing&&visible.Count>loaded.Count?Visibility.Visible:Visibility.Collapsed;
        count.Text=browsing?$"已加载 {loaded.Count}/{visible.Count} 条 · 排序限已加载文件":$"返回 {candidates.Count} 条 · 筛选后 {loaded.Count} 条";
        hint.Text=failureNotice!=""?failureNotice:sourceChanged?"索引配置已变化，当前结果保留；可按当前范围重新搜索。":snapshot.Count>0&&Selection.Expanded(selection,snapshot)?"范围已扩大，新增目录尚未搜索":"";rangeSearch.Visibility=sourceChanged||(snapshot.Count>0&&Selection.Expanded(selection,snapshot))?Visibility.Visible:Visibility.Collapsed;hint.MaxHeight=44;hint.TextTrimming=TextTrimming.CharacterEllipsis;hint.ToolTip=hint.Text;emptyState.Text=loadingSearch?"正在搜索…":candidates.Count==0&&snapshot.Count==0?"选择左侧搜索范围，输入内容后按 Enter 搜索；留空可浏览已收录文件。":loaded.Count==0?"当前范围和文件类型下没有结果。可调整筛选，或扩大范围后重新搜索。":"";emptyState.Visibility=loaded.Count==0?Visibility.Visible:Visibility.Collapsed;
        RefreshSearchPresentation();
        search.IsEnabled=app.Backend.IsInitialized&&app.Backend.Workspaces.SelectMany(w=>w.Roots).Any(r=>Selection.Positive(selection).Any(s=>Rules.Contains(r.Path,s)||Rules.Contains(s,r.Path)));
    }
    int Compare(Hit a,Hit b)
    {
        IComparable? Get(Hit h)=>sortField switch{nameof(Hit.Name)=>h.Name,nameof(Hit.Folder)=>h.Folder,nameof(Hit.Type)=>h.Type,nameof(Hit.Bytes)=>h.Bytes,nameof(Hit.Created)=>h.Created,nameof(Hit.Modified)=>h.Modified,_=>h.Rank};
        var av=Get(a);var bv=Get(b);if(av==null)return bv==null?0:1;if(bv==null)return -1;
        int compare=av is string aa&&bv is string bb?StringComparer.CurrentCultureIgnoreCase.Compare(aa,bb):av.CompareTo(bv);return descending?-compare:compare;
    }
    static readonly SemaphoreSlim metadataSlots=new(4,4);
    async Task Metadata(List<Hit> hits,int version)
    {
        foreach(var batch in hits.Chunk(4))
        {
            if(version!=requestVersion||closing)return;
            var values=await Task.WhenAll(batch.Select(async hit=>
            {
                var path=hit.FullPath;await metadataSlots.WaitAsync();
                try{return (Hit:hit,Path:path,Info:await Task.Run<(long Size,DateTime Created,DateTime Modified)?>(()=>{try{if(!Native.Verified(path,app.Backend.Prefs))return null;var f=new FileInfo(path);return f.Exists?(f.Length,f.CreationTime,f.LastWriteTime):null;}catch{return null;}}));}
                finally{metadataSlots.Release();}
            }));
            if(version!=requestVersion||closing)return;
            foreach(var value in values)
            {
                var hit=value.Hit;if(hit.FullPath!=value.Path)continue;
                bool changed=hit.Modified!=value.Info?.Modified||hit.Bytes!=value.Info?.Size;
                hit.Bytes=value.Info?.Size;hit.Created=value.Info?.Created;hit.Modified=value.Info?.Modified;if(changed)ResultThumbnails.ClearLocation(hit);
                hit.Availability=value.Info==null?"原文件不可用、无读取权限或卷身份不匹配；可继续查看已有索引内容。":"原文件可用 · "+hit.FullPath;hit.Notify();
            }
            if(results.SelectedItem is Hit chosen&&batch.Contains(chosen)){UpdateFileInfo(chosen);LoadSelectionLocation(chosen);_=Preview();}
        }
    }
    string PreviewKey(Hit hit)=>hit.Workspace+"\n"+hit.VirtualPath+"\n"+hit.FullPath+"\n"+configVersion+"\n"+hit.Modified?.Ticks+"\n"+hit.Snippet+"\n"+resultQuery;
    internal void SeedPreview(object content){if(!CloudSandbox.Active||results.SelectedItem is not Hit hit)throw new InvalidOperationException();previewVersion++;previewPendingKey=null;previewCacheKey=PreviewKey(hit);previewCacheContent=content;preview.Content=content;}
    async Task Preview()
    {
        if(closing||!details.ContentVisible(DetailKind.Preview))return;
        if(results.SelectedItem is not Hit hit){previewVersion++;previewPendingKey=null;preview.Content=null;return;}
        var key=PreviewKey(hit);
        if(previewCacheKey==key&&previewCacheContent!=null){preview.Content=previewCacheContent;return;}
        if(previewPendingKey==key)return;
        var version=++previewVersion;previewPendingKey=key;preview.Content=PreviewText("正在加载预览…");
        var physicalPath=hit.FullPath;
        void Apply(object content)
        {
            if(version!=previewVersion||closing||results.SelectedItem is not Hit current||PreviewKey(current)!=key)return;
            previewCacheKey=key;previewCacheContent=content;preview.Content=content;
        }
        try
        {
            if(Native.Verified(physicalPath,app.Backend.Prefs)&&File.Exists(physicalPath))
            {
                var file=new FileInfo(physicalPath);
                if(Rules.TypeMatches(hit.Type,"视频"))
                {var thumbnail=await ResultThumbnails.Get(hit,resultQuery,app.Backend.Prefs,selectionThumbnail?.Token??CancellationToken.None);if(thumbnail?.Image is {} poster){UpdateFileInfo(hit);Apply(new ImagePreviewView(poster,poster.PixelWidth,poster.PixelHeight));return;}Apply(PreviewText("视频封面暂不可用；可双击结果打开原视频。"));return;}
                if(Rules.TypeMatches(hit.Type,"图片")&&file.Length<25*1024*1024)
                {try{Apply(await ImagePreviewView.LoadAsync(physicalPath,()=>Native.Verified(physicalPath,app.Backend.Prefs)));return;}catch(NotSupportedException){}catch(FileFormatException){}}
                if(Rules.TypeMatches(hit.Type,"代码")||new[]{".txt",".md",".csv",".log"}.Contains(hit.Type))
                {var value=await Task.Run(()=>{using var reader=File.OpenText(physicalPath);var buffer=new char[128*1024];int size=reader.ReadBlock(buffer,0,buffer.Length);return new string(buffer,0,size);});Apply(PreviewText(value));return;}
            }
            var text=await app.Backend.Body(hit);Apply(PreviewText("索引正文预览\n\n"+text));
        }
        catch(OperationCanceledException){}
        catch(Exception ex){Apply(PreviewText("预览不可用："+ex.Message));}
        finally{if(version==previewVersion&&previewPendingKey==key)previewPendingKey=null;}
    }
    static TextBox PreviewText(string text){var box=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Background=Brushes.Transparent};box.SetResourceReference(Control.ForegroundProperty,"TextFillColorPrimaryBrush");return box;}
    void OpenSelected(bool folder)
    {
        if(results.SelectedItem is not Hit h)return;
        try
        {
            if(!Native.Verified(h.FullPath,app.Backend.Prefs)||!File.Exists(h.FullPath))throw new IOException("原文件已删除、硬盘离线或卷身份不匹配，请在设置中检查或重新定位目录。");
            if(folder){var p=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};p.ArgumentList.Add("/select,"+h.FullPath);Process.Start(p);}else Process.Start(new ProcessStartInfo(h.FullPath){UseShellExecute=true});
        }catch(Exception ex){UI.Error(ex.Message);}
    }
    public void UpdateStatus(){if(configVersion!=app.Backend.Generation)ReloadTree();RefreshSearchPresentation();search.IsEnabled=app.Backend.IsInitialized&&app.Backend.Workspaces.SelectMany(w=>w.Roots).Any(r=>Selection.Positive(selection).Any(s=>Rules.Contains(r.Path,s)||Rules.Contains(s,r.Path)));}
    internal void SeedVerification()
    {
        tree.Items.Clear();foreach(var name in new[]{"C:\\示例资料","C:\\示例图片","C:\\示例视频","C:\\示例下载"})tree.Items.Add(new TreeViewItem{Header=new CheckBox{Content=name,IsChecked=true,Margin=new Thickness(2,3,2,3)}});
        selection=new(StringComparer.OrdinalIgnoreCase){{"C:/示例资料",true}};snapshot=Scope();
        query.Text="上个月关于笔记本续航的测试记录";
        candidates=new(){new(){Workspace="demo",RootPath="C:/示例资料",VirtualPath="soma://demo-root/笔记本续航测试记录.pdf",FullPath="C:/示例资料/笔记本续航测试记录.pdf",Snippet="测试记录：在相同亮度与电源模式下，对比不同工作负载的续航表现。摘要由 Soma 返回。",Rank=0,Bytes=324000},new(){Workspace="demo",RootPath="C:/示例资料",VirtualPath="soma://demo-root/产品资料/测试方案.docx",FullPath="C:/示例资料/产品资料/测试方案.docx",Snippet="记录测试条件与对比数据。",Rank=1,Bytes=88000}};
        ApplyFilter();previewCacheKey=PreviewKey(candidates[0]);previewCacheContent=PreviewText("此处展示所选文件的内容预览；布局操作不重新搜索。");results.SelectedIndex=0;status.Text="提取文件内容 · 2692/6039";
    }
}
