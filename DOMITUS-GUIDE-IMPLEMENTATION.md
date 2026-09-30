# Domitus farming guide mapping

The supplied guide was reviewed as a behavior reference. This note records
which parts are implemented in the current source and which parts need a
client-specific recognition layer before they can be automated safely.

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
  route is selected.
- Auto-revive still requires live HP confirmation and makes at most three
  spaced attempts. The configured revival delay is applied after death is
  confirmed and before the first key press.
- Existing anti-kill-steal, target safety, and movement-boundary checks remain
  in the active hunt loop.
- A single **Auto revive + return along saved route** toggle controls recovery.
  Enabled recovery requires a recorded compatible return route; a spot-only
  save is not treated as a recorded path. Disabling it stops hunting on death.
- Optional **Auto repair after revival** uses the inventory hammer and Yes
  confirmation, then closes inventory before return. One-time visual setup
  identifies static inventory/dialog text and controls. The configuration is
  local and bound to the game build and window size; the test button performs
  the sequence once. Recognition failure or interruption stops the hunt.
  Confirmation is attempted at most once per recovery cycle. This feature
  requires the game's repair capability/cost and does not verify durability.

## Not automated from the guide

Automatic durability thresholds, repair inventory/price APIs, and verification
of the resulting durability are not exposed by the current memory client.
`/kill` group switching and the guide's named-group wait policy also require a
server/chat integration that is not available. These remain manual. The new
repair recognizer and recovery checks are validated offline; actual repair
success and the full return must be checked in the user's game after setup.
