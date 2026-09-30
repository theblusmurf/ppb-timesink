# Field Console interface

The selected Field Console style uses forest surfaces, brass accents, compact
controls and top navigation. The default window is 1040 x 760; the minimum is
960 x 680. Long settings remain accessible by scrolling.

## Start and update

Install the official Windows setup package or extract the entire portable ZIP
and run PoteHunter.exe. Native read/input is enabled at ordinary startup; the
existing client, focus and Windows input guards still apply. Setup > Updates
checks the public PoteHunter-Releases feed without an account or token.

Preserve settings.json, navigation-routes.json, repair-profile.json and
revival-profile.json when moving from a portable folder to an installation.
Existing installed upgrades preserve these files. See packaging/README.txt
for recovery and installation details.

## Layout

- Setup places operating mode, targets and skills on the left; HP/MP items,
  self-heal conditions, mana reserve and death recovery are on the right.
  Advanced settings and Updates are below the operating mode card.
- Group healer shows tank, follow distance, healing keys, threshold and range.
  Combat and death recovery controls appear only in combat modes.
- Support contains skill buffs and attack/defense potions.
- Monitor selects Targets, Ground loot, Hotbar or Group beside the heading.
- Navigation opens directly from the top bar and contains routes, radar,
  treasure guidance and loot overlay options.
- Advanced selects Protection or Ranged packs beside the heading.
- Index lists every global hotkey and explains route/recovery prerequisites.
- The status strip shows operation feedback, existing HP/MP readings and
  F8/F9 reminders. Unknown or disconnected vitals remain unknown. Hover the
  HP/MP bars for character/location details when connected.

Settings save automatically after a short pause; text edits save on leaving
 the field. Saved or Check settings indicates the result. Invalid edits retain
 the previous valid configuration. Settings inherit the running-state lock.
Changing the healer threshold updates both existing healing thresholds.
Switching away from Solo disables incompatible ranged-pack mode.

## Saved routes and recovery

In Navigation select Primary or either Alternative slot. With the game or
PoteHunter foreground, connected and hunting stopped, Home starts recording.
Walk to the anchor, face the targets and press End to save the route and facing.
Repeated Home preserves an active recording. A spot without waypoints is not
 a return route.

In solo mode Start/F8 can join a compatible route within 10 map units of its
path, follow it to the anchor and continue hunting. The selected compatible
slot is preferred. Activation near its endpoint preserves the exact activation
location and facing; outside the corridor ordinary activation hunting applies.

Enable Revive + return to anchor and Resume farming on arrival for automatic
death recovery. Record alternatives from the same revival start for occupied
spot fallback. Optional repair runs after confirmed revival and before return.
Recognition/focus/identity failures stop recovery. Test revival and Test repair
perform their named actions and leave hunting stopped.

## Validation

Release1.67 has an offline build and self-tests, plus rendered UI checks for all
navigation destinations, grouped page selectors, minimum-size controls, four
persisted modes, autosave, invalid-edit protection, inherited settings locks,
repair/revival controls and unknown/zero/full HP/MP formatting. All pages,
expanded settings and the minimum-size Group healer layout were inspected.

No live game input was used for this UI update. Existing combat and recovery
behavior was retained; offline checks do not establish live gameplay success.
