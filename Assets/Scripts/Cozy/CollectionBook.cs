using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// J = collection book: crops harvested (greenhouse + fields), animals owned, fish caught (GameEvents.Custom "catchFish",
/// or "catchFish:&lt;species&gt;"), sponsors met, plus feats. Shows a percentage complete. Saved under mazraa.cozy.book.*.
/// </summary>
[MazraaSystem(65)]
public class CollectionBook : MonoBehaviour
{
    public static CollectionBook Instance;
    public CozyPanel Panel { get; private set; }

    public static readonly string[] Categories = { "crops", "animals", "fish", "sponsors", "feats" };
    private readonly Dictionary<string, HashSet<string>> found = new Dictionary<string, HashSet<string>>();
    private readonly List<CropProfile> greenhouseCrops = new List<CropProfile>();
    public int FishCaught { get; private set; }

    private void Awake()
    {
        Instance = this;
        Load();
        var json = Resources.Load<TextAsset>("Data/crop_profiles");
        try { var f = json != null ? JsonUtility.FromJson<CropDatabaseFile>(json.text) : null; if (f != null && f.crops != null) greenhouseCrops.AddRange(f.crops); }
        catch (System.Exception) { }
    }

    private void Load()
    {
        found.Clear();
        foreach (var c in Categories) found[c] = CozyFx.LoadSet("mazraa.cozy.book." + c);
        FishCaught = SaveData.GetInt("mazraa.cozy.book.fishCount", 0);
    }

    private void Start()
    {
        Panel = new CozyPanel(transform, "Collection Book UI", 820f);
        GameEvents.Harvested += OnHarvested;
        FieldPlot.FieldHarvested += OnFieldHarvested;
        GameEvents.Custom += OnCustom;
        GameEvents.AnimalBorn += OnAnimal;
        GameEvents.UpgradeBought += OnAnimal;
        GameEvents.NewGameStarted += OnNewGame;
        Loc.OnLanguageChanged += Refresh;
        InvokeRepeating(nameof(CheckAnimals), 3f, 5f);
    }

    private void OnDestroy()
    {
        GameEvents.Harvested -= OnHarvested;
        FieldPlot.FieldHarvested -= OnFieldHarvested;
        GameEvents.Custom -= OnCustom;
        GameEvents.AnimalBorn -= OnAnimal;
        GameEvents.UpgradeBought -= OnAnimal;
        GameEvents.NewGameStarted -= OnNewGame;
        Loc.OnLanguageChanged -= Refresh;
        if (Instance == this) Instance = null;
    }

    private void OnNewGame() { Load(); Refresh(); }

    /// <summary>Add an entry (category: crops, animals, fish, sponsors, feats). Returns true when it is new.</summary>
    public static bool Record(string category, string id)
    {
        var b = Instance;
        if (b == null || string.IsNullOrEmpty(id) || !b.found.ContainsKey(category)) return false;
        if (!b.found[category].Add(id)) return false;
        CozyFx.SaveSet("mazraa.cozy.book." + category, b.found[category]);
        CozyFx.Alert(Loc.Display(Loc.Raw("New in your collection book") + ": " + b.EntryName(category, id)));
        b.Refresh();
        return true;
    }

    public bool Has(string category, string id) => found.ContainsKey(category) && found[category].Contains(id);
    public int Count(string category) => found.ContainsKey(category) ? found[category].Count : 0;

    private void OnHarvested(Cropbed bed, float kg) { if (bed != null && bed.Crop != null) Record("crops", bed.Crop.id); }
    private void OnFieldHarvested(FieldPlot p, string crop, float kg) => Record("crops", crop);
    private void OnAnimal(string id) => CheckAnimals();

    private void OnCustom(string type, float amount)
    {
        if (type == null) return;
        if (type == "catchFish" || type.StartsWith("catchFish:"))
        {
            FishCaught += Mathf.Max(1, Mathf.RoundToInt(amount));
            SaveData.SetInt("mazraa.cozy.book.fishCount", FishCaught);
            SaveData.Save();
            Record("fish", type == "catchFish" ? "fish" : type.Substring("catchFish:".Length));
        }
    }

    private void CheckAnimals()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        foreach (var a in eco.Data.animals) if (eco.Owned(a.id) > 0) Record("animals", a.id);
    }

    // ---------------- totals ----------------

    public int Total(string category)
    {
        var eco = EconomyManager.Instance;
        switch (category)
        {
            case "crops": return greenhouseCrops.Count + (eco != null ? eco.Data.fieldCrops.Length + (eco.Good("dates") != null ? 1 : 0) : 0);
            case "animals": return eco != null ? eco.Data.animals.Length : 0;
            case "fish":
                int fishGoods = 0;
                if (eco != null) foreach (var g in eco.Data.goods) if (g.id.Contains("fish")) fishGoods++;
                return Mathf.Max(Count("fish"), fishGoods);     // 0 when no fishing in this build: not counted
            case "sponsors": return SponsorManager.Instance != null ? SponsorManager.Instance.Brands.Count : 0;
            case "feats": return 1;
        }
        return 0;
    }

    /// <summary>0..100 over every entry that can be collected in this build.</summary>
    public float Percent
    {
        get
        {
            int have = 0, total = 0;
            foreach (var c in Categories) { int t = Total(c); if (t <= 0) continue; total += t; have += Mathf.Min(Count(c), t); }
            return total == 0 ? 0f : 100f * have / total;
        }
    }

    private string EntryName(string category, string id)
    {
        var eco = EconomyManager.Instance;
        switch (category)
        {
            case "crops":
                var fc = eco != null ? eco.FieldCrop(id) : null;
                if (fc != null) return FieldCropText.Name(fc);
                if (id == "dates" && eco != null && eco.Good("dates") != null) return EconomyManager.GoodName(eco.Good("dates"));
                var gc = greenhouseCrops.Find(c => c.id == id);
                return gc != null ? Loc.CropName(gc) : id;
            case "animals": return eco != null && eco.Animal(id) != null ? EconomyManager.AnimalName(eco.Animal(id)) : id;
            case "fish":
                var g = eco != null ? eco.Good(id) : null;
                return g != null ? EconomyManager.GoodName(g) : (id == "fish" ? Loc.Raw("Fish") : id);
            case "sponsors": return SponsorManager.Instance != null ? SponsorManager.Instance.BrandName(id) : id;
            case "feats": return Loc.Raw(id == "falconer" ? "Falconer" : id);
        }
        return id;
    }

    // ---------------- panel ----------------

    private void Update()
    {
        if (Panel == null) return;
        if (Panel.IsOpen) { if (Panel.CloseRequested || (CozyKeys.Down("j") && Time.frameCount != Panel.OpenedFrame)) Panel.Close(); }
        else if (CozyKeys.Free && CozyKeys.Down("j")) Open();
    }

    public void Open() { Panel.Open(); Refresh(); }

    private void Refresh()
    {
        if (Panel == null || !Panel.IsOpen) return;
        var lines = new List<string>();
        var eco = EconomyManager.Instance;
        lines.Add(Section("crops", "Crops harvested"));
        var crops = new List<string>();
        foreach (var c in greenhouseCrops) crops.Add((Has("crops", c.id) ? "+ " : "- ") + Loc.CropName(c));
        if (eco != null) foreach (var c in eco.Data.fieldCrops) crops.Add((Has("crops", c.id) ? "+ " : "- ") + FieldCropText.Name(c));
        if (eco != null && eco.Good("dates") != null) crops.Add((Has("crops", "dates") ? "+ " : "- ") + EconomyManager.GoodName(eco.Good("dates")));
        lines.Add("   " + string.Join("   ", crops.ToArray()));
        lines.Add(Section("animals", "Animals raised"));
        var animals = new List<string>();
        if (eco != null) foreach (var a in eco.Data.animals) animals.Add((Has("animals", a.id) ? "+ " : "- ") + EconomyManager.AnimalName(a));
        lines.Add("   " + string.Join("   ", animals.ToArray()));
        lines.Add(Total("fish") > 0 ? Section("fish", "Fish caught") + "   (" + FishCaught + ")" : Loc.Raw("Fish caught") + ": " + Loc.Raw("none yet"));
        lines.Add(Section("sponsors", "Sponsors met"));
        var sp = new List<string>();
        foreach (var id in found["sponsors"]) sp.Add(EntryName("sponsors", id));
        if (sp.Count > 0) lines.Add("   " + string.Join("   ", sp.ToArray()));
        lines.Add(Section("feats", "Feats"));
        Panel.Show(Loc.Raw("Collection book") + "  " + Mathf.FloorToInt(Percent) + "%", lines, Loc.Raw("[J/Esc] close"));
    }

    private string Section(string category, string key) => Loc.Raw(key) + ": " + Count(category) + "/" + Total(category);
}
