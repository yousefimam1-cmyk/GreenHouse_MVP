using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Farming-Simulator-style vehicle switching: Tab jumps into the next vehicle (on foot: the nearest one), leaving the
/// current one parked where it is. Gamepad players switch from the farm menu's Vehicles tab (Enter vehicle).
/// Also keeps per-vehicle statistics for the Vehicles / Statistics tabs (distance driven, operating time; saved as
/// "mazraa.parity.dist.&lt;id&gt;" / "mazraa.parity.time.&lt;id&gt;", wiped by a new game) and shows each vehicle's dealer
/// configuration (VehicleConfig) once it exists.
/// </summary>
[MazraaSystem(52)]
public class VehicleSwitcher : MonoBehaviour
{
    public const string DistKey = "mazraa.parity.dist.", TimeKey = "mazraa.parity.time.";
    public static VehicleSwitcher Instance { get; private set; }

    private readonly Dictionary<string, float> dist = new Dictionary<string, float>(), time = new Dictionary<string, float>();
    private float saveTimer, configTimer;
    public int Switches { get; private set; }

    private void Awake() => Instance = this;
    private void OnEnable() => GameEvents.NewGameStarted += OnNewGame;
    private void OnDisable() => GameEvents.NewGameStarted -= OnNewGame;
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void OnNewGame()
    {
        dist.Clear(); time.Clear();
        foreach (var v in All()) { SaveData.DeleteKey(DistKey + v.Id); SaveData.DeleteKey(TimeKey + v.Id); VehicleConfig.Apply(v); }
    }

    /// <summary>Drivable vehicles (not driven by a co-op partner), in a fixed order.</summary>
    public static List<Vehicle> All()
    {
        var list = new List<Vehicle>();
        foreach (var v in FindObjectsByType<Vehicle>(FindObjectsSortMode.None)) if (v != null && v.isActiveAndEnabled) list.Add(v);
        list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return list;
    }

    private void Update()
    {
        configTimer -= Time.unscaledDeltaTime;
        if (configTimer <= 0f)
        {
            configTimer = 1f;
            foreach (var v in All()) if (!VehicleConfig.IsApplied(v)) VehicleConfig.Apply(v);
        }
        var cur = Vehicle.Current;
        if (cur != null && Time.deltaTime > 0f)
        {
            Add(dist, DistKey, cur.Id, Mathf.Abs(cur.Speed) * Time.deltaTime);
            if (cur.Drive.EngineOn) Add(time, TimeKey, cur.Id, Time.deltaTime);
        }
        saveTimer -= Time.unscaledDeltaTime;
        if (saveTimer <= 0f) { saveTimer = 5f; Flush(); }

        if (Time.timeScale == 0f || NetSession.Typing || GameMenu.IsOpen) return;
        if ((UIManager.Instance != null && UIManager.Instance.IsUIOpened) || UIManager.PanelClosedFrame == Time.frameCount) return;
        if (GameInput.Pressed(GameAction.SwitchVehicle)) Next();
    }

    private void Add(Dictionary<string, float> d, string key, string id, float v)
    {
        if (!d.ContainsKey(id)) d[id] = SaveData.GetFloat(key + id, 0f);
        d[id] += v;
    }

    private void Flush()
    {
        foreach (var kv in dist) SaveData.SetFloat(DistKey + kv.Key, kv.Value);
        foreach (var kv in time) SaveData.SetFloat(TimeKey + kv.Key, kv.Value);
    }

    public float Distance(string id) => dist.TryGetValue(id, out var d) ? d : SaveData.GetFloat(DistKey + id, 0f);
    public float OperatingSeconds(string id) => time.TryGetValue(id, out var t) ? t : SaveData.GetFloat(TimeKey + id, 0f);
    public float TotalDistance { get { float s = 0f; foreach (var v in All()) s += Distance(v.Id); return s; } }

    /// <summary>Tab: the next vehicle in the list (on foot: the nearest). Returns the vehicle now driven, or null.</summary>
    public Vehicle Next()
    {
        var list = All();
        list.RemoveAll(v => v.RemoteDriven);
        if (list.Count == 0) return null;
        var cur = Vehicle.Current;
        Vehicle target;
        if (cur == null)
        {
            var anchor = Minimap.Anchor();
            Vector3 from = anchor != null ? anchor.position : Vector3.zero;
            target = list[0];
            float best = float.MaxValue;
            foreach (var v in list) { float d = (v.transform.position - from).sqrMagnitude; if (d < best) { best = d; target = v; } }
        }
        else
        {
            int i = list.IndexOf(cur);
            target = list[(i + 1) % list.Count];
            if (target == cur) return cur;   // only one vehicle
        }
        return SwitchTo(target) ? target : null;
    }

    /// <summary>Leave the current vehicle (it stays parked where it is) and get into another one.</summary>
    public bool SwitchTo(Vehicle target)
    {
        if (target == null || target.RemoteDriven) return false;
        var cur = Vehicle.Current;
        if (cur == target) return true;
        if (cur != null) cur.Exit();
        target.Interact();
        if (Vehicle.Current != target) return false;
        Switches++;
        if (UIManager.Instance != null) UIManager.Instance.ShowAlertMessage(Loc.Display(Loc.Raw("Now driving") + ": " + VehicleName(target)));
        return true;
    }

    /// <summary>Display name of a vehicle (translated, unshaped).</summary>
    public static string VehicleName(Vehicle v) => v == null ? "" : Loc.Raw(v is Tractor ? "Tractor" : v is FarmTruck ? "Farm truck" : v.Id);
}
