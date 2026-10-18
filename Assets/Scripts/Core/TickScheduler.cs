using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>How often a scheduled callback runs.</summary>
public enum TickRate
{
    /// <summary>Every rendered frame (input, camera, animation blending). Keep these tiny.</summary>
    Frame,
    /// <summary>Four times a second: AI decisions, proximity checks, HUD values, interaction prompts.</summary>
    Quarter,
    /// <summary>Once a second: economy ticks, growth, weather, autosave checks, network snapshots.</summary>
    Second,
    /// <summary>Once per in-game day (WorldClock.DayStarted): bills, markets, soil, contracts, NPC schedules.</summary>
    Daily
}

/// <summary>A scheduled callback. Dispose() (or Cancel()) stops it; it also stops by itself when its owner is destroyed.</summary>
public sealed class TickHandle : IDisposable
{
    internal readonly TickRate Rate;
    internal readonly Action<float> Callback;        // dt in game seconds (Daily: the day number as float)
    internal readonly UnityEngine.Object Owner;      // optional: auto-cancel when destroyed
    internal readonly string Name;
    internal float Accumulator;
    internal int Failures;
    public int Calls { get; internal set; }
    public bool Active { get; private set; } = true;

    internal TickHandle(TickRate rate, Action<float> cb, UnityEngine.Object owner, string name, float phase)
    {
        Rate = rate; Callback = cb; Owner = owner; Name = name; Accumulator = phase;
    }

    /// <summary>True when the owner (if any) still exists.</summary>
    internal bool OwnerAlive => ReferenceEquals(Owner, null) || Owner;

    public void Cancel() => Active = false;
    public void Dispose() => Cancel();
}

/// <summary>
/// Central tick scheduler: one Update loop that fans out to every system at the rate it asked for, instead of 70+
/// MonoBehaviours each polling in their own Update. Pure C# (no Unity lifecycle) so it runs in headless tests;
/// TickDriver feeds it from the engine. Rules:
/// - Callbacks never see each other's exceptions: a throwing callback is logged and, after 3 failures in a row,
///   dropped (the game keeps running; the bug is in the log).
/// - Subscribers to the same rate are phase-staggered, so 40 "once a second" systems do not all fire on the same frame.
/// - Daily ticks come from WorldClock.DayStarted (hooked by TickDriver) or Scheduler.RaiseDaily(day) in tests.
/// - Time is game time (Time.deltaTime), so pausing (timeScale 0) pauses the Quarter/Second ticks too.
/// </summary>
public sealed class TickScheduler
{
    public const float QuarterInterval = 0.25f, SecondInterval = 1f;
    public const int MaxFailures = 3;

    /// <summary>The scheduler the running game uses (TickDriver advances it). Tests may create their own.</summary>
    public static TickScheduler Main { get; private set; } = new TickScheduler();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Main = new TickScheduler(); }

    private readonly List<TickHandle> frame = new List<TickHandle>(), quarter = new List<TickHandle>(), second = new List<TickHandle>(), daily = new List<TickHandle>();
    private readonly List<TickHandle> pending = new List<TickHandle>();   // added while ticking
    private bool ticking;
    private int phaseCounter;

    /// <summary>Total callbacks invoked (profiling / tests).</summary>
    public long TotalCalls { get; private set; }
    /// <summary>Callbacks dropped after repeated exceptions.</summary>
    public int Dropped { get; private set; }
    /// <summary>Last frame's wall time spent in callbacks, in ms (budget enforcement; TickDriver fills it).</summary>
    public float LastFrameMs { get; internal set; }

    public int Count(TickRate r) => ListFor(r).Count;
    public int Count() => frame.Count + quarter.Count + second.Count + daily.Count;

    private List<TickHandle> ListFor(TickRate r)
    {
        switch (r)
        {
            case TickRate.Frame: return frame;
            case TickRate.Quarter: return quarter;
            case TickRate.Second: return second;
            default: return daily;
        }
    }

    /// <summary>
    /// Run <paramref name="callback"/> at <paramref name="rate"/>. The float argument is the game seconds since the
    /// previous call (Frame/Quarter/Second) or the new day number (Daily). Pass the owning component so the callback
    /// stops when it is destroyed; name is for the log and the profiler.
    /// </summary>
    public TickHandle Every(TickRate rate, Action<float> callback, UnityEngine.Object owner = null, string name = null)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        float interval = rate == TickRate.Quarter ? QuarterInterval : rate == TickRate.Second ? SecondInterval : 0f;
        // stagger: the n-th subscriber of a rate starts part-way through its interval (8 phases)
        float phase = interval > 0f ? interval * ((phaseCounter++ % 8) / 8f) : 0f;
        var h = new TickHandle(rate, callback, owner, name ?? (callback.Method != null ? callback.Method.Name : "tick"), phase);
        if (ticking) pending.Add(h); else ListFor(rate).Add(h);
        return h;
    }

    public TickHandle EveryFrame(Action<float> cb, UnityEngine.Object owner = null, string name = null) => Every(TickRate.Frame, cb, owner, name);
    public TickHandle EveryQuarter(Action<float> cb, UnityEngine.Object owner = null, string name = null) => Every(TickRate.Quarter, cb, owner, name);
    public TickHandle EverySecond(Action<float> cb, UnityEngine.Object owner = null, string name = null) => Every(TickRate.Second, cb, owner, name);
    public TickHandle EveryDay(Action<int> cb, UnityEngine.Object owner = null, string name = null) => Every(TickRate.Daily, d => cb((int)d), owner, name);

    /// <summary>Advance game time by dt seconds (TickDriver calls this once per frame with Time.deltaTime).</summary>
    public void Advance(float dt)
    {
        if (dt < 0f) dt = 0f;
        ticking = true;
        try
        {
            Pump(frame, dt, 0f);
            Pump(quarter, dt, QuarterInterval);
            Pump(second, dt, SecondInterval);
            for (int i = 0; i < daily.Count; i++) if (!daily[i].Active || !daily[i].OwnerAlive) { daily[i].Cancel(); daily.RemoveAt(i--); }   // cancelled daily handles go now, not on the next day
        }
        finally { ticking = false; Flush(); }
    }

    /// <summary>A new in-game day began: run every Daily subscriber once with the day number.</summary>
    public void RaiseDaily(int day)
    {
        ticking = true;
        try
        {
            for (int i = 0; i < daily.Count; i++)
            {
                var h = daily[i];
                if (!h.Active || !h.OwnerAlive) { h.Cancel(); daily.RemoveAt(i--); continue; }
                Invoke(h, day);
                if (!h.Active) { daily.RemoveAt(i--); }
            }
        }
        finally { ticking = false; Flush(); }
    }

    private void Pump(List<TickHandle> list, float dt, float interval)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var h = list[i];
            if (!h.Active || !h.OwnerAlive) { h.Cancel(); list.RemoveAt(i--); continue; }
            if (interval <= 0f) { Invoke(h, dt); }
            else
            {
                h.Accumulator += dt;
                if (h.Accumulator >= interval)
                {
                    float elapsed = h.Accumulator;
                    // a long hitch (loading, alt-tab) runs the callback once with the whole elapsed time, not 50 times
                    h.Accumulator = Math.Min(h.Accumulator - interval, interval * 0.99f);
                    Invoke(h, elapsed);
                }
            }
            if (!h.Active) list.RemoveAt(i--);
        }
    }

    private void Invoke(TickHandle h, float arg)
    {
        try
        {
            h.Callback(arg);
            h.Calls++; TotalCalls++;
            h.Failures = 0;
        }
        catch (Exception e)
        {
            h.Failures++;
            Debug.LogError("[Tick] " + h.Name + " (" + h.Rate + ") threw: " + e.GetType().Name + ": " + e.Message + (h.Failures >= MaxFailures ? " - dropped after " + h.Failures + " failures" : ""));
            if (h.Failures >= MaxFailures) { h.Cancel(); Dropped++; }
        }
    }

    private void Flush()
    {
        if (pending.Count == 0) return;
        foreach (var h in pending) ListFor(h.Rate).Add(h);
        pending.Clear();
    }

    /// <summary>Remove every subscription (scene change, tests).</summary>
    public void Clear() { frame.Clear(); quarter.Clear(); second.Clear(); daily.Clear(); pending.Clear(); }

    /// <summary>One line per rate for the debug overlay.</summary>
    public string Report() => "frame " + frame.Count + ", 1/4s " + quarter.Count + ", 1s " + second.Count + ", daily " + daily.Count + ", calls " + TotalCalls + ", dropped " + Dropped + ", last " + LastFrameMs.ToString("F2") + " ms";
}

/// <summary>Static shortcut to the game's scheduler: <c>Ticks.EverySecond(dt => ..., this)</c>.</summary>
public static class Ticks
{
    public static TickScheduler Scheduler => TickScheduler.Main;
    public static TickHandle Every(TickRate rate, Action<float> cb, UnityEngine.Object owner = null, string name = null) => TickScheduler.Main.Every(rate, cb, owner, name);
    public static TickHandle EveryFrame(Action<float> cb, UnityEngine.Object owner = null, string name = null) => TickScheduler.Main.EveryFrame(cb, owner, name);
    public static TickHandle EveryQuarter(Action<float> cb, UnityEngine.Object owner = null, string name = null) => TickScheduler.Main.EveryQuarter(cb, owner, name);
    public static TickHandle EverySecond(Action<float> cb, UnityEngine.Object owner = null, string name = null) => TickScheduler.Main.EverySecond(cb, owner, name);
    public static TickHandle EveryDay(Action<int> cb, UnityEngine.Object owner = null, string name = null) => TickScheduler.Main.EveryDay(cb, owner, name);
}
