# MapCreation

Paints the wipe overworld + heightmesh so **LivemapBridge.dll** can feed the browser map without the Minimap Harmony mod.

Not a copy of Minimap. Minimap is an in-game UI (fog, markers, heat, CUI). This folder is headless TerrainMeta sampling with the same OceanMargin (500) UV math the viewer uses.

| File | Browser |
|---|---|
| `map.png` | 3D albedo (biome/splat/shore, no monument text) |
| `height.bin` | 513² uint16 heights (`GetHeight01`) |
| `terrain.json` | `mapImageSource: livemap`, `oceanMargin: 500` |

If Minimap’s cache is already on disk, LivemapBridge may copy that PNG instead (`PreferMinimapCache`). Otherwise it renders here. Force a rebuild: `livemap.render`.
