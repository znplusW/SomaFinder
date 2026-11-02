// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SomaFinder;

public static class ThumbnailCache
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Image,Subscription> subscriptions=new();
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(ThumbnailCache),new PropertyMetadata(false,Changed));
    public static void SetEnabled(DependencyObject value,bool enabled)=>value.SetValue(EnabledProperty,enabled);
    public static bool GetEnabled(DependencyObject value)=>(bool)value.GetValue(EnabledProperty);
    static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs e)
    {
        if(value is not Image image||e.NewValue is not true)return;
        subscriptions.GetValue(image,i=>new Subscription(i));
    }
    sealed class Subscription
    {
        readonly Image image;Hit? hit;CancellationTokenSource? pending;string stamp="";
        internal Subscription(Image image)
        {
            this.image=image;image.Loaded+=(_,_)=>Bind();image.Unloaded+=(_,_)=>Release();image.DataContextChanged+=(_,_)=>Bind();image.IsVisibleChanged+=(_,_)=>{if(image.IsVisible)Bind();else Release();};
        }
        void Bind(){Release();if(!image.IsLoaded||!image.IsVisible)return;hit=image.DataContext as Hit;if(hit!=null){hit.PropertyChanged+=Changed;Load();}}
        void Changed(object? sender,PropertyChangedEventArgs e){if(hit!=null&&stamp!=ResultThumbnails.Stamp(hit,hit.ThumbnailQuery))Load();}
        void Release(){pending?.Cancel();if(hit!=null)hit.PropertyChanged-=Changed;hit=null;stamp="";image.Source=null;}
        async void Load()
        {
            if(hit==null||Application.Current is not FinderApplication app)return;var current=hit;string next=ResultThumbnails.Stamp(current,current.ThumbnailQuery);stamp=next;pending?.Cancel();var cancel=pending=new();
            try{var result=await ResultThumbnails.Get(current,current.ThumbnailQuery,app.Backend.Prefs,cancel.Token);if(cancel.IsCancellationRequested||!ReferenceEquals(current,hit)||stamp!=next)return;image.Source=result?.Image;image.ToolTip=result?.Caption;}
            catch(OperationCanceledException){}
        }
    }
    internal static BitmapSource? ReadCached(string path)
    {
        object? shell=null;IntPtr bitmap=IntPtr.Zero;
        try
        {
            var iid=typeof(IShellItemImageFactory).GUID;
            if(SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out shell)!=0)return null;
            // THUMBNAILONLY | INCACHEONLY: never generate new thumbnails or extract video frames.
            if(((IShellItemImageFactory)shell).GetImage(new PixelSize{X=128,Y=112},0x18,out bitmap)!=0||bitmap==IntPtr.Zero)return null;
            var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();return image;
        }
        catch(COMException){return null;}
        finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(shell!=null&&Marshal.IsComObject(shell))Marshal.ReleaseComObject(shell);}
    }
    [StructLayout(LayoutKind.Sequential)] struct PixelSize{public int X,Y;}
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IShellItemImageFactory{[PreserveSig]int GetImage(PixelSize size,uint flags,out IntPtr bitmap);}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object? shell);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
}
