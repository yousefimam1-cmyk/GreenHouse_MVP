# Releasing Mazraa on Windows stores

Unity menu **Mazraa > Release > Build Windows + all store packages (zip + installer + MSIX)** creates, in `release/`:

| File | Use it for |
|---|---|
| `Mazraa_<ver>_Windows/` (folder) | **Steam** (upload as a depot with SteamPipe / steamcmd), **Epic Games Store** (BuildPatchTool takes a folder) |
| `Mazraa_<ver>_Windows.zip` | **itch.io** (upload or `butler push`), **Game Jolt**, direct download, press/review copies |
| `Mazraa_<ver>_x64.msix` | **Microsoft Store** (Partner Center upload; the Store signs it). Set `installer/msix/identity.json` from Partner Center > Product identity first |
| `Mazraa_Setup_<ver>.exe` | Your own website, USB/offline copies, partners and sponsors (needs Inno Setup 6 installed, free) |

The debug folder `*_BurstDebugInformation_DoNotShip` is left out automatically.

## Before you submit (checklist)
- Version: Edit > Project Settings > Player > Version (now 0.9.0). Bump it for every upload.
- Company "Almadarek Games", product "Mazraa" are set in Player settings (saves go to AppData/LocalLow/Almadarek Games/Mazraa).
- App icon: set automatically from `Assets/Branding/MazraaIcon.png` on every build.
- Code signing: sign `Mazraa.exe` and the installer with a code-signing certificate, otherwise Windows SmartScreen warns users.
- EULA.txt is a draft - have it reviewed. CREDITS.txt lists the free asset licences.
- Microsoft Store: reserve the name in Partner Center, copy Package/Identity/Name, Publisher and PublisherDisplayName into `installer/msix/identity.json`, rebuild the MSIX and upload it. The MSIX writer is validated against Microsoft's MSIX SDK (makemsix unpack).
- Steam: Steamworks partner account + app fee; store page needs capsule art, screenshots and a trailer.
- Test the zip on a clean Windows PC (no Unity installed) before uploading.
