// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Media.Imaging;

namespace SomaFinder;

internal sealed record ResultThumbnail(BitmapSource? Image,string Caption,string Text,int? Page,double? Seconds,bool VerifiedTime,string Note);
internal static class ResultThumbnails
{
    const long Budget=48*1024*1024;
    sealed record Entry(ResultThumbnail Value,long Bytes,DateTime Used);
    static readonly Dictionary<string,Entry> cache=new();
    static readonly SemaphoreSlim slots=new(2,2);
    static readonly SemaphoreSlim[] keys=Enumerable.Range(0,31).Select(_=>new SemaphoreSlim(1,1)).ToArray();
    static long bytes;internal static int ActiveWorkers,PeakWorkers,WorkerStarts;
    internal static long CacheBytes{get{lock(cache)return bytes;}}
    internal static Action<Hit>? Updated;
    internal static string Stamp(Hit hit,string query)=>hit.FullPath+"\n"+hit.Modified?.Ticks+"\n"+hit.Snippet+"\n"+hit.MatchMode+"\n"+query;
    internal static void ClearLocation(Hit hit){hit.PreviewCaption="";hit.PreviewNote="";hit.MatchSeconds=null;hit.VerifiedTime=false;hit.Thumbnail=null;hit.Notify();Updated?.Invoke(hit);}
    internal static async Task<ResultThumbnail?> Get(Hit hit,string query,Preferences prefs,CancellationToken cancel)
    {
        string path=hit.FullPath,snippet=hit.Snippet,mode=hit.MatchMode;string stamp=Stamp(hit,query);
        var request=new ThumbnailRequest(path,Cap(snippet,4096),Cap(query,1024),mode,hit.SourceLine,640,Style:prefs.SearchLayout.MatchStyle);
        try
        {
            var fingerprint=await Task.Run(()=>Fingerprint(path,prefs),cancel);if(fingerprint==null){if(Stamp(hit,query)==stamp)ClearLocation(hit);return null;}
            string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint+JsonSerializer.Serialize(request))));
            ResultThumbnail? value;
            lock(cache){value=cache.TryGetValue(key,out var found)?found.Value:null;if(found!=null)cache[key]=found with{Used=DateTime.UtcNow};}
            if(value==null)
            {
                var gate=keys[(int)((uint)StringComparer.Ordinal.GetHashCode(key)%keys.Length)];await gate.WaitAsync(cancel);
                try
                {
                await slots.WaitAsync(cancel);
                try
                {
                    cancel.ThrowIfCancellationRequested();
                    lock(cache){value=cache.TryGetValue(key,out var found)?found.Value:null;}
                    if(value==null)
                    {
                        var reply=await Worker(request,cancel);
                        // A failed or timed-out match search must still be able to show a basic video cover.
                        if(reply.Png==null&&Rules.TypeMatches(Path.GetExtension(path),"视频")&&request.Snippet.Length>0)reply=await Worker(request with{Snippet="",Query=""},cancel);
                        value=await Task.Run(()=>Decode(reply),cancel);
                        lock(cache)
                        {
                            long size=1024+value.Text.Length*2+(value.Image==null?0:(long)value.Image.PixelWidth*value.Image.PixelHeight*4);
                            while(cache.Count>0&&(bytes+size>Budget||cache.Count>=192)){var oldest=cache.MinBy(x=>x.Value.Used);cache.Remove(oldest.Key);bytes-=oldest.Value.Bytes;}
                            cache[key]=new(value,size,DateTime.UtcNow);bytes+=size;
                        }
                    }
                }
                finally{slots.Release();}
                }
                finally{gate.Release();}
            }
            // Recheck both physical media identity and contents after the background work.
            if(await Task.Run(()=>Fingerprint(path,prefs),cancel)!=fingerprint){if(Stamp(hit,query)==stamp)ClearLocation(hit);return null;}
            cancel.ThrowIfCancellationRequested();if(Stamp(hit,query)!=stamp)return null;
            hit.PreviewCaption=value.Caption;hit.MatchSeconds=value.VerifiedTime?value.Seconds:null;hit.VerifiedTime=value.VerifiedTime;hit.PreviewNote=value.Note;hit.Notify();Updated?.Invoke(hit);return value;
        }
        catch(OperationCanceledException){throw;}
        catch{ return null; }
    }
    static string? Fingerprint(string path,Preferences prefs)
    {
        if(!Native.Verified(path,prefs))return null;var f=new FileInfo(path);return f.Exists?Rules.Normalize(path).ToUpperInvariant()+"\n"+f.Length+"\n"+f.LastWriteTimeUtc.Ticks:null;
    }
    static string Cap(string value,int length)=>value.Length<=length?value:value[..length];
    static ResultThumbnail Decode(ThumbnailReply reply)
    {
        BitmapSource? image=null;
        if(reply.Png is {Length:>0}){using var input=new MemoryStream(reply.Png);var decoded=new BitmapImage();decoded.BeginInit();decoded.CacheOption=BitmapCacheOption.OnLoad;decoded.StreamSource=input;decoded.EndInit();if(decoded.PixelWidth>8192||decoded.PixelHeight>8192)throw new IOException();decoded.Freeze();image=decoded;}
        return new(image,reply.Caption,reply.Text,reply.Page,reply.Seconds,reply.VerifiedTime,reply.Note);
    }
    internal static async Task<ThumbnailReply> Worker(ThumbnailRequest request,CancellationToken cancel,int timeoutMs=10000)
    {
        var psi=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppContext.BaseDirectory};
        psi.ArgumentList.Add("--thumbnail-worker");psi.Environment["SOMA_FINDER_THUMBNAIL"]=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);deadline.CancelAfter(timeoutMs);
        var started=ManagedProcess.Start(psi);using var process=started.Process;
        int active=Interlocked.Increment(ref ActiveWorkers);Interlocked.Increment(ref WorkerStarts);PeakWorkers=Math.Max(PeakWorkers,active);
        try
        {
            try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}
            using var output=started.Output;using var error=started.Error;
            var read=ReadBounded(output,12*1024*1024,deadline.Token);var errors=ReadBounded(error,16384,deadline.Token);
            var exit=process.WaitForExitAsync(deadline.Token);
            while(!exit.IsCompleted)
            {
                await Task.WhenAny(exit,Task.Delay(150,deadline.Token));deadline.Token.ThrowIfCancellationRequested();
                process.Refresh();if(!process.HasExited&&process.PrivateMemorySize64>384L*1024*1024)throw new IOException("Preview memory limit");
            }
            await Task.WhenAll(read,errors,exit);
            return JsonSerializer.Deserialize<ThumbnailReply>(await read)??new(null,"预览不可用");
        }
        catch(OperationCanceledException) when(!cancel.IsCancellationRequested){return new(null,"预览暂不可用");}
        finally{try{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}catch(InvalidOperationException){}Interlocked.Decrement(ref ActiveWorkers);}
    }
    static async Task<string> ReadBounded(StreamReader reader,int max,CancellationToken cancel)
    {
        var result=new StringBuilder();var buffer=new char[16384];while(true){int n=await reader.ReadAsync(buffer.AsMemory(),cancel);if(n==0)break;if(result.Length+n>max)throw new IOException("Thumbnail response too large");result.Append(buffer,0,n);}return result.ToString();
    }
}
