# Quest (Harmony OxideCompat port)

Port of Oxide `Quest` 8.6.8. CUI quest list, daily/repeatable quests, rewards, Discord stats.

## Paths

| | |
|---|---|
| Config | `HarmonyConfig/Quest.json` |
| Data | `HarmonyData/Quest/` (`Quest.json`, `PlayerInfo.json`, `QuestStatistics.json`) |
| Images | `HarmonyImages/Quest/` (PNG FileStorage; no ImageLibrary) |
| Lang | `HarmonyLanguage/Quest.json` |
| Runtime DLL | `HarmonyMods/Quest.dll` |
| Source | `.cursor/HarmonyMods/Quest/` |

## Build

```powershell
cd .cursor\HarmonyMods\Quest
.\build.ps1
```

Copies **only** `Quest.dll` to root `HarmonyMods/`.

## Load

- Auto-loads with other Harmony mods. Requires **Permissions** (`0Permissions.dll`).
- Do **not** run the Oxide plugin at the same time — leave `oxide/plugins/Quest.cs` in place but unloaded.
- Chat: `/quest`, `/qlist` (plus aliases from config `questListProgress`).
- CUI buttons: `cui.endtest QUEST …` (`UI_Handler`, `CloseMiniQuestList`, `ToggleQuestPin`, `CloseMainUI`).
- AppDomain: `Quest_ApiType` → `QuestHarmony.QuestMod` (`Call` dispatcher).

## Integrations

- **SkillTree** via `SkillTree_ApiType` (`GetPlayerLevel`). Gather quests use native dispenser/collectible patches (SkillTree does not broadcast `OnSkillTreeHandleDispenser` across assemblies).
- **IQChat / Notify**: no-op; messages use `chat.add`.
- **Friends / Clans**: AppDomain if present; otherwise vanilla teams for friends.
- Plugin-only event quests (RaidableBases, HarborEvent, Convoy, …) fire only if those mods call `QuestMod.Call`.

## Patch notes (this build)

Postfix observers on `BasePlayer.PlayerInit` / `OnDisconnected`, `SaveRestore.Save` / `Load`, `BuildingBlock.DoUpgradeToGrade`, `ResourceDispenser.GiveResourceFromItem`, `CollectibleEntity.DoPickup`, `ItemCrafter.FinishCrafting`, `Planner.DoBuild`, `PlayerLoot.StartLootingEntity`, `BaseCombatEntity.Die`, `BaseNetworkable.Kill`, `HackableLockedCrate.StartHacking`, `Recycler.SVSwitch` / `RecycleThink`, `NPCVendingMachine.GiveSoldItem`, `BigWheelGame.Payout`. Coexist with SkillTree/TruePVE/RustRewards postfixes on the same methods.
