using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A crop type with real-world reference growing conditions (shown to the player).</summary>
[Serializable]
public class CropProfile
{
    public string id;
    public string nameEn;
    public string nameAr;
    public string dayTempC;
    public string nightTempC;
    public string humidity;
    public string ec;
    public string ph;
    public string waterPerPlant;
    public string cycleDays;
    [Tooltip("Multiplier on harvested kg (relative productivity of this crop)")]
    public float yieldFactor = 1f;
}

/// <summary>Starting values for one crop bed, keyed "GreenhouseObjectName/CropbedObjectName".</summary>
[Serializable]
public class CropbedProfile
{
    public string key;
    public string crop;
    public float water;
    public float nutrients;
    public float energy;
    public float climate;
    public float health;
    public float yieldProjection;
}

[Serializable]
public class CropDatabaseFile
{
    public int version;
    public string notes;
    public CropProfile[] crops;
    public CropbedProfile[] beds;
}

/// <summary>
/// Loads Resources/Data/crop_profiles.json. Edit that file to change crops and bed values
/// without touching the scene (e.g. to plug in real Mazraa data).
/// </summary>
public static class CropDatabase
{
    private const string ResourcePath = "Data/crop_profiles";
    private static Dictionary<string, CropProfile> crops;
    private static Dictionary<string, CropbedProfile> beds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { crops = null; beds = null; }

    private static void EnsureLoaded()
    {
        if (crops != null) return;
        crops = new Dictionary<string, CropProfile>(StringComparer.OrdinalIgnoreCase);
        beds = new Dictionary<string, CropbedProfile>(StringComparer.OrdinalIgnoreCase);

        TextAsset json = Resources.Load<TextAsset>(ResourcePath);
        if (json == null)
        {
            Debug.LogWarning("[CropDatabase] Resources/" + ResourcePath + ".json not found - using scene values.");
            return;
        }
        try
        {
            var file = JsonUtility.FromJson<CropDatabaseFile>(json.text);
            if (file?.crops != null) foreach (var c in file.crops) if (!string.IsNullOrEmpty(c.id)) crops[c.id] = c;
            if (file?.beds != null) foreach (var b in file.beds) if (!string.IsNullOrEmpty(b.key)) beds[b.key] = b;
        }
        catch (Exception e)
        {
            Debug.LogError("[CropDatabase] Invalid crop_profiles.json: " + e.Message);
        }
    }

    public static CropProfile GetCrop(string id)
    {
        EnsureLoaded();
        return id != null && crops.TryGetValue(id, out var c) ? c : null;
    }

    public static CropbedProfile GetBed(string key)
    {
        EnsureLoaded();
        return key != null && beds.TryGetValue(key, out var b) ? b : null;
    }
}
