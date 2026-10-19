# Third-party assets

| Asset | Author | License | Used as |
|---|---|---|---|
| Car Kit 3.1 (truck-flat, tractor + colormap) | Kenney (kenney.nl) | CC0 1.0 | Resources/Models/Truck.fbx, Tractor.fbx |
| Nature Kit (crops, fence) | Kenney (kenney.nl) | CC0 1.0 | Resources/Models/Crops/*, Fence.fbx |
| Farm Animals Pack (cow, sheep) | Quaternius | CC0 1.0 | Resources/Models/cow.fbx, sheep.fbx (re-exported to face +Z, legacy animations) |
| Chicken (animated) | Čestmír Dammer (CDmir), opengameart.org | CC0 | Resources/Models/chicken.fbx (converted from .blend) |
| Mini Characters (4 people, 30+ animations) | Kenney (kenney.nl) | CC0 1.0 | Resources/Models/Characters (shopkeepers, workers, buyers, drivers) |
| Animal Pack Vol.2 (dog, cat, eagle) | Quaternius | CC0 1.0 | Resources/Models/dog, cat, eagle |
| Farm Animals Pack (horse) | Quaternius | CC0 1.0 | Resources/Models/horse |
| Tajawal Bold font | Boutros Fonts | SIL OFL 1.1 | Resources/Fonts (Arabic UI) |
| Farm Crops (crop growth stages) | asset already in the project (Assets/FarmCrops) | its original licence | Resources/Models/FarmCrops: copies of 40 stage meshes (materials stay in Assets/FarmCrops) |
| Sounds (engine, wind, crickets, birds, footsteps, animal calls) | generated in code (Audio/ProceduralAudio.cs) | project's own | no audio files |

Recommended next imports (all free):
- Quaternius "Farm Buildings" (CC0) - barn, silo, shop stall: Resources/Models/Barn, ShopStall.
- Poly Haven HDRIs and textures (CC0) for sky and ground.
Any model placed at those paths replaces the placeholder automatically and is scaled to fit.


## Graphics makeover (Resources/Look) - Poly Haven, CC0 1.0

All from Poly Haven (https://polyhaven.com, licence CC0 1.0, https://polyhaven.com/license), downloaded via api.polyhaven.com.
Textures were resized and re-packed with a Python script (mask = R metal, G AO, B height, A smoothness). Foliage atlases were composed
from the leaf textures listed below (cluster cards, grass tufts). The date palm frond atlas is procedurally drawn (project's own).

| Asset (Poly Haven id) | Author | License | Used as |
|---|---|---|---|
| Sandy Gravel 02 (sandy_gravel_02) | Dario Barresi | CC0 1.0 | Resources/Look/Textures/sand_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Gravelly Sand (gravelly_sand) | Dario Barresi | CC0 1.0 | Resources/Look/Textures/gravel_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Dry Riverbed Rock (dry_riverbed_rock) | Amal Kumar | CC0 1.0 | Resources/Look/Textures/rock_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Brown Mud 02 (brown_mud_02) | Rob Tuytel | CC0 1.0 | Resources/Look/Textures/soil_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Grass Ground (grass_ground) | Charlotte Baglioni | CC0 1.0 | Resources/Look/Textures/grass_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Aerial Mud 1 (aerial_mud_1) | Rob Tuytel | CC0 1.0 | Resources/Look/Textures/tilled_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Rough Plaster Brick 02 (rough_plaster_brick_02) | Rob Tuytel | CC0 1.0 | Resources/Look/Textures/mudbrick_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| White Stucco (white_stucco) | Amal Kumar | CC0 1.0 | Resources/Look/Textures/plaster_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Concrete Wall 008 (concrete_wall_008) | Dario Barresi, Charlotte Baglioni | CC0 1.0 | Resources/Look/Textures/concrete_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Corrugated Iron (corrugated_iron) | Jenelle van Heerden, Dimitrios Savva | CC0 1.0 | Resources/Look/Textures/corrugated_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Rusty Metal Sheet (rusty_metal_sheet) | Amal Kumar | CC0 1.0 | Resources/Look/Textures/metal_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Weathered Brown Planks (weathered_brown_planks) | Dimitrios Savva, Rico Cilliers | CC0 1.0 | Resources/Look/Textures/wood_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Rough Linen (rough_linen) | colormass, Rico Cilliers | CC0 1.0 | Resources/Look/Textures/linen_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Curly Teddy Natural (curly_teddy_natural) | colormass, Rico Cilliers | CC0 1.0 | Resources/Look/Textures/wool_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Hessian 230 (hessian_230) | colormass, Rico Cilliers | CC0 1.0 | Resources/Look/Textures/hessian_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Palm Bark (palm_bark) | Charlotte Baglioni | CC0 1.0 | Resources/Look/Textures/palmbark_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Bark Brown 02 (bark_brown_02) | Rob Tuytel | CC0 1.0 | Resources/Look/Textures/bark_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Palm Tree Bark (palm_tree_bark) | Dimitrios Savva, Rico Cilliers | CC0 1.0 | Resources/Look/Textures/eucbark_d/_n/_m (albedo, normal, packed mask; resized to 1K / 512) |
| Jacaranda Tree (jacaranda_tree) leaves | Poly Haven | CC0 1.0 | acacia_leaves_d.png (acacia / ghaf leaf clusters) |
| Grass Medium 01 (grass_medium_01) diffuse, dry diffuse, alpha | Poly Haven | CC0 1.0 | grass_green_d.png, grass_dry_d.png (instanced grass tufts, dry shrubs) |
| Island Tree 02 (island_tree_02) leaves | Poly Haven | CC0 1.0 | euc_leaves_d.png (eucalyptus clusters) |
| Shrub 04 (shrub_04) leaves | Poly Haven | CC0 1.0 | sidr_leaves_d.png (sidr / desert shrub clusters) |
| 2K versions (Ultra preset) of sandy_gravel_02, gravelly_sand, dry_riverbed_rock, brown_mud_02, grass_ground, aerial_mud_1, rough_plaster_brick_02, white_stucco, palm_bark | same authors as above | CC0 1.0 | Resources/Look/Textures2K/*_d.jpg / *_n.jpg (2K diffuse + OpenGL normal, re-encoded JPG q90) |
| Date palm frond atlas | generated (project's own) | - | palm_frond_d.png |
| Foliage shader Mazraa/Foliage | project's own | - | Resources/Look/Shaders/MazraaFoliage.shader |

## Placeholder pass (model catalog: Resources/Models/catalog.json, see PLACEHOLDERS.md)

| Asset | Author | License | Used as |
|---|---|---|---|
| City Kit Industrial 2.0 (building-h, detail-tank, detail-tank-large, solar-panel-landscape-group, water-tower, shipping-container-a/b + colormap) | Kenney (kenney.nl) | CC0 1.0 | ThirdParty/KenneyCityKitIndustrial: Barn, Utilities/SolarPanel, WaterTank, Biogas |
| Car Kit (sedan, suv, van, hatchback-sports, delivery + colormap) | Kenney (kenney.nl) | CC0 1.0 | ThirdParty/KenneyCarKitTown: town traffic / parked cars |
| Kloofendal 48d Partly Cloudy (Pure Sky) HDRI, 2K (kloofendal_48d_partly_cloudy_puresky) | Greg Zaal, Poly Haven | CC0 1.0 | ThirdParty/PolyHavenSky: Look/Materials/SkyHDRI (Ultra sky) |
| Asphalt 02 (asphalt_02) | Poly Haven | CC0 1.0 | Resources/Look/Textures/asphalt_d/_n/_m (albedo, normal, packed mask; 1K / 512): LookMat.Asphalt roads |

## Realistic pass (art-realistic branch) - converted with Blender (bpy), see PLACEHOLDERS.md

| Asset | Author | License | Used as |
|---|---|---|---|
| Portable Generator (portable_generator) | Poly Haven | CC0 1.0 | ThirdParty/PolyHavenFarmProps: `Utilities/Generator` (decimated 50%) |
| Modular Electricity Poles (modular_electricity_poles, preset 03) | Poly Haven | CC0 1.0 | ThirdParty/PolyHavenFarmProps: `Utilities/PowerPole` (decimated 50%) |
| Covered Car (covered_car) | Poly Haven | CC0 1.0 | ThirdParty/PolyHavenFarmProps: `CoveredCar` parked in town |
| Barrel 03 (barrel_03), Wooden Crate 02 (wooden_crate_02), Plastic Crate 02 (plastic_crate_02), Metal Jerrycan (metal_jerrycan), Cement Bag (cement_bag) | Poly Haven | CC0 1.0 | ThirdParty/PolyHavenFarmProps: `Props/*` barn yard clutter |
| Shrub 02 (shrub_02), Wild Rooibos Bush (wild_rooibos_bush) | Poly Haven | CC0 1.0 | ThirdParty/PolyHavenFarmProps: `Shrub`, `DryShrub` |
| Realtime Rancher's 3D Model Pack (cow) | LD, opengameart.org | CC0 | ThirdParty/OGARealtimeRanchers: `cow` (2K textures) |
