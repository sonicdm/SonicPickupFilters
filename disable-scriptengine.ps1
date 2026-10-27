# Forwards to the workspace tool. Do not add disable logic here.
param(
    [string]$ProfileBepInEx = "$env:AppData\com.kesomannen.gale\valheim\profiles\Default\BepInEx"
)

$ErrorActionPreference = "Stop"
$tool = Join-Path (Split-Path -Parent $PSScriptRoot) "tools\dev.ps1"
if (-not (Test-Path -LiteralPath $tool)) {
    throw "Workspace dev tool not found: $tool"
}

& $tool disable -Mod $PSScriptRoot -ProfileBepInEx $ProfileBepInEx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
