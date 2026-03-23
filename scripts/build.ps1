[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$SkipPythonChecks
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

function Get-PythonCommand {
    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($pythonCommand) {
        return @($pythonCommand.Source)
    }

    $pyCommand = Get-Command py -ErrorAction SilentlyContinue
    if ($pyCommand) {
        return @($pyCommand.Source, '-3')
    }

    return $null
}

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDirectory '..')).Path
Set-Location $repoRoot

$globalJsonPath = Join-Path $repoRoot 'global.json'
$webProjectPath = Join-Path $repoRoot 'src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj'
$agentRoot = Join-Path $repoRoot 'src/Agent'

if (-not (Test-Path -LiteralPath $webProjectPath)) {
    Fail-Build "Expected web project was not found at '$webProjectPath'."
}

Write-Step 'Starting repository build'
Write-Info "Repository root: $repoRoot"
Write-Info "Configuration: $Configuration"
Write-Info "Web project: $webProjectPath"

Write-Step 'Checking .NET tooling'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCommand) {
    Fail-Build 'The dotnet CLI is required but was not found on PATH.'
}

Invoke-External -FilePath $dotnetCommand.Source -Arguments @('--info')

if (Test-Path -LiteralPath $globalJsonPath) {
    Write-Info "Using pinned SDK configuration from $globalJsonPath"
    $globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
    $requiredSdkVersion = $globalJson.sdk.version
    if (-not $requiredSdkVersion) {
        Fail-Build "global.json exists but does not define sdk.version."
    }

    $installedSdks = & $dotnetCommand.Source --list-sdks
    if ($LASTEXITCODE -ne 0) {
        Fail-Build 'Unable to list installed .NET SDKs.'
    }

    if (-not ($installedSdks | Where-Object { $_ -like "$requiredSdkVersion*" })) {
        Fail-Build "Required .NET SDK version $requiredSdkVersion from global.json is not installed."
    }

    Write-Info "Verified required .NET SDK version $requiredSdkVersion is installed"
} else {
    Write-Info 'No global.json found; dotnet will use the default SDK selection'
}

Write-Step 'Restoring the web application'
Invoke-External -FilePath $dotnetCommand.Source -Arguments @('restore', $webProjectPath)

Write-Step 'Building the web application'
Invoke-External -FilePath $dotnetCommand.Source -Arguments @('build', $webProjectPath, '--configuration', $Configuration, '--no-restore')

$testProjects = @(Get-ChildItem -Path $repoRoot -Recurse -Filter '*.csproj' -File |
    Where-Object {
        $_.FullName -ne $webProjectPath -and (
            $_.BaseName -match '(^|\.)Tests?$' -or
            $_.DirectoryName -match '([\\/])tests?([\\/]|$)'
        )
    } |
    Sort-Object FullName)

if ($SkipTests) {
    Write-Step 'Skipping .NET tests'
    Write-Info 'Tests were skipped because -SkipTests was specified'
} elseif ($testProjects.Count -eq 0) {
    Write-Step 'Checking for .NET test projects'
    Write-Info 'No .NET test projects were found; continuing without running tests'
} else {
    Write-Step 'Running .NET tests'
    foreach ($testProject in $testProjects) {
        Write-Info "Testing $($testProject.FullName)"
        Invoke-External -FilePath $dotnetCommand.Source -Arguments @('test', $testProject.FullName, '--configuration', $Configuration, '--no-build', '--verbosity', 'minimal')
    }
}

if ($SkipPythonChecks) {
    Write-Step 'Skipping Python syntax checks'
    Write-Info 'Python checks were skipped because -SkipPythonChecks was specified'
} else {
    Write-Step 'Running Python syntax checks'
    if (-not (Test-Path -LiteralPath $agentRoot)) {
        Fail-Build "Expected agent directory was not found at '$agentRoot'."
    }

    $pythonCommand = @(Get-PythonCommand)
    if (-not $pythonCommand) {
        Fail-Build 'Python was not found. Install Python or rerun with -SkipPythonChecks.'
    }

    $pythonFiles = @(Get-ChildItem -Path $agentRoot -Recurse -Filter '*.py' -File | Sort-Object FullName)
    if ($pythonFiles.Count -eq 0) {
        Write-Info 'No Python files were found under src/Agent; nothing to check'
    } else {
        Write-Info "Checking $($pythonFiles.Count) Python file(s) under $agentRoot"
        $pythonExecutable = $pythonCommand[0]
        $pythonArgs = @()
        if ($pythonCommand.Count -gt 1) {
            $pythonArgs += $pythonCommand[1..($pythonCommand.Count - 1)]
        }

        foreach ($pythonFile in $pythonFiles) {
            Write-Info "Compiling $($pythonFile.FullName)"
            Invoke-External -FilePath $pythonExecutable -Arguments ($pythonArgs + @('-m', 'py_compile', $pythonFile.FullName))
        }
    }
}

Write-Step 'Build completed successfully'
Write-Info "Built web project: $webProjectPath"
if ($SkipTests) {
    Write-Info 'Tests: skipped by parameter'
} elseif ($testProjects.Count -eq 0) {
    Write-Info 'Tests: no .NET test projects found'
} else {
    Write-Info "Tests: completed for $($testProjects.Count) project(s)"
}

if ($SkipPythonChecks) {
    Write-Info 'Python checks: skipped by parameter'
} else {
    Write-Info 'Python checks: completed'
}

exit 0
