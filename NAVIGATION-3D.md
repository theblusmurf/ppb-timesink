# Native 3D navigation

Fixed melee farming loot returns still settle within the saved point's existing
arrival tolerance, then keep the current heading so the next target owns aiming.
They no longer restore an optional idle heading before resuming selection.
Required recovery/priority facing and other hunt modes retain their normal
restoration. Short arrival aiming uses the existing pre-pulse movement limit;
offensive skill-facing, endpoint, clearance and same-floor guards are unchanged.

For same-map travel with **Teleport Scroll**, open **Navigation > Teleport Scroll
route**. Existing scroll calibrations appear in **Saved scroll profile**. Select
the farm route slot, choose the destination profile, then **Use for this route**.
Several target/route slots can share one calibration. Each assignment checks that
its recorded walking route joins that profile's landing before reaching the farm.
**Rename profile** changes its display name without recalibration or scroll use.
After a game client update, a saved scroll profile remains selectable for the
same character, map zone, and window size. Its saved client hash identifies the
original capture; the app verifies the current client and checks the scroll,
destination marker, and OK control again before using them. If the updated UI
does not match those captures, make a new profile before using that destination.

For a new destination, put exactly one Teleport Scroll assignment on the active
hotbar page and choose **New scroll profile**. Manually use a scroll, wait its
16-second countdown, then capture the destination marker, confirmation/OK and
living landing as guided. Setup sends no game input; manual scroll use consumes
your item normally. The profile is saved even if its onward route is not recorded
yet. Record the continuous landing-to-farm walking path, then assign the profile.

Enable **Use Teleport Scroll routes** only after the complete capture is saved.
Runtime releases movement and attacks, uses that captured scroll once, waits the
full countdown, selects the recognized destination and OK, verifies arrival, then
walks to the farm anchor. Keep the captured hotbar page and game window active.
Missing/replaced/locked scrolls or an interrupted/failed journey stop without
automatic scroll retries. Existing portal captures remain stored but need new
scroll setup. Scroll travel currently supports two points in the same map zone.

Open **Routes → Navigation**. The native view reads recovered terrain and available static objects from the verified local game installation. It requires no browser, WebView or separate rendering download. Choose **2D** to return to the original main map view.

## In-game overlays

Map labels use near-white text with an opaque dark tag in 2D and a dark outline in
3D, so they remain readable over pale map artwork and shaded terrain. Route lines,
markers and small 2D tag accents retain their original colors. This applies to the
main map, overview preview, radar and route overlays.

Start/F8 follows the selected target's compatible recorded path to its saved end
anchor when the character starts within the configured route corridor. Select the
desired Primary or Alternative slot first. Character and map must match; starts
already near an anchor retain its floor check. The saved height describes the end
anchor, so starting on a lower or higher part of a path does not compare that part
with the destination height. Existing routes store XY waypoints only, with no
per-waypoint elevation or independently verified bridge/floor data.

Fallback routes must share a nearby recorded start in the same map for the same
character. Different end-anchor heights alone do not invalidate that shared start.
The original end-anchor position and facing remain the travel destination.

Expand **Overlay options**. Enable **Show radar overlay** or **Show route overlay**, then select **3D radar** / **3D route overlay** independently. Both default to 3D; clearing either switch restores its original 2D presentation. **Overlays: north-up top view** changes the fixed tilted camera to an overhead view. Map layers and opacity above apply to the 3D overlays. These presentation choices can be changed and saved while hunting.

The radar follows the character using **Radar radius (map units)**. Red/orange markers represent known/unknown-HP living monsters; teal rings identify engaged targets. Gold marks live chests and amber marks remembered chests. Player heading, direction cone, traveled trail, current path and recorded blocked/avoid areas remain visual annotations. Dynamic markers require fresh connected readings. The route overlay fits the selected target's current-zone saved paths and player, retaining route colors, anchor facing, start corridor and farming area. Increasing the farming-area radius annotates the view without enlarging its zoom.

Overlays always use the current game zone, even when the main window is on another page, displaying 2D or browsing another zone. They remain passive and click-through; use controls in the application to change their view. Hidden renderer windows supply cached frames without taking focus. GPU readback is bounded to 900 pixels per axis, normally at most five radar frames and 2.5 route frames per second. Unsupported/missing terrain or hardware graphics fall back to 2D, with a status in Overlay options. Terrain remains a visual aid rather than confirmed collision or walkability.

## Viewing

- Choose Current map or a supported zone. Drag to orbit, right-drag to pan, and scroll to zoom. Top view is north-up, with east to the right.
- Fit map frames the terrain. Fit routes frames the selected target's compatible saved paths. View anchor centers the selected Primary or Alternative slot. Center player uses a fresh connected reading on the displayed map; it is unavailable when disconnected or browsing another zone.
- Map layers independently controls game artwork, terrain, buildings/objects, saved routes and anchor markers. Map opacity is saved from 0% to 100%. Original artwork is clipped to its verified/calibrated bounds instead of stretched across unrelated terrain.
- Existing route recording and management controls remain in expandable sections. Home starts recording and End finishes it while connected and stopped. Display options do not start route travel or change recovery settings.

## Sources and limits

Verified scene IDs are 1, 2, 3, 4, 5, 8, 9, 12, 15 and 16. The verified scene aliases are 6 → 3 and 17/18 → 16. Artwork uses the exact displayed zone's verified bounds; sharing terrain does not imply shared artwork. Other zones, missing files or an unverified client build fall back to the original 2D view. A hardware Windows OpenGL pixel format is required; unavailable rendering also falls back to 2D.

Both the original verified client and the October 6 client are supported. The
newer client moved its map routines but retains the verified terrain format,
coordinate units and zone assignments. Its local scene files match the prior
independent references. Updating to this app version restores the main 3D map,
radar and route overlays that previously reported this client as unavailable.

The scene reader validates all source height values and renders a reduced grid. Available primary buildings and compact object placements are decoded with source transforms. Missing references, invalid/sentinel transforms, vegetation and unverified compound child placements are omitted, with an explicit scene status. Zone 12 has missing object references in the verified installation. Static models currently use neutral materials, without recovered object textures.

Routes are draped over recovered terrain only for display. Segments across missing terrain are split, and anchors at unavailable terrain are omitted. Actual live marker heights come from existing readings. Bridge/walkway surfaces, object collision, navmeshes and walkability have not been verified. These models do not modify movement, combat, loot, revival, repair or route-following decisions.

Map loading runs off the UI thread. Cancellation and generation checks reject old-zone results; a two-map scene cache bounds retained geometry. Artwork uses an owned copy. OpenGL resources are released when the view closes or its handle is recreated.

## Validation and privacy

Offline checks cover ordinary and alternate scene codecs, file/count/index limits, unsafe paths, invalid transforms, cancellation, orientation, artwork clipping, missing-terrain route gaps and saved presentation settings. Local private comparisons independently verify recovered terrain vertices and object geometry/placements against prior decoded exports. Native hidden-window readback verifies rendering on an available hardware GPU; environments without a suitable hardware context report unavailable rather than pretending to render.

Only source code and documentation are published. No game models, map artwork, user routes, calibration profiles, runtime telemetry or diagnostic screenshots are included in Git or release packages. The installed running application is not replaced by development checks. Live GPU performance and marker appearance require verification after upgrading.
