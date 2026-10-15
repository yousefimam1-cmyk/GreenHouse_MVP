using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Window > General > Test Runner > EditMode > Run All.
/// Data/text tests run instantly; PlaysTheMainScene enters Play mode on Blak_Pulse_Main and checks the farm boots.
/// </summary>
public class MazraaEditorTests
{
    [Test]
    public void ArabicShaper_JoinsLettersAndKeepsNumbers()
    {
        string s = ArabicShaper.Shape("الصوبة 1: 3/4");
        Assert.IsTrue(s.Contains("3/4"), s);
        Assert.IsTrue(s.Any(c => c >= 'ﹰ'), "letters should use joined presentation forms");
    }

    [Test]
    public void DataFiles_LoadFromResources()
    {
        foreach (var n in new[] { "crop_profiles", "economy", "missions", "scenarios", "sponsors" })
            Assert.IsNotNull(Resources.Load<TextAsset>("Data/" + n), n);
        Assert.IsNotNull(CropDatabase.GetBed("GreenHouse/CropA"));
        Assert.IsNotNull(CropDatabase.GetCrop("tomato"));
    }

    [Test]
    public void Models_AndFont_Import()
    {
        foreach (var n in new[] { "Truck", "Tractor", "cow", "sheep", "chicken", "dog", "cat", "eagle", "horse", "Characters/character-male-a", "Crops/crop_melon" })
            Assert.IsNotNull(Resources.Load<GameObject>("Models/" + n), n);
        Assert.IsNotNull(Resources.Load<Font>("Fonts/Tajawal-Bold"));
        var cow = Resources.Load<GameObject>("Models/cow");
        Assert.IsNotNull(cow.GetComponentInChildren<Animation>(), "animal models should import with legacy Animation");
    }

    [Test]
    public void Controls_GamepadBindingsAndGlyphs()
    {
        Assert.AreEqual("<Gamepad>/buttonSouth", GameInput.GamepadPath(GameAction.Interact));
        Assert.AreEqual(2, GameInput.Get(GameAction.Interact).bindings.Count, "keyboard + gamepad binding");
        GameInput.ForceDevice(true, true);
        StringAssert.Contains("\u2715", Glyphs.Apply("[E] Use"));
        GameInput.ForceDevice(true, false);
        StringAssert.Contains(">A<", Glyphs.Apply("[E] Use"));
        GameInput.ForceDevice(false, false);
        Assert.AreEqual("[E] Use", Glyphs.Apply("[E] Use"));
        Loc.Raw("x");
        Assert.IsTrue(Loc.HasTranslation("Farm map"), "UI translations (Data/Loc/50_ui.json)");
    public void FarmCrops_StageModels_ImportWithMaterials()
    {
        foreach (var crop in CropVisuals.Crops)
            for (int i = 0; i < 4; i++)
            {
                float g = i / 3f;
                Assert.IsNotNull(CropVisuals.Prefab(crop, g), crop + " stage " + i + " (" + CropVisuals.Get(crop, g) + ")");
                Mesh mesh; Material[] mats;
                Assert.IsTrue(CropVisuals.TryGetMesh(crop, g, out mesh, out mats), crop + " stage " + i + " mesh");
                Assert.IsTrue(mats.Length > 0 && mats.All(m => m != null && m.shader != null), crop + " stage " + i + " materials");
            }
    }

    [Test]
    public void ProceduralSounds_Create()
    {
        foreach (var n in new[] { "engine", "wind", "crickets", "birds", "step0", "cow", "sheep", "chicken", "camel", "goat" })
        {
            var clip = ProceduralAudio.Get(n);
            Assert.IsNotNull(clip, n);
            Assert.Greater(clip.length, 0.1f, n);
        }
    }

    [UnityTest]
    public IEnumerator PlaysTheMainScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Blak_Pulse_Main.unity");
        yield return new EnterPlayMode();
        for (int i = 0; i < 20; i++) yield return null;
        if (GameMenu.Instance != null) GameMenu.Instance.Close();   // the main menu pauses the game at start
        for (int i = 0; i < 240; i++) yield return null;   // ~4 seconds of frames
        Assert.IsNotNull(GameManager.Instance, "GameManager");
        Assert.IsNotNull(FarmTruck.Instance, "truck");
        Assert.IsNotNull(Tractor.Instance, "tractor");
        Assert.AreEqual(6, FieldManager.Instance.Plots.Count, "field plots");
        Assert.AreEqual(2, GameManager.Instance.Greenhouses.Count, "greenhouses");
        Assert.Greater(Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Length, 5, "animated actors");
        Assert.AreEqual(4, GameManager.Instance.Greenhouses.First(g => g.name == "GreenHouse").HealthyCount, "greenhouse 1 healthy");
        yield return new ExitPlayMode();
    }
}
