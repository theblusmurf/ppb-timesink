## Release1.139

Follow saved routes with distance-based, speed-adaptive steering so dense recordings receive a useful forward aim instead of being limited to four samples.

- Precompute route distances and examine a bounded local distance window for curvature and steering. Select the aim by distance along the recorded path from the current incoming-segment projection, with a separate point-count cap for work bounds. Reuse existing displacement-based speed feedback to shorten lookahead at lower speed and around curves; keep the existing maximum lookahead.
- Keep forward movement continuous through ordinary samples and gentle bends. Steering changes neither recorded checkpoints nor their progress/retry ownership. Required connectors, sharp corners, U-turns and final arrival retain their precision handling. Every shortcut stays inside the existing narrow recorded corridor and requires the original segment and direct collision checks.
- Preserve startup, death return and alternative-route behavior, exact final anchor/facing, input focus/identity/health guards and sustained turning detection. Existing routes, settings, profiles and session logs need no migration or re-recording.

Validation covers differently sampled versions of the same path, speed/curvature bounds, blocked segments and chords, offset route entries, corners, loops, overshoot, rotation/translation and unchanged final arrival and retry guarantees. Offline checks do not establish improved gameplay travel time; live smoothness and efficiency still require confirmation after upgrading.

## Release1.138

Allow one guarded recovery from a sustained turning fault while following a recorded route, instead of immediately cancelling route startup.

- Release forward movement and mouse buttons before a passive 120 ms pause, reset the steering response once, and read the current position and body heading with ordinary safety checks. Recalculate the same recorded waypoint on the next iteration; a retry does not confirm arrival or permit movement against stale geometry.
- Limit recovery to one attempt per recorded waypoint within its fixed 20-second allowance. Distance progress, moving aim directions and repeated corrections do not refill that attempt. Preserve route progress, collision, floor, identity, focus, health and cancellation protections. A repeated or late sustained turning fault still stops safely.
- Record bounded route retry/failure details and sampled precise-facing commands with the measured pre-command body heading, intended pixels and calibration. A heading change alone remains insufficient: wrong-way turns and repeated oscillation cannot renew the existing watchdog.

Validation covers a recoverable route turn, repeated faults, unchanged-waypoint progress, new waypoint ownership, expired deadlines and cancellation before or during the released pause. The observed route failure included a heading change away from the intended goal; it does not prove whether delayed client response or external movement caused that change. Live route recovery after upgrading still requires confirmation. Existing settings, routes, profiles and session logs are preserved.

## Release1.137

Allow delayed position and safety checks to confirm that a short forward correction has settled without prematurely stopping route startup.

- Keep forward movement released throughout one bounded settling observation. Preserve the normal 320 ms window and allow confirmation within a fixed one-second hard limit, counting the initial released-position read, safety checks and later position reads in that same budget.
- Require cumulative quiet motion for at least 80 ms before another correction; an unchanged position cannot permit a repeat before 200 ms. Check cancellation immediately after each read and refresh the ordinary input safety guards before accepting settled motion. Continuing drift, unavailable geometry, exceeded hard limits and safety failures remain blocked.
- Record the normal and hard observation limits with actual settling duration so delayed checks can be distinguished from unconfirmed motion. Route progress, waypoint ownership, collision protections and the shorter urgent self-heal deadline remain in effect.

Validation reproduces stable reads completing at 328–375 ms, delayed movement publication, cumulative drift, initial-read latency, exact and exceeded hard deadlines, and safety cancellation during a position read. The earlier 320 ms rejection could reach the route-start hunt failure handler; this update waits within the same released-input step rather than broadly retrying safety exceptions. Live route startup after updating still requires confirmation.

## Release1.136

Give ready HP-triggered self-heals a bounded opportunity before an exhausted melee approach or pending anchor facing, and improve small movement corrections.

- Permit one urgent approach per low-health episode to an already owned, verified living target, with at most 0.5 map units of correction and two seconds of total work. Recheck identity, HP/MP, cooldown, standing posture, Gamekeeper priority, saved-anchor leash, collision and focus before movement and each activation. Preserve pending anchor return; unknown health, target changes and failed attempts cannot renew the approach budget. Attack reach and assigned HP thresholds remain unchanged.
- Measure turn response toward the last actually sent goal, with a smaller progress requirement near the existing facing gate. Wrong-way motion, recycled progress toward the same goal, target motion without measured heading response, and position drift cannot renew the no-response deadline. Record bounded owner, heading, error and progress details when a turn fails.
- Align short forward corrections to the existing input-frame bands, learn their settled displacement separately from velocity, and check the whole conservative travel envelope after aiming. Observe delayed movement before another pulse; release and yield when movement remains unsettled at the fixed observation deadline, without recording a false collision obstacle.

Validation covers the captured marginal range and short-pulse geometry, injected runtime movement/cast guards and deadlines, quantized facing, wrong-way and oscillating responses, delayed settlement, and the full offline/native UI checks. The player-marker render fixture freezes its accepted synthetic snapshot before rendering so slow CI rendering cannot expire a second lookup; live freshness rules and explicit stale-marker checks remain unchanged. These changes address recorded behavior; live gameplay after upgrading still requires confirmation. Existing settings, routes, profiles, item imagery and session logs are preserved.

## Release1.135

Keep optional diagnostic file failures from cancelling a hunt, and retain enough local detail to identify a failed operation.

- Isolate live-status replacement, calibration trace appends and chest-sighting persistence from hunting. Retry Windows access, sharing and lock errors at most twice with 15 ms total delay. Preserve the last complete status file and in-memory chest sightings when a write fails; retry dirty chest sightings on later observations.
- Keep bounded local failure details with the operation, path, exception type, HResult, stack and status-update stage. Show a diagnostic persistence warning until that operation succeeds. Limit the private audit to two one-megabyte files, throttle repeated audit entries, and retain recent failures in memory if its own write fails.
- Leave required settings, client identity, health, focus and input failures under the existing stop guards. Record their original exception details before the status-update stop/reconnect path; a failed required save during connection remains disconnected.
- Describe an automatic-profile reconnect as a verified automatic client layout rather than claiming the client was updated.

Validation includes real Windows sharing locks, atomic snapshot preservation and later recovery, append locks, denied and read-only paths, bounded retries and audit history, dirty chest retries, non-I/O exception propagation, critical callback faults, truthful reconnect text, and the full offline/native UI checks. The captured access-denied stop did not identify its exact failing path; this release prevents optional persistence failures from stopping hunting and improves diagnosis without asserting that the original cause is proved. Running gameplay, the bounded review, routes, profiles and settings are left untouched. Live behavior after upgrading still requires confirmation.

## Release1.134

Resume owned combat promptly after a settled return to the anchor, then restore the saved facing when the pack clears.

- Hand off a settled anchor return to approved living engaged targets before restoring the saved facing. Keep the saved facing as a separate pending action with a two-minute maximum allowance across target changes, preserve Gamekeeper priority and correction opportunities, and release input before travel or any unsafe context.
- Continue fine melee facing while health recovery is deferred and the character is still fighting upright. Actual resting and recovery retain their input guards; the strict offensive skill-facing requirement stays unchanged.
- Stop the previous approach and reset its turn-response state when a new anchor loot sweep takes ownership, so old combat timing cannot cause an immediate turning fault for the new goal. Genuine sustained no-response detection remains active.

Validation covers safe handoffs, identity/range/health/priority changes, pending versus actual rest, renewed loot-goal watchdog deadlines, and the full offline and native UI checks. These changes address captured behavior; live gameplay after upgrading still requires confirmation. Existing settings, routes, profiles and session logs are preserved.

## Release1.133

Resolve short melee approaches and reduce unnecessary movement during loot pickup.

- Complete a stationary melee assist only after a fresh position reading puts the engaged enemy inside the existing attack range. Small corrections no longer disappear inside a larger navigation arrival tolerance.
- Keep assist destinations within 1.5 map units of the saved anchor. Allow bounded replanning, then return when movement stops making progress or the assisted target disappears or changes identity; retain priority, health, focus, target identity and collision checks.
- Check the existing three-unit loot pickup reach before walking toward a drop. Refresh the whole pickup neighborhood before E, preserve the saved loot radius and protection for existing drops, and bound attempts when pickup makes no progress.
- Apply a loot arrival envelope only to the actual final navigation destination, preserving intermediate waypoint checks.
- Use an owned Windows waitable timer with a monotonic deadline for short forward corrections. Prepare timing resources before input, release on cancellation or failure, and retain the conventional timer fallback on older Windows. Movement speed and pulse duration limits remain unchanged.

Validate short-assist range, leash, progress and deadline cases; pickup reach, neighborhood protection and disappearance accounting; pulse deadlines, cancellation, ownership and cleanup; and the full offline and native UI checks. A local no-input timing comparison showed less short-pulse delay; it is not a guarantee of live movement timing. Running gameplay and local routes, profiles and settings are left untouched. Live behavior after upgrading still requires confirmation.

## Release1.132

Keep near-anchor combat continuous while the saved return remains pending.

- Replace the one-second return-defense slices with one guarded engagement. Keep the same living, approved enemy until it dies, leaves reach, Gamekeeper priority changes, or a safety check requires yielding. Preserve return intent and completed repair.
- Use the normal offensive skill rotation during near-anchor defense, including the five-target/80-percent pack requirement and 1.5-second offensive spacing. Preserve priority self-heals, health conditions and mana-reserve exemptions.
- Recheck the live target, range, pack conditions and body facing immediately before every melee skill activation and fallback. Keep the basic swing held while aiming. Ranged targeting keeps its existing camera behavior.
- Preserve the no-damage swing watchdog across withheld and ordinary skill attempts. Reset its input timestamp only when the basic attack is actually released and rearmed; observed damage still renews progress.
- Retain a shared turn-speed-aware arrival opportunity across return retries, so new enemies cannot repeatedly interrupt the final anchor correction. Reset it for a changed destination, new revival or confirmed arrival.
- Bound each defensive engagement by 15 seconds without a new lowest target HP and 120 seconds overall. Renew character, target, floor, focus, protection, destination occupancy and Gamekeeper checks throughout; release input on every exit.

Validate the recorded .868-unit anchor/1.8-unit enemy geometry with a 40-second continuous engagement, target invalidation and cleanup, no-progress/regeneration/deadline handling, shared retry scheduling, delayed skill activation and fresh facing alongside the full offline and native UI checks. These are offline checks; live behavior after upgrading requires confirmation. Running gameplay, local profiles/settings and private evidence are left untouched.

## Release1.131

Smooth route entry and keep skill targeting stable through each cast.

- Brake precisely at the actual sharp corner, without requiring a second precision stop at its preceding gentle waypoint. Preserve early braking, checked route segments, sharp-corner capture and the final 0.5-unit anchor arrival.
- Brake at an unvisited initial connector so it cannot be overshot while normal checkpoint passage remains unavailable.
- Restrict new melee skill targets to the same attack reach used by basic swings. A distant engaged enemy cannot repeatedly replace the stationary swing target before a cast.
- Reserve a chosen skill target through the next guarded cast attempt, with full target/skill identity checks and a three-second expiry. Continue checking health, range, skill eligibility, pack conditions, priority, focus and cancellation. Self-heals can preempt the reservation.
- Preserve a verified living Gamekeeper during offensive skill selection when its priority is enabled. Keep highest-health and pack-density selection for ordinary enemies, continuous basic swings, the five-target/80-percent skill gate and 1.5-second offensive spacing.
- Include target health, distance, generation and selection range in retarget diagnostics so future target changes can be explained.

Validate the captured entry shape in multiple orientations, required corners/connectors/blocked paths, melee skill boundaries, Gamekeeper priority and reservation identity/expiry/cast completion alongside the full offline and native UI checks. Running gameplay is left untouched; live smoothness after upgrading requires confirmation. Local routes, profiles and diagnostic evidence remain private.

## Release1.130

Implement the selected Session Desk Overview.

- Put operating mode, target filter and Gamekeeper priority above net gold, active time and tracked kills.
- Place the five-row resource ledger beside a compact current-zone route snapshot, with loot details/reset actions and target-family shortcuts.
- Add six accessible collapsed configuration sections with live summaries. Adapt the columns to the window width and keep expanded controls reachable through scrolling.
- Share the existing guarded Farming settings and tracker readings. Preserve the main navigation, hotkeys, reset confirmations, gameplay behavior, settings format and verified local 3D maps. Unknown wallet and player readings remain unknown.

Validate disconnected native rendering, two-way Overview/Farming bindings, target route selection, numeric/slider values, protected-state mutation guards, accessible folding and minimum/constrained window bounds alongside the existing offline/UI/package checks. No game input, live settings change or running-app replacement is performed.

## Release1.129

Restore local 3D maps for the verified October 6 game client.

- Accept the newly verified client alongside the original map-compatible build. The old single-build check incorrectly marked its main 3D map, radar and route overlays as unavailable.
- Preserve exact build verification, supported zones, bounded local asset parsing, artwork alignment and the existing 2D fallback. No movement, collision, route-following or gameplay settings change.

Validate the relocated terrain routines, all 18 zone dispatch entries and alternate scene codec locally. Check all ten terrain scenes against independent references, load their objects, and render local maps and passive overlays for zones 8, 9 and 12 on hardware graphics. Build and run the full offline/native UI checks. No game input or live app restart is used; private game assets and diagnostics stay local.

## Release1.128

Smooth saved-route travel and prioritize Gamekeeper as soon as the actual anchor is reached.

- Advance a crossed straight waypoint only when consecutive measured positions prove local forward passage inside the saved corridor and all checked segments are clear. This avoids turning back toward an already-passed sample. Keep sharp corners, final connectors and the exact 0.5-unit anchor arrival.
- Follow gentle curves continuously with shorter bounded steering lookahead, instead of stopping and correcting at every accumulated bend. Preserve collision, corridor, movement-progress and turn-response checks.
- Finish confirmed anchor arrival before another farm-target defense slice. Leave 500 milliseconds for approach between bounded defense slices so repeated nearby spawns cannot indefinitely postpone the last step.
- Hand an approved living Gamekeeper to normal priority targeting before restoring saved facing. Keep the original anchor/direction for the return after its defeat; retain completed revival/repair progress and ordinary engagements. Respect disabled farming/priority, occupied alternatives, identity, floor and target protections.

Validate crossed-waypoint, blocked/corner/final-route, gentle-curve, defense/arrival ordering and Gamekeeper handoff regressions, builds and existing offline/UI/installer checks without game input. Live movement and combat remain to be verified after upgrading; leave the running application and live configuration untouched.

## Release1.127

Fix the Saved routes panel alignment in Navigation.

- Reserve the themed dropdown's actual height so it cannot overlap Start route. Keep all route controls aligned with consistent spacing, and compact section headers that have no summary.
- Show the selected targets even before a 3D map is loaded. Put each saved route and unassigned-route notice on a separate wrapping line.
- Use a readable Use alternative routes caption with the occupied-spot explanation underneath and in its tooltip. Preserve the original setting, route actions, hotkeys and recovery behavior.

Validate disconnected native layouts at normal and minimum window sizes, populated/long labels, control/cell bounds, scrolling and collapse/expand behavior. Build and existing offline/UI/installer checks run without game input; leave the active application and live settings untouched.

## Release1.126

Apply the selected compact Slim Inspector item overlay and add saved tooltip sizing.

- Replace the large two-column Upgrade Desk with a graphite-and-lime vertical panel: item identity and best-stat badge, one focus strip, every readable stat's value/grade/target gap, and separate gem and +10 estimates.
- Add Tooltip size % under World & Tools > Item grades, from 75% to 200% in five-point steps, default 100%. Save automatically, refresh the hovered item after a change, and scale equipment and message tooltips together with Windows DPI. Fit to the monitor without replacing the saved percentage; keep size local during profile transfers.
- Preserve all-stat unfinished Auto focus, manual focus/target choices, jewelry suppression, original loot icons and passive/click-through display. Item calculations and gameplay behavior are unchanged.

Validate clean builds, offline calculations/settings migration, profile-local sizing, native tooltip rendering at four sizes with all 13 supported fields, DPI/secondary-monitor bounds and disconnected minimum-window UI checks. Validation sends no game input and leaves the running application and live settings untouched.

## Release1.125

Remove obsolete source and consolidate repeated helpers while preserving current behavior.

- Remove uncalled legacy combat, navigation, item-reading and UI helpers, unused fields and abandoned Ironbound decoration code. Keep active recovery, targeting, route safeguards and diagnostic tools.
- Share the identical transparent loot-overlay bitmap creation and scaling logic, rounded-control geometry and installer user-data preservation checks. Keep the current layout, in-game item images, reset controls and saved options.
- Remove three unused artwork files from embedded resources (3.2 MB of source assets), stop packaging superseded UI guides and correct current branding, hotkeys and public download instructions.

Run clean builds, existing offline calculation/recovery/movement checks, native main/loot/item-overlay rendering and installer preservation checks. Validation sends no game input and does not replace the running installation or change user settings, routes, profiles or logs.

## Release1.124

Keep Auto focus on every supported unfinished stat and prevent jewelry popups.

- Compare all 13 supported stat fields with their own selected grade threshold. Exclude values at or above the target; if every supported target is reached, show completion instead of falling back to a reached stat. Manual focus preferences and the full stat overview remain available.
- Exclude jewelry using item category metadata rather than item names. Rings, necklaces, amulets, earrings and bracelets do not open automatic hover or manual-hotkey overlays. Dismiss a previous equipment overlay immediately when jewelry is recognized; keep ring-named weapons eligible.
- Add every-stat ranking, exact/above-target exclusion, unavailable-threshold, jewelry-category and hidden-window suppression checks. Preserve target/focus choices, passive display, settings and all gameplay behavior.

Offline calculation and UI checks run without game input. Exact live jewelry-category coverage and hover behavior still require confirmation after upgrading; the running installation is not replaced during validation.

## Release1.123

The item Upgrade Desk now defaults to the unfinished stat closest to its target grade.

- Auto compares each stat's current value with its own target threshold and focuses the highest percentage, rather than the first upgradeable stat. This keeps HP, damage and other stat scales comparable and respects fixed grades or each stat's automatic AAA/S target.
- Manual focus choices still take priority. Reached and unavailable targets do not displace a known unfinished target; ties keep tooltip order. Missing saved stats fall back to Auto, and missing gem estimates stay explicitly unavailable.
- Update the focus selector and help text. Add offline ranking, target-change, fallback and native rendering checks; preserve existing settings and passive overlay behavior.

## Release1.122

Return to the farming anchor after Gamekeeper before selecting ordinary engaged enemies, and smooth route steering and short forward corrections.

- Solo melee Mimic, Pulkhan, Tribal and Tower farming always retains its activation anchor and facing through a Gamekeeper excursion, including older settings with the optional return checkbox off. Handle the confirmed-death return before input preflight can select surviving enemies outside the ordinary leash. Keep the encounter for afterward; other solo modes retain their return preference and group mode follows the group.
- Use the shared half-unit arrival and settling checks for stationary Gamekeeper return; other optional solo modes keep their existing arrival envelope. Keep return ownership through saved-facing restoration, allow protected stationary defense near home, and clear the request only after confirmed arrival. Measured distance progress renews the travel allowance, bounded facing retains its own allowance, and the whole return retains a two-minute hard cap. Preserve health, focus, new-Gamekeeper interrupts and collision protection.
- Separate saved-route steering from checkpoint arrival. Look ahead up to 3.5 path units through locally clear gentle bends, confined to a narrow corridor; brake for cumulative curves. Preserve exact sharp-corner and final-anchor capture, original waypoint progress, blocked-segment checks and the configured turn cap. Logs distinguish steering direction from checkpoint direction.
- Run each short forward correction with one serialized worker responsible for W release. Capture the verified window guard before the pulse, check stop/focus/cancellation while held, and run full scene preflight before and after on the controller thread. Buffer bounded timestamped input audit events until key-up so logging and UI delays do not extend the requested hold. Stop invalidates queued pulses; overlapping pulses and duplicate accepted releases are rejected.

Validate the recorded Gamekeeper handoff distances, surviving encounters, mode preferences, gentle curves, sharp bends, blocked/unknown paths, exact arrival, delayed UI continuation, stop/focus loss, overlapping input and existing offline/UI checks without game input. Settings, routes and calibration profiles are preserved. Live smoothness and the complete death/repair/return cycle still require confirmation after upgrading; the running application is not replaced during validation.

## Release1.121

Stop anchor corrections from bouncing across the saved point and starving stationary combat.

- Remove duplicate saved-route destination points so the final waypoint actually brakes. Brake at sharp corners and short final connectors while retaining continuous forward travel on straight route sections.
- Time short forward corrections with stop/focus checks while held; run slow scene checks after key-up. Learn movement speed from the whole settled step, including delayed client position updates. Use the same half-unit arrival envelope for loot, melee-assist and saved-route returns, then confirm settling and the saved facing.
- Let measured turning progress extend the saved-facing allowance within a shared twenty-second bound. Retain the separate no-response watchdog, speed/pixel limits and cancellation. Slow reader cadence no longer forces every turn correction to use only twenty milliseconds.
- Once already near the stationary anchor, let incoming-damage handling proceed to protected target selection instead of repeatedly returning to the same spot. During an enabled farming return, defend against approved living targets already in the existing swing range, without chasing or assist steps. Renew character, zone, floor, target, HP and input checks; retain self-heal rules and completed revival/repair phases.
- A near-anchor movement fault now preserves the pending return and retries after bounded stationary defense rather than releasing attack to wait passively for death. Travel-only routes with farming disabled, distant travel, unknown health, repair ownership and stop/focus guards retain their restrictions.

Validate recorded overshoot geometry, delayed step feedback, minimum-frame arrival, route duplication/corners, low-cap and slow-feedback facing, target/range/identity changes, damage handoff, cancellation and existing offline/UI checks without game input. The running installation remains untouched; a complete live death/repair/return cycle still needs confirmation after upgrading. Short-step release retains one input owner on the UI thread, so operating-system scheduling can still delay key-up.

## Release1.120

Keep completed revival and repair steps attached to each death until the character reaches its saved anchor. A route or final-facing retry now resumes the return instead of treating a living character as newly revived and opening repair again at the combat spot. A real new death starts a fresh recovery episode; unknown health and stale asynchronous completions cannot finish it.

Give saved-facing restoration a bounded allowance based on the configured turn speed, with settling time, instead of a fixed two seconds. Preserve the separate no-response watchdog, exact anchor/facing arrival, stop/focus/identity/health checks and saved routes. Logs distinguish a facing deadline from missing turn response and show which recovery steps are already complete.

Precisely verify and correct the repair pointer before the one recognized button click, using the same guarded alignment as revival. Record requested and actual click positions for diagnosis. An unrecognized or stuck confirmation still stops without repeating Yes.

The inspected Release1.119 run revived and repaired successfully, reached the anchor, then a facing fault caused an unnecessary second repair. A subsequent real death revived successfully but the repair confirmation remained recognized after one click. The later passive screenshot had no popup; the exact cause of that missed confirmation is unproved. Offline recovery, slow-turn, deadline, cancellation and input regressions pass. Live full-cycle validation remains necessary after upgrading. Settings, routes and calibration profiles are preserved.

## Release1.119

Replace the weapon/armor grade tooltip with the selected Upgrade Desk design: dark green cards, an explicit best-stat badge, focus-stat metrics, progress bars for every readable stat, regular-gem suggestions and separately labelled upgrade/socket estimates. Choose a focus stat in World & Tools > Item grades; Auto and missing-stat fallback use the first actionable plan. Preserve existing grade targets, automatic popup/hotkeys, hover dismissal and passive click-through behavior. Fit the panel to monitor edges and available space at different display scales. Unknown stats receive no invented grade or gem plan.

Reuse the existing item reader, grade table and gem planner. Offline data, passive-window and native-render checks cover weapon, armor, unknown, reached and empty views, focus/profile restoration and display bounds. Existing disconnected UI and behavior checks pass. Settings and profiles are preserved; live hover appearance still requires checking after updating. No game input or installed-app restart was performed during validation.

## Release1.118

Faster, smoother body turns during melee combat: follow the current target during ordinary combat waits in short cooperative steps, retain acceleration through the attack-facing tolerance, and settle visual facing near one degree. Accumulate fractional mouse corrections instead of forcing a pixel per poll; limit normal corrections to two frames and prevent large catch-up packets after longer pauses. Keep the saved 30–360°/s cap, existing attack/range rules, delayed-heading accounting, held attacks, and stop/focus/health/activity guards. Ranged camera aiming, calibration pulses and forward speed retain their existing behavior.

Offline simulations at 150°/s settle a fixed 120° turn in 848 ms with a largest 2.52° correction; simulated 32 ms feedback settles in 864 ms with a largest 4.90° correction. These are controller simulations, not live-game timings. Delayed/partial feedback, turn bounds, cancellation, held attack preservation and disconnected UI checks pass. Existing settings and profiles are preserved; live smoothness still requires checking after updating.

## Release1.117

Direct feature links under five collapsible sidebar groups; persistent Start/Stop/Connect actions; dedicated Farming and Overlays pages. New original app and desktop logo. Hunt setup now has a saved 30–360°/s horizontal turn cap, default 180°/s, shared by travel, facing and ranged camera yaw. Existing settings, profiles, bindings and stop guards are retained. Offline validation covers native navigation, small-window layouts, profile restoration and rate bounds; live smoothness remains to be checked after updating.

## Release1.116

Add the three selected features from PoteHunter 2.0.45 while preserving PlayPoteBot's custom controls.

- Save, apply, import and export named settings profiles in Settings > Profiles. Switching requires stopped hunting and route recording, and backs up current settings. Preserve custom potions, smart skills, ranged packs, durability thresholds and independent route/loot radii. Keep character identity, global keys and display placement local; retain routes, recovery/login files and logs.
- Import calibrated MapTool WorldGeometry from Routes > Navigation 3D. Require the connected client hash and zone to match, review marker alignment before saving, and keep the previous map manifest as a backup. Feed decoded collision polygons into the existing route planner alongside protection zones and observed stalls. Reject incompatible or unconfirmed maps; distant geometry is excluded from bounded path searches.
- Add a read-only hovered weapon/armor grade overlay with gem and +10 estimates. Require linked file and loaded-code checks, validate changing item identities and tooltip records, preserve focus in automatic mode, and reserve existing bot shortcuts. Estimated upgrades and sockets beyond the two mapped fields remain explicitly unconfirmed. Unknown layouts disable this optional reader.
- Align the five main menu buttons vertically on the left. Move Updates into Settings and preserve the existing Orbital Ops appearance, bindings and public update source.
- Verify profile round trips/backups, restored custom UI controls, collision identity gates and polygon detours, grade arithmetic/planning, existing behavior and offline window layouts. Live hovered items and user-imported zone maps still require confirmation in the game after upgrading.

## Release1.115

Rename Radar to MiniMap and recover available player names from the client’s ID-bound name records.

- Use MiniMap throughout navigation controls and overlay headings, including Sentinel MiniMap. Preserve saved options, Zone 8 enemy-only activation, alert range and sound behavior.
- Read the separate active player-name record used by the client’s own player-ID/name consumers. Enable this optional reader only after linked on-disk and loaded-code checks; keep valid existing names and player-ID fallback when a name is unavailable.
- Bind each name to a fresh accepted player body, reject duplicate or changing records and malformed text, and avoid retaining names across polling, zone or connection changes. Apply alternate names to the observer display only; preserve existing combat and route-avoidance name behavior. Limit scan time and read count to protect polling responsiveness.
- Add private status counts for available/missing names and reader availability. Validate name decoding, identity/race rejection, unsupported layouts and overlay rendering offline. The independent live name probe was denied read access by Windows, so live names require confirmation after upgrading.

## Release1.114

Show player names directly on the game map overlays.

- Replace relation-prefixed map captions such as Same faction: PlayerName with the player's available name. Apply the same caption to 2D map/radar overlays and 3D player markers so names have more room and are easier to read.
- Keep faction and relation colors, accepted player identities, fresh-reading gates and explicit player-ID fallback for unavailable names. Preserve enemy-only automatic Sentinel in Zone 8.
- Verify same-faction, enemy, party, unavailable and Unicode names in existing offline map rendering checks. This changes labels only; movement, targeting and saved settings are unchanged.

## Release1.113

Automatically activate enemy-only Sentinel Radar in Zone 8.

- Open Sentinel on fresh connected readings in Zone 8 and hide it automatically on leaving the zone, disconnecting or losing fresh readings. Keep the saved on/off choice as Auto Sentinel in Zone 8, enabled by default.
- Display only recognized opposing-faction enemies. Remove same-faction, party and unknown players from Sentinel markers, names and counts, and remove their unused legend entries. Keep actual enemy names, stable numbers, overlap counts and ID fallback.
- Preserve the range slider, saved overlay position and sonar controls. Entering Zone 8 establishes a silent baseline; later enemy arrivals retain existing health, focus, freshness and sound guards. General map navigation and combat behavior are unchanged.
- Validate zone entry/exit/reentry, disconnected/stale/off states, both local factions, mixed-player filtering, duplicate identities and rendering offline without game input or replacing the running app.

## Release1.112

Show enemy player names prominently in Sentinel Radar.

- Make the nearest enemy's available character name the main heading, with faction and PvP status secondary. Give the five nearest player names more room beside their distance and bearing.
- Show available names on individual radar badges while retaining stable numbers, accurate overlap counts and unchanged player positions. Names stay attached to the observed player identity as enemies move.
- Preserve explicit player-ID labels for unavailable names, and existing safe-zone, unknown-zone, freshness and audio rules. This changes presentation; no alternate name reader or server attackability flag is inferred.

## Release1.111

Repair low durability during combat and resume the engaged target.

- Allow the equipped-durability threshold to trigger during active engagements, incoming damage and nearby spawns. Remove the quiet-health and finished-fight requirements; keep route/revival ownership, known living health, client/focus and equipment identity checks.
- Check the threshold inside prolonged target engagements as well as the outer hunt and support loops. Pause held attack for one recognized inventory repair, then refresh the target before resuming; exclude the repair pause from attack and movement watchdogs.
- Verify fresh recovery above the threshold on the same equipped items while allowing normal combat wear on previously healthy items. Unknown, unchanged, replaced or still-low results stop without repeating Yes.
- Update the repair descriptions and preserve existing thresholds, setup profiles, routes and after-revival repair. Validate combat admission, wear, health/focus/travel blocks, recovery proof and single-attempt bookkeeping offline. Full updated live combat repair remains to be verified after upgrading.

## Release1.110

Repair automatically at the lowest equipped-item durability threshold.

- Add independent Repair low durability controls in Equipment repair and the Overview recovery section. Choose an inclusive 1–99% threshold, default 20%; the new trigger is opt-in and retains the existing repair-after-revival option and saved repair setup.
- Read the lowest current / maximum durability from the client’s actual equipped, repairable items. Verify equipment ownership, eligibility, value/update/repair code signatures and loaded code; inspect every equipped slot twice. Exclude bag, empty and confirmed nonrepairable slots; unknown, invalid or changing readings never trigger repair.
- Wait for a quiet living-health window, finished combat, group/healer duties and route/anchor returns. Repair in the owning hunt/support loop, release held input and preserve the character’s location and anchor. Stop if damage, threats, equipment or higher-priority duties change during repair.
- Attempt once per low-durability episode, share manual/revival attempt bookkeeping, and require a fresh improvement on the same equipped items above the threshold before resuming hunting and pickup. Unchanged or unavailable results stop without repeating the repair confirmation.
- Preserve existing settings, profiles, routes, combat behavior, in-game artwork and public updater. Include durability telemetry, a bounded read-only observation command and offline reader/policy/UI checks. The running application is not replaced and no live repair is triggered during validation.

## Release1.109

Orbital Ops interface and new PlayPoteBot branding.

- Implement the selected Orbital Ops design with graphite surfaces, lime accents, clear sans-serif headings and the philosophical Overview title “The Endless Pursuit.” Keep Start/Stop, mode, target filter and expandable configuration in a persistent left rail; retain the bottom Overview, Hunt, Routes, Recovery and Settings navigation.
- Replace the card-heavy overview with a circular current-zone route scanner, session telemetry, original target portraits and a cargo resource strip. Use the existing saved routes and fresh player readings; show disconnected or unavailable data explicitly. Preserve wallet net gold, detected item totals and existing session rates.
- Restyle the loot overlays as Orbital HUD, Fold, Ledger and Strip while preserving the original in-game item images and gold colors, saved positions, size/transparency controls, reset actions and passive window behavior.
- Add an original lime orbital P emblem with a play-shaped cutout for the app, overlay headers and Windows display icon. Include transparent artwork and seven icon sizes from 16 to 256 pixels; retain the former branding in the source backup.
- Keep all six cargo resource tiles inside their row and reachable by scrolling in compact windows, including smaller Windows displays.
- Keep existing control bindings, four modes, saved settings, running-state locks, guided recovery setup, public updater and 2D/3D navigation. Stack detailed settings cards in compact windows. Validate native layout, mode/target bindings, unknown readings, overlay scaling/transparency/reset behavior and unchanged game artwork offline without sending game input or replacing the running application.

## Release1.108 — Readable map labels

- Replace blue, gold and orange map text with near-white lettering, dark tags in 2D and dark outlines in 3D. Labels stay legible over light map artwork and busy terrain.
- Keep route lines, anchor markers, player status colors and chest markers distinct; compact 2D tags retain a colored edge. Apply the label treatment to the overview map, navigation, radar and route overlays.
- Preserve existing routes, settings and movement behavior. Validate hidden native 2D/3D rendering without sending game input or replacing the running app.

# Release notes

## Release1.107

- Fix Start/F8 rejecting valid saved paths because the player's route-entry height differed from the end anchor's height. Validate the selected target's recorded path, character, zone and configured entry corridor; retain the endpoint floor check when already near the anchor. Check alternative routes' actual shared XY origin rather than comparing their unrelated end-anchor heights. Existing route files contain XY waypoints and end-anchor height only, so path elevations remain unknown.
- Skip redundant end-anchor height validation before starting accepted route travel. Confirm the actual arrival height against the destination before completing return and facing. Show a corridor/character/map/floor explanation when assigned routes cannot be joined, and include start position, height and path distance in private diagnostics. Preserve recorded points, final anchor/facing, occupancy fallback and movement guards.
- Identify Sentinel enemies with stable session numbers, numbered radar markers and a nearby list of up to five individual enemies with distance and bearing. Preserve each enemy's number as distance order and watch range change; reset labels for another character, connection or zone.
- Show overlap counts and numbered cluster badges with leader lines to actual observed positions. Draw enemies above the self marker so nearby or coincident opponents remain visible. Clustering changes drawing only and never merges player identities.
- Use verified names when available and explicit player UID labels when the client name field is empty. Preserve raw names and name availability in private telemetry; no alternate real-name reader has been verified. Reject ambiguous duplicate UIDs consistently across player recognition, radar and alerts.
- Validate identity replacement, brief reading gaps, memory bounds, range changes, stale/disconnected state, five-player lists, coincident markers and native passive-window rendering offline. Actual game input and the running installed application are not changed during validation.

## Release1.106

- Implement the selected Sentinel Radar: compact navy/brass, north-up circular player radar with nearest opposing enemy name, faction, distance, bearing and count. Drag its passive header to reposition it; its body passes clicks through. Preserve the existing terrain radar and routes.
- Add a saved Enemy alert range slider from 1–100 map units in one-unit increments (default 25). Apply the same radius to the radar and arrival sounds. Navigation has independent overlay/sound toggles, volume (default 45%), and position reset; presentation settings can change during hunting without saving unrelated combat edits.
- Play a nonblocking paired sonar for new enemy entries only in confirmed PvP zones. Use silent startup/range/identity/zone baselines, full player identities, duplicate-UID rejection, range plus three-unit exit hysteresis, two-second exit grace, coalesced arrivals and a three-second sound gap. Muted/background arrivals are never replayed later. Unknown/dead local HP, stale/disconnected readings and lost game focus pause sound.
- Freeze local and other-player health with the accepted faction poll. Keep unknown faction/non-PvP/party statuses distinct; confirmed-dead bodies are omitted. Existing model-based faction and limited verified zone rules apply; enemy status does not establish actual attackability.
- Validate radius boundaries, slider persistence during hunting, audio lifecycle through an injected backend, generated PCM, stale/death/focus gates, immutable snapshots, multi-monitor positioning and native passive window rendering offline. No gameplay input, installed-app replacement or actual audio playback is used in validation; live behavior remains to be checked after updating.

## Release1.105

- Recognize exact Human/Kartefant and Akkan/Merkhadian player body families, compare them with the actual connected character, and label opposing factions as Enemy only in confirmed PvP zones. Zone 8 Caernarvon is PvP; Zone 12 Almighty Land uses the previously user-confirmed opposing-faction non-PvP rule. Other zone IDs remain unverified, including map-geometry aliases. No complete current server rule catalog or safe-area boundaries have been verified.
- Show distinct player diamonds and readable status in the 2D radar, 3D radar and main Navigation map, with a visible faction/zone legend. Retain same-faction, validated party, non-PvP opponent and unknown statuses. Same faction does not promise friendliness during guild wars; Enemy describes opposing faction on a PvP map, not confirmed attackability or an attacker identity.
- Copy player identities, body models and party classification from one accepted poll. Clear or hide recognition after stale readings, character/zone changes, death-time read gaps and disconnection; exclude confirmed-dead bodies, self and NPC guards. Extend private live status with faction, relation, rule provenance and enemy count within 25 units.
- Validate faction inversion, exact-model limits, party identity, unknown zones/aliases, marker range, immutable snapshots and visible 2D/3D status through offline policy/UI checks. Existing combat targeting, movement, courtesy, routes, revival, repair and settings are unchanged. Sound and notification layout remain design previews pending selection; live model/rule confirmation remains to be checked after upgrading.

## Release1.104

- Add recovered 3D terrain, available buildings and original game-map artwork to both the in-game radar and saved-route overlay. Each has a saved independent 3D switch, with a shared north-up tilted/top camera choice. Display controls remain in Routes → Navigation → Overlay options; map layer visibility and artwork opacity apply to all 3D views.
- Keep the radar centered on the character at the chosen map-unit radius. Retain copied player heading and direction cone, red/orange living target markers, teal engagement rings, gold live and amber remembered chests, travel trail, current path and blocked/avoid annotations. The route overlay fits current-zone paths and the player, including colored anchors/facing, the configured start corridor and an anchor-area ring that does not change its zoom.
- Load the current game zone independently of the main Navigation page and map selector. Reject stale zone loads and dynamic markers, bound retained map geometry, and render into cached bitmaps from hidden native windows. The actual overlays retain their click-through, no-activation and focus/visibility rules. Unavailable map data or hardware rendering falls back to the existing 2D presentation.
- Bound passive readback resolution and refresh cadence; release frames, textures and native contexts on zone reset/close. Include the navigation guide in portable and installed packages. No game models, artwork, local routes, profiles, telemetry or diagnostic screenshots are bundled or published.
- Validate saved display options during hunting, current-zone independence, stale/disconnected readings, original 2D fallback, hidden native GPU radar/route rendering, radius annotations, layer opacity, resource cleanup and focus preservation. Existing combat, loot, revival, repair and route-following decisions are unchanged. Live GPU responsiveness and terrain/bridge visibility require verification after updating.

## Release1.103

- Add the approved native 3D Navigation view with recovered terrain, available buildings and static objects read from the verified local game installation. Drape the original game map over the terrain with a saved 0–100% opacity control; keep neutral terrain outside verified map artwork bounds.
- View the current map or a supported map, orbit, pan, zoom, switch to a north-up top view, fit the map or selected target's saved routes, and center on its anchor or a fresh connected player reading. Retain Primary and Alternative route colors, anchor facing and live target/chest markers. Missing terrain splits route visuals instead of inventing heights.
- Group existing route recording, management, overlay and loot controls into expandable sections. Persist terrain, object, artwork, route and anchor visibility independently. The original 2D view and passive in-game overlays remain available, with automatic 2D fallback for an unsupported client, missing map data or unavailable hardware renderer.
- Load maps in the background with cancellation, stale-zone rejection and a bounded two-map cache. Use built-in Windows OpenGL without an added browser/runtime download. Game models, artwork, routes, profiles and runtime evidence are not bundled or published.
- Validate scene codecs, file/count/index/path bounds, corrupt-data rejection, exact terrain/object comparisons against independently decoded private exports, map orientation and clipping, layer settings, native readback where supported, original route guards and UI accessibility. Terrain is a visual aid: bridge height, collision and walkability remain unverified and do not change movement, combat, revival or repair logic. Verify live rendering and responsiveness on the user's GPU after updating.

## Release1.102

- Apply the approved navy-and-brass Wayfinder interface and logo pair 1: matching application emblem and Windows desktop icon, bottom navigation, route map and six expandable command sections. Closed sections retain live summaries; Collapse all and keyboard/accessibility actions change presentation only. Detailed Setup, Support and other settings remain available.
- Add saved route-corridor and solo loot-pickup radii, each adjustable from 0.5 to 30 map units in 0.5-unit steps, default 10. Start and client-resume path joining use actual path segments and the configured corridor. Solo pickup trips use the separate loot radius and retain return to the saved anchor/facing. Existing death-return and fallback safety limits remain intact.
- Expose the existing anchor farming radius with half-unit adjustment while preserving its 5–150 bounds and saved values. Draw the configured route band and a subtle anchor-area circle. These controls do not change melee range, stationary target rules or Gamekeeper priority.
- Keep two-way settings bindings and hunting, busy, recovery and healer locks. Preserve target-specific routes, original loot item images, wallet-baseline gold, loot/timer resets, hunting and loot session logs, repair/revival/login profiles and the public updater.
- Validate radius serialization and path/loot boundaries, native section expansion/accessibility, settings synchronization, running-state guards, minimum-size layout and existing offline checks. Live gameplay and the running installed application are not altered by validation; route travel and pickup at newly chosen radii require verification after upgrading.

## Release1.94

- Fix Login setup freezing before the capture countdown. A live thread stack showed the UI blocked in Process.MainModule module enumeration while verifying Client.exe. Use Windows QueryFullProcessImageName with limited query access for executable identity; retain exact path/hash checks for the launcher and client.
- Discover the selected process off the UI thread with a five-second timeout and dispose late results. Both launcher and login captures show the checking stage and recover their controls after failed discovery. The same direct path query protects login action rechecks from module-enumeration stalls.
- Preserve launcher Play calibration, saved login steps, encrypted password, focus/window checks, recovery routes and all existing combat/loot settings. No game input or live profile changes were used during diagnosis.
- Validate direct executable identity, responsive background discovery/timeout, and existing login/recovery checks. Full capture, live login and automatic recovery still require the user's local test after updating.

## Release1.93

- Client crash recovery starts the configured game launcher instead of Client.exe. Setup now captures a separate paired launcher screen marker and Play/Start button. Recovery clicks Play once, waits up to 90 seconds for the verified Client.exe, then continues the saved password/login/character sequence and route return.
- Keep client.exe selected for identity checks and login calibration. Existing saved login steps and Windows-encrypted password remain compatible; add the launcher calibration before enabling recovery. Launcher executable changes require a new Play capture. PPB never starts Client.exe directly.
- Test login can exercise launcher startup when the game client is closed; when the calibrated client already exists, it tests only login. Multiple/mismatched launcher or client processes, focus loss, missing controls, changed executable or timeout stop without repeated Play clicks. F9 cancellation and existing revival/repair, combat, loot and route behavior remain intact.
- Offline checks cover launcher/login ordering, single recognized Play action, invalid or changed launchers and preservation of existing login calibration. Live launcher startup and full recovery require local calibration and testing.

## Release1.92

- Setup > Death recovery adds Login setup, Test login and an independent client crash recovery toggle. Calibrate up to eight ordered screen/button steps, including one password field, login, character and Enter Game. Setup captures cropped static markers and never clicks the game; Test login performs the saved sequence once and leaves hunting stopped.
- Save the password locally using Windows account encryption. Credentials never enter logs, settings, process arguments, clipboard, Git or release packages. Existing saved account name remains in the game. Local setup survives updates; changed executable/window dimensions require recalibration.
- On an unexpected client exit during solo hunting, retain character, zone, target selection, anchor/facing and the compatible saved route. Relaunch one verified client, recognize each step before one action, confirm the correct living character, return along the saved route and resume. F9/manual Stop cancels recovery; focus loss, wrong character/zone, missing UI or route stops it without blind retries. Login and startup deadlines are bounded. Group/healer recovery is not supported.
- Offline validation covers ordered/single actions, recognition timeout, cancellation, protected password round trip, invalid setup and identity/route guards. Windows packaging checks verify the build and installer; live login/input and full crash recovery require the user's local calibration and test. Existing revival/repair, target routes, wallet tracking, PPB UI and updater remain intact.

## Release1.91

- Apply the selected Crownfire design: warmer charcoal/ember-gold panels, illustrated target presets, icon navigation/actions, and native keyboard-accessible recovery switches. Keep the existing sidebar and two-column section layout.
- Rename the displayed application, Windows product metadata, installer and shortcuts to PPB. Retain the existing executable, installation directory, updater feed/asset names and application identity for seamless upgrades and data preservation.
- Match loot, radar and route overlay colors to Crownfire; preserve six approved resource icons, actual-wallet gold accounting, saved size/opacity/position, route colors and passive overlay behavior.
- Validate existing combat/recovery/target-route settings bindings and running-state locks with offline checks and rendered UI review. No live gameplay or installed-app replacement performed.


## Release1.90

- Apply the approved Imperial Command interface inspired by PlayPOTE: midnight panels, gold trim, a permanent sidebar, public website banner artwork and serif headings. Reuse the existing controls, page selectors, settings and running-state guards.
- Apply matching loot, radar and saved-route overlays. Imperial HUD replaces the Dungeon HUD presentation at the same saved style index, with the approved item images, aligned totals/rates and bounded source/recent-drop rows. Runic Fold and Runic Strip share the palette; preserve saved style, position, size and background opacity.
- Make Primary routes gold, Alternative 1 blue and Alternative 2 rose consistently in radar and the route overlay. Keep monster/engagement/obstacle colors and passive window behavior.
- Validate the native sidebar, all pages, minimum-size controls, settings bindings, transparent background/scaling and overlay passivity with offline checks. Preserve target-specific routes, wallet calculations, recovery and profiles. Live gameplay and the installed application are not changed by development checks.

## Release1.89

- Save a separate Primary route and two alternatives for each target preset or custom name filter. Changing the selected target loads its routes in Overview, Navigation, radar and the route overlay; capitalization and outer spaces do not create duplicate sets. An empty filter has its own All targets set.
- Use only the selected target's routes for the ten-unit startup corridor, revival return and occupied-spot fallback. Retain existing character, map, height, anchor, facing and recorded-path checks. Cancel an unfinished recording when its target changes; lock route mutation while hunting.
- Preserve routes from older releases as unassigned. Select their intended target and use Navigation > Assign existing routes on an empty set; create a local backup before migrating. Saving or clearing one target leaves other sets and unassigned routes intact. Preserve unreadable or unsupported route files instead of overwriting them.
- Validate target isolation, all three slots, custom filters, normalization, old single/set formats, profile/facing preservation, scoped clear, recording cancellation, stale-library saves and invalid-file handling. Build, offline recovery and UI checks pass without live game input or replacing the running app. Existing settings, updater and wallet tracker remain compatible; live route travel still requires verification after upgrading.

## Release1.88

- Replace estimated ground-pile gold in the session total and gold/hour with the actual character wallet balance minus a verified session baseline. Validate the optional reader against both inventory currency display routines and the loaded client, then require stable wallet, scene and character reads. Unknown or stale balances display an em dash rather than invented income; other resource counters retain detected-drop estimates.
- Preserve the baseline through death, revival, repair, map travel and reconnecting the same character. Reset loot starts a new wallet baseline; Reset timer preserves session totals and starts a new rate window. A different character requires a loot reset. Repairs, purchases, trades and other income affect net gold, including negative totals; wallet data cannot identify which enemy supplied income.
- Label Gold (net) in all overlay designs and Overview, with current wallet/baseline details in the Overview tooltip. Keep gold-pile estimates separate for diagnostics and source attribution; they never affect wallet earnings. Save death/reset rows to loot-wallet-session-log.csv with wallet freshness, balance and baseline columns, preserving the old drop-estimate CSV.
- Validate gains, spending, unsigned balances, duplicate reads, unknown/stale readings, character changes, reset behavior, route/revival retention, strict signature conflicts and CSV compatibility. The read-only guarded reader matched the live inventory gold display during development; long-run wallet earnings still require verification after upgrading. Existing combat, input, recovery, routes, icons and updater remain unchanged; the running app was not replaced.

## Release1.87

- Apply the approved original inventory images for Silvin, Mithril, Iternium and Fehu to Runic Fold, and use the game's Diamond image for Gems. Keep Gold's coin icon and the two-row brass/ivory layout shown in the preview.
- Bundle the five small transparent PNGs inside the executable, cache them for redraws and preserve their pixel-art appearance and proportions. No game image files or installation paths are needed at runtime. Remove the replaced Runic Fold vector artwork.
- Retain adjustable background opacity, 50–200% size, saved position, live totals/rates and timers. Verify exact embedded assets, scaled/native rendering and background opacity with the existing offline/UI checks. Loot calculations, combat, recovery, routes and updater behavior are unchanged; no live game input or running-app replacement.

## Release1.86

- Add a dark forest/iron background to Runic Fold and Runic Strip. Navigation > Background opacity % adjusts just the background from 0% (fully transparent) to 100% (solid) in 5% steps; start at 40%. Keep text, icons and brass trim bright instead of fading the whole overlay.
- Save opacity independently of style, position and size, including changes while hunting. Reuse it across both transparent styles and retain header dragging, passive-window behavior and screen fitting. Existing Dungeon HUD/Parchment Ledger presentations and loot calculations are unchanged.
- Validate background alpha at 0/40/100%, unchanged foreground artwork, multiple sizes, bounded/saved settings and native presentation without focus changes. Build, offline and UI checks pass without live game input or replacing the running app.

## Release1.85

- Apply the selected Runic Fold loot overlay: two rows of three resources, outlined ivory text, brass frame details and native vector icons on a transparent background. Show Silvin as a plain grey low-tier metal ingot; retain Gold, Mithril, Iternium, Fehu and Gems.
- Display live full totals, hourly rates, session/active timers, zone and kill/drop totals. Keep header dragging, saved position and 50–200% sizing; fit both transparent designs to the screen without changing the saved size. Replace the old Compact Ribbon with Runic Fold and select it once on upgrade, preserving subsequent design choices.
- Validate transparent alpha, scaling, large amounts, empty/long sessions, saved choice/position and passive native composition across opaque/transparent switches. Loot calculations, logs, combat, routes, revival, repair and updater behavior are unchanged. No live game input or running-app replacement was performed.

## Release1.84

- Apply the selected Ironbound medieval interface: deep forest and dark iron surfaces, warm brass outlines, ivory text, serif headings and restrained shield/engraved corner details. Preserve compact native forms, keyboard focus and all existing settings bindings.
- Carry the shared palette through Overview, Setup, Support, Monitor, Navigation, Advanced and Index, including the HP/MP status strip and Windows title bar. Keep the existing transparent loot and route overlays.
- Validate the Release build, offline checks, all page previews, minimum-size layouts, group-healer mode and settings/recovery control access. Existing combat, revival, repair, routes and updater behavior are unchanged.

## Release1.83

- Fix custom repair hammer recognition when a saved selection includes a translucent panel edge or changing world pixels. Add a bounded, exact-position icon structure check with independent inventory recognition; keep separate repair question/Yes, focus, pointer, identity, health and cancellation checks. Existing saved profiles remain compatible.
- Wait for the full 12-second recognition deadline instead of stopping after 25 quick observations. Never repeat the repair confirmation or start route movement while repair UI completion is unverified.
- Record recognition state changes, hammer match scores and the last observed state on timeout. Report explicitly when repair verification blocks the saved-route return.
- Add offline regressions for delayed controls, missing/shifted/different icons, changed backgrounds, independent recognition gates, cancellation and single-click repair completion. Validate the compiled matcher against a private captured frame. Live end-to-end repair and return still require verification after upgrading; the running app was not replaced during development.

## Release1.82

- Reuse saved custom repair setup across game-client updates when its window size and visual controls remain unchanged. Keep the original capture fingerprint as diagnostic metadata; existing version-1 profiles load without rewriting or recalibration.
- Continue requiring inventory, hammer, repair question and confirmation recognition before input, with pointer/focus/identity/health and cancellation checks retained. Reject unsupported/corrupt setups, changed window dimensions and missing or moved controls; a rejected saved setup stops repair with a clear reason instead of switching to automatic clicks. Automatic recognition remains available when no custom setup exists.
- Add offline regressions for legacy profile persistence, changed-client reuse, moved/missing controls, changed resolution, unsupported formats and existing repair cancellation/click guards. Existing settings/routes/profiles remain compatible. Live repair and gameplay remain unverified; the installed application was not replaced during development.

## Release1.81

- Start saved-facing restoration with a fresh turn-response window. Count actual sent pixels rather than requested direction during smoothing waits, replace the 100-poll cap with a two-second deadline, and retry post-loot facing up to three times without swallowing cancellation or disabling real no-response detection.
- Keep an enabled solo run's death recovery available after known turning, blocked-movement or precise loot-return faults. Release all input and watch for confirmed death for up to two minutes; bound the resulting revival/repair/return to ten minutes. Stop, Escape/chat, lost focus, changed character/map, disabled revival, group mode and unknown failures never start or restart this watch. Recovery still requires the recorded route, confirmed living HP, repair when enabled, arrival/facing and the existing resume-on-arrival option.
- Validate the recorded revival route before aim calibration on Start/F8. Expose the movement-fault watch in recovery status and log its start and completed recovery separately from ordinary startup travel.
- Use at least a nominal 16 ms frame for final W corrections. Measure actual held time and movement before release separately from settling displacement; ignore sub-frame velocity samples and filter/limit speed estimate changes. Preserve avoidance checks, the 0.15-unit precise arrival requirement and stop/settle/face/recheck sequence.
- Add offline regressions for unsent turns, idle-to-facing handoff, bounded retry/cancellation, recorded tiny-tap responses, frame-quantized arrival and deliberate-stop/focus safeguards. No live gameplay testing or installed-app replacement was performed. Gamekeeper arrival rules and access-denied diagnostics are outside this update.

## Release1.80

- Prioritize ready Power Drain and recognized self-heal skills at or below the configured character HP percentage in both stationary and moving combat. Self-heals no longer wait for five living enemies, all enemies below 80% HP, the offensive opening delay or the shared 1.5-second skill gap.
- Keep offensive skill pack rules and timing. Preserve the inclusive HP threshold, game cooldown, slot lock, retry, mana-reserve exemption, target/health/focus checks and the immediate HP/identity recheck before activation. Self-heals continue to use the existing valid combat target instead of delaying to retarget another pack member; basic attack restoration remains intact.
- Add a self-heal activation audit with the actual character HP and configured threshold, clarify the healing/targeting tooltips, and add offline regressions for one/three/healthy enemy packs, heal priority, cooldown and retry guards, threshold boundaries and unchanged offensive selection. The separate healing-item/rest threshold is unchanged.
- Existing settings, routes, profiles, Field Console II, updater and auto patcher remain compatible. No live game input or running-app replacement was performed; live gameplay still needs verification.

## Release1.79

- Fix competing movement control near the saved anchor: authorized navigation and loot/return movement retain input ownership while stationary combat stays stationary. Existing focus, death, avoidance, boundary and Gamekeeper checks remain active.
- Brake before the end of precise loot and melee-assist returns, use short measured final corrections, and confirm the character has settled at the original anchor both before and after restoring its saved facing. Keep the 0.15-unit arrival requirement and bounded return timeout.
- Smooth ordinary movement/facing corrections using elapsed-time turn limits and observed heading feedback, retaining fast large turns and unresponsive-input detection.
- Stop repeated no-item rest recovery under incoming damage. Share the three-second quiet period across retries and allow approved in-range defensive targets to be selected while holding a stationary farming position. Target-family, health, identity and ownership checks remain enforced.
- Add offline regressions for delayed heading response, frame-quantized final steps, drift after facing, cancelled/blocked returns, repeated damage and protected defensive targets. Existing settings, routes, profiles, UI, updater and auto patcher remain compatible. Live gameplay validation is still required; no running application was replaced during development.

## Release1.78

- Apply the selected Field Console II design: deeper forest surfaces, brass accents, clearer typography and an Overview dashboard. Keep Start/F8, Stop/F9 and connection status visible across pages.
- Add target-family shortcuts, a custom name filter, existing mode/range/automatic-skill controls, independent Gamekeeper priority, revival/repair/resume toggles and saved-route shortcuts. All edit the existing settings and respect running/busy locks; target shortcuts select one family to match the existing single-filter behavior.
- Show the actual saved anchor, six session resource totals, active timer and estimated gold per hour from the existing tracker. Link directly to detailed hunt, recovery, route and loot pages. No mockup sample values or decorative feature promises are shipped.
- Preserve all combat, revival, repair, navigation, loot and updater behavior. Existing profiles and settings remain compatible; no running application is replaced during development.

## Release1.77

- Increase the solo loot pickup radius from 4 to 10 map units around the saved anchor. Automatic loot sweeps, timed pickup jobs, movement boundaries and out-of-circle pickup checks use the shared ten-unit radius.
- Retain return to the saved anchor and facing after loot, the 0.15-unit arrival tolerance, pickup reach, retry interval and existing recovery/priority/protection guards. Update the UI hint and edge/diagonal/pickup-reach regressions for ten units. Existing settings and profiles remain compatible; live movement remains unverified.

## Release1.76

- Solo automatic pickup now walks to reachable loot within an inclusive 4-map-unit circle centered on the saved anchor once the encounter clears. Timed loot jobs use the same boundary; hunting and enemy ranges stay independent.
- Hold E only within actual pickup reach; refuse pickup beside drops outside the anchor circle. Respect avoidance, other-player protection, focus/health and Gamekeeper priority. Group/healer pickup remains local without a solo anchor excursion.
- Return to within 0.15 units of the saved anchor and restore its saved facing before resuming normal hunting. Retain pending return across interrupted pickup, and stop if a blocked return cannot reach the anchor within the bounded attempt. Retry remaining ground loot on a 15-second interval rather than continuously retrying stuck drops.
- Preserve loot tracker calculations, profiles, routes and existing swing/skill rules. Added circle-edge, diagonal, shifted-anchor, pickup-reach and outside-drop regressions. Validated offline; no game input or current-app replacement performed. Full live movement/pickup remains unverified.

## Release1.75

- Repair recognition now checks stable inventory lettering and allows a bounded uniform color cast on the saved hammer image. This addresses the observed post-revival stop where inventory was visible but its strict background comparison failed. Coordinates, distinctive icon shape, paired question/Yes recognition, focus/health/client guards and single confirmation remain required.
- Stationary Mimic, Tribal, Pulkhan and Tower attacks retain a held swing through a 0.35-unit release margin. Entry remains 2.5 map units with default melee settings; the margin does not start new out-of-range attacks or expand the 1.5-unit assist step.
- Keep basic attack held between defeated targets when another protected, living engaged target remains in melee range. Select the engaged target with the most remaining HP in range, and switch away from an out-of-range locked target when a valid in-range engaged target is available.
- Removed the generic priority-position check that could override the stationary melee envelope. Target healing no longer resets the no-damage swing watchdog; only actual HP loss does. Added bounded range-wait diagnostics for future interruptions.
- Normal empty-pack waits, Gamekeeper priority/return, death/recovery, stop/focus guards, routes, pickup and skill gates remain. The shared combat skill gap stays 1.5 seconds; self heals remain exempt. Added range, target-selection, input-transition and recognition regressions. No live game inputs or current-app replacement were performed; full updated live behavior still needs verification.

## Release1.74

- Corrected custom repair confirmation recognition for translucent dialogs. Recognize the saved static white question and Yes lettering at their saved positions even when the revival portal or scenery changes behind them.
- Keep paired question/button checks, unchanged inventory/hammer recognition, foreground/health/client/window guards, immediate pre-click question validation and one confirmation attempt. Existing compatible repair setups remain usable; no new revival setup is required.
- Live Release1.73 investigation confirmed revival and hammer input succeeded and left the repair question and Yes visibly open. Strict RGB comparison rejected the changing background: question error 57.84 against an 8 limit, while its foreground lettering retained over 97% agreement.
- Added regressions for changed translucent backgrounds, missing/different text, white backgrounds and missing confirmation buttons. Validate the saved failed frame privately without publishing game screenshots, profiles or logs. Live repaired sequence remains to be confirmed after installing this update.

## Release1.73

- Reduced the shared gap between successful combat skill casts from 5 seconds
  to 1.5 seconds in stationary and moving combat. The timer remains shared
  across target changes, and self-healing skills keep their timing exemption.
- Existing five-target, below-80-percent group health requirements, per-skill
  cooldowns, health conditions, mana checks and persistent basic swings remain.
- Build and offline skill eligibility/input checks validate this timing update;
  no live game input was used.

## Release1.72

- Kept the public GitHub updater and manual Download and install option.
  Added automatic patching, enabled by default in Setup > Updates. Checks at
  startup and every 30 minutes; hunting, setup/tests, dialogs and manual route
  recording defer installation. PoteHunter reopens with hunting stopped.
- A staged worker acknowledges the original process before it closes, waits
  for its exit, reserves the application instance and rechecks the latest
  official release plus installer size and SHA256 before silent installation.
  Settings, routes, repair/revival profiles and logs stay in place.
  No forced game/app termination or Windows reboot is requested.
- Failed patches retain local status and installer logs, stop automatic retries
  for that version and leave manual updating available. Toggle automatic
  patching off/on to retry. Administrator startup remains enabled.
- Offline checks cover idle/recording guards, staging boundaries, tampered
  installers and safe argument handling. Windows packaging exercises the
  actual silent patch install routine with user-data preservation sentinels.
  Automatic installation uses the complete release installer, not delta files.

## Release1.71

- Initial automatic patcher build; superseded by Release1.72 before public
  distribution. Release1.72 separates the patch gate from the installer's
  running-application mutex and tests installation while that gate is held.

## Release1.70

- Applied the selected Runic Strip loot overlay: six resource icons, full
  amounts, hourly rates and a session timer in an outlined fantasy style.
  Per-pixel transparency leaves the game visible between text and icons.
- Navigation > Loot size % adjusts the strip from 50% to 200% in 5% steps,
  scaling text, icons and spacing together. Size and dragged position are
  saved; enlargement keeps the overlay inside the current screen.
- Upgrade selects Runic Strip once. Previous designs remain available and
  subsequent design choices are preserved. Tracking, rates, reset controls
  and session logs continue using the existing session data.
- Offline build/UI checks cover transparent pixels, scaled rendering,
  saved settings, native composition and switching between overlay designs.
  No live game input was sent.

## Release1.69

- Restored detection for the September 30 client layout. Verify a complete,
  separate signature catalog before selecting updated scene, hotbar, cooldown,
  lock and ground-loot fields. Original client layout support is retained.
- Updated optional effect and party-name proofs for this client. Root agreement,
  loaded-code, process/window identity and live data validation remain required.
- Administrator launch remains the default in PoteHunter.exe. Packaging now
  verifies the executable's actual elevation manifest. Access-denied errors
  explain that PoteHunter and an elevated client need matching permissions.
- Local build, offline catalog/invalid-layout and UI checks passed. Read-only
  elevated connection tests passed against the running client with readable
  character HP/MP; no game input was sent. Combat/revival were not exercised.

## Release1.68

- Applied the selected Field Console adjustment: 6 px corners on settings
  cards, navigation/action buttons, connection status and the status strip.
  A shared radius keeps these surfaces consistent when resized.
- Retained the Field Console layout and settings. Offline build, UI rendering
  and existing navigation/settings checks passed without live game input.

## Release1.67

- Applied the selected Field Console interface: deep forest surfaces, brass
  accents, compact square controls, and horizontal navigation.
- Setup now places combat controls beside recovery and reserve settings.
  Navigation has its own top-level button; Monitor and Advanced retain page
  selectors. Existing settings, recovery setup and running-state locks remain.
- Added compact HP/MP bars to the status strip using the existing health and
  mana readings. Unknown or disconnected values display as unknown.
- Updated the hotkey index and verified all navigation destinations, compact
  modes, saved settings, minimum-size layout and offline regression checks.

## Release1.66

- Setup > Updates now reads the public theblusmurf/PoteHunter-Releases feed.
  Checking, downloading and installing updates no longer require a GitHub token
  or account. Token entry/storage code has been removed; previously saved Windows
  credentials are left unused. Development source remains in the private repository.
- Startup checks now work without credentials. Official-release selection,
  installer byte-size/SHA256 verification and stopped-state installation remain.
  Public feed outages/rate limits report a retry message without affecting hunting.
- Added anonymous-client/feed checks and public distribution instructions.
  Release files and patch notes are mirrored separately from the private source.

## Release1.65

- Added a Windows x64 installer with Start menu/optional desktop shortcuts and
  an uninstaller. Installation/upgrades preserve existing settings; user-created
  routes, repair/revival profiles and logs remain. Portable ZIP is still offered.
- Added Setup > Updates for the private theblusmurf/PoteHunter GitHub releases.
  Save a repository-scoped Contents read token once in Windows Credential Manager.
  Optional startup checks report newer official releases; download/install is
  user-initiated while hunting and setup/tests are stopped. Installer byte size
  and GitHub SHA256 digest are verified before launch; settings/tokens never ship
  in release packages. No unattended restarts or live input during checks.
- Added numeric-version/stable-release/digest selection tests and Windows CI
  install, repeat-upgrade and uninstall tests with existing user-data sentinels.
  Live authenticated updater access still needs the user's locally configured token.
- Normal PoteHunter.exe launches now enable verified native input and read,
  matching Start-Fixed-Detection.cmd and Start-PoteHunter.ps1. No launch flags
  are needed when opening the executable directly. Client connection/signature,
  Windows export, foreground window and identity checks still gate input.
- Explicit read-only launches and bounded diagnostic modes retain their existing
  opt-in rules. Added startup-policy regressions for normal launches, both
  launcher flag orders, read-only, incomplete/unknown flags and diagnostics.
  Updated packaged startup instructions. Local build, offline/input checks and
  UI checks passed without live game input; GitHub packaged checks also apply.

## Release1.64

- Fixed repair setup being delayed behind full-screen automatic recognition.
  A valid saved custom repair profile now reads its small inventory/hammer and
  prompt/button patches directly and uses its saved coordinates. Missing
  controls remain unrecognised; automatic templates are retained when no
  compatible custom profile is loaded. Marker rechecks still precede clicks.
- Live Release1.63 logs showed the earlier inventory I press sent at 19:58:59Z,
  about 35 seconds after revival, then an inventory recognition timeout.
  Later tests after saving a profile were cancelled before another I press.
  The previous reader scanned the full screen before checking that profile.
  Windows accepting the key does not establish whether the game opened inventory.
- Added requested/sent inventory-key logs and profile-priority regressions for
  correct, absent, cancelled and automatic controls. Existing single-attempt
  repair actions, HP/focus guards and precise button checks remain. Local build,
  offline repair/recovery/input and UI checks passed without live repair input.
  Live repair and durability verification remain pending; preserve repair-profile.json.

## Release1.63

- Fixed revival opening clicks blocked by a stable cursor 50 pixels above the
  requested game centre, confirmed in the running Release1.61 death logs.
  Exact positioning is still attempted first. Opening the death dialog can
  use a stable point within 5% of each client dimension, capped at 64 pixels
  from centre and always inside the game client. Distant or drifting pointers
  remain blocked. This tolerance applies only to opening the death dialog.
- Recognised Revive-button confirmation retains precise pointer alignment,
  paired visual recognition, known zero HP, window/character/focus checks,
  one confirmation, and living-HP verification. The two-second wait, at most
  three openings one second apart, repair and saved-route return are retained.
- Added bounded-centre alignment logs and regressions for the observed offset,
  exact centre, drift, far/outside pointers, shifted desktop and small windows.
  Local build, offline recovery/input checks and UI checks passed without
  live game input. Full live revival/repair/return still needs verification.

## Release1.62

- Fixed a missed-loot attribution path: enemies killed as part of an engaged
  group were removed from the encounter without notifying the loot tracker.
  Confirmed zero HP now reports every enemy with our attack/damage evidence,
  including collateral kills, before removing it. Unknown, unrelated,
  defensive-only, ambiguous, or replaced identities are not credited as kills.
- Deduplicate kill events by zone/id/generation across the loot session, so the
  encounter and selected-target paths cannot count the same death twice.
  Drops seen just before a collateral death can now be matched from the buffer.
- Added per-pile gold credit logs with identity, amount, source and running
  total to support live reconciliation. Gold remains detected-drop estimates;
  the reader does not yet provide confirmed wallet or pickup-message data.
- User reported 6,154 gained versus 5,743 tracked (411 short). That exact live
  difference cannot be reconstructed from the one-second observation log.
  Offline checks reproduce a 411-gold buffered collateral scenario, duplicate
  callbacks and identity/unknown-health exclusions. Live accuracy remains unverified.

## Release1.61

- Added an independent Show route overlay toggle in Advanced > Navigation.
  The passive game overlay auto-fits all saved routes in the current zone,
  colours Primary and both alternatives, marks anchors/facing, and shows the
  10-unit startup corridor. The existing radar route toggle stays separate.
- Added three saved loot-overlay designs: Dungeon HUD (detailed dark panel),
  Compact Ribbon (low-height totals/rates), and Parchment Ledger (warm journal).
  All use the same six tracked item totals, gold amounts, session timers and
  earning rates. Detailed layouts show source kills/drops and recent loot.
- Loot headers remain draggable without activating the game; changing designs
  keeps the overlay on screen. Route visibility changes display only, not travel.
- Offline build/UI checks validate saved choices, independent route visibility,
  passive route-window styles, and render all three designs with sample data.
  Live game overlay placement remains unverified; running app untouched.

## Release1.60

- Fixed revival opening clicks repeatedly being withheld when the game's cursor
  no longer matched the injected centre move. Live Release1.59 logs showed
  expected (1720, 720) versus actual (1720, 670); most attempts sent no click.
- Before an opening or recognised confirmation click, correct a displaced
  cursor once using Windows screen coordinates and require a verified match.
  Clipped/rejected positioning, lost focus, changed window, or unreadable/living
  HP still prevent the click. Alignment outcomes are logged for diagnosis.
- Retained the two-second death wait, up to three centre openings one second
  apart, one recognised confirmation, and 200 ms health polling for 15 seconds.
- Offline checks cover the observed 50-pixel displacement, correction failure,
  already aligned cursors, and cancellation after correction. No live input
  was used for validation; actual revival still needs in-game verification.

## Release1.59

- Visual revival now requires confirmed dead HP and waits at least two seconds
  from the observed death before checking for the centred Revive dialog.
- If no recognised popup is visible, click the game-window centre up to three
  times, one second apart, checking recognition and health between clicks.
  An already visible popup skips opening; recognised popups stop opening clicks.
- Click the recognised Revive button once, then check health every 200 ms for
  up to 15 seconds. Failed recognition or unconfirmed revival stops recovery
  without further clicks. Stop/focus/identity guards remain in place.
- Custom profiles still recognise their paired dialog/button, but opening now
  always uses the centre. Setup captures the dialog only; older opening points
  are retained in user files but ignored. Longer saved death delays still apply.
- Offline checks cover timing, centre geometry, early recognition, unknown HP,
  disappearing dialogs, one confirmation, 200 ms polling, and the 15-second
  deadline. Live revival remains unverified; running applications untouched.

## Release1.58

- Start / F8 now joins a compatible recorded route when the character is within
  10 map units of its path, follows it to the saved farming anchor, restores
  facing, and continues hunting. The selected slot is preferred; otherwise the
  nearest compatible route is used. Group/healer behavior stays separate.
- Route proximity and joining use actual path segments, including between
  recorded waypoints. Existing occupied-spot fallback and death recovery apply
  during travel. Outside the corridor, ordinary activation-point hunting remains.
- Added an Index page listing every registered app hotkey, stop guards,
  route recording, startup travel, and automatic revival/return instructions.
- Validated startup corridor boundaries, slot preference, map/character/floor
  compatibility, segment joins, existing recovery, and UI with offline checks.
  Live game travel remains unverified.

## Release1.57

- Kept confirmed **0 HP** as the death trigger and fixed the resting path so
  dying transfers control to auto-revive instead of ending the hunt. Unknown
  health remains distinct from a confirmed death.
- Fixed visual revival abandoning the recovery when the cursor or dialog
  changes before a click. It now rechecks and repositions within the existing
  timeout, preserving the pending return. Only actual opening clicks count
  toward the three-click limit; an unsent confirmation is rechecked and a sent
  confirmation is never repeated.
- Automatic opening clicks now recheck whether Revive appeared while moving
  the pointer. Three seconds after death and the 250 ms minimum click spacing
  remain unchanged; the status countdown now shows that full wait. Focus loss
  and explicit stop keys still stop input.
- Transient hotbar changes during revival, repair, and return no longer let
  the combat UI cancel recovery. Living HP and the current hotbar are read again
  before the character returns to the saved anchor.
- Stop messages and logs now retain the actual cancellation reason. Deferred
  clicks record the expected and observed pointer positions for diagnosis.
- Fallback now explains when an occupied spot has no compatible alternative
  routes saved. Record Alternative 1 and Alternative 2 from the same route
  origin to use those destinations; no route is invented automatically.
- Added offline regressions for zero HP, resting interruptions, refused clicks,
  bounded retries, single confirmation, and revival through alternative-route
  arrival. Live gameplay verification remains necessary.

## Release1.56

- Visual revival now waits at least **3 seconds after death**, then sends
  **three opening left-clicks** with a minimum 250 ms gap when needed. Both
  automatic recognition and the saved custom opening location use this timing.
- The opening sequence ends early when the Revive button appears or the
  character is alive. Unknown HP blocks clicks; stopping or losing game focus
  interrupts the sequence. Revive confirmation is still clicked only once.
- An already visible Revive button now also respects the three-second wait.
  A longer saved route delay takes precedence without adding another three
  seconds. Test revival measures its wait from the pre-countdown dead-HP check.
- Setup shows the click timing beside the revival controls. Existing custom
  profiles, settings, routes, and optional repair continue to work.
- Offline checks cover three-click death screens with both detectors, timing,
  early dialog recognition, interrupted sequences, and no repeated confirmation.
  Live revival timing still needs verification in the game.

## Release1.55

- Added **Custom revival setup** next to visual revival in Setup → Death
  recovery. While dead with hunting stopped, capture the dialog, select its
  static text, and select the Revive button. An optional first capture records
  the death-screen text and left-click location that opens the dialog.
- Reused the repair image editor for revival, including zoom, marker selection,
  and button selection. Capturing setup sends no game clicks and does not kill
  or revive the character. Cancel leaves the previously saved profile intact.
- Added **Test revival · 5s**. Switch to the game during the countdown; the test
  performs one real revival and requires living HP before reporting success.
  Hunting remains stopped. Normal automatic recovery still handles optional
  repair and the saved-route return after revival.
- Custom recognition uses the saved dialog text and button, checks the screen
  again immediately before clicking, and stops on a client/window-size mismatch
  or unrecognized controls. Confirmation remains limited to one click. A
  dialog-only setup does not invent an opening click.
- Added **Use automatic** to restore the included detector while preserving a
  timestamped backup of the custom profile. Turning off Recognize Revive button
  still selects the existing revival-key method.
- Preserve the new local `revival-profile.json` on upgrade, along with existing
  settings, routes, and repair profile. Custom screenshots/patches and profile
  backups are excluded from source control and release packages.
- Added synthetic recognition, profile persistence, recovery-sequence, and UI
  checks. Local build and offline checks passed without game input; the custom
  procedure still needs its in-game test on the user's client.

## Release1.54

- Added automatic repair recognition using the inventory hammer and matching
  inventory/repair-dialog text from the supplied Domitus source. It recognizes
  moved windows and supported UI scales. **Custom repair setup** remains an
  optional fallback; **Test repair · 5s** runs one real repair. Auto repair is
  still off by default and does not independently verify equipment durability.
- Added **Recognize Revive button** under Setup → Death recovery, enabled by
  default. It requires known dead HP, recognizes the on-screen button, clicks
  confirmation once, and waits for living HP. It permits at most three spaced
  popup-opening clicks. Turn it off to retain the configured revival key.
- Fixed occupied-spot fallback to reverse the current recorded path to the
  shared revival point and then follow the destination route. Primary and both
  alternatives participate. Record all routes from the same origin (within
  3 map units); incompatible or spot-only routes are excluded.
- When every compatible spot is occupied, return to that shared origin, wait
  ten active minutes, and retry the route cycle. Death during travel/waiting
  preserves pending recovery. Occupancy uses loaded recognized players only.
- Return follows actual waypoints, keeps movement held between consecutive
  waypoints when steering allows, reaches the activation anchor, and restores
  facing before resuming farming. Blocked routes stop with a reason. An engaged
  enemy that leaves the allowed area now triggers anchor return and waiting.
- Recognition runs off the UI thread. Rechecks before clicks, cancellation,
  focus/death guards, and bounded waits prevent stale or repeated confirmation.
  Live status identifies revival, repair, return, and occupied-spot waiting.
- Removed the superseded alternative-selection helper. Existing combat skill
  gates, stationary target rules, and swing ranges remain in place.
- Preserve `settings.json`, `navigation-routes.json`, and any custom
  `repair-profile.json` when upgrading. Offline template, recovery, input, and
  UI checks passed; live repair/revival and route travel still need verification
  in the user's client.

## Release1.53

- Added **Auto repair after revival** in Setup → Death recovery, off by default.
  It performs the inventory-hammer repair once after confirmed revival, closes
  inventory, and then starts the saved-route return. The current toggle applies
  to all routes, including routes saved before repair was available.
- Added one-time **Configure repair** and **Test repair · 5s** controls. Setup
  uses captured images to identify inventory/dialog text and the hammer/Yes
  buttons. The test performs one repair after giving time to switch to the game.
  Keep the same game build, window size, and inventory layout after setup.
- Repair checks the game window, character, health, and recognized controls
  before input. Missing/stuck dialogs stop the hunt before return. Confirmation
  is never automatically retried. F9, focus loss, cancellation, and a second
  death interrupt repair. The game must support repair and its required cost.
- Combined the revival and saved-route switches into **Auto revive + return
  along saved route**. On requires a recorded compatible route; off stops on
  death. Any of the three saved destinations can be the activation spot.
  Occupied-spot fallback excludes alternatives that contain only a saved spot.
- Preserve `settings.json`, `navigation-routes.json`, and the new local
  `repair-profile.json` when upgrading. No repair profile or game images ship
  in the release. Detailed setup instructions are in README.
- Removed the old example character name and skill-key list from release
  defaults; new profiles use live character/skill detection. Existing personal
  settings in the user's application folder are not modified.
- Validation: Release build, simulated repair recognition/interruption checks,
  recovery-route checks, input checks with intercepted packets, settings
  persistence, and offscreen UI rendering. No live game input was used.
  Repair completion means the UI sequence closed; durability and actual
  in-game repair/return remain to be verified after the user's one-time setup.

## Release1.52

- Fixed the UI stopping the hunt on death even when automatic revival was
  enabled. Death is logged once, and recovery stays pending through manual
  revival, temporary unreadable health, and a second death during the return.
- Fixed recorded routes being overwritten by the current hunting trail on
  death. Only explicit route/spot saves change the Primary and Alternative slots.
- Return travel now waits for each waypoint and verifies arrival within 0.5
  map units before restoring facing and resuming farming. Combat and pickup
  wait until the return finishes; blocked routes report a reason and stop.
- Occupied-spot fallback now runs after revival and while approaching the
  destination. It chooses a free compatible Alternative 1 or 2, updates the
  active anchor and facing, and stops if no free compatible route is available.
- Fixed the bot's own Enter revival confirmation being mistaken for the
  user's chat/stop key. Manual Enter, F9, focus loss, and cancellation still stop.
- Restored **Death recovery** in compact Setup: Auto revive, delay, revive key,
  and Resume farming on arrival. Navigation still holds the saved-route and
  alternative-route toggles. Saved route profiles retain their arrival policy.
- Validation: .NET 10 Release build, offline recovery/input regression tests,
  settings persistence, and rendered UI checks. No live game input was used;
  in-game revival and route completion still need verification.

## Release1.51

- Added **Home** to start route recording and **End** to finish and save the
  route, destination, and facing to the selected Primary or Alternative slot.
- Added shortcut labels to Navigation's route buttons and recording status.
  Stop hunting before recording; the game or PoteHunter must be in the foreground.
- Pressing Home again preserves the active recording. End cannot overwrite a
  route while disconnected, hunting, or without an active recording.
- Route shortcuts use no-repeat registration, release their keys on close, and
  cannot be assigned as the automatic revive key. A route-shortcut registration
  conflict is reported without disabling the existing hunt hotkeys.
- Validation: .NET 10 Release build, offline self-tests, and UI checks passed,
  including shortcut dispatch and preservation of unfinished recordings. No live
  game input was used during validation.

## Release1.50

- Fixed the Release1.48 gold regression that recorded **one gold per pile**.
  Gold now uses the amount encoded in the ground item's signed type value;
  the uninitialized field at `+0x0c` is no longer read as a quantity.
- Corrected source summaries to report gold amounts, matching session totals,
  gold per hour, recent drops, and death/reset CSV logs. Drop counts still count
  the number of piles/items.
- Unavailable amounts are labeled explicitly and do not add invented gold.
- Added regression checks using recorded client values: five piles worth
  97, 119, 121, 116, and 82 contribute **535 gold**, once. Coverage includes
  stale field values, repeated observations, large totals, rates, and CSV output.
- These remain detected ground-drop totals. They do not confirm wallet credit
  or recover totals already recorded by an older running application.
- Validation: .NET 10 Release build passed with no warnings or errors; all
  offline self-tests passed. Live wallet reconciliation has not been performed.

## Release1.49

- Added a persisted **Show routes on radar** option to the Navigation controls.
  It hides or reveals saved route geometry, anchor markers, and the active
  route while leaving targets, obstacles, movement trail, and treasure markers
  available.
- Exposed route visibility in `live-status.json` as `Radar.RoutesVisible` and
  updated the Navigation status text when routes are hidden.
- Removed the stale compact-shell reparenting of legacy healing and skill
  targeting rows. Their dedicated modern rows remain the single visible source
  for those settings.
- Validation: .NET 10 Release build and the offline PoteHunter self-test pass.

## Release1.48

- The quantity-field change below was incorrect and is superseded by Release1.50.
- Corrected gold pile accounting to use the ground record's quantity field.
  Negative item IDs are now used only to classify a currency pile; their lower
  bits are no longer reported as a payout amount.
- Removed the generated amount from the `Special drop` fallback label so an
  internal type code cannot be mistaken for gold.
- Retained recently observed piles until the kill event arrives, allowing fast
  auto-pickup to be attributed even when the pile disappears between reads.
- Increased tracker sampling to 40 ms in the pickup and active-hunt loops.
- Validation: .NET 10 build and the offline PoteHunter self-test pass. Live
  gameplay verification is still recommended for the current client build.

## Release1.47

- Increased the bounded turn correction for large and medium heading errors so
  side targets are reached sooner without allowing an uncontrolled snap.
- Replaced the fixed 25 ms turn/camera feedback wait with an adaptive 12/16/20
  ms cadence. Large corrections receive faster feedback, while the smaller
  final corrections retain enough settling time to avoid visible jitter.
- Applied the same adaptive feedback to 3D target yaw and vertical aim so body
  and camera movement stay synchronized.
- Validation: .NET 10 Release build and the offline PoteHunter self-test pass.

## Release1.46

- Sampled ground loot from the 100 ms pickup loop and the active hunt loop,
  rather than relying only on the 200 ms UI refresh. Short-lived piles are now
  visible to the tracker before auto-pickup removes them.
- KeyA/KeyB now identify a ground pile across type and amount refreshes, which
  prevents one pile from being counted twice when its client record changes.
- Restricted the negative-ID currency fallback to actual special-drop records,
  normalized source and recent-drop labels to Silvin, Mithril, Iternium, Fehu,
  Gold, and Gems, and show the detected gold amount in recent-drop entries.
- Added regression coverage for refreshed pile records and normalized tracker
  output.

## Release1.45

- Fixed fixed-target damage recovery after a stop/restart. Mimic, Tribal,
  Pulkhan, and Tower profiles now hold the saved anchor when no target is
  locked yet, and return directly to it if an earlier damage response moved the
  character.
- Prevented the generic 2.5-unit defense step from being recreated every loop,
  which was repeatedly pressing **W** and producing the observed run-away/circle
  until another target spawned.
- Added offline coverage for stationary target-filter classification and the
  no-lock anchor guard.

## Release1.44

- Preserved a confirmed engaged swing across transient target-node and target-HP
  reads instead of releasing the basic attack on the first stale snapshot.
- Reused the last engaged target when selection briefly returned no target, so a
  refresh cannot silently drop the active combat lock.
- Stationary swing recovery now sends a real 35 ms key-up interval before
  re-arming, preventing the client from coalescing an immediate up/down pair.
- Added `Combat.BasicAttackHeld` to live status and recording snapshots so a
  future stop can be separated into bot release, target loss, or client input
  loss.

## Release1.43

- Added explicit Navigation overlay controls to start a route recording and
  finish/save it at the destination. Unsafe recordings remain visible as
  cancelled and must be restarted instead of silently becoming anchor-only
  routes.

## Release1.42

- Saved route profiles now honor an explicit zero-second revival delay instead
  of falling back to the global delay, while legacy routes without profile
  metadata continue to use the current hunt settings.

## Release1.41

- Fixed the route-selection startup ordering so alternative-route navigation
  builds cleanly and does not capture an uninitialized return heading.
- Stationary saved anchors now survive reloads, so a route can be saved before
  walking and still be used for occupancy fallback and direct travel.
- Saved routes now carry the character, floor/height, farming radius, revival
  delay, and farm-on-arrival policy. Alternative routes are ignored when their
  character or floor does not match the active client.
- Route recording cancels itself after an unsafe movement gap larger than 8
  map units instead of persisting a discontinuous path. Death recovery honors
  the configured delay before sending the revive key and preserves the active
  route profile when refreshing the saved path.

## Release1.40

- Navigation now stores one **Primary hunt route** plus **Alternative route 1**
  and **Alternative route 2**. Each slot keeps its own location, facing, and
  recorded path in the local ignored `navigation-routes.json` file.
- When the primary saved location is occupied by another recognized player at
  activation, the hunter selects the first free alternative and routes there
  before target selection. Existing single-route `navigation-route.json` files
  migrate into the primary slot automatically.
- The Navigation page adds route-slot save/clear controls, an occupancy-fallback
  toggle, route status, and map colors for the three saved routes. Death return
  uses the active slot's route and facing.
- Validation: offline navigation slot migration, alternate-route reversal, and
  bounded route selection checks. The Windows package is validated by GitHub
  Actions.

## Release1.39

- Added optional automatic death recovery. When the local character reaches
  stable zero HP, combat and pickup input are released, the configured death
  screen key is sent (default `R`, with an `Enter` fallback), and the hunt
  resumes only after live HP is readable again.
- The activation position and facing remain the recovery anchor. After revival,
  the character returns to that point and restores its saved direction before
  target selection resumes.
- Navigation now records the observed path for the active hunt. The Navigation
  tab can save or clear an anchor route, and the saved route is reused during
  death recovery when the respawn position is near the recorded path. The route
  is stored in the ignored local `navigation-route.json` runtime file.
- Added settings for **Auto revive after death**, **Death recovery key**, and
  **Use saved death route**. Disable either option when the client uses a manual
  death flow or when direct anchor recovery is preferred.
- Loot collection now yields immediately when death recovery is requested so
  pickup movement cannot compete with the revive and return sequence.
- Validation: offline navigation recording/reversal checks and bounded source
  verification. The Windows build is validated by GitHub Actions.

## Release1.38

- Added a single bounded melee-assist step for engaged Mimic, Tribal, Pulkhan,
  and Tower targets that are just outside the swing window. The step is capped
  at 1.5 map units and never chains into a chase.
- After an assisted target dies, the character returns to the original saved
  activation point and restores its saved facing before selecting the next
  target.
- Added offline checks for the 1.5-unit assist cap and stationary return flow.

## Release1.37

- Prevented incoming-damage recovery from sidestepping fixed Mimic, Tribal,
  Pulkhan, and Tower assignments while the character is at the saved activation
  point.
- If a damage response had already moved a fixed-target fight away from the
  anchor, the recovery path now routes back to the saved point before resuming
  target selection and swinging.
- Added offline checks for stationary-anchor damage handling and bounded return
  behavior.

## Release1.36

- Kept the basic swing explicitly held across skill selection, activation,
  retargeting, cooldown checks, and declined health/mana rechecks so skill
  transitions do not leave a stale attack state.
- Smart skill targeting now considers only live engaged targets inside the
  configured nearby-enemy radius and selects the highest-current-health target
  for single-target skills. Area and line skills retain their dense-pack
  anchor behavior while preferring the highest-health anchor on ties.
- Added offline coverage for highest-health selection and out-of-range target
  exclusion.

## Release1.35

- Corrected the melee distance baseline to the client’s verified 1.5-unit
  melee gate. The UI and settings loader now prevent invalid sub-gate values,
  and new profiles use 1.5 units.
- Existing Release1.34 profiles with a 1-unit melee setting are normalized to
  1.5 units when loaded or saved.

## Release1.34

- Corrected loot attribution so a new pile is retried when it is seen before
  the matching kill record, instead of being permanently discarded.
- Loot identities now include zone, both client key fields, and item type;
  counted piles remain suppressed if they temporarily disappear and return.
- Drops outside the saved farming radius are ignored, and the Gold parser now
  prefers the displayed pile amount before using the encoded id fallback.
- GPH now uses active farming time, excluding idle, stopped, and disconnected
  periods.
- Validation: release build and offline tracker self-tests for attribution,
  identity, radius, and active-time behavior.

## Release1.33

- Preserved the event-log write failure message when a reset is pressed, so a
  locked or unavailable CSV is reported instead of being hidden by the reset
  confirmation.
- Validation: release build and offline self-test.

## Release1.32

- Replaced periodic loot snapshots with event snapshots for the tracker log.
- The app now appends an Excel-compatible `loot-session-log.csv` row before
  stopping on character death and before either loot reset control clears or
  restarts its counters.
- Each row preserves the session totals, rate-window values, and Mimic,
  Tribal, Pulkhan, and Tower kill/drop counts so the record can be reviewed
  after a run without interrupting the live overlay.
- Validation: release build and offline self-test, including append and reset
  event coverage.

## Release1.31

- Added an application-session timer to the loot tracker and displayed elapsed
  time plus per-hour rates for Silvin, Mithril, Iternium, Fehu, Gold, and Gems.
- Added a **Reset timer** control that starts a fresh earning-rate window while
  preserving the application-session totals; **Reset loot** still clears the
  totals as before.
- Validation: Release build, offline self-test, and Windows package workflow.

## Release1.30

- Self-healing skills now bypass the shared five-second offensive skill timer,
  so a configured health condition can be answered immediately when the
  character is low on health.
- Offensive combat skills still require the five-target pack and highest-health
  below 80 percent gate, and successful offensive casts remain five seconds
  apart.
- Validation: Release build, offline self-test, and Windows package workflow.

## Release1.29

- Kept the basic attack armed through every offensive skill exit, including a
  health/mana re-check that declines a cast and the short fallback release used
  by clients that reject a skill while swinging.
- Treated missing skill-use metadata as a safe short activation and skipped
  unsupported passive/item/set actions instead of letting one slot exception
  stop the combat loop.
- Limited standard combat skill casts to an engaged pack of at least five live
  targets inside the configured nearby-enemy radius, with the highest target
  health below 80 percent.
- Added a shared five-second delay between successful combat skill casts so a
  skill transition cannot starve the continuous swing or be bypassed by a
  target switch. Ranged Firing tags, healing, and area-buff support keep their
  existing independent timing.
- Added offline checks for the five-target/80-percent gate and attack re-arm
  behavior.

## Release1.28

- Changed the Gold total from a drop count to the actual currency amount.
  The client's negative-id `Special drop (N)` records now contribute `N` gold
  to the session total, with numeric gold labels handled as a fallback.
- Added thousands separators and an explicit overlay label so Gold is clearly
  shown as an amount while the other tracked valuables remain item counts.
- Validation: Release build, offline self-test, and Windows package workflow.

## Release1.27

- Fixed the loot tracker to count the client's negative-id `Special drop (...)`
  currency records as Gold, along with gold coin, currency, and money labels.
- Fixed gem tracking for the names emitted by the live client, including Emerald,
  BlackMoon, and other common gem names.

## Release1.26

- Made the Silvin, Mithril, Iternium, Fehu, Gold, and Gems totals
  application-session scoped. They now survive map changes, reconnects, and
  hunt restarts while the app remains open.
- The overlay's **Reset loot** control clears the current session totals;
  launching a new application session starts a fresh record.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.25

- Corrected the loot tracker labels from **Silvein** to **Silvin** and from
  **Mitheil** to **Mithril**.
- Both corrected spellings are used in the overlay and live-status totals;
  the previous spellings remain accepted as compatibility aliases when the
  client reports them.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.24

- Added dedicated loot totals for Silvin, Mithril, Iternium, Fehu, gold, and
  gems. Matching is case-insensitive and also checks the client's item
  description when the displayed name has a quantity or suffix.
- Added the six named totals to the live-status tracker and the draggable loot
  overlay, while retaining per-target kill/drop and recent-drop records.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.23

- Restored combat skill rotation for stationary Mimic, Tribal, Pulkhan, and
  Tower engagements; skills now run between attack checks with the same target,
  cooldown, health, and mana-reserve rules as moving combat.
- Kept the basic attack held for the full stationary engagement and added a
  bounded no-damage re-arm watchdog so swings continue without periodic input
  gaps. Skill casts can briefly release and then restore the swing when the
  client does not accept a cast while the attack button is held.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.22

- Added a live loot tracker for Mimic, Tribal, Pulkhan, and Tower kills. New
  ground items are attributed to the nearest recent tracked kill and grouped
  by source and item name.
- Added a compact draggable overlay with kill/drop totals and recent drops;
  its position and visibility are saved in the normal settings file.
- Added a reset control and live-status JSON output for the tracker.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.21

- Smoothed the Gamekeeper return route by keeping its movement state alive
  across controller passes instead of repeatedly releasing and pressing W.
- The saved facing is restored only after the character reaches the saved
  point's collision envelope, preventing mid-route turning from stalling the
  return and allowing the retained engaged encounter to resume cleanly.
- Added bounded return progress logging and retry handling for blocked routes
  or an unresponsive facing correction.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.20

- Confirmed Gamekeeper defeat now gates the saved-point return, so a transient
  entity refresh cannot clear the return state prematurely.
- The return transition runs before healing, nearby-loot input, or target
  selection and suppresses the engaged-target preflight interrupt while the
  route is active.
- After returning and restoring the saved facing, retained engaged targets are
  selected again from the encounter queue.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.19

- Gamekeeper/Game Master priority now completes its saved-point return before
  selecting surviving engaged targets.
- The activation location and saved facing are restored after the priority kill,
  even when other engaged enemies are waiting; normal target selection resumes
  from that point.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.18

- Captured and retained the character position at hunt activation as the
  stationary combat point.
- Prevented Mimic, Pulkhan, Tribal, and Tower assignments from falling through
  to the normal target-chase route. They now remain at the activation point,
  face the target, and attack when it enters the swing window.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.17

- Fixed Mimic, Pulkhan, Tribal, and Tower combat repeatedly navigating back to
  the saved point while the target was already in attack range.
- Stationary-target combat now stops all approach input immediately and waits
  or swings from the current standing point; it never sends a return route or
  chases the target. Facing and attack pulses continue as the target enters
  range.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.16

- Removed the **Smooth side-step** and **Group engaged targets** settings from
  the UI, saved options, live status, and combat controller.
- Removed their lateral correction and tight-gathering code paths so normal
  combat selects and faces the locked target directly without sporadic turns.
- Preserved the navigation attack-cone overlay as a visual guide; it no longer
  drives combat movement.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.15

- Removed the remaining chase path for Mimic, Pulkhan, Tribal, and Tower
  assignments. Their verified target IDs now keep the character at the saved
  hunt point even when the client briefly refreshes or omits the model name.
- Those targets may only trigger a return to the saved point, facing, and
  stationary swing pulses; group mode and Gamekeeper priority remain the
  explicit movement exceptions.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.14

- Fixed stationary combat sending one persistent left-button hold that some
  client builds treated as a single attack. Stationary targets now receive
  repeated bounded attack pulses with a release between swings.
- Target, protection, facing, and saved-position checks remain active on every
  pulse; the character stays at the saved hunt point.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.13

- Increased the stationary target swing window to cover the live client's
  center-to-collision-envelope distance. Release1.12 was still waiting at
  2.2/2.0 units for a Mimic, so it never reached the attack hold.
- The character remains at the saved hunt point and faces the target while the
  attack hold starts inside the expanded window.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.12

- Fixed stationary Mimic, Pulkhan, Tribal, and Tower targets being held in a
  wait state when their center was near but just outside the configured melee
  stop. The swing gate now includes the verified melee attack distance and a
  small target collision allowance, while navigation remains stationary.
- The live status now reports the effective melee swing distance in the wait
  message, making range decisions visible during a hunt.
- Validation: Release build, offline self-test, and ranged UI check passed.

## Release1.11

- Fixed melee attacks being starved when a nearby target moved slightly between
  live reads. The controller now keeps the basic attack held through a short
  melee range and heading correction window, including stationary hunt targets.
- A transient turn response no longer blocks the first swing after the target
  enters melee reach; the next loop continues facing while the attack remains
  active.
- Validation: Release build, offline self-test, and ranged UI check passed.

Windows releases include a **Patch notes** section generated from the commits since the previous release tag. Each release also lists the packaged ZIP and SHA256 sidecar, followed by the current feature and validation notes.

Keep commit subjects specific and user-facing so the generated notes explain what changed without requiring a separate manual edit for every build.
# Release1.95 — Loot overlay reset buttons

- Add Reset loot and Reset timer directly to all four loot tracker overlays. Buttons scale with Runic Fold/Strip and remain clickable with a fully transparent background; header dragging and non-activating game focus are retained.
- Share the existing Navigation reset actions: save the current log before resetting. Reset loot starts fresh session totals and wallet baseline; Reset timer restarts the earning-rate window while retaining totals and wallet baseline.
- Validate scaled hit areas, one action per click, cancelled releases, opaque layered button pixels, reset semantics and rendered layouts. No live game input or installed settings changes during validation.


# Release1.96 — PlayPoteBot Solar Forge interface

- Rename display branding, installer and shortcuts to PlayPoteBot; add the user-selected Adventurer’s Compass emblem and app icon. Preserve the existing installation, executable and public update feed identities.
- Apply the approved Solar Forge layout with Crownfire charcoal/brass styling, 13-pixel corners and bottom Overview, Hunt, Routes, Recovery and Settings navigation. All existing detailed pages remain reachable.
- Show the actual selected target’s saved routes in Overview, move hunt/recovery controls alongside the map, collapse skill and saved-route details, and include session loot/timer reset actions. No change to hunting, revival, repair, wallet or route execution rules.
- Validate native pages, settings bindings, running-state locks, minimum-size layout, existing offline checks and installer preservation. No live game input during validation.


# Release1.97 - Smoother saved-route entry

- Merge onto a clear forward route segment instead of turning sideways to touch a nearby projected entry point. Use short, bounded waypoint lookahead on straight portions; keep progression local and monotonic.
- Preserve sharp corners, blocked-segment checks, route compatibility and fallback order. Brake and settle on the final anchor with the existing precise-arrival controller before restoring saved facing.
- Add offline regressions for lateral entry, passed points, sharp bends, obstacles, loop boundaries and exact final arrival. No live gameplay input or installed setting changes during validation.
- Fantasy website-inspired interface is provided as a preview only; the approved Solar Forge interface remains installed.

## Release1.98

- Apply the approved fantasy interface: storm-blue castle banner, antique gold frames, Adventurer's Compass branding, illustrated target tiles and bottom navigation. Keep bound controls, settings and gameplay guards.
- Render original Silvin, Mithril, Iternium, Fehu and Diamond/Gems PNGs directly in Overview and the matching Compass Ledger overlay. Retain wallet-baseline gold calculations, reset actions and other saved overlay designs.
- Recheck transient lost Revive recognition until the existing bounded deadline without further opening clicks or stale confirmations. Match unchanged foreground lettering through hover shading; preserve dead-HP, focus, identity, pointer, one-confirmation and living-HP checks.
- Locate a moved custom inventory only when saved title and hammer agree on a unique bounded translation. Keep repair confirmation recognition independent, log cancelled repair tests with the exact reason, and clarify recalibration requirements when layout/UI scale changes.
- Offline recovery, recognition and native UI checks pass. Live recovery still requires verification after updating; stale repair artwork needs one new Custom repair setup. User profiles and running installation remain unchanged.


## Release1.99

- Navigation radar, Overview route panel and saved-route overlay now use the installed game's real map artwork behind recorded routes, anchors and player position.
- Added Fit game map and Follow player display controls; Home/End recording and target-specific Primary/Alternative routes are retained.
- Read-only local DDS decoding supports DXT1, DXT3, DXT5 and BGRA maps. All four quadrants are required. Unknown client builds/zones and malformed assets use the existing route view; explicit calibrated maps take precedence.
- Client map projection was verified for the supported zones; eleven installed map sets passed independent decoding and native route-rendering checks. Game assets and runtime routes remain private and are not bundled or uploaded.
- Map artwork assists recording, but is not a verified collision mesh. Automatic terrain shortcuts are not inferred from the picture. Existing recovery, route movement and obstacle handling are preserved; live travel remains unverified.

## Release1.100

- Increase body-turn acceleration by one third and steering speed limits by 20% when stationary and about 29% while walking. Keep the fine-angle taper, calibrated mouse sensitivity and existing movement/arrival limits.
- Reserve unreported heading changes against the remaining turn angle, including partial feedback, so faster steering can stay responsive without stacking corrections past the goal. Retry lost commands only after 220 ms without a newer command or heading progress; the existing no-response stop remains active.
- Add offline walking/stationary, delayed/partial-feedback, direction, convergence, no-overshoot and faster 90-degree turn checks. Build and offline checks pass; live turning still requires verification after upgrading.

## Release1.101 — Hunting and loot session logs

- Start a private local CSV history automatically when PlayPoteBot opens. Assign each hunt its own ID and record start/stop, selected targets, character/zone, duration and readable-HP-confirmed deaths/revivals, including connected manual revival while hunting is stopped.
- Include cumulative loot-session totals for Silvin, Mithril, Iternium, Fehu, gold and gems, earnings rates, verified wallet current/baseline/net, separate ground-gold estimates, timer durations and source kills/drops. Record before/after loot and timer resets; preserve loot identity on timer reset and rotate it on loot reset.
- Record revival attempts, repair stages and anchor return without treating sent input as successful revival. Death cause remains Unknown because no verified killer field is available; existing recovery behavior is unchanged for player/mob deaths.
- Add Settings > Index > Open session logs. Keep logs out of Git/release packages, preserve them through installer upgrades/uninstall and report file errors without interrupting hunting.
- Validate deduplication, health/identity gaps, CSV escaping, session/reset lifecycle, wallet accuracy, recovery-stage filtering and file-error recovery offline. No live gameplay or installed-app replacement during development; live session verification remains needed after upgrading.


