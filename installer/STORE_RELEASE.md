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

## Microsoft Store submission (Partner Center > Mazraa, Store ID 9P0J538PSTV9)
- Packages: upload `release/Mazraa_<ver>_x64.msix`. Identity comes from `installer/msix/identity.json` (Sladewilsontv.Mazraa).
- The package only declares `runFullTrust` (a Win32 Unity game). In **Submission options > Restricted capabilities** paste:
  "Mazraa is a Unity desktop game (Win32 executable) packaged as MSIX. runFullTrust is required to run the game's
  Mazraa.exe; the game does not access other apps, devices or user files outside its own AppData save folder."
- Notes for certification: "Single-player farming game. No account or sign-in. Press Enter / click New game on the
  main menu; the controls tutorial starts automatically. Requires DirectX 11/12 GPU."
- Privacy policy URL: host `installer/PRIVACY.txt` (e.g. a public page or GitHub Pages) and paste the link.
- Age ratings: answer the IARC questionnaire honestly (no violence, no gambling, no user chat, no purchases) - expect
  "Everyone / 3+".
- Store listing: description, at least 1 screenshot (1920x1080), Store logo 1:1 (Branding/mazraa_icon_1024.png),
  category Games > Simulation. Keep the listing in English only (the package declares en-us).
- Pricing: free or paid; no in-app purchases in this build.
- System requirements: 64-bit Windows 10 1809+, 8 GB RAM, DirectX 11 GPU, about 600 MB disk.
