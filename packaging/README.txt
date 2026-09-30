PoteHunter - Compact Healbot UI

Extract the entire folder and run Start-Fixed-Detection.cmd.
Copy your old settings.json before starting if you want to keep your preferences.
Also preserve navigation-routes.json, repair-profile.json, and revival-profile.json
(if configured).
Choose Solo, Group combat, Healer or Group healer in Setup.
For Group healer, choose a tank and set follow distance, healing skills and range.
Support contains skill buffs and attack/defense potion switches.
Settings save automatically. F8 starts; F9 stops.
See COMPACT-UI.md for details and validation limits.

Death recovery (Setup, combat modes):
- Confirmed 0 HP is death. Auto recovery also interrupts resting when HP hits 0.
- Auto revive + return along saved route: on revives and returns; off stops on death.
- Recognize Revive button is on by default. It clicks only a recognized button,
  then waits for living HP. Turn it off to use the configured revival key.
- Visual revival waits at least 3 seconds after death, then sends three opening
  left-clicks with a minimum 250 ms gap. It stops opening when Revive appears
  or HP is alive/unreadable, and confirms Revive only once. A longer saved
  route delay takes precedence. Custom setups use their saved opening location.
- A shifting cursor or changing dialog is rechecked before clicking; unsent
  clicks do not consume the three-click limit. Recovery stops if the screen
  does not settle within its timeout. Stop messages report the actual reason.
- Custom revival setup works like repair setup. While dead, connected, and not
  hunting, capture the Revive dialog, select its static text, then its button.
  Choose the optional first step if a left-click opens the dialog: capture the
  death-screen text and that opening click location. Each capture allows five
  seconds to switch to the game and move the pointer away. Setup sends no clicks.
- Test revival gives five seconds to switch to the game, then performs one real
  revival. It requires known dead HP and reports success only after living HP.
  Hunting stays stopped; repair and return travel run during normal recovery.
- Custom revival profiles require the same client build, window size, and dialog
  layout. Missing recognition stops recovery. Use automatic backs up the custom
  profile and restores the included detector. Full screenshots are not stored.
- Record a route from the revival point to the farming spot with Home and End.
  A saved spot alone is not a return route. Start near the recorded destination.
- Record Primary and both alternatives from the same starting point (within
  3 map units). Occupied-spot fallback follows recorded paths via that point.
  Saving only Primary enables its return path, not two alternative destinations.
  If all compatible spots are occupied, it waits there 10 minutes and retries.
  Only currently loaded players can be detected. Blocked routes stop recovery.
- Auto repair after revival: optional inventory-hammer repair before the return.
  Requires the game's repair capability and cost. Off by default.
- Automatic recognition uses the included Domitus inventory/repair templates
  and supports moved windows and common UI scales. Enable repair, choose
  Test repair with hunting stopped, switch to the game within 5 seconds,
  and check equipment durability afterward.
  This test performs one repair confirmation. F9 stops it.
- For a different layout, Custom repair setup captures inventory and the repair
  confirmation. Select static text and the hammer/Yes button centers. Cancel
  the open confirmation and close inventory before testing. A custom profile
  requires the same game build, window size, and layout used for setup.
- Use a visible windowed/borderless game. Keep it foreground during recovery.
- Repair stops on missing controls or a stuck dialog, without retrying Yes.
  UI completion does not independently verify durability. The shipped checks
  are offline; verify repair, revival, and return in your client.
