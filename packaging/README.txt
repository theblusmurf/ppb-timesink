PoteHunter - Compact Healbot UI

Extract the entire folder and run Start-Fixed-Detection.cmd.
Copy your old settings.json before starting if you want to keep your preferences.
Also preserve navigation-routes.json and repair-profile.json (if configured).
Choose Solo, Group combat, Healer or Group healer in Setup.
For Group healer, choose a tank and set follow distance, healing skills and range.
Support contains skill buffs and attack/defense potion switches.
Settings save automatically. F8 starts; F9 stops.
See COMPACT-UI.md for details and validation limits.

Death recovery (Setup, combat modes):
- Auto revive + return along saved route: on revives and returns; off stops on death.
- Record a route from the revival point to the farming spot with Home and End.
  A saved spot alone is not a return route. Start near the recorded destination.
- Auto repair after revival: optional inventory-hammer repair before the return.
  Requires the game's repair capability and cost. Off by default.
- Enable repair, choose Configure repair with hunting stopped, and follow the
  two capture prompts. Select static inventory/repair-question text, then the
  hammer/Yes button center. Exclude changing gold values and item slots.
- Cancel the open game confirmation and close inventory after setup. Choose
  Test repair, switch to the game within 5 seconds, and check durability afterward.
  This test performs one repair confirmation. F9 stops it.
- Keep the game window size and inventory layout unchanged; configure again
  after changing them or updating the game. Use windowed/borderless presentation.
- Repair stops on missing controls or a stuck dialog, without retrying Yes.
  UI completion does not independently verify durability. Live repair/return
  require verification after your one-time setup; the shipped checks are offline.
