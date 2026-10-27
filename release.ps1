#Requires -Version 5.1
<#
.SYNOPSIS
  Local release prep for SonicPickupFilters: validate versions, package, tag, and push.
  GitHub Actions publish.yml verifies tests + test-matrix, then uploads to Hexium/Thunderstore
  and creates the GitHub Release notes.

.PARAMETER SkipPackage
  Use an existing Thunderstore zip in dist\ (you already ran package.ps1).

.PARAMETER SkipPush
  Create the local tag and print commands, but do not push.

.PARAMETER DryRun
  Validate and print what would happen; make no git changes.
#>
param(
    [string]$LibDir = "E:\Scripts\Valheim Mods\Reqs",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipPackage,
    [switch]$SkipPush,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectRoot

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$ManifestPath = Join-Path $ProjectRoot "manifest.json"
$PluginSrc = Join-Path $ProjectRoot "src\SonicPickupFiltersPlugin.cs"
$CsprojPath = Join-Path $ProjectRoot "SonicPickupFilters.csproj"
$MatrixPath = Join-Path $ProjectRoot "docs\test-matrix.md"

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$manifest.version_number
Assert-True ($version -match '^\d+\.\d+\.\d+$') "Invalid manifest version_number: $version"

$pluginSrcRaw = Get-Content -LiteralPath $PluginSrc -Raw -Encoding UTF8
Assert-True ($pluginSrcRaw -match 'PluginVersion\s*=\s*"(\d+\.\d+\.\d+)"') "Could not find PluginVersion in source"
$pluginVersion = $Matches[1]
Assert-True ($pluginVersion -eq $version) "Version mismatch: manifest=$version PluginVersion=$pluginVersion"

$csprojRaw = Get-Content -LiteralPath $CsprojPath -Raw -Encoding UTF8
Assert-True ($csprojRaw -match '<Version>(\d+\.\d+\.\d+)</Version>') "Could not find <Version> in csproj"
$csprojVersion = $Matches[1]
Assert-True ($csprojVersion -eq $version) "Version mismatch: manifest=$version csproj=$csprojVersion"

Assert-True (Test-Path -LiteralPath $MatrixPath) "Missing docs/test-matrix.md"
python (Join-Path $ProjectRoot ".github\scripts\verify-test-matrix.py") --version $version
if ($LASTEXITCODE -ne 0) {
    throw "docs/test-matrix.md must have an all-pass section for $version before release."
}

$modName = [string]$manifest.name
$tag = "v$version"
$zipName = "$modName-$version.zip"
$zipPath = Join-Path $ProjectRoot "dist\$zipName"

Write-Host "Release target: $tag ($modName $version)" -ForegroundColor Cyan
Write-Host "Store upload + GitHub Release notes run on tag via .github/workflows/publish.yml"

if (-not $SkipPackage) {
    if ($DryRun) {
        Write-Host "[dry-run] Would run package.ps1"
    }
    else {
        & (Join-Path $ProjectRoot "package.ps1") -LibDir $LibDir -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw "package.ps1 failed with exit code $LASTEXITCODE" }
    }
}

Assert-True (Test-Path -LiteralPath $zipPath) "Missing package zip: $zipPath (run package.ps1 or omit -SkipPackage)"

if ($DryRun) {
    Write-Host "[dry-run] Would tag $tag and push (Actions publishes stores + notes)"
    exit 0
}

Assert-True (Test-Path -LiteralPath (Join-Path $ProjectRoot ".git")) "Not a git repository. Initialize and push to GitHub first."
$status = git status --porcelain
if ($status) {
    Write-Host $status
    throw "Working tree is not clean. Commit or stash before releasing."
}

$existingTag = git tag -l $tag
if ($existingTag) {
    throw "Tag $tag already exists locally. Delete it or bump the version."
}

$branch = (git rev-parse --abbrev-ref HEAD).Trim()

Write-Host "Creating annotated tag $tag on $branch..."
git tag -a $tag -m "Release $version"

if ($SkipPush) {
    Write-Host "SkipPush: tag created locally. Push with:"
    Write-Host "  git push origin $branch"
    Write-Host "  git push origin $tag"
    Write-Host "Then watch Actions publish.yml for Hexium/Thunderstore + GitHub Release."
    exit 0
}

Write-Host "Pushing $branch and $tag..."
git push -u origin $branch
if ($LASTEXITCODE -ne 0) { throw "git push branch failed" }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push tag failed" }

Write-Host "Tag pushed. Actions publish.yml will verify, upload stores, and write GitHub Release notes." -ForegroundColor Green
Write-Host "Local zip (for inspection): $zipPath"
