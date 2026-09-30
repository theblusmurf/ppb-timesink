# Public update distribution

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
