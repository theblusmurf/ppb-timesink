# Domitus farming guide mapping

The supplied guide and Domitus-Complete-Program-and-Source-20260930-020248Z
source were reviewed as behavior references. This note records what was
adapted into PoteHunter and the remaining verification limits.

## Implemented

- Three local route slots hold the destination, facing, observed waypoints, and
  route profile. A profile records the character name, floor/height, farming
  radius, revival delay, and whether farming resumes on arrival.
- The Navigation overlay now has an explicit start/finish workflow: start
  recording at the route origin, walk to the farming destination, then finish
  and save. A separate spot-save action creates a stationary anchor.
- Recording samples movement at roughly one-third map-unit changes. A movement
  gap over 8 map units cancels the recording so a teleport or missing sample
  cannot become a false return route.
- Recovery joins the nearest recorded waypoint only when it is within 20 map
  units, then returns to the saved anchor and restores its saved facing.
- Occupancy fallback is checked at activation, after revival, and while approaching the anchor. Alternative routes are filtered
  by zone, character, floor/height, and their own farming radius before a free
  route is selected. Primary and both alternatives participate in the cycle.
  Switching follows the current path back to the shared origin (within 3 units)
  then the destination's path. All occupied: return to the origin, wait ten
  active minutes, and retry. A second death preserves this pending cycle.
- Visual revival requires known dead HP and recognizes the centered Revive
  button using the supplied template. It permits at most three popup-opening
  clicks after the death delay, then one recognized confirmation. Positive HP
  is required before repair or return. Turning off Recognize Revive button
  retains the configured key method.
- Custom revival setup can replace the included detector with local paired
  dialog/button patches and an optional recognized death-screen opening click.
  It shares repair's capture editor, binds the profile to the client/window,
  and saves only after all selections validate. Test revival performs one real
  revive without starting farming; Use automatic backs up the custom profile
  before restoring built-in recognition. Preserve revival-profile.json on upgrade.
- Existing anti-kill-steal, target safety, and movement-boundary checks remain
  in the active hunt loop.
- A single **Auto revive + return along saved route** toggle controls recovery.
  Enabled recovery requires a recorded compatible return route; a spot-only
  save is not treated as a recorded path. Disabling it stops hunting on death.
- Optional **Auto repair after revival** uses the inventory hammer and Yes
  confirmation, then closes inventory before return. Paired button/text
  recognition uses four embedded Domitus templates and supports moved windows
  and common scales. Custom repair setup remains an optional local fallback
  bound to the game build/window size; the test button performs the sequence
  once. Recognition failure or interruption stops the hunt.
  Confirmation is attempted at most once per recovery cycle. This feature
  requires the game's repair capability/cost and does not verify durability.
- Cancellable image searches run on a worker; the input thread rechecks
  focus, character, HP, dialog markers, and pointer position before clicking.
- Enemies leaving the allowed area cause anchor return and waiting, retaining
  Gamekeeper completion boundaries and the existing stationary combat rules.

## Not automated from the guide

Automatic durability thresholds, repair inventory/price APIs, and verification
of the resulting durability are not exposed by the current memory client.
`/kill` group switching and the source's six named farming groups are not
imported. The occupied-spot wait policy is adapted to the three existing local
route slots. Recognition uses synthetic composites of the supplied templates;
revival, repair, route, cooldown, input, and UI checks run offline without game
input. Actual durability, visual detection in the user's client, and complete
return travel still need live verification. Existing five-target/80%-HP skill
gates, skill spacing, and swing ranges are retained.
