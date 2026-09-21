# HP, MP, attack and defense potions

This update retains the minimal beta.6 UI and extends consumable detection.

## Use

1. Extract into a new folder. Copy your existing settings.json, maps and
   calibration files if you want to preserve your setup.
2. Place the potions you want to use on the active hotbar.
3. HP and MP recovery use the existing Hunt setup thresholds and item delays.
4. Enable Attack potions and/or Defense potions on Support. These switches
   default to off, independently of skill buff upkeep.
5. The Hotbar Role column identifies supported recovery and buff potions.
   Support displays the reason a buff potion is waiting or skipped.

Buff potions are used between fights after recovery checks. Readiness and
locked slots are respected. A recognized active effect prevents another dose.
When only a description duration is available, the full duration is reserved
after an attempted use, even if activation could not be confirmed. Potions
whose duration and matching effect are both unknown are skipped. Restarting
the application clears estimated timers; active effects remain authoritative
when an exact effect-name match is available.

Detection reads item descriptions, not an assumed list of potion names:

- Food / Potion restoration descriptions beginning with Restores, Recovers
  or Replenishes; health / HP, mana / MP, or both; fixed or percentage amounts.
- Potion descriptions explicitly increasing attack, damage, defense or armor.
- Buff duration in seconds, minutes or hours, or an exact live effect-name match.

Unknown/localized wording remains unclassified. No inventory search, item
purchase, hotbar assignment or automatic crafting is performed.

## Search and validation

The official crafting guide confirms tiered potions but did not provide a
verified machine-readable mapping of item names, effects and durations:
https://forum.playpote.com/threads/guide-potion-crafting-%E2%80%94-from-crude-to-almighty.139/

The installed client has an item script, but its records are not plain text.
The read-only live catalog scan failed with "Read-only OpenProcess failed".
Consequently the complete real-game potion catalog was not verified. A
read-only --potion-catalog diagnostic is included for an authorized local run;
it exports potion/food descriptions and effect metadata without sending input.

Build: zero warnings/errors. All offline self-tests and 22 new potion checks
passed, including recovery classification, combined items, cooldowns, effect
timers, duplicate-dose protection and persisted switches. The Support page
was rendered and inspected, and the UI settings round-trip check passed.
Potion fixtures are synthetic description examples, not verified game items.

Portable publish succeeded. Live potion consumption and the elevated packaged
EXE were not tested in this environment. No potions were consumed during work.
