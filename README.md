# PlayPoteBot

**Advanced > Navigation** has a separate **Show route overlay** toggle for
an automatically fitted map of Primary and both Alternative paths, saved
anchors/facing, your position, and the 10-unit startup corridor. It is passive
and click-through, placed at the bottom right of the game; radar remains separate.
This toggle changes visibility only, not whether the bot follows saved routes.

Beside **Show loot tracker**, choose **Dungeon HUD**, **Compact Ribbon**, or
**Parchment Ledger**. The saved design changes presentation only; every design
uses the same session totals and rates for Silvin, Mithril, Iternium, Fehu, Gold
and Gems. Drag its header to move it. The ribbon summarises aggregate kills/drops;
the detailed HUD and ledger also show attribution and recent drops.

**Gold (net)** is the actual wallet balance minus the first verified balance of
the application session. Repairs, purchases and trades reduce it; other income
also increases it. It is net wallet change, not enemy-specific gross loot. The
Overview Gold tooltip shows the wallet and baseline. Unavailable or stale reads
show an em dash. Other resources and per-target gold-pile estimates remain
detected-drop estimates, separate from wallet earnings.

The wallet baseline survives death, revival, repair, zone travel and reconnects
to the same character. **Reset loot** starts a fresh baseline; **Reset timer**
preserves the total and starts a new rate window. Changing characters requires
Reset loot. Rates use active farming time. Death and reset events append to
**loot-wallet-session-log.csv**, including the wallet balance, baseline, read
time and availability. Existing loot-session-log.csv files are preserved.

Start / F8 automatically follows a saved route to its anchor when you stand
within **10 map units of the recorded path**. Select the desired slot in
Advanced > Navigation; it is preferred when compatible and nearby, otherwise
the nearest compatible route is chosen. On arrival the character restores
facing and hunts. This applies to solo hunting. Outside the corridor, Start
uses the activation location as usual. Record with Home at the start and End
at the farming endpoint; saving only a spot cannot supply a return path.

The app's **Index** lists all hotkeys: F6 calibrates, F8 starts/stops hunting,
F9 stops, Home starts route recording, and End finishes/saves the selected
route. Escape, manual Enter/chat, and game focus loss stop automated input.
Route shortcuts require connection and stopped hunting, with the game or
PoteHunter in front. F6/F8 require the game in front.

Current interface: [Orbital Ops](ORBITAL-OPS.md), with graphite surfaces, lime accents, a left control rail and expandable settings.

Windows x64 hunter with combined group healer mode, HP/MP recovery, attack/defense potions, ranged pack farming, an installer and a public auto-updater. See [the interface guide](ORBITAL-OPS.md), [group healer notes](HEALBOT-UPDATE.md), and [potion notes](POTION-UPDATE.md).

## Download and run

Download the installer from [official public releases](https://github.com/theblusmurf/PoteHunter-Releases/releases) for installation and upgrades. For a portable copy, download `PoteHunter-<version>-win-x64.zip`, extract the whole ZIP, and run `PoteHunter/PoteHunter.exe`. Both include .NET; users do not need the SDK. Keep the application executable and runtime files together. Ordinary startup enables verified native read/input; the CMD and PowerShell shortcuts remain available.

The game client is installed separately. The current reader expects `C:\Program Files (x86)\AAT-Games\Client.exe`. Start the client and log in before starting the hunter. F6 calibrates, F8 starts/stops hunting, and F9 stops. Configuration changes are saved to the extracted application's `settings.json`.

Routine game patches are accepted automatically when their required signatures and live layout still validate. A new executable fingerprint alone does not block input. The bot verifies the connected process/window and input backend before reporting a successful connection; genuinely incompatible layouts still stop with a specific error. After updating the game, restart it and reconnect the hunter.

Development source and Actions remain in the private repository. Installers, portable ZIPs and updater downloads are published separately to [PoteHunter-Releases](https://github.com/theblusmurf/PoteHunter-Releases/releases), without requiring a GitHub account or token.

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

For combat modes, **Setup → Death recovery → Auto revive + return along saved route** controls the whole sequence. On: revive, optionally repair, follow the saved route to the anchor, restore facing, and optionally resume farming. Off: stop hunting on death. **Recognize Revive button** is enabled by default: it requires known dead HP, waits at least two seconds after the observed death, recognizes the centered button, clicks confirmation once, and checks HP every 200 milliseconds for up to 15 seconds to confirm revival. If the popup is not visible, it sends up to three left-clicks at the game-window centre, about one second apart, checking the screen and HP between clicks. It stops opening as soon as Revive appears or the character is alive; unreadable HP blocks further clicks. Custom profiles supply dialog/button recognition; opening always uses the centre, including for older profiles. Missing recognition stops recovery after the third click has had time to open the dialog. Turn recognition off to use the configured death-screen key (usually `R`, with `Enter` as confirmation fallback). The saved 0–600 second delay applies before either method; visual revival uses the longer of that delay and its two-second minimum, without adding the two waits together. Enable **Resume farming on arrival** to continue after return. Record Primary and any desired alternatives from the same revival point to each farming destination with **Home** and **End** in Navigation. Start hunting within 2.5 map units of that route's destination. A saved spot without recorded waypoints is insufficient for automatic return. Each route retains its saved delay and arrival policy; resave a slot to update that profile. Navigation's **Use alternatives when the saved spot is occupied** remains a separate choice.

**Custom revival setup** uses the same image editor as repair. With hunting stopped, connect to the game while the character is dead, leave **Recognize Revive button** selected, and choose Custom revival setup. Setup captures the Revive dialog only; opening clicks always use the game-window centre. For the confirmation capture, manually open that dialog without clicking Revive; select its distinctive static text separately from the button, then the button center. Each capture gives five seconds to switch to the game and move the pointer away. Setup reads the screen without sending game clicks. Changing character/map, reviving during capture, cancelling, or invalid selections leaves the old profile intact.

Use **Test revival · 5s** while still dead, switch to the game, and move off the button. It performs a real revival using the saved setup (or the included detector when no setup exists), and reports success only after living HP is confirmed. Hunting stays stopped; repair and route travel run during normal automatic recovery. The custom profile takes precedence over the included detector, is tied to the client build and window size, and requires the same dialog layout. Missing text/buttons or a moved dialog stop input. A missing popup permits at most three centre opening clicks, one second apart; a recognised dialog that disappears before confirmation stops recovery. **Use automatic** keeps a timestamped backup of the custom setup and restores the included detector. Preserve `revival-profile.json` when upgrading. Only selected recognition patches and click positions are stored locally; full captured screenshots are not saved.

Death recovery waits for live positive HP, follows recorded waypoints to the actual activation anchor, and restores its facing before resuming combat. Manual revival also completes this return when automatic recovery is enabled. Death and normal hunting never overwrite a recorded route. Occupancy is checked after revival and while approaching the destination, using loaded recognized players. Fallback cycles through Primary and both alternatives: reverse the current recorded path to its origin, then follow the free destination's path. All participating routes must have a shared origin within 3 map units and belong to the same map, character, and farming floor. If all compatible spots are occupied, recovery returns to that origin, waits ten active minutes, and retries; a second death preserves the pending cycle. The character must be within 20 units of a compatible recorded path to join it. Map changes, unjoinable routes, and blocked travel stop with a reason. F9 and switching away from the game stop input. Engaged enemies leaving the allowed farming/completion area trigger anchor return and waiting. Copy `settings.json`, `navigation-routes.json`, and any custom `repair-profile.json` and `revival-profile.json` into the new extracted application folder when upgrading; keep backups.

**Auto repair after revival** is a separate checkbox in the same card and defaults off. It uses Domitus's inventory-hammer workflow once after confirmed revival, before return travel. It requires the game's repair capability and repair cost; it does not detect durability or perform periodic repairs. Automatic recognition uses the included button and matching inventory/dialog text templates, handles moved windows and supported UI scales, and rejects ambiguous controls. Enable repair to reveal **Custom repair setup** and **Test repair · 5s**. With hunting stopped, use Test and switch to the game within five seconds. The test performs one real repair confirmation; check equipment durability afterward.

**Custom repair setup** is an optional fallback for other layouts. It captures two game images: inventory open, then the repair confirmation open without clicking Yes. Each capture gives you five seconds to switch to the game and move the pointer away. Select distinctive static inventory/repair-question text and the hammer/Yes button center; exclude changing gold amounts and item slots. Keep the inventory marker visible behind the confirmation. Cancel that confirmation and close inventory before testing. Only selected patches and button positions are saved locally, bound to the game build and window size; configure again if a custom layout changes. Use a visible windowed/borderless game. Missing recognition, a stuck confirmation, cancellation, death, or focus loss stops the sequence before return. Confirmation is attempted once, with a fresh check before the click. Completed UI steps do not independently verify durability. The current repair checkbox applies to older routes as well.

Automatic repair/revival recognition and saved-route fallback are adapted from the supplied Domitus source. The existing combat skill gates and swing ranges remain in place. Chat-command group switching and durability verification are not included; see `DOMITUS-GUIDE-IMPLEMENTATION.md`. Image fixtures and recovery simulations passed offline; the complete sequence still needs verification in the user's game.

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
