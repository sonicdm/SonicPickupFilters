# Sonic Pickup Filters

Valheim BepInEx mod for per-item auto-pickup rules: **Allow**, **Block**, or **Cap**.

## Identity

| | |
| --- | --- |
| Author | SonicDM |
| Plugin name | `Sonic Pickup Filters` |
| Plugin GUID | `com.sonicdm.valheim.sonicpickupfilters` |
| Assembly | `SonicPickupFilters.dll` |
| Version | `0.1.1` |
| Config file | `BepInEx/config/com.sonicdm.valheim.sonicpickupfilters.cfg` |
| Source | https://github.com/sonicdm/SonicPickupFilters |

## Features

- **Allow** — vanilla auto-pickup (default). Block unchecked.
- **Block** — check Block with Cap empty: never auto-pick that prefab; manual Use still works.
- **Cap** — check Block and set Cap to N: auto-pick only while inventory count is below N. Cap alone (Block unchecked) does nothing.
- **Pickup Filter panel** — hotkey `L` (configurable) or inventory bar button. Table of known items with Block + Cap columns, search, and category filter.
- **Tabs** — All / Blocked / Capped (with counts).
- **Clear** — resets search and category filter.
- **Inventory** — hold modifier (default LeftAlt) and **right-click** an item to toggle Block.
- Client-only; other players do not need the mod.

## Configuration

- `[General]` Enabled, DebugLogging
- `[Rules]` ItemRules (`Prefab:0` = Block, `Prefab:N` = Cap), DefaultCapUsesMaxStack
- `[UI]` ToggleKey, InventoryBlockModifier
- `[General]` AutoReloadConfig (reload `.cfg` while in-game), Enabled (auto-pickup only — UI/hotkey always work)

Example `ItemRules`:

```text
BlackMetalScrap:0,Wood:50,Stone:100
```

## Installation

1. Install **BepInExPack** and **Jötunn**.
2. Place `SonicPickupFilters.dll` under `BepInEx/plugins/`.

## Build

```powershell
.\build.ps1
```
