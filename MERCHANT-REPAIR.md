# Hoys merchant repair

Choose **Setup > Equipment repair > Repair using > Merchant · Hoys, Roche Camp**. Inventory remains the default and continues to use the existing inventory hammer.

1. Stand at the same route start used by your recorded hunt paths. Click **Record Hoys route**, walk manually to Hoys, stop within interaction range facing his clickable name, then click **Finish Hoys route** or press **End**. This saves a separate merchant route; the three hunt slots are preserved. **Cancel recording** preserves the previous setup.
2. Choose **Capture Hoys shop**. Follow the capture wizard: Hoys' clickable name, the **General Store Merchant** menu row, then the open shop's **Repair All** and close button. Capture a separate cost-confirmation dialog only if the game uses one. The wizard sends no game input. Keep the same game-window size, UI placement, camera zoom and pitch used for these captures.
3. With hunting stopped and movement calibrated using **F6**, **Test repair · 5s** performs one real merchant trip and paid repair. Switch to the game during the countdown. Equipment that is already fully repaired skips the trip; use naturally damaged equipment to exercise the full sequence.
4. Enable **Auto repair after revival**, **Repair low durability**, or both. Low-durability walking trips wait for owned combat to finish. The merchant path must be in the current map and share its starting point with the target's saved hunt route. Unrecorded cross-map travel and portal jumps are not supported by this merchant path.

The bot follows the recorded path to Hoys, restores the recorded facing, clicks his recognized name and General Store Merchant, clicks Repair All once, and uses the captured cost confirmation at most once when configured. It requires fresh, complete readings proving the same equipped items reached full durability before closing the shop. It retraces the outbound path to the departure point and facing. After revival, the normal saved hunt-route return continues from there.

## Optional /kill at the threshold

**Use /kill at durability threshold** is off by default and applies only to Merchant mode with **Repair low durability** enabled. It requires **Auto revive + return** and **Auto repair after revival**, a valid Hoys setup, and a compatible saved hunt route. A stale custom revival profile blocks the option before any command is sent.

At the threshold the owning hunt loop releases movement and attack, submits `/kill` once, and waits up to 15 seconds for known zero HP. Only confirmed death starts the existing revival, merchant repair and saved-route return. Unknown health, a changed character/client/map, lost focus, cancellation or unconfirmed death stops the hunt. The command is never automatically repeated for that low-durability attempt.

F9 cancels the operation. Unknown controls, blocked travel, missing movement response, changed equipment or unproved repair stop the operation rather than repeating paid actions.

The private **merchant-repair-profile.json** contains the route and UI captures. Preserve it with settings/routes when moving a portable installation. Installers preserve it; packages and Git exclude it. Live travel and repair depend on your captured route and controls and must be checked with the stopped-state test.

After a game-client update, capture Hoys' shop controls again. A changed client fingerprint or window size blocks merchant actions, including threshold `/kill`, until setup is renewed.
