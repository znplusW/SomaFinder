// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Text.RegularExpressions;

namespace SomaFinder;

// Coordinates are evidence, not guesses from a document's length or a video's duration.
internal static class ContentLocation
{
    internal static string Normalize(string value)=>new string(value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    internal static string Evidence(string snippet)=>Normalize(snippet.Trim().TrimEnd('.', '…'));
    internal static string[] Terms(string query)
    {
        var terms=new List<string>();bool negative=false;
        foreach(Match m in Regex.Matches(query,"-?\"[^\"]+\"|[^\\s]+"))
        {
            var s=m.Value.Trim('(',')',',','，','。','?','？');
            if(s.Equals("NOT",StringComparison.OrdinalIgnoreCase)){negative=true;continue;}
            if(s.Equals("AND",StringComparison.OrdinalIgnoreCase)||s.Equals("OR",StringComparison.OrdinalIgnoreCase))continue;
            if(!negative&&!s.StartsWith('-')){s=Normalize(s);if(s.Length>0)terms.Add(s);}negative=false;
        }
        return terms.Distinct().ToArray();
    }
    internal static int Score(string text,ThumbnailRequest request)
    {
        var normalized=Normalize(text);var evidence=Evidence(request.Snippet);
        if(evidence.Length>=8&&normalized.Contains(evidence,StringComparison.Ordinal))return 1000+Math.Min(1000,evidence.Length);
        // A semantic query is not an instruction to run a second language model.
        return request.Mode=="vector"?0:Terms(request.Query).Count(t=>normalized.Contains(t,StringComparison.Ordinal));
    }
    internal static (string Text,int Line,bool Matched) Excerpt(string text,ThumbnailRequest request,int radius=7)
    {
        var lines=text.Replace("\r","").Split('\n');int best=0,score=0;
        for(int i=0;i<lines.Length;i++)
        {
            var s=Score(string.Join('\n',lines.Skip(i).Take(3)),request);
            if(s>score){score=s;best=i;}
        }
        int start=score>0?Math.Max(0,best-2):0;
        return(string.Join('\n',lines.Skip(start).Take(radius*2)),start+1,score>0);
    }

    internal sealed record Cue(double Start,double End,string Text);
    internal static List<Cue> ParseVtt(string text,double duration)
    {
        var result=new List<Cue>();
        foreach(var block in Regex.Split(text.Replace("\r", ""),"\n[ \t]*\n"))
        {
            var lines=block.Split('\n');int at=Array.FindIndex(lines,l=>l.Contains(" --> ",StringComparison.Ordinal));if(at<0)continue;
            var pair=lines[at].Split(" --> ");
            if(pair.Length!=2||!Time(pair[0],out double start)||!Time(pair[1].Split(' ')[0],out double end)||start<0||end<=start||end>duration+.5)throw new InvalidDataException("Subtitle timestamp outside media timeline");
            var body=System.Net.WebUtility.HtmlDecode(Regex.Replace(string.Join('\n',lines.Skip(at+1)),"<[^>]*>",""));
            if(Normalize(body).Length>0)result.Add(new(start,end,body));
        }
        return result;
    }
    static bool Time(string text,out double seconds)
    {
        seconds=0;var parts=text.Trim().Replace(',','.').Split(':');if(parts.Length is <2 or >3)return false;
        foreach(var part in parts){if(!double.TryParse(part,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<0)return false;seconds=seconds*60+n;}
        return double.IsFinite(seconds);
    }
    // Only a sufficiently long, unique excerpt in contiguous cues is accepted.
    // Bare search words, source line numbers and external same-name subtitles are never time evidence.
    internal static double? LocateCue(IReadOnlyList<Cue> cues,string snippet)
    {
        var needle=Evidence(snippet);if(needle.Length<12)return null;
        var text=new StringBuilder();var spans=new List<(int Start,int End,Cue Cue)>();double prior=-1;
        foreach(var cue in cues)
        {
            if(cue.Start<prior)return null;
            if(spans.Count>0&&cue.Start-spans[^1].Cue.End>3)text.Append('|');
            int start=text.Length;text.Append(Normalize(cue.Text));spans.Add((start,text.Length,cue));prior=cue.Start;
        }
        string body=text.ToString();int position=body.IndexOf(needle,StringComparison.Ordinal);
        if(position<0||body.IndexOf(needle,position+1,StringComparison.Ordinal)>=0)return null;
        var span=spans.FirstOrDefault(s=>s.Start<=position&&s.End>position);
        return span.Cue==null?null:(span.Cue.Start+span.Cue.End)/2;
    }
    internal static string Timestamp(double seconds)=>TimeSpan.FromSeconds(seconds).ToString(seconds>=3600?@"hh\:mm\:ss":@"mm\:ss");
}
