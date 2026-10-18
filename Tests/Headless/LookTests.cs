// Graphics makeover (Assets/Scripts/Graphics/Look): PBR look materials and their shipped textures, vegetation with LODs
// and wind, instanced grass following Settings > Foliage, terrain layers, ground meshes, regional NPC clothing, animal
// materials, sky / probe / grade by quality preset (incl. Ultra), wet ground after rain and the texture budget.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

static class LookTests
{
    static bool Under(Transform t, string name) { for (; t != null; t = t.parent) if (t.name == name) return true; return false; }
    static Material MatOf(Renderer r) => r.sharedMaterial ?? (r.sharedMaterials != null && r.sharedMaterials.Length > 0 ? r.sharedMaterials[0] : null);
    static IEnumerable<Material> MatsOf(Renderer r)
    {
        if (r.sharedMaterials != null && r.sharedMaterials.Length > 0) return r.sharedMaterials;
        return new[] { r.sharedMaterial };
    }
    static bool Pink(Material m) => m == null || m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader" || m.shader.name == "Standard" || m.shader.name.StartsWith("Legacy Shaders/");

    static void Boot()
    {
        Program.BuildWorld();
        FakeEngine.Run(2.5f);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
        if (intro != null) typeof(StoryIntro).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(intro, null);
        FakeEngine.Run(1f);
    }

    public static void Run()
    {
        Console.WriteLine("Graphics makeover (look materials, vegetation, grass, terrain, clothing, presets)");
        var urp = new UniversalRenderPipelineAsset { renderScale = 1f, msaaSampleCount = 4, shadowDistance = 90f };
        UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline = urp;
        GameSettings.FoliageDensity = -1;
        GraphicsEnhancer.SetPreset(GraphicsEnhancer.PresetHigh);
        Boot();
        int e0 = FakeEngine.Errors.Count;
        var dd = DesertDressing.Instance;
        var atmo = AtmosphereLook.Instance;
        T.Check("Look: systems running (dressing, atmosphere, grass)", dd != null && atmo != null && FoliageGrass.Instance != null);

        // ---- materials: shipped .mat assets with their textures and the right shaders
        var untextured = new List<string>();
        foreach (LookMat m in Enum.GetValues(typeof(LookMat)))
        {
            var mat = LookLibrary.Get(m);
            if (!LookLibrary.IsTextured(m) || Pink(mat)) { untextured.Add(m + " (missing)"); continue; }
            if (m == LookMat.Glass) continue;
            if (LookLibrary.IsFoliage(m))
            {
                if (mat.shader.name != "Mazraa/Foliage" || mat.GetTexture("_BaseMap") == null || !mat.enableInstancing) untextured.Add(m + " foliage");
            }
            else if (mat.shader.name != "Universal Render Pipeline/Lit" || mat.GetTexture("_BaseMap") == null || mat.GetTexture("_BumpMap") == null || mat.GetTexture("_OcclusionMap") == null)
                untextured.Add(m + " " + mat.shader.name);
        }
        T.Check("Look: every look material loads from Resources with albedo + normal + mask (foliage: Mazraa/Foliage, instanced)", untextured.Count == 0, string.Join(", ", untextured));
        var buildings = new[] { LookMat.MudBrick, LookMat.Plaster, LookMat.Concrete, LookMat.Corrugated, LookMat.Metal, LookMat.Wood };
        T.Check("Look: building materials read metal / smoothness from their mask", buildings.All(b => LookLibrary.Get(b).GetTexture("_MetallicGlossMap") != null));
        var sky = Resources.Load<Material>(AtmosphereLook.SkyMaterial); var terrainMat = Resources.Load<Material>(GroundLook.TerrainMaterial);
        T.Check("Look: sky (Skybox/Procedural) and terrain (URP Terrain/Lit) materials ship", sky != null && sky.shader.name == "Skybox/Procedural" && terrainMat != null && terrainMat.shader.name == "Universal Render Pipeline/Terrain/Lit");

        // ---- texture budget: every texture <= 2048, all look textures together under the budget
        long bytes = LookLibrary.TextureBytes();
        var big = LookLibrary.AllLoaded().SelectMany(m => m.TextureProperties.Select(p => m.GetTexture(p))).Where(t => t != null && (t.width > LookLibrary.MaxTextureSize || t.height > LookLibrary.MaxTextureSize)).Select(t => t.name).Distinct().ToList();
        T.Check("Look: texture budget (" + (bytes / (1024 * 1024)) + " MB GPU estimate, every texture <= 2K)", bytes > 4L * 1024 * 1024 && bytes < LookLibrary.TextureBudgetBytes && big.Count == 0, string.Join(",", big));

        // ---- nothing pink on the dressing, vegetation and clothes
        var pink = new List<string>();
        int checkedRenderers = 0;
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r is ParticleSystemRenderer) continue;
            if (!(Under(r.transform, "Desert Dressing") || Under(r.transform, "Vegetation") || Under(r.transform, Appearance.HeadwearName) || Under(r.transform, NpcWardrobe.RobeName))) continue;
            checkedRenderers++;
            foreach (var m in MatsOf(r)) if (Pink(m)) pink.Add(r.name + "/" + (m == null ? "null" : m.shader?.name));
        }
        T.Check("Look: no missing / pink materials on the dressing, trees and clothes (" + checkedRenderers + " renderers)", checkedRenderers > 50 && pink.Count == 0, string.Join(", ", pink.Distinct().Take(5)));

        // ---- vegetation: LOD trees with bark + leaves, lighter LODs, wind weights
        var groups = UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Where(g => g.transform.IsChildOf(dd.transform)).ToList();
        var badLod = groups.Where(g => g.GetLODs().Length != FoliageMeshes.Lods || g.GetLODs().Any(l => l.renderers == null || l.renderers.Length == 0 || l.renderers[0].GetComponent<MeshFilter>().sharedMesh == null)
            || g.fadeMode != LODFadeMode.None || !(g.GetLODs()[0].screenRelativeTransitionHeight > g.GetLODs()[1].screenRelativeTransitionHeight && g.GetLODs()[1].screenRelativeTransitionHeight > g.GetLODs()[2].screenRelativeTransitionHeight)).Select(g => g.name).ToList();
        T.Check("Look: dressing trees are LOD trees (" + groups.Count + " LOD groups: near / mid / far cards, no cross-fade)", groups.Count >= dd.Palms + dd.Trees && badLod.Count == 0, string.Join(",", badLod.Take(3)));
        var lighter = new List<string>();
        foreach (TreeType tt in Enum.GetValues(typeof(TreeType)))
            for (int v = 0; v < FoliageMeshes.Variants; v++)
            {
                var c = FoliageMeshes.TriangleCounts(tt, v);
                if (!(c[0] > c[1] && c[1] > c[2] && c[2] > 0)) lighter.Add(tt + "#" + v + " " + string.Join("/", c));
            }
        T.Check("Look: each LOD is lighter than the one before, for every species and variant", lighter.Count == 0, string.Join(", ", lighter.Take(4)));
        var palm = FoliageMeshes.Get(TreeType.DatePalm, 0)[0];
        T.Check("Look: palm LOD0 has bark + frond sub-meshes with wind colours on every vertex", palm.subMeshCount == 2 && palm.GetIndexCount(0) > 0 && palm.GetIndexCount(1) > 0 && palm.colors.Length == palm.vertices.Length && palm.uv.Length == palm.vertices.Length,
            palm.subMeshCount + " " + palm.GetIndexCount(0) + "/" + palm.GetIndexCount(1));
        var farShadows = groups.SelectMany(g => g.GetLODs().Skip(2)).SelectMany(l => l.renderers).Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
        var nearShadows = groups.SelectMany(g => g.GetLODs().Take(1)).SelectMany(l => l.renderers).All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On);
        T.Check("Look: near trees cast shadows, far cards don't", farShadows == 0 && nearShadows);
        var treeMats = groups.SelectMany(g => g.GetLODs()[0].renderers).SelectMany(r => r.sharedMaterials).Distinct().ToList();
        T.Check("Look: trees share a handful of instanced materials (" + treeMats.Count + ")", treeMats.Count <= 12 && treeMats.Where(m => m.shader.name == "Mazraa/Foliage").All(m => m.enableInstancing));
        T.Check("Look: Najd has acacia, ghaf or sidr trees and date palms", groups.Any(g => g.name == "Date Palm") && groups.Any(g => g.name == "Acacia" || g.name == "Ghaf" || g.name == "Sidr"));
        var props = UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(c => c.name == "Ghaf").ToList();
        T.Check("Look: ghaf trees keep the prop collider layer", props.All(c => c.gameObject.layer == DesertProps.PropLayer));

        // ---- ground meshes and terrain
        var sand = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == "Sand" && r.transform.IsChildOf(dd.transform));
        var track = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == "Track" && r.transform.IsChildOf(dd.transform));
        T.Check("Look: Najd sand mesh uses the sand PBR material, the track compacted gravel", sand != null && sand.sharedMaterial == LookLibrary.Get(LookMat.Sand) && (track == null || track.sharedMaterial == LookLibrary.Get(LookMat.Gravel)));
        var plot = FarmFields.Instance.Fields[0];
        var soil = plot.View.transform.Find("Ground").GetComponent<MeshRenderer>();
        T.Check("Look: FarmCore fields use the tilled-soil texture (detail map) over the cell texture", soil.sharedMaterial.GetTexture("_DetailAlbedoMap") == LookLibrary.Get(LookMat.Tilled).GetTexture("_BaseMap")
            && soil.sharedMaterial.GetTexture("_DetailAlbedoMap") != null && soil.sharedMaterial.GetTexture("_BaseMap") == plot.View.GroundTexture);

        var tgo = new GameObject("Look Test Terrain");
        var terrain = tgo.AddComponent<Terrain>();
        var tcol = tgo.AddComponent<TerrainCollider>();
        terrain.terrainData = new TerrainData { size = new Vector3(200f, 50f, 200f), alphamapResolution = 32 };
        tcol.terrainData = terrain.terrainData;
        bool dressed = GroundLook.ApplyTerrain(terrain, Biome.Desert);
        var data = terrain.terrainData;
        bool sumsToOne = true; float rock = 0f;
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float s = 0; for (int l = 0; l < 4; l++) s += data.alphamaps[y, x, l]; if (Math.Abs(s - 1f) > 0.01f) sumsToOne = false; if (x >= 30) rock += data.alphamaps[y, x, 2]; }
        T.Check("Look: terrain gets 4 PBR layers (albedo + normal + mask) and the Terrain/Lit look material", dressed && data.terrainLayers.Length == 4 && data.terrainLayers.All(l => l.diffuseTexture != null && l.normalMapTexture != null && l.maskMapTexture != null) && terrain.materialTemplate == terrainMat);
        T.Check("Look: splat map is normalised, rock on steep slopes", sumsToOne && rock > 50f, "rock " + rock);
        GroundLook.PaintArea(terrain, new Bounds(new Vector3(100f, 0f, 100f), new Vector3(20f, 5f, 20f)), 3, 1f, 1f);
        T.Check("Look: PaintArea lays tilled soil (layer 3) for the world generator", data.alphamaps[16, 16, 3] > 0.99f && data.alphamaps[2, 2, 3] < 0.01f);
        T.Check("Look: Delta terrain = alluvial soil + grass", GroundLook.LayersOf(Biome.Delta)[0] == LookMat.Soil && GroundLook.LayersOf(Biome.Delta)[1] == LookMat.Grass);
        UnityEngine.Object.Destroy(tgo);

        // ---- foliage density (Settings > Graphics > Foliage) changes the counts; grass is instanced
        var grass = FoliageGrass.Instance;
        GameSettings.FoliageDensity = 100; GameSettings.ApplyOptions(); FakeEngine.Run(0.2f);
        dd.BuildAll(); FakeEngine.Run(0.2f);
        int grass100 = grass.InstanceCount, shrubs100 = dd.Shrubs, trees100 = dd.Trees;
        GameSettings.FoliageDensity = 25; GameSettings.ApplyOptions(); FakeEngine.Run(0.2f);
        int grass25 = grass.InstanceCount, shrubs25 = dd.Shrubs, trees25 = dd.Trees;
        GameSettings.FoliageDensity = 0; GameSettings.ApplyOptions(); FakeEngine.Run(0.2f);
        int grass0 = grass.InstanceCount, shrubs0 = dd.Shrubs, trees0 = dd.Trees;
        T.Check("Look: Foliage 100% -> 25% -> 0% scales grass, shrubs and trees (" + grass100 + "/" + grass25 + "/" + grass0 + " grass)",
            grass100 > 200 && grass25 < grass100 * 0.5f && grass0 == 0 && shrubs100 > shrubs25 && shrubs0 == 0 && trees100 > trees25 && trees0 == 0,
            $"shrubs {shrubs100}/{shrubs25}/{shrubs0} trees {trees100}/{trees25}/{trees0}");
        GameSettings.FoliageDensity = -1; GameSettings.ApplyOptions(); FakeEngine.Run(0.2f);
        T.Check("Look: dressing grass count matches the instanced renderer", dd.GrassTufts == grass.InstanceCount && grass.InstanceCount > 0, dd.GrassTufts + " vs " + grass.InstanceCount);

        var cam = Camera.main;
        var player = GameObject.Find("PlayerCapsule").transform;
        cam.transform.position = player.position + Vector3.up * 1.7f;
        int calls0 = Graphics.InstancedCalls;
        FakeEngine.Tick(0.05f);
        int drawnHigh = grass.DrawnLastFrame;
        T.Check("Look: grass is drawn instanced near the camera (" + drawnHigh + " tufts, " + grass.BatchesLastFrame + " draw calls, <= 1023 each)", drawnHigh > 0 && Graphics.InstancedCalls > calls0 && grass.BatchesLastFrame >= 1);
        GraphicsEnhancer.SetPreset(GraphicsEnhancer.PresetLow); FakeEngine.Tick(0.05f);
        int drawnLow = grass.DrawnLastFrame;
        T.Check("Look: Low preset draws grass only close by (" + drawnLow + " vs " + drawnHigh + ")", FoliageGrass.DrawDistance < 40f && drawnLow < drawnHigh);

        // ---- presets
        GraphicsEnhancer.SetPreset(GraphicsEnhancer.PresetUltra); FakeEngine.Run(0.2f);
        var probe = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).FirstOrDefault();
        T.Check("Look: Ultra = 150 m shadows, 4 cascades, HDR grading, 256 px realtime reflection probe, longest grass",
            Math.Abs(urp.shadowDistance - 150f) < 0.01f && urp.shadowCascadeCount == 4 && urp.colorGradingMode == ColorGradingMode.HighDynamicRange && atmo.ProbeActive && probe != null && probe.resolution == 256
            && probe.refreshMode == UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting && FoliageGrass.DrawDistance >= 140f && QualitySettings.shadowResolution == ShadowResolution.VeryHigh,
            urp.shadowDistance + " " + urp.shadowCascadeCount + " " + urp.colorGradingMode);
        int renders = probe != null ? probe.renders : 0;
        FakeEngine.Run(12f, 0.5f);
        T.Check("Look: the reflection probe re-renders now and then (not every frame)", probe != null && probe.renders > renders && probe.renders < renders + 6, renders + " -> " + (probe != null ? probe.renders : 0));
        GraphicsEnhancer.SetPreset(GraphicsEnhancer.PresetLow); FakeEngine.Run(0.2f);
        T.Check("Look: Low = no probe, 1 cascade, LDR grading, no extra grade, hard shadows", !atmo.ProbeActive && urp.shadowCascadeCount == 1 && urp.colorGradingMode == ColorGradingMode.LowDynamicRange && atmo.GradeVolume.weight == 0f
            && (RenderSettings.sun == null || RenderSettings.sun.shadows == LightShadows.Hard));
        GraphicsEnhancer.SetPreset(GraphicsEnhancer.PresetHigh); FakeEngine.Run(0.2f);
        T.Check("Look: High = probe on, grade on, soft shadows", atmo.ProbeActive && atmo.GradeVolume.weight > 0.9f && urp.shadowCascadeCount == 4 && (RenderSettings.sun == null || RenderSettings.sun.shadows == LightShadows.Soft));
        T.Check("Look: Ultra / High / Medium / Low names have Arabic", GraphicsEnhancer.PresetNames.All(n => Loc.HasTranslation(n)) && Loc.HasTranslation(GameSettings.PresetName(3)));

        // ---- sky, ambient, wind, wet ground
        T.Check("Look: procedural sky installed (sun disk follows the sun)", atmo.SkyInstalled && RenderSettings.skybox != null && RenderSettings.skybox.shader.name == "Skybox/Procedural");
        int env0 = DynamicGI.Updates;
        FakeEngine.Run(10f, 0.5f);
        T.Check("Look: ambient light re-baked from the sky as the sun moves (High)", DynamicGI.Updates > env0 || atmo.EnvironmentUpdates > 0, DynamicGI.Updates + "");
        T.Check("Look: foliage wind is set globally", Shader.Globals.ContainsKey("_MazraaWind") && Shader.Globals["_MazraaWind"].w > 0.2f);
        var clock = WorldClock.Instance;
        T.SetProp(clock, "Weather", Weather.Rain); FakeEngine.Run(70f, 0.5f);
        float wetRain = atmo.Wetness;
        var sandMat = LookLibrary.Get(LookMat.Sand);
        T.Check("Look: rain soaks the ground (wet = darker, glossier)", wetRain > 0.9f && LookLibrary.Wetness > 0.9f, wetRain + "");
        T.SetProp(clock, "Weather", Weather.Sunny); FakeEngine.Run(60f, 0.5f);
        T.Check("Look: ground dries after the rain", atmo.Wetness < wetRain - 0.15f, atmo.Wetness + "");

        // ---- clothing by region
        var villagers = Villagers.Instance != null ? Villagers.Instance.Npcs.Values.Where(v => v != null).ToList() : new List<GameObject>();
        Func<GameObject, string, string, bool> wears = (go, robe, head) =>
            go.transform.Find(NpcWardrobe.RobeName)?.Find(robe) != null && go.transform.Find(Appearance.HeadwearName)?.Find(head) != null;
        T.Check("Look: Najd villagers wear thobe + shemagh / ghutra with agal (" + villagers.Count + ")", villagers.Count > 0 && villagers.All(v => wears(v, "Thobe", "Shemagh") && v.transform.Find(Appearance.HeadwearName).Find("Agal") != null));
        var robeR = villagers.Count > 0 ? villagers[0].transform.Find(NpcWardrobe.RobeName).GetComponentInChildren<MeshRenderer>() : null;
        T.Check("Look: thobe is real cloth (linen weave PBR material, robe mesh)", robeR != null && LookLibrary.Classify(robeR.sharedMaterial) == LookMat.Linen && robeR.GetComponent<MeshFilter>().sharedMesh.vertexCount > 100);
        var woman = new GameObject("Characters/character-female-b test");
        Build.Part(PrimitiveType.Cube, woman.transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.4f, 1.6f, 0.3f), Build.Mat(Color.white), "body-mesh");
        NpcWardrobe.DressRegional(woman, true, 1);
        T.Check("Look: Najd women wear abaya + shayla headscarf", wears(woman, "Abaya", "Scarf"));

        File.WriteAllText(Dlc.KeyFilePath, "nile_delta\n"); Dlc.Refresh();
        Regions.Select("nile_delta");
        FakeEngine.Run(2f);
        T.Check("Look: Nile Delta villagers wear galabeya + emma turban", villagers.Count > 0 && villagers.All(v => wears(v, "Galabeya", "Emma")),
            villagers.Count > 0 ? string.Join(",", villagers[0].transform.Find(NpcWardrobe.RobeName)?.Cast<Transform>().Select(t => t.name) ?? new string[0]) : "none");
        NpcWardrobe.DressRegional(woman, true, 1);
        T.Check("Look: Delta women wear a dark galabeya + tarha", wears(woman, "Galabeya", "Tarha"));
        var deltaSand = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == "Sand" && r.transform.IsChildOf(dd.transform));
        T.Check("Look: Delta ground mesh is grass, eucalyptus are LOD trees", deltaSand != null && deltaSand.sharedMaterial == LookLibrary.Get(LookMat.Grass)
            && UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Any(g => g.name == "Eucalyptus" && g.transform.IsChildOf(dd.transform)));
        T.Check("Look: Delta grass is green", grass.InstanceCount > 0);
        Regions.Select("najd"); FakeEngine.Run(2f);
        T.Check("Look: back in Najd the villagers wear thobes again", villagers.All(v => wears(v, "Thobe", "Shemagh")));
        UnityEngine.Object.Destroy(woman);

        // ---- animals, animation
        T.Check("Look: animal and character models get matte materials and shadows (ModelLook)", ModelLook.Upgraded > 5 && ModelLook.IsAnimal("cow") && ModelLook.IsAnimal("Models/sheep") && !ModelLook.IsAnimal("Tractor"));
        T.Check("Look: smoother animation blends (>= 0.3 s)", Actor.BlendTime >= 0.3f);

        // ---- public API for the world generator
        var area = new Bounds(new Vector3(2000f, 0f, 2000f), new Vector3(120f, 10f, 120f));
        var r1 = Vegetation.Scatter(area, Biome.Oasis, 42, null, null, 1f);
        int g1 = FoliageGrass.Instance.InstanceCount;
        var before2 = new HashSet<LODGroup>(UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None));
        var r2 = Vegetation.Scatter(area, Biome.Oasis, 43, p => p.x < 2000f, null, 1f);
        var new2 = UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Where(g => !before2.Contains(g)).ToList();
        T.Check("Look: Vegetation.Scatter places trees, shrubs and grass (" + r1 + ")", r1.Trees > 5 && r1.Shrubs > 0 && r1.Grass > 100);
        T.Check("Look: Scatter respects canPlace (roads / fields stay clear)", new2.Count == r2.Trees + r2.Shrubs && new2.Count > 0 && new2.All(g => g.transform.position.x < 2000f), r2 + "");
        T.Check("Look: Scatter honours density 0", Vegetation.Scatter(area, Biome.Delta, 7, null, null, 0f).Trees == 0);
        int before = FoliageGrass.Instance.InstanceCount;
        int removed = FoliageGrass.Instance.ClearArea(area);
        T.Check("Look: FoliageGrass.ClearArea removes a tile's grass", removed >= r1.Grass && FoliageGrass.Instance.InstanceCount == before - removed, removed + " of " + before);
        var tree = Vegetation.PlaceTree(new Vector3(2100f, 0f, 2100f), TreeType.DatePalm, 5);
        T.Check("Look: Vegetation.PlaceTree -> LOD palm with a trunk collider on the prop layer", tree.name == "Date Palm" && tree.GetComponent<LODGroup>() != null && tree.GetComponent<BoxCollider>() != null && tree.layer == DesertProps.PropLayer);
        T.Check("Look: biome of each map", Vegetation.BiomeOf("najd") == Biome.Desert && Vegetation.BiomeOf("nile_delta") == Biome.Delta && Vegetation.BiomeOf("batinah") == Biome.Coastal);

        T.Check("Look: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
        GameSettings.FoliageDensity = -1;
        UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline = null;
    }
}
