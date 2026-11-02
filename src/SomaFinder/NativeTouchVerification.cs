// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;

namespace SomaFinder;
internal static class NativeTouchVerification
{
    public static int Run(string[] args)
    {
        var report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-native-touch-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),new Preferences{StartupSync=false,PeriodicSync=false},true);
        int failures=0,touchDown=0,touchMove=0,promotedMouse=0;var tests=new List<object>();
        app.Startup+=async(_,_)=>
        {
            SearchWindow? window=null;SummaryTextView? view=null;
            void Check(bool ok,string why){if(!ok)throw new Exception(why);}
            async Task Test(string name,Func<Task> action){try{await action();tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString(),diagnostic=view?.TouchDiagnostics});}}
            try
            {
                app.NewWindow();window=app.Windows.OfType<SearchWindow>().Single();window.Width=1000;window.Height=680;window.Left=0;window.Top=0;
                view=CloudVerification.Descendants<SummaryTextView>(window).Single();view.Text=string.Join('\n',Enumerable.Range(1,220).Select(i=>$"Line {i:D3}: needle 这是触屏滚动及选字验证。"));
                view.AddHandler(UIElement.PreviewTouchDownEvent,new EventHandler<TouchEventArgs>((_,_)=>touchDown++),true);
                view.AddHandler(UIElement.PreviewTouchMoveEvent,new EventHandler<TouchEventArgs>((_,_)=>touchMove++),true);
                view.AddHandler(UIElement.PreviewMouseDownEvent,new MouseButtonEventHandler((_,e)=>{if(e.StylusDevice!=null)promotedMouse++;}),true);
                await Task.Delay(300);SetForegroundWindow(new WindowInteropHelper(window).Handle);view.Focus();await Task.Delay(120);
                Check(InitializeTouchInjection(4,3),"InitializeTouchInjection failed: "+Marshal.GetLastWin32Error());
                Point Screen(double x,double y)=>view.PointToScreen(new Point(x,y));
                void Touch(uint id,Point point,uint flags){var contact=new Contact{Info=new Pointer{Type=2,Id=id,Flags=flags,Pixel=new Pt{X=(int)point.X,Y=(int)point.Y}},Mask=7,Area=new Rc{Left=(int)point.X-2,Top=(int)point.Y-2,Right=(int)point.X+2,Bottom=(int)point.Y+2},Orientation=90,Pressure=32000};if(!InjectTouchInput(1,new[]{contact}))throw new Exception("InjectTouchInput: "+Marshal.GetLastWin32Error());}
                await Test("native injected touch scrolls both directions without replacing selection",async()=>
                {
                    view.Selection.Select(view.Document.ContentStart.GetPositionAtOffset(3)!,view.Document.ContentStart.GetPositionAtOffset(15)!);string selected=view.Selection.Text;
                    double y=Math.Min(view.ActualHeight-20,190);var point=Screen(60,y);Touch(1,point,0x10006);await Task.Delay(80);
                    for(int i=1;i<=12;i++){Touch(1,new Point(point.X,point.Y-i*8),0x20006);await Task.Delay(18);Check(view.Selection.Text==selected,"Native move started text selection");}Touch(1,new Point(point.X,point.Y-96),0x40000);await Task.Delay(150);
                    // Windows coalesces updates; verify delivery and actual movement,
                    // rather than requiring one WPF callback for every injected frame.
                    Check(touchDown>0&&touchMove>0,"Native contacts did not reach WPF touch routing");Check(view.VerticalOffset>60,"Native upward swipe did not scroll");Check(view.Selection.Text==selected,"Swipe selected text");
                    double offset=view.VerticalOffset;point=Screen(60,40);Touch(2,point,0x10006);await Task.Delay(50);
                    for(int i=1;i<=8;i++){Touch(2,new Point(point.X,point.Y+i*6),0x20006);await Task.Delay(18);}Touch(2,new Point(point.X,point.Y+48),0x40000);await Task.Delay(100);
                    Check(view.VerticalOffset<offset-25&&view.Selection.Text==selected,"Downward swipe changed selection or failed to scroll");Check(view.ActiveTouches==0,"Native capture remained after finger up");
                });
                await Test("real mouse input still selects summary text after touch",async()=>
                {
                    view.ScrollToVerticalOffset(0);await Task.Delay(100);var a=Screen(22,22);var b=Screen(Math.Min(view.ActualWidth-22,180),22);
                    SetCursorPos((int)a.X,(int)a.Y);mouse_event(2,0,0,0,0);await Task.Delay(40);
                    for(int i=1;i<=8;i++){SetCursorPos((int)(a.X+(b.X-a.X)*i/8),(int)a.Y);await Task.Delay(15);}mouse_event(4,0,0,0,0);await Task.Delay(80);
                    Check(view.Selection.Text.Length>2,"Mouse drag no longer selects text");
                });
            }
            catch(Exception ex){failures++;tests.Add(new{name="native input fixture",passed=false,error=ex.ToString()});}
            finally{mouse_event(4,0,0,0,0);window?.Close();app.Backend.Dispose();Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,touchDown,touchMove,promotedMouse,scope="Win32 InjectTouchInput and native mouse events in the cloud desktop. Exercises OS to WPF input conversion; not physical touchscreen acceptance."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);}
        };
        return app.Run();
    }
    [StructLayout(LayoutKind.Sequential)]struct Pt{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct Rc{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct Pointer{public uint Type,Id,Frame,Flags;public nint Device,Target;public Pt Pixel,Himetric,RawPixel,RawHimetric;public uint Time,History;public int Data;public uint Keys;public ulong Performance;public uint Change;}
    [StructLayout(LayoutKind.Sequential)]struct Contact{public Pointer Info;public uint Flags,Mask;public Rc Area,RawArea;public uint Orientation,Pressure;}
    [DllImport("user32.dll",SetLastError=true)]static extern bool InitializeTouchInjection(uint count,uint mode);
    [DllImport("user32.dll",SetLastError=true)]static extern bool InjectTouchInput(uint count,Contact[] contacts);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
}
