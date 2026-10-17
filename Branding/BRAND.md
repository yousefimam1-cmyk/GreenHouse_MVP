# Mazraa brand (مزرعة)

**Name:** MAZRAA / مزرعة  -  **Tagline:** Farming Simulator / محاكي الزراعة  -  **Publisher:** Almadarek Games

## Logo
- `mazraa_logo_en.png` - English lockup (badge left, wordmark right)
- `mazraa_logo_ar.png` - Arabic lockup (right-to-left: badge right, wordmark left)
- `mazraa_icon_1024.png` / `mazraa_icon_512.png` / `mazraa.ico` - app badge (palm with dates, sun, red tractor, field rows)
- Regenerate everything (all sizes, MSIX tiles): `python3 Branding/make_logo.py` (needs Pillow with raqm)
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

## Type
- Tajawal Bold (SIL OFL, `Assets/Resources/Fonts/Tajawal-Bold.ttf`) for English and Arabic, in game and on marketing.
- Wordmark: thick brown outline, gold-to-orange gradient, soft drop shadow.

## Where it is used
Main menu (EN/AR logo by language), Windows .exe icon, installer icon, Microsoft Store tiles (`installer/msix/Assets`), in-game UI palette (ControlsTutorial / MainMenuScene colours).
