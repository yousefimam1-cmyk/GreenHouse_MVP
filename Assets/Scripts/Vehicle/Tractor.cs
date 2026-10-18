using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Implement { None, Plow, Seeder, Harvester }

/// <summary>
/// Tractor with a three-point hitch (TractorHitch.cs): implements (plow, seeder, harvester) are parked in the implement
/// yard; reverse up to one and press Q (hold Y on a gamepad) to attach / detach it, V (tap Y) lowers / raises it,
/// hold V folds it. A lowered, unfolded implement driven over a field plot (above walking speed) plows, sows the best
/// seed you own, or harvests it. Slower than the truck.
/// Heavy (4.5 t + implement + optional front weights); a working implement drags (plow most, seeder least) and
/// has a maximum working speed. Front weights (shop, Upgrades) add grip in the field.
/// </summary>
public partial class Tractor : Vehicle
{
    public static Tractor Instance;
    public Implement ActiveImplement { get; private set; }
    public Transform hitch;

    private readonly Dictionary<Implement, GameObject> visuals = new Dictionary<Implement, GameObject>();
    private float workCooldown;

    protected override string SaveId => "tractor";
    public override string InteractionPrompt => Loc.T("[E] Drive tractor");

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        maxSpeed = 9f; acceleration = 4.5f; turnRate = 60f; fuelPerSecond = 0.25f;
        emptyMass = 4500f; repairCostScale = 1.4f;
        ActiveImplement = (Implement)SaveData.GetInt("mazraa.implement", 1);
        BallastLevel = Mathf.Clamp(SaveData.GetInt("mazraa.ballast", 0), 0, BallastSteps.Length - 1);
        ShopCatalog.Register(new ShopEntry
        {
            id = "tractor_ballast",
            category = "Upgrades",
            name = () => Loc.Raw("Tractor front weights"),
            detail = () => "(" + (Instance != null ? Instance.BallastLevel : 0) + "/" + (BallastSteps.Length - 1) + ")",
            price = () => Instance == null || Instance.BallastLevel >= BallastSteps.Length - 1 ? -1 : BallastPrices[Instance.BallastLevel],
            onBought = () => { if (Instance != null) Instance.SetBallast(Instance.BallastLevel + 1); },
        });
    }

    // ---------------------------------------------------------------- weight, ballast, implement drag
    public static readonly float[] BallastSteps = { 0f, 600f, 1200f };
    public static readonly int[] BallastPrices = { 900, 1500 };
    public int BallastLevel { get; private set; }

    public void SetBallast(int level)
    {
        BallastLevel = Mathf.Clamp(level, 0, BallastSteps.Length - 1);
        SaveData.SetInt("mazraa.ballast", BallastLevel);
    }

    public static float ImplementMass(Implement i) => i == Implement.Plow ? 900f : i == Implement.Seeder ? 1100f : i == Implement.Harvester ? 1400f : 0f;
    /// <summary>Pull needed while the implement works the soil (N).</summary>
    public static float ImplementDraft(Implement i) => i == Implement.Plow ? 9000f : i == Implement.Seeder ? 3000f : i == Implement.Harvester ? 4000f : 0f;
    /// <summary>Highest speed the implement works at (m/s).</summary>
    public static float ImplementMaxSpeed(Implement i) => (i == Implement.Plow ? 12f : i == Implement.Seeder ? 15f : 10f) / 3.6f;

    protected override float LoadMass => ImplementMass(ActiveImplement) + BallastSteps[BallastLevel];
    protected override float BallastMass => BallastSteps[BallastLevel];
    protected override float ReferenceMass => emptyMass + ImplementMass(Implement.Plow);
    protected override float DraftForce => Working ? ImplementDraft(ActiveImplement) : 0f;
    protected override float WorkSpeedLimit => Working ? ImplementMaxSpeed(ActiveImplement) : 0f;

    private float workCheckTime = -1f; private bool working;
    /// <summary>The implement is in the ground: on a plot, moving. Checked a few times a second.</summary>
    public bool Working
    {
        get
        {
            if (ActiveImplement == Implement.None || !InWorkPose || Mathf.Abs(Speed) < 0.5f) return false;
            if (Time.time - workCheckTime < 0.2f && workCheckTime >= 0f) return working;
            workCheckTime = Time.time;
            working = HitchOnPlot();
            return working;
        }
    }

    private bool HitchOnPlot()
    {
        if (FieldManager.Instance == null) return false;
        Vector3 point = hitch != null ? hitch.position : transform.position - transform.forward * 2.5f;
        foreach (var plot in FieldManager.Instance.Plots) if (plot != null && plot.Contains(point)) return true;
        return false;
    }

    protected override void OnNewGame() { base.OnNewGame(); SetBallast(0); ResetImplements(); }

    protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

    public void RegisterVisual(Implement i, GameObject go) { visuals[i] = go; PlaceVisual(i, go); }

    /// <summary>The implement's model (parented to the hitch while attached, standing in the yard while parked).</summary>
    public GameObject Visual(Implement i) => visuals.TryGetValue(i, out var go) ? go : null;

    protected override void DrivingUpdate(Keyboard kb)
    {
        HitchInput();   // Q / hold Y attach-detach, V / tap Y lower-raise, hold V fold (TractorHitch.cs)
        Work();
        if (Working) RaiseWorking();
    }

    private void Work()
    {
        workCooldown -= Time.deltaTime;
        if (ActiveImplement == Implement.None || !InWorkPose || Mathf.Abs(Speed) < 1f || workCooldown > 0f || FieldManager.Instance == null) return;
        Vector3 point = hitch != null ? hitch.position : transform.position - transform.forward * 2.5f;
        foreach (var plot in FieldManager.Instance.Plots)
        {
            if (plot == null || !plot.Contains(point)) continue;
            bool done = ActiveImplement == Implement.Plow ? plot.TryPlow(false)
                      : ActiveImplement == Implement.Seeder ? plot.TrySeed(false)
                      : plot.TryHarvest(false);
            if (done) { workCooldown = 0.4f; GameEvents.RaiseTractorWorked(ActiveImplement); }
        }
    }

    public static string ImplementName(Implement i) => Loc.Raw(i == Implement.None ? "No implement" : i.ToString());
}
