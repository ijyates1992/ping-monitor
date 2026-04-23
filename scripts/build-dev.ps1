[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')]
    [string]$Runtime = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Message)
    Write-Host "    $Message"
}

function Fail-Build {
    param([string]$Message)
    Write-Error $Message
    exit 1
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        Fail-Build "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Remove-PathIfPresent {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string]$DestinationDirectory
    )

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null

    $entries = Get-ChildItem -LiteralPath $SourceDirectory -Force
    foreach ($entry in $entries) {
        $targetPath = Join-Path $DestinationDirectory $entry.Name
        if ($entry.PSIsContainer) {
            Copy-Item -LiteralPath $entry.FullName -Destination $targetPath -Recurse -Force
            continue
        }

        Copy-Item -LiteralPath $entry.FullName -Destination $targetPath -Force
    }
}

$buildTimestamp = Get-Date
$displayVersion = $buildTimestamp.ToString('dd.MM.yy-HH:mm')
$fileVersion = $buildTimestamp.ToString('dd.MM.yy-HH.mm')
$devVersionDisplay = "DEV-$displayVersion"
$devVersionFileSafe = "DEV-$fileVersion"
$buildTimestampUtc = [DateTimeOffset]::UtcNow.ToString('o')

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDirectory '..')).Path
Set-Location $repoRoot

$webProjectPath = Join-Path $repoRoot 'src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj'
$docsPath = Join-Path $repoRoot 'docs'
$configSampleSpecs = @(
    @{ Source = Join-Path $repoRoot 'src/WebApp/PingMonitor.Web/appsettings.json'; Target = 'webapp.appsettings.json' },
    @{ Source = Join-Path $repoRoot 'src/Agent/.env.example'; Target = 'agent.env.example' }
)

$releaseRoot = Join-Path $repoRoot 'artifacts/dev-releases'
$publishRoot = Join-Path $releaseRoot 'publish'
$packageBaseName = "PingMonitor-$devVersionFileSafe-$Runtime"
$stagingRoot = Join-Path $releaseRoot $packageBaseName
$publishOutputPath = Join-Path $publishRoot $Runtime
$zipPath = Join-Path $releaseRoot "$packageBaseName.zip"
$checksumPath = Join-Path $releaseRoot 'SHA256.txt'
$manifestPath = Join-Path $stagingRoot 'manifest.json'

if (-not (Test-Path -LiteralPath $webProjectPath)) {
    Fail-Build "Expected web project was not found at '$webProjectPath'."
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCommand) {
    Fail-Build 'The dotnet CLI is required but was not found on PATH.'
}

$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$commitHash = $null
if ($gitCommand) {
    $isGitRepository = & $gitCommand.Source rev-parse --is-inside-work-tree 2>$null
    if ($LASTEXITCODE -eq 0 -and $isGitRepository -eq 'true') {
        $gitStatus = & $gitCommand.Source status --porcelain
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($gitStatus -join ''))) {
            Write-Warning 'Git working tree is not clean. Dev artifacts will include uncommitted changes.'
        }

        $resolvedCommitHash = & $gitCommand.Source rev-parse --short=12 HEAD
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($resolvedCommitHash)) {
            $commitHash = $resolvedCommitHash.Trim()
        }
    } else {
        Write-Warning 'Current directory is not a Git repository. Commit hash metadata will be unavailable.'
    }
}

Write-Step 'Starting internal dev packaging'
Write-Info "Repository root: $repoRoot"
Write-Info "Dev version (display): $devVersionDisplay"
Write-Info "Dev version (file-safe): $devVersionFileSafe"
Write-Info "Runtime: $Runtime"
Write-Info "Package: $packageBaseName"

Write-Step 'Cleaning previous dev build output'
Remove-PathIfPresent -Path $publishRoot
Remove-PathIfPresent -Path $stagingRoot
Remove-PathIfPresent -Path $zipPath
Remove-PathIfPresent -Path $checksumPath
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

Write-Step 'Restoring and publishing web app (self-contained)'
Invoke-External -FilePath $dotnetCommand.Source -Arguments @('restore', $webProjectPath)
Invoke-External -FilePath $dotnetCommand.Source -Arguments @(
    'publish',
    $webProjectPath,
    '--configuration', 'Release',
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $publishOutputPath,
    "/p:Version=0.0.0-dev",
    "/p:AssemblyVersion=0.0.0.0",
    "/p:FileVersion=0.0.0.0",
    "/p:InformationalVersion=$devVersionFileSafe",
    '/p:ContinuousIntegrationBuild=true'
)

Write-Step 'Validating required source assets'
if (-not (Test-Path -LiteralPath $docsPath -PathType Container)) {
    Fail-Build "Required docs folder not found at '$docsPath'."
}

foreach ($sample in $configSampleSpecs) {
    if (-not (Test-Path -LiteralPath $sample.Source -PathType Leaf)) {
        Fail-Build "Required config sample file not found at '$($sample.Source)'."
    }
}

Write-Step 'Validating publish output'
if (-not (Test-Path -LiteralPath $publishOutputPath -PathType Container)) {
    Fail-Build "Publish output directory was not created at '$publishOutputPath'."
}

$publishedFiles = Get-ChildItem -LiteralPath $publishOutputPath -Recurse -File
if ($publishedFiles.Count -eq 0) {
    Fail-Build "Publish output directory '$publishOutputPath' is empty."
}

if ($Runtime -like 'win-*') {
    $expectedExecutablePath = Join-Path $publishOutputPath 'PingMonitor.Web.exe'
    if (-not (Test-Path -LiteralPath $expectedExecutablePath -PathType Leaf)) {
        Fail-Build "Expected self-contained executable not found at '$expectedExecutablePath'."
    }
}

$requiredAgentAssets = @(
    'Agent/README.md',
    'Agent/requirements.txt',
    'Agent/run-agent.cmd',
    'Agent/app/main.py'
)

foreach ($relativeAssetPath in $requiredAgentAssets) {
    $assetPath = Join-Path $publishOutputPath $relativeAssetPath
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        Fail-Build "Required agent deployment asset missing from publish output: '$relativeAssetPath'."
    }
}

$requiredUpdaterAssets = @(
    'Updater/run-staged-update-bootstrapper.ps1'
)

foreach ($relativeAssetPath in $requiredUpdaterAssets) {
    $assetPath = Join-Path $publishOutputPath $relativeAssetPath
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        Fail-Build "Required updater deployment asset missing from publish output: '$relativeAssetPath'."
    }
}

Write-Step 'Injecting internal dev version into app metadata source'
$appSettingsPath = Join-Path $publishOutputPath 'appsettings.json'
if (-not (Test-Path -LiteralPath $appSettingsPath -PathType Leaf)) {
    Fail-Build "Published appsettings.json was not found at '$appSettingsPath'."
}

$appSettings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
if (-not $appSettings.ApplicationMetadata) {
    $appSettings | Add-Member -NotePropertyName 'ApplicationMetadata' -NotePropertyValue ([PSCustomObject]@{})
}

if (-not ($appSettings.ApplicationMetadata.PSObject.Properties.Name -contains 'Version')) {
    $appSettings.ApplicationMetadata | Add-Member -NotePropertyName 'Version' -NotePropertyValue $devVersionDisplay
} else {
    $appSettings.ApplicationMetadata.Version = $devVersionDisplay
}
$appSettings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $appSettingsPath -Encoding UTF8NoBOM

$injectedSettings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
if (-not $injectedSettings.ApplicationMetadata -or $injectedSettings.ApplicationMetadata.Version -ne $devVersionDisplay) {
    Fail-Build 'Version injection failed. ApplicationMetadata.Version was not updated as expected.'
}

if (-not $devVersionDisplay.StartsWith('DEV-')) {
    Fail-Build 'Dev build version safety check failed. Generated version is not clearly marked as DEV-*.'
}

Write-Step 'Building dev staging layout'
$appStagingPath = Join-Path $stagingRoot 'app'
$docsStagingPath = Join-Path $stagingRoot 'docs'
$configSamplesStagingPath = Join-Path $stagingRoot 'config-samples'

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
Copy-DirectoryContents -SourceDirectory $publishOutputPath -DestinationDirectory $appStagingPath
Copy-DirectoryContents -SourceDirectory $docsPath -DestinationDirectory $docsStagingPath
New-Item -ItemType Directory -Path $configSamplesStagingPath -Force | Out-Null
foreach ($sample in $configSampleSpecs) {
    Copy-Item -LiteralPath $sample.Source -Destination (Join-Path $configSamplesStagingPath $sample.Target) -Force
}

$manifest = [ordered]@{
    appName = 'Ping Monitor'
    version = $devVersionDisplay
    packageVersion = $devVersionFileSafe
    buildType = 'dev'
    buildTimestampUtc = $buildTimestampUtc
    packageFileName = "$packageBaseName.zip"
    runtime = $Runtime
    commitHash = $commitHash
}

$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8NoBOM

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Fail-Build "manifest.json was not created at '$manifestPath'."
}

if (-not (Test-Path -LiteralPath $appStagingPath -PathType Container)) {
    Fail-Build "Staging app directory missing at '$appStagingPath'."
}

if (-not (Test-Path -LiteralPath $docsStagingPath -PathType Container)) {
    Fail-Build "Staging docs directory missing at '$docsStagingPath'."
}

if (-not (Test-Path -LiteralPath $configSamplesStagingPath -PathType Container)) {
    Fail-Build "Staging config-samples directory missing at '$configSamplesStagingPath'."
}

Write-Step 'Creating dev archive'
Compress-Archive -Path $stagingRoot -DestinationPath $zipPath -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    Fail-Build "Dev archive was not created at '$zipPath'."
}

Write-Step 'Generating checksum metadata'
$hashInfo = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
"$($hashInfo.Hash)  $($packageBaseName).zip" | Set-Content -LiteralPath $checksumPath -Encoding UTF8NoBOM

if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    Fail-Build "Checksum file was not created at '$checksumPath'."
}

Write-Step 'Dev package completed successfully'
Write-Info "Staging root: $stagingRoot"
Write-Info "Dev zip: $zipPath"
Write-Info "Manifest: $manifestPath"
Write-Info "Checksum: $checksumPath"
Write-Info "Commit hash: $($commitHash ?? 'unavailable')"

exit 0
