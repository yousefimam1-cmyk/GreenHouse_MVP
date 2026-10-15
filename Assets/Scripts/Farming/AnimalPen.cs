using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fenced pen with a barn. Animals need feed (bought in the shop), water (fill the trough, the well must run)
/// and a clean pen (clean at the barn door). Good care raises productivity and health; neglect for a whole day
/// makes them sick. Happy pairs breed new animals at the start of a day. Collect products at the crate.
/// </summary>
public class AnimalPen : MonoBehaviour
{
    public static AnimalPen Instance;
    public Vector2 size = new Vector2(18f, 12f);

    private readonly List<FarmAnimal> animals = new List<FarmAnimal>();
    private readonly Dictionary<string, float> timers = new Dictionary<string, float>();
    private readonly Dictionary<string, float> waiting = new Dictionary<string, float>();

    public float WaterLevel { get; private set; }   // 0..1
    public float Cleanliness { get; private set; }  // 0..1
    public float Health { get; private set; }       // 0..1
    private float hungryTime, thirstyTime;

    public bool HasFeed => EconomyManager.Instance != null && EconomyManager.Instance.Feed > 0.01f;
    public IReadOnlyDictionary<string, float> Waiting => waiting;
    public int AnimalCount { get { var e = EconomyManager.Instance; int n = 0; if (e != null) foreach (var a in e.Data.animals) n += e.Owned(a.id); return n; } }

    /// <summary>Production multiplier from care (0.1 .. 1.2).</summary>
    public float Productivity => Mathf.Clamp((WaterLevel > 0.02f ? 1f : 0.25f) * (0.5f + 0.5f * Cleanliness) * (0.3f + 0.9f * Health), 0.1f, 1.2f);

    private void Awake()
    {
        Instance = this;
        WaterLevel = SaveData.GetFloat("mazraa.pen.water", 0.6f);
        Cleanliness = SaveData.GetFloat("mazraa.pen.clean", 1f);
        Health = SaveData.GetFloat("mazraa.pen.health", 1f);
    }

    private void Start()
    {
        SyncAnimals();
        GameEvents.UpgradeBought += OnBought;
        WorldClock.DayStarted += OnNewDay;
        InvokeRepeating(nameof(SaveState), 5f, 5f);
    }

    private void OnDestroy()
    {
        GameEvents.UpgradeBought -= OnBought;
        WorldClock.DayStarted -= OnNewDay;
    }

    private void OnBought(string id) => SyncAnimals();

    public Vector3 RandomPoint()
    {
        Vector3 local = new Vector3(Random.Range(-size.x * 0.42f, size.x * 0.42f), 0, Random.Range(-size.y * 0.42f, size.y * 0.42f));
        return Build.OnGround(transform.TransformPoint(local));
    }

    private void SyncAnimals(bool newborn = false)
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return;
        foreach (var def in eco.Data.animals)
        {
            int have = animals.FindAll(a => a.animalId == def.id).Count;
            for (int i = have; i < eco.Owned(def.id); i++)
            {
                var a = FarmAnimal.Create(def.id, this, RandomPoint());
                if (newborn) a.MakeBaby();
                animals.Add(a);
            }
        }
    }

    private void Update()
    {
        var eco = EconomyManager.Instance;
        if (eco == null || AnimalCount == 0) return;
        float day = WorldClock.Instance != null ? WorldClock.Instance.secondsPerDay : 360f;

        // Needs decay over roughly one in-game day.
        WaterLevel = Mathf.Max(0f, WaterLevel - Time.deltaTime / day * (0.6f + AnimalCount * 0.05f));
        if (WorldClock.Instance != null && WorldClock.Instance.Weather == Weather.Rain) WaterLevel = Mathf.Min(1f, WaterLevel + Time.deltaTime / day);
        Cleanliness = Mathf.Max(0f, Cleanliness - Time.deltaTime / day * (0.3f + AnimalCount * 0.04f));
        hungryTime = HasFeed ? 0f : hungryTime + Time.deltaTime;
        thirstyTime = WaterLevel > 0.02f ? 0f : thirstyTime + Time.deltaTime;
        bool neglected = hungryTime > day * 0.5f || thirstyTime > day * 0.5f || Cleanliness < 0.1f;
        Health = Mathf.Clamp01(Health + Time.deltaTime / day * (neglected ? -0.8f : 0.5f));

        foreach (var def in eco.Data.animals)
        {
            int count = eco.Owned(def.id);
            if (count == 0) continue;
            float t = (timers.TryGetValue(def.id, out var v) ? v : 0f) + Time.deltaTime * Productivity;
            if (t >= def.interval)
            {
                t = 0f;
                if (eco.ConsumeFeed(def.feedPerCycle * count))
                    waiting[def.id] = (waiting.TryGetValue(def.id, out var w) ? w : 0f) + def.amount * count;
            }
            timers[def.id] = t;
        }
    }

    // Breeding: a well-cared-for pair has a chance to have young each morning.
    private void OnNewDay(int day)
    {
        var eco = EconomyManager.Instance;
        if (eco == null || Productivity < 0.8f) return;
        foreach (var def in eco.Data.animals)
        {
            int n = eco.Owned(def.id);
            if (n >= 2 && n < def.max && Random.value < 0.35f)
            {
                eco.AddAnimal(def.id);
                SyncAnimals(true);
                if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(Loc.Raw("A new animal was born") + ": " + EconomyManager.AnimalName(def)));
                GameEvents.RaiseAnimalBorn(def.id);
            }
        }
    }

    public string FillWater()
    {
        if (!WellIsRunning()) return Loc.T("Start the well first to get water");
        WaterLevel = 1f; SaveState();
        return Loc.T("Water trough filled");
    }

    public string Clean()
    {
        Cleanliness = 1f; SaveState();
        GameEvents.RaisePenCleaned();
        return Loc.T("Pen cleaned");
    }

    private static bool WellIsRunning()
    {
        var wells = FindObjectsByType<WellController>(FindObjectsSortMode.None);
        if (wells.Length == 0) return true;
        foreach (var w in wells) if (w.IsRunning) return true;
        return false;
    }

    public string StatusLine()
    {
        string health = Health > 0.7f ? Loc.Raw("Healthy") : Health > 0.35f ? Loc.Raw("Weak") : Loc.Raw("Sick");
        return Loc.Display(Loc.Raw("Animals") + " " + AnimalCount + ": " + Loc.Raw("Water") + " " + Mathf.RoundToInt(WaterLevel * 100f) + "% - " +
            Loc.Raw("Clean") + " " + Mathf.RoundToInt(Cleanliness * 100f) + "% - " + health + (HasFeed ? "" : " - " + Loc.Raw("No feed")));
    }

    private void SaveState()
    {
        SaveData.SetFloat("mazraa.pen.water", WaterLevel);
        SaveData.SetFloat("mazraa.pen.clean", Cleanliness);
        SaveData.SetFloat("mazraa.pen.health", Health);
        SaveData.Save();
    }

    public void ResetPen()
    {
        foreach (var a in animals) if (a != null) Destroy(a.gameObject);
        animals.Clear(); timers.Clear(); waiting.Clear();
        WaterLevel = 0.6f; Cleanliness = 1f; Health = 1f;
        SaveState();
    }

    /// <summary>Move all waiting products into farm storage. Returns a localized summary.</summary>
    public string Collect()
    {
        var eco = EconomyManager.Instance;
        if (eco == null) return "";
        var parts = new List<string>();
        foreach (var def in eco.Data.animals)
        {
            if (!waiting.TryGetValue(def.id, out var amount) || amount <= 0f) continue;
            eco.AddGood(def.product, amount);
            var g = eco.Good(def.product);
            parts.Add(amount.ToString("F0") + " " + EconomyManager.Unit(g) + " " + EconomyManager.GoodName(g));
            waiting[def.id] = 0f;
        }
        if (parts.Count == 0) return Loc.T(HasFeed ? "Nothing to collect yet" : "No feed - buy feed at the shop");
        GameEvents.RaiseProductsCollected();
        return Loc.Display(Loc.Raw("Collected") + ": " + string.Join(", ", parts));
    }
}
