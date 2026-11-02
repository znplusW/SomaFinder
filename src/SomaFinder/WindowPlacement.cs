// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows;

namespace SomaFinder;
internal static class WindowPlacement
{
    // All coordinates in this policy are WPF device-independent units.
    public static Rect Recover(Rect requested,IReadOnlyList<Rect> workAreas)
    {
        if(workAreas.Count==0)throw new ArgumentException("No display work area");
        if(workAreas.Any(area=>{var visible=Rect.Intersect(requested,area);return !visible.IsEmpty&&visible.Width>=100&&visible.Height>=60;}))return requested;
        var target=workAreas[0];var width=Math.Min(Math.Max(1,requested.Width),target.Width);var height=Math.Min(Math.Max(1,requested.Height),target.Height);
        return new(target.Left+(target.Width-width)/2,target.Top+(target.Height-height)/2,width,height);
    }
}
