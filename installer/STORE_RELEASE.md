# Releasing Mazraa on Windows stores

Unity menu **Mazraa > Release > Build Windows + package (zip + installer)** creates, in `release/`:

| File | Use it for |
|---|---|
| `Mazraa_<ver>_Windows/` (folder) | **Steam** (upload as a depot with SteamPipe / steamcmd), **Epic Games Store** (BuildPatchTool takes a folder) |
| `Mazraa_<ver>_Windows.zip` | **itch.io** (upload or `butler push`), **Game Jolt**, direct download, press/review copies |
| `Mazraa_Setup_<ver>.exe` | Your own website, USB/offline copies, partners and sponsors (needs Inno Setup 6 installed, free) |

The debug folder `*_BurstDebugInformation_DoNotShip` is left out automatically.

## Before you submit (checklist)
- Version: Edit > Project Settings > Player > Version (now 0.9.0). Bump it for every upload.
- Company "Almadarek Games", product "Mazraa" are set in Player settings (saves go to AppData/LocalLow/Almadarek Games/Mazraa).
- App icon: Player settings > Icon (add a 1024x1024 Mazraa logo PNG) - stores reject the default Unity icon.
- Code signing: sign `Mazraa.exe` and the installer with a code-signing certificate, otherwise Windows SmartScreen warns users.
- EULA.txt is a draft - have it reviewed. CREDITS.txt lists the free asset licences.
- Microsoft Store needs an **MSIX** package (use the MSIX Packaging Tool on the release folder) and a Partner Center account.
- Steam: Steamworks partner account + app fee; store page needs capsule art, screenshots and a trailer.
- Test the zip on a clean Windows PC (no Unity installed) before uploading.
