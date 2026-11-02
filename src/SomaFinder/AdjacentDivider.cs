// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SomaFinder;

// Freeze actual widths at drag start. Only the adjacent pair exchanges width.
internal sealed class AdjacentDivider : Thumb
{
    readonly Grid grid;readonly int left,right;readonly Action completed;
    public AdjacentDivider(Grid grid,int left,int right,Action completed)
    {
        this.grid=grid;this.left=left;this.right=right;this.completed=completed;
        Width=8;HorizontalAlignment=HorizontalAlignment.Center;Cursor=Cursors.SizeWE;Focusable=false;
        var root=new FrameworkElementFactory(typeof(Grid));root.SetValue(Panel.BackgroundProperty,Brushes.Transparent);
        var rule=new FrameworkElementFactory(typeof(Border));rule.SetValue(FrameworkElement.WidthProperty,1d);rule.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);rule.SetResourceReference(Border.BackgroundProperty,"ControlStrokeColorDefaultBrush");root.AppendChild(rule);Template=new ControlTemplate(typeof(Thumb)){VisualTree=root};
        DragStarted+=(_,_)=>Freeze();DragDelta+=(_,e)=>Move(e.HorizontalChange);DragCompleted+=(_,_)=>completed();
        System.Windows.Automation.AutomationProperties.SetName(this,"调整相邻区域宽度");
    }
    void Freeze(){foreach(var c in grid.ColumnDefinitions)c.Width=new GridLength(c.ActualWidth);}
    internal void Move(double delta)
    {
        var a=grid.ColumnDefinitions[left];var b=grid.ColumnDefinitions[right];
        double x=a.Width.IsAbsolute?a.Width.Value:a.ActualWidth,y=b.Width.IsAbsolute?b.Width.Value:b.ActualWidth;
        delta=Math.Clamp(delta,a.MinWidth-x,y-b.MinWidth);a.Width=new GridLength(x+delta);b.Width=new GridLength(y-delta);
    }
}
