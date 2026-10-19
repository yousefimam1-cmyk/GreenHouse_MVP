using UnityEngine;

/// <summary>Writes SaveData to disk in the background (every ~2 s when changed, on pause and on quit).</summary>
public class SaveFlusher : MonoBehaviour
{
    private void Update() => SaveData.Tick();
    private void OnApplicationPause(bool paused) { if (paused) SaveData.Flush(); }
    private void OnApplicationQuit() => SaveData.Flush(true);
}
