using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Feeds TickScheduler.Main from the engine: one Update advances every Frame / Quarter / Second subscriber, and
/// WorldClock.DayStarted triggers the Daily ones. Added to the GameManager automatically (first, so the scheduler
/// runs before the systems that depend on it in the same frame). Also measures the time spent in callbacks so the
/// performance budget (ARCHITECTURE.md: 2 ms per frame for scheduled logic) can be enforced by a test or overlay.
/// </summary>
[MazraaSystem(-95)]
public class TickDriver : MonoBehaviour
{
    public static TickDriver Instance { get; private set; }
    private readonly Stopwatch watch = new Stopwatch();

    /// <summary>Frames in which the scheduled callbacks took longer than BudgetMs (debug overlay / tests).</summary>
    public int OverBudgetFrames { get; private set; }
    public float BudgetMs = 2f;

    private void Awake() => Instance = this;

    private void OnEnable() => WorldClock.DayStarted += OnDay;
    private void OnDisable() => WorldClock.DayStarted -= OnDay;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private static void OnDay(int day) => TickScheduler.Main.RaiseDaily(day);

    private void Update()
    {
        watch.Restart();
        TickScheduler.Main.Advance(Time.deltaTime);
        watch.Stop();
        float ms = (float)watch.Elapsed.TotalMilliseconds;
        TickScheduler.Main.LastFrameMs = ms;
        if (ms > BudgetMs) OverBudgetFrames++;
    }
}
