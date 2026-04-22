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

function Initialize-Outputs {
    $metadataResolved = Resolve-FullPath -Path $StagedMetadataPath
    $metadataDirectory = Split-Path -Path $metadataResolved -Parent

    if ([string]::IsNullOrWhiteSpace($StatusJsonPath)) {
        $script:statusPathResolved = Join-Path $metadataDirectory 'external-updater-status.json'
    }
    else {
        $script:statusPathResolved = Resolve-FullPath -Path $StatusJsonPath
    }

    if ([string]::IsNullOrWhiteSpace($LogPath)) {
        $script:logPathResolved = Join-Path $metadataDirectory 'external-updater.log'
    }
    else {
        $script:logPathResolved = Resolve-FullPath -Path $LogPath
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

    $status | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $script:statusPathResolved -Encoding UTF8
}

function Write-Log {
    param([Parameter(Mandatory = $true)][string]$Message)

    $line = "$(Get-Date -Format o) $Message"
    Add-Content -LiteralPath $script:logPathResolved -Value $line -Encoding UTF8
    Write-Host $line
}

function Test-IsPreserved {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    $normalizedRelative = $RelativePath.Replace('\\', '/').TrimStart('./').TrimStart('/')
    foreach ($pattern in $Patterns) {
        $normalizedPattern = $pattern.Replace('\\', '/').TrimStart('./').TrimStart('/')
        if ($normalizedRelative -like $normalizedPattern) {
            return $true
        }

        if ($normalizedRelative.StartsWith("$normalizedPattern/", [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    return [System.IO.Path]::GetRelativePath($BasePath, $FullPath)
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

function Finalize-Success {
    $status.succeeded = $true
    $status.resultCode = 'success'
    $status.completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    $status.stage = 'completed'
    $status | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $script:statusPathResolved -Encoding UTF8
}

function Finalize-Failure {
    param([Parameter(Mandatory = $true)][string]$ErrorMessage)

    $status.succeeded = $false
    $status.resultCode = 'failed'
    $status.completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    $status.error = $ErrorMessage
    $status | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $script:statusPathResolved -Encoding UTF8
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

    $workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ping-monitor-updater-" + [System.Guid]::NewGuid().ToString('N'))
    $extractPath = Join-Path $workRoot 'extract'
    Ensure-Directory -Path $extractPath

    try {
        Expand-Archive -LiteralPath $zipPathResolved -DestinationPath $extractPath -Force

        $payloadRoot = Join-Path $extractPath 'app'
        if (-not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
            throw "Staged ZIP did not contain expected 'app' payload folder at '$payloadRoot'."
        }

        $status.appOfflinePath = Join-Path $script:installRootResolved 'app_offline.htm'

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
        Remove-IfStale -DestinationRoot $script:installRootResolved -SourceRoot $payloadRoot -PreservePatterns $PreserveRelativePaths
        Copy-NewPayload -SourceRoot $payloadRoot -DestinationRoot $script:installRootResolved -PreservePatterns $PreserveRelativePaths

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
