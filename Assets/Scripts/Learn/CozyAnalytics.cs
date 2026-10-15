using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable] public class AnalyticsSession { public string startedUtc; public int seconds, missionsDone, sponsorPurchases; public List<string> sponsorItems = new List<string>(); }
[Serializable] public class AnalyticsFile
{
    public string note = "Mazraa local play statistics. Opt-in, saved only on this computer, never uploaded.";
    public int sessions, totalSeconds, missionsDone, sponsorPurchases;
    public List<AnalyticsSession> recent = new List<AnalyticsSession>();
}

/// <summary>
/// Opt-in local analytics (default off; toggle with 0 in the learn panel, K): session length, missions completed and
/// sponsor item purchases, written only to Application.persistentDataPath/mazraa_analytics.json. Nothing is sent
/// anywhere. Turning it off deletes the file. The opt-in (mazraa.learn.analytics) survives a new game.
/// </summary>
[MazraaSystem(69)]
public class CozyAnalytics : MonoBehaviour
{
    public static CozyAnalytics Instance;
    private const string OptKey = "mazraa.learn.analytics";
    private const int KeepSessions = 30;

    public static string FilePath => Path.Combine(Application.persistentDataPath, "mazraa_analytics.json");
    public static bool OptIn { get; private set; }

    private AnalyticsSession session;
    private float sessionStart;
    private AnalyticsFile before;   // totals from earlier sessions

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OptIn = false;

    private void Awake()
    {
        Instance = this;
        OptIn = SaveData.GetInt(OptKey, 0) == 1;
        sessionStart = Time.unscaledTime;
        session = new AnalyticsSession { startedUtc = DateTime.UtcNow.ToString("o") };
    }

    private void Start()
    {
        GameEvents.MissionCompleted += OnMission;
        SponsorManager.Purchased += OnSponsor;
        InvokeRepeating(nameof(Write), 60f, 60f);
    }

    private void OnDestroy()
    {
        GameEvents.MissionCompleted -= OnMission;
        SponsorManager.Purchased -= OnSponsor;
        Write();
        if (Instance == this) Instance = null;
    }

    private void OnApplicationQuit() => Write();

    // Nothing is counted while opted out.
    private void OnMission(MissionDef m) { if (OptIn) session.missionsDone++; }
    private void OnSponsor(string id) { if (!OptIn) return; session.sponsorPurchases++; session.sponsorItems.Add(id); Write(); }

    public static void SetOptIn(bool on)
    {
        OptIn = on;
        WriteOptIn();
        if (!on)
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception) { }
            if (Instance != null) Instance.before = null;
        }
        else if (Instance != null) { Instance.NewSession(); Instance.Write(); }
        CozyFx.Alert(Loc.T(on ? "Play stats on (saved only on this computer)" : "Play stats off (file deleted)"));
    }

    /// <summary>Store the opt-in in the save (also re-written after a new game wipes the save).</summary>
    public static void WriteOptIn() { SaveData.SetInt(OptKey, OptIn ? 1 : 0); SaveData.Save(); }

    /// <summary>Start counting from now (opting in mid-session does not include what happened before).</summary>
    private void NewSession()
    {
        sessionStart = Time.unscaledTime;
        session = new AnalyticsSession { startedUtc = DateTime.UtcNow.ToString("o") };
        before = null;
    }

    public int SessionSeconds => Mathf.RoundToInt(Time.unscaledTime - sessionStart);
    public AnalyticsSession Current => session;

    /// <summary>Write the file now (only when opted in).</summary>
    public void Write()
    {
        if (!OptIn || session == null) return;
        try
        {
            if (before == null)
            {
                before = new AnalyticsFile();
                if (File.Exists(FilePath)) { var f = JsonUtility.FromJson<AnalyticsFile>(File.ReadAllText(FilePath)); if (f != null) before = f; }
                if (before.recent == null) before.recent = new List<AnalyticsSession>();
                before.recent.RemoveAll(s => s == null || s.startedUtc == session.startedUtc);
            }
            session.seconds = SessionSeconds;
            var file = new AnalyticsFile
            {
                sessions = before.sessions + 1,
                totalSeconds = before.totalSeconds + session.seconds,
                missionsDone = before.missionsDone + session.missionsDone,
                sponsorPurchases = before.sponsorPurchases + session.sponsorPurchases,
            };
            file.recent.AddRange(before.recent);
            file.recent.Add(session);
            while (file.recent.Count > KeepSessions) file.recent.RemoveAt(0);
            File.WriteAllText(FilePath, JsonUtility.ToJson(file, true));
        }
        catch (Exception e) { Debug.LogWarning("[Analytics] Could not write: " + e.Message); }
    }
}
