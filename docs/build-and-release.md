# Build and Release

This project includes PowerShell scripts to produce clean, self-contained packages.

- `scripts/build.ps1` is for **strict release builds**
- `scripts/build-dev.ps1` is for **internal development/test builds only**

Both scripts:
- inject version information into the application
- build and publish the app as self-contained
- package the app, agent, docs, and sample configs
- generate an archive and checksum

---

## Requirements

- Windows environment
- PowerShell 7 (`pwsh`) for updater apply operations and release scripts
- .NET SDK installed (matching project version)

> Operator note: the web application's update **check** and **stage** actions can run without `pwsh`, but update **apply** is blocked unless `pwsh` is available in PATH.

---

## Release builds (`build.ps1`)

### Version format

Release builds require a strict version format:

`Vx.x.x`

Examples:
- `V0.1.0`
- `V1.2.3`
- `V10.12.3`

Invalid examples:
- `v1.2.3`
- `1.2.3`
- `V1.2`
- `V1.2.3-beta`

The release script fails if the format is invalid.

### Run release build

```powershell
pwsh ./scripts/build.ps1 -Version V0.1.0
```

Optional runtime override:

```powershell
pwsh ./scripts/build.ps1 -Version V0.1.0 -Runtime win-x64
```

### Release output

The release script produces versioned output under:

`artifacts/releases/`

Primary release archive example:

`PingMonitor-V0.1.0-win-x64.zip`

---

## Internal development builds (`build-dev.ps1`)

> This script is for internal dev/test packaging only. Do not use it for production releases.

### Dev version behavior

- Does **not** accept `-Version`
- Automatically generates a timestamped dev version for in-app display:
  - `DEV-DD.MM.YY-HH:MM`
  - Example: `DEV-05.04.26-18:42`
- Uses filename-safe variant for folder/archive names:
  - `DEV-DD.MM.YY-HH.MM`
  - Example: `DEV-05.04.26-18.42`

### Run dev build

```powershell
pwsh ./scripts/build-dev.ps1
```

Optional runtime override:

```powershell
pwsh ./scripts/build-dev.ps1 -Runtime win-x64
```

### Dev output

The dev script produces versioned output under:

`artifacts/dev-releases/`

Primary dev archive example:

`PingMonitor-DEV-05.04.26-18.42-win-x64.zip`

---

## Package contents (release and dev)

Generated staging folder contents:

- `app/`                → self-contained web application (includes agent runtime assets)
- `docs/`               → repository documentation
- `config-samples/`     → sample configuration files
- `manifest.json`       → package metadata

Additional generated file in artifact root:

- `SHA256.txt`          → checksum for the generated zip

For dev builds, `manifest.json` includes:

- `buildType: "dev"`

---

## Notes

- Packages are self-contained and do not require a separate .NET runtime.
- Version information is injected during the build process.
- If version injection is missing, the app will display `Internal dev build`.
- Scripts validate required files before packaging and will fail if anything is missing.

---

## Tips

- Always verify the generated zip before publishing or sharing.
- Do not include private configuration files in packages.
- Keep release version numbers consistent with your release history.
