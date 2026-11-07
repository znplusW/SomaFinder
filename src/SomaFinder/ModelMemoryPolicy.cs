// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 znplusW. Separate commercial licensing is available.
namespace SomaFinder;

internal sealed class ModelMemoryPolicy
{
    internal const ulong LowMemoryBytes=1UL*1024*1024*1024;
    internal const long LowMemoryMilliseconds=2*60*1000,IdleMilliseconds=15*60*1000;
    long? lowSince,idleSince,lastSample;
    bool enabled;
    long retryAfter;
    internal object Snapshot=>new{lowSince,idleSince,lastSample,enabled,retryAfter};

    internal void Reset(){lowSince=null;idleSince=null;lastSample=null;enabled=false;retryAfter=0;}
    internal void Activity(){idleSince=null;}
    internal void Attempted(long now){retryAfter=now+60*1000;}
    internal bool Observe(bool option,ulong? available,bool busy,long now)
    {
        // Missing samples (sleep, a suspended process or a slow system) are not proof
        // that memory remained low and the backend remained idle during that interval.
        if(!option){Reset();return false;}
        if(!enabled||lastSample is not long previous||now<previous||now-previous>45*1000)
        {lowSince=null;idleSince=null;retryAfter=0;}
        enabled=true;lastSample=now;
        if(available is null||available>=LowMemoryBytes)lowSince=null;else lowSince??=now;
        if(busy)idleSince=null;else idleSince??=now;
        return lowSince is long low&&idleSince is long idle&&now-low>=LowMemoryMilliseconds&&now-idle>=IdleMilliseconds&&now>=retryAfter;
    }
}

// Only continuous, observed time after indexing has actually stopped counts.
// Searches may delay reclaim without restarting the pause timer.
internal sealed class PausedIndexModelPolicy
{
    internal const long DelayMilliseconds=60*1000;
    long? pausedSince,lastSample;
    long retryAfter;
    internal object Snapshot=>new{pausedSince,lastSample,retryAfter};
    internal void Reset(){pausedSince=null;lastSample=null;retryAfter=0;}
    internal bool Observe(bool enabled,bool confirmedPaused,long now)
    {
        if(!enabled||!confirmedPaused){Reset();return false;}
        if(lastSample is not long previous||now<previous||now-previous>45*1000){pausedSince=now;retryAfter=0;}
        lastSample=now;return Ready(now)&&now>=retryAfter;
    }
    internal bool Ready(long now)=>pausedSince is long start&&lastSample is long sample&&now>=sample&&now-sample<=45*1000&&now-start>=DelayMilliseconds;
    internal void Attempted(long now){retryAfter=now+60*1000;}
}
