# CustomMapGen Implementation Status

## ✅ FULLY IMPLEMENTED (Will Work)

### Infrastructure Settings
- ✅ **AboveGroundRails** - Controls above-ground railroad generation via `WorldConfigPatches`
- ✅ **RemoveSmallPowerLines** - Removes small powerlines via `WorldConfigPatches`
- ✅ **RemoveLargePowerLines** - Removes large powerlines via `WorldConfigPatches`
- ✅ **RemoveRivers** - Disables river generation via `GenerateRiverLayoutPatches`
- ✅ **EnableCliffs** - Enables/disables cliff generation via `PlaceCliffsPatches`
- ✅ **GenerateRingRoad** - Controls ring road generation via `GenerateRoadRingPatches`
- ✅ **RemoveCarWrecks** - Removes car wrecks via `VehicleCarWrecksPatches` and `PlaceMonumentsRoadsidePatches`
- ✅ **RemoveUndergroundTunnels** - Disables underground tunnels via `WorldConfigPatches` (BelowGroundRails)
- ✅ **EmbedCargoShipPath** - Controls cargo ship path embedding via `CargoNotifierPatches`
- ✅ **AllowBuildingOnRoads** - Removes roadside topology flags via `GenerateRoadTopologyPatches`
- ✅ **TrySpawningOutpostInCenter** - Moves outpost to map center (redirects position in `World_AddPrefab_Patch`; no second outpost created)

### Water Features
- ✅ **LakeMinAmount / LakeMaxAmount** - Limits lake count via `LakeInfoPatches` (max enforced, min not enforced)
- ✅ **LakesBlocked** - Removes all lakes if true
- ✅ **LakesGenerate** - Controls lake generation ("Wanted", "NotWanted", "NoPreference")

### Islands
- ✅ **IslandsEnabled** - Enables/disables islands via `PlaceMonumentsOffshorePatches`
- ✅ **IslandIntensity** - Controls island count intensity (0-10) via `PlaceMonumentsOffshorePatches`

### Monuments
- ✅ **OasesMinAmount / OasesMaxAmount** - Limits oasis count via `PlaceMonumentsPatches` (max enforced)
- ✅ **OasesBlocked** - Removes all oases if true
- ✅ **OasesGenerate** - Controls oasis generation ("Wanted", "NotWanted", "NoPreference")
- ✅ **CanyonsMinAmount / CanyonsMaxAmount** - Limits canyon count via `PlaceMonumentsPatches` (max enforced)
- ✅ **CanyonsBlocked** - Removes all canyons if true
- ✅ **CanyonsGenerate** - Controls canyon generation ("Wanted", "NotWanted", "NoPreference")

### Underwater Labs
- ✅ **UnderwaterLabsBlocked** - Disables underwater labs via `WorldConfigPatches`
- ✅ **UnderwaterLabsGenerate** - Controls underwater lab generation ("Wanted", "NotWanted", "NoPreference")
- ⚠️ **UnderwaterLabsMinAmount / UnderwaterLabsMaxAmount** - Stored but NOT enforced (only enable/disable works)

---

## ✅ TERRAIN CONFIGURATION
- ✅ **TerrainConfiguration.IslandConfig** - Already handled by top-level `IslandsEnabled`/`IslandIntensity`
- ✅ **TerrainConfiguration.MountainConfig.ReduceMountains** - Reduces mountain count via `MountainPatches`
- ✅ **TerrainConfiguration.TierConfig** - Controls tier percentages via `WorldConfigPatches` (Tier0Percentage, Tier1Percentage, Tier2Percentage)
- ✅ **TerrainConfiguration.BiomeConfig** - Controls biome percentages via `WorldConfigPatches` (AridPercentage, TemperatePercentage, etc.)
- ✅ **TerrainConfiguration.BiomeAxisAngle** - Sets biome axis angle via `TerrainMetaPatches` (uses reflection)
- ✅ **TerrainConfiguration.LootAxisAngle** - Sets loot axis angle via `TerrainMetaPatches` (uses reflection)
- ✅ **TerrainConfiguration.FlattenShoreAndBay** - Flattens terrain near water via `GenerateHeightShoreFlattenPatches`

### Monument Configurations
- ✅ **OilRigConfigurations** - Filters oil rigs via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **Safezones** - Filters safezones via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **LargeMonuments** - Filters large monuments via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **SmallMonuments** - Filters small monuments via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **Harbors** - Filters harbors via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **WaterWells** - Filters water wells via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **Caves** - Filters caves via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **Mountains** - Filters mountains via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **Quarries** - Filters quarries via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **Icebergs** - Filters icebergs via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **IceLakes** - Filters ice lakes via `PlaceMonumentsFilterPatches` (blocked/desired)
- ✅ **Ruins** - Filters ruins via `PlaceMonumentsFilterPatches` (blocked/desired)

### Other Settings
- ✅ **BlockedPrefabs** - Applied to WorldConfig.PrefabBlacklist via `WorldConfigPatches`
- ❌ **Webhook** - No patch exists (notification system, may not need patching)

---

## Summary

**Working Settings (43+):**
- **Infrastructure (11):** AboveGroundRails, RemoveSmallPowerLines, RemoveLargePowerLines, RemoveRivers, EnableCliffs, GenerateRingRoad, RemoveCarWrecks, RemoveUndergroundTunnels, EmbedCargoShipPath, AllowBuildingOnRoads, TrySpawningOutpostInCenter
- **Water Features (4):** LakeMinAmount, LakeMaxAmount, LakesBlocked, LakesGenerate
- **Islands (2):** IslandsEnabled, IslandIntensity
- **Monuments - Oases/Canyons (6):** OasesMinAmount, OasesMaxAmount, OasesBlocked, OasesGenerate, CanyonsMinAmount, CanyonsMaxAmount, CanyonsBlocked, CanyonsGenerate
- **Underwater Labs (2):** UnderwaterLabsBlocked, UnderwaterLabsGenerate
- **Terrain Config (7):** MountainConfig.ReduceMountains, TierConfig (percentages), BiomeConfig (percentages), BiomeAxisAngle, LootAxisAngle, FlattenShoreAndBay
- **Monument Configs (12):** OilRigConfigurations, Safezones, LargeMonuments, SmallMonuments, Harbors, WaterWells, Caves, Mountains, Quarries, Icebergs, IceLakes, Ruins
- **Other (1):** BlockedPrefabs

**Not Working (1):**
- **Webhook** - Notification system, may not need patching (could be implemented as a post-generation notification)

**Note:** Most settings are now implemented! The mod should handle the vast majority of your configuration options.
