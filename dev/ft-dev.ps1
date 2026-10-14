<#
.SYNOPSIS
    Development commands for the Fitness Tracker.

.DESCRIPTION
    build      Build the solution (warnings are errors).
    test       Run every test, or only those whose full name contains -Filter.
    run        Run the web app on http://localhost:5137. The first run creates the development
               database's connection string in user-secrets (the database itself lives in
               %LOCALAPPDATA%\FitnessTracker, outside the repository).
    format     Fix formatting and code style in place.
    check      What must pass before every commit: formatting check, build, every test.
    migration  Add an EF Core migration named -Name for the latest model changes.

    Uses the per-user .NET SDK at %LOCALAPPDATA%\GravelMasterDev\dotnet when it exists
    (this PC has no dotnet on PATH), otherwise dotnet from PATH.

.EXAMPLE
    dev\ft-dev.ps1 check

.EXAMPLE
    dev\ft-dev.ps1 test -Filter ArchitectureTests

.EXAMPLE
    dev\ft-dev.ps1 migration -Name AddLocations
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('build', 'test', 'run', 'format', 'check', 'migration')]
    [string] $Command,

    # Runs only tests whose full name contains this text. Any `dotnet test --filter` expression also works.
    [string] $Filter,

    # The new migration's name, describing what changed (e.g. AddLocations).
    [string] $Name
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
if ($Name -and $Command -ne 'migration') {
    Write-Host "-Name only works with the migration command." -ForegroundColor Red
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

# Stores the development database's connection string in user-secrets the first time, pointing at
# %LOCALAPPDATA%\FitnessTracker. Real personal data then never sits inside the repository folder.
function Initialize-DevelopmentDatabase {
    $ErrorActionPreference = 'Continue'
    # Created every time: SQLite makes a missing database file, but not a missing folder.
    $folder = Join-Path $env:LOCALAPPDATA 'FitnessTracker'
    New-Item -ItemType Directory -Force $folder | Out-Null

    $secrets = & $dotnet user-secrets list --project $webProject
    if ($secrets -match '^ConnectionStrings:Default = ') { return }

    $database = Join-Path $folder 'fitness-dev.db'
    Invoke-Dotnet @('user-secrets', 'set', 'ConnectionStrings:Default', "Data Source=$database", '--project', $webProject)
    Write-Host "Development database: $database" -ForegroundColor Cyan
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
        Initialize-DevelopmentDatabase
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
    'migration' {
        if (-not $Name) {
            Write-Host "Give the migration a name: dev\ft-dev.ps1 migration -Name AddLocations" -ForegroundColor Red
            exit 1
        }
        Invoke-Dotnet @('tool', 'restore')

        # dotnet-ef starts the app's services to read the model, which needs some connection string. This one is
        # passed as an app argument (so nothing is left behind in the shell), overrides user-secrets so the real
        # database isn't touched, and is never connected to.
        $designTimeDatabase = @('--', '--ConnectionStrings:Default', 'Data Source=:memory:')

        $ErrorActionPreference = 'Continue'
        & $dotnet dotnet-ef migrations has-pending-model-changes --project $webProject @designTimeDatabase
        if ($LASTEXITCODE -eq 0) {
            Write-Host 'The model has not changed since the last migration, so no migration was added.' -ForegroundColor Yellow
            exit 0
        }

        Invoke-Dotnet (@('dotnet-ef', 'migrations', 'add', $Name, '--project', $webProject,
                '--output-dir', 'Data\Migrations') + $designTimeDatabase)
        # Generated code uses block namespaces, which the build's code-style rules reject.
        Invoke-Dotnet @('format', $solution, '--include', 'src\FitnessTracker.Web\Data\Migrations\')
    }
}
