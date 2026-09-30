# Gold tracking

Release1.50 fixes an amount-reading regression introduced in Release1.48.
The running application's recording showed currency drops labeled `Gold (1)`.
Its `Quantity` values included 990760296, 1216813036, and 1420326648. These
were rejected by a plausibility limit, which then substituted one gold.

The client ground-item constructor copies the signed type to record offset
`+0x08`. It does not initialize `+0x0c`, so that field must not supply a quantity.
This was checked read-only in the installed client (SHA256
`c7feb7dedf493ed6526682dd8ccab9ecfbc0963a9ee56b59b33335e7d6031b64`,
constructor at `0x004e7ff0`, type copy at `0x004e8104`).

The tracker restores the currency encoding: for a negative currency type,
clear the high bit and use the remaining 31 bits as the pile amount. For
example, type -2147483551 yields 97 gold. An explicitly named currency amount
is a fallback for records with a nonnegative type. An unavailable amount adds
zero and appears as `Gold (amount unavailable)` rather than an invented payout.
Diagnostics now expose `EncodedGoldAmount` instead of the invalid raw quantity.

New piles are matched to a nearby Mimic, Tribal, Pulkhan, or Tower death within
the existing attribution window and saved farming radius. Zone and pile keys
prevent duplicate credit. Existing piles in the initial snapshot are excluded.
Recent observations remain available briefly when auto-pickup happens before
the kill is observed. These behaviors and the 40 ms sampling remain in place.

Gold amounts feed session totals, per-source summaries, active-time rates, and
death/reset CSV logs. The separate drop count continues to count piles/items.
Offline regression checks parse the actual signed types and stale field values,
verify 535 gold from five recorded piles, and check CSV output and deduplication.

This counts detected drops, not confirmed wallet receipts. A pile can be missed
between snapshots, attributed to the wrong nearby death, or collected by another
player. The repair does not retroactively change an older application's session
totals. Live pickup-to-wallet reconciliation has not been performed.
