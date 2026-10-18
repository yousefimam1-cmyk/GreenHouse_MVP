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

Not replaced yet (no reachable CC0 source that fits): tractor/trailer (still Kenney Car Kit, 3.6 m to match the
physics body; final target ~4.5 m with a retuned collider), silo, regional town houses (the procedural Najdi / delta
houses already use Poly Haven PBR materials and fit the setting better than the available Western suburban kits).
Quaternius Farm Buildings / Ultimate Nature (CC0) were the first choice but their Google Drive downloads were over
quota; add them as `Assets/ThirdParty/QuaterniusFarmBuildings` + catalog lines when reachable.
