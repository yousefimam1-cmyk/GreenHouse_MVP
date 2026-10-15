# Mazraa: crop data sources and scale factors

The open-field crops, orchard trees and prices in `Assets/Resources/Data/economy.json` and
`Assets/Resources/Data/Economy/30_crops.json` are based on real Saudi data for Al-Qassim, scaled for play.
Researched in October 2026. Values marked *estimate* are standard agronomy figures that were not found in an
official source during this pass. Replace them with MEWA or GASTAT figures when the team has them.

## Sources

| # | Source | Used for |
|---|--------|----------|
| 1 | GASTAT, *Average Prices of Goods and Services, August 2024* (stats.gov.sa) | Retail SAR/kg: local tomatoes 6.73, potatoes 4.71, onions 4.69, watermelon 3.18, rotab dates 14.59, olives 23.98 (table olives), pomegranate 14.69, local grapes 11.28, lemons 7.27, oranges 5.81, local flour 3.24-4.63; clover (alfalfa) 21.43 SAR per bale |
| 2 | GASTAT news: *Date production reaches 1,923 thousand tons in 2024* (stats.gov.sa/en/w/news/127) | 37.6 M palms, 32 M fruiting, so about 60 kg dates per fruiting palm per year |
| 3 | Saudipedia: *When does the dates production season start* | Date harvest runs June to November depending on region (Qassim: Aug-Oct) |
| 4 | Global Trade Alert: *SAGO raises the purchase price of wheat (March 2022)* | Saudi Grains Organization pays local farmers SAR 1,700/t for wheat (1.7 SAR/kg) |
| 5 | Sabq, 9 Sep 2024: Unaizah dates season | Khalas 39-93 SAR and Saqi 57-132 SAR per 3 kg box (13-31 SAR/kg Khalas) |
| 6 | Asharq Al-Awsat: Buraydah dates festival | Sukkari 70-220 SAR per package by grade; Qassim has about 8 M palms (6 M productive, more than 205,000 t) |
| 7 | Cambridge *Experimental Agriculture*: Hasawi alfalfa under three irrigation and cutting regimes in Saudi Arabia | Alfalfa is cut about 14 times a year (about every 26 days); 27-32 t dry matter/ha/yr |
| 8 | MEWA rain-fed wheat report (CRL 2022) and FEWS NET KSA country book | Seasons: winter cereals sown Nov-Dec and harvested Apr-May; summer is too hot for most open-field crops |

*Estimates* (general agronomy, not from an official source this pass): days to harvest for potato (about 110), onion
(about 150), watermelon (about 90), open-field tomato (about 100), sorghum (about 100), pearl millet (about 75);
alfalfa establishment (about 60 days); years to first crop for trees (date palm offshoot 4-5, olive 3-5,
pomegranate and grape 2-3, lemon 3-4); per-tree yields (olive 20-30 kg, pomegranate 20-40 kg, grape 10-15 kg per
vine, lemon 40-50 kg); farm-gate prices for sorghum, millet and Ajwa dates (Ajwa retail is about 40-80 SAR/kg).

## Seasons (in game: spring Mar-May, summer Jun-Aug, autumn Sep-Nov, winter Dec-Feb)

The cool season (Oct-Apr) is the main open-field season. In summer only heat-tolerant crops grow at full speed.

| Crop | Real season (Qassim) | Game seasons |
|------|----------------------|--------------|
| Wheat | sown Nov-Dec, harvested Apr-May | autumn, winter |
| Potato | autumn crop (Sep) and winter/spring crop (Jan) | autumn, winter |
| Onion | transplanted in winter, harvested in spring | winter, spring |
| Watermelon (حبحب) | spring and autumn plantings | spring, autumn |
| Field tomato | autumn and spring plantings | autumn, spring |
| Alfalfa (برسيم) | perennial, cut all year, slows in peak summer | autumn, winter, spring |
| Sorghum | planted Apr-Jul, heat tolerant | spring, summer |
| Millet (دخن) | summer crop | summer |
| Date palms | pollinated Feb-Mar, harvested Aug-Oct | pollinate in spring, ripe from day 3 of summer |
| Grapes | Jun-Jul | summer |
| Olives, pomegranate | Sep-Nov | autumn |
| Lemons | Nov-Jan | winter |

## Scale factors

The calendar is short: 1 in-game day = 360 s, a season = 5 days (30 min), and a year = 20 days (2 h).

* **Field crop time:** `growSeconds = 1.6 s x real days to harvest` (wheat 150 d = 240 s, potato 110 d = 180 s,
  onion 150 d = 240 s, watermelon 90 d = 150 s, tomato 100 d = 160 s, sorghum 100 d = 160 s, millet 75 d = 120 s,
  alfalfa establishment 60 d = 100 s). An alfalfa cut every about 26 days regrows from 60% to 100% growth, which is
  about 40 s. Several crops fit into one game season, so the season gates *which* crops do well, not how many.
* **Tree time:** 1 game season of tree age is about 1.5 real years. A date palm offshoot takes 4-5 years, so 3 seasons
  in the game; olives and lemons 3, pomegranates and grapes 2. Trees fruit once per game year in their real season.
* **Money:** produce prices are the GASTAT Aug-2024 average retail price x about 1.4, rounded: tomato 9, potato 7,
  onion 6, watermelon 4, grapes 10, pomegranate 12, lemons 8. Wheat uses the retail flour price (4.6 x 1.4 = 7)
  instead of the SAGO farm-gate price (1.7), because the game has no milling step. Sorghum (5) and millet (6) are
  estimates. Alfalfa hay is 2 SAR/kg. Real clover is about 1 SAR/kg (21.43 SAR for a bale of about 20 kg, x 1.4,
  rounded up) because the harvest also gives animal feed.
  Dates per kg: Khalas 20 (Sabq 13-31 SAR/kg), Sukkari 26 (premium Qassim variety), Ajwa 45 (estimate).
* **Yields:** a 6 x 6 m plot stands for a larger field so loads match the truck (150-500 kg). Broad-acre crops use
  about 0.01 ha per plot: wheat 6.5 t/ha gives 70 kg, sorghum grain 60 kg, millet 45 kg, alfalfa 40 kg per cut
  (plus 0.1 feed unit per kg). Vegetables use about 0.003-0.004 ha: potato 90 kg, onion 100 kg, watermelon 120 kg,
  tomato 80 kg. Each plot cycle then earns about 300-900 SAR against 60-200 SAR of seed.
  Trees give half the real per-tree yield (date palm 60 kg becomes 30 kg; Ajwa 20 kg), because one tree slot
  earns 400-900 SAR a year and the slots are permanent.
* **Soil and pests (game rules, not measured data):** each harvest uses 12% of soil fertility, or 22% when the same
  crop family is grown twice in a row. Alfalfa (a legume) adds 4% per cut and 10% at the end. Yield is
  x (0.55 + 0.45 x fertility). Rotating crop families gives +15%. Fertilizer adds 35% and manure adds 25%.
  Pest chances per day and their seasons follow the usual pattern in central Saudi Arabia: aphids in the cool
  months; whitefly in the hot months and after heatwaves; blight after rain or cloud in winter; red palm weevil all
  year; desert locust swarms are rare and follow winter or spring rain (FAO Desert Locust Watch).
