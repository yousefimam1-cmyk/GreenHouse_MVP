using System.Linq;
using UnityEditor;
using UnityEditor.Build;

/// <summary>
/// Co-op over the network is off by default (single player always compiles). Menu Mazraa > Co-op > Enable Netcode
/// adds the MAZRAA_NETCODE define for Windows/Mac/Linux so NetcodeBackend.cs is compiled (needs the
/// com.unity.netcode.gameobjects and com.unity.transport packages from Packages/manifest.json).
/// If it doesn't compile, use Disable to go back to single player.
/// </summary>
public static class MazraaNetcodeToggle
{
    const string Define = "MAZRAA_NETCODE";

    [MenuItem("Mazraa/Co-op/Enable Netcode")]
    public static void Enable() => Set(true);

    [MenuItem("Mazraa/Co-op/Disable Netcode")]
    public static void Disable() => Set(false);

    static void Set(bool on)
    {
        var target = NamedBuildTarget.Standalone;
        var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(d => d.Length > 0 && d != Define).ToList();
        if (on) defines.Add(Define);
        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        UnityEngine.Debug.Log("Mazraa co-op Netcode " + (on ? "enabled" : "disabled") + ". Unity will recompile.");
    }
}
