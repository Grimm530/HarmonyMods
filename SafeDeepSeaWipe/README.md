# SafeDeepSeaWipe

Harmony mod that makes Deep Sea close/wipe **safe** (only kill entities inside bounds) and documents a **more efficient** way the game could gather entities.

## What it does

1. **Bounds filter (patch)**  
   After the game builds the list of entities to kill, entities **not inside `DeepSeaBounds`** are removed. So mainland/base entities that ended up in the wrong network group are no longer wiped.

2. **Efficiency note**  
   The game’s `GetAllDeepSeaEntities` is expensive because it:
   - Iterates **every** entity in `GlobalNetworkGroup.networkables` and checks `IsInsideDeepSea` for each.
   - Iterates **every** entity in `LimboNetworkGroup.networkables` the same way.
   - Then iterates all visibility groups and all entities in layer‑4 groups.

   So every Deep Sea close does O(Global + Limbo + layer‑4) work. On large servers that’s a lot.

## More efficient approach (for game or a full Harmony mod)

Per **Rust Plugin Performance Best Practices** (“never search the server for entities”):

- **Option A – Don’t use Global/Limbo here**  
  Build the wipe list only from:
  - `ServerIslands`, `ServerGhostShips`, `ServerFloatingCities`, `ServerRHIBS` (already tracked),
  - `BaseNetworkable.DeepSeaGroup.networkables`,
  - Visibility groups with **layer == 4** (Deep Sea grid cells).

  Skip the two loops over `GlobalNetworkGroup` and `LimboNetworkGroup`. Everything that should be wiped is either in the tracked lists or in Deep Sea / layer‑4 groups. That avoids touching every global/limbo entity on the server.

- **Option B – Track as they enter**  
  When an entity’s network group becomes a Deep Sea group (or it’s spawned by Deep Sea), add it to a `HashSet<BaseEntity>`. On wipe, use that set only (O(1) lookups, no big iterations). Clear/update the set when Deep Sea closes and when entities leave.

This mod does **not** implement the efficiency patch (Option A). Doing it in a Harmony mod would require referencing both Assembly-CSharp and Facepunch.Network; Facepunch.Network’s `Network` namespace then shadows Assembly-CSharp’s `Network.Net`, so the project fails to compile unless every game type is qualified with an extern alias. That’s not practical here, so only the **bounds filter** (safety) is patched; the game still does the full (inefficient) gather.

## Build / load

- Build: `dotnet build -c Release` or `.\build.ps1`
- DLL: `HarmonyMods\SafeDeepSeaWipe.dll`
- Load: `harmony.load SafeDeepSeaWipe` or restart server.
