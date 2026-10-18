// Headless tests: vehicles collide with fences and props (gates stay open), bumping people and animals, vehicle looks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

static class BumpTests
{
    const float Dt = 0.02f;
    static UnityEngine.InputSystem.Keyboard kb;

    /// <summary>A scene-style fence line: a "Fence ..." root with 2 m panels (box colliders, like FenceLayout's prefab).</summary>
    static GameObject FenceLine(string name, Vector3 from, Vector3 to)
    {
        var root = new GameObject(name);
        Vector3 d = to - from; d.y = 0f;
        int n = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / 2f));
        float yaw = (float)(Math.Atan2(d.x, d.z) * 180.0 / Math.PI);
        for (int i = 0; i < n; i++)
        {
            var p = new GameObject("FencePanel");
            p.transform.SetParent(root.transform, false);
            p.transform.position = from + d * ((i + 0.5f) / n);
            p.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var c = p.AddComponent<BoxCollider>(); c.center = new Vector3(0f, 0.5f, 0f); c.size = new Vector3(0.15f, 1f, d.magnitude / n);
            var cube = new GameObject("Cube"); cube.transform.SetParent(p.transform, false); cube.AddComponent<MeshFilter>(); cube.AddComponent<MeshRenderer>();
        }
        return root;
    }

    static void Place(Vehicle v, Vector3 pos, float yaw)
    {
        v.transform.position = new Vector3(pos.x, v.rideHeight, pos.z);
        v.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        T.Set(v, "yaw", yaw);
        T.SetProp(v, "Speed", 0f);
        v.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
    }

    static void Drive(float seconds, Func<bool> until = null)
    {
        kb.wKey.isPressed = true;
        for (float t = 0; t < seconds; t += Dt) { FakeEngine.Tick(Dt); if (until != null && until()) break; }
        kb.wKey.isPressed = false;
    }

    static float FrontZ(Vehicle v) => v.transform.position.z + v.BodyCenter.z + v.BodyHalfExtents.z;

    public static void Run()
    {
        Console.WriteLine("Bumps: fences, gates, people, animals, vehicle looks");
        Program.BuildWorld();
        FakeEngine.Run(3f);
        for (int i = 0; i < 400 && LoadingScreen.Visible; i++) FakeEngine.Tick(0.1f);
        var intro = UnityEngine.Object.FindAnyObjectByType<StoryIntro>();
        if (intro != null) typeof(StoryIntro).GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(intro, null);
        Loc.SetLanguage(Language.English);
        FakeEngine.Tick(Dt);
        int e0 = FakeEngine.Errors.Count;
        kb = new UnityEngine.InputSystem.Keyboard();
        foreach (var f in typeof(UnityEngine.InputSystem.Keyboard).GetFields())
            if (f.FieldType == typeof(UnityEngine.InputSystem.KeyControl)) f.SetValue(kb, new UnityEngine.InputSystem.KeyControl());
        UnityEngine.InputSystem.Keyboard.current = kb;
        UnityEngine.InputSystem.Gamepad.current = null;
        var truck = FarmTruck.Instance; var tractor = Tractor.Instance;
        var bump = NpcBump.Instance;
        T.Check("Bumps: system created, vehicles have a body box from their collider", bump != null && truck.BodyHalfExtents.z > 2f && tractor.BodyHalfExtents.z > 1.5f);
        T.Check("Bumps: the animal pen fence is solid (boxes either side of its gate)",
            AnimalPen.Instance.GetComponentsInChildren<BoxCollider>().Count(c => c.gameObject.name == "Pen Fence") == 5);

        // ---- gates: fence panels on the pads, where vehicles park and over plots come down; the rest stay
        var bay = DeliveryZone.LoadingBay.transform.position;
        var bayFence = FenceLine("Fence Bay Test", bay + new Vector3(-60f, 0f, 1f), bay + new Vector3(60f, 0f, 1f));
        var plot = FarmFields.Instance.Fields[0];
        Transform pt = plot.View.transform;
        float edge = plot.depth * 0.5f + 1f;   // the far side of the field (the pen is on the near side)
        var plotFence = FenceLine("Fence Field Test", pt.position + pt.rotation * new Vector3(-60f, 0f, edge), pt.position + pt.rotation * new Vector3(60f, 0f, edge));
        int opened = VehicleObstacles.OpenGates();
        var bayPanels = bayFence.GetComponentsInChildren<BoxCollider>(true);
        Func<BoxCollider, bool> gate = c => VehicleObstacles.InGate(c.transform.position, 1f);
        T.Check("Gates: fence panels across the loading bay are taken down, the far ones stay",
            bayPanels.Where(c => Vector3.Distance(c.transform.position, bay) < 3f).All(c => !c.gameObject.activeSelf) && bayPanels.Any(c => c.gameObject.activeSelf)
            && bayPanels.All(c => c.gameObject.activeSelf != gate(c)),
            opened + " opened, " + bayPanels.Count(c => c.gameObject.activeSelf) + " stay");
        var plotPanels = plotFence.GetComponentsInChildren<BoxCollider>(true);
        T.Check("Gates: a fence along a field plot gets a gate in front of the plot (only where gates belong)",
            plotPanels.Where(c => Vector3.Distance(c.transform.position, pt.position + pt.rotation * new Vector3(0f, 0f, edge)) < 2.5f).All(c => !c.gameObject.activeSelf)
            && plotPanels.Any(c => c.gameObject.activeSelf) && plotPanels.All(c => c.gameObject.activeSelf != gate(c)),
            plotPanels.Count(c => !c.gameObject.activeSelf) + "/" + plotPanels.Length);
        T.Check("Gates: the animal pen's own fence is never opened", AnimalPen.Instance.GetComponentsInChildren<BoxCollider>(true).Where(c => c.gameObject.name == "Pen Fence").All(c => c.gameObject.activeSelf));
        var bare = new GameObject("Fence Bare"); var barePanel = new GameObject("FencePanel"); barePanel.transform.SetParent(bare.transform, false); barePanel.transform.position = new Vector3(500f, 0f, 500f);
        barePanel.AddComponent<MeshFilter>(); barePanel.AddComponent<MeshRenderer>();
        T.Check("Gates: a scene fence piece without a collider gets one", VehicleObstacles.EnsureFenceColliders() >= 1 && barePanel.GetComponent<BoxCollider>() != null);

        // ---- driving into a fence: the truck stops in front of it (thump, lost speed, a little wear)
        truck.Interact(); FakeEngine.Tick(Dt);
        T.Check("Bumps: in the truck", truck.IsDriving);
        truck.SurfaceOverride = DriveSurface.Asphalt; truck.RefreshSurface();
        Place(truck, new Vector3(300f, 0f, 300f), 0f);
        var wall = FenceLine("Fence Wall Test", new Vector3(292f, 0f, 314f), new Vector3(308f, 0f, 314f));
        int impacts = truck.ImpactCount, thumps = AmbientAudio.Instance.Thumps; float wear = truck.Drive.EngineWear;
        float maxSpeed = 0f;
        Drive(7f, () => { maxSpeed = Math.Max(maxSpeed, truck.Speed); return false; });
        T.Check("Fence: the truck drives up to the fence and stops instead of passing through", truck.transform.position.z > 305f && FrontZ(truck) < 314f - 0.05f + 0.02f,
            "front at " + FrontZ(truck).ToString("F2") + " (fence 314), top speed " + maxSpeed);
        T.Check("Fence: hitting it costs speed (it can't keep driving into it)", Math.Abs(truck.Speed) < 1.5f, truck.Speed + "");
        T.Check("Fence: thump, impact counted and a little wear", truck.ImpactCount > impacts && AmbientAudio.Instance.Thumps > thumps && truck.Drive.EngineWear > wear && truck.Drive.EngineWear < wear + 0.1f,
            truck.ImpactCount + " " + AmbientAudio.Instance.Thumps + " " + truck.Drive.EngineWear);
        T.Check("Fence: the obstacle hit was the fence", truck.LastObstacle != null && truck.LastObstacle.transform.IsChildOf(wall.transform));
        // at an angle it slides along the fence instead of stopping dead
        Place(truck, new Vector3(300f, 0f, 304f), 35f);
        Drive(4f);
        T.Check("Fence: at an angle the truck slides along it, still on this side", FrontZ(truck) < 314f + 0.3f && truck.transform.position.x > 303f,
            truck.transform.position + " front " + FrontZ(truck));
        // other solid props block as well: a palm trunk blocker of the desert dressing
        var palm = DesertProps.DatePalm(null, new Vector3(330f, 0f, 312f), new System.Random(3));
        Place(truck, new Vector3(330f, 0f, 300f), 0f);
        Drive(5f);
        T.Check("Props: a date palm trunk stops the truck too", FrontZ(truck) < 312f - 0.3f + 0.1f && truck.transform.position.z > 304f, FrontZ(truck) + "");
        UnityEngine.Object.Destroy(palm);

        // ---- the field gate stays passable: drive through the opened gap onto the plot
        float plotYaw = pt.eulerAngles.y;
        Vector3 startPos = pt.position + pt.rotation * new Vector3(0f, 0f, edge + 9f);
        Place(truck, startPos, plotYaw + 180f);
        Drive(4f, () => (Quaternion.Inverse(pt.rotation) * (truck.transform.position - pt.position)).z < edge - 3f);
        float lz = (Quaternion.Inverse(pt.rotation) * (truck.transform.position - pt.position)).z;
        T.Check("Gates: the truck drives through the field gate onto the plot", lz < edge - 2f, "local z " + lz + " (fence at " + edge + ") last hit " + (truck.LastObstacle != null ? truck.LastObstacle.name + " " + truck.LastObstacle.transform.position : "-"));
        foreach (var go in new[] { bayFence, plotFence, wall, bare }) UnityEngine.Object.Destroy(go);
        T.SetProp(truck, "Speed", 0f);

        // ---- bumping a villager: knock back, tumble, get up, friendship drops, fine at speed, the truck slows
        var fr = Friendship.Instance;
        var npc = fr.Npcs.ContainsKey("shopkeeper") ? fr.Npcs["shopkeeper"] : Villagers.Instance.Npcs["shopkeeper"];
        fr.Talk("shopkeeper");
        float pts0 = fr.Points("shopkeeper");
        var eco = EconomyManager.Instance; eco.AddMoney(1000);
        int money0 = eco.Money, fines0 = FinanceLedger.Instance.Expense(FinanceLedger.Today, Ledger.Fines);
        var actor = npc.GetComponent<Actor>();
        Vector3 npcPos = new Vector3(340f, 0f, 309f);
        npc.transform.position = npcPos; if (actor != null) { actor.home = npcPos; T.Set(actor, "target", npcPos); }
        bump.Rescan();
        Place(truck, new Vector3(340f, 0f, 300f), 0f);
        T.SetProp(truck, "Speed", 9f);
        int bumps0 = bump.Bumps; float before = 0f, after = 0f;
        kb.wKey.isPressed = true;
        for (int i = 0; i < 150 && bump.Bumps == bumps0; i++) { before = truck.Speed; FakeEngine.Tick(Dt); after = truck.Speed; }
        kb.wKey.isPressed = false;
        T.Check("NPC: driving into a villager bumps them", bump.Bumps == bumps0 + 1 && bump.LastBumped == npc, bump.Bumps + " " + (bump.LastBumped != null ? bump.LastBumped.name : "-"));
        T.Check("NPC: the truck slows down on impact", after < before * 0.75f, before + " -> " + after);
        var react = npc.GetComponent<BumpReaction>();
        T.Check("NPC: their walking pauses while they react", react != null && react.Busy && (actor == null || !actor.enabled));
        FakeEngine.Run(0.5f, Dt);
        Vector3 moved = npc.transform.position - npcPos; moved.y = 0f;
        T.Check("NPC: knocked back, out of the truck's path", moved.magnitude > 1f && Math.Abs(npc.transform.position.x - truck.transform.position.x) > truck.BodyHalfExtents.x + 0.3f, moved + "");
        T.Check("NPC: a cartoon tumble (no ragdoll): on the ground for a moment", react.Tilt > 45f && (react.State == BumpReaction.Phase.Down || react.State == BumpReaction.Phase.GetUp), react.State + " " + react.Tilt);
        T.Check("NPC: says something cross (with an Arabic translation)", NpcBump.VehicleLines.Contains(bump.LastLine) && Loc.HasTranslation(bump.LastLine), bump.LastLine);
        T.Check("NPC: friendship drops a little", pts0 - fr.Points("shopkeeper") > 10f && pts0 - fr.Points("shopkeeper") <= NpcBump.VehicleFriendLoss + 0.01f, pts0 + " -> " + fr.Points("shopkeeper"));
        int fined = money0 - eco.Money;
        T.Check("NPC: a small fine at speed, booked under Fines", fined >= NpcBump.BaseFine && fined <= NpcBump.MaxFine && bump.LastFine == fined && FinanceLedger.Instance.Expense(FinanceLedger.Today, Ledger.Fines) - fines0 == fined, fined + "");
        T.Check("NPC: Fines has an Arabic name in the finances page", Loc.HasTranslation(Ledger.Name(Ledger.Fines)) && Loc.HasTranslation("Fine for reckless driving: {0} SAR") && Loc.HasTranslation("{0} is upset with you"));
        T.SetProp(truck, "Speed", 0f);
        FakeEngine.Run(3f, Dt);
        T.Check("NPC: gets back up and goes on (upright, walking again)", !react.Busy && (actor == null || actor.enabled) && npc.transform.up.y > 0.99f, react.State + " up " + npc.transform.up);
        Vector3 p1 = npc.transform.position;
        FakeEngine.Run(8f, 0.05f);
        T.Check("NPC: pathing resumes (walks back to their spot)", (npc.transform.position - p1).magnitude > 0.3f && (npc.transform.position - npcPos).magnitude < 1.5f,
            p1 + " -> " + npc.transform.position);
        // slow bump: no fine
        int money1 = eco.Money;
        npc.transform.position = new Vector3(360f, 0f, 307.5f); if (actor != null) { actor.home = npc.transform.position; T.Set(actor, "target", npc.transform.position); }
        Place(truck, new Vector3(360f, 0f, 300f), 0f);
        T.SetProp(truck, "Speed", 3f);
        bumps0 = bump.Bumps;
        for (int i = 0; i < 150 && bump.Bumps == bumps0; i++) { T.SetProp(truck, "Speed", Math.Max(truck.Speed, 3f)); FakeEngine.Tick(Dt); }
        T.Check("NPC: bumping someone slowly costs no fine", bump.Bumps == bumps0 + 1 && eco.Money == money1 && bump.LastFine == 0, bump.Bumps + " " + (money1 - eco.Money));
        T.SetProp(truck, "Speed", 0f);
        FakeEngine.Run(4f, Dt);

        // ---- animals: bumped ones hop aside and run off, animals nearby are startled and run too; no fine, no friendship
        var animals = UnityEngine.Object.FindObjectsByType<Actor>(FindObjectsSortMode.None).Where(a => a.IsAnimal && NpcBump.Bumpable(a)).ToList();
        var horse = animals.FirstOrDefault(a => a.name.Contains("horse")) ?? animals.FirstOrDefault();
        var other = animals.FirstOrDefault(a => a != horse && a.mode != ActorMode.FollowPlayer);
        T.Check("Animals: ambient animals can be bumped", horse != null && other != null, animals.Count + "");
        if (horse != null && other != null)
        {
            Vector3 hp = new Vector3(380f, 0f, 309f);
            horse.transform.position = hp; horse.home = hp; T.Set(horse, "target", hp);
            other.transform.position = hp + new Vector3(-3f, 0f, 2f); other.home = other.transform.position; T.Set(other, "target", other.transform.position);
            bump.Rescan();
            int m2 = eco.Money;
            Place(truck, new Vector3(380f, 0f, 300f), 0f);
            T.SetProp(truck, "Speed", 7f);
            bumps0 = bump.Bumps;
            for (int i = 0; i < 150 && bump.Bumps == bumps0; i++) FakeEngine.Tick(Dt);
            T.SetProp(truck, "Speed", 0f);
            var hr = horse.GetComponent<BumpReaction>(); var or = other.GetComponent<BumpReaction>();
            T.Check("Animals: the bumped animal reacts, no fine for animals", bump.LastBumped == horse.gameObject && hr != null && hr.Busy && bump.LastFine == 0 && eco.Money == m2 && bump.LastLine == "!");
            T.Check("Animals: an animal nearby is startled and runs away", or != null && or.Fleeing);
            Vector3 o0 = other.transform.position;
            FakeEngine.Run(0.6f, Dt);
            T.Check("Animals: ...away from the commotion", Vector3.Distance(other.transform.position, hp) > Vector3.Distance(o0, hp) + 1f, Vector3.Distance(o0, hp) + " -> " + Vector3.Distance(other.transform.position, hp));
            bool fled = false; Vector3 h0 = horse.transform.position;
            for (int i = 0; i < 100; i++) { FakeEngine.Tick(Dt); fled |= hr.Fleeing; }
            T.Check("Animals: the bumped animal runs off after hopping aside", fled && Vector3.Distance(horse.transform.position, h0) > 1.5f);
            FakeEngine.Run(4f, Dt);
            T.Check("Animals: back to wandering afterwards", !hr.Busy && horse.enabled && !or.Busy && other.enabled);
        }

        // ---- the player sprinting into a villager: a stagger and a word, a tiny friendship drop, no fine
        T.SetProp(truck, "Speed", 0f); truck.Exit(); FakeEngine.Tick(Dt);
        var player = GameObject.Find("PlayerCapsule").transform;
        var buyer = fr.Npcs.ContainsKey("buyer") ? fr.Npcs["buyer"] : Villagers.Instance.Npcs["buyer"];
        fr.Talk("buyer");
        float bp0 = fr.Points("buyer"); int m3 = eco.Money;
        buyer.transform.position = new Vector3(420f, 0f, 305f); var ba = buyer.GetComponent<Actor>(); if (ba != null) { ba.home = buyer.transform.position; T.Set(ba, "target", buyer.transform.position); }
        player.position = new Vector3(420f, 0f, 300f);
        bump.Rescan();
        FakeEngine.Tick(0.1f);
        bumps0 = bump.Bumps;
        for (int i = 0; i < 20 && bump.Bumps == bumps0; i++) { player.position += new Vector3(0f, 0f, 0.55f); FakeEngine.Tick(0.1f); }
        T.Check("Running: sprinting into a villager bumps them (stagger, a word)", bump.Bumps == bumps0 + 1 && bump.LastBumped == buyer && NpcBump.RunLines.Contains(bump.LastLine) && Loc.HasTranslation(bump.LastLine), bump.LastLine);
        T.Check("Running: friendship drops only a little, no fine", Math.Abs(bp0 - fr.Points("buyer") - NpcBump.RunFriendLoss) < 0.01f && eco.Money == m3, bp0 + " -> " + fr.Points("buyer"));
        var brr = buyer.GetComponent<BumpReaction>();
        T.Check("Running: they only stagger (no tumble)", brr != null && brr.State != BumpReaction.Phase.Down);
        FakeEngine.Run(3f, Dt);
        T.Check("Running: they recover", !brr.Busy);
        bumps0 = bump.Bumps;
        for (int i = 0; i < 20; i++) { player.position += new Vector3(0.1f, 0f, 0f); FakeEngine.Tick(0.1f); }
        T.Check("Running: walking past people doesn't bump them", bump.Bumps == bumps0);

        // ---- wheels: spin with speed, the front pair steers
        truck.Interact(); FakeEngine.Tick(Dt);
        Place(truck, new Vector3(450f, 0f, 300f), 0f);
        var wheels = new Transform[4];
        string[] wn = { "wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right" };
        Vector3[] wpos = { new Vector3(-0.9f, -0.1f, 1.45f), new Vector3(0.9f, -0.1f, 1.45f), new Vector3(-0.9f, -0.1f, -1.4f), new Vector3(0.9f, -0.1f, -1.4f) };
        for (int i = 0; i < 4; i++)
        {
            var w = new GameObject(wn[i]).transform; w.SetParent(truck.transform, false); w.localPosition = wpos[i];
            var tyre = new GameObject("Tyre"); tyre.transform.SetParent(w, false); tyre.transform.localScale = new Vector3(0.3f, 0.9f, 0.9f); tyre.AddComponent<MeshRenderer>();
            wheels[i] = w;
        }
        var oldWheels = truck.wheels;
        truck.wheels = wheels; T.Set(truck, "wheelBase", null);
        FakeEngine.Tick(Dt);
        T.Check("Wheels: front pair found, radius from the tyre", truck.IsFrontWheel(0) && truck.IsFrontWheel(1) && !truck.IsFrontWheel(2) && Math.Abs(truck.WheelRadius(0) - 0.45f) < 0.01f, truck.WheelRadius(0) + "");
        Func<float, float> SpinStep = speed =>
        {
            T.SetProp(truck, "Speed", speed); float s0 = truck.WheelSpin; FakeEngine.Tick(Dt);
            return Mathf.DeltaAngle(s0, truck.WheelSpin);
        };
        float d0 = SpinStep(0f), d2 = SpinStep(2f), d6 = SpinStep(6f);
        T.Check("Wheels: rotate with speed (none when stopped, 3x faster at 3x speed)", Math.Abs(d0) < 0.01f && d2 > 1f && Math.Abs(d6 / d2 - 3f) < 0.2f, d0 + " " + d2 + " " + d6);
        T.Check("Wheels: the rotation is ~v / r (rolling, no slipping)", Math.Abs(d2 - 2f / 0.45f * Mathf.Rad2Deg * Dt) < 0.3f, d2 + "");
        float r2 = SpinStep(-2f);
        T.Check("Wheels: turn backwards in reverse", r2 < -1f, r2 + "");
        Quaternion spinOnly = wheels[0].localRotation;
        T.SetProp(truck, "Speed", 0f);
        kb.dKey.isPressed = true;
        for (int i = 0; i < 40; i++) { FakeEngine.Tick(Dt); T.SetProp(truck, "Speed", 0f); }
        kb.dKey.isPressed = false;
        Quaternion inv = Quaternion.Inverse(truck.transform.rotation);
        Vector3 axleFront = inv * (wheels[0].rotation * Vector3.right), axleBack = inv * (wheels[2].rotation * Vector3.right);
        T.Check("Wheels: front wheels steer visually, back wheels don't", truck.WheelSteer > 15f && Math.Abs(axleFront.z) > 0.25f && Math.Abs(axleBack.z) < 0.02f,
            truck.WheelSteer + " " + axleFront + " " + axleBack);
        foreach (var w in wheels) UnityEngine.Object.Destroy(w.gameObject);
        truck.wheels = oldWheels; T.Set(truck, "wheelBase", null);
        for (int i = 0; i < 40; i++) { truck.StepDrive(0f, 0f, false, Dt); FakeEngine.Tick(Dt); }

        // ---- looks: lamps, brake lights, indicators, beacon, exhaust smoke, dust on sand, livery materials
        var vis = truck.GetComponent<VehicleVisuals>(); var tvis = tractor.GetComponent<VehicleVisuals>();
        T.Check("Looks: both vehicles have head, tail, indicator and reversing lamps and a beacon", vis != null && tvis != null && vis.LampCount >= 10 && tvis.LampCount >= 10, (vis != null ? vis.LampCount : 0) + "");
        T.Check("Looks: a pitch / roll body pivot carries the model, wheels and cargo stay on the chassis", truck.transform.Find("Body") != null && truck.cargoRoot.IsChildOf(truck.transform.Find("Body")));
        Place(truck, new Vector3(450f, 0f, 300f), 0f);
        FakeEngine.Tick(Dt);
        T.Check("Looks: headlights on while driving, brake lights off when cruising", vis.HeadlightsOn && !vis.BrakeLightsOn);
        float idle = vis.SmokeRate;
        kb.wKey.isPressed = true; for (int i = 0; i < 20; i++) FakeEngine.Tick(Dt); kb.wKey.isPressed = false;
        T.Check("Looks: exhaust smoke thickens when accelerating", vis.SmokeRate > idle + 5f && idle > 0f, idle + " -> " + vis.SmokeRate);
        T.Check("Looks: the nose lifts a little when accelerating", vis.Pitch < -0.2f && vis.Pitch > -3f, vis.Pitch + "");
        T.SetProp(truck, "Speed", 8f);
        kb.spaceKey.isPressed = true; FakeEngine.Tick(Dt); FakeEngine.Tick(Dt); kb.spaceKey.isPressed = false;
        T.Check("Looks: brake lights when braking", vis.BrakeLightsOn);
        truck.SurfaceOverride = DriveSurface.Sand; truck.RefreshSurface();
        T.SetProp(truck, "Speed", 8f); FakeEngine.Tick(Dt);
        float dustSand = vis.DustRate;
        truck.SurfaceOverride = DriveSurface.Asphalt; truck.RefreshSurface();
        T.SetProp(truck, "Speed", 8f); FakeEngine.Tick(Dt);
        T.Check("Looks: dust trail on sand, none on asphalt", dustSand > 5f && vis.DustRate == 0f, dustSand + " / " + vis.DustRate);
        bool ind = false;
        T.SetProp(truck, "Speed", 3f);
        kb.dKey.isPressed = true;
        for (int i = 0; i < 60; i++) { FakeEngine.Tick(Dt); ind |= vis.IndicatorRightOn; if (vis.IndicatorLeftOn) { ind = false; break; } }
        kb.dKey.isPressed = false;
        T.Check("Looks: the right indicator blinks when turning right", ind);
        T.Check("Looks: a beacon flashes while driving", Enumerable.Range(0, 60).Select(_ => { FakeEngine.Tick(Dt); return vis.BeaconOn; }).Distinct().Count() == 2);
        T.SetProp(truck, "Speed", 0f); truck.SurfaceOverride = null; truck.Exit();
        T.Check("Looks: lamps go dark when you get out", Enumerable.Range(0, 3).Select(_ => { FakeEngine.Tick(Dt); return vis.HeadlightsOn || vis.BrakeLightsOn || vis.BeaconOn; }).Last() == false);
        T.Check("Bumps: no errors", FakeEngine.Errors.Count == e0, string.Join(" | ", FakeEngine.Errors.Skip(e0).Take(3)));
        UnityEngine.InputSystem.Keyboard.current = null;
    }
}
