// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;

// Create once and reuse on NIM_MODIFY. Add orange pause bars to the existing logo;
// a narrow white keyline separates the bars on light and dark taskbars.
internal static class PauseBadge
{
    internal static nint Create(nint original)
    {
        const int size=32;
        nint dc=CreateCompatibleDC(0),bitmap=0,mask=0;IconInfo source=default;
        try
        {
            var info=new BitmapInfo{Size=40,Width=size,Height=-size,Planes=1,Bits=32};
            bitmap=CreateDIBSection(dc,ref info,0,out var bits,0,0);
            if(bitmap==0||dc==0)return 0;
            // Copy source pixels directly: DrawIconEx blends the antialiased edges,
            // which would slightly change the original artwork outside the badge.
            if(!GetIconInfo(original,out source)||source.Color==0)return 0;
            if(GetDIBits(dc,source.Color,0,size,bits,ref info,0)!=size)return 0;
            var pixels=new int[size*size];Marshal.Copy(bits,pixels,0,pixels.Length);
            // Legacy icons carry transparency in the AND mask, not in alpha. Once
            // an alpha badge is added Windows uses alpha for the entire icon, so
            // preserve the original visible pixels by converting that mask first.
            if(!pixels.Any(pixel=>(unchecked((uint)pixel)>>24)!=0))
            {
                if(source.Mask==0||GetDIBits(dc,source.Mask,0,size,bits,ref info,0)!=size)return 0;
                var transparency=new int[pixels.Length];Marshal.Copy(bits,transparency,0,transparency.Length);
                for(int i=0;i<pixels.Length;i++)pixels[i]=(transparency[i]&0x00ffffff)!=0?0:pixels[i]|unchecked((int)0xff000000);
            }
            for(int y=20;y<size;y++)for(int x=20;x<size;x++)
            {
                // Supersample the small geometry without resampling the original icon.
                uint originalPixel=unchecked((uint)pixels[y*size+x]);
                int a=0,r=0,g=0,b=0;
                for(int sy=0;sy<4;sy++)for(int sx=0;sx<4;sx++)
                {
                    double px=x+(sx+.5)/4,py=y+(sy+.5)/4;
                    bool inside=Bar(px,py,21,0)||Bar(px,py,27,0);
                    bool outline=Bar(px,py,21,.325)||Bar(px,py,27,.325);
                    uint color=inside?0xffe87900u:outline?0xffffffffu:originalPixel;
                    a+=(int)(color>>24);r+=(int)((color>>16)&255);g+=(int)((color>>8)&255);b+=(int)(color&255);
                }
                pixels[y*size+x]=unchecked((int)((uint)(a/16)<<24|(uint)(r/16)<<16|(uint)(g/16)<<8|(uint)(b/16)));
            }
            Marshal.Copy(pixels,0,bits,pixels.Length);
            mask=CreateBitmap(size,size,1,1,new byte[128]);
            var data=new IconInfo{Icon=true,Color=bitmap,Mask=mask};
            return mask==0?0:CreateIconIndirect(ref data);
        }
        finally{if(bitmap!=0)DeleteObject(bitmap);if(mask!=0)DeleteObject(mask);if(source.Color!=0)DeleteObject(source.Color);if(source.Mask!=0)DeleteObject(source.Mask);if(dc!=0)DeleteDC(dc);}
    }
    static bool Bar(double x,double y,double left,double border)=>x>=left-border&&x<=left+3.5+border&&y>=20.5-border&&y<=31.5+border;
    [StructLayout(LayoutKind.Sequential)]struct BitmapInfo{internal uint Size;internal int Width,Height;internal ushort Planes,Bits;internal uint Compression,ImageSize;internal int X,Y;internal uint Used,Important;}
    [StructLayout(LayoutKind.Sequential)]struct IconInfo{[MarshalAs(UnmanagedType.Bool)]internal bool Icon;internal uint X,Y;internal nint Mask,Color;}
    [DllImport("gdi32.dll")]static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")]static extern nint CreateBitmap(int width,int height,uint planes,uint bits,byte[] data);
    [DllImport("user32.dll")]static extern bool GetIconInfo(nint icon,out IconInfo info);
    [DllImport("gdi32.dll")]static extern int GetDIBits(nint dc,nint bitmap,uint start,uint lines,nint bits,ref BitmapInfo info,uint usage);
    [DllImport("user32.dll")]static extern nint CreateIconIndirect(ref IconInfo info);
}
