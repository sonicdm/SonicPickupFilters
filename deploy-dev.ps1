# Forwards to the workspace tool. Do not add deploy logic here.
param(
    [string]$LibDir = "E:\Scripts\Valheim Mods\Reqs",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$PluginsDir = "$env:AppData\com.kesomannen.gale\valheim\profiles\Default\BepInEx\plugins",
    [string]$ScriptsDir = "$env:AppData\com.kesomannen.gale\valheim\profiles\Default\BepInEx\scripts",
    [switch]$ScriptEngine,
    [switch]$Plugins
)

$ErrorActionPreference = "Stop"
if ($ScriptEngine -and $Plugins) {
    throw "Pass only one of -ScriptEngine or -Plugins."
}

$tool = Join-Path (Split-Path -Parent $PSScriptRoot) "tools\dev.ps1"
if (-not (Test-Path -LiteralPath $tool)) {
    throw "Workspace dev tool not found: $tool"
}

$forward = @{
    Command       = "deploy"
    Mod           = $PSScriptRoot
    LibDir        = $LibDir
    Configuration = $Configuration
    PluginsDir    = $PluginsDir
    ScriptsDir    = $ScriptsDir
}
if ($Plugins) {
    $forward.Plugins = $true
}

& $tool @forward
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
