# Release notes

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
