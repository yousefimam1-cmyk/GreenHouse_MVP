// QA pass (docs/BUG_AUDIT.md): save robustness (crash mid-write, damaged files, bad values, locale), damaged save slots,
// a vehicle removed while being driven, and the localisation tables dropping destroyed texts.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;

static class QaTests
{
    static void Reset()
    {
        foreach (var f in new[] { SaveData.Path, SaveData.BackupPath, SaveData.CorruptPath, SaveData.Path + ".tmp" }) if (File.Exists(f)) File.Delete(f);
        SaveData.Reload();
    }

    public static void Run()
    {
        Console.WriteLine("QA: save robustness, damaged slots, vehicle removed while driving, localisation tables");
        Program.BuildWorld();
        FakeEngine.Run(2f);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        string keep = File.Exists(SaveData.Path) ? File.ReadAllText(SaveData.Path) : null;

        // ---- crash in the middle of writing: the backup brings the farm back
        Reset();
        SaveData.SetInt("qa.money", 4321); SaveData.SetFloat("qa.fuel", 12.5f); SaveData.SetString("qa.name", "Najd farm");
        SaveData.Flush(true);
        SaveData.SetInt("qa.day", 9); SaveData.Flush(true);   // second write: the first one becomes the backup
        T.Check("Save: each write keeps the previous save as a backup", File.Exists(SaveData.BackupPath) && !File.Exists(SaveData.Path + ".tmp"));
        File.WriteAllText(SaveData.Path, "{\"version\":1,\"entries\":[{\"k\":\"qa.money\",\"v\":\"43");   // truncated by a power cut
        SaveData.Reload();
        T.Check("Save: a truncated save file loads the backup instead of starting fresh",
            SaveData.GetInt("qa.money") == 4321 && Math.Abs(SaveData.GetFloat("qa.fuel") - 12.5f) < 1e-4f && SaveData.GetString("qa.name") == "Najd farm" && SaveData.LoadedFromBackup,
            SaveData.GetInt("qa.money") + "");
        T.Check("Save: the damaged file is kept aside for support", File.Exists(SaveData.CorruptPath));
        SaveData.Flush(true);
        SaveData.Reload();
        T.Check("Save: after recovering, the next write is a good save again", SaveData.GetInt("qa.money") == 4321 && !SaveData.LoadedFromBackup);

        // ---- crash between writing the temp file and swapping it in
        Reset();
        SaveData.SetInt("qa.money", 777); SaveData.Flush(true);
        File.Move(SaveData.Path, SaveData.Path + ".tmp");
        SaveData.Reload();
        T.Check("Save: a finished temp write is used when the save file itself is missing", SaveData.GetInt("qa.money") == 777);

        // ---- empty file, garbage, no backup: start fresh without errors
        Reset();
        File.WriteAllText(SaveData.Path, "");
        SaveData.Reload();
        int errs = FakeEngine.Errors.Count;
        T.Check("Save: an empty save file starts a fresh farm (defaults)", SaveData.GetInt("qa.money", 5) == 5 && FakeEngine.Errors.Count == errs);

        // ---- a value of the wrong type reads as the default, never as a stale PlayerPrefs value
        Reset();
        PlayerPrefs.SetInt("qa.legacy", 99);
        SaveData.SetString("qa.legacy", "lots"); SaveData.SetString("qa.f", "NaN"); SaveData.SetString("qa.i", "3.6");
        T.Check("Save: an unreadable number reads as the default", SaveData.GetInt("qa.legacy", 1) == 1 && SaveData.GetFloat("qa.f", 2f) == 2f, SaveData.GetInt("qa.legacy", 1) + "");
        T.Check("Save: an int key holding a decimal is rounded (type changed between versions)", SaveData.GetInt("qa.i") == 4);
        PlayerPrefs.DeleteKey("qa.legacy");

        // ---- the player's language / region settings don't change how numbers are saved
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Reset();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            SaveData.SetInt("qa.debt", -1500); SaveData.SetFloat("qa.ph", 6.75f); SaveData.Flush(true);
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
            SaveData.Reload();
            T.Check("Save: numbers round-trip whatever the PC's locale (German write, Arabic read)", SaveData.GetInt("qa.debt") == -1500 && Math.Abs(SaveData.GetFloat("qa.ph") - 6.75f) < 1e-4f,
                SaveData.GetInt("qa.debt") + " " + SaveData.GetFloat("qa.ph"));
            T.Check("Save: the file stores invariant numbers", File.ReadAllText(SaveData.Path).Contains("\"-1500\"") && File.ReadAllText(SaveData.Path).Contains("6.75"));
        }
        finally { Thread.CurrentThread.CurrentCulture = culture; }

        // ---- a damaged save slot is not copied over the running farm
        Reset();
        SaveData.SetInt("qa.money", 2468); SaveData.Flush(true);
        File.WriteAllText(SaveSlots.SlotPath(3), "{\"version\":1,\"entries\":[{\"k\":");
        bool copied = SaveSlots.CopySlotToSave(3);
        T.Check("Slots: a damaged slot file is refused and the current save is untouched", !copied && SaveData.GetInt("qa.money") == 2468 && SaveData.IsReadable(SaveData.Path));
        int loads = UnityEngine.SceneManagement.SceneManager.LoadCount;
        T.Check("Slots: loading a damaged slot does not reload the scene", !SaveSlots.Load(3) && UnityEngine.SceneManagement.SceneManager.LoadCount == loads);
        File.Delete(SaveSlots.SlotPath(3));

        // restore the world's save for anything that runs after
        Reset();
        if (keep != null) { File.WriteAllText(SaveData.Path, keep); SaveData.Reload(); }

        // ---- a vehicle removed while the player drives it gives the player back
        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
        if (intro != null) typeof(StoryIntro).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(intro, null);
        FakeEngine.Tick(0.02f);
        var truck = FarmTruck.Instance;
        var player = UnityEngine.Object.FindAnyObjectByType<StarterAssets.StarterAssetsInputs>();
        var cam = Camera.main;
        truck.Interact(); FakeEngine.Tick(0.02f);
        T.Check("Vehicle: getting in hides the player and the main camera", truck.IsDriving && player != null && !player.gameObject.activeSelf && cam != null && !cam.enabled);
        UnityEngine.Object.Destroy(truck.gameObject);
        typeof(FarmTruck).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(truck, null);   // the fake Destroy doesn't call it
        T.Check("Vehicle: removed while driving -> player, main camera and Vehicle.Current are restored",
            player.gameObject.activeSelf && cam.enabled && Vehicle.Current == null && FarmTruck.Instance == null);
        FakeEngine.Tick(0.02f);

        // ---- floating texts don't pile up in the localisation tables
        for (int i = 0; i < 40; i++)
        {
            var go = new GameObject("Float " + i);
            var t = go.AddComponent<TMPro.TextMeshPro>();
            Loc.ExcludeFromScene(t); Loc.ApplyFont(t);
            UnityEngine.Object.Destroy(go);
        }
        T.Check("Loc: destroyed texts are dropped from the font / exclusion tables", Loc.Prune() >= 80 && Loc.Prune() == 0);
    }
}
