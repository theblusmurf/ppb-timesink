# Local builds

Build, validate and package on this computer. Use an elevated PowerShell prompt because the existing application executable requests administrator startup, including in its offline test modes:

```powershell
./Local-Build.ps1 -Version Release1.152
```

The command uses the installed .NET SDK, cached NuGet packages and an existing Inno Setup compiler. Package sources are cleared and dependency auditing is disabled for restore; missing dependencies fail locally rather than downloading them. No GitHub login, Actions runner, source push or release upload is part of the command.

An optional, ignored `local-build.config.json` configures this computer:

```json
{
  "Dotnet": "C:/Program Files/dotnet/dotnet.exe",
  "CompilerPath": "C:/Tools/Inno/ISCC.exe",
  "OutputRoot": "C:/Builds/PlayPoteBot"
}
```

Use existing local paths. You can also pass `-Dotnet`, `-CompilerPath` and `-OutputDirectory` directly. Existing output folders are never overwritten.

Each output folder contains the self-contained portable ZIP, local installer, SHA256 sidecars, package verification report and `local-build-manifest.json`. Checks run against build/test copies. Building does not install the update or change the running bot, game, settings or routes. Install the resulting local setup program when the bot is closed; its existing settings-preservation rules remain in effect.

`-TestInstaller` additionally runs installation, patcher and uninstall checks against disposable fixture data. Use a disposable Windows profile without an existing PlayPoteBot installation or registration: installer tests share the app identity and shortcuts. This option is off by default and is blocked on the normal installed-app profile.

The former GitHub build workflow is retained as `.github/workflows/windows-package.yml.disabled`; it is not an active workflow. Publishing is a separate action and requires a fresh explicit user request. Existing remote releases are historical and are not part of a local build.
