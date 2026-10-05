# Durability-triggered repair

In **Settings → Equipment repair**, enable **Repair low durability** and choose **Repair at … % or lower**. The default threshold is 20%; the setting is off until enabled. The Overview recovery section exposes the same toggle and percentage. Your existing repair setup is reused.

The percentage is the lowest **current / maximum** durability among equipped, repairable items. It is not an average. For example, equipment at 40/80 and 5/50 has a lowest durability of 10%. Bag items, empty equipped slots and items which the client marks nonrepairable do not lower the result. An unreadable occupied slot, invalid maximum or unstable equipment set makes the entire reading unavailable.

The trigger works independently of **Auto repair after revival**. It runs inside the existing hunt or healer loop and waits for combat, group movement, healing duties and route/anchor returns to clear. A fresh living-health baseline must remain quiet for at least three seconds. Repair releases held attack, pickup and movement input and uses the same recognized Inventory → Hammer → Question/Yes → inventory close sequence. It does not move the character or change the saved anchor.

Only one attempt is admitted for a low-durability episode. Repeated low readings, temporary unknown readings and stopping/starting the hunt do not repeat the paid confirmation. A fresh reading above the threshold rearms the trigger. Manual and post-revival repairs share the attempt bookkeeping.

After a durability-triggered repair, the same equipped set must show a real increase, no worsening pieces and a lowest value above the threshold within five seconds. If the result is unknown or unchanged, repair stops without another confirmation. New damage, nearby threats, changed equipment, needed group/healer actions, death, focus loss, F9 or identity/map changes interrupt the guarded sequence. Hunting resumes only after confirmed durability recovery; normal loot pickup is then restored.

The reader is optional and validates the client’s equipped-slot ownership, item class, repair eligibility, tooltip values and repair/update routines against both disk and loaded code. Unsupported code leaves durability unavailable and does not disable normal hunting. Each observation reads every equipped slot twice and checks character, scene and map stability. The diagnostic `--observe-durability --native-read-compat` saves five read-only samples beside the diagnostic build; it never opens inventory, activates a window or sends gameplay input.

Keep `settings.json`, `navigation-routes.json` and your repair/revival/login profiles during updates. Runtime durability observations and other game data stay local.
