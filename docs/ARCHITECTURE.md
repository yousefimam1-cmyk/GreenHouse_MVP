# Mazraa — Code Architecture

Status: October 2026, branch `mazraa-dev` after the open-world, graphics, movement, online co-op and QA merges.
~215 C# files, ~45,000 lines under `Assets/Scripts`, no assembly definitions (one assembly), Unity 6000.3.10f1, URP 17.3,
Input System 1.18, Netcode for GameObjects 2.11 (compiled only with `MAZRAA_NETCODE`), TextMeshPro.
Tests: `Tests/Headless/run.sh` (mono + a fake UnityEngine, 1,789 checks incl. 62 Core) and `python3 Tests/static_tests.py .` (34 checks).

This document has three parts: **what is there** (code map), **what hurts** (problems), **where we go** (target
architecture and a staged refactor plan that runs next to feature work). The code added with this document lives in
`Assets/Scripts/Core` and is the first stage.

---

## 1. Code map

### 1.1 Boot sequence
1. Scene `Blak_Pulse_Main` loads with the greenhouses, player (StarterAssets `FirstPersonController`), UI canvas and terrain.
2. `GameManager.Bootstrap()` (`RuntimeInitializeOnLoadMethod(AfterSceneLoad)`) creates `GameManager (auto)` if a `GreenhouseManager` is in the scene.
3. `GameManager.Awake` adds the fixed core components by hand (HUD, Scenario, Economy, Mission, VehicleSpawner, GraphicsEnhancer, FeedbackFX, ShopUI, WorldClock, FieldManager, MarketManager, ObjectiveMarker, StoryIntro, DayNightCycle, AmbientLife) and then `AddFeatureSystems()` reflects over the assembly for every `[MazraaSystem(order)]` MonoBehaviour (69 today) and adds them in order (−95 TickDriver, −90 GamepadBridge, −50 LoadingScreen … 96 SettingsApplier).
4. Each system reads its state from `SaveData` in `Awake`/`Start`, subscribes to `GameEvents`/static events in `OnEnable`, builds its own UI with `MenuKit`/`UiKit`/`Build`, and polls in `Update`.
5. `LoadingScreen` hides the world until the open world (`OpenWorld`, order 68) has built terrain tiles, roads, places, town (`WorldTown`) and land.
6. Statics are reset for domain reloads through `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` methods on every static holder (`GameEvents`, `SaveData`, `Loc`, `Regions`, `Dlc`, `ShopCatalog`, `HudLines`, `WorldClock` events, `Signals`, `TickScheduler`, `ContentPacks`, `SaveMigrations`…).

### 1.2 Folders and responsibilities

| Folder | Files | What lives there | Owner today |
|---|---|---|---|
| `Base` | 10 | `SaveData` (JSON key/value save with batching, backup, co-op session map, schema version), `GameEvents` (static event bus, 19 events), `DataPacks` + `[MazraaSystem]` + `ShopCatalog` + `HudLines` (extension points), greenhouse `Cropbed`/`Module`/`ControllStation`, `Interactable` base | core |
| `Core` | 5 | **new**: `TickScheduler`/`TickDriver` (rate-based update fan-out), `Signals` (typed events), `SaveMigrations` (schema steps), `ContentPacks` (region/DLC manifests) | architecture |
| `Managers` | 17 | `GameManager` (bootstrap + harvest totals), `EconomyManager` (money, goods, seeds, animals, shop), `MarketManager` (buyers, sell points, price curves), `MissionManager`, `CampaignManager`, `AchievementManager`, `GameModeDirector`/`GameModes`/`Career`, `Regions`/`Dlc`/`RegionContent`, `ScenarioManager`, `UIManager` (greenhouse panels), `AudioManager` | economy / story helpers |
| `Farming` | 14 | `WorldClock` (calendar, weather), `FieldPlot`/`FieldManager` (open fields state machine), `SoilManager`/`SoilModel` (N/P/K, pH, EC, water balance, growth factors), `AnimalPen`/`FarmAnimal`/`PenTask`, `AmbientLife`, `Actor`, `NpcBump`/`BumpReaction`, `ShopStall` | farming core (FarmCore replaces/wraps `FieldPlot`+`SoilManager`) |
| `FarmCore` | — | not yet on this branch: cell-grid fields painted by implements, growth model, irrigation network (sources, pumps, pipes, pivots, drip, flood, sensors, schedules), jobs (another helper; see docs/PLAN_LIVING_WORLD.md) | farming core |
| `Crops` | 4 | `CropCareManager` (fertility, rotation, pests), `OrchardManager`/`TreeSlot`, `CropsData` | farming |
| `Vehicle` | 9 | `Vehicle` (base: enter/exit, physics, fuel, wear), `Tractor`, `FarmTruck`, `Drivetrain`, `DriveAssists`, `VehicleObstacles`, `VehicleVisuals`, `VehicleSpawner` (builds the market pad, stalls, vehicles), `DeliveryZone` | movement / vehicles helper |
| `World` | 15 | `OpenWorld` (terrain tiles, roads, places, culling), `WorldLayout` (road graph, POIs), `WorldShape`/`WorldMesh`/`WorldBuildings`/`WorldRim`, `WorldTown` (street grid, 45 buildings, shops), `TownCar`/`TownWalker`/`Walkable`, `WorldLand`/`WorldLandSign` (land for sale), `WorldMap` (map, waypoints, fast travel), `StuckRescue` | world helper |
| `Graphics`, `Graphics/Look` | 9 + 9 | `GraphicsEnhancer` (URP volume, quality presets), `DesertDressing`/`DesertProps` (props, trees), `DesertWeatherFX`, `DayNightCycle`, `CropVisuals`/`CropbedVisuals`, `FeedbackFX`, `ProcMesh`; Look: `LookLibrary` (PBR material set), `GroundLook`, `Vegetation`/`FoliageMeshes`/`FoliageGrass`, `NpcWardrobe`, `ModelLook`, `AtmosphereLook`, `BoneFollower` | graphics helper |
| `Input` | 9 | `GameInput` (device abstraction), `GameSettings`/`GameSettingsOptions`/`SettingsApplier`, `GamepadBridge`, `MenuNav`, `Glyphs`, `ThirdPersonView`, `MoveFeel` (FS25-style on-foot feel) | movement helper |
| `Life` | 7 | `PlayerLife` (stamina, carrying, needs), `Carriable`, `HerdManager`, `PenBoard`, `LifeUI`, `LifeModels`, `PropSpinner` | movement/life helper |
| `UI` | 24 | `GameHUD`, `GameMenu` + `GameMenuFrontEnd` + `GameMenuSettings`, `FarmInfoScreen`, `MainMenuScene`, `SaveSlots`, `ShopUI`, `EconomyPages`, `Minimap`/`MinimapIcon`, `ObjectiveMarker`, `LoadingScreen`/`LoadingOverlay`, `MenuKit` (widgets), `UiKit` (9-slice art, icons, glyphs), `TouchControls`, `TutorialOverlay`, `ControlsTutorial`, `StoryIntro`, `UiText`, `Billboard`, `InteractionPromptUI` | FS25 HUD/menus + release UI helpers |
| `Multiplayer` | 11 | `NetSession` (`INetBackend`, `LocalBackend`), `NetcodeBackend` (`#if MAZRAA_NETCODE`), `OnlineServices`/`UgsOnlineServices` (Relay, lobby), `NetProtocol` (text wire format, `NetSyncState` capture/apply), `MultiplayerManager` (host-authoritative requests, 1 Hz state, 10 Hz positions, emotes, quick chat, gifts), `MultiplayerPanel`, `RemotePlayer`, `PlayerNames`, `NetIds`, `ChatSafety` | co-op helper |
| `Cozy`, `FarmLife`, `Fish`, `Finance`, `Utilities`, `Events`, `Learn`, `Sponsors`, `Audio` | 13+12+8+7+6+3+2+1+4 | feature systems added as `[MazraaSystem]`s: pets, decorations, villagers, daily quests, seasonal events, collection book; farm level, mastery, beauty, friendship, festivals, photo mode, roadside stand; fishing, aquaculture, hives, dovecote, processing yard, Saudi animals; bank, land office, workers, ledger; utility grid; event director + modifiers; learn mode + analytics; sponsors; procedural and ambient audio | feature helpers |
| `Localization` | 2 | `Loc` (EN keys → AR, data packs `Data/Loc/*.json`, font swap), `ArabicShaper` | core |
| `Helper`, `Controllers`, `Data` | 3+2+1 | `Build` (UI builders), `LocalizedLabel`, `InteractionController` (raycast + prompt), `WellController`, `CropProfiles` | core |

### 1.3 Data
`Assets/Resources/Data/*.json` (economy, market, soil, regions, missions, campaign, events, quests, farmlife, fish, processing, scenarios, sponsors, crop_profiles) plus pack folders `Data/{Economy,Loc,Missions,Campaigns,Events,Quests,Learn}/NN_name.json` loaded in name order by `DataPacks.Load`. Region gating is per item (`regions: [...]` arrays checked with `Regions.Allows`/`HasContent`). New: `Data/Packs/*.json` manifests read by `ContentPacks`.

### 1.4 Save
`SaveData`: one JSON file `mazraa_save.json` (+ `.bak`, `.tmp`, `.corrupt`), PlayerPrefs-style typed get/set, dirty flag flushed every 2 s by `SaveFlusher`, atomic write with backup (QA pass), co-op client session map (`BeginSession/EndSession`), slots copied by `SaveSlots`/`Career`. 154 distinct `mazraa.*` keys are written by ~60 systems. New: `SchemaVersion = 2`, `LoadedVersion`, migration hook calling `SaveMigrations.Apply` on load.

### 1.5 Events
`GameEvents` (19 static `Action` events + `Custom(string, float)` as the catch-all for new features), `WorldClock.DayStarted/SeasonChanged/WeatherChanged`, `FieldPlot.FieldPlanted/FieldHarvested`, `Loc.OnLanguageChanged`, plus ~25 per-class static events. New: `Signals.Of<T>()` typed signals with disposable subscriptions.

### 1.6 Co-op
Host-authoritative. Clients send requests (interact, buy, sell, work); the host applies them to the real systems and broadcasts `NetSyncState` once a second (money, storage, calendar, weather, mission, well, field plots) which clients `Apply` into their systems; positions are owner-authoritative at 10 Hz; `SaveData.BeginSession` protects the client's save. Everything is plain strings over `INetBackend` so it runs in the headless tests (`OnlineTests`).

### 1.7 Tests
`Tests/Headless/FakeUnity.cs` (+ `FakePkg`, `FakeLook`) implements enough of UnityEngine to compile the whole game and run lifecycle, coroutines, `Invoke`, `Resources` from disk, `JsonUtility` via Newtonsoft and a tiny rigidbody. `GameTests.Main` builds a scene like `Blak_Pulse_Main`, boots `GameManager` and runs suites in sequence (Farm, Fish, Utilities, Events/Finance, Cozy, UI, Driving, Soil, Online, Feel, QA, Bump, FrontEnd, Look, World, **Core**). `static_tests.py` guards Unity-only rules the fakes cannot (meta files, class/file names, translations, fake-only members, read-only property writes, editor API, JsonUtility-serialisable types, static-event unsubscription, FixedUpdate rules, atomic saves).

---

## 2. Problems (ranked by cost)

1. **77 `Update()` loops, no shared scheduler.** Each system polls on its own, many at full frame rate for things that change once a second (prices, soil, culling). The frame budget is invisible and un-enforceable; phase-aligned work (everything "every 1 s" firing on the same frame) causes hitches. → `TickScheduler` (stage 1, done; adoption stage 2).
2. **God classes.** `SoilManager` (935 lines), `OpenWorld` (778), `MultiplayerManager` (716), `MarketManager` (626), `DesertDressing` (610), `Vehicle` (591), `WorldLayout` (584), `EventDirector` (522), `GameHUD` (505), `CropCareManager` (503). They mix simulation, persistence, UI text and world building. Several are owned by helpers right now, so they are refactored by *extraction*, not rewrite (stage 3).
3. **81 static `Instance` singletons + direct `Instance` lookups everywhere (900+ call sites).** Any order change at boot or a missing system is a null reference; tests have to null every `Instance` by reflection (`Program.ResetStatics`). Target: a `Services` locator with registration from `[MazraaSystem]` and null-safe accessors, then constructor-style injection for pure logic classes.
4. **Duplicated logic.** Field growth exists in `FieldPlot` (growSeconds) *and* `SoilModel` (temperature/water factors); prices in `EconomyManager` *and* `MarketManager`; two interaction prompt paths (`Interactable` vs `IInteractable` + `InteractionPromptUI`); three "place a label" helpers (`Build`, `MenuKit`, `UiKit`); region look applied by `DesertDressing`, `DesertWeatherFX`, `GraphicsEnhancer` and `AtmosphereLook` separately; NPC walkers exist as `AmbientLife` actors, `Villagers`, `TownWalker` and neighbour-farm workers in `OpenWorld`. FarmCore must become *the* field model and `FieldPlot` a view of it.
5. **Static mutable state outside singletons.** `FieldPlot.PreferredSeed`, `GameMenu.ShowOnStart`, `EventDirector.RandomEventsEnabled`, `FarmLife.PassiveBonuses`, `Dlc.UnlockAllInDevBuilds`, `LoadingScreen.Visible`… Tests set them; production code reads them from anywhere. Keep as explicit `GameConfig`/`DebugFlags` objects.
6. **Coupling through strings.** `GameEvents.Custom("harvest_" + crop, kg)`, mission types as strings matched in three files (`MissionManager`, `ObjectiveMarker`, data), Loc keys as English sentences (fine for UI, fragile for logic). → typed `Signals` payloads; mission types as an enum-backed registry.
7. **Save keys are ad hoc.** 154 `mazraa.*` keys, no schema, no version until now; unit or key changes silently break old saves. → `SchemaVersion` + `SaveMigrations` (done), then per-system `ISaveSection` blocks (stage 4).
8. **UI built in code per system** (every manager builds its own panel with `Build`/`MenuKit`). Fine for speed, but RTL/mirroring, controller focus and theming are re-solved each time. The FS25 menu helper is consolidating on `MenuKit` + `UiKit`; new screens must use only those.
9. **Fakes diverge from Unity.** The fake `JsonUtility` is Newtonsoft (accepts properties/dictionaries), `Destroy` does not call `OnDestroy`, physics is a toy, `Resources.Load` only knows TextAssets/models/look, `Application.isEditor` is true. The static tests now catch fake-only members, read-only writes and JsonUtility shapes; still, anything about rendering, physics, input and UI layout must be verified in the editor. Rule: a feature is done when its headless test passes **and** a 2-minute editor checklist is ticked (ROADMAP item W2-14 adds an EditMode smoke test for each helper's area).
10. **Boot is order-sensitive and slow.** Hand-written `AddComponent` chain + reflection over all types; `Start()` work (building the town, trees, 24 terrain tiles) is synchronous behind `LoadingScreen`. → async build steps registered with a `BootStep` list and a progress contract (stage 3).
11. **Networking is request/snapshot over strings** (good for tests, 1 Hz state is enough for farm data) but every new state field must be added in `NetSyncState.Capture/Apply` by hand. → systems expose an `INetReplicated` section; the protocol serialises sections generically (stage 4).
12. **No performance budget enforcement.** No frame-time test, no counts of active objects. `TickDriver` now measures scheduled logic per frame; W2-13 extends this to object counts and draw-call estimates in the fakes.

---

## 3. Target architecture

```
                 ┌──────────────────────────────────────────────────────────────┐
                 │  Boot: GameManager.Bootstrap → Services.Register([MazraaSystem]) │
                 │        → BootSteps (async, progress) → LoadingScreen hides     │
                 └──────────────────────────────────────────────────────────────┘
   Data packs ─► ContentPacks (manifests) ─► DataPacks (filtered by active packs) ─► typed data classes
                                                                                  │
   Save file ─► SaveData (kv + SchemaVersion) ─► SaveMigrations ─► ISaveSection per system (stage 4)
                                                                                  │
   ┌──────────── Simulation (pure C#, no MonoBehaviour, headless-testable) ─────────────────────────┐
   │ Calendar  Weather  FarmCore(fields, irrigation, soil)  Economy/Market  Animals  Jobs  NPC schedules │
   └───────────────────────────────▲──────────────────────────────────────────────────────────────────┘
                                   │ TickScheduler (Frame / 0.25 s / 1 s / Daily)    Signals<T> (typed events)
   ┌───────────────────────────────┴──────────────────────────────────────────────────────────────────┐
   │ Presentation (MonoBehaviours): World build, Vehicles(physics), Player, NPC bodies, VFX, Audio, UI │
   └──────────────────────────────────────────────────────────────────────────────────────────────────┘
                                   │ INetReplicated sections ─► NetProtocol ─► INetBackend (Netcode / Local)
```

Principles
- **Simulation is plain C#** with explicit inputs (dt, day, weather) and outputs (signals). MonoBehaviours only read it and render. This is what makes the headless tests honest and co-op sync generic.
- **One scheduler.** Nothing new gets an `Update()` unless it moves a transform every frame. Logic subscribes to `Ticks.EverySecond/EveryQuarter/EveryDay` with an owner, so it stops when the owner dies.
- **Typed events.** New events are `struct` payloads raised through `Signals`; `GameEvents` stays for existing subscribers and gets bridged (stage 2) so both work until the old one is removed.
- **Content is packs.** A region or seasonal drop is a manifest + prefixed data files + tags. Code never checks `Regions.IsEgypt` for content; it asks the data.
- **Save has a schema.** A key rename is a migration step, bumped version, and a test with a v(n−1) file.
- **Performance is a test.** Budgets in `docs/GDD.md` §15 are asserted: scheduler ≤ 2 ms, active NPC/vehicle counts within range, no `Update()` added without a reason in the file header (static test W2-13).

### 3.1 Services locator (stage 2)
```csharp
public static class Services {
    public static T Get<T>() where T : class;          // null-safe, logs once when missing
    public static bool TryGet<T>(out T s) where T : class;
    public static void Register<T>(T instance);        // called by GameManager for every [MazraaSystem] and core component
}
```
`XManager.Instance` stays as a thin wrapper (`=> Services.Get<XManager>()`) during migration; tests reset one dictionary instead of 81 fields.

### 3.2 FarmCore contract (for the helper landing it)
- Pure classes: `FieldGrid` (cells painted by implements; `Field` = connected worked cells, area ha, soil `SoilState` from `SoilModel`, crop state, moisture, salinity), `FieldSim.Advance(field, dtHours, weather)`, `IrrigationSystem` (method, efficiency, source, schedule), `JobQueue` (`Job`, `IJobWorker`).
- Subscribes with `Ticks.EverySecond` (growth, water) and `Ticks.EveryDay` (bills, salt, soil), raises `FieldWorkedSignal`, `StorageChangedSignal`, `MoneyChangedSignal` and the legacy `GameEvents.Raise*` through the bridge.
- `FieldPlot` becomes the MonoBehaviour view/interaction surface of a `Field`; `SoilManager` keeps the data but delegates ticks.
- Save: one `ISaveSection` "farmcore" (JSON blob under one key) instead of dozens of keys; migration step converts existing `mazraa.field.*` keys.

---

## 4. Staged refactor plan (runs next to feature work)

| Stage | Scope | Who | Risk | Done when |
|---|---|---|---|---|
| **1 (this commit)** | `Core/`: `TickScheduler`+`TickDriver`, `Signals`, `SaveMigrations` + `SaveData.SchemaVersion`, `ContentPacks` + manifests; `CoreTests` | architecture | none (new files, 3 marked hooks) | tests pass; this doc |
| **2** | Adoption without behaviour change: (a) bridge `GameEvents` → `Signals` (one adapter file raising the typed payloads from the old events); (b) move "once a second / quarter" polls of **unowned** systems (`Cozy`, `FarmLife`, `Finance`, `Fish`, `Utilities`, `Sponsors`, `Audio`) to `Ticks`; (c) `Services` locator with `Instance` wrappers; (d) `DataPacks.Load` filters files by `ContentPacks.Active` prefixes (feature flag, default on, falls back to all files when no manifest claims a file) | 1 helper (W2-11) | low | `grep -c "void Update()"` drops below 45; no test changes except new ones |
| **3** | Extraction from god classes owned by helpers *after their waves land*: `SoilManager` → `SoilSim` (pure) + `SoilManager` (view); `MarketManager` → `PriceModel` + buyers; `OpenWorld` → `BootSteps` (async, progress); `EventDirector` → `EventSim`; `Vehicle` → `VehicleBody` (physics) + `VehicleSeat` (enter/exit) + `VehicleHud` | owners of each file | medium | each extracted pure class has its own headless suite; file sizes < 400 lines |
| **4** | Save sections (`ISaveSection` JSON blobs per system, migration v3 converting flat keys); `INetReplicated` sections used by `NetSyncState`; mission types registry | core + co-op helper | medium | v2 save loads into v3 with identical state (golden-file test); `NetSyncState.Capture/Apply` has no hand-written field list |
| **5** | Assembly definitions (`Mazraa.Sim`, `Mazraa.Game`, `Mazraa.Net`, `Mazraa.Tests`) so pure simulation cannot reference UnityEngine; Burst/Jobs for field and traffic simulation if profiling says so | architecture | low–medium | `Mazraa.Sim` has no `using UnityEngine` |

Rules during the refactor
- Never rewrite a file another helper owns this wave; add a marked hook (`// [Core]`) of ≤ 5 lines or a new file.
- Every stage lands with tests (headless + static) and a note in this document's §1 code map.
- Behaviour changes (numbers, UX) go through `docs/GDD.md`, not through refactors.

---

## 5. How to use the Core pieces (for helpers)

```csharp
// Tick: replace an Update() that polls once a second
private TickHandle tick;
private void OnEnable()  { tick = Ticks.EverySecond(dt => Recompute(), this, "MarketManager.prices"); }
private void OnDisable() { tick?.Dispose(); }
// daily work (bills, soil): Ticks.EveryDay(day => ChargeWater(day), this);

// Signals: raise a typed event; subscribers hold a token
Signals.Raise(new MoneyChangedSignal { Balance = money, Delta = +500, Reason = "sell:wheat" });
private readonly Subscriptions subs = new Subscriptions();
private void OnEnable()  { subs.Add(Signals.On<MoneyChangedSignal>(OnMoney, this)); }
private void OnDisable() { subs.Dispose(); }

// Save migration: rename a key when you change it, bump SaveData.SchemaVersion
SaveMigrations.Register(2, s => SaveMigrations.Rename(s, "mazraa.water", "mazraa.water.m3", 1));

// Content packs: is Egypt content active? which pack owns a file?
if (ContentPacks.IsActive("nile_delta")) ...
var owner = ContentPacks.OwnerOfFile(textAsset.name);   // null = base
```

Headless test entry points: `CoreTests.Run()` (called from `GameTests.Main`); add your suite file to `run.sh` and call it from `Main` — one line each, the same way.
