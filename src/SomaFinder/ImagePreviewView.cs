// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

// The same view moves between docked and floating hosts; its image coordinates survive reparenting.
internal sealed class ImagePreviewView : DockPanel
{
    readonly Canvas surface=new(){ClipToBounds=true,Background=Brushes.Transparent};
    readonly Image image=new(){Stretch=Stretch.Fill,IsHitTestVisible=false};
    readonly Button fitButton;
    readonly Dictionary<int,(TouchDevice Device,Point Point)> touches=new();
    readonly string? path;readonly Func<bool>? mayRead;
    readonly int sourceWidth,sourceHeight;readonly long fileSize;readonly DateTime modified;
    Size viewport;double zoom=1,scale=1;Vector offset;bool releasing,qualityRequested;
    internal FrameworkElement TouchSurface=>surface;
    internal double ZoomFactor=>zoom;
    internal Rect ImageBounds=>new(offset.X,offset.Y,sourceWidth*scale,sourceHeight*scale);
    internal BitmapSource Bitmap=>(BitmapSource)image.Source;
    internal Button FitButton=>fitButton;
    internal Point CenterImagePoint=>new((viewport.Width/2-offset.X)/scale,(viewport.Height/2-offset.Y)/scale);

    internal ImagePreviewView(BitmapSource bitmap,int width,int height,string? file=null,Func<bool>? canRead=null)
    {
        sourceWidth=width;sourceHeight=height;path=file;mayRead=canRead;
        if(file!=null){var info=new FileInfo(file);fileSize=info.Length;modified=info.LastWriteTimeUtc;}
        image.Source=bitmap;image.Width=width;image.Height=height;
        fitButton=UI.Button("适应窗口",Fit);fitButton.ToolTip="完整显示图片；双指缩放，放大后单指拖动查看";
        fitButton.FontSize=12;fitButton.Padding=new Thickness(6,4,6,4);Children.Add(surface);surface.Children.Add(image);
        surface.SizeChanged+=(_,_)=>Resize();
        surface.PreviewTouchDown+=HandleTouchDown;
        surface.PreviewTouchMove+=HandleTouchMove;
        surface.PreviewTouchUp+=HandleTouchUp;
        surface.LostTouchCapture+=(_,e)=>{if(!releasing)touches.Remove(e.TouchDevice.Id);};
        Unloaded+=(_,_)=>ReleaseTouches();
        Loaded+=(_,_)=>{Resize();RequestQuality();};
    }
    internal static async Task<ImagePreviewView> LoadAsync(string path,Func<bool> mayRead)
    {
        var loaded=await Task.Run(()=>
        {
            if(!mayRead())throw new IOException("文件所在磁盘已离线或发生变化。");
            return OrientedImage.Load(path,1200,1_440_000);
        });
        return new ImagePreviewView(loaded.Bitmap,loaded.Width,loaded.Height,path,mayRead);
    }
    internal static (int Width,int Height) DecodeSize(int width,int height,int maxSide=4096,int maxPixels=8_000_000)
    {
        if(width<=0||height<=0)throw new FileFormatException("图片尺寸无效。");
        double ratio=Math.Min(1,Math.Min((double)maxSide/Math.Max(width,height),Math.Sqrt((double)maxPixels/width/height)));
        return (Math.Max(1,(int)(width*ratio)),Math.Max(1,(int)(height*ratio)));
    }
    async void RequestQuality()
    {
        if(path==null||qualityRequested||zoom<1.2||!IsLoaded)return;
        qualityRequested=true;var size=DecodeSize(sourceWidth,sourceHeight);
        if(size.Width<=Bitmap.PixelWidth&&size.Height<=Bitmap.PixelHeight)return;
        try
        {
            // One bounded upgrade per image, off the UI thread. OnLoad releases the file handle.
            var sharper=await Task.Run(()=>
            {
                if(mayRead?.Invoke()==false)throw new IOException("图片目录已不可用。");
                var info=new FileInfo(path);if(info.Length!=fileSize||info.LastWriteTimeUtc!=modified)throw new IOException("图片已更改，请重新选择。");
                return OrientedImage.Load(path).Bitmap;
            });
            if(IsLoaded)image.Source=sharper;else qualityRequested=false;
        }
        catch(Exception){fitButton.ToolTip="高清预览暂不可用，仍可查看当前图片；点击恢复完整显示。";}
    }
    double FitScale=>Math.Max(.000001,Math.Min(viewport.Width/sourceWidth,viewport.Height/sourceHeight));
    void Resize()
    {
        var next=new Size(surface.ActualWidth,surface.ActualHeight);if(next.Width<=0||next.Height<=0)return;
        var anchor=viewport.Width>0?CenterImagePoint:new Point(sourceWidth/2d,sourceHeight/2d);
        viewport=next;scale=FitScale*zoom;offset=new Vector(viewport.Width/2-anchor.X*scale,viewport.Height/2-anchor.Y*scale);Draw();
    }
    internal void Fit(){zoom=1;scale=FitScale;offset=new Vector((viewport.Width-sourceWidth*scale)/2,(viewport.Height-sourceHeight*scale)/2);Draw();}
    void Draw()
    {
        var width=sourceWidth*scale;var height=sourceHeight*scale;
        offset.X=width<=viewport.Width?(viewport.Width-width)/2:Math.Clamp(offset.X,viewport.Width-width,0);
        offset.Y=height<=viewport.Height?(viewport.Height-height)/2:Math.Clamp(offset.Y,viewport.Height-height,0);
        image.RenderTransform=new MatrixTransform(scale,0,0,scale,offset.X,offset.Y);
    }
    void HandleTouchDown(object? sender,TouchEventArgs e)
    {
        e.Handled=true;
        // Disable mouse promotion and let this view own the complete finger sequence.
        touches[e.TouchDevice.Id]=(e.TouchDevice,e.GetTouchPoint(surface).Position);e.TouchDevice.Capture(surface);
    }
    void HandleTouchMove(object? sender,TouchEventArgs e)
    {
        if(!touches.TryGetValue(e.TouchDevice.Id,out var previous))return;e.Handled=true;
        var before=touches.Values.Take(2).Select(t=>t.Point).ToArray();var point=e.GetTouchPoint(surface).Position;
        touches[e.TouchDevice.Id]=(e.TouchDevice,point);
        if(before.Length==1){offset+=point-previous.Point;Draw();return;}
        var after=touches.Values.Take(2).Select(t=>t.Point).ToArray();double distance=(before[1]-before[0]).Length;
        if(distance<4)return;
        var centerBefore=new Point((before[0].X+before[1].X)/2,(before[0].Y+before[1].Y)/2);
        var centerAfter=new Point((after[0].X+after[1].X)/2,(after[0].Y+after[1].Y)/2);
        var anchor=new Point((centerBefore.X-offset.X)/scale,(centerBefore.Y-offset.Y)/scale);
        zoom=Math.Clamp(zoom*(after[1]-after[0]).Length/distance,1,16);scale=FitScale*zoom;
        offset=new Vector(centerAfter.X-anchor.X*scale,centerAfter.Y-anchor.Y*scale);Draw();RequestQuality();
    }
    void HandleTouchUp(object? sender,TouchEventArgs e)
    {
        if(!touches.Remove(e.TouchDevice.Id))return;e.Handled=true;
        releasing=true;try{e.TouchDevice.Capture(null);}finally{releasing=false;}
        // Other fingers retain their current positions, so pinch -> drag has no jump.
    }
    void ReleaseTouches()
    {
        releasing=true;try{foreach(var touch in touches.Values.ToArray())touch.Device.Capture(null);}finally{touches.Clear();releasing=false;}
    }
}
