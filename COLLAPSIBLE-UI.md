# Wayfinder command interface

The approved interface uses midnight navy panels, brass accents and matching Wayfinder compass branding in the application and Windows shortcut. Navigation remains along the bottom. Overview and the first Hunt page present the same compact command controls; the Hunt page picker retains detailed Setup and Support.

Six sections expand independently: Targets, Skills & self-healing, Movement & loot, Death & client recovery, Route & anchor radii, and Saved routes. Each closed header keeps a current settings summary. Collapse all reduces the command panel without changing a setting or interrupting hunting. Native buttons support keyboard and accessibility actions; use Enter or Space to toggle, Left to close, and Right to open.

## Radius controls

- **Route corridor:** 0.5–30 map units in 0.5-unit steps, default 10. Start can join a compatible saved route when the character lies within this distance from an actual path segment. The configured corridor is also used when client recovery resumes from a path. Death-return and occupied-spot fallback keep their existing compatibility and safety limits.
- **Anchor area:** the existing farming radius, now adjustable in 0.5-unit steps within its existing 5–150 bounds. Saved values and defaults are preserved. This controls the farming boundary; it does not change melee range or allow stationary targets to be chased.
- **Loot pickup radius:** 0.5–30 map units in 0.5-unit steps, default 10. Solo anchor pickup trips and the out-of-circle pickup guard use this separate radius. After pickup, the existing return sequence restores the saved anchor and facing.

The map shows the configured route band and anchor area using actual recorded routes and live position. An empty map remains empty until data is available. The anchor area is clipped to the current map viewport rather than forcing routes to zoom out.

## Existing behavior and data

Target presets continue to select one target family or a custom name filter, with that selection's Primary route and two alternatives. The stationary Mimic, Pulkhan, Tribal and Tower rules, Gamekeeper priority, skill and self-heal rules, recovery calibration, wallet baseline, session logs and updater retain their existing behavior.

All new controls edit the existing options, with two-way synchronization to detailed settings and the same running, busy, recovery and healer guards. Folding sections remains available while hunting because it only changes presentation. The ledger uses the existing six resource totals, original game item images and actual-wallet net gold. Reset loot and Reset timer call the existing actions.

Upgrades preserve settings.json, navigation-routes.json and local revival, repair and login profiles. Runtime settings, screenshots, routes, credentials and session logs are not shipped in the release.
