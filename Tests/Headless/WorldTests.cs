// Open world (Assets/Scripts/World): terrain tiles around the scene terrain, road graph, places, selling at a place,
// expansion land, map + waypoint + fast travel, signposts, culling, the Delta layout and the open-world missions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

static class WorldTests
{
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    public static void Run()
    {
        Console.WriteLine("Open world: terrain, roads, places, selling, land, map, fast travel");
        Program.BuildWorld();
        FakeEngine.Run(3f);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        Loc.SetLanguage(Language.English);
        int e0 = FakeEngine.Errors.Count;
        var w = OpenWorld.Instance;
        T.Check("World: built at scene start", w != null && w.IsBuilt && w.BuiltRegion == "najd", w == null ? "null" : w.BuiltRegion);
        var L = w.Layout;
        w.CoreRect(out var cmin, out var cmax);
        float coreArea = (cmax.x - cmin.x) * (cmax.z - cmin.z), worldArea = (L.Half * 2f) * (L.Half * 2f);
        T.Check("World: at least 2.4 x 2.4 km, > 25x the old playable area", L.Half >= 1200f && worldArea > coreArea * 25f, $"half {L.Half} core {coreArea} world {worldArea}");
        T.Check("World: 24 terrain tiles around the scene terrain, low-res heightmaps", w.Tiles.Count == 24 && w.HeightmapResolution <= 129
            && w.Tiles.All(t => t.terrainData != null && !t.drawTreesAndFoliage && t.basemapDistance <= 300f));
        var tileArea = w.Tiles.Sum(t => t.terrainData.size.x * t.terrainData.size.z);
        T.Check("World: tiles cover the world minus the scene terrain", Math.Abs(tileArea + coreArea - worldArea) < worldArea * 0.001f, tileArea + "");
        T.Check("World: tiles are neighbours of each other (seamless LOD)", w.Tiles.Count(t => t.rightNeighbor != null || t.leftNeighbor != null) >= 20);
        // soft edge: ground rises at the rim, invisible walls at the very edge
        float mid = w.Shape.Final(L.Center.x + L.Half * 0.3f, L.Center.z - L.Half * 0.8f);
        float rim = w.Shape.Final(L.Center.x + L.Half * 0.3f, L.Center.z - L.Half + 5f);
        T.Check("World: soft edge rises at the rim, 4 edge walls far away", rim > mid + 15f && w.EdgeWalls.Count == 4
            && w.EdgeWalls.All(e => Math.Abs(Math.Abs(e.transform.position.x - L.Center.x) - L.Half) < 10f || Math.Abs(Math.Abs(e.transform.position.z - L.Center.z) - L.Half) < 10f), mid + " -> " + rim);
        // terrain heights come from the shape (sample a tile)
        var probe = new Vector3(L.Center.x + L.Half * 0.75f, 0f, L.Center.z + L.Half * 0.75f);
        var tile = w.Tiles.First(t => probe.x >= t.transform.position.x && probe.x <= t.transform.position.x + t.terrainData.size.x && probe.z >= t.transform.position.z && probe.z <= t.transform.position.z + t.terrainData.size.z);
        float th = tile.SampleHeight(probe) + tile.transform.position.y, sh = w.BaseY + w.Shape.Final(probe.x, probe.z);
        T.Check("World: terrain tiles hold the generated ground", Math.Abs(th - sh) < 1.5f, th + " vs " + sh);
        T.Check("World: surface layers painted (3 small layers, alphamaps set)", tile.terrainData.terrainLayers.Length == 3 && tile.terrainData.alphamaps != null
            && tile.terrainData.terrainLayers.All(l => l.diffuseTexture != null && l.diffuseTexture.width <= 64));

        // ---- roads
        var reach = L.Reachable();
        var unreachable = L.Pois.Where(p => !reach.Contains(p.node)).Select(p => p.id).ToList();
        T.Check("Roads: every place is connected to the farm (graph search)", unreachable.Count == 0 && L.Pois.Count >= 12, string.Join(",", unreachable));
        T.Check("Roads: all places within driving distance by road", L.Pois.All(p => p.kind == PoiKind.Farm || L.RoadDistance(L.Pois[L.FarmPoi].node, p.node) < L.Half * 6f));
        var farmRoad = L.Roads.First(r => r.inCore);
        T.Check("Roads: the farm road starts at the market pad and leaves the scene terrain", Flat(farmRoad.path[0] - DeliveryZone.Market.transform.position).magnitude < 1f
            && L.CoreDistance(farmRoad.path[farmRoad.path.Count - 1]) < 1f);
        T.Check("Roads: an asphalt ring highway with dirt tracks to farms and land", L.Roads.Count(r => r.asphalt && r.width >= 9f) >= 6 && L.Roads.Any(r => !r.asphalt) && L.Ring.Count > 100);
        var ring = L.Roads.First(r => r.width >= 9f);
        var rp = ring.path[ring.path.Count / 2];
        T.Check("Roads: ground flattened under the road (profile)", Math.Abs(w.Shape.Final(rp.x, rp.z) - ring.profile[ring.path.Count / 2]) < 0.3f);
        float maxGrade = 0f;
        foreach (var r in L.Roads.Where(r => !r.inCore)) for (int i = 1; i < r.path.Count; i++) { float d = Flat(r.path[i] - r.path[i - 1]).magnitude; if (d > 1f) maxGrade = Math.Max(maxGrade, Math.Abs(r.profile[i] - r.profile[i - 1]) / d); }
        T.Check("Roads: drivable grades (< 20%)", maxGrade < 0.2f, maxGrade + "");
        T.Check("Roads: asphalt on the highway, sand off-road (Najd)", DriveSurfaces.At(rp) == DriveSurface.Asphalt
            && DriveSurfaces.At(new Vector3(L.Center.x - L.Half * 0.85f, 0f, L.Center.z + L.Half * 0.85f)) == DriveSurface.Sand);
        T.Check("Roads: the farm road is asphalt on the scene terrain too", DriveSurfaces.At(farmRoad.path[farmRoad.path.Count / 2]) == DriveSurface.Asphalt);
        T.Check("Roads: signposts at junctions (EN + AR)", w.Signs >= 6 && UnityEngine.Object.FindObjectsByType<TMPro.TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(t => t.gameObject.name == "AR"), w.Signs + "");
        int gateNode = L.Nodes.FindIndex(n => n.roads.Any(ri => L.Roads[ri].inCore) && n.poi < 0);
        var gateLines = w.SignLines(gateNode);
        T.Check("Roads: the farm gate sign lists the town and the nearest places by road", gateLines.Any(g => g.poi.id == "town") || gateLines.Count == 5, string.Join(",", gateLines.Select(g => g.poi.id)));

        // ---- places
        string[] najd = { "town", "fuel", "dealer", "silo", "dairy", "fodder", "port" };
        T.Check("Places (Najd): town, fuel, dealership, silo, dairy co-op, fodder traders, dry port, 3 neighbour farms, 3 land parcels",
            najd.All(id => L.Poi(id) != null && w.Places.ContainsKey(id)) && L.Pois.Count(p => p.kind == PoiKind.Neighbour) == 3 && L.Parcels.Count == 3);
        var badPlaces = L.Pois.Where(p => p.kind != PoiKind.Farm && (L.InCore(p.pos, p.radius * 0.5f) || !L.InWorld(p.pos, WorldShape.EdgeBand))).Select(p => p.id + p.pos).ToList();
        T.Check("Places: outside the scene terrain, inside the world", badPlaces.Count == 0, string.Join(" ", badPlaces));
        T.Check("Places: selling points are market points of this map", L.Pois.Where(p => p.Sells).All(p => MarketManager.Instance.Point(p.sellPoint) != null));
        var townVisual = w.Places["town"].Find("Visual");
        T.Check("Places: merged meshes + a few cheap box colliders", townVisual != null && townVisual.GetComponentsInChildren<BoxCollider>().Length >= 10 && townVisual.GetComponentsInChildren<BoxCollider>().Length < 40
            && townVisual.gameObject.layer == DesertProps.PropLayer && townVisual.GetComponentsInChildren<MeshRenderer>(true).Length <= 30,
            townVisual == null ? "null" : townVisual.GetComponentsInChildren<BoxCollider>().Length + " cols " + townVisual.GetComponentsInChildren<MeshRenderer>(true).Length + " renderers");
        T.Check("Places: each has a map icon", L.Pois.Where(p => p.kind != PoiKind.Farm).All(p => w.Places[p.id].GetComponent<MinimapIcon>() != null));
        T.Check("Places: neighbour farms have workers (when models exist) and trees are scattered", w.Trees > 50, w.Trees + "");

        // ---- culling
        var player = GameObject.Find("PlayerCapsule").transform;
        FakeEngine.Run(0.5f);
        var portVisual = w.Places["port"].Find("Visual").gameObject;
        var fuelLabel = w.Places["fuel"].Find("Label").gameObject;
        T.Check("Culling: far places switched off (labels too)", !fuelLabel.activeSelf && w.ActiveCulled < w.CulledCount, w.ActiveCulled + "/" + w.CulledCount);
        Vector3 home = player.position;
        player.position = w.OnGround(L.Poi("fuel").arrival, 1f);
        FakeEngine.Run(0.5f);
        T.Check("Culling: places come back when you get near", fuelLabel.activeSelf);

        // ---- discovery + missions hooks
        T.Check("Discovery: getting near a place unlocks it for fast travel", w.Seen(L.Poi("fuel")) && !w.Seen(L.Poi("port")));
        var mm = MissionManager.Instance;
        int wRoad = Array.FindIndex(mm.Missions, m => m.id == "w_road");
        var camp = CampaignManager.Instance;
        T.Check("Campaign (Najd): chapter 8 'The Open Road' starts with the open-world missions", wRoad == 45 && camp.Chapters.Length == 8 && camp.Chapters[7].fromMission == wRoad + 1
            && new[] { "w_road", "w_town_sell", "w_dealer", "w_land", "w_travel" }.All(id => mm.Missions.Any(m => m.id == id)), wRoad + "");
        T.Set(mm, "<Index>k__BackingField", wRoad); T.Set(mm, "<Progress>k__BackingField", 0f);
        var target = typeof(ObjectiveMarker).GetMethod("FindTarget", BindingFlags.NonPublic | BindingFlags.Static);
        object[] args = { 0f };
        var tt = (Transform)target.Invoke(null, args);
        T.Check("Missions: the objective marker points at the town", tt == w.Places["town"], tt == null ? "null" : tt.name);
        int money0 = EconomyManager.Instance.Money;
        player.position = w.OnGround(L.Poi("town").pos, 1f);
        FakeEngine.Run(0.5f);
        T.Check("Missions: reaching the town completes 'The road to town'", mm.Current?.id == "w_town_sell" && EconomyManager.Instance.Money >= money0 + 300, mm.Current?.id);

        // ---- selling at a specific place uses that place's prices
        var eco = EconomyManager.Instance; var market = MarketManager.Instance;
        var silo = L.Poi("silo");
        float g0 = market.Glut("mill", "wheat"), gSouq = market.Glut("souq", "wheat");
        float unit = market.Price("wheat", "mill") / (1f - g0);
        var millPt = market.Point("mill");
        float g1 = MarketMath.GlutAfter(millPt, g0, 100f);
        float mul = Career.SellMultiplier * (Regions.Current.market > 0f ? Regions.Current.market : 1f);
        int expected = Mathf.RoundToInt(MarketMath.SaleValue(unit, g0, g1, 100f) * MarketManager.DirectSaleBonus * mul);
        var cargo = new Dictionary<string, float> { { "wheat", 100f }, { "onion", 20f } };
        int before = eco.Money;
        var notes = new List<string>();
        int earned = w.SellHere(silo, cargo, notes);
        T.Check("Selling: the silo pays its own wheat price (+ direct bonus, no haul)", Math.Abs(earned - expected) <= Math.Max(2, expected * 0.02f) && eco.Money - before == earned, earned + " vs " + expected);
        T.Check("Selling: only that point's market moves (silo glut up, souq unchanged)", market.Glut("mill", "wheat") > g0 && Math.Abs(market.Glut("souq", "wheat") - gSouq) < 1e-5f);
        T.Check("Selling: goods the place doesn't buy stay loaded", cargo.Count == 1 && cargo.ContainsKey("onion") && notes.Any(n => n.Contains("doesn't buy")), string.Join(" | ", notes));
        // truck parked on the town pad sells the load there (and counts for the mission)
        var truck = FarmTruck.Instance;
        truck.transform.position = w.OnGround(L.Poi("town").pos, 0.6f);
        truck.Interact();
        truck.Cargo.Clear(); truck.Cargo["wheat"] = 200f;
        T.SetProp(truck, "Speed", 0f);
        int m1 = eco.Money;
        FakeEngine.Run(1f);
        T.Check("Selling: the truck sells at the town pad it is parked on", truck.Cargo.Count == 0 && eco.Money > m1, eco.Money - m1 + "");
        T.Check("Missions: 150 kg sold in town completes 'Sell in town'", mm.Current?.id == "w_dealer", mm.Current?.id);
        // workshop and fuel
        truck.transform.position = w.OnGround(L.Poi("dealer").pos, 0.6f);
        FakeEngine.Run(0.5f);
        T.Check("Workshop: parked in the dealership yard counts as a workshop, visit completes the mission", truck.AtWorkshop && mm.Current?.id == "w_land", mm.Current?.id);
        T.SetProp(truck, "Fuel", 10f);
        truck.transform.position = w.OnGround(L.Poi("fuel").pos, 0.6f);
        int m2 = eco.Money;
        FakeEngine.Run(2f);
        T.Check("Fuel station refuels the parked vehicle for money", truck.Fuel > 10f && eco.Money < m2, truck.Fuel + "");
        T.SetProp(truck, "Speed", 0f); truck.Exit();

        // ---- expansion land
        var land = WorldLand.Instance;
        var parcel = L.Parcels[0];
        var fm = FieldManager.Instance;
        int plots0 = fm.Plots.Count;
        eco.AddMoney(20000);
        int m3 = eco.Money;
        var sign = UnityEngine.Object.FindObjectsByType<WorldLandSign>(FindObjectsSortMode.None).First(s => s.Parcel == parcel);
        T.Check("Land: for-sale sign with price", sign.InteractionPrompt.Contains(parcel.price.ToString("N0")), sign.InteractionPrompt);
        var targetLand = (Transform)target.Invoke(null, new object[] { 0f });
        T.Check("Missions: the land mission points at a for-sale sign", targetLand != null && targetLand.GetComponent<WorldLandSign>() != null);
        sign.Interact();
        T.Check("Land: buying adds the parcel's fields (FieldPlots, own save keys)", fm.Plots.Count == plots0 + parcel.Plots && eco.Money == m3 - parcel.price + mm.Missions.First(m => m.id == "w_land").reward
            && land.PlotsOf(parcel).All(pl => pl.index >= FieldManager.WorldPlotBase && L.InWorld(pl.transform.position)), fm.Plots.Count + " " + eco.Money);
        T.Check("Land: owned, sign gone, map icon turns green, mission done", land.Owned(parcel) && sign == null && mm.Current?.id == "w_travel"
            && w.Places[L.Pois.First(x => x.parcel == parcel).id].GetComponent<MinimapIcon>().color.g > 0.8f, mm.Current?.id);
        var newPlot = land.PlotsOf(parcel).First();
        newPlot.ResetPlot(); eco.AddSeeds("wheat", 2); FieldPlot.PreferredSeed = "wheat";
        newPlot.TryPlow(false); newPlot.TrySeed(false);
        T.Check("Land: the new fields work like the farm's", newPlot.State == PlotState.Growing);
        T.Check("Land: can't buy twice", !land.Buy(parcel));
        w.BuildWorld();
        FakeEngine.Run(0.2f);
        T.Check("Land: owned fields come back once after a rebuild (no duplicates)", fm.Plots.Count == plots0 + parcel.Plots && fm.Plots.Count(pl => pl.index >= FieldManager.WorldPlotBase) == parcel.Plots, fm.Plots.Count + "");
        L = w.Layout;
        T.Check("Land: the farm grid keeps its own save keys", fm.AddPlot(9, 0).index < FieldManager.WorldPlotBase);
        fm.Plots.RemoveAt(fm.Plots.Count - 1);

        // ---- map, waypoint, fast travel
        var map = WorldMap.Instance; var mini = Minimap.Instance;
        T.Check("Map: lists every place of the world", map.Entries.Count == L.Pois.Count && map.Entries.Any(e => e.id == "port"));
        player.position = home;
        mini.SetFull(true);
        FakeEngine.Run(0.6f);
        T.Check("Map: the full map frames the whole world with the places list", mini.FullRange > 900f && map.PanelVisible && map.ListText.Contains("Town market") && map.ListText.Contains("Farmland for sale"), mini.FullRange + "");
        var town = L.Poi("town");
        float k = mini.fullSize * 0.5f / mini.FullRange;
        Vector2 px = new Vector2((town.pos.x - mini.Center.x) * k + 3f, (town.pos.z - mini.Center.z) * k - 2f);
        map.MapClick(px, k, mini.Center, map.Entries);
        T.Check("Waypoint: a click near the town on the map snaps to it", map.WaypointPoi == town);
        float wd = map.WaypointDistance(player.position);
        T.Check("Waypoint: distance from the player", Math.Abs(wd - Flat(town.pos - player.position).magnitude) < 0.5f, wd + "");
        mini.SetFull(false);
        FakeEngine.Tick(0.1f);
        string hud = string.Join("\n", HudLines.Status.Select(f => f()).Where(s => !string.IsNullOrEmpty(s)));
        T.Check("Waypoint: HUD line with name, distance and direction", hud.Contains("Waypoint: Town market") && hud.Contains(" m") && hud.Contains("north"), hud);
        T.Check("Waypoint: beam marker and a W on the minimap", MinimapIcon.All.Any(i => i.letter == "W" && i.isActiveAndEnabled));
        // fast travel
        var port = L.Poi("port");
        T.Check("Fast travel: only to visited places", map.FastTravelBlock(port) == "Visit it first to unlock fast travel" && !map.FastTravel(port));
        int fee = map.Fee(town, player.position);
        int m4 = eco.Money;
        bool moved = map.FastTravel(town);
        T.Check("Fast travel: moves the player to the place and charges the fee", moved && Flat(player.position - town.arrival).magnitude < 2f && eco.Money == m4 - fee + mm.Missions.First(m => m.id == "w_travel").reward + camp.Chapters[7].reward && fee > WorldMap.FeeBase,
            moved + " fee " + fee + " money " + (m4 - eco.Money));
        T.Check("Fast travel: completes the open-world chapter", mm.Current == null || mm.Index > Array.FindIndex(mm.Missions, m => m.id == "w_travel"));
        T.Check("Fast travel: not to where you already are", map.FastTravelBlock(town) == "You are already here");
        player.position = home;

        // ---- every play mode gets the world (not gated by campaign progress)
        SaveData.SetInt(GameModes.ModeKey, (int)GameModes.Mode.FreePlay);
        T.Set(mm, "<Index>k__BackingField", 0);
        w.BuildWorld();
        T.Check("Modes: free play at mission 1 builds the same world", w.IsBuilt && w.Layout.Pois.Count == L.Pois.Count && w.Tiles.Count == 24);
        SaveData.SetInt(GameModes.ModeKey, (int)GameModes.Mode.Campaign);

        // ---- texts
        var missing = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(FakeEngine.ResourcesRoot, "..", "Scripts", "World"), "*.cs"))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "Loc\\.(?:T|Raw)\\(\"((?:[^\"\\\\]|\\\\.)*)\""))
                if (!Loc.HasTranslation(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
        foreach (var region in new[] { "najd", "nile_delta" }) foreach (var d in WorldLayout.PoiDefs(region)) if (!Loc.HasTranslation(d.nameEn) || string.IsNullOrEmpty(d.nameAr)) missing.Add(d.nameEn);
        foreach (var key in new[] { "Farmland for sale", "Your farm", "north-east", "Not for sale", "You already own this land", "Not available", "Visit it first to unlock fast travel", "You are already here" })
            if (!Loc.HasTranslation(key)) missing.Add(key);
        T.Check("Texts: every open-world text has Arabic (Loc/99_world.json)", missing.Count == 0, string.Join(" | ", missing.Distinct()));

        // ---- Nile Delta layout
        GameMenu.Instance.StartCareer("nile_delta", Career.Start.NewFarmer, Career.Economy.Normal);
        FakeEngine.Run(0.5f);
        L = w.Layout;
        T.Check("Delta: region switch rebuilds the world for the Nile Delta", w.BuiltRegion == "nile_delta" && L.Delta && w.Tiles.Count == 24);
        T.Check("Delta: cotton gin, rice mill, village souq, town, Damietta port, fuel, dealership", new[] { "gin", "ricemill", "village", "town", "port", "fuel", "dealer" }.All(id => L.Poi(id) != null) && L.Poi("silo") == null
            && L.Poi("port").nameEn.Contains("Damietta"));
        T.Check("Delta: canals and fields (off-road is field, not sand)", w.Shape.Canals.Count >= 3 && DriveSurfaces.At(new Vector3(L.Center.x - L.Half * 0.85f, 0f, L.Center.z + L.Half * 0.3f)) == DriveSurface.Field);
        T.Check("Delta: all places reachable from the farm", L.Pois.All(x => L.Reachable().Contains(x.node)));
        T.Check("Delta: own open-world missions and chapter 6", mm.Missions.Length == 25 && mm.Missions.Skip(20).All(m => m.id.StartsWith("eg_w_")) && camp.Chapters.Length == 6 && camp.Chapters[5].fromMission == 21);
        T.Check("Delta: land bought in Najd stays in Najd", WorldLand.Instance.OwnedCount == 0 && fm.Plots.Count(pl => pl.index >= FieldManager.WorldPlotBase) == 0);
        GameMenu.Instance.StartCareer("najd", Career.Start.NewFarmer, Career.Economy.Normal);
        FakeEngine.Run(0.5f);
        T.Check("New game: land sold back, places undiscovered", w.BuiltRegion == "najd" && WorldLand.Instance.OwnedCount == 0 && !w.Seen(w.Layout.Poi("town")));
        T.Check("Open world: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
    }
}
