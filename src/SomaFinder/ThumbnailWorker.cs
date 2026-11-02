// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace SomaFinder;

internal sealed record ThumbnailRequest(string Path,string Snippet,string Query,string Mode,int? SourceLine,int Edge=640,string Body="",string Style="Bold");
internal sealed record ThumbnailReply(byte[]? Png,string Caption,string Text="",int? Page=null,double? Seconds=null,bool VerifiedTime=false,string Note="");

// Each expensive parse/decode runs in a disposable, kill-on-parent-exit process.
// No local model, document conversion app or backend mutation is used.
internal static class ThumbnailWorker
{
    internal static int Run()
    {
        Console.OutputEncoding=Encoding.UTF8;
        try
        {
            var json=Environment.GetEnvironmentVariable("SOMA_FINDER_THUMBNAIL")??"";
            var request=JsonSerializer.Deserialize<ThumbnailRequest>(Encoding.UTF8.GetString(Convert.FromBase64String(json)))??throw new IOException();
            Environment.SetEnvironmentVariable("SOMA_FINDER_THUMBNAIL",null);
            if(request.Edge is <128 or >1024||request.Snippet.Length>4096||request.Query.Length>1024||request.Body.Length>4096)throw new IOException();
            var reply=Read(request);if(reply.Png==null&&ThumbnailCache.ReadCached(request.Path) is {} cached)reply=reply with{Png=Png(cached)};Console.Write(JsonSerializer.Serialize(reply));return 0;
        }
        catch(Exception){Console.Write(JsonSerializer.Serialize(new ThumbnailReply(null,"预览不可用")));return 0;}
    }
    internal static ThumbnailReply Read(ThumbnailRequest r)
    {
        var type=Path.GetExtension(r.Path).ToLowerInvariant();var file=new FileInfo(r.Path);
        if(!file.Exists)throw new FileNotFoundException();
        if(type==".pdf"&&file.Length<512L*1024*1024)return PdfThumbnail.Read(r);
        if(Rules.TypeMatches(type,"视频"))return VideoThumbnail.Read(r);
        if(Rules.TypeMatches(type,"图片"))
        {
            using var input=new FileStream(r.Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            var header=BitmapDecoder.Create(input,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames[0];var size=ImagePreviewView.DecodeSize(header.PixelWidth,header.PixelHeight,r.Edge,r.Edge*r.Edge);input.Position=0;
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=size.Width;bitmap.DecodePixelHeight=size.Height;bitmap.StreamSource=input;bitmap.EndInit();
            if(bitmap.PixelHeight>8192||bitmap.PixelWidth>8192)throw new IOException();return new(Png(bitmap),"");
        }
        if(new[]{".docx",".pptx",".xlsx",".odt",".odp",".ods"}.Contains(type)&&file.Length<=128*1024*1024)return Office(r,type);
        if(Rules.TypeMatches(type,"代码")||new[]{".txt",".md",".csv",".log",".srt",".vtt",".rtf"}.Contains(type))
        {
            using var reader=new StreamReader(new FileStream(r.Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete),Encoding.UTF8,true);var chars=new char[1_048_576];int count=reader.ReadBlock(chars,0,chars.Length);var text=new string(chars,0,count);
            if(type==".rtf")
            {
                var document=new System.Windows.Documents.FlowDocument();var range=new System.Windows.Documents.TextRange(document.ContentStart,document.ContentEnd);using var stream=File.OpenRead(r.Path);range.Load(stream,DataFormats.Rtf);text=range.Text;
            }
            var excerpt=ContentLocation.Excerpt(text,r);return Card(r,excerpt.Matched?$"内容片段 · 第 {excerpt.Line} 行附近":"开头内容",excerpt.Text);
        }
        if(!string.IsNullOrWhiteSpace(r.Body))return Card(r,"索引内容片段",r.Body);
        if(Rules.TypeMatches(type,"文档")&&r.Snippet.Length>0)return Card(r,"索引内容片段",r.Snippet);
        return new(null,"文件预览");
    }
    static XDocument Xml(ZipArchiveEntry entry)
    {
        if(entry.Length>12*1024*1024)throw new IOException("Document part too large");
        using var input=entry.Open();using var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=12*1024*1024});return XDocument.Load(reader);
    }
    static string Paragraphs(XDocument doc)=>string.Join('\n',doc.Descendants().Where(e=>e.Name.LocalName=="p").Select(p=>string.Concat(p.Descendants().Where(e=>e.Name.LocalName is "t" or "span").Select(e=>e.Value))));
    static ThumbnailReply Office(ThumbnailRequest r,string type)
    {
        using var archive=ZipFile.OpenRead(r.Path);
        if(type==".docx")
        {
            var entry=archive.GetEntry("word/document.xml")??throw new IOException();var text=Paragraphs(Xml(entry));var excerpt=ContentLocation.Excerpt(text,r);
            return Card(r,excerpt.Matched?"命中段落 · 内容片段":"开头内容 · 内容片段",excerpt.Text);
        }
        if(type==".pptx")
        {
            var presentation=Xml(archive.GetEntry("ppt/presentation.xml")??throw new IOException());var relationships=Xml(archive.GetEntry("ppt/_rels/presentation.xml.rels")??throw new IOException());
            var targets=relationships.Descendants().Where(e=>e.Name.LocalName=="Relationship"&&e.Attribute("TargetMode")?.Value!="External").ToDictionary(e=>e.Attribute("Id")!.Value,e=>e.Attribute("Target")!.Value);
            var slides=new List<string>();
            foreach(var slide in presentation.Descendants().Where(e=>e.Name.LocalName=="sldId").Take(400))
            {
                var id=slide.Attributes().FirstOrDefault(a=>a.Name.LocalName=="id"&&a.Name.NamespaceName.Length>0)?.Value;
                if(id==null||!targets.TryGetValue(id,out var target))continue;
                var key=new Uri(new Uri("https://local/ppt/"),target).AbsolutePath.TrimStart('/');var entry=archive.GetEntry(key);if(entry!=null)slides.Add(Paragraphs(Xml(entry)));
            }
            int best=Best(slides,r);return Card(r,$"第 {best+1} 张幻灯片 · 内容片段",slides.ElementAtOrDefault(best)??"",best+1);
        }
        if(type==".xlsx")
        {
            var shared=archive.GetEntry("xl/sharedStrings.xml") is {} strings?Xml(strings).Descendants().Where(e=>e.Name.LocalName=="si").Select(e=>string.Concat(e.Descendants().Where(t=>t.Name.LocalName=="t").Select(t=>t.Value))).ToArray():Array.Empty<string>();
            var sheets=new List<(string Name,string Text)>();
            var book=Xml(archive.GetEntry("xl/workbook.xml")??throw new IOException());var rels=Xml(archive.GetEntry("xl/_rels/workbook.xml.rels")??throw new IOException());
            var targets=rels.Descendants().Where(e=>e.Name.LocalName=="Relationship"&&e.Attribute("TargetMode")?.Value!="External").ToDictionary(e=>e.Attribute("Id")!.Value,e=>e.Attribute("Target")!.Value);
            foreach(var sheet in book.Descendants().Where(e=>e.Name.LocalName=="sheet").Take(100))
            {
                var id=sheet.Attributes().FirstOrDefault(a=>a.Name.LocalName=="id")?.Value;if(id==null||!targets.TryGetValue(id,out var target))continue;
                var key=new Uri(new Uri("https://local/xl/"),target).AbsolutePath.TrimStart('/');var entry=archive.GetEntry(key);if(entry==null)continue;
                var text=string.Join('\n',Xml(entry).Descendants().Where(e=>e.Name.LocalName=="row").Take(20000).Select(row=>string.Join("  |  ",row.Elements().Where(e=>e.Name.LocalName=="c").Select(cell=>
                {string value=cell.Descendants().FirstOrDefault(e=>e.Name.LocalName is "v" or "t")?.Value??"";if(cell.Attribute("t")?.Value=="s"&&int.TryParse(value,out var index)&&index>=0&&index<shared.Length)value=shared[index];return cell.Attribute("r")?.Value+": "+value;}))));
                sheets.Add((sheet.Attribute("name")?.Value??"工作表",text));
            }
            int best=Best(sheets.Select(s=>s.Text).ToList(),r);var chosen=sheets.ElementAtOrDefault(best);var excerpt=ContentLocation.Excerpt(chosen.Text??"",r);
            return Card(r,$"{chosen.Name} · {(excerpt.Matched?"命中区域":"开头区域")} · 内容片段",excerpt.Text);
        }
        var content=Xml(archive.GetEntry("content.xml")??throw new IOException());var plain=string.Join('\n',content.Descendants().Where(e=>e.Name.LocalName is "p" or "h").Select(e=>e.Value));var selected=ContentLocation.Excerpt(plain,r);return Card(r,selected.Matched?"命中内容 · 内容片段":"开头内容 · 内容片段",selected.Text);
    }
    static int Best(IReadOnlyList<string> texts,ThumbnailRequest r){int best=0,score=0;for(int i=0;i<texts.Count;i++){int s=ContentLocation.Score(texts[i],r);if(s>score){best=i;score=s;}}return best;}
    internal static ThumbnailReply Card(ThumbnailRequest r,string label,string text,int? page=null)
    {
        // A visibly labelled excerpt, never a fabricated screenshot of original pagination.
        var visual=new DrawingVisual();int width=480,height=620;
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,width,height));
            var face=new Typeface("Microsoft YaHei UI, Segoe UI");
            var title=new FormattedText(label,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,face,18,Brushes.DimGray,1){MaxTextWidth=424,MaxTextHeight=40,Trimming=TextTrimming.CharacterEllipsis};dc.DrawText(title,new Point(28,22));
            dc.DrawLine(new Pen(Brushes.LightGray,1),new Point(28,70),new Point(452,70));
            string body=text.Length>2200?text[..2200]+"…":text;var formatted=new FormattedText(body,System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,face,19,Brushes.Black,1){MaxTextWidth=424,MaxTextHeight=500,Trimming=TextTrimming.CharacterEllipsis};
            foreach(var term in ContentLocation.Terms(r.Query))
            {
                // Decoration only; the original excerpt is not changed or summarized.
                for(int pos=body.IndexOf(term,StringComparison.OrdinalIgnoreCase);pos>=0;pos=body.IndexOf(term,pos+Math.Max(1,term.Length),StringComparison.OrdinalIgnoreCase))
                    formatted.SetFontWeight(FontWeights.Bold,pos,term.Length);
            }
            dc.DrawText(formatted,new Point(28,90));
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return new(Png(bitmap),label,text.Length>2200?text[..2200]:text,page);
    }
    internal static byte[] Png(BitmapSource bitmap){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();}
}
