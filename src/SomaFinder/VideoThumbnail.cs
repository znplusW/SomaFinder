// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

internal static class VideoThumbnail
{
    internal static ThumbnailReply Read(ThumbnailRequest request)
    {
        var metadata=Run("ffprobe",new[]{"-v","error","-protocol_whitelist","file,pipe","-show_format","-show_streams","-of","json",request.Path},2_000_000,2500);
        using var json=JsonDocument.Parse(metadata);var root=json.RootElement;double duration=0;
        if(root.TryGetProperty("format",out var format)&&format.TryGetProperty("duration",out var length))double.TryParse(length.GetString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out duration);
        double? time=null;bool ambiguous=false;var matches=new List<double>();
        var streams=root.GetProperty("streams").EnumerateArray().Where(s=>s.TryGetProperty("codec_type",out var t)&&t.GetString()=="subtitle").ToArray();
        // Image subtitles, external subtitles and non-zero/unknown timing cannot invent a match.
        if(duration>0&&double.IsFinite(duration)&&streams.Length<=8&&ContentLocation.Evidence(request.Snippet).Length>=12)
        {
            foreach(var stream in streams)
            {
                if(!new[]{"subrip","webvtt","ass","ssa","mov_text","text"}.Contains(stream.GetProperty("codec_name").GetString()))continue;
                try
                {
                    int index=stream.GetProperty("index").GetInt32();
                    var bytes=Run("ffmpeg",new[]{"-v","error","-nostdin","-threads","1","-protocol_whitelist","file,pipe","-copyts","-start_at_zero","-i",request.Path,"-map","0:"+index,"-f","webvtt","pipe:1"},2_000_000,2000);
                    var cues=ContentLocation.ParseVtt(Encoding.UTF8.GetString(bytes),duration);var candidate=ContentLocation.LocateCue(cues,request.Snippet);
                    if(candidate.HasValue)matches.Add(candidate.Value);
                    else if(ContentLocation.Normalize(string.Join(' ',cues.Select(c=>c.Text))).Contains(ContentLocation.Evidence(request.Snippet),StringComparison.Ordinal))ambiguous=true;
                }
                catch{ambiguous=true;}
            }
            if(!ambiguous&&matches.Count>0&&matches.Max()-matches.Min()<1)time=matches[0];
        }
        double cover=duration>0?Math.Min(1,duration/10):0;double position=time??cover;
        byte[] Frame(double at)=>Run("ffmpeg",new[]{"-v","error","-nostdin","-threads","1","-filter_threads","1","-protocol_whitelist","file,pipe","-ss",at.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),"-i",request.Path,"-an","-sn","-frames:v","1","-vf",$"scale={request.Edge}:{request.Edge}:force_original_aspect_ratio=decrease","-f","image2pipe","-c:v","png","pipe:1"},8_000_000,3500);
        byte[] image;
        try{image=Frame(position);if(image.Length<8)throw new IOException();}
        catch when(time.HasValue){time=null;image=Frame(cover);}
        return new(image,time.HasValue?"匹配字幕片段 · "+ContentLocation.Timestamp(time.Value):"视频封面",Seconds:time,VerifiedTime:time.HasValue,Note:time.HasValue?"根据同一视频的内嵌字幕及唯一原文片段定位；该时间的画面不一定呈现字幕描述。":"没有可验证的片段时间，显示普通封面。");
    }
    internal static byte[] Run(string name,IEnumerable<string> arguments,int maxBytes,int milliseconds)
    {
        var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"Components","Media",name+".exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in arguments)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new IOException();var error=process.StandardError.ReadToEndAsync();
        var read=Task.Run(async()=>{using var output=new MemoryStream();var buffer=new byte[65536];while(true){int count=await process.StandardOutput.BaseStream.ReadAsync(buffer);if(count==0)break;if(output.Length+count>maxBytes)throw new IOException("Preview output limit");output.Write(buffer,0,count);}return output.ToArray();});
        try
        {
            if(!Task.WhenAll(read,error,process.WaitForExitAsync()).Wait(milliseconds))throw new TimeoutException();
            if(process.ExitCode!=0)throw new IOException("Preview decoder failed");return read.Result;
        }
        finally{try{if(!process.HasExited){process.Kill(true);process.WaitForExit(1000);}}catch(InvalidOperationException){}}
    }
}
