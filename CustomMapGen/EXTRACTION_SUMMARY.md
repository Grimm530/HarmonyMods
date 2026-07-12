# RustEdit Prefab Entity Extraction - Summary

## What Was Created

I've created a complete solution for extracting entity data (position, rotation, scale, prefab paths) from RustEdit prefab files so you can use them in CustomMapGen procedural monument generation.

## Files Created

### 1. **Rust Server Plugin** (`oxide/plugins/PrefabEntityExtractor.cs`)
   - **Purpose**: Extract entity data from RustEdit prefabs using Rust's runtime environment
   - **Usage**: Console command `prefab.extract <prefab_path> [output_name]`
   - **Output**: Generates JSON and C# code files in `oxide/data/CustomMapGen/`

### 2. **Unity Editor Scripts** (in `D:\VehicleEditor-main\Assets\Scripts\Editor\`)
   - **PrefabEntityExtractor.cs**: Full Unity Editor window for extracting entities
   - **RustEditPrefabExtractor.cs**: Generates server extraction scripts
   - **Note**: These require Unity Editor with Rust assets loaded

### 3. **Documentation**
   - **PREFAB_EXTRACTION_GUIDE.md**: Complete guide on how to extract and use prefab entities
   - **MAP_GENERATION_ASSEMBLY_REFERENCE.md**: Updated with prefab extraction section

## Quick Start Guide

### Step 1: Extract Entities from Your Prefab

1. Copy `PrefabEntityExtractor.cs` to your server's `oxide/plugins/` folder
2. Restart server or reload: `oxide.reload PrefabEntityExtractor`
3. Run extraction command:
   ```
   prefab.extract "D:\RustEdit\CustomPrefabs\Grimm's Gas.prefab" grimm_gas
   ```

### Step 2: Use Generated Code

The plugin generates two files in `oxide/data/CustomMapGen/`:
- `grimm_gas_entities.json` - JSON data (for reference/debugging)
- `grimm_gas_entities.cs` - C# code ready to use

### Step 3: Integrate into CustomMapGen

1. Copy the generated C# class to your CustomMapGen patches folder
2. Add a patch to call it when monuments are placed:

```csharp
[HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
public static class PlaceMonumentsCustom_Patch
{
    static void Postfix(PlaceMonuments __instance, uint seed)
    {
        if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
            return;
        
        foreach (var monument in TerrainMeta.Path.Monuments)
        {
            if (monument.name.Contains("YourMonumentName"))
            {
                GrimmGasEntityData.AddEntitiesToMonument(monument);
            }
        }
    }
}
```

## How It Works

1. **Loads the Prefab**: Uses Rust's `FileSystem.Load<GameObject>()` or `GameManager.server.FindPrefab()`
2. **Finds All Entities**: Recursively traverses the prefab hierarchy looking for `BaseEntity` components
3. **Converts to Local Coordinates**: Transforms world positions/rotations to local coordinates relative to prefab root
4. **Exports Data**: Generates both JSON (for debugging) and C# code (for use in patches)

## Key Features

- ✅ Extracts all `BaseEntity` components from prefab hierarchy
- ✅ Converts to local coordinates for monument-agnostic placement
- ✅ Generates ready-to-use C# code
- ✅ Includes position, rotation, scale, and prefab paths
- ✅ Works with any RustEdit prefab file

## Example Output

The generated C# code will look like:

```csharp
namespace CustomMapGen.Patches
{
    public static class GrimmGasEntityData
    {
        private static readonly List<EntityData> Entities = new List<EntityData>
        {
            new EntityData { 
                PrefabName = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab",
                LocalPosition = new Vector3(1.234f, 0.567f, -2.345f),
                LocalRotation = new Quaternion(0f, 0.707f, 0f, 0.707f),
                Scale = new Vector3(1f, 1f, 1f)
            },
            // ... more entities
        };
        
        public static void AddEntitiesToMonument(MonumentInfo monument)
        {
            // Spawns all entities at monument location
        }
    }
}
```

## Troubleshooting

### Prefab Won't Load
- Check that the file path is correct and accessible
- Verify the prefab is a valid Unity prefab file
- Try using the full absolute path

### No Entities Found
- Make sure the prefab contains `BaseEntity` components
- Check server logs for error messages
- Verify the prefab structure is correct

### Entities Not Spawning
- Verify prefab paths are correct (check server logs)
- Ensure the monument patch is being called
- Check that entities are spawned after monument placement

## Next Steps

1. Extract entities from your RustEdit prefabs
2. Review the generated code
3. Integrate into your CustomMapGen patches
4. Test on a generated map
5. Adjust positions/rotations if needed

## Related Documentation

- See `PREFAB_EXTRACTION_GUIDE.md` for detailed instructions
- See `MAP_GENERATION_ASSEMBLY_REFERENCE.md` for map generation API reference
- See `PlaceMonumentsCompoundPatches.cs` for example implementation
