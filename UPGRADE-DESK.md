# Equipment Upgrade Desk

The weapon and armor grade overlay now uses the selected Upgrade Desk design:
a dark green panel with item identity, an explicitly labelled best-stat badge,
three focus-stat metrics, every readable stat's grade and target progress, a
regular-gem plan and a separately labelled AAA upgrade/socket estimate.

Choose the target and **Focus stat** in **World & Tools > Item grades**. Auto picks
the unfinished stat with the highest percentage of its selected grade threshold,
considering all 13 supported stat fields on the item.
This compares unlike stat units fairly; equal percentages keep tooltip order.
Reached targets and unavailable thresholds are excluded from Auto focus.
If all supported targets are reached, the focus panel reports completion without
selecting a reached stat. Gem estimate
availability does not affect proximity. A manually selected stat takes priority;
a saved stat absent on the current item falls back to Auto. Existing target,
automatic popup and hotkey choices
are retained. The last-reading text retains the planner's other gem tiers.

The overlay uses the existing item reader and grade planner. No new item
discovery or game input is introduced. Automatic mode remains click-through;
manual mode remains non-activating and click-to-close. Hover dismissal, focus
gates and the manual timeout retain their existing behavior. The panel stays
beside the item while open, flips at monitor edges, and scales to fit the
monitor's available space. Unknown stats retain raw values without grades,
target progress or gem recommendations.

Jewelry categories (rings, necklaces, amulets, earrings and bracelets) do not
open either automatic or manual overlays. Hovering jewelry immediately dismisses
the previous equipment panel. Classification uses the item's client category or
grade-table type/category, never its display name: weapons such as Mount Ring
remain eligible. Unknown categories and unsupported stat thresholds are not guessed.

All gem and +10 scenarios are estimates. The socket assumptions are explicitly
shown; they are not a claim that the current item has empty sockets. The
best-stat badge is not a grade applying to every stat of the item.

Validation: `--item-grade-desk-check <output-directory>` renders the production
panel offscreen using synthetic weapon/armor/unknown/reached/empty examples.
It checks planner-derived values, focus fallback, legacy settings, DPI/work
area bounds and native passive/click-through styles without connecting to the
game or sending input. Verify.ps1 runs these checks and the existing offline
and disconnected native UI checks. Live hover appearance remains to be checked
after updating the installed app.
