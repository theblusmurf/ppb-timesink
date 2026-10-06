PlayPoteBot - Orbital Ops

Graphite surfaces, lime accents, a left control rail, expandable settings and
HP/MP status bars. Routes opens Navigation with native 3D and the original 2D view.
Hover HP/MP for details. Route and loot options remain independently adjustable.

3D maps read verified terrain and available objects from your installed game.
Choose a map, drag to orbit, right-drag to pan, and scroll to zoom. Map layers
controls the original artwork, its transparency, terrain, objects, routes and anchors.
Top view is north-up. Fit routes and View anchor use the selected target's routes.
Unsupported client/data or hardware falls back to 2D.
Overlay options adds independent 3D radar and 3D route overlay switches, plus
a fixed north-up tilted/top camera. Radar radius controls player-centered zoom;
the route overlay fits paths and displays corridor/anchor areas. Both remain
click-through and always use the current game zone. Map layers/opacity apply
to the 3D overlays too. Display controls do not change movement or recovery.
No game map models or map artwork are included. Terrain is for viewing and does not alter
movement, collision or recovery. See NAVIGATION-3D.md for supported maps and limits.

PlayPoteBot keeps the existing PoteHunter.exe and package names for upgrade compatibility.
Extract the entire folder and run PoteHunter.exe or Start-Fixed-Detection.cmd.
Normal executable and packaged launcher starts enable verified native read/input.
Client connection and Windows export validation are still required before input.
Or download PoteHunter-Release1.x-Setup.exe for an installed copy with shortcuts
and an uninstaller. Setup preserves existing settings and saved route/profile files.
Settings > Updates connects to the public theblusmurf/PoteHunter-Releases feed.
No GitHub account or token is required; development source remains private.
Automatic patching checks at startup/every 30 minutes and applies newer releases
only when hunting, setup/tests, dialogs and route recording are stopped.
It preserves user data and reopens with hunting stopped. Manual download/install
remains available. Installer size and GitHub SHA256 digest are
verified before launch. Existing saved credentials are unused by this version.
Failed patches retain local UpdateCache logs and stop automatic retries; toggle
automatic patching off/on or use manual installation to retry. No game close or
Windows reboot is requested. Install Release1.72 once to enable auto patching.
For first migration, close the app and copy settings.json, navigation-routes.json,
repair-profile.json and revival-profile.json to the installed app folder if present.
Copy your old settings.json before starting if you want to keep your preferences.
Also preserve navigation-routes.json, repair-profile.json, and revival-profile.json
(if configured).
Choose Solo, Group combat, Healer or Group healer in Setup.
For Group healer, choose a tank and set follow distance, healing skills and range.
Support contains skill buffs and attack/defense potion switches.
Settings save automatically. F6 calibrates, F8 starts/stops hunting, and F9 stops.
See ORBITAL-OPS.md for the current layout; recovery instructions follow below.

Death recovery (Setup, combat modes):
- Confirmed 0 HP is death. Auto recovery also interrupts resting when HP hits 0.
- Revive + return to anchor: on revives and returns; off stops on death.
- Recognize Revive button is on by default. It clicks only a recognized button,
  then waits for living HP. Turn it off to use the configured revival key.
- Visual revival waits at least 2 seconds after death, then sends up to three centre opening
  left-clicks with a minimum 1 second gap. It stops opening when Revive appears
  or HP is alive/unreadable, and confirms Revive only once. A longer saved
  route delay takes precedence. Custom profiles also use the window centre.
  After the single confirmation, HP is checked every 200 ms for up to 15 seconds.
- A shifting cursor or changing dialog is rechecked before clicking; unsent
  clicks do not consume the three-click limit. Recovery stops if the screen
  does not settle within its timeout. Stop messages report the actual reason.
- Custom revival setup works like repair setup. While dead, connected, and not
  hunting, capture the Revive dialog, select its static text, then its button.
  Opening always uses the centre; setup captures only the dialog. Capture allows five
  seconds to switch to the game and move the pointer away. Setup sends no clicks.
- Test revival gives five seconds to switch to the game, then performs one real
  revival. It requires known dead HP and reports success only after living HP.
  Hunting stays stopped; repair and return travel run during normal recovery.
- Custom revival profiles require the same client build, window size, and dialog
  layout. Missing recognition stops recovery. Use automatic backs up the custom
  profile and restores the included detector. Full screenshots are not stored.
- Record a route from the revival point to the farming spot with Home and End.
  A saved spot alone is not a return route. Start near the recorded destination.
- Record Primary and both alternatives from the same starting point (within
  3 map units). Occupied-spot fallback follows recorded paths via that point.
  Saving only Primary enables its return path, not two alternative destinations.
  If all compatible spots are occupied, it waits there 10 minutes and retries.
  Only currently loaded players can be detected. Blocked routes stop recovery.
- Auto repair after revival: optional inventory-hammer repair before the return.
  Requires the game's repair capability and cost. Off by default.
- Automatic recognition uses the included Domitus inventory/repair templates
  and supports moved windows and common UI scales. Enable repair, choose
  Test repair with hunting stopped, switch to the game within 5 seconds,
  and check equipment durability afterward.
  This test performs one repair confirmation. F9 stops it.
- For a different layout, Custom repair setup captures inventory and the repair
  confirmation. Select static text and the hammer/Yes button centers. Cancel
  the open confirmation and close inventory before testing. A custom profile
  requires the same game build, window size, and layout used for setup.
- Use a visible windowed/borderless game. Keep it foreground during recovery.
- Repair stops on missing controls or a stuck dialog, without retrying Yes.
  UI completion does not independently verify durability. The shipped checks
  are offline; verify repair, revival, and return in your client.

Client crash recovery: Setup > Death recovery > Login setup. Choose the game launcher and capture its Play/Start button separately; Client.exe is selected only for verification and login captures, never launched directly. Capture login steps with password empty, save password locally, test from login screen, then enable Relaunch client + login + return. To test launcher startup too, close the game client manually before Test login. Requires a recorded route for solo targets. F9 cancels. Carry client-recovery-profile.json across portable upgrades; existing login captures/password are retained when adding launcher calibration. It contains private calibrated patches and a Windows-account-encrypted secret. Never publish this file.
