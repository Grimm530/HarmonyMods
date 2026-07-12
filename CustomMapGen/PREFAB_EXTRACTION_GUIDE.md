# RustEdit Prefab Entity Extraction Guide

This guide explains how to extract entity data (position, rotation, scale, prefab paths) from RustEdit prefab files for use in CustomMapGen procedural monument generation.

## Overview

RustEdit prefab files are Unity prefab files that contain multiple entities arranged in a hierarchy. To use these prefabs in procedural map generation, you need to:

1. Extract all `BaseEntity` components from the prefab
2. Record their world positions, rotations, and scales
3. Convert them to local coordinates relative to a reference point (usually the prefab root)
4. Export the data in a format compatible with CustomMapGen

## Method 1: Extract from Loaded Map (Recommended - No Files Needed)

**This is the easiest and most reliable method** - it works entirely within Rust's runtime without needing file access.

### Setup

1. Copy `PrefabEntityExtractor.cs` to your server's `oxide/plugins` folder
2. Load a map that contains your RustEdit monument (the prefab must be placed in the map)
3. Restart your server or reload plugins: `oxide.reload PrefabEntityExtractor`

### Usage

```
prefab.extractfrommap "Grimm's Gas" grimm_gas
```

This command:
- Searches for a monument matching the name
- Finds all entities near that monument (within 100m radius)
- Extracts their data relative to the monument's position/rotation
- Generates JSON and C# code files

**Advantages:**
- ✅ No file access needed
- ✅ Works with any loaded monument
- ✅ Uses actual in-game entity data
- ✅ Works with RustEdit-placed monuments
- ✅ Most reliable method

## Method 2: Extract from Prefab File (Limited)

**NOTE**: Unity `.prefab` files are binary/serialized Unity objects. Rust's runtime cannot directly deserialize external prefab files unless they're in Rust's asset bundles.

### Setup

1. Copy `PrefabEntityExtractor.cs` to your server's `oxide/plugins` folder
2. Copy your RustEdit prefab file to: `oxide/data/PrefabEntityExtractor/`
3. Restart your server or reload plugins

### Usage

```
prefab.extract "Grimm's Gas.prefab" grimm_gas
```

**Limitations:**
- ⚠️ Unity prefab files are binary and may not load
- ⚠️ Only works if prefab is in Rust's asset bundles
- ⚠️ External prefab files cannot be deserialized by Rust's FileSystem

**If it fails**, you'll see an error message recommending `prefab.extractfrommap` instead.

### File Locations Tried

The plugin searches for prefab files in these locations (in order):
1. The path you provide (if absolute)
2. `{server_root}/{your_path}` (relative to server)
3. `{server_root}/oxide/data/PrefabEntityExtractor/{filename}`
4. `{server_root}/oxide/data/PrefabEntityExtractor/{your_path}`

**Recommended location**: `oxide/data/PrefabEntityExtractor/YourPrefab.prefab`

## Method 3: Using Unity Editor Scripts

If you have access to the Unity Editor with RustEdit prefabs loaded, you can use the Unity Editor scripts:

1. **`PrefabEntityExtractor.cs`** - Full-featured Unity Editor window
2. **`RustEditPrefabExtractor.cs`** - Generates server extraction scripts

### Setup

1. Copy the scripts to `Assets/Scripts/Editor/` in your Unity project
2. Open Unity Editor
3. Go to `RustEdit > Extract Prefab Entities` menu

### Usage

1. Select your RustEdit prefab file
2. Set reference position/rotation (or use "Quick Extract" to use root as reference)
3. Choose output location
4. Click "Extract Entities"

## Method 4: Manual Extraction from RustEdit

If you have RustEdit open with the prefab loaded:

1. Note the root GameObject's position and rotation
2. For each entity in the hierarchy:
   - Record the prefab path (from `BaseEntity.PrefabName`)
   - Record world position, rotation, and scale
   - Calculate local position: `localPos = Quaternion.Inverse(rootRot) * (worldPos - rootPos)`
   - Calculate local rotation: `localRot = Quaternion.Inverse(rootRot) * worldRot`

## Using Extracted Data in CustomMapGen

Once you have the extracted entity data, integrate it into CustomMapGen:

### Step 1: Add the Generated Class

Copy the generated C# class to your CustomMapGen patches folder (e.g., `Patches/GrimmGasEntityData.cs`).

### Step 2: Call from Monument Placement Patch

Modify your monument placement patch to call the entity spawner:

```csharp
[HarmonyPatch(typeof(PlaceMonuments), nameof(PlaceMonuments.Process))]
public static class PlaceMonumentsCustom_Patch
{
    static void Postfix(PlaceMonuments __instance, uint seed)
    {
        if (!CustomMapGen.IsCustomMapGenEnabled() || TerrainMeta.Path == null)
            return;
        
        var config = CustomMapGen.Instance.GetConfig();
        
        // Find your custom monument
        foreach (var monument in TerrainMeta.Path.Monuments)
        {
            if (monument.name.Contains("YourMonumentName"))
            {
                // Add entities to the monument
                GrimmGasEntityData.AddEntitiesToMonument(monument);
            }
        }
    }
}
```

### Step 3: Compile and Test

1. Compile your CustomMapGen mod
2. Generate a new map
3. Check the logs for entity spawn messages
4. Verify entities appear at the monument location

## Understanding the Data Structure

### Entity Data Fields

- **PrefabName**: Full Rust prefab path (e.g., `"assets/prefabs/deployable/large wood storage/box.wooden.large.prefab"`)
- **LocalPosition**: Position relative to reference point (Vector3)
- **LocalRotation**: Rotation relative to reference rotation (Quaternion)
- **Scale**: Local scale of the entity (Vector3)

### Coordinate System

Entities are stored in **local coordinates** relative to the prefab root. This allows them to be placed at any monument location while maintaining their relative positions.

**Conversion Formula:**
- **World Position**: `worldPos = monumentPos + monumentRot * localPos`
- **World Rotation**: `worldRot = monumentRot * localRot`

## Troubleshooting

### File Access Issues

**Problem**: "File not found" or "Cannot access file"

**Solution**: 
- Use `prefab.extractfrommap` instead (no file needed)
- Or copy prefab to: `oxide/data/PrefabEntityExtractor/`
- Make sure the path is relative to server root or use just the filename

### Prefab Won't Load

**Problem**: "Could not load Unity .prefab file directly"

**Reason**: Unity prefab files are binary/serialized objects that require Unity's serialization system. Rust's runtime cannot deserialize external prefab files.

**Solutions**:
1. ✅ **Use `prefab.extractfrommap`** - Extract from loaded monument (RECOMMENDED)
2. Use RustEdit to place prefab, save map, then extract from map
3. Use Unity Editor extraction tool (see Method 3)

### No Entities Found

**For `prefab.extractfrommap`:**
- Make sure the map is fully loaded
- Verify the monument name matches exactly
- Check available monuments by running command without arguments
- Increase search radius if monument is large (edit plugin code, default is 100m)

**For `prefab.extract`:**
- Unity prefab files cannot be loaded directly (see above)
- Use `extractfrommap` method instead

### Entities Not Spawning

- Verify prefab paths are correct (check server logs)
- Ensure the monument patch is being called
- Check that entities are spawned after monument placement
- Verify monument name matching in your patch

## Why Prefab Files Can't Be Loaded Directly

Unity `.prefab` files are:
- **Binary/serialized Unity objects** - They use Unity's proprietary serialization format
- **Require Unity's runtime** - Need Unity's `AssetDatabase` or serialization system to deserialize
- **Not in Rust's asset bundles** - External prefab files aren't part of Rust's game assets

Rust's `FileSystem.Load<T>()` can only load:
- Files that are in Rust's asset bundles
- Files in specific formats Rust understands
- **NOT** external Unity prefab files

This is why `prefab.extractfrommap` is recommended - it works with entities that are already loaded in the game, avoiding the file format issue entirely.

## Tips

1. **Use `extractfrommap` First**: It's the most reliable method and requires no file access
2. **Test with Simple Prefabs**: Start with prefabs containing few entities to verify the process works
3. **Check Prefab Paths**: Rust prefab paths are case-sensitive and must match exactly
4. **Use JSON Output**: The JSON file is useful for debugging and manual adjustments
5. **Batch Processing**: You can extract multiple monuments by calling the extraction function multiple times

## Related Files

- `PrefabEntityExtractor.cs` - Rust server plugin for extraction
- `PrefabEntityExtractor.cs` (Editor) - Unity Editor extraction tool
- `PlaceMonumentsCompoundPatches.cs` - Example of using extracted entity data

## Notes

- Prefab files are binary Unity format, so direct text parsing is not recommended
- The extraction process requires Rust's runtime environment to properly identify `BaseEntity` components
- Extracted data is monument-agnostic and can be reused across different monument types
- **Best practice**: Use `prefab.extractfrommap` to avoid file format limitations
