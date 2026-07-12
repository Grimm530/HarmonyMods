# Changelog

All notable changes to the Rust Server Metrics HarmonyMod will be documented in this file.

> **IMPORTANT FOR FUTURE UPDATES**: When making code changes to this mod, please automatically update this changelog file (`CHANGELOG.md`) to document those changes. Add new entries at the top with the date and categorize changes as Added, Changed, Fixed, or Removed.

## [Custom Build] - 2026-01-06

### Added
- **HarmonyMod Plugin Tracking**: Automatic tracking of HarmonyMod plugins alongside Oxide plugins
  - New method `OnHarmonyModMetrics()` in `MetricsLogger.cs`
  - Collects HarmonyMod plugin information every 5 seconds
  - HarmonyMod plugins appear in the same `oxide_plugins` measurement as Oxide plugins
  - Uses `hookTime=1` to indicate loaded status (vs. actual execution times for Oxide plugins)

### Changed
- **MetricsLogger.StartLoggingMetrics()**: Added periodic call to `OnHarmonyModMetrics()` every 5 seconds
- **MetricsLogger.OnServerStarted()**: Updated to use reflection for HarmonyLoader access to avoid compile-time dependency
- **MetricsLogger.OnHarmonyModMetrics()**: The `[ServerMetrics] Collected metrics for X HarmonyMod(s)` log message now only appears when `Debug Logging` is enabled in the configuration file. This prevents log spam when debug logging is disabled.
- **README.md**: Updated to document HarmonyMod plugin tracking feature

### Technical Details
- **Reflection Implementation**: Uses reflection to access `HarmonyLoader.GetHarmonyMods()` without direct dependency
  - Tries multiple assembly names: `Rust.Harmony`, `Harmony-Assembly`
  - Falls back to searching all loaded assemblies if type not found by name
  - Accesses `HarmonyModInfo` struct fields via reflection (`GetField()` not `GetProperty()`)
  
- **Data Format**: HarmonyMod plugins use identical format to Oxide plugins
  - Measurement: `oxide_plugins`
  - Format: `plugin="ModName" hookTime=1`
  - No type tags or version tags (seamless integration per user requirement)

- **Error Handling**: Comprehensive error logging for debugging
  - Logs assembly names searched if type resolution fails
  - Logs specific failure points (method not found, null returns, etc.)
  - Graceful failure - Oxide plugin tracking continues if HarmonyMod tracking fails

### Files Modified
1. `src/RustServerMetrics/MetricsLogger.cs`
   - Added `OnHarmonyModMetrics()` method (lines 244-360)
   - Modified `StartLoggingMetrics()` to call HarmonyMod metrics collection
   - Modified `OnServerStarted()` to use reflection for HarmonyLoader
   - Modified `OnHarmonyModMetrics()` to check `Configuration.debugLogging` before logging success message (line 351)

2. `README.md`
   - Added section explaining HarmonyMod plugin tracking
   - Updated plugin metrics documentation

### Files Created
1. `TECH_DOCS.md` - Comprehensive technical documentation
2. `CHANGELOG.md` - This file

### Build Notes
- Built with MSBuild for Linux target (.NET Framework 4.8)
- Dependencies updated from local Rust server: `D:\!RustServer\RustDedicated_Data\Managed`
- Output: `src/RustServerMetrics/bin/Linux/net48/RustServerMetrics.dll`
- Deployed to: `D:\!RustServer\HarmonyMods\RustServerMetrics.dll`

### Testing
- Verified HarmonyMod plugins appear in InfluxDB `oxide_plugins` measurement
- Confirmed format matches Oxide plugins exactly
- Tested reflection-based type resolution with multiple assembly names
- Verified error handling and logging

### Known Issues
- None currently

### Future Improvements
- Consider tracking HarmonyMod version information (currently excluded per user requirement)
- Consider tracking HarmonyMod load/unload events
- Consider adding HarmonyMod-specific performance metrics

---

## Original Version
- Oxide plugin tracking
- Server performance metrics
- Network statistics
- Player metrics
- Memory and FPS tracking
- Console command tracking
- RPC call tracking
- Work queue tracking
