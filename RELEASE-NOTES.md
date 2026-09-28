# Release notes

## Release1.11

- Fixed melee attacks being starved when a nearby target moved slightly between
  live reads. The controller now keeps the basic attack held through a short
  melee range and heading correction window, including stationary hunt targets.
- A transient turn response no longer blocks the first swing after the target
  enters melee reach; the next loop continues facing while the attack remains
  active.
- Validation: Release build, offline self-test, and ranged UI check passed.

Windows releases include a **Patch notes** section generated from the commits since the previous release tag. Each release also lists the packaged ZIP and SHA256 sidecar, followed by the current feature and validation notes.

Keep commit subjects specific and user-facing so the generated notes explain what changed without requiring a separate manual edit for every build.
