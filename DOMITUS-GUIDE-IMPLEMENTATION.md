# Domitus farming guide mapping

The supplied guide was reviewed as a behavior reference. This note records
which parts are implemented in the current source and which parts need a
client-specific recognition layer before they can be automated safely.

## Implemented

- Three local route slots hold the destination, facing, observed waypoints, and
  route profile. A profile records the character name, floor/height, farming
  radius, revival delay, and whether farming resumes on arrival.
- Recording samples movement at roughly one-third map-unit changes. A movement
  gap over 8 map units cancels the recording so a teleport or missing sample
  cannot become a false return route.
- Recovery joins the nearest recorded waypoint only when it is within 20 map
  units, then returns to the saved anchor and restores its saved facing.
- Occupancy fallback is checked at activation. Alternative routes are filtered
  by zone, character, floor/height, and their own farming radius before a free
  route is selected.
- Auto-revive still requires live HP confirmation and makes at most three
  spaced attempts. The configured revival delay is applied after death is
  confirmed and before the first key press.
- Existing anti-kill-steal, target safety, and movement-boundary checks remain
  in the active hunt loop.

## Not automated from the guide

Repair-after-death requires recognizing the client inventory, repair hammer,
confirmation dialog, and popup state. The current source has no verified UI
recognition or repair-item API, so adding coordinate clicks would be unsafe and
could damage an unrelated window. Likewise, `/kill` group switching and the
guide's named-group wait policy require a server/chat integration that is not
exposed by the current memory client. These steps remain manual until that
client capability is available.
