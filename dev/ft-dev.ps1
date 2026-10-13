<#
.SYNOPSIS
    Development commands for the Fitness Tracker.

.DESCRIPTION
    build   Build the solution (warnings are errors).
    test    Run every test, or only those whose full name contains -Filter.
    run     Run the web app on http://localhost:5137.
    format  Fix formatting and code style in place.
    check   What must pass before every commit: formatting check, build, every test.

    Uses the per-user .NET SDK at %LOCALAPPDATA%\GravelMasterDev\dotnet when it exists
    (this PC has no dotnet on PATH), otherwise dotnet from PATH.

.EXAMPLE
    dev\ft-dev.ps1 check

.EXAMPLE
    dev\ft-dev.ps1 test -Filter ArchitectureTests
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('build', 'test', 'run', 'format', 'check')]
    [string] $Command,

    # Runs only tests whose full name contains this text. Any `dotnet test --filter` expression also works.
    [string] $Filter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'FitnessTracker.slnx'
$webProject = Join-Path $repoRoot 'src\FitnessTracker.Web\FitnessTracker.Web.csproj'

function Get-DotnetPath {
    $perUser = Join-Path $env:LOCALAPPDATA 'GravelMasterDev\dotnet\dotnet.exe'
    if (Test-Path $perUser) { return $perUser }

    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    Write-Host "Can't find the .NET SDK. Install .NET SDK 10.0.401 or later (see global.json)." -ForegroundColor Red
    exit 1
}

if ($Filter -and $Command -ne 'test') {
    Write-Host "-Filter only works with the test command." -ForegroundColor Red
    exit 1
}

$dotnet = Get-DotnetPath
# Quieter output, and no usage data sent to Microsoft from these commands.
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# A test run where nothing ran (a mistyped -Filter, or tests that stopped being discovered) is a failure, not a pass.
$noTestsIsAnError = @('--', 'RunConfiguration.TreatNoTestsAsError=true')

function Invoke-Dotnet {
    param([string[]] $Arguments)

    # dotnet's own messages decide success (via its exit code), even if the caller redirects stderr.
    $ErrorActionPreference = 'Continue'
    Write-Host ('> dotnet ' + ($Arguments -join ' ')) -ForegroundColor DarkGray
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: dotnet $($Arguments[0]) exited with code $LASTEXITCODE." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

switch ($Command) {
    'build' {
        Invoke-Dotnet @('build', $solution)
    }
    'test' {
        $testArguments = @('test', $solution)
        if ($Filter) { $testArguments += @('--filter', $Filter) }
        Invoke-Dotnet ($testArguments + $noTestsIsAnError)
    }
    'run' {
        Invoke-Dotnet @('run', '--project', $webProject, '--launch-profile', 'http')
    }
    'format' {
        Invoke-Dotnet @('format', $solution)
    }
    'check' {
        Write-Host '1/3 Formatting and code style' -ForegroundColor Cyan
        Invoke-Dotnet @('format', $solution, '--verify-no-changes')
        Write-Host '2/3 Build (warnings are errors)' -ForegroundColor Cyan
        Invoke-Dotnet @('build', $solution)
        Write-Host '3/3 Tests' -ForegroundColor Cyan
        Invoke-Dotnet (@('test', $solution, '--no-build') + $noTestsIsAnError)
        Write-Host 'check passed: formatting, build and every test.' -ForegroundColor Green
    }
}
