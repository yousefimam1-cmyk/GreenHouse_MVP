using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aquaculture tanks beside the reservoir. Buy tilapia fingerlings in the shop (page "Fishing"), feed them once per
/// in-game day at the tank (uses animal feed), and they grow over FishTankDef.growDays days. Unfed days kill part of
/// the stock. When grown, E harvests kg of tilapia into farm storage. Numbers in Resources/Data/fish.json (tank).
/// </summary>
[MazraaSystem(61)]
public class AquaFarm : MonoBehaviour
{
    public static AquaFarm Instance;
    public const string FingerlingsId = "fish_fingerlings";
    public const string TankGood = "fish_tilapia";

    public int Fish { get; private set; }
    public float Growth { get; private set; }      // 0..1
    public float Survival { get; private set; } = 1f;
    public bool FedToday { get; private set; }
    public bool Ready => Fish > 0 && Growth >= 0.999f;

    private FishTankDef def;
    private readonly List<Transform> swimmers = new List<Transform>();
    private Transform tankRoot;

    private void Awake()
    {
        Instance = this;
        def = (FishingManager.Instance != null ? FishingManager.Instance.Data : FishingManager.LoadData()).tank ?? new FishTankDef();
        Fish = SaveData.GetInt("mazraa.fish.tank.count", 0);
        Growth = SaveData.GetFloat("mazraa.fish.tank.growth", 0f);
        Survival = SaveData.GetFloat("mazraa.fish.tank.survival", 1f);
        FedToday = SaveData.GetInt("mazraa.fish.tank.fed", 0) == 1;
        ShopCatalog.Register(new ShopEntry
        {
            id = FingerlingsId, category = "Fishing",
            name = () => FishText.Raw("Tilapia fingerlings") + " x" + (Instance != null ? Instance.def.fingerlings : 100),
            price = () => Instance != null ? Instance.def.fingerlingPrice : 400,
            blockReason = () => Instance != null && Instance.Fish > 0 ? "The fish tank is already stocked" : null,
            onBought = () => { if (Instance != null) Instance.Stock(); }
        });
    }

    private void Start()
    {
        BuildTanks(FishSite.At("FishTankSpot", 36f, -16f), FishSite.Facing());
        WorldClock.DayStarted += OnNewDay;
        GameEvents.NewGameStarted += ResetAll;
        if (!HudLines.Status.Contains(HudLine)) HudLines.Status.Add(HudLine);
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnNewDay;
        GameEvents.NewGameStarted -= ResetAll;
        if (Instance == this) Instance = null;
    }

    public void Stock()
    {
        Fish = def.fingerlings; Growth = 0f; Survival = 1f; FedToday = false;
        Save();
    }

    /// <summary>Daily growth: fed tanks grow 1/growDays; unfed tanks lose part of the stock.</summary>
    private void OnNewDay(int day)
    {
        if (Fish <= 0 || Ready) { FedToday = false; Save(); return; }
        if (FedToday) Growth = Mathf.Min(1f, Growth + 1f / Mathf.Max(1f, def.growDays));
        else Survival = Mathf.Max(0.2f, Survival - def.starveLoss);
        FedToday = false;
        Save();
        if (Ready && UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(FishText.T("The tilapia in the fish tank are ready to harvest"));
    }

    public float HarvestKg => Fish * def.kgPerFish * Survival;

    /// <summary>E at the tank.</summary>
    public string Use()
    {
        string msg;
        var eco = EconomyManager.Instance;
        if (Fish <= 0) msg = FishText.T("Empty tank - buy tilapia fingerlings at the shop (Fishing)");
        else if (Ready)
        {
            float kg = Mathf.Round(HarvestKg * 10f) / 10f;
            if (eco != null) eco.AddGood(TankGood, kg);
            Fish = 0; Growth = 0f; Survival = 1f; FedToday = false;
            Save();
            GameEvents.RaiseCustom("harvestTank", 1f);
            GameEvents.RaiseCustom("harvestTankKg", kg);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayHarvest();
            msg = FishText.T("Harvested {0} kg of tilapia", kg.ToString("F0"));
        }
        else if (FedToday) msg = FishText.T("The fish are fed for today");
        else if (eco != null && eco.ConsumeFeed(def.feedPerDay))
        {
            FedToday = true; Save();
            GameEvents.RaiseCustom("feedFish", 1f);
            msg = FishText.T("Fish fed");
        }
        else msg = FishText.T("No feed - buy feed at the shop");
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(msg);
        return msg;
    }

    private string Prompt()
    {
        if (Fish <= 0) return FishText.T("Aquaculture tank (empty)");
        if (Ready) return FishText.T("[E] Harvest tilapia (~{0} kg)", HarvestKg.ToString("F0"));
        if (!FedToday) return FishText.T("[E] Feed the fish ({0}% grown)", Mathf.RoundToInt(Growth * 100f));
        return FishText.T("Fish growing: {0}% - fed today", Mathf.RoundToInt(Growth * 100f));
    }

    private static string HudLine()
    {
        var a = Instance;
        if (a == null || a.Fish <= 0) return null;
        string state = a.Ready ? FishText.Raw("ready to harvest") : a.FedToday ? FishText.Raw("fed today") : FishText.Raw("needs feed");
        return Loc.Display(FishText.Raw("Fish tank") + ": " + Mathf.RoundToInt(a.Growth * 100f) + "% - " + state);
    }

    private void Update()
    {
        // Fish circle in the tanks: visible when stocked, bigger as they grow.
        if (swimmers.Count == 0) return;
        float t = Time.time;
        int show = Fish <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(swimmers.Count * Survival), 1, swimmers.Count);
        float size = 0.12f + 0.28f * Growth;
        for (int i = 0; i < swimmers.Count; i++)
        {
            var s = swimmers[i];
            bool on = i < show;
            if (s.gameObject.activeSelf != on) s.gameObject.SetActive(on);
            if (!on) continue;
            float a = t * (0.6f + (i % 3) * 0.15f) + i * 1.3f;
            float r = 0.5f + (i % 2) * 0.55f;
            float cx = i % 2 == 0 ? -2.6f : 2.6f;
            s.localPosition = new Vector3(cx + Mathf.Cos(a) * r, 0.78f, Mathf.Sin(a) * r);
            s.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
            s.localScale = new Vector3(size * 0.45f, size * 0.4f, size);
        }
    }

    private void BuildTanks(Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("Aquaculture Tanks");
        root.transform.SetPositionAndRotation(pos, rot);
        tankRoot = root.transform;
        var concrete = Build.Mat(new Color(0.7f, 0.69f, 0.66f), 0.2f);
        var water = Build.Mat(new Color(0.16f, 0.45f, 0.42f), 0.95f);
        var fishMat = Build.Mat(new Color(0.55f, 0.58f, 0.6f), 0.7f, 0.3f);
        var blue = Build.Mat(new Color(0.2f, 0.4f, 0.75f), 0.4f);
        var spot = FishSpot.Create("Fish Tank", root.transform, Vector3.zero, new Vector3(0, 0.5f, 0), new Vector3(9f, 1f, 4.2f), Prompt, () => Use());
        foreach (float x in new[] { -2.6f, 2.6f })
        {
            Build.Part(PrimitiveType.Cylinder, spot.transform, new Vector3(x, 0.45f, 0), new Vector3(4f, 0.45f, 4f), concrete, "Tank");
            Build.Part(PrimitiveType.Cylinder, spot.transform, new Vector3(x, 0.86f, 0), new Vector3(3.7f, 0.03f, 3.7f), water, "Water");
        }
        // Feed bin and aerator pipe
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 0.5f, 2.6f), new Vector3(0.9f, 1f, 0.7f), blue, "Feed bin");
        Build.Part(PrimitiveType.Cube, root.transform, new Vector3(0, 1.1f, 0), new Vector3(5.2f, 0.08f, 0.08f), concrete, "Aerator pipe");
        FishSite.SetLayer(spot.gameObject, 7);
        for (int i = 0; i < 8; i++)
        {
            var f = Build.Part(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 0.1f, fishMat, "Tilapia").transform;
            f.gameObject.SetActive(false);
            swimmers.Add(f);
        }
        string label = "Aquaculture tanks";
        Build.Label(root.transform, new Vector3(0, 2.6f, 0), label, 2.5f, Color.white);
    }

    private void Save()
    {
        SaveData.SetInt("mazraa.fish.tank.count", Fish);
        SaveData.SetFloat("mazraa.fish.tank.growth", Growth);
        SaveData.SetFloat("mazraa.fish.tank.survival", Survival);
        SaveData.SetInt("mazraa.fish.tank.fed", FedToday ? 1 : 0);
        SaveData.Save();
    }

    public void ResetAll()
    {
        Fish = 0; Growth = 0f; Survival = 1f; FedToday = false;
        Save();
    }
}
