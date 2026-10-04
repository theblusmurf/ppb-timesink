# Native 3D navigation

Open **Routes → Navigation**. The native view reads recovered terrain and available static objects from the verified local game installation. It requires no browser, WebView or separate rendering download. Choose **2D** to return to the original map view. The passive radar and route overlays retain their existing 2D presentation.

## Viewing

- Choose Current map or a supported zone. Drag to orbit, right-drag to pan, and scroll to zoom. Top view is north-up, with east to the right.
- Fit map frames the terrain. Fit routes frames the selected target's compatible saved paths. View anchor centers the selected Primary or Alternative slot. Center player uses a fresh connected reading on the displayed map; it is unavailable when disconnected or browsing another zone.
- Map layers independently controls game artwork, terrain, buildings/objects, saved routes and anchor markers. Map opacity is saved from 0% to 100%. Original artwork is clipped to its verified/calibrated bounds instead of stretched across unrelated terrain.
- Existing route recording and management controls remain in expandable sections. Home starts recording and End finishes it while connected and stopped. Display options do not start route travel or change recovery settings.

## Sources and limits

Verified scene IDs are 1, 2, 3, 4, 5, 8, 9, 12, 15 and 16. The verified scene aliases are 6 → 3 and 17/18 → 16. Artwork uses the exact displayed zone's verified bounds; sharing terrain does not imply shared artwork. Other zones, missing files or an unverified client build fall back to the original 2D view. A hardware Windows OpenGL pixel format is required; unavailable rendering also falls back to 2D.

The scene reader validates all source height values and renders a reduced grid. Available primary buildings and compact object placements are decoded with source transforms. Missing references, invalid/sentinel transforms, vegetation and unverified compound child placements are omitted, with an explicit scene status. Zone 12 has missing object references in the verified installation. Static models currently use neutral materials, without recovered object textures.

Routes are draped over recovered terrain only for display. Segments across missing terrain are split, and anchors at unavailable terrain are omitted. Actual live marker heights come from existing readings. Bridge/walkway surfaces, object collision, navmeshes and walkability have not been verified. These models do not modify movement, combat, loot, revival, repair or route-following decisions.

Map loading runs off the UI thread. Cancellation and generation checks reject old-zone results; a two-map scene cache bounds retained geometry. Artwork uses an owned copy. OpenGL resources are released when the view closes or its handle is recreated.

## Validation and privacy

Offline checks cover ordinary and alternate scene codecs, file/count/index limits, unsafe paths, invalid transforms, cancellation, orientation, artwork clipping, missing-terrain route gaps and saved presentation settings. Local private comparisons independently verify recovered terrain vertices and object geometry/placements against prior decoded exports. Native hidden-window readback verifies rendering on an available hardware GPU; environments without a suitable hardware context report unavailable rather than pretending to render.

Only source code and documentation are published. No game models, map artwork, user routes, calibration profiles, runtime telemetry or diagnostic screenshots are included in Git or release packages. The installed running application is not replaced by development checks. Live GPU performance and marker appearance require verification after upgrading.
