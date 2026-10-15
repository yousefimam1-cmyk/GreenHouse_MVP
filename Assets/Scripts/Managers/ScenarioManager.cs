using System;
using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public class ScenarioDef
{
    public string id;
    public string titleEn, titleAr, briefEn, briefAr;
    public string objective;      // "allHealthy" | "harvestKg"
    public float target;
    public float timeLimit = 180f;
    public float goldTime = 60f, silverTime = 120f;
    public string setup;          // "randomize" or ""
    public string breakModule;    // ModuleType name or ""
}

[Serializable]
public class ScenarioFile { public ScenarioDef[] scenarios; }

/// <summary>
/// Timed training scenarios (Resources/Data/scenarios.json): failures to fix or targets to reach,
/// graded Gold/Silver/Bronze with best times saved. T = start/stop, N = choose the next scenario.
/// Built so the same scenarios can later drive a VR training build.
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    public static ScenarioManager Instance;

    private ScenarioDef[] defs = new ScenarioDef[0];
    private GreenhouseManager[] greenhouses = new GreenhouseManager[0];
    private int selected;

    public bool HasScenarios => defs.Length > 0;
    public bool Running { get; private set; }
    public ScenarioDef Selected => HasScenarios ? defs[selected] : null;
    public float Elapsed { get; private set; }
    public float HarvestedKg { get; private set; }
    public int Optimizations { get; private set; }

    private void Awake() => Instance = this;

    private void Start()
    {
        TextAsset json = Resources.Load<TextAsset>("Data/scenarios");
        if (json != null)
        {
            try
            {
                var file = JsonUtility.FromJson<ScenarioFile>(json.text);
                if (file?.scenarios != null) defs = file.scenarios;
            }
            catch (Exception e) { Debug.LogError("[Scenario] Invalid scenarios.json: " + e.Message); }
        }
        greenhouses = FindObjectsByType<GreenhouseManager>(FindObjectsSortMode.None);
        foreach (var gh in greenhouses) gh.OnModuleOptimized += HandleOptimized;
        if (GameManager.Instance != null) GameManager.Instance.OnHarvest += HandleHarvest;
    }

    private void OnDestroy()
    {
        foreach (var gh in greenhouses) if (gh != null) gh.OnModuleOptimized -= HandleOptimized;
        if (GameManager.Instance != null) GameManager.Instance.OnHarvest -= HandleHarvest;
        if (Instance == this) Instance = null;
    }

    private void HandleOptimized(GreenhouseManager gh, ModuleType t) { if (Running) Optimizations++; }
    private void HandleHarvest(float kg) { if (Running) HarvestedKg += kg; }

    public static string Title(ScenarioDef d) => d == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(d.titleAr) ? d.titleAr : d.titleEn);
    public static string Brief(ScenarioDef d) => d == null ? "" : (Loc.IsArabic && !string.IsNullOrEmpty(d.briefAr) ? d.briefAr : d.briefEn);

    private void Update()
    {
        var kb = Keyboard.current;
        bool uiOpen = UIManager.Instance != null && UIManager.Instance.IsUIOpened;
        if (kb != null && !uiOpen && HasScenarios)
        {
            if (kb.tKey.wasPressedThisFrame) { if (Running) Stop(Loc.T("Training stopped")); else Begin(); }
            else if (kb.nKey.wasPressedThisFrame && !Running) selected = (selected + 1) % defs.Length;
        }

        if (!Running) return;
        Elapsed += Time.deltaTime;
        if (IsComplete()) Finish();
        else if (Elapsed >= Selected.timeLimit) Stop(Loc.T("Time's up - training failed"));
    }

    public void Begin()
    {
        var d = Selected;
        if (d == null) return;
        bool randomize = string.Equals(d.setup, "randomize", StringComparison.OrdinalIgnoreCase);
        bool hasBreak = Enum.TryParse(d.breakModule, true, out ModuleType broken) && !string.IsNullOrEmpty(d.breakModule);
        foreach (var gh in greenhouses)
        {
            if (gh == null) continue;
            if (randomize) gh.ResetModules(true);
            if (hasBreak) gh.BreakModule(broken);
        }
        Elapsed = 0f;
        HarvestedKg = 0f;
        Optimizations = 0;
        Running = true;
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(Title(d) + ": " + Brief(d)));
    }

    private void Stop(string message)
    {
        Running = false;
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(message);
    }

    public int HealthyBeds(out int total)
    {
        total = 0;
        int healthy = 0;
        foreach (var gh in greenhouses)
        {
            if (gh == null) continue;
            total += gh.Cropbeds.Count;
            healthy += gh.HealthyCount;
        }
        return healthy;
    }

    private bool IsComplete()
    {
        var d = Selected;
        if (string.Equals(d.objective, "harvestKg", StringComparison.OrdinalIgnoreCase))
            return HarvestedKg >= d.target;
        int healthy = HealthyBeds(out int total);
        return total > 0 && healthy == total;
    }

    /// <summary>Localized one-line progress for the HUD.</summary>
    public string ProgressText()
    {
        var d = Selected;
        if (d == null) return "";
        string progress;
        if (string.Equals(d.objective, "harvestKg", StringComparison.OrdinalIgnoreCase))
            progress = Loc.T("Harvested {0}/{1} kg", HarvestedKg.ToString("F0"), d.target.ToString("F0"));
        else
        {
            int healthy = HealthyBeds(out int total);
            progress = Loc.T("Healthy beds {0}/{1}", healthy, total);
        }
        int left = Mathf.Max(0, Mathf.CeilToInt(d.timeLimit - Elapsed));
        return progress + "\n" + Loc.T("Time left {0}s", left);
    }

    public float BestTime(ScenarioDef d) => d == null ? -1f : PlayerPrefs.GetFloat("mazraa.best." + d.id, -1f);

    private void Finish()
    {
        var d = Selected;
        Running = false;
        string grade = Elapsed <= d.goldTime ? "Gold" : Elapsed <= d.silverTime ? "Silver" : "Bronze";
        float best = BestTime(d);
        if (best < 0f || Elapsed < best)
        {
            PlayerPrefs.SetFloat("mazraa.best." + d.id, Elapsed);
            PlayerPrefs.Save();
        }
        string msg = Loc.Raw("Training complete: {0}");
        msg = string.Format(msg, Loc.Raw(grade)) + " - " + Elapsed.ToString("F0") + "s, " +
              string.Format(Loc.Raw("Optimizations: {0}"), Optimizations);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(msg));
    }
}
