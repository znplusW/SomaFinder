// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
using System.Windows.Controls;

namespace SomaFinder;
internal sealed class PreviewHost : ContentControl
{
    internal event Action? ContentUpdated;
    protected override void OnContentChanged(object oldContent,object newContent){base.OnContentChanged(oldContent,newContent);ContentUpdated?.Invoke();}
}
