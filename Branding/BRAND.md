# Mazraa brand (مزرعة)

**Name:** MAZRAA / مزرعة  -  **Tagline:** Farming Simulator / محاكي الزراعة  -  **Publisher:** Almadarek Games

## Logo
- `mazraa_logo_en.png` - English lockup (badge left, wordmark right)
- `mazraa_logo_ar.png` - Arabic lockup (right-to-left: badge right, wordmark left)
- `mazraa_icon_1024.png` / `mazraa_icon_512.png` / `mazraa.ico` - app badge (palm with dates, sun, red tractor, field rows)
- `mazraa_logo_en_stacked.png` / `mazraa_logo_ar_stacked.png` - badge above the wordmark (key art, splash, square formats)
- Regenerate everything: `python3 Branding/make_brand_kit.py` (logos, icon, in-game logos, loading-screen layers, MSIX tiles,
  `installer/store_art`) and `python3 Branding/make_ui_kit.py` (UI sprites, icons, glyphs). Needs Pillow with raqm + numpy.
  `make_logo.py` holds the badge / wordmark drawing and is imported by the kit.
- Keep clear space around the badge equal to 1/4 of its width. Don't recolour, stretch or add effects.

## Colours
| Name | Hex | Use |
|---|---|---|
| Sky turquoise | #40C4D6 | badge sky, splash background |
| Sun gold | #FFC828 | sun, highlights |
| Wordmark gradient | #FFEC78 to #FF8C28 | MAZRAA / مزرعة letters |
| Outline brown | #2B1B0E | all outlines, text stroke |
| Field green | #2E8C46 | tagline pill, crops |
| Palm green (UI) | #2E6B3A / #163A20 | menus, panels |
| Sand (UI) | #E9D8B4 | UI text on green |
| Tractor red | #E2322E | accent only |

## Key art (original illustration, drawn in code)
Golden-hour farm: palm groves, field rows converging on the horizon, a tractor on the near track, birds and dust in the
light. `keyart_najd_4k.jpg` (Saudi oasis: dunes, mud-brick town, greenhouses, canal) and `keyart_delta_4k.jpg` (Egypt:
Nile, felucca, pigeon tower, green fields). `installer/store_art/` has the Microsoft Store set: key art 1920x1080 and
3840x2160 (EN, AR, Delta DLC), hero 2160x1080, box 1080x1080, poster 720x1080, store logo 300x300, app icon 1080x1080,
clean background. The loading screen uses the same scene as 4 parallax layers (`Assets/Resources/Branding/Loading`).

## UI art kit (`Assets/Resources/UI`, `UiKit` in code)
- `Kit/` 9-slice sprites: `panel` (frame with eight-point star corners), `panel_plain`, `panel_light`, `card`, `tooltip`,
  `button` / `_hover` / `_selected` / `_primary` / `_danger` / `_disabled` / `_hc` (high contrast), `highlight`, `tab` /
  `tab_active`, `bar_track`, `bar_fill` (+ `_green` / `_blue` / `_red`), `divider`, `pattern` (girih tile), `dot`, `glow`.
  Borders are in `kit_manifest.json`; `UiKit.Sliced(image, "panel")` applies one.
- `Icons/` 120 flat white icons tinted at runtime (`UiKit.Icon("crop_wheat")`): economy, time / weather, crops, animals,
  vehicles and implements, map places (`poi_*`), settings categories and actions.
- `Glyphs/` controller and key glyphs (`xbox_a`, `ps_cross`, `xbox_lb`, `ps_options`, `key_e`, ...), `UiKit.Glyph(pad)`.
- Menus (MenuKit) use `panel` + `button` / `button_selected`; everything falls back to flat colours when a sprite is missing.

## Type
- Tajawal Bold (SIL OFL, `Assets/Resources/Fonts/Tajawal-Bold.ttf`) for English and Arabic, in game and on marketing.
- Wordmark: thick brown outline, gold-to-orange gradient, soft drop shadow.

## Where it is used
Main menu (EN/AR logo by language), Windows .exe icon, installer icon, Microsoft Store tiles (`installer/msix/Assets`), in-game UI palette (ControlsTutorial / MainMenuScene colours).
