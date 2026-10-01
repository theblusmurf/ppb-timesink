# Public update distribution

Setup > Updates retains manual checking/installation. Automatic patching is
enabled by default from Release1.72 and can be disabled independently. It checks
at startup and every 30 minutes, stages a verified installer, and applies it only
when hunting, setup/tests, dialogs and manual route recording are stopped.
The packaged executable copies its patch worker outside the install directory,
verifies the original process before handoff, waits for exit, reserves the
single-instance mutex, and rechecks public release identity, size and SHA256.
The existing installer updates the same directory and preserves runtime data.
The worker then reopens PoteHunter with hunting stopped. It never forces a game
process to close or requests a Windows reboot. Logs stay in the local
PoteHunter/UpdateCache staging folder; failed versions stop automatic retry.
Manual installation remains available. Turning automatic patching off/on clears
that block. Install Release1.72 once using the existing updater to enable this.

The application checks https://github.com/theblusmurf/PoteHunter-Releases anonymously.
This repository contains installers, portable builds, checksums and patch notes.
Source development stays in the private PoteHunter repository. App users need no
GitHub account or token. Existing Windows updater credentials are unused.

The private Windows workflow builds and verifies each release before publication.
To automate the public mirror, the repository owner configures a fine-grained
publisher token as the private source repository's `PUBLIC_RELEASE_TOKEN` Actions
secret: restrict access to PoteHunter-Releases and Contents read/write. This is a
CI publishing credential, never an application credential or packaged file.
The private repository's built-in GITHUB_TOKEN cannot write another repository.

Without that secret, public mirroring is explicitly reported pending in the build.
Upload the exact four verified release files manually to a draft on the public
repository: installer EXE, portable ZIP, and their two SHA256 sidecars. Copy only
the version patch notes. Publish the draft as an official release after every
asset finishes uploading. Do not publish settings, profiles, logs or source archives.
Verify `/releases/latest` and download the installer without authentication before
reporting public distribution complete. Keep the public/private release tags equal.
