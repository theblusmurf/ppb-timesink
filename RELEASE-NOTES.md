# Release notes

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
