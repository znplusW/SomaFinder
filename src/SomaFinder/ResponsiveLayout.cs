// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;

namespace SomaFinder;

internal enum DetailMode { SideBySide, Tabs, Hidden }
internal sealed record PaneWidths(DetailMode Mode,double Tree,double Results,double Left,double Middle,double Right);
internal static class ResponsiveLayout
{
    internal const double TreeBar=40,Divider=44,DetailBar=44;
    internal static PaneWidths Calculate(double width,bool treeVisible,double tree,double left,double right,double minLeft,double minRight,bool detailsCollapsed=false)
    {
        width=Math.Max(1,width);double bar=TreeBar,treeMin=treeVisible?Math.Min(140,Math.Max(0,width-bar-Divider-120)):0;
        tree=treeVisible?Math.Max(treeMin,tree):0;
        // Preserve readable result width before allocating optional detail surfaces.
        if(!detailsCollapsed&&width>=treeMin+bar+320+Divider+minLeft+DetailBar+minRight)
        {
            double budget=width-bar-320-Divider-DetailBar;
            double desired=tree+left+right,min=treeMin+minLeft+minRight;
            double ratio=desired>min?Math.Clamp((budget-min)/(desired-min),0,1):1;
            tree=treeMin+(tree-treeMin)*ratio;left=minLeft+(left-minLeft)*ratio;right=minRight+(right-minRight)*ratio;
            return new(DetailMode.SideBySide,tree,width-tree-bar-Divider-left-DetailBar-right,left,DetailBar,right);
        }
        if(!detailsCollapsed&&width>=treeMin+bar+300+Divider+240)
        {
            double budget=width-bar-300-Divider,preferred=Math.Max(240,Math.Min(left,380));
            double ratio=Math.Clamp((budget-treeMin-240)/Math.Max(1,tree-treeMin+preferred-240),0,1);
            tree=treeMin+(tree-treeMin)*ratio;left=240+(preferred-240)*ratio;
            return new(DetailMode.Tabs,tree,width-tree-bar-Divider-left,left,0,0);
        }
        tree=treeVisible?Math.Clamp(tree,treeMin,Math.Max(treeMin,width-bar-Divider-240)):0;
        return new(DetailMode.Hidden,tree,Math.Max(1,width-tree-bar-Divider),0,0,0);
    }
}
