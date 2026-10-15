using UnityEngine;

/// <summary>
/// Volume settings for a settings menu: Master (AudioListener.volume, so it also covers AudioManager's music and
/// effects), Ambience (wind, crickets, birds, animals) and Effects (engine, footsteps). Each 0..1, saved.
/// </summary>
public static class MazraaAudio
{
    private const string MasterKey = "mazraa.vol.master", AmbienceKey = "mazraa.vol.ambience", EffectsKey = "mazraa.vol.effects";

    public static float Master { get { return SaveData.GetFloat(MasterKey, 1f); } }
    public static float Ambience { get { return SaveData.GetFloat(AmbienceKey, 1f); } }
    public static float Effects { get { return SaveData.GetFloat(EffectsKey, 1f); } }

    public static event System.Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Changed = null;

    public static void SetMaster(float v) { SaveData.SetFloat(MasterKey, Mathf.Clamp01(v)); Apply(); }
    public static void SetAmbience(float v) { SaveData.SetFloat(AmbienceKey, Mathf.Clamp01(v)); Apply(); }
    public static void SetEffects(float v) { SaveData.SetFloat(EffectsKey, Mathf.Clamp01(v)); Apply(); }

    /// <summary>Push the master volume to the listener (called at startup and on every change).</summary>
    public static void Apply()
    {
        AudioListener.volume = Master;
        SaveData.Save();
        Changed?.Invoke();
    }
}
