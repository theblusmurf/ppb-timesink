# Grouped menu and turn-speed limit

The left sidebar now opens each feature directly. Hunting, Party, World & tools,
Safety and Settings can be collapsed with a mouse, keyboard or screen reader.
Collapsing a group leaves the current page open. The active group stays marked;
programmatic page changes reopen its group. The menu scrolls on smaller windows.
Farming contains the existing target, skill, movement, recovery and route folds.
Overlays has the existing MiniMap and loot presentation controls. Updates remains
inside Settings > Options. Start, Stop and Connect / refresh remain visible below
every page. Existing bound controls, operation locks and hotkeys are preserved.

The new original lime-and-silver wayfinder logo is embedded in the header,
window icon and executable. Windows shortcuts and installer branding use the
same multi-resolution icon. Internal executable and installation identities stay
the same to preserve upgrades.

Hunting > Hunt setup > Turn speed cap sets the maximum calibrated horizontal
turn rate between 30 and 360 degrees per second (default 180). Try 90 for gentler
turning. The value saves automatically and travels with a named hunting profile.
It applies to body facing, travel steering and ranged camera yaw; it does not
change calibration pulses, vertical aiming or forward movement speed.
Existing acceleration smoothing, heading feedback, outstanding-turn accounting,
collision gates and stop guards remain. A shared elapsed-time angular allowance
prevents rapid polling or alternating body/camera calls from bypassing the cap.
At very low speeds with coarse calibration it waits until one calibrated pixel
is affordable. Idle time stores at most 50ms or one pixel, preventing large bursts.

Validation uses disconnected native UI fixtures and offline simulated turning.
Actual game appearance and live movement smoothness require post-update testing.
No live game input or changes to existing settings, routes or recovery profiles
are required for validation.
