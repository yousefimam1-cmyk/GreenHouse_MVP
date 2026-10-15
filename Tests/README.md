# Tests

Run without Unity (Linux/macOS with mono, or WSL):

- `python3 Tests/static_tests.py .` - 16 checks: .meta files, GUIDs, missing scripts in scenes/prefabs, class/file names,
  Resources paths, data JSON cross-references, translations for every UI string, key bindings.
- `Tests/Headless/run.sh` - 68 gameplay checks on a fake UnityEngine: boot, greenhouses, growth/harvest, fields, tractor,
  truck deliveries, market, shop, animals, calendar, missions (incl. the last one), training, Arabic, save/restart, new game,
  10 simulated days without errors.

These do not replace playing the game in the Unity editor (rendering, physics, input and UI layout are not simulated).
