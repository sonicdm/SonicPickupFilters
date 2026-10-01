param(
    [string]$LibDir = "E:\Scripts\Valheim Mods\Reqs",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$PluginsDir = "$env:AppData\com.kesomannen.gale\valheim\profiles\Default\BepInEx\plugins",
    [string]$ScriptsDir = "$env:AppData\com.kesomannen.gale\valheim\profiles\Default\BepInEx\scripts"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $ProjectRoot "build.ps1") -LibDir $LibDir -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed" }

$Dll = Join-Path $ProjectRoot "bin\$Configuration\SonicPickupFilters.dll"
if (-not (Test-Path -LiteralPath $Dll)) { throw "DLL missing: $Dll" }

$PluginFolder = Join-Path $PluginsDir "SonicPickupFilters"
New-Item -ItemType Directory -Force -Path $PluginFolder | Out-Null

$DestDll = Join-Path $PluginFolder "SonicPickupFilters.dll"
$DestPdb = Join-Path $PluginFolder "SonicPickupFilters.pdb"
$DisabledDll = $DestDll + ".disabled"

# Re-enable if a prior ScriptEngine workflow left the plugins copy disabled.
if ((-not (Test-Path -LiteralPath $DestDll)) -and (Test-Path -LiteralPath $DisabledDll)) {
    Move-Item -LiteralPath $DisabledDll -Destination $DestDll -Force
    Write-Host "Re-enabled: $DestDll"
}

Copy-Item -LiteralPath $Dll -Destination $DestDll -Force
$Pdb = Join-Path $ProjectRoot "bin\$Configuration\SonicPickupFilters.pdb"
if (Test-Path -LiteralPath $Pdb) {
    Copy-Item -LiteralPath $Pdb -Destination $DestPdb -Force
}

# Remove ScriptEngine scripts copies so only plugins\ loads.
foreach ($name in @("SonicPickupFilters.dll", "SonicPickupFilters.pdb")) {
    $scriptPath = Join-Path $ScriptsDir $name
    if (Test-Path -LiteralPath $scriptPath) {
        Remove-Item -LiteralPath $scriptPath -Force
        Write-Host "Removed scripts copy: $scriptPath"
    }
}

Write-Host "Deployed (plugins): $DestDll"
Write-Host "Restart Valheim to load (normal BepInEx plugin — no ScriptEngine / F6)."
Write-Host "Console: spf_dump"
