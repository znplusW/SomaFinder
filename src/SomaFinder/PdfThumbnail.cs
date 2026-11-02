// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

internal static class PdfThumbnail
{
    internal static ThumbnailReply Read(ThumbnailRequest request)
    {
        NativeLibrary.SetDllImportResolver(typeof(PdfThumbnail).Assembly,(name,assembly,path)=>name=="pdfium"?NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,"Components","Media","pdfium.dll")):IntPtr.Zero);
        FPDF_InitLibrary();nint document=0;
        try
        {
            document=FPDF_LoadDocument(request.Path,null);if(document==0)throw new IOException("PDF cannot be opened");
            int count=FPDF_GetPageCount(document);if(count<=0)throw new IOException();
            int best=0,score=0;var clock=Stopwatch.StartNew();
            if(request.Query.Length>0||request.Snippet.Length>0)
                for(int i=0;i<Math.Min(count,500)&&clock.ElapsedMilliseconds<1800;i++)
                {
                    nint page=FPDF_LoadPage(document,i);if(page==0)continue;
                    try
                    {
                        nint text=FPDFText_LoadPage(page);if(text==0)continue;
                        try{int chars=Math.Clamp(FPDFText_CountChars(text),0,200000);var buffer=new ushort[chars+1];int used=FPDFText_GetText(text,0,chars,buffer);var body=new string(buffer.Take(Math.Max(0,used-1)).Select(c=>(char)c).ToArray());int s=ContentLocation.Score(body,request);if(s>score){best=i;score=s;}}
                        finally{FPDFText_ClosePage(text);}
                    }
                    finally{FPDF_ClosePage(page);}
                }
            nint selected=FPDF_LoadPage(document,best);if(selected==0)throw new IOException();
            try
            {
                double w=FPDF_GetPageWidth(selected),h=FPDF_GetPageHeight(selected);if(!double.IsFinite(w)||!double.IsFinite(h)||w<=0||h<=0)throw new IOException();
                double scale=Math.Min((double)request.Edge/Math.Max(w,h),1.5);int width=Math.Max(1,(int)(w*scale)),height=Math.Max(1,(int)(h*scale));
                nint bitmap=FPDFBitmap_Create(width,height,1);if(bitmap==0)throw new IOException();
                try
                {
                    FPDFBitmap_FillRect(bitmap,0,0,width,height,0xffffffff);FPDF_RenderPageBitmap(bitmap,selected,0,0,width,height,0,1);
                    int stride=FPDFBitmap_GetStride(bitmap);var pixels=new byte[stride*height];Marshal.Copy(FPDFBitmap_GetBuffer(bitmap),pixels,0,pixels.Length);
                    var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,stride);image.Freeze();
                    return new(ThumbnailWorker.Png(image),score>0?$"命中内容 · 第 {best+1} 页":"首页",Page:best+1);
                }
                finally{FPDFBitmap_Destroy(bitmap);}
            }
            finally{FPDF_ClosePage(selected);}
        }
        finally{if(document!=0)FPDF_CloseDocument(document);FPDF_DestroyLibrary();}
    }
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern void FPDF_InitLibrary();
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern void FPDF_DestroyLibrary();
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern nint FPDF_LoadDocument([MarshalAs(UnmanagedType.LPUTF8Str)]string path,[MarshalAs(UnmanagedType.LPUTF8Str)]string? password);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern void FPDF_CloseDocument(nint document);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern int FPDF_GetPageCount(nint document);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern nint FPDF_LoadPage(nint document,int page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern void FPDF_ClosePage(nint page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern double FPDF_GetPageWidth(nint page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern double FPDF_GetPageHeight(nint page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern nint FPDFText_LoadPage(nint page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern void FPDFText_ClosePage(nint page);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern int FPDFText_CountChars(nint text);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern int FPDFText_GetText(nint text,int start,int count,[Out]ushort[] result);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern nint FPDFBitmap_Create(int width,int height,int alpha);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern void FPDFBitmap_Destroy(nint bitmap);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern void FPDFBitmap_FillRect(nint bitmap,int x,int y,int width,int height,uint color);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern void FPDF_RenderPageBitmap(nint bitmap,nint page,int x,int y,int width,int height,int rotate,int flags);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern int FPDFBitmap_GetStride(nint bitmap);
    [DllImport("pdfium",CallingConvention=CallingConvention.Cdecl)]static extern nint FPDFBitmap_GetBuffer(nint bitmap);
}
