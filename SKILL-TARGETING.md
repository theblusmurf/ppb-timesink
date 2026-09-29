# Skill targeting

Skill casts now use the engaged roster when smart targeting is enabled. Target
changes stay inside the configured nearby-enemy radius, so an engaged enemy
outside the active pack cannot pull the character away from its saved position.
Area and enemy-line skills select the densest nearby engaged pack as their aim
anchor; single-target skills retarget to the highest-current-health engaged
enemy in range. The compact UI exposes independent controls for smart
selection, area centering, and single-target retargeting. Area clustering is
capped to a close 2.5-unit pack so distant engaged enemies do not pull the aim
away from the attack cone.

The basic left swing is reasserted after each skill hand-off, including a
declined health/mana check, a target switch, and cooldown telemetry. This keeps
the attack active between skill uses while the character faces the selected
engaged target.
