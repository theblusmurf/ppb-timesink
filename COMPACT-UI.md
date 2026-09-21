# Compact UI update

Based on the latest healbot + potion build. The original application ZIP,
source ZIP, and extracted application folder were backed up before editing.

## Start

Extract the entire new ZIP into its own folder. Copy your previous settings.json
and any calibration/maps you want to retain, then run Start-Fixed-Detection.cmd.
The runtime is included. F8 starts; F9 stops. Existing game connection and
administrator requirements remain unchanged.

## Layout

- Setup: one mode selector (Solo, Group combat, Healer, Group healer).
  Only the relevant target, tank, combat and healing rows are shown.
- Group healer: choose the tank in Setup, set follow distance, healing skills,
  Heal below and healing range. Keep follow distance within healing range.
- Recovery: HP and MP item thresholds together; mana reserve remains visible.
- Support: skill buffs, attack potions and defense potions plus detected status.
- Monitor: select Targets, Ground loot, Hotbar or Group from the sidebar list.
- Advanced: protection, navigation and ranged-pack pages. Setup also has an
  Advanced settings expander for item delays, follow limit, priorities, pickup,
  positioning and other tuning.

Changes save automatically after a short pause. Text edits save after leaving
the field; the Setup status shows Saved or Check settings. Invalid edits do
not replace the last valid saved configuration. Separate Save buttons are gone.

The healer's Heal below control displays the effective threshold from existing
settings. Old configurations with different party and skill thresholds retain
their behavior until edited. Editing Heal below updates both numeric thresholds
so a hidden lower skill threshold cannot silently block the configured heal.
Switching away from Solo disables ranged packs to avoid an incompatible mode.

Window: 980 x 700 by default, 900 x 640 minimum. Existing settings keys remain
compatible; combat, group-follow, potion and healer policies are unchanged.

## Validation

Release build and portable publish passed. Offline self-tests, including the
healer/group and potion policies, passed. UI checks cover all four persisted
modes, the unified healing threshold, actual delayed autosave, invalid-edit
protection, inherited running-state locks and page rendering. All nine pages
were inspected, plus Group healer at minimum size and expanded settings.

The elevated packaged EXE and live gameplay were not tested in this environment.
The original extracted app was left in place; the updated build is delivered
separately so you can switch back using the backup.
