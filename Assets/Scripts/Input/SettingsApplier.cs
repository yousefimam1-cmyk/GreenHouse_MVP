using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs the Settings that need the game world (GameSettingsOptions.cs holds the values):
/// UI text size (CanvasScaler reference size), help hints, ambient life density, timed autosave, crop withering,
/// hired helpers buying seeds, controller rumble, field of view / view distance for cameras made later.
/// </summary>
[MazraaSystem(96)]
public class SettingsApplier : MonoBehaviour
{
    public static SettingsApplier Instance { get; private set; }
    public static readonly Vector2 BaseReference = new Vector2(1920, 1080);

    /// <summary>Reference resolution for ScaleWithScreenSize canvases at the chosen UI size (smaller = bigger text).</summary>
    public static Vector2 UiReference => BaseReference * (100f / GameSettings.UiScale);

    public int Autosaves { get; private set; }
    public int Withered { get; private set; }
    public int HelperSeedsBought { get; private set; }

    private float autosaveTimer, slowTimer, rescanTimer;
    private readonly Dictionary<int, int> withered = new Dictionary<int, int>();   // FarmCore field id -> withered cells seen
    private readonly List<Actor> hidden = new List<Actor>();

    private void Awake() => Instance = this;
    private void OnEnable() { GameEvents.Harvested += OnHarvest; GameEvents.VehicleChanged += OnVehicle; }
    private void OnDisable() { GameEvents.Harvested -= OnHarvest; GameEvents.VehicleChanged -= OnVehicle; }
    private void OnDestroy() { if (Instance == this) Instance = null; GameSettings.StopRumble(); }

    private void Start() => Apply();

    /// <summary>Called from GameSettings.ApplyOptions whenever a setting changes.</summary>
    public static void ApplyNow() { if (Instance != null) Instance.Apply(); }

    private void Apply()
    {
        ApplyUiScale();
        ApplyAmbientDensity();
        if (GameHUD.Instance != null) GameHUD.Instance.SetHintsVisible(GameSettings.ShowHints);
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) GraphicsEnhancer.ConfigureView(cam);
    }

    public static void ApplyUiScale()
    {
        Vector2 refRes = UiReference;
        foreach (var s in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (s != null && s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) s.referenceResolution = refRes;
    }

    /// <summary>Hide part of the wandering / flying ambient actors (cat, horses, eagles, market buyers, villagers).</summary>
    private void ApplyAmbientDensity()
    {
        foreach (var a in hidden) if (a != null) a.gameObject.SetActive(true);
        hidden.Clear();
        int level = GameSettings.AmbientDensity;
        if (level >= 3) return;
        var list = new List<Actor>(FindObjectsByType<Actor>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        list.RemoveAll(a => a == null || (a.mode != ActorMode.Wander && a.mode != ActorMode.Fly) || !a.gameObject.activeSelf);
        list.Sort((x, y) => { int c = string.CompareOrdinal(x.name, y.name); return c != 0 ? c : x.transform.GetSiblingIndex().CompareTo(y.transform.GetSiblingIndex()); });
        for (int i = 0; i < list.Count; i++)
            if (i % 3 >= level) { list[i].gameObject.SetActive(false); hidden.Add(list[i]); }
    }

    /// <summary>Number of ambient actors hidden by the density setting (tests).</summary>
    public int HiddenAmbient => hidden.Count;

    private void Update()
    {
        float dt = Time.deltaTime;   // stops while a menu pauses the game
        if (GameSettings.RumbleUntil > 0f && Time.unscaledTime >= GameSettings.RumbleUntil) GameSettings.StopRumble();

        // Timed autosave (in-game-day autosave is GameMenu's)
        int mins = GameSettings.AutosaveMinutes;
        if (mins > 0 && dt > 0f)
        {
            autosaveTimer += dt;
            if (autosaveTimer >= mins * 60f) { autosaveTimer = 0f; Autosave(); }
        }
        else autosaveTimer = 0f;

        slowTimer += dt;
        if (slowTimer >= 1f) { TickWithering(slowTimer); TickHelpers(); slowTimer = 0f; }

        rescanTimer += Time.unscaledDeltaTime;
        if (rescanTimer >= 2f)
        {
            rescanTimer = 0f;
            if (GameSettings.UiScale != 100) ApplyUiScale();   // canvases built later (shops, panels)
            if (GameSettings.CameraFov > 0 || GameSettings.ViewDistance > 0)
                foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None)) GraphicsEnhancer.ConfigureView(cam);
        }
    }

    public void Autosave()
    {
        SaveSlots.Autosave();
        Autosaves++;
        if (UIManager.Instance != null && !GameMenu.IsOpen) UIManager.Instance.ShowAlertMessage(UiText.T("Autosaved"));
    }

    /// <summary>
    /// Crops left bone dry for a whole in-game day (or ripe too long) wither (Settings > Game > Withering). FarmCore's
    /// growth model withers the cells (FarmSim); this counts fields where new cells withered and tells the player.
    /// </summary>
    public void TickWithering(float seconds)
    {
        var ff = FarmFields.Instance;
        if (ff == null) return;
        if (!GameSettings.Withering) { withered.Clear(); return; }
        foreach (var f in ff.Fields)
        {
            int n = 0;
            for (int i = 0; i < f.Cells; i++) if (f.ground[i] == (byte)Ground.Sown && f.Has(i, CellFlags.Withered)) n++;
            int seen;
            withered.TryGetValue(f.id, out seen);
            if (n > seen)
            {
                Withered++;
                if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(UiText.T("A crop withered: the field was dry for a whole day"));
            }
            withered[f.id] = n;
        }
    }

    /// <summary>Hired field hands buy two bags of the cheapest in-season seed when the store has none (Settings > Game > Helper refills).</summary>
    public void TickHelpers()
    {
        if (!GameSettings.HelperRefill) return;
        var wm = WorkerManager.Instance; var eco = EconomyManager.Instance; var ff = FarmFields.Instance;
        if (wm == null || eco == null || ff == null || !wm.IsHired(WorkerRole.FieldHand) || eco.Data == null) return;
        bool needSeeds = false;
        foreach (var f in ff.Fields) { var s = FarmSim.Summarize(f); if (s.sown < s.cells) needSeeds = true; }
        if (!needSeeds) return;
        FieldCropDef pick = null;
        foreach (var c in eco.Data.fieldCrops)
        {
            if (eco.Seeds(c.id) > 0) return;   // still has seeds
            if (EconomyManager.Available(c) && FarmFields.InSeasonNow(c.id) && (pick == null || c.seedPrice < pick.seedPrice)) pick = c;
        }
        if (pick == null || !eco.Spend(pick.seedPrice * 2)) return;
        eco.AddSeeds(pick.id, 2);
        HelperSeedsBought += 2;
    }

    private void OnHarvest(Cropbed bed, float kg) => GameSettings.Rumble(0.15f, 0.35f, 0.12f);
    private void OnVehicle(bool entered) { if (entered) GameSettings.Rumble(0.45f, 0.2f, 0.25f); }
}
