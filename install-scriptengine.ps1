# Forwards to the workspace tool. Do not add install logic here.
param(
    [string]$ProfileBepInEx = "$env:AppData\com.kesomannen.gale\valheim\profiles\Default\BepInEx",
    [string]$ReloadKey = "F9"
)

$ErrorActionPreference = "Stop"
$tool = Join-Path (Split-Path -Parent $PSScriptRoot) "tools\dev.ps1"
if (-not (Test-Path -LiteralPath $tool)) {
    throw "Workspace dev tool not found: $tool"
}

& $tool install -ProfileBepInEx $ProfileBepInEx -ReloadKey $ReloadKey
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
