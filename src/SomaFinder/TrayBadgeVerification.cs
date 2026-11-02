// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using SomaFinder.Tray;

namespace SomaFinder;
internal static class TrayBadgeVerification
{
    // Observe the existing host state before sending any appearance command. This catches
    // reconnect/TaskbarCreated regressions that a normal->paused test cycle would conceal.
    internal static void AssertPausedNative(FinderApplication app)
    {
        if(!CloudSandbox.Active)throw new InvalidOperationException();
        var tray=app.VerificationTray;
        if(!tray.Paused)throw new Exception("Frontend pause state was lost");
        nint image=SendMessageW(tray.HostWindow,0x7f,0,0);
        if(image==0)throw new Exception("Native icon is missing after state recovery");
        using var borrowed=System.Drawing.Icon.FromHandle(image);using var bitmap=borrowed.ToBitmap();
        var orange=bitmap.GetPixel(22,25);var bar=bitmap.GetPixel(28,25);
        if(!(orange.R>180&&orange.G>70&&orange.G<170&&orange.B<50&&bar.R>180&&bar.G>70&&bar.G<170&&bar.B<50))
            throw new Exception("Native paused badge was not preserved before any appearance update");
        if(!TrayProtocol.IsRegistered())throw new Exception("Shell icon is missing after state recovery");
    }
    internal static Task Verify(FinderApplication app)
    {
        if(!CloudSandbox.Active)throw new InvalidOperationException();
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var now=DateTimeOffset.UtcNow;var tray=app.VerificationTray;var host=tray.HostWindow;
        var cases=new[]{
            ("normal idle",false,false,false,(DateTimeOffset?)null,false,false,false),
            ("session",false,false,true,(DateTimeOffset?)null,false,false,true),
            ("timed",false,false,false,(DateTimeOffset?)now.AddHours(1),false,false,true),
            ("expired",false,false,false,(DateTimeOffset?)now.AddSeconds(-1),false,false,false),
            ("battery",false,false,false,(DateTimeOffset?)null,true,false,true),
            ("memory",false,false,false,(DateTimeOffset?)null,false,true,true),
            ("battery and memory",false,false,false,(DateTimeOffset?)null,true,true,true),
            ("still indexing on battery",true,false,false,(DateTimeOffset?)null,true,false,false),
            ("stopping",true,true,true,(DateTimeOffset?)null,false,false,false),
            ("draining search",false,true,true,(DateTimeOffset?)null,false,false,false)
        };
        tray.Paused=false;nint normal=SendMessageW(host,0x7f,0,0);Check(normal!=0,"No native icon");
        foreach(var c in cases)
        {
            var state=IndexTrayState.Evaluate(c.Item2,c.Item3,c.Item4,c.Item5,now,c.Item6,c.Item7,"索引状态");
            Check(state.Paused==c.Item8,"Wrong policy appearance: "+c.Item1);
            if(state.Paused)Check(state.Phase.Contains("暂停")||state.Phase.Contains("暂缓"),"Missing policy explanation");
            tray.Paused=state.Paused;
            nint image=SendMessageW(host,0x7f,0,0);
            Check(image!=0&&(image!=normal)==state.Paused,"Native icon did not change with policy: "+c.Item1);
            using var borrowed=System.Drawing.Icon.FromHandle(image);using var bitmap=borrowed.ToBitmap();
            var orange=bitmap.GetPixel(22,25);var bar=bitmap.GetPixel(28,25);
            if(state.Paused)Check(orange.R>180&&orange.G>70&&orange.G<170&&orange.B<50&&bar.R>180&&bar.G>70&&bar.G<170&&bar.B<50,"Badge does not contain two orange pause bars");
            Check(tray.HostWindow==host&&TrayProtocol.IsRegistered(),"Badge replaced tray identity");
        }
        for(int i=0;i<40;i++)tray.Paused=(i%2)==0;
        tray.Paused=false;Check(SendMessageW(host,0x7f,0,0)==normal,"Normal icon not restored");
        // Leave the pause state active for TaskbarCreated and crash/reconnect tests.
        tray.Paused=true;
        return Task.CompletedTask;
    }
    [DllImport("user32.dll")]static extern nint SendMessageW(nint window,uint message,nint wparam,nint lparam);
}
