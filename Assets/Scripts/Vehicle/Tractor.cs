using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// FarmCore: Cultivator..Baler added after the original three (saved ints stay the same). What each one does to the ground
// is FarmCore (FarmTools / FieldWork); widths, mass, draft and work speeds come from Data/FarmCore tools.
public enum Implement { None, Plow, Seeder, Harvester, Cultivator, Spreader, Sprayer, Roller, Mulcher, Mower, Baler }

/// <summary>
/// Tractor with a three-point hitch (TractorHitch.cs): implements (plow, seeder, harvester and the FarmCore cultivator,
/// spreader, sprayer, roller, mulcher, mower, baler) are parked in the implement yard; reverse up to one and press Q
/// (hold Y on a gamepad) to attach / detach it, V (tap Y) lowers / raises it, hold V folds it. A lowered, unfolded
/// implement driven over a field paints the FarmCore cells under its working width (plough, sow the chosen seed,
/// spread / spray the chosen product, harvest into the tank...). Slower than the truck.
/// Heavy (4.5 t + implement + optional front weights); a working implement drags (plow most, seeder least) and
/// has a maximum working speed. Front weights (shop, Upgrades) add grip in the field.
/// </summary>
public partial class Tractor : Vehicle
{
    public static Tractor Instance;
    public Implement ActiveImplement { get; private set; }
    public Transform hitch;

    private readonly Dictionary<Implement, GameObject> visuals = new Dictionary<Implement, GameObject>();

    protected override string SaveId => "tractor";
    public override string InteractionPrompt => Loc.T("[E] Drive tractor");

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        maxSpeed = 9f; acceleration = 4.5f; turnRate = 60f; fuelPerSecond = 0.25f;
        emptyMass = 4500f; repairCostScale = 1.4f;
        ActiveImplement = (Implement)Mathf.Clamp(SaveData.GetInt("mazraa.implement", 1), 0, FarmTools.ImplementCount - 1);
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

    public static float ImplementMass(Implement i) => FarmTools.Mass(i);
    /// <summary>Pull needed while the implement works the soil (N).</summary>
    public static float ImplementDraft(Implement i) => FarmTools.Draft(i);
    /// <summary>Highest speed the implement works at (m/s).</summary>
    public static float ImplementMaxSpeed(Implement i) => FarmTools.MaxSpeed(i);

    protected override float LoadMass => ImplementMass(ActiveImplement) + BallastSteps[BallastLevel];
    protected override float BallastMass => BallastSteps[BallastLevel];
    protected override float ReferenceMass => emptyMass + ImplementMass(Implement.Plow);
    protected override float DraftForce => Working ? ImplementDraft(ActiveImplement) : 0f;
    protected override float WorkSpeedLimit => Working ? ImplementMaxSpeed(ActiveImplement) : 0f;

    private float workCheckTime = -1f; private bool working;
    /// <summary>The implement is in the ground: lowered, on a field, moving. Checked a few times a second.</summary>
    public bool Working
    {
        get
        {
            if (ActiveImplement == Implement.None || !InWorkPose || Mathf.Abs(Speed) < 0.5f) return false;
            if (Time.time - workCheckTime < 0.2f && workCheckTime >= 0f) return working;
            workCheckTime = Time.time;
            working = FarmTools.Lowered(this) && FarmTools.OverField(HitchPoint);
            return working;
        }
    }

    protected override void OnNewGame() { base.OnNewGame(); SetBallast(0); ResetImplements(); }

    protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

    public void RegisterVisual(Implement i, GameObject go) { visuals[i] = go; PlaceVisual(i, go); if (i == Implement.Harvester) FarmTools.EnsureVisuals(this); }   // FarmCore: + the other implements' models

    /// <summary>The implement's model (parented to the hitch while attached, standing in the yard while parked).</summary>
    public GameObject Visual(Implement i) => visuals.TryGetValue(i, out var go) ? go : null;

    protected override void DrivingUpdate(Keyboard kb)
    {
        HitchInput();   // Q / hold Y attach-detach, V / tap Y lower-raise, hold V fold (TractorHitch.cs)
        Work();
        if (Working) RaiseWorking();
    }

    // FarmCore: the attached, lowered, unfolded implement paints the field cells under its working width along the hitch's path.
    private void Work() => FarmTools.TractorWork(this, ActiveImplement, HitchPoint, Speed);

    public static string ImplementName(Implement i) => Loc.Raw(i == Implement.None ? "No implement" : i.ToString());
}
