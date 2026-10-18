using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A typed event with safe subscribers: Subscribe returns an IDisposable (no forgotten -= in OnDisable), every
/// handler runs even when another throws (the exception is logged, not propagated into the raiser), handlers tied to
/// a Unity object stop when it is destroyed, and the list can change while raising. Use for new systems instead of
/// adding more fields to GameEvents; the payload type documents the event (see <see cref="Signals"/>).
/// </summary>
public sealed class Signal<T>
{
    private sealed class Sub
    {
        public Action<T> Handler; public UnityEngine.Object Owner; public bool Once; public bool Dead;
        public bool OwnerAlive => ReferenceEquals(Owner, null) || Owner;
    }

    private sealed class Token : IDisposable
    {
        private Sub sub;
        public Token(Sub s) { sub = s; }
        public void Dispose() { if (sub != null) { sub.Dead = true; sub = null; } }
    }

    private readonly List<Sub> subs = new List<Sub>();
    private int raising;

    public int Count { get { int n = 0; foreach (var s in subs) if (!s.Dead && s.OwnerAlive) n++; return n; } }
    /// <summary>Times Raise was called (tests, debug overlay).</summary>
    public int Raised { get; private set; }
    /// <summary>The payload of the last Raise (lets a late subscriber read the current state).</summary>
    public T Last { get; private set; }
    public bool HasValue { get; private set; }

    /// <summary>Call handler on every Raise until the token is disposed or owner (optional) is destroyed.</summary>
    public IDisposable Subscribe(Action<T> handler, UnityEngine.Object owner = null)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var s = new Sub { Handler = handler, Owner = owner };
        subs.Add(s);
        return new Token(s);
    }

    /// <summary>Call handler on the next Raise only.</summary>
    public IDisposable Once(Action<T> handler, UnityEngine.Object owner = null)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var s = new Sub { Handler = handler, Owner = owner, Once = true };
        subs.Add(s);
        return new Token(s);
    }

    /// <summary>Like Subscribe, but if the signal was raised before, handler also runs now with the last value.</summary>
    public IDisposable SubscribeReplay(Action<T> handler, UnityEngine.Object owner = null)
    {
        var t = Subscribe(handler, owner);
        if (HasValue) Guard(handler, Last);
        return t;
    }

    public void Raise(T value)
    {
        Raised++; Last = value; HasValue = true;
        raising++;
        try
        {
            int n = subs.Count;   // handlers added during this raise run next time
            for (int i = 0; i < n; i++)
            {
                var s = subs[i];
                if (s.Dead || !s.OwnerAlive) { s.Dead = true; continue; }
                if (s.Once) s.Dead = true;
                Guard(s.Handler, value);
            }
        }
        finally
        {
            raising--;
            if (raising == 0) subs.RemoveAll(x => x.Dead);
        }
    }

    private static void Guard(Action<T> h, T v)
    {
        try { h(v); }
        catch (Exception e) { Debug.LogError("[Signal<" + typeof(T).Name + ">] handler " + Describe(h) + " threw: " + e.GetType().Name + ": " + e.Message); }
    }

    private static string Describe(Delegate d) => d.Method == null ? "?" : (d.Method.DeclaringType != null ? d.Method.DeclaringType.Name + "." : "") + d.Method.Name;

    public void Clear() { subs.Clear(); HasValue = false; Last = default(T); }
}

/// <summary>Collects subscriptions so a component can drop them all in one call (OnDisable / OnDestroy).</summary>
public sealed class Subscriptions : IDisposable
{
    private readonly List<IDisposable> list = new List<IDisposable>();
    public int Count => list.Count;
    public Subscriptions Add(IDisposable d) { if (d != null) list.Add(d); return this; }
    public static Subscriptions operator +(Subscriptions s, IDisposable d) => s.Add(d);
    public void Dispose() { foreach (var d in list) { try { d.Dispose(); } catch (Exception) { } } list.Clear(); }
}

/// <summary>
/// Game-wide typed signals, one per payload type: <c>Signals.Of&lt;MoneyChanged&gt;().Subscribe(...)</c> and
/// <c>Signals.Raise(new MoneyChanged { Balance = 100 })</c>. A payload type is the whole contract; adding an event is
/// adding a struct, not editing a shared file. Reset between scenes/tests like GameEvents.
/// </summary>
public static class Signals
{
    private static readonly Dictionary<Type, object> all = new Dictionary<Type, object>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => all.Clear();

    public static Signal<T> Of<T>()
    {
        object o;
        if (!all.TryGetValue(typeof(T), out o)) { o = new Signal<T>(); all[typeof(T)] = o; }
        return (Signal<T>)o;
    }

    public static void Raise<T>(T value) => Of<T>().Raise(value);
    public static IDisposable On<T>(Action<T> handler, UnityEngine.Object owner = null) => Of<T>().Subscribe(handler, owner);
    public static int Topics => all.Count;
    public static void ClearAll() => all.Clear();
}

// ---------------------------------------------------------------------------------------------------------------
// Payload types for the core signals. Systems raise these next to the legacy GameEvents.* calls during the
// migration (ARCHITECTURE.md, stage 2); new systems should use only these.
// ---------------------------------------------------------------------------------------------------------------

/// <summary>A new in-game day began (mirrors WorldClock.DayStarted).</summary>
public struct DayStartedSignal { public int Day; public Season Season; }
/// <summary>The player's cash changed.</summary>
public struct MoneyChangedSignal { public int Balance; public int Delta; public string Reason; }
/// <summary>Goods entered or left the farm's storage.</summary>
public struct StorageChangedSignal { public string GoodId; public float Kg; public float Total; }
/// <summary>The player entered (true) or left a vehicle.</summary>
public struct VehicleChangedSignal { public bool Entered; public string VehicleId; }
/// <summary>A field finished a job (plow, sow, harvest...); FarmCore raises these for workers, missions and HUD.</summary>
public struct FieldWorkedSignal { public int FieldIndex; public string Job; public string CropId; public float Kg; }
/// <summary>The active region/content pack set changed (career start, DLC unlock).</summary>
public struct ContentChangedSignal { public string RegionId; public string[] PackIds; }
/// <summary>Co-op: a player joined or left the session.</summary>
public struct PlayerPresenceSignal { public ulong ClientId; public string Name; public bool Joined; }
