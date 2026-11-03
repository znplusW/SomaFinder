// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

// Normalize pixels once, before preview layout or PNG thumbnail encoding.
internal static class OrientedImage
{
    internal static (BitmapSource Bitmap,int Width,int Height) Load(string path,int maxSide=4096,int maxPixels=8_000_000)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        var header=BitmapDecoder.Create(input,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames[0];
        int width=header.PixelWidth,height=header.PixelHeight;int orientation=ReadOrientation(header);
        var size=ImagePreviewView.DecodeSize(width,height,maxSide,maxPixels);input.Position=0;
        var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth=size.Width;bitmap.DecodePixelHeight=size.Height;bitmap.StreamSource=input;bitmap.EndInit();
        if(orientation>=5)(width,height)=(height,width);
        return (Apply(bitmap,orientation),width,height);
    }
    static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if(frame.Metadata is not BitmapMetadata metadata)return 1;
            foreach(var query in new[]{"/app1/ifd/{ushort=274}","/ifd/{ushort=274}"})
            {
                try
                {
                    var value=metadata.GetQuery(query);
                    if(value is ushort number&&number is >=1 and <=8)return number;
                }
                catch(Exception ex)when(ex is NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException){}
            }
        }
        catch(Exception ex)when(ex is NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException){}
        return 1;
    }
    static BitmapSource Apply(BitmapSource bitmap,int orientation)
    {
        var matrix=orientation switch
        {
            2=>new Matrix(-1,0,0,1,0,0),
            3=>new Matrix(-1,0,0,-1,0,0),
            4=>new Matrix(1,0,0,-1,0,0),
            5=>new Matrix(0,1,1,0,0,0),
            6=>new Matrix(0,1,-1,0,0,0),
            7=>new Matrix(0,-1,-1,0,0,0),
            8=>new Matrix(0,-1,1,0,0,0),
            _=>Matrix.Identity
        };
        BitmapSource result=matrix.IsIdentity?bitmap:new TransformedBitmap(bitmap,new MatrixTransform(matrix));
        result.Freeze();return result;
    }
}
