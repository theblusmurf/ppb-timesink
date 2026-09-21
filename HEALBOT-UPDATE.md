# Combined healer and group mode

Built on the most recent minimal UI + potion update.

## Setup

1. Extract the ZIP into a new folder. Close the app and copy your existing
   settings.json and any maps/calibration files if you want to retain them.
2. Connect while logged into a party. On Group, select another current party
   member as the tank and enable Group mode.
3. On Support, enable Healer mode. Place healing skills on the active hotbar
   and configure automatic detection or explicit healing keys.
4. Set the party heal threshold and range. Keep Group follow distance within
   the heal range (for example, follow distance 4 with heal range 40).
   The Hunt setup healing-skill HP condition also applies: if enabled, its
   threshold must allow the desired party healing percentage.
5. Disable Ranged packs. Press F8 with the game focused. Following requires
   movement calibration; the normal start flow calibrates when needed.
   F9 stops and releases held inputs.

The healbot stops moving to heal the lowest-health eligible party member in
range, including itself. When no usable heal is ready, it follows the selected
tank at the configured distance. Group attack radius is unused in this mode.
Offensive skill targets, monster selection and nearby pickup are excluded from
the combined mode. Party skill buffs, HP/MP items, mana reserve rules and the
latest attack/defense potion switches remain available.

Following waits for a loaded, living tank with known health within the follow
limit. It respects configured avoidance zones and navigation. A departed,
unloaded, dead or distant tank is not chased; other eligible nearby party
members can still receive healing. Healing recipients are rechecked for range,
identity, party membership and F-key mapping before and during input. A changed
recipient interrupts that cast and is selected again on the next cycle.

Ordinary group combat and stationary healer mode retain their separate paths.

## Validation

- Release build and portable win-x64 publish succeeded without warnings/errors.
- Offline self-tests passed, including 22 group-healer policy checks and all
  22 potion checks from the previous update.
- UI persistence check passed for Healer + Group + tank selection together,
  preserving potion settings. Existing offline page rendering checks passed.
- No live follow/healing session or elevated packaged-EXE launch was tested.
  Real movement, healing range, F-key behavior and spell activation still need
  verification with the intended character and party in the game.
