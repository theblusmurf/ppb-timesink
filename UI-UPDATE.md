# Minimal UI update for beta.6

The setup page now groups everyday controls into Hunt and Health & Mana.
Advanced settings expands skill timing, pickup, target priority, movement,
and automation controls. Save settings remains next to the expander.

The app uses neutral charcoal surfaces, a slimmer sidebar, subtle selection
highlights, and a blue Start action. Stop remains visible on every page.
F8 and F9 shortcuts are unchanged. The Save button no longer occupies the
mana-reserve row.

All settings controls remain children of the existing settings panel so
the existing running/busy lock still applies. Saved option names, defaults,
healing/mana policies, and game input behavior are unchanged.

## Validation

- All three projects built successfully with .NET SDK 10.0.401, with zero warnings.
- Final application offline self-tests passed through the .NET host.
- Offline UI checks passed: settings round-trip, advanced expand/collapse,
  inherited settings lock, and rendered pages at default/minimum window sizes.
- All nine page previews were inspected; the setup page was also inspected
  at 1000 x 720 and with Advanced expanded.
- Portable win-x64 publish succeeded and includes the .NET runtime.
- The packaged EXE requires administrator elevation, inherited from beta.6.
  This environment could not launch it, so packaged-EXE execution and live
  game interaction are not verified. The DLL checks above did run successfully.

## Install

Extract the application ZIP into a new folder and run Start-Fixed-Detection.cmd.
To retain your preferences, close the app and copy your existing settings.json
into the new PoteHunter folder before launching. Keep your existing maps and
calibration files if you use them.

Source changes are in HunterForm.ModernUi.cs, HunterForm.MinimalSettings.cs,
and the offline preview checks in HunterForm.RangedPull.cs.
