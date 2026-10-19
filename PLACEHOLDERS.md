# Placeholder art (free CC0)

Third-party packs live untouched in `Assets/ThirdParty/<Pack>/` (models under `Resources/TP_<Pack>/` so they load at
runtime). Game code never names those paths: it asks `Build.TryModel(id)`, which reads our catalog
`Assets/Resources/Models/catalog.json` (`ModelCatalog`): id -> pack path, real-world fit size, yaw, fallback id.
If a model is missing (headless tests, stripped pack) the fallback id, then `Resources/Models/<id>`, then the
caller's primitive build is used. Replacing a placeholder = drop the final model in and change one catalog line.

| Asset | Source | License | Replaces | Planned final replacement |
|---|---|---|---|---|
| Kenney City Kit Industrial: building-h (gable shed) | https://kenney.nl/assets/city-kit-industrial | CC0 1.0 | Pen barn cubes (`Barn`, 5.2 m) | Bespoke red timber/steel barn with opening doors |
| Kenney City Kit Industrial: solar-panel-landscape-group | same | CC0 1.0 | `Utilities/SolarPanel` cubes | Realistic ground-mount PV array |
| Kenney City Kit Industrial: detail-tank, detail-tank-large | same | CC0 1.0 | `Utilities/WaterTank`, `Utilities/Biogas` primitives | Polyethylene water tank, biogas digester dome |
| Kenney City Kit Industrial: water-tower, shipping-container-a/b | same | CC0 1.0 | (catalog ids `FarmWaterTower`, `StorageContainerA/B`, not placed yet) | Farm water tower, storage container |
| Kenney Car Kit: sedan, suv, van, hatchback-sports, delivery | https://kenney.nl/assets/car-kit | CC0 1.0 | Town traffic and two parked cars (were all the pickup) - group `town_car` | Fictional-brand realistic cars |
| Poly Haven HDRI kloofendal_48d_partly_cloudy_puresky (2K .hdr) | https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky | CC0 1.0 | Procedural sky on the Ultra preset (`Look/Materials/SkyHDRI`, turned to follow the sun) | Region-specific desert / delta HDRI set with a time-of-day blend |
| Poly Haven asphalt_02 (1K, repacked to _d/_n/_m) | https://polyhaven.com/a/asphalt_02 | CC0 1.0 | Grey-tinted concrete on open-world asphalt roads (`LookMat.Asphalt`) | Road material with lane wear / decals |
| Poly Haven portable_generator (1K PBR, decimated 50%) | https://polyhaven.com/a/portable_generator | CC0 1.0 | `Utilities/Generator` yellow cube (real 0.82 m size) | Larger diesel genset |
| Poly Haven modular_electricity_poles (preset 03: pole + transformer, decimated 50%) | https://polyhaven.com/a/modular_electricity_poles | CC0 1.0 | `Utilities/PowerPole` cylinder + transformer cube (native 6.1 m) | - |
| Poly Haven covered_car | https://polyhaven.com/a/covered_car | CC0 1.0 | One parked town sedan (`CoveredCar`, fallback `TownSedan`) | - |
| Poly Haven barrel_03, wooden_crate_02, plastic_crate_02, metal_jerrycan, cement_bag | https://polyhaven.com/models | CC0 1.0 | New yard clutter beside the pen barn (`Props/*`, no primitive) | - |
| Poly Haven shrub_02 (variant c), wild_rooibos_bush (variant a) | https://polyhaven.com/a/shrub_02, https://polyhaven.com/a/wild_rooibos_bush | CC0 1.0 | Procedural open-land shrubs (`Shrub`, `DryShrub`) | Regional desert shrubs with LODs + wind |
| Realtime Rancher's cow (2K PBR, 6.3k tris) | https://opengameart.org/content/realtime-ranchers-3d-model-pack | CC0 | Quaternius low-poly cow (`cow`; no rig, so FarmAnimal sways it instead of playing clips) | Rigged realistic cow |

Not replaced yet (searched Poly Haven, ambientCG, Kenney, OpenGameArt CC0, Khronos glTF samples; no reachable CC0 model that
is realistic): tractor, trailer, combine, silo, barn, town houses/shops, people and the other animals. Kenney/Quaternius
placeholders stay; the tractor model is now fitted to a real-world 4.5 m (collider 2.5 x 2.0 x 4.25 m). Trees stay
procedural (Poly Haven tree scans are 60-75 MB / several hundred k tris each: unusable for scattered trees; their leaf
textures already feed the procedural trees). The OpenGameArt "Old Wood Barn" (CC0) was tried and rejected (broken walls
after conversion). Quaternius Farm Buildings (Drive) is low-poly, not realistic, and was skipped.
Converted packs (Blender 5.1 via bpy): single FBX per model, front = +Z, bottom-centre pivot, metres; textures repacked
to `_d` albedo (+alpha), `_n` OpenGL normal, `_m` mask (R metal, G AO, A smoothness); URP Lit `.mat` files in
`Materials/` are remapped onto the FBX via the model `.meta` (so alpha-clip / normal / mask keywords ship).
