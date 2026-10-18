using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Farming-Simulator-style three-point hitch for the tractor (gameplay feel only, Mazraa's own models and texts):
/// - implements not on the tractor stand in the implement yard next to the tractor's parking spot (ParkedImplement);
/// - Q (hold Y / Triangle on a gamepad) attaches the implement whose hitch point is within AttachRange of the tractor's
///   hitch (reverse up to it, nearly stopped) or detaches the attached one where it is;
/// - V (tap Y) lowers / raises the attached implement; only a lowered, unfolded implement works the ground;
/// - hold V folds / unfolds it (folded = narrow for the road); lowering a folded implement unfolds it first, and a raised
///   implement folds itself above AutoFoldSpeed (the "auto fold" assist, so gamepad players never need a fold button).
/// Hooks for the farming core (FarmCore): ImplementLowered, ImplementFolded, WorkingWidth, HitchPoint, the ImplementChanged
/// event (attach / detach / lower / raise / fold / sell) and ImplementWorking (every frame the implement is in the ground).
/// Saved: which implement is attached ("mazraa.implement"), lowered / folded state, each parked implement's pose and
/// sold implements (ImplementYard keys). A new game re-parks everything in the yard with the plow attached and lowered.
/// </summary>
public partial class Tractor
{
    public const float AttachRange = 3.2f;          // metres between the tractor's hitch and the implement's hitch point
    public const float AttachMaxSpeed = 1.5f;       // m/s: nearly stopped
    public const float PadHoldSeconds = 0.5f;       // hold Y: attach / detach
    public const float KeyHoldSeconds = 0.6f;       // hold V: fold / unfold
    public const float AutoFoldSpeed = 15f / 3.6f;  // a raised implement folds itself above this speed
    public const string LoweredKey = "mazraa.parity.lowered", FoldedKey = "mazraa.parity.folded";
    public const float RaisedLift = 0.35f, FoldedWidth = 0.45f;

    /// <summary>Raised on attach, detach, lower, raise, fold and unfold (FarmCore, HUD).</summary>
    public static event Action<Tractor> ImplementChanged;
    /// <summary>Every frame a lowered, unfolded implement is moving over a field plot (FarmCore paints the ground from this).</summary>
    public static event Action<Tractor> ImplementWorking;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetHitchStatics() { ImplementChanged = null; ImplementWorking = null; }

    public bool ImplementLowered { get; private set; } = true;
    public bool ImplementFolded { get; private set; }
    /// <summary>Lowered and unfolded: the attached implement can work the ground.</summary>
    public bool InWorkPose => ImplementLowered && !ImplementFolded;
    /// <summary>Working width of the attached implement (m); 0 when nothing is attached or it is folded.</summary>
    public float WorkingWidth => ImplementFolded ? 0f : ImplementWidth(ActiveImplement);
    public static float ImplementWidth(Implement i) => i == Implement.Plow ? 2.4f : i == Implement.Seeder ? 3f : i == Implement.Harvester ? 3f : 0f;
    /// <summary>World position of the hitch (where an implement's hitch point snaps to).</summary>
    public Vector3 HitchPoint => hitch != null ? hitch.position : transform.position - transform.forward * 2.5f;

    private float padHold = -1f, keyHold = -1f;
    private bool padHoldFired, keyHoldFired, hitchLoaded;

    private void LoadHitchState()
    {
        if (hitchLoaded) return;
        hitchLoaded = true;
        ImplementLowered = SaveData.GetInt(LoweredKey, 1) == 1;
        ImplementFolded = SaveData.GetInt(FoldedKey, 0) == 1;
    }

    private void SaveHitchState()
    {
        SaveData.SetInt("mazraa.implement", (int)ActiveImplement);
        SaveData.SetInt(LoweredKey, ImplementLowered ? 1 : 0);
        SaveData.SetInt(FoldedKey, ImplementFolded ? 1 : 0);
    }

    private void RaiseWorking() { ImplementWorking?.Invoke(this); }
    private void Changed() { workCheckTime = -1f; SaveHitchState(); ApplyToolPose(); ImplementChanged?.Invoke(this); }

    // ---------------------------------------------------------------- input

    private void HitchInput()
    {
        LoadHitchState();
        var pad = Gamepad.current;
        bool padY = pad != null && pad.buttonNorth.wasPressedThisFrame;
        if (GameInput.Pressed(GameAction.Implement) && !padY) ToggleAttach();   // Q (keyboard)
        // Gamepad Y: tap lowers / raises, hold attaches / detaches.
        if (padY) { padHold = 0f; padHoldFired = false; }
        if (padHold >= 0f)
        {
            if (pad != null && pad.buttonNorth.isPressed)
            {
                padHold += Time.unscaledDeltaTime;
                if (!padHoldFired && padHold >= PadHoldSeconds) { padHoldFired = true; ToggleAttach(); }
            }
            else { if (!padHoldFired) ToggleLower(); padHold = -1f; }
        }
        // V: tap lowers / raises, hold folds / unfolds.
        if (GameInput.Pressed(GameAction.ImplementLower)) { keyHold = 0f; keyHoldFired = false; }
        if (keyHold >= 0f)
        {
            if (GameInput.Held(GameAction.ImplementLower))
            {
                keyHold += Time.unscaledDeltaTime;
                if (!keyHoldFired && keyHold >= KeyHoldSeconds) { keyHoldFired = true; ToggleFold(); }
            }
            else { if (!keyHoldFired) ToggleLower(); keyHold = -1f; }
        }
        AutoFold();
    }

    /// <summary>Raised implement + road speed: fold it (assist).</summary>
    private void AutoFold()
    {
        if (ActiveImplement == Implement.None || ImplementLowered || ImplementFolded || Mathf.Abs(Speed) < AutoFoldSpeed) return;
        ImplementFolded = true;
        Changed();
    }

    // ---------------------------------------------------------------- actions

    /// <summary>Q / hold Y: detach the attached implement, or attach the parked one in reach.</summary>
    public bool ToggleAttach() => ActiveImplement != Implement.None ? Detach() : TryAttachNearest();

    public bool TryAttachNearest()
    {
        var p = ParkedImplement.Nearest(HitchPoint, AttachRange);
        if (p == null) { Alert(Glyphs.Apply(Loc.T(ParkedImplement.All.Count == 0 ? "No implements in the yard" : "No implement in reach - reverse up to one and press [Q]"))); return false; }
        if (Mathf.Abs(Speed) > AttachMaxSpeed) { Alert(Loc.T("Stop to attach or detach an implement")); return false; }
        Attach(p.kind);
        Alert(Glyphs.Apply(Loc.Display(Loc.Raw("Attached") + ": " + ImplementName(p.kind) + " - " + Loc.Raw("press [V] to lower it"))));
        return true;
    }

    /// <summary>Hitch an implement straight away (no range check). It comes up raised.</summary>
    public void Attach(Implement kind)
    {
        LoadHitchState();
        if (kind == Implement.None || ImplementYard.IsSold(kind)) return;
        if (ActiveImplement != Implement.None && ActiveImplement != kind) ParkAt(ActiveImplement, HitchPoint, yaw);
        ActiveImplement = kind;
        ImplementLowered = false; ImplementFolded = false;
        var go = Visual(kind);
        ImplementYard.Attached(kind, go, hitch);
        Changed();
    }

    public bool Detach()
    {
        LoadHitchState();
        if (ActiveImplement == Implement.None) return false;
        if (Mathf.Abs(Speed) > AttachMaxSpeed) { Alert(Loc.T("Stop to attach or detach an implement")); return false; }
        var kind = ActiveImplement;
        ParkAt(kind, HitchPoint, yaw);
        ActiveImplement = Implement.None;
        ImplementLowered = true; ImplementFolded = false;
        Changed();
        Alert(Loc.Display(Loc.Raw("Detached") + ": " + ImplementName(kind)));
        return true;
    }

    private void ParkAt(Implement kind, Vector3 at, float atYaw)
    {
        var go = Visual(kind);
        if (go == null) return;
        go.transform.localScale = Vector3.one;
        ImplementYard.Park(kind, go, Build.OnGround(at, rideHeight), atYaw);
    }

    public void ToggleLower()
    {
        LoadHitchState();
        if (ActiveImplement == Implement.None) { Alert(Glyphs.Apply(Loc.T("No implement attached - reverse up to one and press [Q]"))); return; }
        if (!ImplementLowered && ImplementFolded) ImplementFolded = false;   // unfold before lowering
        ImplementLowered = !ImplementLowered;
        Changed();
        Alert(Loc.Display(ImplementName(ActiveImplement) + ": " + Loc.Raw(ImplementLowered ? "lowered" : "raised")));
    }

    public void ToggleFold()
    {
        LoadHitchState();
        if (ActiveImplement == Implement.None) return;
        if (ImplementLowered && !ImplementFolded) ImplementLowered = false;   // raise before folding
        ImplementFolded = !ImplementFolded;
        Changed();
        Alert(Loc.Display(ImplementName(ActiveImplement) + ": " + Loc.Raw(ImplementFolded ? "folded" : "unfolded")));
    }

    /// <summary>Attached implement: raised lifts it, folded narrows it.</summary>
    private void ApplyToolPose()
    {
        var go = Visual(ActiveImplement);
        if (go == null || ActiveImplement == Implement.None) return;
        go.transform.localPosition = new Vector3(0f, ImplementLowered ? 0f : RaisedLift, 0f);
        go.transform.localScale = new Vector3(ImplementFolded ? FoldedWidth : 1f, 1f, 1f);
    }

    /// <summary>Called when the spawner registers an implement model: hitch it, or stand it in the yard / where it was left.</summary>
    private void PlaceVisual(Implement kind, GameObject go)
    {
        LoadHitchState();
        if (go == null) return;
        if (!ImplementYard.HasOrigin) ImplementYard.SetOrigin(transform.position, transform.rotation, rideHeight);
        if (kind == ActiveImplement && !ImplementYard.IsSold(kind)) { ImplementYard.Attached(kind, go, hitch); ApplyToolPose(); }
        else ImplementYard.Restore(kind, go);
    }

    /// <summary>New game: plow on the tractor (lowered, ready to plow), seeder and harvester back in the yard, none sold.</summary>
    private void ResetImplements()
    {
        hitchLoaded = true;
        ImplementYard.ClearSaved();
        ActiveImplement = Implement.Plow;
        ImplementLowered = true; ImplementFolded = false;
        foreach (var kv in visuals)
        {
            if (kv.Value == null) continue;
            kv.Value.SetActive(true);
            if (kv.Key == ActiveImplement) ImplementYard.Attached(kv.Key, kv.Value, hitch);
            else ImplementYard.Restore(kv.Key, kv.Value);
        }
        Changed();
    }

    /// <summary>Sell a parked implement (Vehicles tab); bought back from the Farm Shop. Returns the money paid.</summary>
    public int SellImplement(Implement kind)
    {
        if (kind == Implement.None || kind == ActiveImplement || ImplementYard.IsSold(kind)) return 0;
        int value = ImplementYard.SellValue(kind);
        ImplementYard.SetSold(kind, true, Visual(kind));
        var eco = EconomyManager.Instance;
        if (eco != null) using (Ledger.As(Ledger.Sales)) eco.AddMoney(value);
        ImplementChanged?.Invoke(this);
        return value;
    }

    /// <summary>Bought back from the shop: stands in the yard again.</summary>
    public void BuyBackImplement(Implement kind)
    {
        if (!ImplementYard.IsSold(kind)) return;
        ImplementYard.SetSold(kind, false, Visual(kind));
        ImplementYard.ClearPose(kind);
        var go = Visual(kind);
        if (go != null) ImplementYard.Restore(kind, go);
        ImplementChanged?.Invoke(this);
    }
}
