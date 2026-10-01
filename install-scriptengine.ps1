param(
    [string]$ProfileBepInEx = "$env:AppData\com.kesomannen.gale\valheim\profiles\Default\BepInEx",
    [string]$ReloadKey = "F9"
)

$ErrorActionPreference = "Stop"

$Plugins = Join-Path $ProfileBepInEx "plugins\BepInEx-ScriptEngine"
$Scripts = Join-Path $ProfileBepInEx "scripts"
$ConfigDir = Join-Path $ProfileBepInEx "config"
$Tmp = Join-Path $env:TEMP "ScriptEngine_install"
New-Item -ItemType Directory -Force -Path $Plugins, $Scripts, $Tmp | Out-Null

Write-Host "Downloading BepInEx.Debug ScriptEngine r11.1..."
gh release download r11.1 -R BepInEx/BepInEx.Debug -p "ScriptEngine_r11.1.zip" -D $Tmp --clobber
Expand-Archive -LiteralPath (Join-Path $Tmp "ScriptEngine_r11.1.zip") -DestinationPath $Tmp -Force
Get-ChildItem $Tmp -Recurse -Filter "ScriptEngine.dll" | ForEach-Object {
    Copy-Item $_.FullName -Destination (Join-Path $Plugins "ScriptEngine.dll") -Force
    Write-Host "Installed -> $Plugins"
}

# Real GUID is com.bepis.bepinex.scriptengine (not org.bepinex...)
$CfgPath = Join-Path $ConfigDir "com.bepis.bepinex.scriptengine.cfg"
@"
## Settings file was created by plugin Script Engine v11.1
## Plugin GUID: com.bepis.bepinex.scriptengine

[AutoReload]

## Watches the scripts directory for file changes and automatically reloads all plugins if any of the files gets changed (added/removed/modified).
# Setting type: Boolean
# Default value: false
EnableFileSystemWatcher = true

## Delay in seconds from detecting a change to files in the scripts directory to plugins being reloaded. Affects only EnableFileSystemWatcher.
# Setting type: Single
# Default value: 3
AutoReloadDelay = 1.5

## If enabled, BepInEx will save patched assemblies & symbols into BepInEx/ScriptEngineDumpedAssemblies.
## This can be used by developers to inspect and debug plugins loaded by ScriptEngine.
# Setting type: Boolean
# Default value: false
DumpAssemblies = false

[General]

## Load all plugins from the scripts folder when starting the application.
# Setting type: Boolean
# Default value: false
LoadOnStart = true

## Press this key to reload all the plugins from the scripts folder
# Setting type: KeyboardShortcut
# Default value: F6
ReloadKey = $ReloadKey

## Disable all logging except for error messages.
# Setting type: Boolean
# Default value: false
QuietMode = false

## Also load plugins from subdirectories of the scripts folder.
# Setting type: Boolean
# Default value: false
IncludeSubdirectories = true
"@ | Set-Content -LiteralPath $CfgPath -Encoding UTF8

Remove-Item (Join-Path $ConfigDir "org.bepinex.plugins.scriptengine.cfg") -Force -ErrorAction SilentlyContinue

Write-Host "Config: $CfgPath"
Write-Host "  LoadOnStart=true  ReloadKey=$ReloadKey  FileSystemWatcher=true"
Write-Host "Next: .\deploy-dev.ps1 -DisablePluginsCopy   then restart Valheim once."
Write-Host "Log should show: Loading [Sonic Pickup Filters ...] from ScriptEngine / scripts."
