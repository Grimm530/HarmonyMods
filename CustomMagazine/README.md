# CustomMagazine (Harmony)

Oxide **CustomMagazine 1.0.9** port. Skin-based extended magazine capacity.

## Deploy

```powershell
.\build.ps1
```

Copies **only** `CustomMagazine.dll` to `HarmonyMods\CustomMagazine.dll`.

## Paths

| Kind | Path |
|------|------|
| Config | `HarmonyConfig/CustomMagazine.json` |

## Console

```
givemagazine <skinid> <steamid>
```

Server console only.

## Behavior

- Magazines with configured SkinIDs spawn in listed crates (`LootContainer.SpawnLoot` postfix).
- Reload / mod-change applies `Ammo Multiplier` to magazine capacity.
- Items with different skins do not stack; split preserves custom name/skin.
