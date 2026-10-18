# Mazraa: "GTA feel in a farming sim" plan

Goal: a responsive, bug-free open world that feels alive like GTA, with Farming Simulator 25-depth farming.
Rule: no copied art or names from other games; mechanics and feel only.

## Wave 1 (running now, separate helpers, merged one at a time with the full test suite)
| Track | Scope |
|---|---|
| World + town | Open rim/hills, roads on the ground at real widths, no stuck spots + rescue, visible far world, full town (20+ detailed buildings, working shops, mosque/cafe/souq), people in regional clothing, Delta village variant |
| Movement feel | FS-like on-foot responsiveness, frame-rate independent, camera, pets visible, high contrast only affects UI, natural exposure |
| Max graphics (URP) | STP/TAA, soft shadows, SSAO, probes, 2K textures on Ultra, water shader, lens flare, denser grass, stripper kept in sync |
| FS25 parity | Parity audit doc, FS-style HUD, Tab vehicle switching, implement attach/lower, tabbed in-game menu, GPS route line, dealership configs |
| New farming core | Cell-grid fields painted by implements, crop growth model, real irrigation network (sources, pumps, pipes, pivots, drip, flood, sensors, schedules), replaces old plots |
| Online play | Relay join codes, lobby, LAN, safety (merging now) |

## Wave 2 (after wave 1 merges, because it builds on the town, roads and new farming)
| Track | Scope |
|---|---|
| Living traffic | AI cars/trucks on the road network with lanes, junction rules, horns, reacting to the player; pedestrians with daily schedules (work, prayer times, cafe, souq), reacting to bumps/horns |
| Vehicle feel | GTA-responsive handling layer over the drivetrain (arcade assist slider), cameras (chase/cab/cinematic), enter/exit any vehicle animation, radio stations (original music) |
| Interaction layer | One context-action system for everything (talk, buy, use, pick up, enter), consistent prompts, no dead buttons |
| AI workers on new fields | Hired helpers driving implements over the new cell fields with courses |

## Wave 3 (quality gate before every build)
| Track | Scope |
|---|---|
| Bug hunt + performance | Static audit for real-Unity compile risks, null/exception sweeps, input latency, frame-time budget per system, memory budget, save/load fuzz, co-op desync checks |
| Playtest pass | Build, play in Unity on the user's PC, fix what is seen (black screens, stuck spots, missing objects), Store package |
