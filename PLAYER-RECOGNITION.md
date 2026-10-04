# Player recognition

Player status is observational. It does not target players, start combat, change
movement, send chat, or establish who caused damage. Alert sound/layout concepts
are separate previews and have not been selected or enabled in this update.

## Faction families

| Exact player body | Model-based faction |
| --- | --- |
| `PC_MAN.GCMDS`, `PC_WOMAN.GCMDS` | Kartefant / Human |
| `PC_Akhan_A.GCMDS`, `PC_Akhan_B.GCMDS` | Merkhadian / Akkan |
| Other model, mount, NPC or unreadable body | Unknown |

These model families were previously identified by the user in project review
on September 25. They are not a server faction flag, and all four variants have
not been independently re-observed in the current client. The local character's
actual body determines its faction; no race is hard-coded as the enemy side.

## Zone policy and evidence

| Client zone | Map | Faction enemy status |
| --- | --- | --- |
| 8 | Caernarvon / Caernavon | Opposing recognized faction is Enemy |
| 12 | Almighty Land | Opposing recognized faction is Non-PvP opponent |
| 1–7, 9–11, 13–18, 100, other/unreadable IDs | Unverified | Unknown; no inferred Enemy status |

[Official staff leveling guide](https://forum.playpote.com/threads/level-up-guide-starting-your-journey.160/)
describes opposing-race PvP in Caernarvon. The numeric Zone 8/name mapping comes
from earlier user-confirmed client observations. The Zone 12/no faction PvP rule
comes from the user's September 25 confirmation, not independent official policy
verification. Neither rule establishes protected town/camp polygons or a
particular player's current attackability.

[Official staff Fame guide](https://forum.playpote.com/threads/quick-guide-fame-system.24/)
also documents PvP in Battleground, but its numeric client zone is unverified.
Caernarvon Guild Wars may permit same-race kills, so Same faction is not a promise
of friendliness. The [staff MOBA guide](https://forum.playpote.com/threads/new-feature-moba-mode-5-vs-5.17/)
uses teams; the current reader has no verified team state or arena zone ID.
These rules cannot be generalized to those zones. Map geometry aliases do not
inherit combat rules. No complete current official map/ID PvP catalog was found.

## Display and freshness

The 2D/3D radar and main Navigation use player diamonds with status labels:
pink Enemy, blue Same faction, teal Party, gold Non-PvP opponent, gray Unknown.
Party status requires a current validated ID/name match; name avoidance rules
are not a friend list. Confirmed-dead players, self and NPC guards are omitted.
Player status is copied from one accepted poll and clears after three seconds
without a fresh reading, zone/local identity changes, death-time read gaps or
disconnection. Choosing another map does not change the live character's rule.

Private `live-status.json` records the faction basis, current zone evidence,
each player's relation and a count of recognized enemies within 25 map units.
This count is not a damage, attackability or faction-war state reading. Runtime
logs, character names, profiles and screenshots remain local and uncommitted.
