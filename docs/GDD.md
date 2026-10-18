# Mazraa (المزرعة) — Game Design Document

Version 1.0 — October 2026 — Almadarek Games — Unity 6 (6000.3.10f1, URP) — Microsoft Store (PC), Xbox later.
Companion documents: `docs/ARCHITECTURE.md` (code), `docs/ROADMAP.md` (helper backlog), `DATA_SOURCES.md` (Saudi crop data already in the build).

This is the steering document for every helper working on the game. When a feature is not described here, ask or add a section; when the code disagrees with a number here, the number here wins unless a playtest says otherwise (then update this file).

---

## 0. One-page summary

| | |
|---|---|
| **Elevator pitch** | The first open-world farming sim of the Arab world. Run a real farm in the Najd oasis (Saudi Arabia) or the Nile Delta (Egypt), drive to town, deal with real people, real crops and real water, alone or with friends — with the responsiveness and "alive world" feel of GTA, not the spreadsheet feel of a tractor catalogue. |
| **Genre** | Open-world farming / life sim, single player + 2–4 player drop-in co-op |
| **Platform** | PC (Microsoft Store, Steam later), Xbox Series after PC launch |
| **Target** | 60 fps on a GTX 1060 / RX 580 at 1080p High; 30 fps minimum on Intel Iris Xe at 720p Low |
| **Business model** | Premium (USD 24.99 / SAR 94 / EGP 999 regional), region DLC packs (USD 9.99), free seasonal content drops, no micro-transactions, no loot boxes, no pay-to-win, no real-money cosmetics |
| **Rating** | PEGI 3 / ESRB E; family friendly, no violence (vehicles bump NPCs, nobody is hurt) |
| **Languages** | English, Arabic (Modern Standard; Egyptian Arabic dialogue for Delta content), more later |
| **Pillars** | 1. Real Arab farming 2. Alive, responsive world ("GTA feel") 3. Fun with friends 4. Depth you can ignore 5. Bug-free, respectful of time |

---

## 1. Vision and pillars

### 1.1 Vision
Farming Simulator sells 25 years of tractor licensing to European and American farmers' kids. Mazraa sells *the place*: the smell of dust before rain in Qassim, the date harvest festival in Buraydah, the canal turn (نوبة المية) in a Delta village, the souq on a Friday morning, the microbus that will not stop honking. Players should feel they are *somewhere*, among people, and that their farm is the heart of a living community. Then we make the actual farming deeper and more honest than FS25 (water, salt, soil, seasons that matter) while keeping it readable.

### 1.2 Pillars (rank order — when two conflict, the lower number wins)

1. **Real Arab farming.** Real crops, real calendars, real water problems, real markets of Saudi Arabia and Egypt first. If a Saudi or Egyptian farmer laughs at it, fix it. Numbers in this document come from FAO, GASTAT, CAPMAS, MEWA and published agronomy (section 6 and Appendix A).
2. **Alive and responsive ("GTA feel").** Input is answered within 3 frames. Vehicles have weight and bounce. NPCs have schedules, notice you, react to you and talk back. Traffic flows. The world is never frozen waiting for a menu. Everything you can see you can walk or drive to. Section 15 is the measurable checklist.
3. **Fun with friends.** Drop-in co-op in under five minutes from the title screen, shared farm goals *and* friendly competition (races, harvest contests, animal shows), gifting, farm visits with ratings, photo mode and clips built for sharing. Family-safe chat. Section 12.
4. **Depth you can ignore.** Every system has a "just press E" layer and an expert layer (soil tests, salinity, contracts, loans). A nine-year-old and an agronomist both have fun; the agronomist earns more.
5. **Bug-free and respectful of time.** Zero known crash bugs at release, autosave every 2 s of change, no softlocks, every action undoable or cheap to recover from. Play sessions of 15 minutes are rewarding; sessions of 4 hours are never padded.

### 1.3 Anti-goals
- No real-money shop of any kind beyond region DLC. No loot boxes, no gacha, no energy timers.
- No photoreal brand licensing race. Vehicles are "inspired by" (Gulf-market pickups, Egyptian 1970s tractors) with our own names.
- No punishing failure: debt, droughts and pests cost money and time, never the save.
- No "content gates" that block a region's whole map behind story. Story adds; it never locks.

---

## 2. Target player

| Persona | Who | What they want | What we give them |
|---|---|---|---|
| **Khalid, 14, Riyadh** | Plays FS25 and GTA V on Xbox/PC with friends on Discord, Arabic UI, TikTok heavy | Mess around with friends, drive fast, jump a tractor over a canal, show off | Co-op in 5 minutes, races, physics fun, photo mode, clips, emotes, farm ratings |
| **Mariam, 27, Mansoura** | Cosy-game player (Stardew, Animal Crossing), grew up visiting the family land in the Delta | Beauty, calm, animals, decorating, a village that feels like home | Cosy loop (pet, decorations, villagers, festivals), Egyptian dialogue, canal/village life |
| **Abu Fahad, 41, Qassim** | Actually farms dates and alfalfa; sim fan | Realism, numbers, water economics, pride in his region | Soil model, water bills, salinity, real prices, Qassim date festival, expert HUD |
| **Streamer / creator, 20–30, Gulf or Egypt** | Makes 60-second clips | Funny moments, chaos, viewer interaction | Haboob survival events, tractor pulling, farm-visit ratings, creator codes for layouts |
| **Parent + child, co-op couch/online** | Safe, shared, simple | Roles that let a child do meaningful work | Co-op roles (Driver, Herder, Shopkeeper, Farmer), text-free quick chat, no voice needed |

Primary market: Saudi Arabia, Egypt, UAE, Kuwait, Jordan, Morocco; secondary: farming-sim fans worldwide (English). Age 10–40.

---

## 3. Core loop

### 3.1 Moment to moment (10–60 seconds)
Move or drive → see something that invites action (field ready, NPC waving, delivery truck arriving, friend doing something) → one-button interaction with immediate feedback (sound, animation, HUD counter, money ticking up) → short consequence (field state changes, dialogue line, cargo in truck). Every interaction answers within 3 frames and finishes within 2 seconds, or shows progress.

### 3.2 Daily (one in-game day = 24 real minutes by default; 6/12/24/48 selectable)
Morning: wake-up card (weather, water turn, prices, friend activity, today's quests) → morning chores (animals, irrigation, check fields) → work block (field work, deliveries, contracts, town errands) → afternoon heat (Najd summer: NPC siesta, markets close 12–16, player is nudged to workshop/shop/greenhouse) → evening market and souq (best prices, NPC gatherings, festivals) → night (quiet, animals in, autosave, co-op races on the ring road).

### 3.3 Seasonal (default 5 days per season; options 5 / 10 / 20)
Season plan (which fields, which crops), sowing window pressure (miss the window, lose yield), mid-season management (water, pests, salt), harvest crunch (hire workers, call friends), post-harvest (sell or store, plan the rotation), seasonal festival (date harvest, Ramadan nights, Eid, Nile flood / canal cleaning, Sham el-Nessim).

### 3.4 Long term (20–60 hours)
Farm level 1 → 30 (section 11), land expansion (0.5 ha greenhouse plot → 60 ha estate), production chains (dates → pressed dates → date syrup; milk → cheese; cotton → gin), vehicle fleet, workers, co-op farm reputation, mastery per crop, campaign per region, then the next region DLC with a new climate to learn.

---

## 4. World

### 4.1 Base map: Najd Oasis (Saudi Arabia, Al-Qassim style)
- 2.4 × 2.4 km playable (current OpenWorld), expandable to 4 × 4 km in wave 3.
- Zones: the player's farm (greenhouses, well, pens) at the centre; an asphalt ring highway with dirt tracks; a small town (souq, agricultural cooperative, fuel station, mosque, café, workshop, bank, land office, market pad, clinic); date palm estates; a wadi with seasonal water; dunes to the east; a camel market and racetrack; an abandoned farm (reclaimable); a government grain silo (SAGO-like buyer).
- Landmarks for orientation: water tower, mosque minaret, radio mast, big billboard at the highway junction.

### 4.2 DLC map 1: Nile Delta (Egypt)
- Same scale; flat, green, cut by canals (ترع) and drains; villages of red brick with pigeon towers; eucalyptus and casuarina windbreaks; a market town with the cooperative (الجمعية الزراعية), cotton gin, rice mill, a railway crossing, microbus stop, a Nile branch with feluccas; fields are long thin strips (feddan-shaped, 60 × 70 m).
- Canal water turn every 4 days drives irrigation planning (already in regions.json).

### 4.3 Future regions (see 21): Jordan Valley, Souss Valley (Morocco), Mesopotamia (Iraq), Al Batinah (Oman).

### 4.4 World rules
- Everything visible is reachable. No invisible walls inside the map; a soft rising rim at the edge.
- Day/night and weather change lighting, NPC behaviour, prices and sound.
- Time pauses only in pause menu; shop and info screens run with the world (co-op safe).

---

## 5. Player, movement and camera

- First person and third person (toggle V). Third person is the default in the open world; first person inside greenhouses and vehicles (selectable).
- Walk 1.8 m/s, jog 4.0 m/s, sprint 6.5 m/s (stamina-free; sprint lowers carrying capacity), crouch, vault over low walls and fences (≤ 1.1 m), climb ladders on silos and water towers, swim in canals (slow, no drowning: you climb out).
- Carry one heavy object (sack, crate, lamb) in hands or several light items in inventory (section 9.6).
- Camera: GTA-style spring-arm, 0.12 s lag, collision pull-in, auto-recentre behind the player after 1.5 s of forward movement when the stick is released; free look on right stick/mouse; FOV 60 walking, 68 sprinting, 75 driving fast.
- Fall damage: none; a stumble animation above 4 m.

---

## 6. Farming systems

### 6.1 Field model (FarmCore)
Fields are polygons on the terrain (not grid tiles) with a state machine per field: `Fallow → Plowed → Cultivated → Sown → Growing(stage 1..n) → Ready → Harvested(stubble) → Fallow`, plus overlays: `Weeds (0..1)`, `Moisture (0..1)`, `Salinity EC (dS/m)`, `N/P/K (kg/ha)`, `pH`, `Organic matter %`, `Compaction`, `Pest/Disease`. Fields can be subdivided or merged at the land office. The soil model that already exists (`SoilModel`, `soil.json`) is the reference and FarmCore should wrap it, not replace it.

### 6.2 Crop catalogue — Najd (Saudi Arabia)

In-game season mapping: Spring Mar–May, Summer Jun–Aug, Autumn Sep–Nov, Winter Dec–Feb. "Sow" is the in-game season window; "Days" is the real days-to-harvest the growth curve is scaled from (the game scales 1 real crop day to 1 in-game day at the 5-day season setting, so a 120-day crop takes about 1.2 in-game seasons; at 10 and 20 days per season the scale is 2× and 4×).

| Crop | Sow (real months) | Harvest | Days | Yield (real) | Water need, season total (mm) | Farm-gate price (SAR/kg) | Notes |
|---|---|---|---|---|---|---|---|
| Wheat | Nov–Dec | Apr–May | 150 | 6 t/ha irrigated (Saudi avg. ~6.3 t/ha, FAOSTAT) | 450–650 | 1.70 (SAGO procurement) | Centre-pivot sprinklers; government buyer, fixed price |
| Barley | Nov–Dec | Apr | 130 | 4.5 t/ha | 450–650 | 1.3 (est.) | Feed; tolerant of salt |
| Alfalfa (برسيم حجازي) | Sep–Oct (establish 60 d) | cuts every 25–30 d, 10–14/yr | 60 then cuts | 27–32 t DM/ha/yr | 1,600–2,000 (very high) | 21 SAR/bale (~25 kg) | The big water consumer; Saudi policy discourages it — in game, the water bill makes it a trade-off |
| Potato | Jan–Feb and Sep–Oct | Apr–May / Dec–Jan | 110 | 30 t/ha | 500–700 | 2.5 (retail 4.71) | Two windows |
| Onion | Nov–Dec (transplant) | Apr–May | 150 | 35 t/ha | 350–550 | 2.0 (retail 4.69) | |
| Tomato (open field) | Sep–Oct, Feb | Dec–Jan / May | 100 | 40 t/ha | 400–800 | 3.5 (retail 6.73) | Greenhouse variant: all year, 150 t/ha |
| Watermelon | Feb–Mar | May–Jun | 90 | 30 t/ha | 400–600 | 1.6 (retail 3.18) | Heat tolerant |
| Sorghum | Jun–Jul | Sep–Oct | 100 | 4 t/ha grain | 450–650 | 1.4 (est.) | Summer-safe cereal |
| Pearl millet (دخن) | Jun–Jul | Aug–Sep | 75 | 2.5 t/ha | 350–500 | 1.5 (est.) | Fastest summer crop |
| Date palm (Khalas, Sukkari, Ajwa) | plant offshoot (4–5 yr to crop) | Aug–Oct | perennial | 60 kg/palm/yr (GASTAT 2024: 1.92 Mt from 32 M fruiting palms) | 1,500–2,500 per ha (drip) | Khalas 13–31, Sukkari 20–60 by grade, Ajwa 40–80 | The flagship; grading matters |
| Olive | plant (3–5 yr) | Oct–Nov | perennial | 20–30 kg/tree | 600–800 | 24 (table olives) | Al-Jouf style; oil press chain |
| Pomegranate | plant (2–3 yr) | Sep–Oct | perennial | 20–40 kg/tree | 800–1,000 | 14.7 | |
| Grapes | plant (2–3 yr) | Jun–Jul | perennial | 10–15 kg/vine | 500–1,200 | 11.3 | |
| Lemon / orange | plant (3–4 yr) | Nov–Jan | perennial | 40–50 kg/tree | 900–1,200 | 7.3 / 5.8 | |

Sources: FAO Crop Water Information (water needs per crop), FAO Irrigation & Drainage Paper 56 (Kc, ETo), FAOSTAT (national yields), GASTAT price bulletins and date production release, SAGO wheat price — links in Appendix A. Saudi retail prices are already in `DATA_SOURCES.md`; farm-gate is set at roughly 50–60 % of retail where no official farm-gate series exists (marked est.).

### 6.3 Crop catalogue — Nile Delta (Egypt)

Egyptian seasons: **Shetwi** (winter, sow Oct–Dec), **Seifi** (summer, sow Mar–May), **Nili** (late summer/autumn, sow Jul–Sep). Prices in EGP are volatile (2023–2025 inflation); the game stores base prices in a neutral unit and shows EGP with the region multiplier, so these are relative, re-tuned each season patch.

| Crop | Season | Sow | Harvest | Days | Yield (real) | Water (mm) | Price (EGP/kg, 2024–25 approx.) | Notes |
|---|---|---|---|---|---|---|---|---|
| Wheat | Shetwi | Nov–Dec | Apr–May | 150 | 6.5 t/ha (~2.7 t/feddan; Egypt is among the highest-yield wheat producers, FAOSTAT) | 450–650 | 14.7 (government 2,200 EGP/ardeb of 150 kg, 2024/25) | Government procurement with bonus for early delivery |
| Egyptian clover (برسيم مصري) | Shetwi | Oct–Nov | 4–5 cuts Dec–May | 50 then cuts | 30 t fresh/ha | 600–900 | sold by cut/feddan | Feeds buffalo and cattle; fixes N for the next crop |
| Faba bean (فول) | Shetwi | Oct–Nov | Mar–Apr | 140 | 3 t/ha | 300–500 | 25 (est.) | Rotation crop; breakfast staple |
| Onion | Shetwi | Nov–Dec | Apr–May | 150 | 35 t/ha | 350–550 | 8 (est.) | Export crop |
| Potato | Seifi (Jan–Feb) and Nili (Sep–Oct) | | | 110 | 28 t/ha | 500–700 | 10 (est.) | Export; frost-free |
| Tomato | all three seasons | | | 100 | 40 t/ha | 400–800 | 8 (est.) | Volatile prices (±50 %) |
| Cotton (Giza long-staple) | Seifi | Mar–Apr | Sep–Oct | 180 | 2.5–3 t seed cotton/ha (~7–8 qintar/feddan) | 700–1,300 | ~70 per kg seed cotton (10,000–12,000 EGP/qintar of 157.5 kg, 2024) | Picked by hand → workers; sold at the gin |
| Rice | Seifi | May–Jun (nursery, transplant) | Sep–Oct | 130 | 9 t/ha (Egypt is the top yielder in Africa, FAOSTAT) | 450–700 (flooded, often >1,200 in practice) | 15–16 (paddy, 15,000–16,000 EGP/t 2024) | Government area limits (licence quota mechanic) |
| Maize (ذرة شامية) | Seifi / Nili | May–Jun / Jul | Sep / Oct–Nov | 110 | 8 t/ha | 500–800 | 11 (est.) | Feed chain |
| Sugar beet | Shetwi | Sep–Nov | Mar–May | 190 | 50 t/ha | 550–750 | 2.0 (factory contract) | Delta factories; contract mechanic |
| Berseem seed, sesame, peanuts (sandy fringe) | Seifi | May | Sep | 110 | 1.2 / 1.0 / 3 t/ha | 500–700 | | Fringe/sandy fields |
| Citrus (Valencia), mango (Ismailia style), banana | perennial | | Nov–Feb / Jul–Sep / all year | | 25 / 12 / 40 t/ha | 900–1,200 / 1,000 / 1,200–2,200 | | Orchard chains |
| Date palm (Siwi, Zaghloul) | perennial | | Sep–Oct | | 60 kg/palm | | | Shared with Najd |

Sources: FAO Crop Water Information; FAOSTAT Egypt; CAPMAS agricultural bulletins; Ministry of Supply wheat price announcements (press, 2024/25); cotton price committee announcements (press, 2024). Appendix A.

### 6.4 Growth and yield
- Growth per in-game hour = base rate × temperature factor (crop cardinal temps from `soil.json`) × water factor × nutrient factor × salt factor × daylight factor. Already modelled in `SoilModel`; FarmCore must call it, not re-implement it.
- **Yield** = potential × min(limiting factors) × weed penalty × pest penalty × harvest timing (window ±2 days: 100 %; 5 days late: 85 %; 10 days late: 60 % and lodging risk).
- **Quality grade** (A/B/C) from: harvested in window, water stress during flowering, pest damage, variety. Grade sets price multiplier 1.25 / 1.0 / 0.7 and is the hook for mastery and contracts.

### 6.5 Field operations and implements

| Operation | Hand (E, slow) | Implement | Speed (hand / machine) | Notes |
|---|---|---|---|---|
| Plow | hoe (shallow, leaves weeds) | mouldboard plow, chisel | 0.05 ha/h / 1.2 ha/h | Deep plow buries weeds, breaks compaction; wet plowing compacts |
| Cultivate / level | rake | disc harrow, laser leveller (Egypt rice) | 0.08 / 2.0 | Levelling cuts flood-irrigation water 20 % |
| Sow | seed bag | seed drill, planter, rice transplanter | 0.05 / 1.5 | Row spacing by crop; drill vs broadcast yield +8 % |
| Fertilise | sack spreader | spinner spreader, fertigation through drip | 0.1 / 3.0 | N/P/K products: urea, DAP, potash, compost, manure, gypsum |
| Spray | backpack sprayer | boom sprayer | 0.1 / 3.0 | Herbicide, insecticide, fungicide; wind > 20 km/h blocks spraying |
| Irrigate | hose / open the canal gate | drip, sprinkler, centre pivot, furrow/flood (section 7) | | |
| Harvest | sickle, basket | combine (cereal, with header), potato harvester, forage mower + baler, cotton picker, date-palm lift | 0.03 / 1.0–1.5 | Crop-specific; hand picking creates worker demand |
| Transport | wheelbarrow | trailer, grain cart, truck | | Capacity table in 8.3 |
| Bale / wrap | — | round baler, bale wrapper | | Alfalfa, berseem |
| Mulch | straw by hand | mulcher | | Residue → organic matter |

Working width × speed gives area/h; all implements have a fill level, wear (0–100 %) and a daily maintenance cost. Breakdowns only at wear > 80 %; a flat tyre is a 20-second fix with a jack, not a tow.

### 6.6 Greenhouses (existing core, kept)
Modules Irrigation / Nutrient / Energy / Climate per greenhouse; bed states Healthy / Weak / Sick; year-round tomato, cucumber, pepper, strawberries, lettuce. Greenhouses are the beginner's safe income and the summer activity in Najd. Add: shade-net houses (cheap, Egypt), hydroponic upgrade, solar panels for the Energy module (cuts the bill 60 %).

### 6.7 Pests, weeds and disease
- Weeds grow 0.02/day untreated, faster with moisture; cut yield up to 35 %.
- Pests per crop: red palm weevil (dates; trap + inject), tomato leaf miner (Tuta), cotton leafworm, aphids on faba, rice stem borer, rodents in stores. Pest pressure rises with monoculture and warm nights; drops with rotation, predators (owl boxes, 50 % fewer rodents) and timely spraying.
- Rotation bonus: a different family after the last crop gives +10 % yield and −40 % pest pressure; legumes add 40 kg N/ha.

---

## 7. Water and irrigation (signature system)

Water is the Arab farmer's real constraint and our differentiator. Every field has a daily crop water need (ETc = Kc × ETo; ETo from season and weather, Kc from stage — FAO 56). Irrigation methods deliver with efficiency and salt consequences:

| Method | Efficiency | Labour | Capital (SAR/ha) | Salt build-up | Where |
|---|---|---|---|---|---|
| Flood / basin (غمر) | 50–60 % | high (gates, watching) | 0 | high in heat | Delta canals, old Najd farms |
| Furrow | 60–70 % | medium | low | medium | Vegetables, cotton |
| Sprinkler / centre pivot | 75–85 % | low | 12,000–18,000 | low–medium | Wheat, alfalfa, potato in Najd |
| Drip (تنقيط) | 85–95 % | low | 8,000–15,000 | lowest; needs leaching occasionally | Orchards, greenhouses, vegetables |
| Flood with laser levelling | 65–75 % | medium | levelling 1,000 | medium | Delta rice |

Sources: FAO irrigation efficiency guidance; typical capital costs are industry estimates (Appendix A).

Water sources:
- **Najd groundwater well**: pumping cost per m³ (diesel/electric), aquifer level that drops with overuse (soft cap: pump rate halves below 40 % level; recovers slowly), well deepening upgrade, solar pump upgrade. EC of water 1.5–3 dS/m depending on well.
- **Delta canal**: free water but only on the turn (every 4 days, 12 hours); storage pond upgrade stores a turn; illegal pumping between turns triggers a village-council warning event (reputation hit, not a fine on first offence). Drain water reuse is cheap but saltier.
- **Rain**: rare in Najd (winter), negligible in Delta; waters all fields for a day.
- **Treated wastewater** (Najd, late game): cheap, allowed only for fodder and trees.

Salinity: EC rises with flood irrigation in heat and saline water, falls with leaching (uses 150 mm of water), gypsum and drainage upgrade. Salt-tolerant crops (barley, date palm, sugar beet, cotton) vs sensitive (bean, onion, potato, citrus) from FAO salt-tolerance tables.

Water HUD: a blue bar per field (moisture), a daily water bill, aquifer/canal gauge on the map. The expert layer shows mm applied vs ETc.

---

## 8. Vehicles and machines

### 8.1 Handling model ("GTA feel", see 15)
Rigidbody + 4 wheel colliders, torque curve, gearbox (auto with manual option), differential lock on tractors, trailer coupling with physical hitch, weight transfer, suspension travel. Arcade assists: steering help at speed, brake assist on slopes (toggle), reset-to-road (hold R 1.5 s). Damage is cosmetic (dents, dust) plus wear; no destruction. Horns, lights, indicators, wipers, radio with regional stations (section 17).

### 8.2 Fleet (our own names; "inspired by" real Gulf/Egypt machines)

| Class | Example (Najd) | Example (Delta) | Price (SAR) | Top speed | Notes |
|---|---|---|---|---|---|
| Pickup | "Sahra 2500" (Hilux-like) | "Nile 2500" | 95,000 | 150 km/h | Starter vehicle; carries 800 kg; the GTA driving fun |
| Small tractor 45 hp | "Falah 45" | "Nasr 45" (Egyptian 1970s look) | 85,000 | 30 km/h | Pulls plow/seeder; hand-crank start gag when worn |
| Mid tractor 110 hp | "Falah 110" | | 240,000 | 42 km/h | Most implements |
| Big tractor 220 hp | "Jabal 220" | | 620,000 | 50 km/h | Centre pivot, big plow |
| Combine | "Hasad 300" | | 850,000 | 25 km/h (road 32) | Headers for cereal, sunflower |
| Truck 10 t | "Naqil 10" | Egyptian flatbed | 320,000 | 100 km/h | Deliveries |
| Microbus | — | "Wahid" 14-seat microbus (traffic, taxi) | 180,000 | 120 km/h | Delta signature traffic; drivable |
| Tuk-tuk | — | tuk-tuk | 25,000 | 50 km/h | Village deliveries, race class |
| Motorbike | 125cc | 125cc | 12,000 | 95 km/h | Quick travel; 1 crate |
| Quad / dune buggy | "Rimal" | — | 48,000 | 90 km/h | Dune fun; camel market deliveries |
| Camel | yes | — | 15,000 | 20 km/h (race 40) | Rideable; camel beauty/race |
| Donkey cart | — | yes | 4,000 | 10 km/h | Starter Delta transport; comedy |
| Felucca / small boat | — | yes | 30,000 | 15 km/h | Nile branch deliveries, fishing |
| Water tanker, fuel trailer, bale trailer, tipper 8 t, grain cart 15 t | | | | | |

### 8.3 Capacities and economy of use
Fuel consumption per hour at full load: pickup 9 L, 45 hp tractor 8 L, 110 hp 18 L, 220 hp 36 L, combine 45 L, truck 25 L. Diesel price SAR 1.15/L (Saudi) and EGP 13.5/L (Egypt, 2024) — region multiplier. Maintenance 0.5 % of price per 10 hours of use. Resale 70 % at purchase, −5 %/season.

### 8.4 Enter / exit and vehicle AI
Enter from either door with a 0.6 s animation (skippable after 0.3 s); exit leaves the engine running if you held E less than 0.3 s. Vehicles keep physics state when you leave (a truck on a slope rolls). Workers and traffic drive the same vehicle code with an AI driver component (section 13).

---

## 9. Animals, chains, workers, land, inventory

### 9.1 Animals

| Animal | Region | Buy price | Product | Interval | Feed/day | Max per pen | Extras |
|---|---|---|---|---|---|---|---|
| Najdi sheep | Najd | SAR 1,200 | wool (yearly), lambs, meat (sell live) | lambing 150 d | 2 kg hay | 60 | Eid demand spike ×1.8 |
| Goat (Ardi, Damascus) | both | 900 | milk 2 L/d, kids | | 1.5 kg | 60 | Cheese chain |
| Camel | Najd | 15,000–60,000 | milk 6 L/d, racing, beauty show | | 8 kg | 12 | Prestige; race and show (section 12) |
| Holstein dairy cow | Najd (big farms) | 9,000 | milk 28 L/d | | 25 kg TMR | 40 | Needs cooling in summer (fans) |
| Water buffalo (جاموس) | Delta | EGP 60,000 | milk 8 L/d (rich) | | berseem 30 kg | 20 | Delta signature; wallows in the canal |
| Baladi chicken | both | 60 | eggs 0.7/d | | 0.12 kg | 200 | Coop upgrade levels |
| Pigeons (برج حمام) | Delta | | squabs | | | | Pigeon tower already in look |
| Bees | both | 800/hive | honey 20 kg/yr (Sidr honey premium in Najd) | | | 20 | Pollination +10 % orchard yield |
| Fish (tilapia ponds) | Delta, Najd aquaculture | | 25 t/ha/yr | | | | Existing fish feature |
| Horse (Arabian) | Najd | 40,000 | riding, show | | | 6 | Cosmetic prestige |
| Dog (Saluki) | both | | companion; herds sheep | | | 1 | Existing pet system |

Animals need feed, water, shelter and cleaning; neglect lowers production, never kills (they "go to the neighbour" and come back after a fee). Breeding is a slider, not a chore.

### 9.2 Production chains

| Chain | Input → Output | Building cost | Where |
|---|---|---|---|
| Dates: palm → fresh (rutab) / dried (tamr) → pressed dates, date syrup (دبس), date paste; grading A/B/C | | 80,000 | Najd |
| Dairy: milk → laban, labneh, white cheese (Domiati, Delta), ghee (سمن بلدي) | | 60,000 | both |
| Cereal: wheat → flour (mill) → bakery (bread, rusk) | | 150,000 | both |
| Fodder: alfalfa/berseem → bales → TMR mix with sorghum | | 30,000 | both |
| Cotton: seed cotton → gin → lint + seed → oil press | | gin is town-owned; player sells | Delta |
| Rice: paddy → mill → white rice + bran + husk (fuel) | | town mill | Delta |
| Olive: olives → press → oil (grades) | | 70,000 | Najd |
| Honey, wool → yarn, hides → leather (workshop) | | | |
| Sugar beet → factory contract | | | Delta |

### 9.3 Economy and market
- Three price layers: **base** (data), **seasonal curve** (per good, ±30 %; sell in off-season for more), **event** (Eid lamb ×1.8, Ramadan dates ×1.4, flood, import ban, fuel subsidy change), and a **saturation** term: selling > 5 t of one good at one buyer in a day lowers its price 2 % per tonne for 3 days.
- Buyers: government buyer (fixed, reliable, slow pay 2 days), cooperative (fair), souq stall (small lots, best price, needs presence or a worker), wholesaler (bulk, low), export agent (grade A only, +40 %, contract), roadside stand (existing), friends (co-op trade).
- Costs: seed, fertiliser, water, fuel, wages, maintenance, loan interest, land rent, insurance (optional; pays 60 % of a weather loss).
- Bank: loans up to 3× yearly revenue at 4–8 %, no Riba mode option (Murabaha-style fixed fee instead of interest, same numbers, different wording) for players who want it.
- Start money: Najd 60,000 SAR + pickup + 2 greenhouses; Delta 150,000 EGP + donkey cart + 2 feddan. "Rich" and "Hard" presets ×3 / ×0.5.

### 9.4 Contracts and missions
- **Contracts** (repeatable, procedural): plow/sow/harvest/transport/spray for a neighbour with your or their machine; deliver N kg of X by day D; herd 20 sheep to the market; water a neighbour's field during his travel. Payment + reputation with that NPC family. Fail = reputation −, no money loss.
- **Missions** (existing data-driven system) carry the campaign and tutorials; types already handled by MissionManager stay.
- **Daily quests** (existing) feed the engagement loop (section 12.4).

### 9.5 Workers
Hire from the town café / cooperative board: tractor driver, picker (hand harvest, cotton), herder, shopkeeper (sells at the souq), irrigator (opens gates on the turn), mechanic. Wage per day, skill 1–5 (speed and quality), fatigue, they go home at night and on Friday noon. Workers use the same job queue as players and co-op friends (section 13.3), so a friend can "be" a worker.

### 9.6 Land and buildings
Land parcels on the map (WorldLand exists): price per ha by soil quality and water access; rent option (10 %/year). Buildings are placed on the farm with a snap grid and a free-rotation mode: barns, pens, silos (grain 100/500/2,000 t), cold store, workshop, worker house, mosque corner (cosmetic, no mechanics), guest tent (majlis; co-op meeting spot), pigeon tower, date drying yard.

### 9.7 Inventory and carrying
Light items (seed bags, tools, phone, keys) in a 12-slot inventory; heavy items in hands (one). Vehicles have cargo with weight and volume. No inventory tetris.

---

## 10. Town, NPCs and life

### 10.1 NPC population and schedules
- 120–180 named NPCs per map with a home, a job, a family, a daily schedule (prayer-time pauses are part of the schedule in both regions; shops close Friday noon), and a mood. Schedules are data (`npcs.json`): e.g. "Abu Saleh, 58, date farmer: 05:30 farm, 12:00 home, 16:30 café, 19:30 mosque, 21:00 home".
- Reaction layer: NPCs look at you within 2 m, greet with region-appropriate lines (voice-like text barks; recorded barks later), comment on your vehicle ("ما شاء الله على الجبل 220"), flinch and complain when bumped (existing NpcBump), run from a haboob, gather at festivals, clap at races, rate your farm when visiting.
- Relationship per family (existing Friendship): gifts, contracts, tea invitations; high relationship unlocks discounts, land tips, co-op NPC help (they water your field once when you are away).

### 10.2 Town services
Souq (sell small lots, buy food/gifts), cooperative (seed, fertiliser, subsidy paperwork), fuel station (fuel, tyres, snacks), workshop (repair, paint, tune), bank, land office, clinic (restores nothing — cosmetic, first-aid tutorial gag), café (hire workers, hear rumours = price tips), mosque (time marker; no mechanics), school (children NPCs on schedule), post office (DLC: sell export), vehicle dealer, animal market (Thursday), camel racetrack (Najd), railway station (Delta, scenery + train).

### 10.3 Traffic
Vehicle population per km of road scaled by time (peak 07:00–09:00, 16:00–19:00): pickups, sedans, trucks, water tankers, microbuses (Delta), tuk-tuks, donkey carts, tractors with trailers, buses to the city. Traffic obeys lanes, stops at the junction when you cut in, honks, pulls over for the ambulance (random event), and *does not* despawn within 60 m of the player. Drivers get out and complain after a bump (family friendly), then drive on. Pedestrians cross at the souq, kids play football on a dirt lot (ball is physical), shepherds cross the road with a flock (the classic jam).

### 10.4 Weather, seasons, climate events
Daily weather from the regional climate table (already in WorldClock + regions.json): Najd: sunny 70 %, heatwave (summer 55 %), dust/haboob (spring 10 %, big storm event 2×/year), rain (winter 15 %), rare hail. Delta: humid haze mornings, mild winter rain, khamaseen hot wind in spring (sand + heat), fog on canals. Events: **haboob** (visibility 50 m, dust damages open crops 5 %, animals must be penned, co-op survival event), **frost night** (Delta Jan: potato/tomato damage unless covered), **flash flood in the wadi** (Najd: free water + blocked road), **canal cleaning week** (Delta: no turn, roads full of mud), **locust warning** (rare, spray or lose 20 %), **heatwave** (ET ×1.5, animals need shade).

---

## 11. Progression

### 11.1 Farm level (1–30; XP from every action; existing FarmLevel extended)
Unlocks at: 2 pickup customisation, 3 first field purchase, 4 workers, 5 mid tractor, 6 drip kits, 8 contracts tier 2, 10 bank loans tier 2, 12 production building 1, 15 big tractor, 18 export agent, 20 camel racing, 25 estate land, 30 "Shaykh al-Falaheen" title and golden sidr tree cosmetic.

### 11.2 Mastery (existing CropMastery, extended)
Per crop and per machine: 5 stars from quantity and grade-A ratio; each star +2 % yield / −2 % fuel for that item and a cosmetic (field sign, decal). Per animal species likewise.

### 11.3 Achievements (existing manager): 60 at launch, store-synced where the platform supports it. Examples: "First date harvest", "Water saver" (season under 70 % of ETc with full yield), "Souq king" (10 t sold at the souq), "Jumped the canal" (airtime 1.5 s in a tractor), "Haboob survivor", "Good neighbour" (20 contracts), "Co-op harvest 50 t".

### 11.4 Campaign per region (data-driven CampaignManager)
- Najd: "The inheritance" — 12 chapters: grandfather's farm, the dry well, the date festival, the water inspector, the cousin's loan, the first export; ends at farm level ~12.
- Delta: "نوبة المية" — 12 chapters in Egyptian Arabic: the cooperative queue, cotton season, the canal dispute, the microbus wedding, Sham el-Nessim, the government wheat delivery.
- Campaign never blocks free play; chapters trigger from state (crop, level, day) and can be dismissed.

### 11.5 Free play and challenges
Free play with presets (Easy / Normal / Expert; starting money; season length). Challenges: weekly seeded scenarios (same seed for everyone: "20 days, max profit from 4 feddan", "drought year") with a leaderboard among friends (local + platform friends; no global ranked to keep it friendly).

---

## 12. Fun with friends (pillar 3)

### 12.1 Co-op basics
- 2–4 players, drop-in/drop-out, host's farm (visitor saves nothing locally — existing SaveData session mode) **plus** a *shared farm* mode where all players are owners and the save is the host's cloud slot.
- Join flow under 5 minutes: title → Play with friends → "Host" shows a 6-character room code and a Microsoft Store / Steam invite; joiner types the code or accepts the invite → spawn next to the host with a pickup already provided → 30-second co-op tutorial card (roles, quick chat, ping).
- Roles (soft, switch any time at the majlis tent): **Farmer** (fields), **Driver** (deliveries, traffic), **Herder** (animals, camel), **Shopkeeper** (souq stall and prices), **Engineer** (irrigation, repairs). Roles give a small bonus (+10 % speed in your role) and a HUD focus, nothing exclusive.
- Shared goals: daily farm goal (e.g. 2 t wheat delivered), season goal, "farm reputation" rated by NPC visitors and friends.
- Ping/marker (middle mouse / RB): a world marker everyone sees; "come here", "look", "help" quick messages.

### 12.2 Friendly competition (host starts from the majlis or the racetrack board; all opt-in; results to the farm notice board)
| Activity | Rules | Reward |
|---|---|---|
| Ring-road race | 3 laps in matched vehicles (tuk-tuk class is the funny one), checkpoints, ghost of best lap | cosmetic trophy, XP, no money |
| Harvest contest | same field size, same crop, 5 in-game minutes; kg harvested | title "Harvest king" for the day |
| Tractor pulling | tug-of-war with a physical rope (joint) on the dirt lot; weight transfer matters | decal |
| Camel race / camel beauty show (Najd), buffalo show (Delta) | timing race around the track; show scores on condition, feed, grooming | seasonal cosmetic |
| Canal jump | ramp placed by the host; airtime and landing scored | clip auto-saved |
| Bale stacking | 60 seconds, physics bales | emote |
| Haboob survival | host triggers (once/day) a storm; get animals penned and vehicles under roof in time; team score | farm banner |
| Market duel | both sell at the souq for one day; higher revenue wins | title |

### 12.3 Social features
- **Farm visits**: visit a friend's farm (read-only or helper), leave a 1–5 star rating and one of 12 preset comments; ratings show on the world map "farms nearby" board. Likes feed farm reputation.
- **Gifting**: send a gift (dates box, sidr honey, a cosmetic you unlocked) once per day; the receiver gets a card on the morning screen.
- **Photo mode** (exists) → **Clip mode**: 15-second gameplay buffer with one key; clips saved to Videos/Mazraa with region watermark; "trendy moment" auto-trigger when airtime > 1 s, NPC bump > 3 in 10 s, 100 kg harvested in 30 s, race finish (opt-in). Share sheet opens the folder; no in-game upload (store-safe).
- **Emotes** (8 at launch, 24 unlockable): wave, thumbs up, dabke step, "ya salam", facepalm, tea pour, clap, dance.
- **Quick chat**: 40 preset lines in EN/AR/Egyptian AR, context menu; free text optional with the existing ChatSafety filter; no voice chat required (platform party voice works).
- **Creator codes**: export your farm layout (buildings, decorations, fields) as a 12-character code and text file; import applies on a new farm of the same region. Shareable on TikTok captions.
- **Streamer mode**: hide room code, big-font quick chat, "chat picks the weather" vote (host-local).

### 12.4 Healthy engagement loops (no dark patterns)
- **Daily goals** (3, 10 minutes): e.g. water two fields, sell at the souq, greet 3 villagers. Reward: XP, a seasonal token.
- **Weekly goals** (3, one session): deliver 5 t, finish 2 contracts, race once with a friend.
- **Season track** ("Harvest Pass"): 30 tiers of cosmetics and titles per real-world season, earned only by play tokens, free, no money, no timers beyond the season; missed tiers return next year. Items: wardrobes (Najdi bisht trim, Egyptian galabiya patterns), vehicle paints and decals, farm decorations (majlis carpets, lanterns), emotes, pet accessories.
- **Real-calendar events** (switch on by date, content in packs): Ramadan nights (night market, lanterns, iftar bell, dates demand), Eid al-Fitr and al-Adha (sheep demand, gift cards), date harvest festival (Buraydah-style stalls, Aug–Oct), Sham el-Nessim (Delta: coloured eggs, feseekh stall, picnics), Nile flood / canal cleaning week (Aug), national days (flags on vehicles). All cosmetic plus price effects; nothing exclusive forever.
- No streak punishment: coming back after a month gives a "welcome back" harvest from a neighbour who watched the farm.

### 12.5 Chaos and physics fun (GTA-like, family friendly)
Ramps appear at the dirt lot and the dune; bales, crates, melons and jerry cans are physical; vehicles tow anything with a rope; the quad flips and rights itself; camels refuse to move when honked at; NPCs chase you with a slipper if you flatten their vegetable stall (no harm, just comedy and a small fine). Haboob pushes light objects. Every chaos moment feeds clip mode.

---

## 13. AI: workers, traffic, pedestrians, animals

### 13.1 Job queue (shared by player prompts, workers and co-op)
`Job { field, operation, implement, priority, assignee }`; workers pick the highest priority they are skilled for; players see the queue on the farm board and can claim jobs. One system, no duplicated logic for AI vs human.

### 13.2 Field AI
Path planning on field polygons: headlands, parallel passes at implement width, turn radius by tractor; obstacles (poles, trees) skipped with a rounded path; refill trips to the yard; park at the field edge on completion. Target: a worker is never stuck for more than 10 s (watchdog teleports it to the field edge with a log entry).

### 13.3 Traffic
Lane graph from WorldLayout roads (centre line ± lane offset), spawn/despawn ring 250–400 m from any player, 20–40 vehicles active, intersection priority by sign, car-following model (IDM), overtaking only on the highway, pull-over for horn + flash (fun and useful behind a slow tractor). Player vehicles are obstacles; NPC drivers avoid, brake, honk, and after a stop >10 s get out and gesture, then drive on.

### 13.4 Pedestrians and animals
Navmesh around town and farms; crowd slots at the souq; flock behaviour for sheep (boids with a shepherd leader), free-roaming chickens that scatter, camels that path on tracks, dogs that follow. Stray goat on the road event.

---

## 14. UX

### 14.1 First 30 minutes (Najd; Delta mirrors it)
0:00 Title (sand, wind, oud). New farm wizard: name, look, region, difficulty, season length, language. 
0:30 Arrive by pickup with the cousin (driving tutorial on the ring road, 90 s, skippable). 
2:00 Farm: cousin shows the greenhouse (E to interact, well, modules) — first harvest in 4 minutes thanks to a bed that is already ripe. 
6:00 First sale at the market pad; money animation; "Farm shop" intro, buy wheat seed. 
8:00 Field: plow by hand (slow, funny) → the tractor keys appear → plow with the tractor (speed contrast). Sow. Water (well pump on, drip on field 1). 
13:00 Town trip: deliver the harvest to the cooperative, meet 3 NPCs, café (hire tutorial preview), fuel. Traffic and pedestrians visible from minute 13. 
18:00 Back at the farm: night falls, animals intro (3 sheep gift), autosave card. 
20:00 Day 2 morning card: weather, prices, a contract offer, a daily quest, "Invite a friend" button. 
25:00 First contract (transport bales for Abu Saleh) → first reputation. 
30:00 Free. Chapter 2 of the campaign is waiting but not blocking.
Target KPIs: 90 % of testers reach the first sale by minute 8, 70 % hire or invite by minute 30.

### 14.2 Tutorial beats
Contextual, one card at a time, bottom-left, dismissed by doing the thing; "Teach me" button on every screen (existing Learn mode); never more than one new concept per 2 minutes.

### 14.3 Menus (FS25-grade, already in progress)
Title → Career (slots), Play with friends, Challenges, Settings, Credits. In-game pause: Map, Farm (fields, animals, vehicles, buildings, finances tabs), Market (prices with 10-day history sparkline, buyers), Contracts, Workers, Season track, Social (friends, ratings, gifts), Settings, Photo/Clip, Help. All navigable with gamepad; RTL mirrored for Arabic.

### 14.4 HUD
Minimal by default: compass strip with waypoints and NPC icons, context prompt, cash, clock + weather + day, a vehicle widget (speed, fuel, implement state, cargo), a field widget when on a field (crop, stage, moisture, soil quick stats; expert toggle shows N/P/K/EC). Notifications stack top-right, 3 max, 4 s each. Everything scales 80–150 %.

### 14.5 Controller map (Xbox layout; keyboard in brackets)
Left stick move (WASD), right stick camera (mouse), A interact / enter (E), B cancel / exit (Esc), X secondary action e.g. implement lower (Q), Y switch tool / headlights (F), RB ping (middle mouse), LB quick chat (T), RT accelerate (W in vehicle), LT brake/reverse (S), D-pad: up HUD expert toggle (Tab), down horn (H), left/right radio (,/.), View map (M), Menu pause (Esc), hold View photo mode (P), click L3 sprint (Shift), click R3 third/first person (V), LB+RB clip save (F9).

---

## 15. The "GTA feel" checklist (measurable budgets)

| Area | Budget | How we test |
|---|---|---|
| Input to visible action | ≤ 50 ms (3 frames at 60) for movement, steering, interact, camera | automated: frame-count test in the headless harness for logic; in-editor stopwatch overlay |
| Interaction prompt appears | ≤ 100 ms after entering range; range 2.2 m, cone 60° | WorldTests + play |
| Enter vehicle | ≤ 0.6 s door-to-drive; skippable at 0.3 s | play, animation length asserts |
| Camera | spring-arm lag 0.12 s; no clipping through walls; recentre in 1.5 s; FOV ramps with speed | play |
| Vehicle | weight transfer visible; brake from 60 km/h in < 30 m (pickup); tractor + trailer stable on 15 % grade; reset in 1.5 s | physics test scene with assertions |
| NPC reaction | look within 300 ms at 2 m; bark within 500 ms; flinch on bump instantly; schedules keep 95 % on time | headless: schedule simulation over 10 days |
| Traffic density | 20–40 vehicles active within 400 m; a car passes the farm gate at least every 60 s in daytime | headless counter |
| Pedestrians | 15–40 in town during the day; 2–6 at night | headless counter |
| World streaming | no hitch > 16 ms while driving 100 km/h across the map | profiler capture; TickDriver budget |
| Load times | title in 5 s, farm in 20 s on SSD; join a friend in 15 s | measured |
| Frame time | 60 fps on recommended spec; logic (TickScheduler) ≤ 2 ms/frame; UI ≤ 1 ms | TickDriver.OverBudgetFrames == 0 in tests |
| Audio | every action has a sound within 1 frame; engine pitch continuous; horns echo in town | play |
| Fail states | any stuck vehicle/worker recovers in 10 s; "Reset to road" always available | watchdog tests |
| Save | autosave 2 s after a change; never more than 10 s of progress lost | SaveData tests |

---

## 16. Art direction

### 16.1 Palette and light
Najd: warm sand (#D9B382), ochre tracks (#B8966F), sage-grey saltbush, deep palm green (#2F5E2B), white-plaster and mud brick (#C9A77B), turquoise doors, dusk magenta. Harsh noon sun (shadow strength 1.0, high exposure), golden hours long. Delta: saturated field green (#5B7A3B), black Nile silt (#3B3127), red brick (#9C4A3A), whitewash, blue-green canal water, soft humid light, morning haze, warm orange sodium lamps at night.

### 16.2 Materials
URP Lit PBR, 2k max, Poly Haven base set (credited), tri-planar ground blending, wind on foliage (existing Mazraa/Foliage shader), dust and heat shimmer post, wetness on rain. Vehicles: clear-coat paint, dust accumulation layer that washes at the station.

### 16.3 Characters and wardrobe (NpcWardrobe exists; extend)
Najd men: white/beige thobe, shemagh (red-white) or ghutra (white), occasionally bisht at festivals; farm workers in shalwar-style work clothes and caps (reflecting the real workforce), boys in thobes with caps. Women: abaya, often with a coloured farm dress underneath in family farms; always present in markets and schools. Delta men: galabiya (striped blue/grey/brown) with a scarf, taqiya, or trousers and shirt in town; women: colourful galabiyas with headscarf, black in some villages; children in school uniforms. No caricature; clothing per schedule (work vs Friday).

### 16.4 Architecture
Najd: mud-brick (adobe) with triangular roof vents, white plaster, wooden doors with brass, modern villas with sandstone cladding, concrete farm sheds, water towers, date drying yards, palm-frond fences. Delta: red fired brick with exposed concrete columns, pigeon towers (existing), domed saints' shrines, canal bridges, tuk-tuk stops, painted microbuses, railway crossing, mosque with green neon. Jordan/Morocco/Iraq/Oman in section 21.

### 16.5 UI style
Flat, warm, sand-and-ink: panels #F4EBDD with 1 px ink borders, accents in turquoise (#1E9C9C) and date-red (#9C2F2F); Tajawal for Arabic, Inter for Latin; large touch-friendly targets; icons line-based; RTL mirrored.

---

## 17. Audio
Oud/qanun/ney ambient score with regional variation (Najdi samri rhythms; Delta: shaabi-inspired and Nubian rhythms); dynamic layers by activity (driving adds percussion). Vehicle radio with 3 fake stations per region (news snippets about prices, call-in farming show, music) — content in packs. Procedural sound set exists (engine pitch, footsteps, animals, crickets); add dust storms, canal water, mosque adhan as a time marker (muted option), microbus horn melodies, market crowd walla in Arabic. Full Arabic VO for barks in wave 3; text barks at launch.

---

## 18. Monetisation and store
- Base game USD 24.99 (SAR 94, EGP 999 regional pricing). Launch discount 15 %.
- Region DLC USD 9.99 each (Nile Delta at launch +1 month, then one per 6–8 months). Entitlement via Microsoft Store add-ons (Dlc.Provider) and a key file for testers; ContentPacks registry gates content.
- Free: seasonal events, Harvest Pass, challenges, patches.
- Never: consumables, paid cosmetics, boosters, ads, loot boxes. Store description states it.

---

## 19. Accessibility
Remappable controls; hold-to-toggle options; subtitle size and background; colour-blind-safe field states (icons + colours); HUD scale 80–150 %; camera shake and motion blur off switches; auto-drive to waypoint option; one-handed vehicle mode (auto throttle); text-to-speech for cards (platform); no time-limited tutorials; difficulty sliders independent (economy, growth speed, driving assists); dyslexia-friendly font option; screen-reader labels on menus (Unity UI accessibility hooks).

---

## 20. Localisation
- EN and AR (MSA) at launch; all strings through `Loc` and data packs; Arabic shaping and RTL exist.
- Delta pack dialogue in **Egyptian Arabic** (ammiya) with an MSA fallback for UI; Najd barks in Najdi colloquial for flavour, MSA for UI.
- Numbers: Western Arabic numerals default, Eastern Arabic numerals option. Currency per region (SAR, EGP...). Dates: Gregorian with Hijri month shown in Najd during Ramadan/Eid events.
- Later: French (Morocco), Turkish, German (FS audience), Urdu.

---

## 21. DLC roadmap (regions)
| # | Region | Hook | Signature content | Target |
|---|---|---|---|---|
| 1 | Nile Delta (Egypt) | canal turn, cotton, rice, buffalo, microbus | done/in progress | launch + 1 month |
| 2 | Jordan Valley | below sea level, winter vegetables for export, bananas, dates, Dead Sea haze, water from the King Abdullah Canal | export truck convoys, greenhouse clusters, Bedouin tents | +8 months |
| 3 | Souss Valley (Morocco) | argan, citrus, greenhouse tomatoes, Atlas backdrop, souk days | argan cooperative chain, goats in trees, Berber villages, French strings | +14 months |
| 4 | Mesopotamia (Iraq) | Tigris–Euphrates canals, dates (Basra), wheat/barley, water buffalo in marshes, salinity crisis | reed houses (mudhif), marsh boats, drainage project | +20 months |
| 5 | Al Batinah (Oman) | falaj irrigation (UNESCO), limes, mangoes, fishing, coast | falaj time-share mechanic, dhow deliveries, Omani dishdasha/kumma | +26 months |

Each DLC = content pack (manifest in `Resources/Data/Packs`), its own regions.json entry, crops in Economy, missions/campaign/events/quests tagged with its id, look block, wardrobe set, radio station, 12-chapter campaign, 10 achievements.

---

## 22. Beta KPIs (closed beta, 300 players, 4 weeks)
- Crash-free sessions ≥ 99.5 %; zero softlocks reported twice.
- First sale by minute 8 for ≥ 85 %; day-2 return ≥ 45 %; week-1 return ≥ 30 %.
- Median session 48 minutes; ≥ 25 % of sessions co-op; co-op join success ≥ 95 % within 2 minutes.
- Frame time: ≥ 90 % of frames under 16.7 ms on recommended spec; TickScheduler over-budget frames < 0.5 %.
- ≥ 40 % of players use photo/clip mode once; ≥ 10 % export a creator code.
- Arabic UI users ≥ 50 %; Arabic string bugs reported < 20.
- Net promoter ≥ 40; "feels alive" ≥ 4/5 in the survey.

---

## Appendix A — Sources for real-world data

Agronomy and water
- FAO, *Crop Water Information* (per-crop water needs, growth stages, sensitivity): https://www.fao.org/land-water/databases-and-software/crop-information/en/
- FAO Irrigation and Drainage Paper 56, *Crop evapotranspiration* (Kc, ETo method): https://www.fao.org/4/x0490e/x0490e00.htm
- FAO Irrigation and Drainage Paper 29, *Water quality for agriculture* (salt tolerance): https://www.fao.org/4/t0234e/t0234e00.htm
- FAO, *Irrigation Water Management: Irrigation Methods* (efficiencies): https://www.fao.org/4/s8684e/s8684e00.htm
- FAOSTAT, Crops and livestock products (national yields, Saudi Arabia and Egypt): https://www.fao.org/faostat/en/#data/QCL
- FAO AQUASTAT country profiles (Egypt, Saudi Arabia water use): https://www.fao.org/aquastat/en/countries-and-basins/country-profiles/

Saudi Arabia
- General Authority for Statistics (GASTAT), average prices of goods and services; date production 2024: https://www.stats.gov.sa
- Ministry of Environment, Water and Agriculture (MEWA), agricultural statistics and water policy: https://www.mewa.gov.sa
- Saudi Grains Organization (SAGO) wheat procurement price (SAR 1,700/t, 2022): https://www.sago.gov.sa
- Already compiled in this repo: `DATA_SOURCES.md` (GASTAT retail prices, Qassim dates, alfalfa cutting regime).

Egypt
- Central Agency for Public Mobilization and Statistics (CAPMAS), agricultural statistics bulletins: https://www.capmas.gov.eg
- Ministry of Agriculture and Land Reclamation (crop calendars, cotton and rice regulations): https://www.agri.gov.eg
- Ministry of Supply wheat procurement price announcements (EGP 2,200/ardeb, 2024/25 season) and cotton price committee announcements (2024) — as reported by Reuters, Ahram Online and Egypt Today; verify the current season's figure before each price retune.
- Egyptian clover, faba bean, rice and cotton calendars: FAO GIEWS Country Brief Egypt: https://www.fao.org/giews/countrybrief/country.jsp?code=EGY

Other
- Red palm weevil management (FAO): https://www.fao.org/palm-weevil/en/
- Aflaj irrigation systems of Oman (UNESCO): https://whc.unesco.org/en/list/1207/
- Fuel prices: Saudi Aramco diesel SAR 1.15/L (2024); Egypt diesel EGP 13.5/L (October 2024 adjustment) — press reports; retune per patch.

Values marked *est.* are standard agronomy ranges not taken from an official series during this pass; replace them when the team obtains MEWA/CAPMAS farm-gate data.

## Appendix B — Tuning constants currently in data
`economy.json` (goods, animals, feed, fieldCrops with growSeconds/yieldKg), `Economy/30_crops.json`, `soil.json` (crop cardinal temperatures, ET, NPK uptake, salt tolerance; irrigation methods), `regions.json` (heat/rain/market multipliers, look, canal turn), `market.json` (buyers, sell points), `missions.json`, `campaign.json`, `events.json`, `quests.json`, `farmlife.json`, `fish.json`, `processing.json`, `scenarios.json`, `sponsors.json`, `Packs/*.json` (content pack manifests). The GDD tables are the target; a data patch brings the files to them (ROADMAP item W2-10).
