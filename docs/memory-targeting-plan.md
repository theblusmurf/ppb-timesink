# Memory Targeting Plan

This plan keeps the current 3D memory-position aiming path in service while
the client target-selection path is proven against the live build. No target
field is written in Stage 1.

## Stage 1: read-only target-path discovery

Goal: identify the current client's authoritative skill target value and the
moment it becomes authoritative.

1. Locate the live `CCharacterControl` instance from a unique code signature
   and validate its vtable and pointer stability in the running client.
2. Map candidate target fields and compiler-generated temporary target storage
   from the current binary. Historical source is used only as a guide; it does
   not supply offsets for the patched client.
3. Run a manual, read-only capture across at least two distinct monsters.
   Each capture records candidate field values, target identity, selected skill,
   cooldown transition, range, and monster HP before and after the user's
   ordinary Firing input.
4. Repeat the capture for overlap, target death, range loss, release, and zone
   change. Require the same current-build value to correlate with the monster
   actually selected by the client each time.

Stage 1 completes only when a target value is stable, current-build validated,
and correlated with the actual defender identity. It produces no game-memory
writes, injected calls, or fabricated network traffic.

## Stage 2: guarded target control

Goal: choose the least invasive mechanism that gives the client the intended
target without bypassing its normal validation.

1. Prefer a deterministic normal client selection path driven by the known
   3D world position and verified target readback.
2. Consider direct target-state control only if Stage 1 proves the complete
   state lifecycle, including the temporary selection state used by Firing.
3. Immediately before every selection, validate the monster's UID, generation,
   address, position, health, hunt-zone, range, and protection rules.
4. Immediately after selection, read back the client target state. If it does
   not match the intended identity, release input and return to normal target
   selection rather than firing blindly.
5. Confirm the result from selected-skill state, cooldown, and intended target
   health. Treat an overlap or stale identity as a failed selection, not a
   successful precise hit.
6. Keep a manual kill switch and retain the existing normal 3D cursor route as
   the fallback whenever target-state validation is unavailable.

Stage 2 is accepted only after isolated manual tests cover two nearby targets,
an overlapping pair, a moving target, a dead target, a zone change, and a
client reconnect.
