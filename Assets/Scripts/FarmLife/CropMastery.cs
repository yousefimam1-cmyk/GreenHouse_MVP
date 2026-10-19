using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] class MasteryEntry { public string id; public float kg; public int stars; }
[Serializable] class MasterySave { public List<MasteryEntry> crops = new List<MasteryEntry>(); }

/// <summary>
/// Crop mastery (like FarmVille): every kg harvested of a crop counts towards 1-3 stars (thresholds in farmlife.json
/// "mastery"). Each star gives a badge, XP and small permanent bonuses for that crop: +5% harvest per star and +10%
/// roadside stand pay per star; competitions also favour mastered crops. Field crops, greenhouse beds (by their crop)
/// and orchard trees ("orchard") count. Saved as mazraa.fl.mastery.
/// </summary>
[MazraaSystem(74)]
public class CropMastery : MonoBehaviour
{
    public const string SaveKey = "mazraa.fl.mastery";
    public const int MaxStars = 3;
    public static CropMastery Instance { get; private set; }

    private MasterySave state = new MasterySave();
    private bool granting;

    private void Awake()
    {
        Instance = this;
        state = FarmLife.Load<MasterySave>(SaveKey);
        if (state.crops == null) state.crops = new List<MasteryEntry>();
    }

    private void OnEnable()
    {
        GameEvents.Harvested += OnHarvested;
        GameEvents.Custom += OnCustom;
        GameEvents.NewGameStarted += OnNewGame;
        FieldEvents.Harvested += OnFieldHarvested;
    }

    private void OnDisable()
    {
        GameEvents.Harvested -= OnHarvested;
        GameEvents.Custom -= OnCustom;
        GameEvents.NewGameStarted -= OnNewGame;
        FieldEvents.Harvested -= OnFieldHarvested;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
    private void OnNewGame() { state = new MasterySave(); Save(); }
    private void Save() => FarmLife.Store(SaveKey, state);

    // ---------------- data ----------------

    public static FLMasteryCrop Def(string id) => Array.Find(FarmLife.Data.mastery.crops, c => c.id == id);
    public static bool Tracked(string id) => Def(id) != null;

    public static int[] Thresholds(string id)
    {
        var d = Def(id);
        return d != null && d.stars != null && d.stars.Length >= MaxStars ? d.stars : FarmLife.Data.mastery.stars;
    }

    public static string Name(string id) { var d = Def(id); return d != null ? FarmLife.Pick(d.en, d.ar) : id; }

    private MasteryEntry Entry(string id, bool create)
    {
        var e = state.crops.Find(x => x.id == id);
        if (e == null && create) { e = new MasteryEntry { id = id }; state.crops.Add(e); }
        return e;
    }

    public float Kg(string id) { var e = Entry(id, false); return e != null ? e.kg : 0f; }
    public int Stars(string id) { var e = Entry(id, false); return e != null ? e.stars : 0; }
    public int TotalStars { get { int n = 0; foreach (var e in state.crops) n += e.stars; return n; } }

    /// <summary>Static helpers for other systems (0 when the system is missing).</summary>
    public static int StarsOf(string id) => Instance != null ? Instance.Stars(id) : 0;
    public static float OrderBonus(string good) => StarsOf(good) * FarmLife.Data.mastery.orderBonus;

    /// <summary>"Wheat *** (2,150 kg)" badge line.</summary>
    public string Badge(string id)
    {
        int s = Stars(id);
        var th = Thresholds(id);
        string next = s < MaxStars ? "  " + Loc.Raw("next star at") + " " + th[s].ToString("N0") + " kg" : "  " + Loc.Raw("Mastered!");
        return Name(id) + "  " + FarmLife.Stars(s, MaxStars) + "  (" + Kg(id).ToString("N0") + " kg)" + next;
    }

    // ---------------- counting ----------------

    private void OnHarvested(Cropbed b, float kg)
    {
        if (granting) return;
        string id = b != null && b.Crop != null ? b.Crop.id : null;
        if (id == null) return;
        Count(id, kg);
        Bonus("crops", id, kg);
    }

    private void OnFieldHarvested(FarmField p, string crop, float kg)
    {
        if (granting || string.IsNullOrEmpty(crop)) return;
        Count(crop, kg);
        Bonus(crop, crop, kg);
    }

    private void OnCustom(string type, float amount) { if (type == "cropsTreeHarvest") Count("orchard", amount); }

    /// <summary>Extra harvest for mastered crops (passive bonus, added to the farm store).</summary>
    private void Bonus(string good, string crop, float kg)
    {
        int s = Stars(crop);
        if (s <= 0 || !FarmLife.PassiveBonuses || EconomyManager.Instance == null) return;
        float extra = kg * s * FarmLife.Data.mastery.yieldBonus;
        if (extra <= 0.01f) return;
        granting = true;
        try { EconomyManager.Instance.AddGood(good, extra); }
        finally { granting = false; }
    }

    /// <summary>Add harvested kg for a crop; awards stars as thresholds are passed. Returns the stars now.</summary>
    public int Count(string id, float kg)
    {
        if (!Tracked(id) || kg <= 0f) return Stars(id);
        var e = Entry(id, true);
        e.kg += kg;
        var th = Thresholds(id);
        while (e.stars < MaxStars && e.kg >= th[e.stars])
        {
            e.stars++;
            int xp = FarmLife.Data.mastery.starXp != null && FarmLife.Data.mastery.starXp.Length >= e.stars ? FarmLife.Data.mastery.starXp[e.stars - 1] : 50;
            FarmLife.Card(Loc.Raw("Crop mastery") + ": " + Name(id) + " " + FarmLife.Stars(e.stars, MaxStars),
                Loc.Raw("Permanent bonus") + ": +" + Mathf.RoundToInt(e.stars * FarmLife.Data.mastery.yieldBonus * 100f) + "% " + Loc.Raw("harvest") + ", +"
                + Mathf.RoundToInt(e.stars * FarmLife.Data.mastery.orderBonus * 100f) + "% " + Loc.Raw("roadside stand pay") + "   +" + xp + " " + Loc.Raw("XP"));
            if (FarmLevel.Instance != null) FarmLevel.Instance.AddXp(xp * Mathf.Max(1f, FarmLevel.Instance.Rate("masteryStar")));
            GameEvents.RaiseCustom("masteryStarEarned", 1f);
        }
        Save();
        return e.stars;
    }

    /// <summary>Lines for the journal page: every tracked crop that has been harvested, best first.</summary>
    public List<string> Lines()
    {
        var list = new List<MasteryEntry>(state.crops);
        list.Sort((a, b) => b.kg.CompareTo(a.kg));
        var lines = new List<string>();
        foreach (var e in list) lines.Add(Badge(e.id));
        return lines;
    }
}
