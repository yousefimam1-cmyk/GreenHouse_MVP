# Tests

Run without Unity (Linux/macOS with mono, or WSL):

- `python3 Tests/static_tests.py .` - 16 checks: .meta files, GUIDs, missing scripts in scenes/prefabs, class/file names,
  Resources paths, data JSON cross-references, translations for every UI string, key bindings.
- `Tests/Headless/run.sh` - 74 gameplay checks on a fake UnityEngine: boot, greenhouses, growth/harvest, fields, tractor,
  truck deliveries, market, shop, animals, animated life (dog follows you, eagles, shopkeepers, drivers), calendar, missions (incl. the last one), training, Arabic, save/restart, new game,
  10 simulated days without errors.

- `Tests/Headless/run.sh` also covers crop stage models, desert dressing keep-outs, quality presets, weather FX,
  procedural sounds (engine pitch, footsteps, animal calls, crickets) and touch controls.
- `python3 Tests/unused_assets.py . > Tests/UNUSED_ASSETS.md` - assets nothing in the build uses (report only).

On GitHub: `.github/workflows/tests.yml` runs the headless and static tests on every push/PR to mazraa-dev, and the
Unity EditMode tests when the UNITY_LICENSE secret is set.

These do not replace playing the game in the Unity editor (rendering, physics, input and UI layout are not simulated).

In Unity: Window > General > Test Runner > EditMode > Run All (Assets/Tests/Editor/MazraaEditorTests.cs):
data and models import, Arabic shaping, and a Play-mode boot of Blak_Pulse_Main.
