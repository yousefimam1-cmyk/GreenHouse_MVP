using System;
using System.Collections.Generic;
using UnityEngine;

public enum FishState { Idle, Waiting, Bite }

/// <summary>
/// Farm reservoir fishing. E at the water casts; after a random wait the bobber dips (HUD + alert) and E within the
/// bite window reels the fish in. Species, weights, seasons and time-of-day rules come from Resources/Data/fish.json;
/// rods and bait (shop page "Fishing") raise the catch chance, the shrimp bait + carbon rod reach the coast fish
/// (hamour, najil). Caught fish go to farm storage as market goods (fish_*), so the truck sells them.
/// </summary>
[MazraaSystem(60)]
public class FishingManager : MonoBehaviour
{
    public static FishingManager Instance;
    public const float PondRadius = 7f;

    public FishFile Data { get; private set; }
    public FishState State { get; private set; }
    public float Timer { get; private set; }
    public int RodLevel { get; private set; }
    public int CaughtTotal { get; private set; }
    public FishSpecies LastCatch { get; private set; }
    public float LastKg { get; private set; }
    public Transform Pond { get; private set; }

    private readonly Dictionary<string, int> baits = new Dictionary<string, int>();
    private FishBait castBait;
    private FishSpecies hooked;
    private Transform bobber;
    private float waterY;

    private void Awake()
    {
        Instance = this;
        Data = LoadData();
        Load();
        RegisterShop();
    }

    public static FishFile LoadData()
    {
        FishFile f = null;
        var json = Resources.Load<TextAsset>("Data/fish");
        try { if (json != null) f = JsonUtility.FromJson<FishFile>(json.text); }
        catch (Exception e) { Debug.LogError("[Fishing] Invalid fish.json: " + e.Message); }
        if (f == null) f = new FishFile();
        if (f.species == null || f.species.Length == 0)
            f.species = new[] { new FishSpecies { id = "tilapia", good = "fish_tilapia", nameEn = "Tilapia", nameAr = "بلطي", minKg = 0.3f, maxKg = 1.2f } };
        if (f.rods == null) f.rods = new FishRod[0];
        if (f.baits == null) f.baits = new FishBait[0];
        if (f.tank == null) f.tank = new FishTankDef();
        return f;
    }

    private void Start()
    {
        BuildPond(FishSite.At("FishPondSpot", 22f, -18f), FishSite.Facing());
        if (!HudLines.Status.Contains(HudLine)) HudLines.Status.Add(HudLine);
        GameEvents.NewGameStarted += ResetAll;
    }

    private void OnDestroy()
    {
        GameEvents.NewGameStarted -= ResetAll;
        if (Instance == this) Instance = null;
    }

    // ---------------- gear ----------------

    public FishRod Rod => Data.rods == null ? null : Array.Find(Data.rods, r => r.level == RodLevel);
    public int Bait(string id) => baits.TryGetValue(id, out var n) ? n : 0;

    /// <summary>Best bait the player has (highest bonus), or null.</summary>
    public FishBait BestBait()
    {
        FishBait best = null;
        foreach (var b in Data.baits) if (Bait(b.id) > 0 && (best == null || b.chanceBonus > best.chanceBonus)) best = b;
        return best;
    }

    private void RegisterShop()
    {
        foreach (var rod in Data.rods)
        {
            var r = rod;
            ShopCatalog.Register(new ShopEntry
            {
                id = r.id, category = "Fishing",
                name = () => FishRules.Name(r.nameEn, r.nameAr),
                detail = () => Instance != null && Instance.RodLevel >= r.level ? "(" + FishText.Raw("Owned") + ")" : "",
                price = () => r.price,
                blockReason = () => Instance != null && Instance.RodLevel >= r.level ? "Already owned" : null,
                onBought = () => { if (Instance != null) { Instance.RodLevel = Mathf.Max(Instance.RodLevel, r.level); Instance.Save(); } }
            });
        }
        foreach (var bait in Data.baits)
        {
            var b = bait;
            ShopCatalog.Register(new ShopEntry
            {
                id = b.id, category = "Fishing",
                name = () => FishRules.Name(b.nameEn, b.nameAr),
                detail = () => Instance != null ? "(" + Instance.Bait(b.id) + ")" : "",
                price = () => b.price,
                onBought = () => { if (Instance != null) { Instance.baits[b.id] = Instance.Bait(b.id) + b.count; Instance.Save(); } }
            });
        }
    }

    // ---------------- casting minigame ----------------

    /// <summary>E at the water: cast / reel in. Returns the localized message (also shown as an alert).</summary>
    public string Use()
    {
        string msg;
        if (State == FishState.Idle) msg = Cast();
        else if (State == FishState.Waiting) { msg = FishText.T("Too early - the fish got away"); EndCast(); }
        else msg = Reel();
        if (!string.IsNullOrEmpty(msg) && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg);
        return msg;
    }

    private string Cast()
    {
        castBait = BestBait();
        string tag = castBait != null ? castBait.tag : "";
        if (FishRules.Eligible(Data, FishRules.SeasonNow(), FishRules.Band(FishRules.Hour()), RodLevel, tag).Count == 0)
            return FishText.T("Nothing is biting right now - try another time of day");
        if (castBait != null) { baits[castBait.id] = Bait(castBait.id) - 1; Save(); }
        State = FishState.Waiting;
        Timer = UnityEngine.Random.Range(Data.waitMin, Data.waitMax) * (castBait != null ? Mathf.Max(0.2f, castBait.waitFactor) : 1f);
        hooked = null;
        ShowBobber(true);
        return FishText.T("Line cast - wait for the bobber to dip, then press E");
    }

    private string Reel()
    {
        var fish = hooked;
        float chance = FishRules.CatchChance(Data, Rod, castBait);
        EndCast();
        if (fish == null || UnityEngine.Random.value > chance) return FishText.T("It slipped off the hook");
        float kg = Mathf.Round(UnityEngine.Random.Range(fish.minKg, fish.maxKg) * 10f) / 10f;
        kg = Mathf.Max(0.1f, kg);
        var eco = EconomyManager.Instance;
        if (eco != null) eco.AddGood(fish.good, kg);
        LastCatch = fish; LastKg = kg;
        CaughtTotal++;
        Save();
        GameEvents.RaiseCustom("catchFish", 1f);
        GameEvents.RaiseCustom("catchFishKg", kg);
        if (fish.coast) GameEvents.RaiseCustom("catchCoastFish", 1f);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
        return Loc.Display(FishText.Raw("You caught a fish") + ": " + FishRules.Name(fish) + " " + kg.ToString("F1") + " " + FishText.Raw("kg"));
    }

    private void EndCast()
    {
        State = FishState.Idle; Timer = 0f; hooked = null;
        ShowBobber(false);
    }

    private void Update()
    {
        if (State == FishState.Waiting)
        {
            Timer -= Time.deltaTime;
            if (Timer <= 0f)
            {
                string tag = castBait != null ? castBait.tag : "";
                hooked = FishRules.Pick(FishRules.Eligible(Data, FishRules.SeasonNow(), FishRules.Band(FishRules.Hour()), RodLevel, tag), UnityEngine.Random.value);
                if (hooked == null) { EndCast(); return; }
                State = FishState.Bite;
                Timer = FishRules.Window(Data, Rod);
                if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(FishText.T("BITE! Press E to reel in"));
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX();
            }
        }
        else if (State == FishState.Bite)
        {
            Timer -= Time.deltaTime;
            if (Timer <= 0f)
            {
                EndCast();
                if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(FishText.T("Too slow - the fish got away"));
            }
        }
        if (bobber != null && bobber.gameObject.activeSelf)
        {
            float t = Time.time;
            float y = State == FishState.Bite ? -0.16f + Mathf.Sin(t * 22f) * 0.05f : Mathf.Sin(t * 2.5f) * 0.03f;
            var p = bobber.localPosition; p.y = waterY + y; bobber.localPosition = p;
        }
    }

    private string Prompt()
    {
        if (State == FishState.Bite) return FishText.T("[E] BITE! Reel in now!");
        if (State == FishState.Waiting) return FishText.T("Waiting for a bite... ([E] reel in)");
        var bait = BestBait();
        string gear = bait != null ? FishRules.Name(bait.nameEn, bait.nameAr) + " " + Bait(bait.id) : FishText.Raw("no bait");
        return Loc.Display(FishText.Raw("[E] Cast line") + " (" + gear + ")");
    }

    private static string HudLine()
    {
        var f = Instance;
        if (f == null || f.State == FishState.Idle) return null;
        if (f.State == FishState.Bite) return "<color=#FF6B4A>" + FishText.T("BITE! Press E to reel in") + "</color>";
        return FishText.T("Fishing: waiting for a bite...");
    }

    // ---------------- world ----------------

    private void BuildPond(Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Fish Reservoir");
        root.transform.SetPositionAndRotation(pos, rot);
        Pond = root.transform;
        var sand = Build.Mat(new Color(0.82f, 0.71f, 0.5f), 0.1f);
        var stone = Build.Mat(new Color(0.62f, 0.58f, 0.52f), 0.2f);
        var water = Build.Mat(new Color(0.12f, 0.36f, 0.45f), 0.95f);
        var reed = Build.Mat(new Color(0.35f, 0.55f, 0.22f), 0.1f);
        var wood = Build.Mat(new Color(0.5f, 0.34f, 0.2f), 0.15f);
        float d = PondRadius * 2f;
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.03f, 0), new Vector3(d + 2.4f, 0.03f, d + 2.4f), sand, "Bank");
        Build.Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0.07f, 0), new Vector3(d + 0.6f, 0.05f, d + 0.6f), stone, "Rim");

        // The water is the interactable: look at it from the bank and press E.
        var spot = FishSpot.Create("Reservoir Water", root.transform, Vector3.zero, new Vector3(0, 0.3f, 0), new Vector3(d * 0.9f, 0.6f, d * 0.9f), Prompt, () => Use());
        Build.Part(PrimitiveType.Cylinder, spot.transform, new Vector3(0, 0.11f, 0), new Vector3(d, 0.04f, d), water, "Water");
        FishSite.SetLayer(spot.gameObject, 7);

        for (int i = 0; i < 14; i++)
        {
            float a = i * 0.45f + (i % 3) * 0.2f;
            var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (PondRadius + 0.5f);
            Build.Part(PrimitiveType.Cylinder, root.transform, p + new Vector3(0, 0.55f, 0), new Vector3(0.06f, 0.55f + (i % 4) * 0.1f, 0.06f), reed, "Reed");
        }
        // Small jetty on the farm side.
        for (int i = 0; i < 5; i++)
            Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 0.3f, -PondRadius - 0.6f + i * 0.55f), new Vector3(1.4f, 0.08f, 0.45f), wood, "Jetty plank");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0.9f, 0.7f, -PondRadius - 1.4f), new Vector3(0.12f, 1.4f, 0.12f), wood, "Sign post");
        string label = "Farm reservoir";
        Build.Label(root.transform, new Vector3(0, 3f, 0), label, 3f, Color.white);

        bobber = new GameObject("Bobber").transform;
        bobber.SetParent(root.transform, false);
        waterY = 0.16f;
        bobber.localPosition = new Vector3(0, waterY, 0);
        Build.Part(PrimitiveType.Sphere, bobber, Vector3.zero, Vector3.one * 0.18f, Build.Mat(new Color(0.9f, 0.12f, 0.1f), 0.6f), "Float");
        Build.Part(PrimitiveType.Sphere, bobber, new Vector3(0, 0.07f, 0), Vector3.one * 0.1f, Build.Mat(Color.white, 0.6f), "Float top");
        bobber.gameObject.SetActive(false);
    }

    private void ShowBobber(bool on)
    {
        if (bobber == null) return;
        if (on)
        {
            Vector2 c = UnityEngine.Random.insideUnitCircle * PondRadius * 0.5f;
            bobber.localPosition = new Vector3(c.x, waterY, c.y);
        }
        bobber.gameObject.SetActive(on);
    }

    // ---------------- save ----------------

    private void Load()
    {
        RodLevel = SaveData.GetInt("mazraa.fish.rod", 0);
        CaughtTotal = SaveData.GetInt("mazraa.fish.caught", 0);
        foreach (var b in Data.baits) baits[b.id] = SaveData.GetInt("mazraa.fish.bait." + b.id, 0);
    }

    private void Save()
    {
        SaveData.SetInt("mazraa.fish.rod", RodLevel);
        SaveData.SetInt("mazraa.fish.caught", CaughtTotal);
        foreach (var kv in baits) SaveData.SetInt("mazraa.fish.bait." + kv.Key, kv.Value);
        SaveData.Save();
    }

    public void ResetAll()
    {
        RodLevel = 0; CaughtTotal = 0; LastCatch = null; LastKg = 0f;
        foreach (var b in Data.baits) baits[b.id] = 0;
        EndCast();
        Save();
    }
}
