# Sonic Pickup Filters publish test matrix

Manual checks before a `v*` tag. Everyday edits stop at `.\build.ps1` and ScriptEngine deploy.
Procedures stay the same across versions. Copy this section forward for a new version and reset results to `pending`.

## How to run

1. `.\build.ps1` then `..\tools\dev.ps1 deploy` (ScriptEngine). F9 reload after each fix.
2. Work the checks below in order. Use ValheimMCP for log/console/render rows while you play.
3. Logs: `C:\Users\Allan\AppData\Roaming\com.kesomannen.gale\valheim\profiles\Default\BepInEx\LogOutput.log` and `ValheimConsole.log`.
4. After ScriptEngine passes: `..\tools\dev.ps1 deploy -Plugins`, restart Valheim, re-check S01–S03.
5. Mark results `pass` / `fail`. Tag only when every result under the version heading is `pass`.

## Procedures

### S01 — BepInEx load line
**Rule:** Plugin loads for this version.
1. Deploy the build under test.
2. Enter a world.
3. Open `LogOutput.log` (or ValheimMCP `get_log` with contains `Sonic Pickup Filters`).
4. **Pass:** `Loading [Sonic Pickup Filters 0.1.1]` (or tagged version) and a `loaded` info line.
5. **Fail:** missing load line or immediate exception from this mod.

### S02 — No exception in Gale logs
**Rule:** Clean log for this mod after the session.
1. Search `LogOutput.log` and `ValheimConsole.log` for `SonicPickupFilters` / `Sonic Pickup Filters` / exceptions.
2. **Pass:** no exception stack owned by this mod.
3. **Fail:** any exception from this mod.

### S03 — Config applies
**Rule:** BepInEx config still drives behavior.
1. Toggle a visible setting (panel key, DebugLogging, or Cap default).
2. Reload ScriptEngine if needed.
3. **Pass:** setting change is observed.
4. **Fail:** no effect.

### R01 — Auto-pickup only
**Rule:** Block/Cap apply to auto-pickup only; manual Use still works.
1. Block an item in the panel.
2. Walk over a ground pile of that item (auto-pickup).
3. Manually Use a drop of the same item.
4. **Pass:** auto-pickup is blocked; manual Use still picks up.
5. **Fail:** manual Use is blocked, or Block does nothing to auto-pickup.

### R02 — Cap by inventory count
**Rule:** Cap is inventory count by shared item token; oversized piles shrink; leftover stays on ground.
1. Set Cap for an item to a small number.
2. Auto-pickup a larger stack than remaining Cap room.
3. **Pass:** inventory reaches Cap; leftover remains on ground; no crash/InvalidCast.
4. **Fail:** Cap ignored, full vacuum, or InvalidCast.

### R03 — Allow bypasses Cap/Block
**Rule:** Allow items never enter Cap/Block logic.
1. Set an item to Allow while Cap/Block exist for others.
2. Auto-pickup the Allow item.
3. **Pass:** Allow item is picked normally regardless of Cap/Block on other items.
4. **Fail:** Allow item is capped or blocked.

### R04 — Discovery list sources
**Rule:** Discovery list uses ObjectDB + known materials + ItemType; Jötunn is GUI only.
1. Open the panel and search for a known vanilla item and an unknown one.
2. **Pass:** known/vanilla items appear; panel still works without relying on another mod’s item DB.
3. **Fail:** panel empty or missing vanilla items that ObjectDB knows.

### R05 — Inventory Block toggle
**Rule:** Modifier + right-click on player inventory grid toggles Block.
1. Open inventory. Hold the configured modifier and right-click an item.
2. **Pass:** that item toggles Block in the filter list.
3. **Fail:** no toggle, or wrong inventory (chest) is required.

### R06 — Headless skip
**Rule:** Client-only; skip Harmony/GUI when headless.
1. If a dedicated/headless instance is available, check log for skipped GUI/patches.
2. **Pass:** no GUI crash on headless.
3. **Fail:** headless throws during GUI/Harmony setup.
4. Without headless: note `code-review` in Notes after confirming the guard remains.

## 0.1.1

| ID | Check | Result | Notes |
| --- | --- | --- | --- |
| S01 | BepInEx load line | pass | ScriptEngine F9 session; user confirmed |
| S02 | No exception in Gale logs | pass | User confirmed clean session |
| S03 | Config applies | pass | AutoReloadConfig + Cap/Block live |
| R01 | Auto-pickup only | pass | User confirmed |
| R02 | Cap by inventory count | pass | Cap checkbox + qty UX verified |
| R03 | Allow bypasses Cap/Block | pass | User confirmed |
| R04 | Discovery list sources | pass | Panel list + Cap columns verified |
| R05 | Inventory Block toggle | pass | User confirmed |
| R06 | Headless skip | pass | code-review (GUIManager.IsHeadless guard) |

## 0.1.0

| ID | Check | Result | Notes |
| --- | --- | --- | --- |
| S01 | BepInEx load line | pass | Shipped v0.1.0 |
| S02 | No exception in Gale logs | pass | Shipped v0.1.0 |
| S03 | Config applies | pass | Shipped v0.1.0 |
| R01 | Auto-pickup only | pass | Shipped v0.1.0 |
| R02 | Cap by inventory count | pass | Shipped v0.1.0 |
| R03 | Allow bypasses Cap/Block | pass | Shipped v0.1.0 |
| R04 | Discovery list sources | pass | Shipped v0.1.0 |
| R05 | Inventory Block toggle | pass | Shipped v0.1.0 |
| R06 | Headless skip | pass | code-review |
