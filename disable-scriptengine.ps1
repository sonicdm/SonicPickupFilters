param(
    [string]$ProfileBepInEx = "$env:AppData\com.kesomannen.gale\valheim\profiles\Default\BepInEx"
)

$ErrorActionPreference = "Stop"

$ConfigDir = Join-Path $ProfileBepInEx "config"
$Scripts = Join-Path $ProfileBepInEx "scripts"
$SePluginDir = Join-Path $ProfileBepInEx "plugins\BepInEx-ScriptEngine"
$CfgPath = Join-Path $ConfigDir "com.bepis.bepinex.scriptengine.cfg"

# Disable the ScriptEngine plugin binary so it cannot bind F6/F9 or load scripts.
$SeDll = Join-Path $SePluginDir "ScriptEngine.dll"
$SeDisabled = Join-Path $SePluginDir "ScriptEngine.dll.disabled"
if (Test-Path -LiteralPath $SeDll) {
    Move-Item -LiteralPath $SeDll -Destination $SeDisabled -Force
    Write-Host "Disabled ScriptEngine plugin: $SeDisabled"
} elseif (Test-Path -LiteralPath $SeDisabled) {
    Write-Host "ScriptEngine already disabled: $SeDisabled"
} else {
    Write-Host "No ScriptEngine.dll found under $SePluginDir"
}

# Persist safe cfg if the file is still around (LoadOnStart off, no reload key, no watcher).
@"
## Settings file was created by plugin Script Engine v11.1
## Plugin GUID: com.bepis.bepinex.scriptengine
## Disabled for normal SonicPickupFilters plugins\ installs.

[AutoReload]

## Watches the scripts directory for file changes and automatically reloads all plugins if any of the files gets changed (added/removed/modified).
# Setting type: Boolean
# Default value: false
EnableFileSystemWatcher = false

## Delay in seconds from detecting a change to files in the scripts directory to plugins being reloaded. Affects only EnableFileSystemWatcher.
# Setting type: Single
# Default value: 3
AutoReloadDelay = 3

## If enabled, BepInEx will save patched assemblies & symbols into BepInEx/ScriptEngineDumpedAssemblies.
## This can be used by developers to inspect and debug plugins loaded by ScriptEngine.
# Setting type: Boolean
# Default value: false
DumpAssemblies = false

[General]

## Load all plugins from the scripts folder when starting the application.
# Setting type: Boolean
# Default value: false
LoadOnStart = false

## Press this key to reload all the plugins from the scripts folder
# Setting type: KeyboardShortcut
# Default value: F6
# Unbound while ScriptEngine is disabled for normal installs.
ReloadKey =

## Disable all logging except for error messages.
# Setting type: Boolean
# Default value: false
QuietMode = false

## Also load plugins from subdirectories of the scripts folder.
# Setting type: Boolean
# Default value: false
IncludeSubdirectories = false
"@ | Set-Content -LiteralPath $CfgPath -Encoding UTF8
Write-Host "Config: $CfgPath (LoadOnStart=false, watcher off, ReloadKey unbound)"

# Clear ScriptEngine-loaded mod copies.
if (Test-Path -LiteralPath $Scripts) {
    Get-ChildItem -LiteralPath $Scripts -Filter "SonicPickupFilters.*" -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force
            Write-Host "Removed: $($_.FullName)"
        }
}

Write-Host "Done. Restart Valheim. SonicPickupFilters should load only from plugins\."
