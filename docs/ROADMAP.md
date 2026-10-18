# Mazraa — Roadmap and helper backlog

Each item is sized for **one helper, one wave** (roughly one working session on a worktree branched from `mazraa-dev`),
has acceptance criteria that a reviewer can check, and test ideas for the headless harness (`Tests/Headless`) and the
static checks (`Tests/static_tests.py`). Items reference `docs/GDD.md` (design) and `docs/ARCHITECTURE.md` (code rules).

Ground rules for every item
- New logic subscribes to `Ticks.*` (no new `Update()` unless it moves a transform every frame) and raises `Signals` payloads; legacy `GameEvents.Raise*` only where an existing subscriber needs it.
- Content goes in data packs with the pack's prefix (`ContentPacks`), bilingual EN/AR, Egyptian Arabic for Delta dialogue.
- Verify in the Unity editor too: the fakes do not render, simulate physics or lay out UI.
- Don't rewrite files another helper owns in the same wave; marked hooks (`// [W2-xx]`) of a few lines are fine.

Priority: P0 = needed for the "GTA feel" and "fun with friends" pillars at beta; P1 = beta; P2 = release; P3 = post-release.

---

## Wave 2 (now, in parallel)

### W2-01 · P0 · Living traffic with schedules
**Goal**: GDD §10.3 / §13.3. A lane graph from `WorldLayout` roads; 20–40 AI vehicles within 400 m; time-of-day density; intersections; following/braking; pull-over on horn; drivers get out and complain after a bump then drive on; Delta microbuses and tuk-tuks, Najd pickups and tankers; shepherd-crossing event.
**Acceptance**: a vehicle passes the farm gate at least every 60 s between 07:00 and 19:00; no NPC vehicle despawns within 60 m of a player; no AI car intersects another for > 0.5 s; player cutting in triggers brake + horn within 300 ms; AI drivers obey `TickScheduler` budget (traffic ≤ 0.6 ms/frame in the fake).
**Tests**: headless 10-day run counts gate passes per hour; collision-pair scan each second; horn/brake reaction frame count; `TickDriver.OverBudgetFrames == 0`.
**Touches**: new `Assets/Scripts/Traffic/*`; hook in `WorldLayout` to expose lanes; `TownCar` becomes a traffic client.

### W2-02 · P0 · Pedestrians and NPC schedules
**Goal**: GDD §10.1 / §13.4. `npcs.json` per region (120+ named NPCs: home, job, family, schedule with prayer-time pauses, Friday noon closures); one `NpcSim` (pure C#) drives `TownWalker`/`Villagers`/neighbour workers; reactions: look at the player at 2 m, greet (text bark, region dialect), flinch on bump (existing `NpcBump`), gather at festivals, run from haboob.
**Acceptance**: 15–40 pedestrians in town by day, 2–6 at night; 95 % of schedule stops reached on time over 10 simulated days; greeting within 500 ms at 2 m; no NPC stuck > 10 s (watchdog teleport + log).
**Tests**: schedule simulation over 10 days (pure sim, no scene); counts by hour; stuck-watchdog triggers 0 times in a 3-day run; static test: every bark key has Arabic.

### W2-03 · P0 · GTA-like vehicle feel, cameras, enter/exit
**Goal**: GDD §8.1, §8.4, §15. Weight transfer, suspension, gearbox, trailer hitch joint, reset-to-road, steering assist at speed; chase camera spring-arm 0.12 s with collision pull-in, speed-FOV 60→75; enter/exit 0.6 s skippable at 0.3 s, both doors, engine stays running on quick exit; horn, lights, indicators.
**Acceptance**: pickup 60→0 km/h in < 30 m; tractor + loaded trailer stable on 15 % grade; reset in 1.5 s always available; enter/exit never leaves the player inside geometry (10 positions tested); input-to-steer ≤ 3 frames.
**Tests**: fake-rigidbody braking distance and grade tests (`DrivingTests` style); frame-count on steer input; enter/exit position sanity; editor physics test scene checklist in the PR.
**Touches**: `Vehicle`, `Drivetrain`, `DriveAssists`, `ThirdPersonView` (owner: this helper).

### W2-04 · P0 · Interaction layer
**Goal**: one `Interaction` system (replaces the two prompt paths): targets register `IInteractable` with verb, hold time, availability reason; context prompt ≤ 100 ms; range 2.2 m / 60°; hold-to-confirm for destructive actions; controller glyphs via `Glyphs`; works for players and is reused by workers (`JobQueue` calls the same `Perform`).
**Acceptance**: every existing interactable (beds, well, station, plots, pens, stalls, vehicles, signs, land) works through the new layer; prompt text passes the Loc static test; no duplicate prompt UI; co-op clients' interactions arrive as requests unchanged.
**Tests**: existing suites keep passing (they exercise prompts via `InteractionPrompt`); new: prompt latency in frames; availability reasons bilingual.

### W2-05 · P0 · AI workers on FarmCore fields
**Goal**: GDD §9.5 / §13.1–13.2. `JobQueue` shared by players, workers and co-op friends; field path planning (headlands, passes at implement width, turn radius, refill trips); worker skill/fatigue/wages; farm board UI listing jobs; workers go home at night and Friday noon.
**Acceptance**: a hired worker plows, sows and harvests a FarmCore field end-to-end without player input; never stuck > 10 s; wages charged daily via `Ticks.EveryDay`; a co-op friend can claim a queued job; job progress replicated.
**Tests**: headless 3-day run with 2 workers and 3 fields; coverage ≥ 98 % of the field's cells; wage ledger equals days × wage; `FieldWorkedSignal` raised per completed pass.
**Depends on**: FarmCore landing (cell-grid fields, implement widths). Companion plan: `docs/PLAN_LIVING_WORLD.md`.

### W2-06 · P0 · Co-op in five minutes + roles + ping
**Goal**: GDD §12.1. Title → "Play with friends" → room code (6 chars) and store invite; joiner spawns beside host with a pickup; 30-second co-op card; roles (Farmer/Driver/Herder/Shopkeeper/Engineer) switchable at the majlis tent with +10 % role speed and HUD focus; world ping marker (RB/middle mouse) and 3 quick messages; shared daily farm goal.
**Acceptance**: 95 % join success within 2 minutes in LAN and Relay tests; role switch replicated < 1 s; ping visible to all within 200 ms; the shared goal counts both players' deliveries; no save corruption for the client (QA suite).
**Tests**: `OnlineTests` additions with two fake backends: join flow states, role replication, ping message, goal progress from two clients.

### W2-07 · P0 · Friendly competitions pack 1 (race, harvest contest, tractor pull)
**Goal**: GDD §12.2. Host starts an activity from the racetrack board / majlis; opt-in; ring-road race (3 laps, checkpoints, matched class, best-lap ghost), harvest contest (same crop/field size, 5 in-game minutes), tractor pulling (rope joint on the dirt lot). Results on the farm notice board; rewards are cosmetics/titles/XP only.
**Acceptance**: all three run with 2–4 players and with 1 player + AI ghost; checkpoints cannot be skipped; results replicated and persisted on the host; no money rewards; clip-mode trigger on race finish.
**Tests**: headless race state machine with two fake players (checkpoint order, lap count, finish ordering); harvest contest scoring from `FieldWorkedSignal`; pull winner decided by rope displacement sign.

### W2-08 · P0 · Clip mode, trendy moments, creator codes
**Goal**: GDD §12.3. 15-second rolling capture (Unity `ScreenCapture` frames → mp4 via platform encoder or image sequence fallback) with one key (F9 / LB+RB); auto-triggers (airtime > 1 s, 3 bumps in 10 s, 100 kg in 30 s, race finish) opt-in; clips to `Videos/Mazraa` with region watermark and open-folder button; farm layout export/import as a 12-char code + file (buildings, decorations, fields) with checksum.
**Acceptance**: clip saved within 3 s of the key without a frame hitch > 33 ms; triggers fire at most once per 30 s; a layout code round-trips (export → new farm → import → identical placements); codes never contain profanity (ChatSafety on the alphabet).
**Tests**: trigger detector unit tests on synthetic telemetry; layout serializer round-trip; code alphabet filter.

### W2-09 · P0 · Daily/weekly goals, Harvest Pass, real-calendar events
**Goal**: GDD §12.4. 3 daily + 3 weekly goals from a pool (data); season track of 30 tiers unlocked by play tokens only; real-date events switched on by the system clock with pack content: Ramadan nights, Eid al-Fitr/al-Adha (sheep demand ×1.8), date harvest festival (Aug–Oct), Sham el-Nessim (Delta), canal cleaning week (Aug), national days; "welcome back" neighbour harvest after ≥ 14 days away.
**Acceptance**: no real-money or timer mechanics; missed tiers return next year; goals never require co-op (co-op variants are bonus); events bilingual; Hijri dates computed (Umm al-Qura table or algorithmic) with a manual override in settings.
**Tests**: goal generator determinism per day seed; pass progression math; date → event mapping for 2026–2030; welcome-back grant once per absence.

### W2-10 · P1 · Data patch to GDD agronomy tables
**Goal**: bring `economy.json`, `Economy/30_crops.json`, `Economy/93_egypt.json`, `soil.json`, `market.json` to GDD §6–§9 (sowing windows, days, yields, water needs, prices, animals, chains) with `DATA_SOURCES.md` updated for Egypt (CAPMAS, Ministry of Supply, FAO).
**Acceptance**: every crop has season windows, `days`, `yieldKg`, `waterMm`, `priceBase`, bilingual names; static test for consistency extended (water need within FAO range per crop id); existing gameplay tests retuned where exact numbers changed.
**Tests**: static JSON schema check; headless growth: wheat sown in winter reaches Ready in the right number of in-game days at the 5-day season setting.

### W2-11 · P1 · Core adoption (architecture stage 2)
**Goal**: ARCHITECTURE §4 stage 2: GameEvents→Signals bridge file; move once-a-second polls of unowned systems (Cozy, FarmLife, Finance, Fish, Utilities, Sponsors, Audio) to `Ticks`; `Services` locator with `Instance` wrappers; `DataPacks.Load` filtering by `ContentPacks` prefixes behind a flag.
**Acceptance**: `grep -c "void Update()"` under `Assets/Scripts` ≤ 45; all suites pass unchanged; `Program.ResetStatics` shrinks to resetting `Services`; a file with an unclaimed prefix still loads (fallback).
**Tests**: new static test "no new Update() without `// update-ok:` reason comment"; bridge test: raising `GameEvents.RaiseMoneyChanged` delivers `MoneyChangedSignal`.

### W2-12 · P1 · DLC pipeline
**Goal**: GDD §21, ARCHITECTURE §3. A `Tools/new_region.py` scaffold that creates: `regions.json` entry, `Packs/NN_region.json` manifest, prefixed data files in every pack folder with EN/AR stubs, a look block, wardrobe set entry, radio station stub, campaign skeleton (12 chapters), 10 achievements; Microsoft Store add-on id wiring via `Dlc.Provider`; the region card in the front end reads ownership from `ContentPacks`.
**Acceptance**: running the scaffold for `jordan_valley` yields a playable (empty) map after flipping `comingSoon`; `ContentPacks.Validate()` is empty; static tests pass; the store entitlement stub is called once per session and cached.
**Tests**: scaffold output validated by the static tests; `ContentPacks` tests extended with the scaffold's manifest.

### W2-13 · P1 · Performance budget enforcement
**Goal**: GDD §15 budgets as tests: `TickDriver` ≤ 2 ms logic; counts of active NPCs/vehicles/lights within ranges; a `PerfOverlay` (F3) showing frame time, scheduler report, active object counts, draw-call estimate; static test counting `Update()`/`FindObjectsByType` in hot paths; streaming hitch test (teleport across the map in the fake, assert no `Start()` burst > N objects per frame).
**Acceptance**: budget breaches fail the headless suite; overlay available in builds behind a setting; one profiler capture per wave attached to the PR.
**Tests**: as described; fakes gain an object/draw estimate counter.

### W2-14 · P1 · Editor smoke tests per area
**Goal**: close the fake/Unity gap: Unity EditMode/PlayMode tests (`Assets/Tests/Editor`) that boot the scene, drive for 30 s on the ring road, enter/exit a vehicle, open every menu, switch language, and screenshot; CI runs them when `UNITY_LICENSE` is present.
**Acceptance**: 0 exceptions in the log; every menu screen opens and closes with gamepad only; Arabic layout screenshot has no left-aligned Arabic paragraphs (RTL check by text bounds).
**Tests**: these are the tests.

---

## Wave 3 (after wave 2 lands)

### W3-01 · P1 · Haboob survival and climate events
Host- or clock-triggered haboob (visibility 50 m, dust damage 5 % to open crops, animals must be penned, vehicles under roof), frost night (Delta), wadi flash flood, canal cleaning week, locust warning; co-op team score and farm banner reward. Tests: event scheduler probabilities per region table; damage application; team score.

### W3-02 · P1 · Camel racing, camel beauty show, buffalo show, canal jump, bale stacking, market duel
Competitions pack 2 (GDD §12.2), including rideable camels and the racetrack place. Tests: race timing; show scoring from condition/feed/grooming; airtime scoring.

### W3-03 · P1 · Farm visits, ratings, gifting, emotes 2, quick chat 2, streamer mode
Visit a friend's farm read-only or as helper; 1–5 stars + 12 preset comments on the farms-nearby board; daily gift; 16 more emotes; 40 quick-chat lines in EN/AR/Egyptian AR; streamer mode (hidden room code, big quick chat, chat-picks-weather host vote). Tests: rating persistence and anti-spam (one per visitor per day); gift once per day; chat lines bilingual (static).

### W3-04 · P1 · Production chains and buildings placement
Dates, dairy, cereal, fodder, cotton gin/rice mill town buyers, olive press; snap-grid building placement with rotation and ground conformity. Tests: chain conversion math; placement validity (slope, overlap).

### W3-05 · P1 · Water and irrigation UX
Water HUD bar per field, aquifer/canal gauge, water bill breakdown, expert mm-vs-ETc view, drip/sprinkler/pivot placement, storage pond upgrade, canal-turn planner. Tests: ETc per FAO Kc table per stage; efficiency per method; bill equals pumped m³ × tariff.

### W3-06 · P2 · Contracts 2.0 and reputation
Procedural neighbour contracts (plow/sow/harvest/transport/herd/water), payment + family reputation, discounts and land tips. Tests: generator variety and feasibility (crop in season, machine owned or lent).

### W3-07 · P2 · Campaign content: Najd "The inheritance" (12 chapters), Delta "نوبة المية" (12 chapters, Egyptian Arabic)
Data-only plus a few event hooks. Tests: chapter triggers by state; every text bilingual; dialect tag present on Delta lines.

### W3-08 · P2 · Architecture stage 3–4 extractions
`SoilSim`, `PriceModel`, `BootSteps`, `EventSim`, `VehicleBody/Seat/Hud`; `ISaveSection` + migration v3; `INetReplicated`. Tests: golden-file save v2 → v3; `NetSyncState` generic round-trip.

### W3-09 · P2 · Accessibility and localisation pass
Remaps, hold-to-toggle, colour-blind field states, HUD scale, motion options, Eastern Arabic numerals, Hijri display, screen-reader labels. Tests: every HUD state has an icon as well as a colour (static); numeral switch covers every formatted number path.

### W3-10 · P2 · Audio: regional radio, barks, adhan marker, crowd walla
Three fake stations per region with price-news snippets from live market data; text barks → recorded barks pipeline; adhan time marker with mute option. Tests: station schedule; news snippet reflects actual prices.

### W3-11 · P3 · World 4 × 4 km and Xbox port prep
Streaming tiles, more places, Xbox input/UX certification checklist, Smart Delivery packaging.

---

## Top 10 (if only ten things get done before beta)
1. W2-03 vehicle feel + cameras + enter/exit
2. W2-01 living traffic
3. W2-02 pedestrians with schedules
4. W2-06 co-op in five minutes + roles + ping
5. W2-07 competitions pack 1 (race, harvest contest, tractor pull)
6. W2-05 AI workers on FarmCore fields
7. W2-04 interaction layer
8. W2-09 daily/weekly goals, Harvest Pass, real-calendar events
9. W2-08 clip mode + creator codes
10. W2-13 performance budget enforcement (with W2-11 core adoption feeding it)
