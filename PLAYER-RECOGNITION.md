# Player recognition

Player status is observational. It does not target players, start combat, change
movement, send chat, or establish who caused damage.

## Sentinel Radar

In **Routes → Navigation → Sentinel Radar**, turn the overlay or paired sonar
sound on/off, adjust **Enemy alert range** from **1 to 100 map units** in one-unit
steps, and set sound volume from 0–100%. The initial range is 25 and volume 45%.
These choices can change during hunting and are saved independently of combat
settings. The slider changes both the circular radar and arrival sound range.

The compact north-up radar shows nearby players and the nearest recognized enemy's
name, faction, distance and map bearing. Drag its header to reposition it; the
body passes clicks through to the game. **Reset radar position** restores its
initial placement. It scales to fit the game window and retains negative monitor
coordinates. The existing terrain radar and route overlays remain separate.

One paired sonar clip announces a new opposing-faction enemy entering the chosen
range in a verified PvP zone. Current players establish a silent baseline when
connecting, enabling the radar, changing its range/zone/character, or returning
after a prolonged reading gap. Repeated observations do not repeat the sound;
leaving beyond the selected range plus three units, or disappearing, for two
seconds rearms that identity. Simultaneous arrivals share one clip; sounds are
spaced at least three seconds apart. Muted or background arrivals are consumed,
not replayed on unmute or focus return.

Audio pauses for unknown/dead local HP, stale/disconnected readings, background
game windows, disabled radar/sound, or zero volume. Unknown/non-PvP zones and
validated party members never trigger enemy audio. Other-player HP may be unknown;
confirmed-dead bodies are excluded. Model-based faction and the zone-policy
limitations below still apply. Real client playback/placement should be checked
after upgrading; offline verification does not establish live attackability.

## Enemy identification

Sentinel makes the nearest enemy's available name the main heading and lists
the five nearest enemies separately, using session numbers that
remain stable as players cross, move or leave the selected watch radius. Overlap
Individual radar badges show available names alongside their session number.
Overlap badges show how many observed identities share a small radar area, plus their
numbers; leader lines retain the actual positions. More distant enemies remain
in the total count. A nearby enemy is drawn above the white self marker.

Names come from the client's verified creature-name field. If that field is empty,
the interface displays `Player` followed by its eight-digit hexadecimal UID and
states that names are unavailable. This is an observed body ID, not a permanent
character identity or a guessed real name. Raw name and name availability remain
separate in private status data. No verified alternate enemy-name reader exists.
Labels reset on connection, character or zone change; duplicate simultaneous UID
records are excluded before classification. Distinct UIDs at the same position
remain separate observations.

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
