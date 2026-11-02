// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace SomaFinder;

internal static class SearchScenarioVerification
{
    public static int Run(string[] args)
    {
        string report=System.IO.Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);var dir=System.IO.Path.GetDirectoryName(report)!;
        var root=System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-search-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var store=new Store(root+"/data",root+"/config",root+"/state");var docs=root+"/docs";Directory.CreateDirectory(docs);Directory.CreateDirectory(store.StateDirectory);
        File.WriteAllText(docs+"/match.md","synthetic keyword sentinel");File.WriteAllText(store.StateDirectory+"/main.sqlite","");
        store.SaveWorkspace(new Workspace{Name="main",ConfigPath=store.ConfigDirectory+"/main.yml",Roots=new(){new(){Name="docs",Path=docs}}});
        var prefs=new Preferences{StartupSync=false,PeriodicSync=false,LowMemoryBlocksAuto=false};prefs.Volumes[docs]=Native.Identify(docs)!;
        var app=new FinderApplication(store,prefs,true);int failures=0;var records=new List<object>();
        app.Startup+=async(_,_)=>
        {
            using var server=new Server(store);app.Backend.VerificationPort(app.Backend.Workspaces.Single(),server.Port);app.NewWindow();var w=app.Windows.OfType<SearchWindow>().Single();
            async Task Test(string name,Func<Task> action){Console.WriteLine("START "+name);try{await action();records.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;records.Add(new{name,passed=false,error=ex.ToString()});Console.WriteLine("FAIL "+name+" "+ex.Message);}}
            void Check(bool value,string message){if(!value)throw new Exception(message);}
            void Snapshot(string name,SearchWindow window){records.Add(new{name,observation=window.ScenarioSnapshot});}
            void Capture(string name,SearchWindow window){var content=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var drawing=backdrop.RenderOpen())drawing.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,content.ActualWidth,content.ActualHeight));bitmap.Render(backdrop);bitmap.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(System.IO.Path.Combine(dir,name+".png"));encoder.Save(stream);}
            try
            {
                await Test("slow hybrid >30s keeps search state separate while keyword returns immediately",async()=>
                {
                    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);server.Hold=release.Task;server.Count=1;var mixed=w.ScenarioSearch("needle","hybrid");
                    try
                    {
                        await server.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));app.NewWindow();var peer=app.Windows.OfType<SearchWindow>().First(x=>x!=w);var clock=Stopwatch.StartNew();await peer.ScenarioSearch("needle","lexical").WaitAsync(TimeSpan.FromSeconds(3));Check(peer.DisplayedHits.Count==1,"Keyword result missing");records.Add(new{name="keyword latency with a held model request",milliseconds=clock.Elapsed.TotalMilliseconds});
                        for(int i=0;i<32;i++){app.Backend.ScenarioIndexPhase(i);w.UpdateStatus();peer.UpdateStatus();await Task.Delay(1000);}
                        Snapshot("during held hybrid",w);Capture("waiting-over-30-seconds",w);Check(w.ScenarioRunning&&w.ScenarioStatus.Contains("已等待")&&w.ScenarioStatus.Contains("混合")&&w.DisplayedHits.Count==1,"Index phase hid active search");peer.Close();
                    }
                    finally{release.TrySetResult();server.Hold=null;await mixed.WaitAsync(TimeSpan.FromSeconds(5));}
                    Snapshot("hybrid completed with a match",w);Check(w.DisplayedHits.Count==1,"Delayed hybrid result lost");
                });
                await Test("zero-result hybrid must not leave a permanent searching message",async()=>
                {
                    server.KeywordCount=0;server.Count=0;await w.ScenarioSearch("none","hybrid");app.Backend.ScenarioIndexPhase(77);w.UpdateStatus();Snapshot("hybrid returned zero",w);Capture("zero-result",w);
                    Check(!w.ScenarioRunning&&!w.ScenarioEmpty.Contains("正在搜索"),"Completed zero-result request still says searching");Check(w.ScenarioStatus.Contains("0 条")&&w.ScenarioStatus.Contains("混合"),"Completion was replaced with extraction status");
                });
                await Test("backend vector error remains visible after indexing status updates",async()=>
                {
                    server.KeywordCount=1;server.Error="Vector index is empty for the selected projects.";try{await w.ScenarioSearch("needle","hybrid");app.Backend.ScenarioIndexPhase(78);w.UpdateStatus();Snapshot("backend error",w);Check(w.ScenarioStatus.Contains("关键词结果")&&w.ScenarioHint.Contains("语义索引暂不可用"),"Backend failure hidden by index phase");}finally{server.Error=null;}
                });
                await Test("cancelled wait clears the loading banner and retains a visible outcome",async()=>
                {
                    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);server.Hold=release.Task;server.Count=1;var task=w.ScenarioSearch("cancel","hybrid");await Task.Delay(150);w.ScenarioCancel();await task.WaitAsync(TimeSpan.FromSeconds(3));Snapshot("cancelled",w);release.TrySetResult();server.Hold=null;await Task.Delay(100);
                    Check(w.ScenarioStatus.Contains("取消")&&!w.ScenarioEmpty.Contains("正在搜索"),"Cancellation outcome hidden");
                });
                await Test("timeout clears the loading banner and is distinguishable from no matches",async()=>
                {
                    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);server.Hold=release.Task;app.Backend.SearchWaitLimit=TimeSpan.FromMilliseconds(250);try{await w.ScenarioSearch("timeout","hybrid");Snapshot("timed out",w);Check(w.ScenarioStatus.Contains("超时")&&!w.ScenarioEmpty.Contains("正在搜索"),"Timeout looks like background indexing");}finally{release.TrySetResult();server.Hold=null;app.Backend.SearchWaitLimit=TimeSpan.FromMinutes(3);await Task.Delay(100);}
                });
                await Test("configuration refresh cannot silently turn a running query into idle indexing",async()=>
                {
                    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);server.Hold=release.Task;var task=w.ScenarioSearch("changed","hybrid");await Task.Delay(150);app.Backend.Reload();w.UpdateStatus();await task.WaitAsync(TimeSpan.FromSeconds(3));Snapshot("configuration changed",w);release.TrySetResult();server.Hold=null;await Task.Delay(100);Check(!w.ScenarioRunning&&w.ScenarioStatus.Contains("配置"),"Configuration cancellation was silent");
                });
                await Test("subsequent keyword completion replaces the previous error or cancellation",async()=>
                {
                    server.Count=1;await w.ScenarioSearch("needle","lexical");Snapshot("keyword completed",w);Check(w.DisplayedHits.Count==1&&w.ScenarioStatus.Contains("关键词")&&w.ScenarioStatus.Contains("完成"),"Latest result has stale outcome");Capture("keyword-completed",w);
                });
            }
            catch(Exception ex){failures++;records.Add(new{name="suite",passed=false,error=ex.ToString()});}
            finally
            {
                Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),version=typeof(Program).Assembly.GetName().Version?.ToString(),failures,tests=records,scope="Targeted simulation only: actual WPF windows and HTTP adapter, controlled 32-second model response and index-progress notifications. No real models, index rebuild, hardware or local tests."},new JsonSerializerOptions{WriteIndented=true}));
                foreach(var window in app.Windows.OfType<Window>().ToArray()){if(window is SearchWindow search)search.SkipExitOnce=true;window.Close();}app.Backend.Dispose();app.Shutdown(failures==0?0:1);
            }
        };return app.Run();
    }
    sealed class Server:IDisposable
    {
        readonly Store store;readonly HttpListener listener=new();public int Port{get;}public int Count=1,KeywordCount=1;public string? Error;public Task? Hold;
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Server(Store store){this.store=store;var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();Port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();listener.Prefixes.Add($"http://127.0.0.1:{Port}/");listener.Start();_=Listen();}
        async Task Listen(){try{while(listener.IsListening){var c=await listener.GetContextAsync();_=Handle(c);}}catch(HttpListenerException){}catch(ObjectDisposedException){}}
        async Task Handle(HttpListenerContext c)
        {
            try
            {
                using var doc=await JsonDocument.ParseAsync(c.Request.InputStream);string command=doc.RootElement.GetProperty("command").GetString()!;object result;
                if(command=="status")result=new{success=true,data=new{workspace="main",databaseFile=store.StateDirectory+"/main.sqlite",configFile=store.ConfigDirectory+"/main.yml"}};
                else
                {
                    bool model=command!="search.lexical";if(model){Entered.TrySetResult();if(Hold is Task held)await held;}
                    result=model&&Error!=null?new{success=false,error=new{message=Error}}:(object)new{success=true,data=new{results=Enumerable.Range(0,model?Count:KeywordCount).Select(_=>new{project="docs",path="match.md",virtualPath="soma://docs/match.md",snippet="needle is the indexed keyword in this original snippet"}).ToArray()}};
                }
                var bytes=JsonSerializer.SerializeToUtf8Bytes(result);c.Response.ContentType="application/json";c.Response.ContentLength64=bytes.Length;await c.Response.OutputStream.WriteAsync(bytes);
            }catch{c.Response.StatusCode=500;}finally{c.Response.Close();}
        }
        public void Dispose()=>listener.Close();
    }
}

public sealed partial class SearchWindow
{
    internal bool ScenarioRunning=>loadingSearch;
    internal string ScenarioStatus=>status.Text;
    internal string ScenarioEmpty=>emptyState.Text;
    internal string ScenarioHint=>hint.Text;
    internal object ScenarioSnapshot=>new{running=loadingSearch,status=status.Text,empty=emptyState.Text,hint=hint.Text,hits=DisplayedHits.Count};
    internal Task ScenarioSearch(string text,string searchMode){if(!CloudSandbox.Active)throw new InvalidOperationException();query.Text=text;mode.SelectedIndex=searchMode=="lexical"?1:0;return Search();}
    internal void ScenarioCancel()=>cancelWait.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
public sealed partial class Backend
{
    internal void ScenarioIndexPhase(int ready){if(!CloudSandbox.Active)throw new InvalidOperationException();Message="提取文件内容";Progress="main · "+ready+"/6048";Changed?.Invoke();}
}
