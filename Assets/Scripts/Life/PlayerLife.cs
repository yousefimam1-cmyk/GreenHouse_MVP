using StarterAssets;
using UnityEngine;

/// <summary>
/// On-foot life for the StarterAssets first-person player:
/// - stamina and thirst (StaminaModel): sprinting drains (faster in summer heat and heatwaves, and with a load),
///   resting (crouch and stand still) and drinking at the well / water trough restore. Exhausted = no sprint.
/// - crouch / rest (Ctrl / L3), smooth acceleration and carried weight are applied by the FirstPersonController
///   through SpeedFactor, SprintAllowed, AccelerationFactor and Crouching.
/// - carrying (F / D-pad up): pick up the nearest hay bale or crate, put it down in front; a bale in the pen feeds
///   the herd. One bale is pressed each morning from 20 kg of stored alfalfa (up to 4 at the barn).
/// - hand tools on a quick wheel (comma / period or LB / RB cycle, Z / RT use): hoe (plow a plot), watering can
///   (water a plot or the trough, refill at the well), shovel (muck out the pen, gives manure), grooming brush
///   (clean and calm an animal), bare hands (drink).
/// - footstep surface (sand, field soil, pen straw) for AmbientAudio.
/// Light: one Update with a few distance checks; HUD text refreshes 5 times a second.
/// </summary>
[MazraaSystem(8)]
public class PlayerLife : MonoBehaviour
{
    public static PlayerLife Instance;

    // Read by the StarterAssets FirstPersonController every frame (defaults when there is no PlayerLife).
    public static float SpeedFactor = 1f;
    public static bool SprintAllowed = true;
    public static bool Crouching;
    public static float AccelerationFactor = 1f;
    public static bool JumpAllowed = true;

    public const float ReachMeters = 2.5f;
    public const int MaxBales = 4;

    public StaminaModel Model { get; private set; } = new StaminaModel();
    public HandTool Tool { get; private set; } = HandTool.Hands;
    public Carriable Carried { get; private set; }
    public float CarryKg => Carried != null ? Carried.weightKg : 0f;
    public float CanWater { get; private set; }
    public bool IsCrouching { get; private set; }
    public string LastMessage { get; private set; } = "";
    public LifeHud Hud { get; private set; }

    private StarterAssetsInputs inputs;
    private Transform player;
    private float findTimer, hudTimer, wheelUntil, saveTimer;
    private bool spawned, lastJump;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SpeedFactor = 1f; SprintAllowed = true; Crouching = false; AccelerationFactor = 1f; JumpAllowed = true;
        Carriable.All.Clear();
    }

    private void Awake()
    {
        Instance = this;
        Carriable.All.Clear();   // a new PlayerLife = a freshly loaded farm; its bales and crates are spawned again
        Model.stamina = SaveData.GetFloat("mazraa.life.stamina", StaminaModel.Max);
        Model.hydration = SaveData.GetFloat("mazraa.life.hydration", 1f);
        Tool = (HandTool)Mathf.Clamp(SaveData.GetInt("mazraa.life.tool", 0), 0, HandTools.All.Length - 1);
        CanWater = SaveData.GetFloat("mazraa.life.can", 1f);
    }

    private void Start()
    {
        Hud = new LifeHud(transform);
        WorldClock.DayStarted += OnDay;
        GameEvents.NewGameStarted += ResetAll;
    }

    private void OnDestroy()
    {
        WorldClock.DayStarted -= OnDay;
        GameEvents.NewGameStarted -= ResetAll;
        if (Instance == this) { Instance = null; ResetStatics(); }
    }

    private bool FindPlayer()
    {
        if (inputs != null) return true;
        findTimer -= Time.unscaledDeltaTime;
        if (findTimer > 0f) return false;
        findTimer = 1f;
        inputs = FindAnyObjectByType<StarterAssetsInputs>();
        player = inputs != null ? inputs.transform : null;
        return inputs != null;
    }

    /// <summary>On foot and free to act (not driving, no panel open, not paused).</summary>
    public bool OnFoot => player != null && player.gameObject.activeInHierarchy && Vehicle.Current == null;

    private void Update()
    {
        float dt = Time.deltaTime;
        if (!spawned && AnimalPen.Instance != null) SpawnProps();
        if (!FindPlayer() || !OnFoot)
        {
            SpeedFactor = 1f; SprintAllowed = true; Crouching = false; AccelerationFactor = 1f; JumpAllowed = true;
            if (Hud != null) Hud.SetVisible(false);
            return;
        }
        bool uiBlocked = (UIManager.Instance != null && UIManager.Instance.IsUIOpened) || Time.timeScale == 0f || NetSession.Typing;
        if (!uiBlocked)
        {
            if (GameInput.Pressed(GameAction.Crouch)) SetCrouch(!IsCrouching);
            if (GameInput.Pressed(GameAction.Carry)) Say(ToggleCarry());
            if (GameInput.Pressed(GameAction.ToolNext)) CycleTool(1);
            if (GameInput.Pressed(GameAction.ToolPrev)) CycleTool(-1);
            if (GameInput.Pressed(GameAction.UseTool)) Say(UseTool());
        }
        Step(dt);
        hudTimer -= Time.unscaledDeltaTime;
        if (hudTimer <= 0f && Hud != null)
        {
            hudTimer = 0.2f;
            Hud.SetVisible(!uiBlocked);
            Hud.Show(Model, Tool, Time.unscaledTime < wheelUntil, CarryKg, CanWater);
        }
        saveTimer -= dt;
        if (saveTimer <= 0f) { saveTimer = 5f; Save(); }
    }

    /// <summary>Heat felt by the player now (season, weather, night).</summary>
    public static float HeatNow()
    {
        var c = WorldClock.Instance;
        if (c == null) return 0f;
        return StaminaModel.Heat(c.Season, c.Weather, c.SunHeight < 0f);
    }

    /// <summary>One frame of stamina / thirst from the controller's inputs, then the movement factors.</summary>
    public void Step(float dt)
    {
        if (inputs == null) FindPlayer();
        bool moving = inputs != null && inputs.move.x * inputs.move.x + inputs.move.y * inputs.move.y > 0.01f;
        bool wantSprint = inputs != null && inputs.sprint;
        bool canSprint = MoveRules.CanSprint(CarryKg, IsCrouching, Model.exhausted);
        bool jump = inputs != null && inputs.jump;
        if (jump && !lastJump && !Model.Spend(6f, HeatNow())) { inputs.jump = false; jump = false; }
        lastJump = jump;
        float day = WorldClock.Instance != null ? WorldClock.Instance.secondsPerDay : 360f;
        Model.Tick(dt, moving, wantSprint && canSprint, IsCrouching, HeatNow(), CarryKg, dt / Mathf.Max(1f, day));
        canSprint = MoveRules.CanSprint(CarryKg, IsCrouching, Model.exhausted);
        if (!canSprint && inputs != null && inputs.sprint) inputs.sprint = false;
        SprintAllowed = canSprint;
        SpeedFactor = MoveRules.SpeedFactor(CarryKg, IsCrouching, Model.exhausted, Model.hydration);
        AccelerationFactor = MoveRules.Acceleration(CarryKg);
        Crouching = IsCrouching;
        JumpAllowed = !IsCrouching && CarryKg < MoveRules.NoSprintKg && !Model.exhausted;
    }

    public void SetCrouch(bool on) { IsCrouching = on; Crouching = on; }

    /// <summary>Ground under a point for footsteps: "soil" on a field plot, "straw" in the pen, else "sand".</summary>
    public static string SurfaceAt(Vector3 p)
    {
        var pen = AnimalPen.Instance;
        if (pen != null && pen.Contains(p)) return "straw";
        if (FieldManager.Instance != null)
            foreach (var plot in FieldManager.Instance.Plots) if (plot != null && plot.Contains(p)) return "soil";
        return "sand";
    }

    // ------------------------------------------------------------------ tools

    public void SetTool(HandTool t) { Tool = t; Save(); }

    public void CycleTool(int d)
    {
        Tool = HandTools.Cycle(Tool, d);
        wheelUntil = Time.unscaledTime + 2.5f;
        hudTimer = 0f;
        Save();
    }

    private Vector3 Pos => player != null ? player.position : Vector3.zero;
    private Vector3 Front
    {
        get
        {
            if (player == null) return Vector3.zero;
            Vector3 f = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            return player.position + f.normalized * 1.5f;
        }
    }

    private FieldPlot PlotHere()
    {
        if (FieldManager.Instance == null) return null;
        foreach (var p in FieldManager.Instance.Plots) if (p != null && (p.Contains(Front) || p.Contains(Pos))) return p;
        return null;
    }

    /// <summary>"well", "trough" or null: a water source within reach that has water.</summary>
    public string WaterSourceNear()
    {
        foreach (var w in FindObjectsByType<WellController>(FindObjectsSortMode.None))
            if (w.IsRunning && Flat(w.transform.position - Pos) < 4f) return "well";
        var pen = AnimalPen.Instance;
        if (pen != null && pen.WaterLevel > 0.05f)
            foreach (var t in pen.GetComponentsInChildren<PenTask>())
                if (t.kind == PenTaskKind.Water && Flat(t.transform.position - Pos) < 3.5f) return "trough";
        return null;
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    /// <summary>Use the current tool on what is in front of the player. Returns the (translated) result.</summary>
    public string UseTool()
    {
        if (!OnFoot && player == null) FindPlayer();
        float heat = HeatNow();
        float cost = HandTools.Cost(Tool);
        if (cost > 0f && Model.stamina < cost * (1f + 0.5f * heat)) return Loc.T("Too tired - rest or drink water");
        var pen = AnimalPen.Instance;
        bool inPen = pen != null && (pen.Contains(Pos) || pen.Contains(Front));
        switch (Tool)
        {
            case HandTool.Hoe:
            {
                var plot = PlotHere();
                if (plot == null || plot.State != PlotState.Untilled) return Loc.T("Nothing to hoe here - stand on an untilled field");
                plot.TryPlow(false);
                Model.Spend(cost, heat);
                return Loc.T("Field plowed by hand");
            }
            case HandTool.WateringCan:
            {
                if (WaterSourceNear() == "well" || (WaterSourceNear() == "trough" && CanWater < 1f))
                {
                    CanWater = 1f; Save();
                    return Loc.T("Watering can filled");
                }
                if (CanWater < 0.24f) return Loc.T("The watering can is empty - fill it at the well");
                var plot = PlotHere();
                if (plot != null && (plot.State == PlotState.Growing || plot.State == PlotState.Plowed))
                {
                    if (FarmModifiers.IrrigationBanned) return FarmModifiers.BanText();
                    plot.AddWater(0.5f);
                    CanWater -= 0.25f; Model.Spend(cost, heat); Save();
                    return Loc.T("Field watered by hand");
                }
                if (inPen)
                {
                    pen.AddWater(0.2f);
                    if (HerdManager.Instance != null) HerdManager.Instance.WaterAll();
                    CanWater -= 0.25f; Model.Spend(cost, heat); Save();
                    return Loc.T("Poured water into the trough");
                }
                return Loc.T("Nothing to water here");
            }
            case HandTool.Shovel:
            {
                if (!inPen) return Loc.T("Nothing to shovel here - muck out the animal pen");
                if (pen.Cleanliness >= 0.99f) return Loc.T("The pen is already clean");
                pen.CleanPartly(0.3f);
                var eco = EconomyManager.Instance;
                if (eco != null && eco.Good("manure") != null) eco.AddGood("manure", 2f);
                Model.Spend(cost, heat);
                return Loc.T("Mucked out part of the pen");
            }
            case HandTool.Brush:
            {
                var body = NearestAnimal();
                if (body == null || HerdManager.Instance == null) return Loc.T("No animal close enough");
                Model.Spend(cost, heat);
                return HerdManager.Instance.Groom(body.uid);
            }
            default:
            {
                if (WaterSourceNear() != null) { Model.Drink(); Save(); return Loc.T("You drink cool water"); }
                return Loc.T("Nothing to do here - pick a tool with [,] [.]");
            }
        }
    }

    private FarmAnimal NearestAnimal()
    {
        var pen = AnimalPen.Instance;
        if (pen == null) return null;
        FarmAnimal best = null; float bd = ReachMeters + 0.5f;
        foreach (var a in pen.Animals)
        {
            if (a == null) continue;
            float d = Flat(a.transform.position - Pos);
            if (d < bd) { bd = d; best = a; }
        }
        return best;
    }

    // ------------------------------------------------------------------ carrying

    /// <summary>Pick up the nearest object, or put down the one in hand.</summary>
    public string ToggleCarry()
    {
        if (player == null && !FindPlayer()) return "";
        if (Carried != null) return PutDown();
        Carriable best = null; float bd = ReachMeters;
        foreach (var c in Carriable.All)
        {
            if (c == null || c.Carried) continue;
            float d = Flat(c.transform.position - Pos);
            if (d < bd) { bd = d; best = c; }
        }
        if (best == null) return Loc.T("Nothing to pick up here");
        if (best.weightKg > MoveRules.MaxCarryKg) return Loc.T("Too heavy to carry");
        best.PickUp(player);
        Carried = best;
        if (IsCrouching) SetCrouch(false);
        Step(0f);
        return Loc.Display(Loc.Raw("Picked up") + ": " + Loc.Raw(best.NameKey) + " (" + best.weightKg.ToString("F0") + " kg)");
    }

    private string PutDown()
    {
        var c = Carried;
        Carried = null;
        Vector3 fwd = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        c.PutDown(player.position + fwd.normalized * 1.1f, Quaternion.LookRotation(fwd.normalized));
        Step(0f);
        var pen = AnimalPen.Instance;
        if (c.kind == "hay_bale" && pen != null && pen.Contains(c.transform.position) && HerdManager.Instance != null && HerdManager.Instance.Animals.Count > 0)
        {
            HerdManager.Instance.FeedFromBale();
            Destroy(c.gameObject);
            Carriable.All.Remove(c);
            SaveBales();
            return Loc.T("Hay bale spread in the pen - the animals are fed");
        }
        return Loc.Display(Loc.Raw("Put down") + ": " + Loc.Raw(c.NameKey));
    }

    // ------------------------------------------------------------------ props, day, save

    private Vector3 BaleSpot(int i)
    {
        var pen = AnimalPen.Instance;
        return pen.transform.TransformPoint(new Vector3(pen.size.x / 2f + 1.4f, 0f, -pen.size.y / 2f + 1.5f + i * 0.7f));
    }

    private void SpawnProps()
    {
        spawned = true;
        var pen = AnimalPen.Instance;
        int bales = Mathf.Clamp(SaveData.GetInt("mazraa.life.bales", 2), 0, MaxBales);
        for (int i = 0; i < bales; i++) Carriable.Spawn("hay_bale", BaleSpot(i), pen.transform.rotation);
        Carriable.Spawn("crate", pen.transform.TransformPoint(new Vector3(4.6f, 0, pen.size.y / 2f + 1.6f)), pen.transform.rotation);
        Carriable.Spawn("crate", pen.transform.TransformPoint(new Vector3(5.4f, 0, pen.size.y / 2f + 1.4f)), pen.transform.rotation);
    }

    public int BaleCount { get { int n = 0; foreach (var c in Carriable.All) if (c != null && c.kind == "hay_bale") n++; return n; } }

    private void OnDay(int day)
    {
        // Press a bale from stored alfalfa hay each morning.
        var eco = EconomyManager.Instance;
        if (spawned && eco != null && BaleCount < MaxBales && eco.Good("alfalfa") != null && eco.TakeGood("alfalfa", 20f))
        {
            Carriable.Spawn("hay_bale", BaleSpot(BaleCount), AnimalPen.Instance.transform.rotation);
            SaveBales();
        }
    }

    private void SaveBales() { SaveData.SetInt("mazraa.life.bales", BaleCount); SaveData.Save(); }

    private void Say(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return;
        LastMessage = msg;
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Glyphs.Apply(msg));
    }

    public void Save()
    {
        SaveData.SetFloat("mazraa.life.stamina", Model.stamina);
        SaveData.SetFloat("mazraa.life.hydration", Model.hydration);
        SaveData.SetInt("mazraa.life.tool", (int)Tool);
        SaveData.SetFloat("mazraa.life.can", CanWater);
        if (spawned) SaveData.SetInt("mazraa.life.bales", BaleCount);
        SaveData.Save();
    }

    public void ResetAll()
    {
        if (Carried != null) { Destroy(Carried.gameObject); Carried = null; }
        foreach (var c in Carriable.All.ToArray()) if (c != null) Destroy(c.gameObject);
        Carriable.All.Clear();
        Model.Reset(); Tool = HandTool.Hands; CanWater = 1f; SetCrouch(false);
        SaveData.SetInt("mazraa.life.bales", 2);
        spawned = false;
        if (AnimalPen.Instance != null) SpawnProps();
        Save();
    }
}
