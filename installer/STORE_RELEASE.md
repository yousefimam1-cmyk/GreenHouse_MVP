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
- Capabilities: `internetClient` + `internetClientServer` (online co-op through Unity Relay / Lobby),
  `privateNetworkClientServer` (LAN co-op on port 7777) and the restricted `runFullTrust` (a Win32 Unity game). In
  **Submission options > Restricted capabilities** paste:
  "Mazraa is a Unity desktop game (Win32 executable) packaged as MSIX. runFullTrust is required to run the game's
  Mazraa.exe; the game does not access other apps, devices or user files outside its own AppData save folder."
- Notes for certification: "Farming game, single player or co-op for up to 4 players. No account or sign-in (online
  co-op uses an anonymous Unity Gaming Services ID). Press any key, then Career to start a farm; the controls tutorial
  starts automatically. To test online co-op: main menu > Multiplayer > Host an online game > Start hosting shows a
  6-character join code; on a second PC choose Multiplayer > Join with a code. Requires a DirectX 11/12 GPU."
- Privacy policy URL: host `installer/PRIVACY.txt` (it covers Unity Gaming Services: anonymous player ID, IP address
  processed by Relay / Lobby, chat sent to the players in the session, reports e-mailed only when the player chooses)
  and paste the link. The same text goes on the website privacy page.
- Age ratings (IARC questionnaire): answer **Yes - users can interact / communicate online** (co-op with up to 3 other
  players, text chat). Mention the safety features: chat bad-word filter (English and Arabic), block and report,
  chat Everyone / Friends only / Off, farms can be friends-only or code-only (only public farms are listed), player
  names are picked from a list (no free text names), the host can remove or ban a player for the session. No
  violence, no gambling, no purchases. Expect the rating to carry the "Users Interact" interactive element.
- Data collection in the listing / Partner Center "Privacy" questions: no personal data; online co-op uses Unity
  Gaming Services (anonymous ID, IP address to connect players). No data is sold or used for ads.
- Store listing: description, at least 1 screenshot (1920x1080), Store logo 1:1 (Branding/mazraa_icon_1024.png),
  category Games > Simulation. Keep the listing in English only (the package declares en-us).
- Pricing: free or paid; no in-app purchases in this build.
- Online co-op needs the project linked to a Unity Cloud project (Edit > Project Settings > Services) with Relay and
  Lobby turned on in the Unity dashboard; Unity's free tier covers small player counts. Without the link the
  Multiplayer screen shows "Online play is not available" and LAN still works.
- System requirements: 64-bit Windows 10 1809+, 8 GB RAM, DirectX 11 GPU, about 600 MB disk.

## Online co-op: one-time setup in the Unity editor
Online play (Multiplayer > Host an online game / Join with a code / Browse public games) uses Unity Gaming Services
through the package `com.unity.services.multiplayer` 2.3.3 (Relay + Lobby + anonymous Authentication). Windows builds
compile it with the scripting defines `MAZRAA_NETCODE;MAZRAA_ONLINE` (Project Settings > Player > Standalone).
1. Open the project once so the Package Manager downloads `com.unity.services.multiplayer` 2.3.3 and
   `com.unity.transport` 2.7.3 (both in Packages/manifest.json).
2. **Edit > Project Settings > Services**: sign in, choose the organisation and **Link** the project to a Unity Cloud
   project (create one called "Mazraa" if needed). This fills the Cloud Project ID the game needs.
3. On cloud.unity.com > the Mazraa project: open **Multiplayer > Relay** and **Multiplayer > Lobby** and turn both on
   (Authentication > Anonymous sign-in is on by default). Production environment is used.
4. Test: Play in the editor (or a build) > Multiplayer > Host an online game > Start hosting shows a 6-character code;
   a second build joins with Join with a code.
If the packages can't be downloaded, menu **Mazraa > Co-op > Disable Online (keep LAN)** turns `MAZRAA_ONLINE` off
(the game still builds; LAN co-op keeps working). **Disable Netcode** goes back to single player only.
