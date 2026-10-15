using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One orchard tree slot. E plants a sapling, pollinates a date palm in spring, sets a palm weevil trap,
/// or picks ripe fruit. Visual: a model from Resources/Models/Trees/&lt;name&gt; if present, else a primitive
/// trunk + fronds/crown that grows with age, with fruit when ripe.
/// </summary>
public class TreeSlot : Interactable
{
    public int index;

    public string Kind { get; private set; } = "";
    public float Age { get; private set; }           // seasons since planting
    public bool Pollinated { get; private set; }
    public float FruitKg { get; private set; }
    public int FruitSeasonsLeft { get; private set; }
    public string Pest { get; private set; } = "";
    public float Damage { get; private set; }
    public int ProtectDays { get; private set; }
    public int LastFruitDay { get; private set; } = -100;

    public TreeDef Def => CropsData.Tree(Kind);
    public bool IsMature => Def != null && Age >= Def.matureSeasons - 0.001f;
    /// <summary>Palms flower in spring: pollinate by hand for a full crop.</summary>
    public bool NeedsPollination
    {
        get
        {
            var d = Def;
            if (d == null || !d.palm || Pollinated || FruitKg > 0f || WorldClock.Instance == null || WorldClock.Instance.Season != Season.Spring) return false;
            return Age + 1f >= d.matureSeasons - 0.001f;   // mature by the summer harvest
        }
    }

    private string Key => "mazraa.crops.tree." + index + ".";
    private Transform visual;
    private string shownState = "";

    public void Setup(int idx)
    {
        index = idx;
        Kind = SaveData.GetString(Key + "kind", "");
        Age = SaveData.GetFloat(Key + "age", 0f);
        Pollinated = SaveData.GetInt(Key + "pollinated", 0) == 1;
        FruitKg = SaveData.GetFloat(Key + "fruit", 0f);
        FruitSeasonsLeft = SaveData.GetInt(Key + "fruitLeft", 0);
        Pest = SaveData.GetString(Key + "pest", "");
        Damage = SaveData.GetFloat(Key + "dmg", 0f);
        ProtectDays = SaveData.GetInt(Key + "prot", 0);
        LastFruitDay = SaveData.GetInt(Key + "fruitDay", -100);
        var col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0, 1.2f, 0); col.size = new Vector3(2.2f, 2.4f, 2.2f);
        gameObject.layer = 7;
        // Small irrigation basin around the trunk.
        Build.Part(PrimitiveType.Cylinder, transform, new Vector3(0, 0.03f, 0), new Vector3(2f, 0.03f, 2f), Build.Mat(new Color(0.45f, 0.34f, 0.22f), 0.05f), "Basin");
        Refresh();
    }

    public override string InteractionPrompt => Loc.Display(PromptRaw());

    public string PromptRaw()
    {
        var d = Def;
        var orchard = OrchardManager.Instance;
        if (d == null)
        {
            var next = orchard != null ? orchard.NextSapling() : null;
            return next != null ? CropsText.R("[E] Plant") + " " + CropsData.Name(next) + " (" + orchard.Saplings(next.id) + ")" : CropsText.R("Empty tree slot - buy saplings in the shop (Orchard)");
        }
        string name = CropsData.Name(d);
        if (Pest != "")
        {
            var cure = CropCareManager.Instance != null ? CropCareManager.Instance.OwnedCureFor(Pest) : null;
            string pest = CropsData.Name(CropsData.Pest(Pest));
            if (cure != null) return CropsText.R("[E] Treat") + " " + pest + ": " + CropsData.Name(cure) + " - " + name;
            if (FruitKg <= 0f) return name + " | " + pest + "! " + CropsText.R("Buy a cure (Crop care)");
        }
        if (FruitKg > 0f) return CropsText.R("[E] Pick") + " " + FruitKg.ToString("F0") + " " + CropsText.R("kg") + " - " + name;
        if (NeedsPollination) return CropsText.R("[E] Pollinate") + " " + name;
        string s = name + " - ";
        if (!IsMature) s += CropsText.R("Young tree") + " " + CropsText.Pct(Age / Mathf.Max(0.01f, d.matureSeasons));
        else s += CropsText.R("Fruits in") + " " + CropsText.R(SeasonKey(d.fruitSeason)) + (d.palm && Pollinated ? " | " + CropsText.R("Pollinated") : "");
        if (Damage > 0.05f) s += " | " + CropsText.R("Damage") + " " + CropsText.Pct(Damage);
        return s;
    }

    public static string SeasonKey(string id) => string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1);

    public override void Interact()
    {
        base.Interact();
        var d = Def;
        var orchard = OrchardManager.Instance;
        if (d == null)
        {
            var next = orchard != null ? orchard.NextSapling() : null;
            if (next == null || !orchard.UseSapling(next.id)) { CropsText.Say(CropsText.R("Empty tree slot - buy saplings in the shop (Orchard)")); return; }
            Plant(next.id);
            CropsText.Say(CropsText.R("Planted") + " " + CropsData.Name(next));
            return;
        }
        if (Pest != "" && TryTreat()) return;
        if (FruitKg > 0f) { Harvest(); return; }
        if (NeedsPollination)
        {
            Pollinated = true; Save(); Refresh();
            GameEvents.RaiseCustom("cropsPollinate", 1f);
            CropsText.Say(CropsText.R("Palm pollinated - full crop this summer"));
            return;
        }
        CropsText.Say(PromptRaw());
    }

    public void Plant(string kind)
    {
        Kind = kind; Age = 0f; LastFruitDay = -100; Pollinated = false; FruitKg = 0f; FruitSeasonsLeft = 0; Pest = ""; Damage = 0f; ProtectDays = 0;
        Save(); Refresh();
        GameEvents.RaiseCustom("cropsPlantTree", 1f);
    }

    public bool TryTreat()
    {
        var care = CropCareManager.Instance;
        var cure = care != null ? care.OwnedCureFor(Pest) : null;
        if (cure == null || !care.UseItem(cure.id)) return false;
        string pest = CropsData.Name(CropsData.Pest(Pest));
        Pest = ""; ProtectDays = Mathf.Max(ProtectDays, cure.protectDays);
        Save(); Refresh();
        GameEvents.RaiseCustom("cropsCurePest", 1f);
        CropsText.Say(CropsData.Name(cure) + ": " + pest + " - " + CropsText.R("treated"));
        return true;
    }

    public float Harvest()
    {
        var d = Def;
        if (d == null || FruitKg <= 0f) return 0f;
        float kg = FruitKg;
        var eco = EconomyManager.Instance;
        if (eco != null) eco.AddGood(d.good, kg);
        FruitKg = 0f; FruitSeasonsLeft = 0;
        Save(); Refresh();
        if (GameManager.Instance != null) GameManager.Instance.RegisterFieldHarvest(kg);
        FeedbackFX.Float(transform.position + Vector3.up * 2.5f, "+" + kg.ToString("F0") + " kg", new Color(0.95f, 0.75f, 0.35f));
        GameEvents.RaiseCustom("cropsTreeHarvest", kg);
        if (d.palm) GameEvents.RaiseCustom("cropsDateHarvest", 1f);
        var good = eco != null ? eco.Good(d.good) : null;
        CropsText.Say(CropsText.R("Harvested") + " " + kg.ToString("F0") + " " + CropsText.R("kg") + " " + EconomyManager.GoodName(good));
        return kg;
    }

    // ---- called by OrchardManager ----

    public void DailyTick(float seasonFraction)
    {
        var d = Def;
        if (d == null) return;
        Age += seasonFraction;
        if (ProtectDays > 0) ProtectDays--;
        var pest = CropsData.Pest(Pest);
        if (pest != null) Damage = Mathf.Min(0.6f, Damage + pest.damagePerDay);
        else if (ProtectDays <= 0 && CropCareManager.Instance != null && WorldClock.Instance != null)
        {
            string season = WorldClock.SeasonId(WorldClock.Instance.Season);
            string family = d.palm ? "palm" : "tree";
            foreach (var p in CropsData.Data.pests)
            {
                if (p.id == "locust" || !CropsData.Has(p.families, family) || !CropsData.Has(p.seasons, season)) continue;
                if (CropCareManager.Instance.Rng.NextDouble() < p.chance) { Infest(p.id); break; }
            }
        }
        TryFruit();
        Save(); Refresh();
    }

    /// <summary>Fruit once a year: in the fruit season from its ripening day, when mature.</summary>
    private void TryFruit()
    {
        var d = Def;
        var clock = WorldClock.Instance;
        if (d == null || clock == null || !IsMature || FruitKg > 0f) return;
        if (WorldClock.SeasonId(clock.Season) != d.fruitSeason || clock.DayOfSeason < d.ripenDay) return;
        if (clock.Day - LastFruitDay < clock.daysPerSeason * 2) return;   // already fruited this year
        float pollination = d.palm ? (Pollinated ? 1f : 0.3f) : 1f;
        FruitKg = Mathf.Round(d.yieldKg * pollination * (1f - Mathf.Clamp01(Damage)) * 10f) / 10f;
        FruitSeasonsLeft = 1;
        LastFruitDay = clock.Day;
        Pollinated = false;
        Damage *= 0.5f;   // the tree recovers between seasons
        if (FruitKg > 0f) CropsText.Say(CropsData.Name(d) + ": " + CropsText.R("fruit is ripe"));
    }

    public bool Infest(string pestId)
    {
        if (Def == null || ProtectDays > 0 || CropsData.Pest(pestId) == null) return false;
        Pest = pestId;
        Save(); Refresh();
        CropsText.Say(CropsData.Name(CropsData.Pest(pestId)) + " - " + CropsData.Name(Def));
        return true;
    }

    public void AddDamage(float amount)
    {
        if (Def == null || ProtectDays > 0) return;
        Damage = Mathf.Min(0.6f, Damage + amount);
        Save();
    }

    public void SeasonTick(Season s)
    {
        var d = Def;
        if (d == null) return;
        // Ripe fruit can be picked for the rest of its season and the next one, then it spoils.
        int today = WorldClock.Instance != null ? WorldClock.Instance.Day : 0;
        if (FruitKg > 0f && LastFruitDay != today)
        {
            FruitSeasonsLeft--;
            if (FruitSeasonsLeft < 0) { FruitKg = 0f; FruitSeasonsLeft = 0; CropsText.Say(CropsData.Name(d) + ": " + CropsText.R("the fruit spoiled on the tree")); }
        }
        Save(); Refresh();
    }

    public void Clear()
    {
        Kind = ""; Age = 0f; LastFruitDay = -100; Pollinated = false; FruitKg = 0f; FruitSeasonsLeft = 0; Pest = ""; Damage = 0f; ProtectDays = 0;
        Save(); Refresh();
    }

    private void Save()
    {
        SaveData.SetString(Key + "kind", Kind);
        SaveData.SetFloat(Key + "age", Age);
        SaveData.SetInt(Key + "pollinated", Pollinated ? 1 : 0);
        SaveData.SetFloat(Key + "fruit", FruitKg);
        SaveData.SetInt(Key + "fruitLeft", FruitSeasonsLeft);
        SaveData.SetString(Key + "pest", Pest);
        SaveData.SetFloat(Key + "dmg", Damage);
        SaveData.SetInt(Key + "prot", ProtectDays);
        SaveData.SetInt(Key + "fruitDay", LastFruitDay);
        SaveData.Save();
    }

    // ---- visuals ----

    private void Refresh()
    {
        var d = Def;
        float grow = d == null ? 0f : Mathf.Clamp01(0.25f + 0.75f * Age / Mathf.Max(0.01f, d.matureSeasons));
        int step = Mathf.RoundToInt(grow * 8f);
        string state = Kind + "|" + step + "|" + (FruitKg > 0f) + "|" + (Pest != "") + "|" + Pollinated;
        if (state == shownState) return;
        shownState = state;
        if (visual != null) Destroy(visual.gameObject);
        visual = null;
        if (d == null) return;
        visual = new GameObject("Tree").transform;
        visual.SetParent(transform, false);
        float s = step / 8f;
        float height = (d.palm ? 5.5f : 3f) * s;
        if (string.IsNullOrEmpty(d.model) || Build.TryModelHeight(d.model, visual, height) == null)
        {
            if (d.palm) BuildPalm(visual, height, s);
            else BuildTree(visual, height, s, d);
        }
        if (FruitKg > 0f) BuildFruit(visual, height, s, d);
        if (Pest != "") Build.Part(PrimitiveType.Sphere, visual, new Vector3(0.18f, 0.6f, 0.18f), Vector3.one * 0.18f, Build.Mat(new Color(0.75f, 0.15f, 0.1f), 0.3f), "PestMark");
        foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = 7;
    }

    private static void BuildPalm(Transform root, float h, float s)
    {
        var trunk = Build.Mat(new Color(0.5f, 0.38f, 0.25f), 0.1f);
        var frond = Build.Mat(new Color(0.3f, 0.55f, 0.22f), 0.2f);
        float r = 0.35f * Mathf.Max(0.4f, s);
        Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, h * 0.5f, 0), new Vector3(r, h * 0.5f, r), trunk, "Trunk");
        int n = 9;
        for (int i = 0; i < n; i++)
        {
            float yaw = i * 360f / n;
            var pivot = new GameObject("Frond").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0, h, 0);
            pivot.localRotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(i % 2 == 0 ? 25f : -10f, 0, 0);
            Build.Part(PrimitiveType.Cube, pivot, new Vector3(0, 0, 1.1f * s), new Vector3(0.45f * s, 0.05f, 2.2f * s), frond, "Leaf");
        }
    }

    private static void BuildTree(Transform root, float h, float s, TreeDef d)
    {
        var trunk = Build.Mat(new Color(0.42f, 0.32f, 0.22f), 0.1f);
        var leaf = Build.Mat(d.id == "olive" ? new Color(0.45f, 0.55f, 0.4f) : new Color(0.25f, 0.5f, 0.2f), 0.2f);
        float r = 0.18f * Mathf.Max(0.5f, s);
        Build.Part(PrimitiveType.Cylinder, root, new Vector3(0, h * 0.35f, 0), new Vector3(r, h * 0.35f, r), trunk, "Trunk");
        float c = 2.2f * s;
        Build.Part(PrimitiveType.Sphere, root, new Vector3(0, h * 0.75f, 0), new Vector3(c, c * 0.8f, c), leaf, "Crown");
    }

    private static void BuildFruit(Transform root, float h, float s, TreeDef d)
    {
        var mat = Build.Mat(new Color(d.r, d.g, d.b), 0.4f);
        int n = 6;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Vector3 pos = d.palm ? new Vector3(Mathf.Cos(a) * 0.45f, h - 0.4f, Mathf.Sin(a) * 0.45f)
                                 : new Vector3(Mathf.Cos(a) * 0.9f * s, h * 0.7f + (i % 2) * 0.3f, Mathf.Sin(a) * 0.9f * s);
            Build.Part(PrimitiveType.Sphere, root, pos, d.palm ? new Vector3(0.3f, 0.5f, 0.3f) : Vector3.one * 0.22f, mat, "Fruit");
        }
    }
}
