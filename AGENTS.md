# AGENTS.md — Sonic Pickup Filters

Guidance for Cursor agents (and humans) working in this repo.

## What this is

Valheim **BepInEx** mod: per-item auto-pickup Allow / Block / Cap, with an in-game panel and inventory modifier+right-click Block toggle.

| | |
| --- | --- |
| GUID | `com.sonicdm.valheim.sonicpickupfilters` |
| Assembly | `SonicPickupFilters.dll` |
| Source | `src/` |

## Product rules

- Block/Cap apply to **auto-pickup only**. Never patch `ItemDrop.CanPickup` for rules (manual Use must keep working).
- Cap is inventory **count** by shared item token (`Inventory.CountItems`). Oversized piles are shrunk to the remaining Cap room (leftover stays on the ground). Avoid `ItemData.Clone` Cap-splits (InvalidCast under some loaders).
- Auto-pickup filtering: Prefix clears `m_autoPickup` on suppressed drops; Postfix restores. Allow items never enter Cap/Block logic.
- Discovery list: vanilla `ObjectDB` + `Player.m_knownMaterial` + `ItemType`. Jötunn is for GUI/timing only.
- Inventory Block toggle: configured modifier + **right-click** on the player inventory grid (`OnRightClickItem`).
- Client-only (`NotEnforced`). Skip Harmony/GUI when `GUIManager.IsHeadless()`.

## Valheim references (local only)

```text
E:\Scripts\Valheim Mods\Reqs
```

Override with `-LibDir` on `build.ps1` / `package.ps1` / `release.ps1`.

- Never commit those DLLs or the Reqs folder.
- GitHub Actions only refreshes release notes from `CHANGELOG.md` on tag push.

## Build habits

```powershell
.\build.ps1
.\deploy-dev.ps1   # optional: copy Release DLL into Gale Default profile plugins\
```

After code changes, verify with `.\build.ps1` before finishing. Prefer **build only** while iterating — do not package/release unless asked.

**Normal install:** `deploy-dev.ps1` copies to `BepInEx/plugins/SonicPickupFilters/` and clears any `scripts\` copy. **Restart Valheim** to load (no hot-reload).

ScriptEngine was a temporary local helper. To tear it down: `.\disable-scriptengine.ps1` (disables the plugin, unbinds reload key, turns off LoadOnStart/watcher). Re-enable later only if needed via `.\install-scriptengine.ps1`.

Console: **`spf_dump`** — nearby Cap/Block dump without a rebuild.

## Version bumps

Stay on **`0.1.x`** while iterating. Do not call it **`1.0.0`** until the user says the mod is feature-complete and ready to ship.

When shipping, keep these aligned: `PluginVersion`, csproj `<Version>`, `manifest.json` `version_number`, README Version row, `CHANGELOG.md` `## X.Y.Z`.

## Install

Do **not** copy the DLL into an r2modman profile. User installs themselves. Build output: `bin\Release\SonicPickupFilters.dll` and `dist\SonicPickupFilters.dll`.

## Troubleshooting

Player BepInEx log (Gale / r2modman Default profile):

```text
C:\Users\Allan\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Default\BepInEx\LogOutput.log
```

On crash / UI bugs, read the end of that file first (search `SonicPickupFilters`, `NullReferenceException`, `Exception`).
