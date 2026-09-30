# PoteHunter

Latest appearance update: [sleek desktop UI](SLEEK-UI.md), with slate surfaces, teal accents, rounded settings cards, and clearer typography.

Windows x64 hunter with a compact desktop UI, combined group healer mode, HP/MP recovery, attack/defense potions, ranged pack farming, and automated ZIP packaging. See [the compact UI guide](COMPACT-UI.md), [group healer notes](HEALBOT-UPDATE.md), and [potion notes](POTION-UPDATE.md).

## Download and run

Download the `PoteHunter-<version>-win-x64.zip` asset from a release, extract the whole ZIP, and run `PoteHunter/Start-Fixed-Detection.cmd`. Keep the application executable and runtime files, and runtime files together. The ZIP includes .NET; users do not need the SDK.

The game client is installed separately. The current reader expects `C:\Program Files (x86)\AAT-Games\Client.exe`. Start the client and log in before starting the hunter. F8 calibrates and starts; F9 stops. Configuration changes are saved to the extracted application's `settings.json`.

Routine game patches are accepted automatically when their required signatures and live layout still validate. A new executable fingerprint alone does not block input. The bot verifies the connected process/window and input backend before reporting a successful connection; genuinely incompatible layouts still stop with a specific error. After updating the game, restart it and reconnect the hunter.

The repository is intended to remain private. GitHub release and Actions downloads require access to the private repository. You can separately share a downloaded application ZIP with users without sharing the source repository.

## Ranged packs

Open **Setup** in the sidebar and enable **Ranged (bow/crossbow)**, then open **Ranged packs** and enable **Tag a pack, then let it come to me**. This is a solo mode; select Solo in Setup.

The cycle follows the requested [Fast Farming Gunner example](https://www.youtube.com/watch?v=eJbofmXWSdE):

1. Move through bounded waypoints within the hunt area while aiming and holding the slotted **Firing** skill on distinct eligible monsters. Movement uses W/A/S/D independently of camera aiming, keeps a margin inside the active target's shooting range, and continues while adding members to the pack. A brief target range exit releases the pending shot and resumes acquisition without ending the pull. Firing is required; the mode stops before attacking if it is unavailable and waits when it is cooling down.
2. Start clearing when at least five approved living monsters are within two units in 3D. Hold position while a sufficient tagged group approaches; there is no gather timeout. If too few tagged monsters remain alive and loaded, resume pulling replacements. The healing threshold pauses adding new monsters.
3. Close to the 1.5-unit melee reach, turn the character toward each target, and swing with the basic attack and other offensive skills. Keep clearing while any approved monster remains in the nearby radius; when it is empty, resume pulling. Firing is excluded from clearing, including its short approaches.

Defaults are five nearby monsters and a two-unit nearby radius. The maximum simultaneous pull size is adjustable and cannot be smaller than the nearby threshold. The adding window defaults to 15 seconds; it never imposes an arrival deadline. During pulling the gunner holds the right mouse button while aiming and moving between targets. Firing activation or a stable target-health decrease counts as a pull attempt, since blocked attacks can still attract monsters. An attempt does not prove damage or arrival: the live five-within-two proximity rule controls clearing. Distant survivors remain recorded while the next nearby group is acquired.

Ranged aiming uses verified optical camera view and inverse-view matrices. F8 measures character turning and optical yaw during the same horizontal movements, then measures vertical response. Each calibration step waits for camera-observed movement, retries an entirely missed pulse, and discards quiet partial samples. Pitch probes can reverse or rebase near a limit; two consistent complete measurements are still required. The status identifies the axis and retry in progress. This handles the client's per-frame cursor recentering without treating Windows input acceptance as proof of camera movement.

The controller tracks a point one unit above the monster's base position, matching the supplied client source's cursor-target convention. Both axes track while movement steers independently. Aim acceptance also checks radial screen error against the distance-adjusted picker area, using the live viewport, so an ultrawide display does not turn a loose angular tolerance into a missed selection. A conservative 3D creature-envelope check favors unobstructed front targets without preventing blocked pulls. It checks loaded monsters, including those that are not approved for attack; it does not establish terrain or exact model-mesh line of sight.

Healing, retreat, Gamekeeper priority, target identity, health, and target-protection checks remain active. Nearby automatic pickup waits while a pack is engaged. Generic ranged and Archer class profiles retain their existing 18- and 24-unit defaults respectively; choose the profile appropriate to the character. The Archer option changes range and is separate from pack behavior.

The pack policy, input lifecycle, 3D projection/controller geometry, settings save/load, and offline UI rendering are tested. Camera signatures, matrices, aspect ratio, and both optical axes were verified against a manual live sweep. The complete calibrated hunting loop remains subject to the user's live test.

## MP recovery and radar overlay

Hunt setup includes **Use MP items**, a threshold (default 30%), and an item delay (default 5 seconds). Current/maximum MP are read from code-signature-validated live fields and displayed beside HP. Recovery uses only ready Food/Potion hotbar items with explicit mana restoration, including combined HP/MP items, and respects both item cooldowns and the configured delay. Unknown mana or dead/unreadable character health does not trigger an item. Firing is released before recovery input.

The Navigation page offers **Show radar overlay**, a 200–900 pixel size control (450 by default), independent view radius from 10–2000 game units, and **Fit loaded**. It also stores a primary hunt route plus two alternative routes. Select a save slot, stop hunting, then press **Home** (or **Start route**) at the start point. Walk to the destination and press **End** (or **Finish & save**) to save the route and destination facing in the selected slot. The shortcuts work with the game or PoteHunter in the foreground while connected; pressing Home again preserves the current recording. **Save current spot** remains available for stationary anchors. Each route records its character, floor/height, farming radius, revival delay, farm-on-arrival policy, destination, facing, and observed waypoints. A movement gap over 8 map units cancels recording so a missing sample cannot create a false return path. If another recognized player is inside the primary saved location when a hunt starts, the first free alternative with a matching character/floor is selected using that route's farming radius; the fallback can be disabled. Recovery joins a saved path only when the nearest waypoint is within 20 units, then restores the saved facing. The radar is a passive, click-through, nonactivating window positioned inside the game's top-right corner. It shows loaded monsters, engaged-target rings, chest markers, avoidance areas, observed trail, and the three saved routes. Zoom does not extend client object-loading range. Read-only 30-second surveys observed living monsters out to 134.6 units and chests out to 105.9 units; these are observations, not a proven server ceiling. The overlay hides when disconnected, minimized, or another application is foreground; windowed/borderless game presentation is the intended mode.

Death recovery has a configurable 0–600 second delay and waits for live HP confirmation before returning to the active saved anchor. The route profile is refreshed when death saves the current path, so the selected farming tab keeps its recovery settings. Repair-hammer recognition and chat-command group switching are not enabled because this client build does not expose a verified UI or chat API for those actions; see `DOMITUS-GUIDE-IMPLEMENTATION.md`.

## Build and verify

Development requires Windows x64, PowerShell, and the .NET 10 SDK. Internet access to nuget.org is required when SDK/runtime packs are not cached.

```powershell
.\Verify.ps1
.\Build.ps1 -OutputDirectory .\dist-new
```

`Verify.ps1` builds the application and supporting projects, runs the offline regression suite, and checks ranged settings persistence and UI layout without connecting to a game. Reports and the preview image are written under `PoteHunter/bin/Release/net10.0-windows`. `Build.ps1` publishes the application with the runtime, startup commands, and clean settings from `packaging/settings.json`. It refuses to overwrite a nonempty output folder. Both accept `-Dotnet` to select a particular dotnet executable.

Create a distributable locally:

```powershell
.\Package.ps1 -Version v0.1.0-beta.1
```

## GitHub packaging

The **Windows package** Actions workflow runs on pushes to any branch, version tags beginning with `v`, and manual **Run workflow** requests. It builds and tests the source, publishes the self-contained app, tests the packaged application in isolation, and uploads a Windows ZIP and SHA-256 checksum as workflow artifacts.

Pushing a version tag also creates a GitHub release with the ZIP and checksum attached. Tags containing a hyphen, such as `v0.1.0-beta.1`, are marked as prereleases. Regular branch and manual builds also create CI prereleases, identified by the run number and commit hash. The workflow uses GitHub's repository token; no personal access token is embedded in the source.

```powershell
git tag v0.1.0-beta.1
git push origin v0.1.0-beta.1
```

## Source layout

The desktop UI uses sidebar navigation, persistent Start/Stop controls, a connection badge, dark input fields, and focused settings pages. Existing game controls and settings remain connected to the same runtime logic. The UI checks render every page and the minimum-size ranged page without starting a game connection.

- `PoteHunter/Entry.cs`: desktop startup.
- `PoteHunter/CommandLine.cs`: diagnostic command dispatch.
- `PoteHunter/HunterForm.cs`: UI and runtime coordination.
- `PoteHunter/HunterForm.ModernUi.cs`: desktop shell, navigation, and styling.
- `PoteHunter/HunterForm.RangedPull.cs`: ranged pack settings and controller integration.
- `PoteHunter/RangedPull.cs`: bounded tagging/gathering/clearing policy.
- `PoteHunter/SelfTests.cs` and `*Checks.cs`: offline regression checks.
- `PoteHunter/Options.cs`: settings and persistence.
- `PoteHunter/TraceLog.cs`: diagnostic trace output.
- `PoteHunter`: packaged startup entry point.
- `PoteMemoryProbe`: shared reader and standalone diagnostics.

Keep the three project folders together: PoteHunter links `Program.cs` and `WindowsClientRead.cs` from PoteMemoryProbe. Build signatures and default settings are embedded resources. The game client, personal settings, recordings, logs, generated builds, and source backups are excluded from Git.
