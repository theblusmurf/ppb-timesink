# Release notes

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
