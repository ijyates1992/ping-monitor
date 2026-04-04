# Build and Release

This project includes a PowerShell script to produce a clean, self-contained release package.

The script:
- injects version information into the application
- builds and publishes the app as self-contained
- packages the app, agent, docs, and sample configs
- generates a release archive and checksum

---

## Requirements

- Windows environment
- PowerShell (`pwsh` or Windows PowerShell)
- .NET SDK installed (matching project version)

---

## Version Format

Releases must use the following strict version format:

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

The script will fail if the version format is invalid.

---

## Running the build script

From the repository root, run:

```powershell
pwsh ./scripts/build.ps1 -Version V0.1.0
```

Optional runtime override:

```powershell
pwsh ./scripts/build.ps1 -Version V0.1.0 -Runtime win-x64
```

---

## Output

The script produces versioned output under:

`artifacts/releases/`

Primary release archive:

`PingMonitor-V0.1.0-win-x64.zip`

Release staging folder contents:

- `app/`                → self-contained web application (includes agent runtime assets)
- `docs/`               → repository documentation
- `config-samples/`     → sample configuration files
- `manifest.json`       → release metadata

Additional generated file in `artifacts/releases/`:

- `SHA256.txt`          → checksum for the generated zip

---

## Notes

- The release is self-contained and does not require a separate .NET runtime.
- Version information is injected during the build process.
- If version injection is missing, the app will display `Internal dev build`.
- The script validates required files before packaging and will fail if anything is missing.

---

## Tips

- Always verify the generated zip before publishing.
- Do not include private configuration files in releases.
- Keep version numbers consistent with your release history.
