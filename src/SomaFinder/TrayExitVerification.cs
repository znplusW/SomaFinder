// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SomaFinder.Tray;

namespace SomaFinder;
internal static class TrayExitVerification
{
    public static int Run(string[] args)
    {
        var report=Path.GetFullPath(args.Single(a=>a.StartsWith("--report="))[9..]);
        var root=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!,"SomaFinder-cloud-tray-exit-"+Guid.NewGuid().ToString("N"));CloudSandbox.Initialize(root);
        var app=new FinderApplication(new Store(root+"/data",root+"/config",root+"/state"),new Preferences{StartupSync=false,PeriodicSync=false,DefaultDetailLayout=DetailLayout.Both},true);
        var tests=new List<object>();var observations=new List<object>();int failures=0;Process? index=null;
        app.Startup+=async(_,_)=>
        {
            SearchWindow? search=null;var field=typeof(Backend).GetField("indexProcess",BindingFlags.Instance|BindingFlags.NonPublic)!;
            void Check(bool ok,string why){if(!ok)throw new Exception(why);}
            async Task Test(string name,Func<Task> action){int errors=CloudSandbox.Errors.Count;try{await action();await Task.Delay(40);Check(CloudSandbox.Errors.Count==errors,string.Join('\n',CloudSandbox.Errors.Skip(errors)));tests.Add(new{name,passed=true});}catch(Exception ex){failures++;tests.Add(new{name,passed=false,error=ex.ToString()});}}
            async Task Wait(Func<bool> ready){for(int i=0;i<100&&!ready();i++)await Task.Delay(20);Check(ready(),"Timed out waiting for dialog/menu state");}
            async Task ConfirmMenu(ContextMenu menu,DialogLocation location)
            {
                bool seen=false;string? error=null;var clock=new Stopwatch();clock.Start();
                var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
                timer.Tick+=(_,_)=>
                {
                    var dialog=app.Windows.Cast<Window>().FirstOrDefault(w=>w.Title=="退出 Soma Finder");
                    if(dialog==null){if(clock.ElapsedMilliseconds>2500){timer.Stop();error="Confirmation did not open";}return;}
                    if(!dialog.IsLoaded||dialog.Opacity==0){if(clock.ElapsedMilliseconds>2500){timer.Stop();error="Dialog did not finish initial placement";dialog.Close();}return;}
                    timer.Stop();seen=true;
                    try
                    {
                        var area=DialogPlacement.Resolve(location).Area;var bounds=DialogPlacement.Bounds(dialog);var expected=DialogPlacement.Center(area,bounds.Size);
                        Check(area.Contains(bounds)&&Math.Abs(bounds.X-expected.X)<2&&Math.Abs(bounds.Y-expected.Y)<2,"Dialog was centered on another window/monitor: "+bounds+" expected "+expected);
                        Check(dialog.Owner==null,"Tray exit inherited a search window on another monitor");
                        observations.Add(new{area=area.ToString(),bounds=bounds.ToString(),click=location.Click.ToString(),simulated=location.CloudArea!=null});
                    }
                    catch(Exception ex){error=ex.ToString();}
                    finally{dialog.Close();}
                };
                timer.Start();
                var exit=menu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"退出 Soma Finder"));exit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Wait(()=>seen||error!=null);timer.Stop();Check(error==null,error??"");Check(seen&&!app.Backend.Exiting,"Exit cancellation or dialog flow failed");
            }
            try
            {
                app.NewWindow();search=app.Windows.OfType<SearchWindow>().Single();search.Left=30;search.Top=30;
                index=Process.Start(new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"-NoProfile","-NonInteractive","-Command","Start-Sleep -Seconds 120"}})!;
                field.SetValue(app.Backend,index);await app.VerificationTray.ShowAsync();
                await Test("real tray callback keeps the clicked screen origin through menu exit and modal cancellation",async()=>
                {
                    var point=System.Windows.Forms.Cursor.Position;
                    long packed=(ushort)(short)point.X|((long)(ushort)(short)point.Y<<16);
                    TrayProtocol.PostMessageW(app.VerificationTray.HostWindow,TrayProtocol.ShellCallback,(nint)packed,0x7b);
                    await Wait(()=>app.VerificationTrayMenu?.IsOpen==true&&app.VerificationTrayLocation!=null);
                    var location=app.VerificationTrayLocation!;Check(location.Click==point,"Tray callback discarded original coordinates");
                    SetCursorPos(10,10);await ConfirmMenu(app.VerificationTrayMenu!,location);
                    if(app.VerificationTrayMenu!=null)app.VerificationTrayMenu.IsOpen=false;
                });
                await Test("actual modal positions honor simulated negative and secondary work areas at mixed DPI",async()=>
                {
                    foreach(var item in new[]{(new Rect(-1920,0,1920,1040),96u),(new Rect(1920,-800,2880,1720),144u),(new Rect(-3840,-2160,3840,2120),192u)})
                    {
                        var location=DialogLocation.ForCloud(item.Item1,item.Item2);
                        SetCursorPos(10,10);await ConfirmMenu(app.CreateTrayMenu(location),location);
                    }
                });
                await Test("dialog centering clips oversized geometry inside taskbar-excluded work areas",()=>
                {
                    foreach(var area in new[]{new Rect(0,0,1920,1040),new Rect(-1920,-1080,1920,1040),new Rect(3000,0,380,260)})foreach(var size in new[]{new Size(480,260),new Size(1900,1200)})
                    {var rect=DialogPlacement.Center(area,size);Check(area.Contains(rect),"Dialog leaves usable work area");Check(Math.Abs(rect.X+rect.Width/2-area.X-area.Width/2)<.01,"Incorrect horizontal center");}
                    return Task.CompletedTask;
                });
                field.SetValue(app.Backend,null);index.Kill();await index.WaitForExitAsync();index.Dispose();index=null;
                await Test("shrinking hides the current rightmost pane first after either panel order",async()=>
                {
                    search.EnableCloudLayoutFixture();var details=search.VerificationDetails;
                    async Task Width(double width){search.MinWidth=width;search.Width=width;await Task.Delay(80);search.UpdateLayout();}
                    await Width(1800);Check(details.Mode==DetailMode.SideBySide,"Wide fixture did not open both panes");details.SelectTab(details.RightKind);
                    await Width(900);Check(details.Mode==DetailMode.Tabs&&details.ActiveTab==details.LeftKind,"Narrowing retained rightmost pane ahead of left pane");
                    await Width(1800);details.Swap();details.SelectTab(details.RightKind);var left=details.LeftKind;
                    await Width(900);Check(details.ActiveTab==left&&details.ContentVisible(left)&&!details.ContentVisible(details.RightKind),"Swapped order did not determine collapse priority");
                    await Width(600);Check(details.Mode==DetailMode.Hidden,"Very narrow view should preserve tree/results first");await Width(1800);
                    Check(details.LeftKind==left&&details.RightExpanded&&details.Mode==DetailMode.SideBySide,"Expansion overwrote preferred order or open state");
                });
                Check(CloudSandbox.Errors.Count==0,string.Join('\n',CloudSandbox.Errors));
            }
            catch(Exception ex){failures++;tests.Add(new{name="fixture",passed=false,error=ex.ToString()});}
            finally
            {
                field.SetValue(app.Backend,null);if(index!=null){if(!index.HasExited)index.Kill();index.Dispose();}
                search?.Close();app.Backend.Dispose();Store.Atomic(report,JsonSerializer.Serialize(new{commit=Environment.GetEnvironmentVariable("GITHUB_SHA"),failures,tests,observations,scope="Only tray-origin exit modal placement and positional collapse priority. Native Shell callback and actual WPF modal windows; secondary/mixed-DPI monitors are simulated work areas, not physical monitor hotplug. No Soma backend/index."},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(failures==0?0:1);
            }
        };
        return app.Run();
    }
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
}
