# Mazraa browser preview (three.js)

Playable simplified version of the Unity game, published as a claude.ai artifact.

- `src.html` – page source. `__DATA__` is replaced by `build.py` with the JSON in `Assets/Resources/Data`.
- Build: `python3 build.py ../Assets/Resources/Data` -> `index.html`.
- `models/` – Kenney + Quaternius CC0 models converted to embedded glTF `.json` (artifacts don't serve `.glb`).
- Publish `index.html` with every file under `models/` as supporting files (same relative paths).
- `tests/` – Playwright scripts (serve this folder on :8765; they route the CDN to a local `three@0.128.0`).
  `logic.js` runs every mission type; `play.js` is a scripted play-through with screenshots.
- Debug handle: `window.MZ` (state, interact, enter, sellCargo, tractorWork, simulate...).
