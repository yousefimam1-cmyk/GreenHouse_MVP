# Mazraa vs Farming Simulator 25 - parity audit

Goal (from the user): "an exact replica of Farming Simulator 25 with no art copied, mixing in the best of other
high-quality games". The legal and store-safe reading we work to: match FS25's **gameplay systems, controls, UI
structure / information architecture and feel**, using only Mazraa's own art, names and text. That means no Giants
logos, icons, screenshots, model rips, real machinery brand names or verbatim FS text. Where a design element is
generic (a clock block, tabbed menus, Tab to switch vehicles, Q to attach), we match it. Where it is expressive (art,
icons, wording), we make our own.

Status key: **done** = playable, saved, tested headless; **partial** = exists in a simpler form (the gap is noted);
**missing** = not in the game yet. File paths are under `Assets/Scripts/` unless noted.

## Sources used

- FS25 overview and feature list: <https://farmingsimulator.wiki.gg/wiki/Farming_Simulator_25>
- FS25 PC keyboard controls (Tab next vehicle, Q attach / detach, V lower / raise, H helper, F1 help): <https://www.magicgameworld.com/?p=107752>
- FS25 manual (official, controls and menus): <https://manuals.giants-software.com/Farming_Simulator_25/Basegame/lang/en/FS25-manual_EN.pdf>
- Beginner guides: <https://www.bisecthosting.com/pt-pt/blog/farming-simulator-25-beginners-guide-controls-vehicles-harvest-crops-walkthrough>
- Feature roundups: <https://www.thrustmaster.com/news/farming-simulator-25-great-new-features-that-you-wont-want-to-miss-out-on/>

## Summary

| | done | partial | missing | total |
|---|---|---|---|---|
| Systems in sections 1-6 | 44 | 18 | 11 | 73 |

This session added or finished: the FS-style HUD layout, Tab vehicle switching, implement attach / detach /
lower / raise / fold, the tabbed farm menu (Map, Calendar, Weather forecast, Vehicles, Animals, Help, Statistics
distance), the GPS route by road and dealer paint and wheel configurations. All of these are marked (new) below.

## 1. HUD

| System (FS25) | Mazraa | Status | Where |
|---|---|---|---|
| Top-right block: clock, date / season, weather + temperature, money | Clock block. Status / mission and training panels stack under it; mirrored in Arabic | done (new) | `UI/GameHUD.cs` (ClockText, StackRightColumn) |
| Bottom-right vehicle gauges: speed, fuel, damage, cruise, gear / RPM | Gauge cluster: big speed + cruise / limiter, RPM / gear, fuel, damage and load bars, implement state, drivetrain details | done (new) | `UI/GameHUD.cs` (RefreshGauges, SpeedLine) |
| Top-left context help (F1-style key hints for what you are doing) | Context help box: on foot / truck / tractor. Names the implement in reach and follows the device (gamepad says "hold Y") | done (new) | `UI/ContextHelp.cs` |
| Notifications top-centre | Alerts use the scene's alert text (one line at a time). There is no stacked feed | partial | `Managers/UIManager.cs` |
| Minimap bottom-left with field numbers | Round minimap bottom-left (bottom-right in Arabic); field plots show their number; GPS route dots | done (new) | `UI/Minimap.cs` |
| Minimap modes (size / zoom cycling, hide) | One size; the full map opens with N | missing | - |
| Direction / objective hints | Objective marker + HUD arrow to the next stop | done | `UI/ObjectiveMarker.cs`, `UI/GameHUD.cs` |

## 2. In-game menu (one tabbed screen)

The farm menu is one tabbed screen. LB / RB or Q / E switch tabs, as in FS, and 1-9 / 0 jump to a tab. A tab strip
runs across the top. Open it with I or O, or from the pause menu's "Farm menu (all tabs)".

| Tab (FS25) | Mazraa | Status | Where |
|---|---|---|---|
| Map with filters and field info | Map tab: every field with number, state, crop, growth and area. Enter sets a waypoint and opens the full map, which has a legend, place list, fast travel and click-to-waypoint. There are no overlay filters (growth / soil colouring) on the map image | partial | `UI/MenuPages.cs`, `UI/Minimap.cs`, `World/WorldMap.cs` |
| Calendar per crop | Sow (S) / harvest (H) per season and crop, with the current season marked. Read from `ICropCalendarSource`, so FarmCore can plug in | done (new) | `UI/MenuPages.cs` |
| Weather forecast | Today + 5 days with day / night temperatures. The forecast is pre-rolled, so it comes true (saved) | done (new) | `Farming/WorldClock.cs`, `UI/MenuPages.cs` |
| Prices per selling point | Prices tab per good and per selling point, with where to sell | done | `UI/EconomyPages.cs`, `Managers/MarketManager.cs` |
| Vehicles: list / locate / sell / lease | Vehicles tab: fuel, condition, km, operating hours, distance away. Actions: enter, locate (waypoint), repaint, change wheels. Implements can be sold for half price and bought back. The truck and tractor themselves cannot be sold, and there is no leasing | partial (new) | `UI/MenuPages.cs`, `Vehicle/VehicleSwitcher.cs` |
| Finances | Accounts by category, the last 7 days and the season | done | `UI/EconomyPages.cs`, `Finance/FinanceLedger.cs` |
| Statistics | Money earned, harvest, days, missions, animals, distance driven | done | `UI/FarmInfoScreen.cs` |
| Animals | Herds (owned / max), product and interval, ready products, feed, water, cleanliness, health, productivity | done (new) | `UI/MenuPages.cs` |
| Production | Production lines with supply / collect / sell / auto | done | `UI/EconomyPages.cs`, `Fish/ProcessingYard.cs` |
| Contracts | Contracts board, accept, progress | done | `UI/EconomyPages.cs` |
| Help / encyclopedia | 9 topics (start, driving, implements, fields, weather, animals, selling, map / GPS, dealer), EN / AR, device glyphs | done (new) | `UI/MenuPages.cs` |
| Settings | Settings hub: graphics, controls with rebinding, game, accessibility, audio | done | `UI/GameMenuSettings.cs`, `Input/GameSettingsOptions.cs` |

## 3. Vehicles, implements and driving

| System (FS25) | Mazraa | Status | Where |
|---|---|---|---|
| Tab cycles vehicles (also from on foot) | Tab: on foot it enters the nearest vehicle, in a vehicle the next one, and the old vehicle stays parked. Gamepad switches from the Vehicles tab | done (new) | `Vehicle/VehicleSwitcher.cs` |
| Attach / detach implements (Q) with a physics hitch | Implements stand in the yard. Reverse to within 3.2 m and press Q (hold Y) to attach; Q detaches where you are. Poses are saved. The implement snaps rigidly to the hitch: no joint physics, no collision while parked | partial (new) | `Vehicle/TractorHitch.cs`, `Vehicle/ParkedImplement.cs` |
| Lower / raise (V) | V (tap Y). Only a lowered, unfolded implement works the ground | done (new) | `Vehicle/TractorHitch.cs` |
| Fold / unfold | Hold V. Lowering unfolds first, and a raised implement auto-folds at road speed | done (new) | `Vehicle/TractorHitch.cs` |
| Turn implement on / off (B) | Implements work whenever they are lowered | missing | - |
| Workers / AI helpers on fields and courses (H) | Hired field hand, greenhouse worker, herder and driver do jobs on a timer. They do not drive vehicles along courses | partial | `Finance/WorkerManager.cs` |
| GPS / steering assist | GPS straight-line lane assist (Slash / R3) + GPS route by road to the waypoint | done | `Vehicle/DriveAssists.cs`, `UI/GpsRoute.cs` |
| Cruise control / speed limiter | Y / D-pad up, +/- set speed | done | `Vehicle/DriveAssists.cs` |
| Engine start, gearbox (auto / manual / CVT), RPM, slip, 4WD, diff lock, tyre pressure | Full drivetrain model | done | `Vehicle/Drivetrain.cs`, `Vehicle/Vehicle.cs` |
| Fuel, wear, damage, repair, towing | Fuel by load, engine / tyre wear, repair at workshops, tow when out of fuel | done | `Vehicle/Vehicle.cs` |
| Camera: inside cab / outside orbit | Chase camera in vehicles; first / third person on foot. No cab view or free orbit | partial | `Vehicle/Vehicle.cs`, `Input/ThirdPersonView.cs` |
| Lights, beacons, horn | Automatic lamps / beacons / brake lights. No light modes or horn key | partial | `Vehicle/VehicleVisuals.cs` |
| Radio | - | missing | - |
| Dealership configurations (colour, wheels, extras) | Paint (7 Mazraa colours) and wheels (standard / wide flotation / road; wide = always low pressure) for the farm's vehicles at a workshop. Front weights in the shop. No catalogue of new vehicles to buy | partial (new) | `Vehicle/VehicleConfig.cs`, `Vehicle/Tractor.cs` |
| Leasing | - | missing | - |

## 4. Field work chain and crops

FarmCore (another helper) is rebuilding field farming from scratch: a cell-grid painted by implements, growth and
irrigation. The hitch exposes hooks for it: `Tractor.ImplementLowered`, `ImplementFolded`, `WorkingWidth`,
`HitchPoint`, and the `ImplementChanged` / `ImplementWorking` events. The menu tabs read `FarmMenuData.Fields` /
`FarmMenuData.Calendar`, which FarmCore can replace.

| System (FS25) | Mazraa | Status | Where |
|---|---|---|---|
| Cultivate | - (FarmCore) | missing | - |
| Plough | Plow implement / E on a plot | done | `Vehicle/Tractor.cs`, `Farming/FieldPlot.cs` |
| Sow | Seeder (best seed in stock), sowing density | done | `Vehicle/Tractor.cs`, `Farming/SoilManager.cs` |
| Fertilise | Soil fertility and care items. No spreader implement | partial | `Crops/CropCareManager.cs` |
| Spray (weeds / pests) | Pest cures by hand. No sprayer implement | partial | `Crops/CropCareManager.cs` |
| Roll / mulch | - | missing | - |
| Harvest | Harvester header on the tractor. No combine with a grain tank, pipe or unloading | partial | `Vehicle/Tractor.cs` |
| Swath, bale, wrap | Hay bales exist as carriable items. No baler or wrapper | partial | `Life/Carriable.cs` |
| Collect / store | Farm storage (abstract), silo selling place in the world | partial | `Managers/EconomyManager.cs`, `World/OpenWorld.cs` |
| Stones / weeds / lime / rolling | Precision soil: pH, nutrients, limiting factor, yield history. No stones or rolling | partial | `Farming/SoilManager.cs` |
| Crop growth stages and seasons | Stage models, sow seasons, weather effects | done | `Graphics/CropVisuals.cs`, `Farming/FieldPlot.cs` |
| Irrigation | Methods per field (drip lines etc.), well, utilities grid | done | `Farming/SoilManager.cs`, `Utilities/` |
| Trailers / tipping | Truck cargo is loaded and unloaded automatically on pads. No trailers or tipping | missing | `Vehicle/FarmTruck.cs` |
| Silos / bunkers | Storage only. No bunker silo or silage | partial | `Managers/EconomyManager.cs` |
| Pallets | - | missing | - |
| Forklift / front loader / telehandler | Carrying by hand only | missing | `Life/PlayerLife.cs` |
| Greenhouses (Mazraa extra) | Module-driven greenhouses with crop beds | done | `Managers/GreenhouseManager.cs` |
| Orchards / trees (date palms) | Orchard trees and saplings | done | `Crops/OrchardManager.cs` |

## 5. Animals, production and economy

| System (FS25) | Mazraa | Status | Where |
|---|---|---|---|
| Animal husbandry (buy, feed, water, clean, products, young) | Pen with feed, water, cleaning, products; herd health and the vet | done | `Farming/AnimalPen.cs`, `Life/HerdManager.cs` |
| Feed mixing (TMR) | One feed type | missing | - |
| Manure / slurry | Cleaning the pen. No manure as a product or for spreading | partial | `Farming/PenTask.cs` |
| Production chains | Processing yard (mill etc.) with inputs / outputs, auto mode | done | `Fish/ProcessingYard.cs` |
| Selling points with dynamic prices | Several selling points per map, daily prices, a destination for loads | done | `Managers/MarketManager.cs` |
| Contracts / missions | Buyer contracts + field contracts; campaign missions | done | `Managers/MarketManager.cs`, `Managers/MissionManager.cs` |
| Buying land | Farmland parcels for sale in the open world, land office | done | `World/WorldLand.cs`, `Finance/LandOffice.cs` |
| Loans / bank | Bank with loans and interest | done | `Finance/BankManager.cs` |
| Construction / placeable buildings | Decorations, farm decor, utility yard placement. No free construction of sheds or silos | partial | `Cozy/Decorations.cs`, `FarmLife/FarmDecor.cs`, `Utilities/UtilityYard.cs` |
| Forestry | - | missing | - |
| Fishing / aquaculture (FS25 expansion) | Fishing spots, fish farm, hives, dovecote | done | `Fish/` |

## 6. World, meta and multiplayer

| System (FS25) | Mazraa | Status | Where |
|---|---|---|---|
| Large map with roads, towns, selling places | 2.4 km open world, ring highway, places, signposts | done | `World/` |
| Fast travel to visited places | For a fee by distance | done | `World/WorldMap.cs` |
| Day / night, seasons, weather (rain, heat, fog, storms) | Day / night, 4 seasons, sunny / cloudy / rain / heatwave, dust storms (events) | done | `Graphics/DayNightCycle.cs`, `Farming/WorldClock.cs`, `Events/EventDirector.cs` |
| Multiplayer farms with roles / permissions | Co-op sessions with remote players, vehicles and chat. No separate farms, roles or permissions | partial | `Multiplayer/` |
| Achievements | 24 achievements with progress | done | `Managers/AchievementManager.cs` |
| Career start options / economy difficulty | New farmer / manager / start-from-scratch, economy levels, free play, challenges | done | `Managers/Career.cs`, `Managers/GameModes.cs` |
| Save slots, autosave | Career slots, autosave | done | `UI/SaveSlots.cs` |
| Tutorials | Controls tutorial, tutorial overlay, academy | done | `UI/ControlsTutorial.cs`, `UI/TutorialOverlay.cs` |
| Key rebinding, gamepad glyphs (Xbox / PlayStation) | Done | done | `Input/GameInput.cs`, `Input/Glyphs.cs` |
| Photo mode | Done | done | `FarmLife/PhotoMode.cs` |

## 7. Best of other games: concrete adaptations

| Idea (game) | Adaptation in Mazraa | Status |
|---|---|---|
| Calendar, heart events, festivals (Stardew Valley) | Farm life journal, friendship, festivals, daily quests | done (`FarmLife/`, `Cozy/`) |
| Order board (Hay Day) | Roadside stand orders | done (`FarmLife/RoadsideStand.cs`) |
| GPS route line and road atlas (Euro Truck Simulator 2) | Orange route by road over the road graph: in the world, on the minimap and on the full map, with a HUD "GPS: x km by road" line | done (new, `UI/GpsRoute.cs`) |
| Photo mode polish (Forza Horizon) | Photo mode exists. Next: depth of field / filters / hide-HUD presets and a "pose the vehicle" option | partial |
| Building / construction placement (Ranch Simulator) | Decorations placement exists. Next: grid placement of sheds, silos and fences with a cost preview and a terrain-flatten footprint | partial |
| No-fail relaxed mode (Farm Together) | Free play with unlimited money exists (GameModes). Next: a "relaxed" toggle (no fuel use, no wear, crops never wither) | partial |
| Auto-pilot courses (Courseplay-style FS mods) | Next: let hired drivers follow a recorded GPS course on a field using the new `WorkingWidth` / `ImplementWorking` hooks | missing |
| Dealer configurator with live preview (Forza / FS shop) | Paint and wheels apply live on the vehicle. Next: a 3D preview turntable in the dealership | partial (new) |
| Learn while farming (agronomy) | Learn mode quizzes and crop reference cards | done (`Learn/`) |

## 8. What is next (highest impact first)

1. Combine harvester with a grain tank, unloading pipe and a tipping trailer. This is FS's core loop and needs FarmCore cells.
2. Turn implements on / off (B), plus cultivator / spreader / sprayer / roller implements on the same hitch hooks.
3. AI helpers that drive a field on a course (H), using `WorkingWidth` and the GPS line.
4. Vehicle shop catalogue: buy / sell / lease new tractors and trailers with the configurator (colour, wheels, extras).
5. Cab camera + free orbit camera; light modes and horn; a radio with Mazraa's own stations.
6. Map tab overlays (growth, soil, field owner) and minimap size modes.
7. Pallets + a front loader / telehandler; bales from a baler and wrapper.
8. Stacked notification feed top-centre (FS style) replacing the single alert line.
