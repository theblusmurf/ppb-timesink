# Durability-triggered repair

In **Recovery → Equipment repair**, enable **Repair low durability** and choose **Repair at … % or lower**. The default threshold is 20%; the setting is off until enabled. The Overview recovery section exposes the same toggle and percentage. Your existing repair setup is reused.

The percentage is the lowest **current / maximum** durability among equipped, repairable items. It is not an average. For example, equipment at 40/80 and 5/50 has a lowest durability of 10%. Bag items, empty equipped slots and items which the client marks nonrepairable do not lower the result. An unreadable occupied slot, invalid maximum or unstable equipment set makes the entire reading unavailable.

The trigger works independently of **Auto repair after revival**. It runs inside the existing hunt or healer loop, including prolonged combat, incoming damage and nearby spawns. Route/anchor returns, revival and an input action already in progress retain ownership. Repair briefly releases held attack, pickup and movement input for the recognized Inventory → Hammer → Question/Yes → inventory close sequence. It does not move the character or change the saved anchor.

Only one attempt is admitted for a low-durability episode. Repeated low readings, temporary unknown readings and stopping/starting the hunt do not repeat the paid confirmation. A fresh reading above the threshold rearms the trigger. Manual and post-revival repairs share the attempt bookkeeping.

After a durability-triggered repair, a fresh reading of the same equipped set must show a real increase in its minimum and recovery above the threshold within five seconds. Normal combat wear on previously healthy items is allowed. Unknown, unchanged or still-low results stop without another confirmation. Changed equipment or maxima, death, unknown health, focus loss, F9 or identity/map changes still interrupt the sequence. After verified recovery, the combat loop refreshes the target and its health/range before resuming normal attack; a dead or replaced target is not attacked blindly. Normal loot pickup is restored.

The reader is optional and validates the client’s equipped-slot ownership, item class, repair eligibility, tooltip values and repair/update routines against both disk and loaded code. Unsupported code leaves durability unavailable and does not disable normal hunting. Each observation reads every equipped slot twice and checks character, scene and map stability. The diagnostic `--observe-durability --native-read-compat` saves five read-only samples beside the diagnostic build; it never opens inventory, activates a window or sends gameplay input.

Keep `settings.json`, `navigation-routes.json` and your repair/revival/login profiles during updates. Runtime durability observations and other game data stay local.
