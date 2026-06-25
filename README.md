# VaultBackpack

RocketMod plugin for Unturned — per-player persistent backpack with upgradeable size.

## Commands

| Command | Permission | Description |
|---------|------------|-------------|
| `/bp`, `/backpack` | `vaultbackpack.use` | Open your backpack |
| `/bpu`, `/backpackupgrade` | `vaultbackpack.use` | Upgrade backpack size (+1 row) |
| `/bpset <player> <w> <h>` | `vaultbackpack.admin` | Admin: set exact size for a player |

## Features

- **Persistent storage** — items saved to MySQL, survives server restarts
- **Upgradeable size** — pay items + coins per level to expand by 1 row
- **Death drop** — backpack contents drop as a loot box at death location; backpack is cleared
- **Ban drop** — same as death drop, triggered on ban
- **No physical box** — opens as virtual storage UI, no barricade spawned in the world

## Upgrade Cost

Cost scales with current level: `base × level`

| Level | Items | Coins |
|-------|-------|-------|
| 1 → 2 | 1× | 500 |
| 2 → 3 | 2× | 1,000 |
| 3 → 4 | 3× | 1,500 |
| … | … | … |

If items or coins are insufficient, everything is returned automatically.

## Configuration

```xml
<DefaultWidth>5</DefaultWidth>
<DefaultHeight>5</DefaultHeight>
<MaxHeight>15</MaxHeight>

<UpgradeCoinsCost>500</UpgradeCoinsCost>
<UpgradeItemId>6114</UpgradeItemId>
<UpgradeItemAmount>1</UpgradeItemAmount>

<VaultDeadboxBarricadeId>6599</VaultDeadboxBarricadeId>
```

| Field | Description |
|-------|-------------|
| `DefaultWidth / DefaultHeight` | Starting size for new players |
| `MaxHeight` | Max rows (0 = unlimited) |
| `UpgradeCoinsCost` | Base coin cost per upgrade (multiplied by level) |
| `UpgradeItemId` | Item ID required for upgrade (0 = no item required) |
| `UpgradeItemAmount` | Base item amount per upgrade (multiplied by level) |
| `VaultDeadboxBarricadeId` | Barricade ID for death drop box (0 = disabled) |

## Database

Table `sv_vault_players`:

| Column | Type | Description |
|--------|------|-------------|
| `steam_id` | BIGINT | Player Steam ID |
| `width` | TINYINT | Current backpack width |
| `height` | TINYINT | Current backpack height |
| `items_data` | LONGTEXT | Serialized item data |

Coins are read from `sv_coins` (shared with other plugins).

## Build

Requires Roslyn `csc.exe` (Visual Studio 2022). Edit paths in `build.cmd` then run:

```
build.cmd
```

Output: `dist/VaultBackpack.dll` + `dist/MySql.Data.dll`

## Deploy

Copy both files from `dist/` to your server's `Rocket/Plugins/` folder, then restart.
