// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SomaFinder;

// A notification-area popup needs an active owner even when every search window is closed.
// This transparent tool window exists only to own input while the menu is showing.
internal sealed class TrayMenuHost : Window
{
    readonly Border target=new(){Background=Brushes.Transparent,Focusable=true};
    ContextMenu? menu;
    bool closing;
    internal TrayMenuHost()
    {
        Width=Height=1;Left=SystemParameters.VirtualScreenLeft;Top=SystemParameters.VirtualScreenTop;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        AllowsTransparency=true;Background=Brushes.Transparent;Opacity=0;ShowActivated=true;
        Content=target;
        Deactivated+=(_,_)=>Dispatcher.BeginInvoke(new Action(CheckDeactivated));
        Closing+=(_,_)=>{closing=true;if(menu!=null)menu.IsOpen=false;};
    }
    internal void Open(ContextMenu value)
    {
        if(menu!=null)menu.IsOpen=false;
        menu=value;value.StaysOpen=false;value.PlacementTarget=target;value.Placement=PlacementMode.MousePoint;
        value.Closed+=MenuClosed;value.PreviewKeyDown+=MenuKeyDown;
        Show();Activate();target.Focus();
        value.IsOpen=true;value.Focus();
    }
    void CheckDeactivated()
    {
        if(closing||menu?.IsOpen!=true)return;
        var foreground=GetForegroundWindow();
        // A popup may itself acquire foreground on some Windows/WPF versions.
        var popup=(PresentationSource.FromVisual(menu) as HwndSource)?.Handle??0;
        if(foreground!=new WindowInteropHelper(this).Handle&&foreground!=popup)menu.IsOpen=false;
    }
    void MenuKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape&&menu!=null){menu.IsOpen=false;e.Handled=true;}
    }
    void MenuClosed(object sender,RoutedEventArgs e)
    {
        if(sender is not ContextMenu closed)return;
        closed.Closed-=MenuClosed;closed.PreviewKeyDown-=MenuKeyDown;closed.PlacementTarget=null;
        if(!ReferenceEquals(menu,closed))return;
        menu=null;if(closing)return;Hide();
        // Complete the Shell menu interaction so a subsequent right click can reopen it.
        PostMessageW(new WindowInteropHelper(this).Handle,0,0,0);
    }
    [DllImport("user32.dll")]static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]static extern bool PostMessageW(nint window,uint message,nint wp,nint lp);
}
