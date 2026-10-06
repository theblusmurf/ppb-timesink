# Selected additions

This release adapts comparison choices 1, 14 and 15 from PoteHunter 2.0.45 to the existing PlayPoteBot source.

## Settings profiles

Open **Settings**, choose **Profiles** in the page selector, enter a name and select **Store current**. Select a saved profile and **Apply profile** to switch while hunting, recovery/setup and route recording are stopped. Applying makes a backup of settings first. Export and Import share compatible PlayPoteBot profiles; import alone does not activate them.

Profiles include the custom potion, ranged, smart-skill, durability and route/loot-radius settings. Character identity, global item-grade key, overlay placement and display preferences stay local. Routes, login/revival/repair calibration, logs and update preferences are separate and preserved. Profile files are in `settings-profiles` beside the application. Older or foreign profile formats are rejected instead of silently losing custom options.

## Collision maps

In **Routes > Navigation**, expand **Routing options** and select **Import collision map** with hunting stopped and the game connected in the intended zone. Choose a calibrated MapTool WorldGeometry JSON export. The client hash and zone must match. Review the map preview and live character marker, then confirm its alignment before accepting. Any unresolved geometry is listed in the preview.

The importer saves the map and decoded collision blockers in the existing `maps` folder and backs up a previous manifest. Only matching, confirmed maps affect routes. Obstacles join the existing protection zones and observed blocked directions; this is an additional local planning input, not a replacement for recorded routes or the existing movement checks. Maps without decoded collision candidates provide a background only.

## Item grades

In **Settings > Item grades**, enable automatic display, choose Auto or a target grade, and optionally choose a free shortcut. Connect and hover a weapon or armor until the game's tooltip appears. The passive overlay shows available stat grades and gem estimates beside the cursor. Existing F6, F8, F9, Home and End controls keep their roles; the default grade shortcut is None.

This reader checks the supported instructions and item identity and does not modify equipment or trigger an upgrade. Missing or unsupported readings remain unavailable. Gem and +10 projections are estimates; sockets beyond the two mapped fields are unconfirmed. Confirm these estimates in game before spending gems.

## Menu and updates

The main menu sits on the left. Open **Settings > Settings** for **Updates**, which uses the existing public updater.
