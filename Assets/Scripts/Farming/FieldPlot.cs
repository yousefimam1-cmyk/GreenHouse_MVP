using System.Collections.Generic;
using UnityEngine;

public enum PlotState { Untilled, Plowed, Growing, Ripe }

/// <summary>
/// An open-field plot. E does the next job: plow, plant (uses a seed bag from the shop), water (needs the well
/// running), harvest. Crops grow only while watered; out of season they grow at a quarter speed; rain waters,
/// heatwaves dry twice as fast. Three visual growth stages. State is saved per plot.
/// </summary>
public class FieldPlot : Interactable
{
    public int index;
    public Vector2 size = new Vector2(6f, 6f);

    public PlotState State { get; private set; }
    public string CropId { get; private set; } = "";
    public float Growth { get; private set; }      // 0..1
    public float Water { get; private set; }       // 0..1

    private Transform soil, plantsRoot;
    private readonly List<Transform> plants = new List<Transform>();
    private int shownStage = -1;
    private string shownCrop = "";
    private string Key => "mazraa.plot." + index + ".";

    public static event System.Action<FieldPlot, string, float> FieldHarvested;
    public static event System.Action<FieldPlot> FieldPlanted;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { FieldHarvested = null; FieldPlanted = null; }

    public override string InteractionPrompt
    {
        get
        {
            var eco = EconomyManager.Instance;
            switch (State)
            {
                case PlotState.Untilled: return Loc.T("[E] Plow field");
                case PlotState.Plowed:
                    var seed = BestSeed();
                    return seed != null ? Loc.Display(Loc.Raw("[E] Plant") + " " + FieldCropText.Name(seed) + " (" + eco.Seeds(seed.id) + ")") : Loc.T("No seeds - buy them at a Farm Shop");
                case PlotState.Growing:
                    string crop = FieldCropText.Name(eco != null ? eco.FieldCrop(CropId) : null);
                    if (Water < 0.35f) return Loc.Display(Loc.Raw("[E] Water") + " - " + crop);
                    return Loc.Display(crop + " " + Mathf.FloorToInt(Growth * 100f) + "% - " + Loc.Raw(InSeason() ? "In season" : "Out of season - slow"));
                default: return Loc.T("[E] Harvest");
            }
        }
    }

    public void Setup(int idx, Vector2 plotSize)
    {
        index = idx; size = plotSize;
        State = (PlotState)SaveData.GetInt(Key + "state", 0);
        CropId = SaveData.GetString(Key + "crop", "");
        Growth = SaveData.GetFloat(Key + "growth", 0f);
        Water = SaveData.GetFloat(Key + "water", 0f);
        BuildSoil();
        Refresh();
    }

    private void BuildSoil()
    {
        var col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 0.15f, 0); col.size = new Vector3(size.x, 0.3f, size.y);
        soil = Build.Part(PrimitiveType.Cube, transform, new Vector3(0, 0.05f, 0), new Vector3(size.x, 0.12f, size.y), Build.Mat(new Color(0.42f, 0.33f, 0.22f), 0.05f), "Soil").transform;
        plantsRoot = new GameObject("Plants").transform;
        plantsRoot.SetParent(transform, false);
        // Furrows
        for (int i = 0; i < 5; i++)
            Build.Part(PrimitiveType.Cube, soil, new Vector3(0, 0.55f, -0.4f + i * 0.2f), new Vector3(0.96f, 0.3f, 0.04f), Build.Mat(new Color(0.32f, 0.24f, 0.16f), 0.05f), "Furrow");
        gameObject.layer = 7;
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
    }

    private FieldCropDef BestSeed()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return null;
        FieldCropDef fallback = null;
        foreach (var c in eco.Data.fieldCrops)
        {
            if (eco.Seeds(c.id) <= 0) continue;
            if (InSeason(c)) return c;
            if (fallback == null) fallback = c;
        }
        return fallback;
    }

    private bool InSeason() => InSeason(EconomyManager.Instance?.FieldCrop(CropId));
    private static bool InSeason(FieldCropDef c)
    {
        if (c == null || c.seasons == null || WorldClock.Instance == null) return true;
        string now = WorldClock.SeasonId(WorldClock.Instance.Season);
        foreach (var s in c.seasons) if (s == now) return true;
        return false;
    }

    public override void Interact()
    {
        base.Interact();
        switch (State)
        {
            case PlotState.Untilled: TryPlow(true); break;
            case PlotState.Plowed: TrySeed(true); break;
            case PlotState.Growing:
                if (!WellRunning()) { Say(Loc.T("Start the well first to get water")); return; }
                Water = 1f;
                Say(Loc.T("Field watered"));
                Refresh(); Save();
                break;
            case PlotState.Ripe: TryHarvest(true); break;
        }
    }

    // --- Actions shared by the player (E) and tractor implements ---

    public bool TryPlow(bool announce)
    {
        if (State != PlotState.Untilled) return false;
        State = PlotState.Plowed;
        if (announce) Say(Loc.T("Field plowed"));
        Refresh(); Save();
        return true;
    }

    public bool TrySeed(bool announce)
    {
        if (State != PlotState.Plowed) return false;
        var eco = EconomyManager.Instance;
        var seed = BestSeed();
        if (seed == null || eco == null || !eco.UseSeed(seed.id)) { if (announce) Say(Loc.T("No seeds - buy them at a Farm Shop")); return false; }
        CropId = seed.id; Growth = 0f; State = PlotState.Growing;
        FieldPlanted?.Invoke(this);
        if (Water < 0.3f) Water = 0.3f;
        if (announce) Say(Loc.Display(Loc.Raw("Planted") + " " + FieldCropText.Name(seed) + (InSeason(seed) ? "" : " - " + Loc.Raw("Out of season - slow"))));
        Refresh(); Save();
        return true;
    }

    public bool TryHarvest(bool announce)
    {
        if (State != PlotState.Ripe) return false;
        var eco = EconomyManager.Instance;
        var crop = eco != null ? eco.FieldCrop(CropId) : null;
        float kg = crop != null ? crop.yieldKg : 50f;
        if (eco != null) eco.AddGood(CropId, kg);
        FieldHarvested?.Invoke(this, CropId, kg);
        if (GameManager.Instance != null) GameManager.Instance.RegisterFieldHarvest(kg);
        FeedbackFX.Float(transform.position + Vector3.up * 1.4f, "+" + kg.ToString("F0") + " kg", new Color(0.95f, 0.85f, 0.4f));
        if (announce) Say(Loc.Display(Loc.Raw("Harvested") + " " + kg.ToString("F0") + " " + Loc.Raw("kg") + " " + FieldCropText.Name(crop)));
        State = PlotState.Untilled; CropId = ""; Growth = 0f;
        Refresh(); Save();
        return true;
    }

    /// <summary>True when the world point lies on this plot.</summary>
    public bool Contains(Vector3 world)
    {
        Vector3 l = transform.InverseTransformPoint(world);
        return Mathf.Abs(l.x) <= size.x * 0.5f && Mathf.Abs(l.z) <= size.y * 0.5f;
    }

    private static bool WellRunning()
    {
        var wells = FindObjectsByType<WellController>(FindObjectsSortMode.None);
        if (wells.Length == 0) return true;
        foreach (var w in wells) if (w.IsRunning) return true;
        return false;
    }

    private void Update()
    {
        if (State != PlotState.Growing) return;
        var clock = WorldClock.Instance;
        bool raining = clock != null && clock.Weather == Weather.Rain;
        if (raining) Water = 1f;
        float dry = (clock != null && clock.Weather == Weather.Heatwave ? 2f : 1f) / 150f;
        Water = Mathf.Max(0f, Water - dry * Time.deltaTime);
        if (Water > 0.05f)
        {
            var crop = EconomyManager.Instance?.FieldCrop(CropId);
            float seconds = crop != null ? crop.growSeconds : 200f;
            float rate = (InSeason() ? 1f : 0.25f) * EconomyManager.GrowthSpeed / Mathf.Max(10f, seconds);
            Growth = Mathf.Min(1f, Growth + rate * Time.deltaTime);
            if (Growth >= 1f) { State = PlotState.Ripe; Save(); }
        }
        Refresh();
    }

    private void Refresh()
    {
        if (soil == null) return;
        // Wet soil is darker.
        float wet = State == PlotState.Untilled ? 0f : Water;
        var soilColor = Color.Lerp(new Color(0.55f, 0.45f, 0.3f), new Color(0.28f, 0.2f, 0.13f), wet);
        if (State == PlotState.Untilled) soilColor = new Color(0.5f, 0.47f, 0.33f);
        var r = soil.GetComponent<MeshRenderer>();
        r.sharedMaterial = Build.Mat(new Color(Mathf.Round(soilColor.r * 20f) / 20f, Mathf.Round(soilColor.g * 20f) / 20f, Mathf.Round(soilColor.b * 20f) / 20f), 0.05f + wet * 0.3f);
        for (int i = 0; i < soil.childCount; i++) soil.GetChild(i).gameObject.SetActive(State != PlotState.Untilled);

        int stage = State == PlotState.Growing ? (Growth < 0.34f ? 1 : Growth < 0.67f ? 2 : 3) : State == PlotState.Ripe ? 4 : 0;
        if (stage == shownStage && CropId == shownCrop) return;
        shownStage = stage; shownCrop = CropId;
        foreach (var p in plants) if (p != null) Destroy(p.gameObject);
        plants.Clear();
        if (stage == 0) return;
        var crop = EconomyManager.Instance?.FieldCrop(CropId);
        var leaf = Build.Mat(new Color(0.28f, 0.55f, 0.2f), 0.2f);
        var fruit = Build.Mat(crop != null ? new Color(crop.r, crop.g, crop.b) : Color.yellow, 0.35f);
        float h = stage == 1 ? 0.15f : stage == 2 ? 0.4f : 0.7f;
        for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                var plant = new GameObject("Plant").transform;
                plant.SetParent(plantsRoot, false);
                plant.localPosition = new Vector3(-size.x * 0.4f + x * size.x * 0.2f, 0.1f, -size.y * 0.4f + z * size.y * 0.2f);
                plant.localRotation = Quaternion.Euler(0, (x * 37 + z * 61) % 360, 0);
                if (CropId == "wheat")
                {
                    var stalk = stage == 4 ? fruit : leaf;
                    for (int s = 0; s < 3; s++)
                        Build.Part(PrimitiveType.Cube, plant, new Vector3((s - 1) * 0.12f, h * 0.5f, 0), new Vector3(0.04f, h, 0.04f), stalk);
                    if (stage >= 3) Build.Part(PrimitiveType.Cube, plant, new Vector3(0, h + 0.08f, 0), new Vector3(0.32f, 0.16f, 0.06f), fruit);
                }
                else
                {
                    Build.Part(PrimitiveType.Sphere, plant, new Vector3(0, h * 0.5f, 0), new Vector3(h * 1.1f, h, h * 1.1f), leaf);
                    if (stage == 4)
                    {
                        float fs = CropId == "watermelon" ? 0.45f : 0.2f;
                        Build.Part(PrimitiveType.Sphere, plant, new Vector3(0.22f, fs * 0.5f, 0.1f), Vector3.one * fs, fruit);
                    }
                }
                foreach (var rr in plant.GetComponentsInChildren<Renderer>(true)) rr.gameObject.layer = 7;
                plants.Add(plant);
            }
    }

    private void Save()
    {
        SaveData.SetInt(Key + "state", (int)State);
        SaveData.SetString(Key + "crop", CropId);
        SaveData.SetFloat(Key + "growth", Growth);
        SaveData.SetFloat(Key + "water", Water);
    }

    private void OnApplicationQuit() { Save(); SaveData.Save(); }

    public void ResetPlot()
    {
        State = PlotState.Untilled; CropId = ""; Growth = 0f; Water = 0f;
        Save(); Refresh();
    }

    private static void Say(string msg) { if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg); }
}
