using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Game actions that can be pressed on keyboard or gamepad (keyboard keys can be rebound in Settings).</summary>
public enum GameAction { Interact, VehicleAction, Implement, Brake, Pause, Map, Inventory, Stats, View,
    EngineToggle, GearUp, GearDown, Cruise, CruiseFaster, CruiseSlower, Traction, LaneAssist }

/// <summary>
/// One Input System action map built in code for the farm actions (E, R, Q, Space, P, N, I, O, V) and the driving actions
/// (F engine, Shift / Ctrl gears, Y cruise, . / , cruise speed, Z 4WD / diff lock, / GPS line) with gamepad bindings.
/// Keyboard bindings can be rebound (Input System rebinding API); overrides are saved with SaveBindingOverridesAsJson
/// in PlayerPrefs ("mazraa.settings.bindings"), so they survive a new game.
/// Also tracks whether the player last used a gamepad (and whether it is a PlayStation pad) for button glyphs.
/// </summary>
public static class GameInput
{
    public const string BindingsKey = "mazraa.settings.bindings";

    private struct Def
    {
        public string label, key, pad;
        public Def(string label, string key, string pad) { this.label = label; this.key = key; this.pad = pad; }
    }

    // Order matches GameAction. label = English Loc key shown in the Controls menu.
    private static readonly Def[] Defs =
    {
        new Def("Interact", "<Keyboard>/e", "<Gamepad>/buttonSouth"),
        new Def("Vehicle reset / tow", "<Keyboard>/r", "<Gamepad>/buttonWest"),
        new Def("Tractor implement", "<Keyboard>/q", "<Gamepad>/buttonNorth"),
        new Def("Brake", "<Keyboard>/space", null),
        new Def("Pause menu", "<Keyboard>/p", "<Gamepad>/start"),
        new Def("Map", "<Keyboard>/n", "<Gamepad>/select"),
        new Def("Inventory", "<Keyboard>/i", null),
        new Def("Farm stats", "<Keyboard>/o", null),
        new Def("Camera view", "<Keyboard>/v", "<Gamepad>/rightStickPress"),
        // driving (only read while in a vehicle, so the pad buttons can be shared with on-foot actions)
        new Def("Engine start / stop", "<Keyboard>/f", "<Gamepad>/leftStickPress"),
        new Def("Gear up", "<Keyboard>/leftShift", "<Gamepad>/rightShoulder"),
        new Def("Gear down", "<Keyboard>/leftCtrl", "<Gamepad>/leftShoulder"),
        new Def("Cruise control / limiter", "<Keyboard>/y", "<Gamepad>/dpad/up"),
        new Def("Cruise speed +", "<Keyboard>/period", "<Gamepad>/dpad/right"),
        new Def("Cruise speed -", "<Keyboard>/comma", "<Gamepad>/dpad/left"),
        new Def("4WD / diff lock", "<Keyboard>/z", "<Gamepad>/dpad/down"),
        new Def("GPS straight line", "<Keyboard>/slash", "<Gamepad>/rightStickPress"),
    };

    private static InputActionAsset asset;
    private static InputActionMap map;
    private static InputAction[] actions;
    private static InputActionRebindingExtensions.RebindingOperation rebindOp;

    /// <summary>Bumped when the device type or a binding changes (prompts re-apply their glyphs).</summary>
    public static int Version { get; private set; }
    public static bool UsingGamepad { get; private set; }
    public static bool IsPlayStation { get; private set; }
    public static bool Rebinding => rebindOp != null;
    public static event Action Changed;

    public static int Count => Defs.Length;
    public static string Label(GameAction a) => Defs[(int)a].label;
    public static string DefaultKeyPath(GameAction a) => Defs[(int)a].key;
    public static string GamepadPath(GameAction a) => Defs[(int)a].pad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (rebindOp != null) { try { rebindOp.Dispose(); } catch { } }
        rebindOp = null; asset = null; map = null; actions = null;
        UsingGamepad = false; IsPlayStation = false; Version = 0; Changed = null;
    }

    public static InputActionAsset Asset { get { Init(); return asset; } }

    private static void Init()
    {
        if (actions != null) return;
        asset = ScriptableObject.CreateInstance<InputActionAsset>();
        asset.name = "Mazraa Actions (runtime)";
        map = asset.AddActionMap("Farm");
        actions = new InputAction[Defs.Length];
        for (int i = 0; i < Defs.Length; i++)
        {
            var act = map.AddAction(((GameAction)i).ToString(), InputActionType.Button);
            act.AddBinding(Defs[i].key, groups: "Keyboard");            // binding 0 = keyboard (rebindable)
            if (!string.IsNullOrEmpty(Defs[i].pad)) act.AddBinding(Defs[i].pad, groups: "Gamepad");
            actions[i] = act;
        }
        string json = PlayerPrefs.GetString(BindingsKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try { asset.LoadBindingOverridesFromJson(json); }
            catch (Exception e) { Debug.LogWarning("[GameInput] Ignoring saved key bindings: " + e.Message); }
        }
        asset.Enable();
    }

    public static InputAction Get(GameAction a) { Init(); return actions[(int)a]; }

    /// <summary>True on the frame the action's key or gamepad button went down (false while rebinding).</summary>
    public static bool Pressed(GameAction a)
    {
        Init();
        var act = actions[(int)a];
        return rebindOp == null && act != null && act.WasPressedThisFrame();
    }

    public static bool Held(GameAction a)
    {
        Init();
        var act = actions[(int)a];
        return rebindOp == null && act != null && act.IsPressed();
    }

    /// <summary>Current keyboard key for an action, e.g. "E" (after rebinding: the new key).</summary>
    public static string KeyName(GameAction a)
    {
        Init();
        var act = actions[(int)a];
        string s = act.GetBindingDisplayString(0);
        if (string.IsNullOrEmpty(s)) s = Tail(act.bindings[0].effectivePath);
        return s;
    }

    public static string KeyPath(GameAction a) { Init(); return actions[(int)a].bindings[0].effectivePath; }

    private static string Tail(string path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        int i = path.LastIndexOf('/');
        string t = i >= 0 ? path.Substring(i + 1) : path;
        return t.Length == 1 ? t.ToUpperInvariant() : char.ToUpperInvariant(t[0]) + t.Substring(1);
    }

    /// <summary>Set the keyboard key of an action (e.g. "&lt;Keyboard&gt;/f"); saved immediately.</summary>
    public static void SetKey(GameAction a, string path)
    {
        Init();
        var act = actions[(int)a];
        if (path == Defs[(int)a].key) act.RemoveBindingOverride(0); else act.ApplyBindingOverride(0, path);
        SaveBindings();
    }

    /// <summary>Wait for the next keyboard key and bind it to the action (Esc cancels). Mouse and gamepad are ignored.</summary>
    public static void StartRebind(GameAction a, Action<bool> done)
    {
        Init();
        CancelRebind();
        var act = actions[(int)a];
        asset.Disable();
        rebindOp = act.PerformInteractiveRebinding(0)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(0.08f)
            .OnComplete(op => FinishRebind(true, done))
            .OnCancel(op => FinishRebind(false, done))
            .Start();
    }

    public static void CancelRebind()
    {
        if (rebindOp == null) return;
        try { rebindOp.Cancel(); } catch { }
        if (rebindOp != null) FinishRebind(false, null);
    }

    private static void FinishRebind(bool ok, Action<bool> done)
    {
        var op = rebindOp;
        rebindOp = null;
        if (op != null) { try { op.Dispose(); } catch { } }
        if (asset != null) asset.Enable();
        if (ok) SaveBindings(); else Bump();
        if (done != null) done(ok);
    }

    public static void ResetBindings()
    {
        Init();
        asset.RemoveAllBindingOverrides();
        SaveBindings();
    }

    public static void SaveBindings()
    {
        Init();
        PlayerPrefs.SetString(BindingsKey, asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
        Bump();
    }

    private static void Bump() { Version++; if (Changed != null) Changed(); }

    // ---------------------------------------------------------------- devices

    /// <summary>Called once per frame (GamepadBridge): switch glyphs to whichever device was used last.</summary>
    public static void TrackDevice()
    {
        var pad = Gamepad.current;
        bool padUsed = pad != null && PadActive(pad);
        bool keysUsed = false;
        var kb = Keyboard.current;
        if (kb != null && kb.anyKey != null && kb.anyKey.wasPressedThisFrame) keysUsed = true;
        var mouse = Mouse.current;
        if (mouse != null)
        {
            var d = mouse.delta.ReadValue();
            if (d.x * d.x + d.y * d.y > 9f || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) keysUsed = true;
        }
        bool ps = pad != null && IsPlayStationPad(pad);
        bool usePad = padUsed ? true : keysUsed ? false : UsingGamepad;
        if (usePad != UsingGamepad || ps != IsPlayStation)
        {
            UsingGamepad = usePad;
            IsPlayStation = ps;
            Bump();
        }
    }

    /// <summary>For tests and the settings menu preview.</summary>
    public static void ForceDevice(bool gamepad, bool playStation)
    {
        if (gamepad == UsingGamepad && playStation == IsPlayStation) return;
        UsingGamepad = gamepad; IsPlayStation = playStation; Bump();
    }

    private static bool PadActive(Gamepad p)
    {
        if (p.buttonSouth.wasPressedThisFrame || p.buttonEast.wasPressedThisFrame || p.buttonWest.wasPressedThisFrame || p.buttonNorth.wasPressedThisFrame) return true;
        if (p.startButton.wasPressedThisFrame || p.selectButton.wasPressedThisFrame || p.leftShoulder.wasPressedThisFrame || p.rightShoulder.wasPressedThisFrame) return true;
        if (p.dpad.up.wasPressedThisFrame || p.dpad.down.wasPressedThisFrame || p.dpad.left.wasPressedThisFrame || p.dpad.right.wasPressedThisFrame) return true;
        if (p.leftTrigger.ReadValue() > 0.3f || p.rightTrigger.ReadValue() > 0.3f) return true;
        var l = p.leftStick.ReadValue(); var r = p.rightStick.ReadValue();
        return l.x * l.x + l.y * l.y > 0.16f || r.x * r.x + r.y * r.y > 0.16f;
    }

    /// <summary>DualShock 4 / DualSense (Sony) by layout or product name; everything else uses Xbox labels.</summary>
    public static bool IsPlayStationPad(InputDevice d)
    {
        if (d == null) return false;
        string s = ((d.name ?? "") + " " + (d.displayName ?? "")).ToLowerInvariant();
        return s.Contains("dualshock") || s.Contains("dualsense") || s.Contains("playstation") || s.Contains("ps4") || s.Contains("ps5") || s.Contains("sony") || s.Contains("wireless controller");
    }
}
