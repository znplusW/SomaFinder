// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

internal readonly record struct IndexTrayState(bool Paused,string Phase)
{
    internal static IndexTrayState Evaluate(bool running,bool transitioning,bool session,DateTimeOffset? until,DateTimeOffset now,bool batteryBlocked,bool memoryBlocked,string phase)
    {
        if(running||transitioning)return new(false,phase);
        if(session)return new(true,"索引已暂停至下次启动");
        if(until>now)return new(true,"索引已暂停，剩余 "+Rules.Remaining(until.Value-now));
        if(batteryBlocked)return new(true,"电池供电或状态未知，自动同步已暂缓");
        if(memoryBlocked)return new(true,"内存不足，自动同步已暂缓");
        return new(false,phase);
    }
}
