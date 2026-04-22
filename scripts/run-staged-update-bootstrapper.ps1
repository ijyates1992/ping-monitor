[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$StagedMetadataPath,

    [Parameter(Mandatory = $true)]
    [string]$InstallRootPath,

    [Parameter(Mandatory = $false)]
    [string]$StatusJsonPath,

    [Parameter(Mandatory = $false)]
    [string]$LogPath,

    [Parameter(Mandatory = $false)]
    [string]$SiteName,

    [Parameter(Mandatory = $false)]
    [string]$AppPoolName,

    [Parameter(Mandatory = $false)]
    [string]$ExpectedReleaseTag,

    [Parameter(Mandatory = $false)]
    [string[]]$PreserveRelativePaths = @('App_Data', 'appsettings.json', 'appsettings.*.json'),

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$status = [ordered]@{
    schemaVersion = 1
    startedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    completedAtUtc = $null
    stage = 'starting'
    succeeded = $false
    resultCode = 'in_progress'
    targetReleaseTag = $null
    stagedZipPath = $null
    installRootPath = $null
    siteName = $SiteName
    appPoolName = $AppPoolName
    dryRun = [bool]$DryRun
    appOfflinePath = $null
    logPath = $null
    statusJsonPath = $null
    steps = @()
    error = $null
}

$script:siteStopped = $false
$script:appPoolStopped = $false
$script:appOfflineCreated = $false
$script:statusPathResolved = $null
$script:logPathResolved = $null
$script:installRootResolved = $null
$script:runtimeStateRootResolved = $null
$script:statusMirrorPathResolved = $null
$script:effectivePreservePatterns = @()

# Release/package ownership model:
# 1) Runtime/environment-owned paths MUST survive file replacement.
# 2) Release-owned payload files are replaced from staged package contents.
# 3) Mixed-ownership files (for example appsettings.json) are preserved, then patched with release-owned metadata.
$mandatoryRuntimePreservePatterns = @(
    # Startup Gate persists DB reconnection state in App_Data/StartupGate (dbsettings.json + dbpassword.bin).
    # This directory is machine/runtime-owned and must survive payload replacement unchanged.
    'App_Data/StartupGate',
    'App_Data/Backups',
    'App_Data/DbBackups',
    'App_Data/Updater'
)

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -Path $Path -ItemType Directory -Force | Out-Null
    }
}

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}


function Test-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string]$RootPath
    )

    $candidateFull = [System.IO.Path]::GetFullPath($CandidatePath).TrimEnd('\', '/')
    $rootFull = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\', '/')

    if ([string]::Equals($candidateFull, $rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $rootWithSeparator = $rootFull + [System.IO.Path]::DirectorySeparatorChar
    return $candidateFull.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}

function New-RuntimeStateRoot {
    $sessionName = "ping-monitor-external-updater-" + [System.Guid]::NewGuid().ToString('N')
    return Join-Path ([System.IO.Path]::GetTempPath()) $sessionName
}

function Try-WriteTextFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Description
    )

    try {
        $parentDirectory = Split-Path -Path $Path -Parent
        if (-not [string]::IsNullOrWhiteSpace($parentDirectory)) {
            Ensure-Directory -Path $parentDirectory
        }

        Set-Content -LiteralPath $Path -Value $Content -Encoding UTF8
        return $true
    }
    catch {
        Write-Warning "Failed to write $Description at '$Path': $($_.Exception.Message)"
        return $false
    }
}

function Initialize-Outputs {
    $metadataResolved = Resolve-FullPath -Path $StagedMetadataPath
    $metadataDirectory = Split-Path -Path $metadataResolved -Parent

    $script:installRootResolved = Resolve-FullPath -Path $InstallRootPath

    $fallbackRuntimeRoot = New-RuntimeStateRoot
    $fallbackStatusPath = Join-Path $fallbackRuntimeRoot 'external-updater-status.json'
    $fallbackLogPath = Join-Path $fallbackRuntimeRoot 'external-updater.log'

    $statusPathCandidate = if ([string]::IsNullOrWhiteSpace($StatusJsonPath)) {
        Join-Path $metadataDirectory 'external-updater-status.json'
    }
    else {
        Resolve-FullPath -Path $StatusJsonPath
    }

    $logPathCandidate = if ([string]::IsNullOrWhiteSpace($LogPath)) {
        Join-Path $metadataDirectory 'external-updater.log'
    }
    else {
        Resolve-FullPath -Path $LogPath
    }

    $statusPathWithinInstallRoot = Test-PathWithinRoot -CandidatePath $statusPathCandidate -RootPath $script:installRootResolved
    $logPathWithinInstallRoot = Test-PathWithinRoot -CandidatePath $logPathCandidate -RootPath $script:installRootResolved

    if ($statusPathWithinInstallRoot -or $logPathWithinInstallRoot) {
        $script:runtimeStateRootResolved = $fallbackRuntimeRoot
        $script:statusPathResolved = $fallbackStatusPath
        $script:logPathResolved = $fallbackLogPath

        $script:statusMirrorPathResolved = $statusPathCandidate

        Write-Warning "Bootstrapper runtime log/status paths are inside the replaceable install root. Using external runtime state path '$script:runtimeStateRootResolved' for live writes."
    }
    else {
        $script:statusPathResolved = $statusPathCandidate
        $script:logPathResolved = $logPathCandidate

        $statusDirectory = Split-Path -Path $script:statusPathResolved -Parent
        $script:runtimeStateRootResolved = if (-not [string]::IsNullOrWhiteSpace($statusDirectory)) {
            $statusDirectory
        }
        else {
            New-RuntimeStateRoot
        }
    }

    Ensure-Directory -Path (Split-Path -Path $script:statusPathResolved -Parent)
    Ensure-Directory -Path (Split-Path -Path $script:logPathResolved -Parent)

    $status.logPath = $script:logPathResolved
    $status.statusJsonPath = $script:statusPathResolved
}

function Write-Status {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $status.stage = $Stage
    $status.steps += [ordered]@{
        stage = $Stage
        message = $Message
        atUtc = (Get-Date).ToUniversalTime().ToString('o')
    }

    $statusJson = $status | ConvertTo-Json -Depth 8
    [void](Try-WriteTextFile -Path $script:statusPathResolved -Content $statusJson -Description 'status JSON')
}

function Write-Log {
    param([Parameter(Mandatory = $true)][string]$Message)

    $line = "$(Get-Date -Format o) $Message"

    try {
        $parentDirectory = Split-Path -Path $script:logPathResolved -Parent
        if (-not [string]::IsNullOrWhiteSpace($parentDirectory)) {
            Ensure-Directory -Path $parentDirectory
        }

        Add-Content -LiteralPath $script:logPathResolved -Value $line -Encoding UTF8
    }
    catch {
        Write-Warning "Failed to append updater log at '$script:logPathResolved': $($_.Exception.Message)"
    }

    Write-Host $line
}

function Test-IsPreserved {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    $normalizedRelative = $RelativePath -replace '[\\/]+', '/'
    $normalizedRelative = $normalizedRelative.TrimStart('./').TrimStart('/')
    foreach ($pattern in $Patterns) {
        $normalizedPattern = $pattern -replace '[\\/]+', '/'
        $normalizedPattern = $normalizedPattern.TrimStart('./').TrimStart('/')
        if ($normalizedRelative -like $normalizedPattern) {
            return $true
        }

        if ($normalizedRelative.StartsWith("$normalizedPattern/", [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Test-HasPreservedDescendant {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    $normalizedRelative = $RelativePath -replace '[\\/]+', '/'
    $normalizedRelative = $normalizedRelative.TrimStart('./').TrimStart('/')
    if ([string]::IsNullOrWhiteSpace($normalizedRelative)) {
        return $false
    }

    foreach ($pattern in $Patterns) {
        $normalizedPattern = $pattern -replace '[\\/]+', '/'
        $normalizedPattern = $normalizedPattern.TrimStart('./').TrimStart('/')
        if ($normalizedPattern.StartsWith("$normalizedRelative/", [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Get-EffectivePreservePatterns {
    param([Parameter(Mandatory = $true)][string[]]$RequestedPatterns)

    $allPatterns = @()
    $allPatterns += $mandatoryRuntimePreservePatterns
    $allPatterns += $RequestedPatterns

    $result = New-Object System.Collections.Generic.List[string]
    foreach ($pattern in $allPatterns) {
        if ([string]::IsNullOrWhiteSpace($pattern)) {
            continue
        }

        $normalized = ($pattern -replace '[\\/]+', '/').Trim()
        if (-not $result.Contains($normalized)) {
            $result.Add($normalized)
        }
    }

    return $result.ToArray()
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    $resolvedBasePath = [System.IO.Path]::GetFullPath($BasePath)
    $resolvedFullPath = [System.IO.Path]::GetFullPath($FullPath)

    if ($resolvedBasePath.Equals($resolvedFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return '.'
    }

    $baseWithSeparator = $resolvedBasePath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $baseUri = [System.Uri]::new($baseWithSeparator)
    $fullUri = [System.Uri]::new($resolvedFullPath)
    $relativeUri = $baseUri.MakeRelativeUri($fullUri)

    if ($relativeUri.IsAbsoluteUri) {
        return $resolvedFullPath
    }

    $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString()) -replace '/', [System.IO.Path]::DirectorySeparatorChar
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        return '.'
    }

    return $relativePath
}

function Update-PreservedAppSettingsVersion {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot
    )

    $sourceAppSettingsPath = Join-Path $SourceRoot 'appsettings.json'
    $destinationAppSettingsPath = Join-Path $DestinationRoot 'appsettings.json'

    if (-not (Test-Path -LiteralPath $sourceAppSettingsPath -PathType Leaf)) {
        Write-Log "Mixed-ownership merge skipped: staged payload appsettings.json not found at '$sourceAppSettingsPath'."
        return
    }

    if (-not (Test-Path -LiteralPath $destinationAppSettingsPath -PathType Leaf)) {
        Write-Log "Mixed-ownership merge skipped: existing appsettings.json not found at '$destinationAppSettingsPath'."
        return
    }

    $sourceSettings = Get-Content -LiteralPath $sourceAppSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $destinationSettings = Get-Content -LiteralPath $destinationAppSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json

    if (-not $sourceSettings -or -not $destinationSettings) {
        throw 'Unable to parse appsettings.json for mixed-ownership merge.'
    }

    $sourceVersion = $sourceSettings.ApplicationMetadata.Version
    if ([string]::IsNullOrWhiteSpace($sourceVersion)) {
        Write-Log 'Mixed-ownership merge skipped: staged appsettings.json does not define ApplicationMetadata.Version.'
        return
    }

    if (-not $destinationSettings.ApplicationMetadata) {
        $destinationSettings | Add-Member -NotePropertyName 'ApplicationMetadata' -NotePropertyValue ([PSCustomObject]@{})
    }

    if (-not ($destinationSettings.ApplicationMetadata.PSObject.Properties.Name -contains 'Version')) {
        $destinationSettings.ApplicationMetadata | Add-Member -NotePropertyName 'Version' -NotePropertyValue $sourceVersion
    }
    else {
        $destinationSettings.ApplicationMetadata.Version = $sourceVersion
    }

    if ($DryRun) {
        Write-Log "DryRun: would merge staged ApplicationMetadata.Version '$sourceVersion' into preserved appsettings.json."
        return
    }

    $mergedJson = $destinationSettings | ConvertTo-Json -Depth 32
    Set-Content -LiteralPath $destinationAppSettingsPath -Value $mergedJson -Encoding UTF8
    Write-Log "Merged staged ApplicationMetadata.Version '$sourceVersion' into preserved appsettings.json."
}

function Stop-IisRuntime {
    if ($DryRun) {
        Write-Log 'DryRun enabled: skipping IIS stop operations.'
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($SiteName) -or -not [string]::IsNullOrWhiteSpace($AppPoolName)) {
        Import-Module WebAdministration -ErrorAction Stop
    }

    if (-not [string]::IsNullOrWhiteSpace($SiteName)) {
        Write-Log "Stopping IIS site '$SiteName'."
        Stop-Website -Name $SiteName -ErrorAction Stop
        $script:siteStopped = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($AppPoolName)) {
        Write-Log "Stopping IIS app pool '$AppPoolName'."
        Stop-WebAppPool -Name $AppPoolName -ErrorAction Stop
        $script:appPoolStopped = $true
    }
}

function Start-IisRuntime {
    if ($DryRun) {
        Write-Log 'DryRun enabled: skipping IIS start operations.'
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($SiteName) -or -not [string]::IsNullOrWhiteSpace($AppPoolName)) {
        Import-Module WebAdministration -ErrorAction Stop
    }

    if ($script:appPoolStopped -and -not [string]::IsNullOrWhiteSpace($AppPoolName)) {
        Write-Log "Starting IIS app pool '$AppPoolName'."
        Start-WebAppPool -Name $AppPoolName -ErrorAction Stop
    }

    if ($script:siteStopped -and -not [string]::IsNullOrWhiteSpace($SiteName)) {
        Write-Log "Starting IIS site '$SiteName'."
        Start-Website -Name $SiteName -ErrorAction Stop
    }
}

function Remove-IfStale {
    param(
        [Parameter(Mandatory = $true)][string]$DestinationRoot,
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string[]]$PreservePatterns
    )

    Get-ChildItem -LiteralPath $DestinationRoot -Recurse -Force | Sort-Object -Property FullName -Descending | ForEach-Object {
        $relative = Get-RelativePath -BasePath $DestinationRoot -FullPath $_.FullName
        if (Test-IsPreserved -RelativePath $relative -Patterns $PreservePatterns) {
            return
        }

        if ($_.PSIsContainer -and (Test-HasPreservedDescendant -RelativePath $relative -Patterns $PreservePatterns)) {
            return
        }

        $sourceCounterpart = Join-Path $SourceRoot $relative
        if (-not (Test-Path -LiteralPath $sourceCounterpart)) {
            if ($DryRun) {
                Write-Log "DryRun: would remove stale path '$($_.FullName)'."
            }
            else {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force
            }
        }
    }
}

function Copy-NewPayload {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot,
        [Parameter(Mandatory = $true)][string[]]$PreservePatterns
    )

    Get-ChildItem -LiteralPath $SourceRoot -Recurse -Force | ForEach-Object {
        $relative = Get-RelativePath -BasePath $SourceRoot -FullPath $_.FullName
        if (Test-IsPreserved -RelativePath $relative -Patterns $PreservePatterns) {
            Write-Log "Preserving existing target path for '$relative'."
            return
        }

        $destinationPath = Join-Path $DestinationRoot $relative
        if ($_.PSIsContainer) {
            if (-not $DryRun) {
                Ensure-Directory -Path $destinationPath
            }
            return
        }

        $destinationDirectory = Split-Path -Path $destinationPath -Parent
        if (-not $DryRun) {
            Ensure-Directory -Path $destinationDirectory
            Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
        }
        else {
            Write-Log "DryRun: would copy '$($_.FullName)' to '$destinationPath'."
        }
    }
}

function Resolve-PayloadRoot {
    param(
        [Parameter(Mandatory = $true)][string]$ExtractRoot
    )

    $directPayloadRoot = Join-Path $ExtractRoot 'app'
    if (Test-Path -LiteralPath $directPayloadRoot -PathType Container) {
        Write-Log "Detected payload layout A at '$directPayloadRoot'."
        return [ordered]@{
            payloadRoot = $directPayloadRoot
            packageRoot = $ExtractRoot
            layout = 'extract-root/app'
        }
    }

    $topLevelDirectories = @(Get-ChildItem -LiteralPath $ExtractRoot -Directory -Force)
    if ($topLevelDirectories.Count -eq 1) {
        $nestedPackageRoot = $topLevelDirectories[0].FullName
        $nestedPayloadRoot = Join-Path $nestedPackageRoot 'app'
        if (Test-Path -LiteralPath $nestedPayloadRoot -PathType Container) {
            Write-Log "Detected payload layout B at '$nestedPayloadRoot'."
            return [ordered]@{
                payloadRoot = $nestedPayloadRoot
                packageRoot = $nestedPackageRoot
                layout = 'extract-root/<single-package-folder>/app'
            }
        }
    }

    $topLevelEntries = @(Get-ChildItem -LiteralPath $ExtractRoot -Force | ForEach-Object {
            if ($_.PSIsContainer) { "[D] $($_.Name)" } else { "[F] $($_.Name)" }
        })
    $topLevelSummary = if ($topLevelEntries.Count -gt 0) { $topLevelEntries -join ', ' } else { '(none)' }

    $attemptedLayouts = @(
        "A: '$directPayloadRoot'",
        "B: '<extract-root>/<single-top-level-dir>/app'"
    ) -join '; '

    throw "Unable to detect staged ZIP payload root. Extract root: '$ExtractRoot'. Top-level entries: $topLevelSummary. Attempted layouts: $attemptedLayouts."
}

function Test-PackageStructure {
    param(
        [Parameter(Mandatory = $true)][string]$PackageRoot
    )

    $payloadRoot = Join-Path $PackageRoot 'app'
    if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
        throw "Detected package root '$PackageRoot' is missing required 'app' folder."
    }

    $manifestPath = Join-Path $PackageRoot 'manifest.json'
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        Write-Log "Package structure validation: found manifest at '$manifestPath'."
    }
    else {
        Write-Log "Package structure validation: manifest.json not found at '$manifestPath' (continuing)."
    }
}

function Finalize-Success {
    $status.succeeded = $true
    $status.resultCode = 'success'
    $status.completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    $status.stage = 'completed'
    $statusJson = $status | ConvertTo-Json -Depth 8
    [void](Try-WriteTextFile -Path $script:statusPathResolved -Content $statusJson -Description 'status JSON')

    if (-not [string]::IsNullOrWhiteSpace($script:statusMirrorPathResolved)) {
        [void](Try-WriteTextFile -Path $script:statusMirrorPathResolved -Content $statusJson -Description 'mirrored status JSON')
    }
}

function Finalize-Failure {
    param([Parameter(Mandatory = $true)][string]$ErrorMessage)

    $status.succeeded = $false
    $status.resultCode = 'failed'
    $status.completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    $status.error = $ErrorMessage
    $statusJson = $status | ConvertTo-Json -Depth 8
    [void](Try-WriteTextFile -Path $script:statusPathResolved -Content $statusJson -Description 'status JSON')

    if (-not [string]::IsNullOrWhiteSpace($script:statusMirrorPathResolved)) {
        [void](Try-WriteTextFile -Path $script:statusMirrorPathResolved -Content $statusJson -Description 'mirrored status JSON')
    }
}

try {
    Initialize-Outputs

    Write-Status -Stage 'validating_inputs' -Message 'Validating staged metadata and target install paths.'
    Write-Log "Using staged metadata path '$StagedMetadataPath'."

    $metadataPathResolved = Resolve-FullPath -Path $StagedMetadataPath
    if (-not (Test-Path -LiteralPath $metadataPathResolved)) {
        throw "Staged metadata file does not exist: $metadataPathResolved"
    }

    $script:installRootResolved = Resolve-FullPath -Path $InstallRootPath
    if (-not (Test-Path -LiteralPath $script:installRootResolved -PathType Container)) {
        throw "Install root directory does not exist: $script:installRootResolved"
    }

    $status.installRootPath = $script:installRootResolved

    $metadataRaw = Get-Content -LiteralPath $metadataPathResolved -Raw -Encoding UTF8
    $metadata = $metadataRaw | ConvertFrom-Json

    if (-not $metadata) {
        throw 'Unable to parse staged metadata JSON.'
    }

    if ([string]::IsNullOrWhiteSpace($metadata.ReleaseTag)) {
        throw 'Staged metadata is missing ReleaseTag.'
    }

    if ([string]::IsNullOrWhiteSpace($metadata.StagedZipPath)) {
        throw 'Staged metadata is missing StagedZipPath.'
    }

    if (-not $metadata.ChecksumVerified) {
        throw 'Staged metadata indicates checksum has not been verified. Refusing to continue.'
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedReleaseTag) -and -not [string]::Equals($ExpectedReleaseTag, $metadata.ReleaseTag, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "ExpectedReleaseTag '$ExpectedReleaseTag' does not match staged release '$($metadata.ReleaseTag)'."
    }

    $zipPathResolved = Resolve-FullPath -Path $metadata.StagedZipPath
    if (-not (Test-Path -LiteralPath $zipPathResolved -PathType Leaf)) {
        throw "Staged ZIP package not found: $zipPathResolved"
    }

    $status.targetReleaseTag = [string]$metadata.ReleaseTag
    $status.stagedZipPath = $zipPathResolved

    Write-Status -Stage 'extracting_staged_zip' -Message 'Extracting staged ZIP to temporary location for validation.'
    Write-Log "Staged release tag: $($metadata.ReleaseTag)."
    Write-Log "Staged ZIP path: $zipPathResolved."
    Write-Log "Install root path: $script:installRootResolved."
    Write-Log "Runtime updater state path (non-replaceable): $script:runtimeStateRootResolved."

    $workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ping-monitor-updater-" + [System.Guid]::NewGuid().ToString('N'))
    $extractPath = Join-Path $workRoot 'extract'
    Ensure-Directory -Path $extractPath

    try {
        Expand-Archive -LiteralPath $zipPathResolved -DestinationPath $extractPath -Force

        $resolvedPayload = Resolve-PayloadRoot -ExtractRoot $extractPath
        $payloadRoot = $resolvedPayload.payloadRoot
        $packageRoot = $resolvedPayload.packageRoot
        Write-Log "Using payload root '$payloadRoot' (layout: $($resolvedPayload.layout))."
        Test-PackageStructure -PackageRoot $packageRoot

        $status.appOfflinePath = Join-Path $script:installRootResolved 'app_offline.htm'
        $script:effectivePreservePatterns = Get-EffectivePreservePatterns -RequestedPatterns $PreserveRelativePaths
        Write-Log "Effective preserve patterns: $($script:effectivePreservePatterns -join ', ')."

        Write-Status -Stage 'entering_maintenance' -Message 'Creating app_offline marker and stopping IIS runtime.'
        if ($DryRun) {
            Write-Log 'DryRun enabled: skipping app_offline creation.'
        }
        else {
            @'
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>Maintenance</title></head>
<body><h1>Ping Monitor is updating</h1><p>Please retry shortly.</p></body>
</html>
'@ | Set-Content -LiteralPath $status.appOfflinePath -Encoding UTF8
            $script:appOfflineCreated = $true
            Write-Log "Created app_offline marker at '$($status.appOfflinePath)'."
        }

        Stop-IisRuntime

        Write-Status -Stage 'replacing_payload' -Message 'Replacing IIS payload while preserving configured local paths.'
        Remove-IfStale -DestinationRoot $script:installRootResolved -SourceRoot $payloadRoot -PreservePatterns $script:effectivePreservePatterns
        Copy-NewPayload -SourceRoot $payloadRoot -DestinationRoot $script:installRootResolved -PreservePatterns $script:effectivePreservePatterns

        Write-Status -Stage 'merging_mixed_ownership_configuration' -Message 'Merging release-owned version metadata into preserved runtime configuration.'
        Update-PreservedAppSettingsVersion -SourceRoot $payloadRoot -DestinationRoot $script:installRootResolved

        Write-Status -Stage 'starting_iis_runtime' -Message 'Starting IIS runtime after payload replacement.'
        Start-IisRuntime

        if ($script:appOfflineCreated -and -not $DryRun) {
            Remove-Item -LiteralPath $status.appOfflinePath -Force -ErrorAction SilentlyContinue
            $script:appOfflineCreated = $false
            Write-Log "Removed app_offline marker at '$($status.appOfflinePath)'."
        }

        Write-Status -Stage 'completed' -Message 'External updater completed successfully.'
        Write-Log 'Updater completed successfully.'
        Finalize-Success
    }
    finally {
        if (Test-Path -LiteralPath $workRoot) {
            Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
catch {
    $errorMessage = $_.Exception.Message
    Write-Log "Updater failed: $errorMessage"

    Write-Status -Stage 'failed' -Message $errorMessage

    try {
        if ($script:appPoolStopped -or $script:siteStopped) {
            Write-Log 'Attempting best-effort IIS restart after failure.'
            Start-IisRuntime
        }
    }
    catch {
        Write-Log "Best-effort IIS restart failed: $($_.Exception.Message)"
    }

    Finalize-Failure -ErrorMessage $errorMessage
    exit 1
}

exit 0
