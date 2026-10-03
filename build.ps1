<#>
.SYNOPSIS
    Builds, tests and packages ASX11 Battery.

.DESCRIPTION
    Produces a self-contained win-x64 build so the output folder can be zipped and
    handed to someone with no .NET runtime installed. Self-contained is the point:
    the app is a small utility, and requiring a 200 MB runtime download before it
    will show you a battery percentage is the wrong trade.

.PARAMETER Configuration
    Debug or Release. Defaults to Release.

.PARAMETER Runtime
    Runtime identifier to publish for. Defaults to win-x64.

.PARAMETER NoTests
    Skips the test run. Use it only when iterating on packaging itself; the tests
    are the only thing standing between a wrong byte offset and a confident wrong
    number on screen.

.PARAMETER NoPublish
    Builds and tests but does not publish.

.EXAMPLE
    .\build.ps1
    Publishes to .\artifacts\publish.

.EXAMPLE
    .\build.ps1 -NoTests
    Repackages quickly after a packaging-only change.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [switch]$NoTests,

    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root 'ASX11Battery.sln'
$appProject = Join-Path $root 'src\ASX11Battery.App\ASX11Battery.App.csproj'
$testProject = Join-Path $root 'tests\ASX11Battery.Tests\ASX11Battery.Tests.csproj'
$outDir = Join-Path $root 'artifacts\publish'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Dotnet([string[]]$Arguments) {
    # Surfaces the real exit code instead of letting a non-zero result scroll past
    # in the middle of build output.
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

Write-Step "ASX11 Battery - $Configuration / $Runtime"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found on PATH. Install the .NET 9 SDK from https://dotnet.microsoft.com/download"
}

# --- A running instance holds the output DLLs open, and the copy then fails with
# --- an MSB3027 that says nothing about what is actually wrong.
$running = Get-Process -Name 'ASX11Battery.App' -ErrorAction SilentlyContinue
if (-not $running) { $running = Get-Process -Name 'ASX11Battery' -ErrorAction SilentlyContinue }
if ($running) {
    Write-Host "    stopping $($running.Count) running instance(s) so the output files are not locked" -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Write-Step 'Restoring'
Invoke-Dotnet @('restore', $solution)

Write-Step "Building ($Configuration)"
Invoke-Dotnet @('build', $solution, '-c', $Configuration, '--no-restore', '-v', 'quiet', '--nologo')

if (-not $NoTests) {
    Write-Step 'Running tests'
    Invoke-Dotnet @('test', $testProject, '-c', $Configuration, '--no-build', '--nologo')
}

if ($NoPublish) {
    Write-Step 'Skipping publish (-NoPublish)'
    return
}

Write-Step "Publishing self-contained ($Runtime)"
if (Test-Path $outDir) {
    # Not Remove-Item -Recurse on a computed path without checking where it points.
    $resolved = (Resolve-Path $outDir).Path
    if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete $resolved - it is outside the repository."
    }

    Remove-Item $resolved -Recurse -Force
}

# RuntimeIdentifier and SelfContained are build-time facts, not just publish-time
# ones. Publishing --no-build after a build that did not carry them yields a
# framework-dependent folder sitting under a self-contained-looking path, so the
# runtime build happens first and the publish reuses it.
Write-Step "Building for publish ($Runtime, self-contained)"
Invoke-Dotnet @(
    'build', $appProject,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:DebugType=none',
    '-v', 'quiet', '--nologo'
)

Write-Step 'Publishing'
Invoke-Dotnet @(
    'publish', $appProject,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '-p:DebugType=none',
    '-o', $outDir,
    '--no-build', '--nologo'
)

$exe = Join-Path $outDir 'ASX11Battery.App.exe'
if (-not (Test-Path $exe)) {
    throw "Publish reported success but $exe is missing."
}

$sizeMb = [math]::Round(((Get-ChildItem $outDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)

Write-Step 'Done'
Write-Host "    $exe  ($sizeMb MB, self-contained $Runtime)"
Write-Host ''
Write-Host '    Run it, or zip the whole folder and send it to someone.' -ForegroundColor Green
Write-Host ''