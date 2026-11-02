// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SomaFinder;

public sealed partial class SearchWindow
{
    CancellationTokenSource? searchWait;
    string searchStage="正在搜索";
    Button cancelWait=null!,lexicalAlternative=null!,zoomReset=null!;
    double summaryZoom=1;
    internal double SummaryZoom=>summaryZoom;
    internal void ZoomSummary(double factor){summaryZoom=Math.Clamp(summaryZoom*factor,.75,2.5);snippet.FontSize=14*summaryZoom;if(zoomReset!=null)zoomReset.Content=$"{summaryZoom:P0}";}
    void ConfigureInputs()
    {
        foreach(var control in new DependencyObject[]{tree,results,summaryResults})control.SetValue(ScrollViewer.PanningModeProperty,PanningMode.VerticalOnly);
        snippet.Zoom=ZoomSummary;
        SourceInitialized+=(_,_)=>{if(PresentationSource.FromVisual(this) is HwndSource h)h.AddHook(InputHook);};
    }
    IntPtr InputHook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
    {
        if(msg==0x020E)
        {
            var raw=lp.ToInt64();var point=PointFromScreen(new Point((short)(raw&0xffff),(short)((raw>>16)&0xffff)));
            if(InputHitTest(point) is DependencyObject target)handled=HorizontalWheel(target,(short)((wp.ToInt64()>>16)&0xffff));
        }
        return IntPtr.Zero;
    }
    internal static bool HorizontalWheel(DependencyObject target,int delta)
    {
        for(DependencyObject? item=target;item!=null;item=item is Visual?VisualTreeHelper.GetParent(item):LogicalTreeHelper.GetParent(item))
            if(item is ScrollViewer scroll&&scroll.ScrollableWidth>0)
            {double next=Math.Clamp(scroll.HorizontalOffset+delta/120d*48,0,scroll.ScrollableWidth);if(Math.Abs(next-scroll.HorizontalOffset)>.001){scroll.ScrollToHorizontalOffset(next);return true;}}
        return false;
    }
    internal FrameworkElement SortIcon()
    {
        var grid=new Grid{Width=24,Height=22};
        System.Windows.Shapes.Path Arrow(bool down,double x)=>new(){Data=Geometry.Parse(down?$"M {x},2 L {x},20 M {x-5},15 L {x},20 L {x+5},15":$"M {x},20 L {x},2 M {x-5},7 L {x},2 L {x+5},7"),Stroke=(!sortOriginal&&descending==down)?new SolidColorBrush(Color.FromRgb(0,103,192)):Brushes.Gray,StrokeThickness=1.6};
        grid.Children.Add(Arrow(false,6));grid.Children.Add(Arrow(true,18));return grid;
    }
    internal void SortColumn(DataGridColumn column)
    {bool same=!sortOriginal&&sortField==column.SortMemberPath;descending=same?!descending:false;sortOriginal=false;sortField=column.SortMemberPath;ApplyFilter();}
    internal IReadOnlyList<double> RegionWidths=>new[]{0,2,4,6}.Select(i=>panels.ColumnDefinitions[i].Width.Value).ToArray();
}
