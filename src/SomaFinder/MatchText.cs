// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace SomaFinder;

// One matching rule for both summary surfaces. This decorates returned text only.
public static class MatchText
{
    public static readonly DependencyProperty TextProperty=DependencyProperty.RegisterAttached("Text",typeof(string),typeof(MatchText),new PropertyMetadata("",Changed));
    public static readonly DependencyProperty QueryProperty=DependencyProperty.RegisterAttached("Query",typeof(string),typeof(MatchText),new PropertyMetadata("",Changed));
    public static readonly DependencyProperty StyleNameProperty=DependencyProperty.RegisterAttached("StyleName",typeof(string),typeof(MatchText),new PropertyMetadata("Bold",Changed));
    public static void SetText(DependencyObject d,string value)=>d.SetValue(TextProperty,value);
    public static string GetText(DependencyObject d)=>d.GetValue(TextProperty) as string??"";
    public static void SetQuery(DependencyObject d,string value)=>d.SetValue(QueryProperty,value);
    public static string GetQuery(DependencyObject d)=>d.GetValue(QueryProperty) as string??"";
    public static void SetStyleName(DependencyObject d,string value)=>d.SetValue(StyleNameProperty,value);
    public static string GetStyleName(DependencyObject d)=>d.GetValue(StyleNameProperty) as string??"Bold";
    static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {if(d is TextBlock block){block.Inlines.Clear();foreach(var run in Runs(GetText(d),GetQuery(d),GetStyleName(d)))block.Inlines.Add(run);}}
    internal static IEnumerable<Run> Runs(string text,string query,string style)
    {
        var terms=new List<string>();bool negate=false;
        foreach(Match match in Regex.Matches(query,"-?\"[^\"]+\"|[^\\s]+"))
        {
            var term=match.Value.Trim('(',')',',','，','。','?','？');
            if(term.Equals("NOT",StringComparison.OrdinalIgnoreCase)){negate=true;continue;}
            if(term.Equals("AND",StringComparison.OrdinalIgnoreCase)||term.Equals("OR",StringComparison.OrdinalIgnoreCase))continue;
            if(!negate&&!term.StartsWith('-')){term=term.Trim('"');if(term.Length>0)terms.Add(term);}negate=false;
        }
        var words=terms.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(t=>t.Length).ToArray();
        for(int at=0;at<text.Length;)
        {
            int next=text.Length;string? found=null;
            foreach(var word in words){int pos=text.IndexOf(word,at,StringComparison.OrdinalIgnoreCase);if(pos>=0&&pos<next){next=pos;found=word;}}
            if(next>at)yield return new Run(text[at..next]);
            if(found==null)yield break;
            var run=new Run(text.Substring(next,found.Length));
            if(style=="Yellow"){run.Background=Brushes.Gold;run.Foreground=Brushes.Black;}else run.FontWeight=FontWeights.Bold;
            yield return run;at=next+found.Length;
        }
    }
}
