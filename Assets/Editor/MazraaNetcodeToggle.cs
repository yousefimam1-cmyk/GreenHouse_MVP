using System.Linq;
using UnityEditor;
using UnityEditor.Build;

/// <summary>
/// Co-op switches for Windows / Mac / Linux builds (Project Settings > Player > Scripting Define Symbols, Standalone):
///  - MAZRAA_NETCODE: compiles NetcodeBackend.cs (LAN / direct IP co-op; needs com.unity.netcode.gameobjects and
///    com.unity.transport from Packages/manifest.json). On by default.
///  - MAZRAA_ONLINE: compiles UgsOnlineServices.cs (internet co-op with Unity Relay join codes and the public games
///    list; needs com.unity.services.multiplayer and the project linked in Edit > Project Settings > Services). On by
///    default; it only works together with MAZRAA_NETCODE.
/// If something doesn't compile (e.g. a package couldn't be downloaded), use the Disable entries to go back to a build
/// without online play (Disable Netcode turns both off: single player only).
/// </summary>
public static class MazraaNetcodeToggle
{
    const string Netcode = "MAZRAA_NETCODE";
    const string Online = "MAZRAA_ONLINE";

    [MenuItem("Mazraa/Co-op/Enable Netcode (LAN)")]
    public static void Enable() => Set(Netcode, true);

    [MenuItem("Mazraa/Co-op/Disable Netcode (single player only)")]
    public static void Disable() { Set(Online, false); Set(Netcode, false); }

    [MenuItem("Mazraa/Co-op/Enable Online (Unity Relay + Lobby)")]
    public static void EnableOnline() { Set(Netcode, true); Set(Online, true); }

    [MenuItem("Mazraa/Co-op/Disable Online (keep LAN)")]
    public static void DisableOnline() => Set(Online, false);

    static void Set(string define, bool on)
    {
        var target = NamedBuildTarget.Standalone;
        var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0 && d != define).ToList();
        if (on) defines.Add(define);
        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        UnityEngine.Debug.Log("Mazraa co-op " + define + " " + (on ? "enabled" : "disabled") + ". Unity will recompile.");
    }
}
