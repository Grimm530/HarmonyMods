# Rust Server Metrics - Technical Documentation

> **IMPORTANT FOR FUTURE UPDATES**: When making code changes to this mod, please automatically update this documentation file (`RSM-Instructional.md`) to reflect those changes. This includes updating method descriptions, log messages, configuration options, and any other relevant technical details.

## Overview
This document provides technical documentation for the Rust Server Metrics HarmonyMod, including its architecture, components, and the HarmonyMod tracking feature that was added.

## Project Structure

```
src/RustServerMetrics/
├── MetricsLogger.cs              # Main metrics collection and orchestration
├── ReportUploader.cs              # Handles HTTP uploads to InfluxDB
├── RustServerMetricsLoader.cs     # HarmonyMod entry point and lifecycle hooks
├── ModTimeWarnings.cs             # Tracks method execution time warnings
├── Config/
│   └── ConfigData.cs              # Configuration data structure
├── HarmonyAssemblyStubs/
│   ├── IHarmonyModHooks.cs        # Interface for HarmonyMod lifecycle hooks
│   ├── OnHarmonyModLoadedArgs.cs  # Arguments for OnLoaded event
│   └── OnHarmonyModUnloadedArgs.cs # Arguments for OnUnloaded event
└── HarmonyPatches/
    ├── BasePlayer_*.cs            # Player-related patches
    ├── Bootstrap_*.cs             # Server startup patches
    ├── NetWrite_*.cs              # Network packet patches
    ├── OxideMod_*.cs              # Oxide plugin metrics patches
    ├── Performance_*.cs           # Performance monitoring patches
    ├── ServerMgr_*.cs             # Server manager patches
    └── Delayed/                   # Patches applied after server start
        ├── ConsoleSystem_*.cs
        ├── InvokeHandlerBase_*.cs
        ├── ObjectWorkQueue_*.cs
        ├── RPCServer_*.cs
        └── ServerMgr_Metrics_Patches.cs
    └── Utility/
        ├── DelayedHarmonyPatchAttribute.cs
        ├── Helpers.cs
        └── MetricsTimeStorage.cs
```

## Core Components

### 1. RustServerMetricsLoader
**File**: `RustServerMetricsLoader.cs`

**Purpose**: Entry point for the HarmonyMod. Implements `IHarmonyModHooks` to receive lifecycle events.

**Key Methods**:
- `OnLoaded(OnHarmonyModLoadedArgs args)`: Called when the mod is loaded. Initializes `MetricsLogger` and applies delayed patches.
- `OnUnloaded(OnHarmonyModUnloadedArgs args)`: Called when the mod is unloaded. Cleans up patches and destroys the logger instance.
- `AddModTimeWarnings(List<MethodInfo> methods)`: Dynamically adds time warning patches for specific methods.

**Static Fields**:
- `__serverStarted`: Tracks if the server has started
- `__harmonyInstance`: Main Harmony instance for patches
- `__modTimeWarningsHarmonyInstances`: List of Harmony instances for time warnings

### 2. MetricsLogger
**File**: `MetricsLogger.cs`

**Purpose**: Central component that collects, aggregates, and queues metrics for upload.

**Key Features**:
- Singleton component that persists across Unity scenes
- Manages multiple `MetricsTimeStorage` instances for different metric types
- Handles player lifecycle events
- Collects network statistics
- **NEW**: Collects HarmonyMod plugin metrics (see Changes section)

**Key Methods**:
- `Initialize()`: Sets up the logger, loads configuration, and initializes the uploader
- `StartLoggingMetrics()`: Begins periodic metric collection via `InvokeRepeating`
- `OnOxidePluginMetrics(Dictionary<string, double> metrics)`: Receives Oxide plugin hook times and calculates initialization vs runtime metrics
- `OnHarmonyModMetrics()`: **NEW** - Collects HarmonyMod plugin information
- `UploadPacket<T>(string ID, T data, Action<StringBuilder, T> serializer)`: Queues metrics for upload

**Plugin Metrics Tracking**:
- Tracks previous hook times to calculate deltas between measurements
- Separates initialization period (first 2 minutes) from runtime
- Calculates separate metrics: initialization time, peak/avg/min running times
- Prevents initialization overhead from skewing runtime performance averages

**MetricsTimeStorage Instances**:
- `ServerInvokes`: Tracks method invocation execution times
- `ServerRpcCalls`: Tracks RPC call execution times
- `WorkQueueTimes`: Tracks work queue job execution times
- `ServerUpdate`: Tracks server update method execution times
- `TimeWarnings`: Tracks methods that exceed time thresholds
- `ServerConsoleCommands`: Tracks console command usage

### 3. ReportUploader
**File**: `ReportUploader.cs`

**Purpose**: Handles HTTP uploads of metrics to InfluxDB.

**Key Features**:
- Maintains a send buffer (capacity: 100,000 reports)
- Batches metrics for efficient uploads
- Handles network errors and retries
- Throttles error messages to prevent log spam

**Key Methods**:
- `AddToSendBuffer(string payload)`: Adds a metric payload to the send buffer
- `Start()`: Begins the upload coroutine
- `Stop()`: Stops the upload coroutine and clears the buffer

### 4. Harmony Patches

The mod uses HarmonyLib to patch Rust server code at runtime. Patches are organized into:

**Immediate Patches** (applied on mod load):
- `Bootstrap_StartServer_Patch`: Hooks server startup
- `BasePlayer_PlayerInit_Patch`: Hooks player initialization
- `BasePlayer_OnDisconnected_Patch`: Hooks player disconnection
- `BasePlayer_PerformanceReport_Patch`: Hooks client performance reports
- `NetWrite_PacketID_Patch`: Tracks network packet types
- `NetWrite_Send_Patch`: Tracks network send operations
- `OxideMod_OnFrame_Patch`: Collects Oxide plugin metrics
- `Performance_FPSTimer_Patch`: Tracks server FPS
- `ServerMgr_OpenConnection_Patch`: Tracks new connections

**Delayed Patches** (applied after server start via `DelayedHarmonyPatchAttribute`):
- `ConsoleSystem_Internal_Patch`: Tracks console commands
- `InvokeHandlerBase_DoTick_Patch`: Tracks invoke execution
- `ObjectWorkQueue_RunJob_Patch`: Tracks work queue jobs
- `RPCServer_Attribute_Method_Patch`: Tracks RPC calls
- `ServerMgr_Metrics_Patches`: Tracks server update methods

## HarmonyMod Tracking Feature

### Overview
Added functionality to automatically track HarmonyMod plugins in the same way Oxide plugins are tracked, appearing seamlessly in Grafana dashboards.

### Implementation Details

**Location**: `MetricsLogger.cs` - `OnHarmonyModMetrics()` method (lines 244-360)

**How It Works**:
1. Uses reflection to access `HarmonyLoader.GetHarmonyMods()` without a direct compile-time dependency
2. Iterates through loaded HarmonyMods using the `HarmonyModInfo` struct
3. Uploads each HarmonyMod as a metric in the `oxide_plugins` measurement
4. Uses `hookTime=1` to indicate the mod is loaded (vs. Oxide plugins which use actual hook execution times)

**Key Implementation Points**:

1. **Reflection-Based Access**:
   - Tries multiple assembly names: `Rust.Harmony`, `Harmony-Assembly`
   - Falls back to searching all loaded assemblies if type not found by name
   - Uses `Type.GetType()` and `Assembly.GetType()` for type resolution

2. **HarmonyModInfo Struct Access**:
   - `HarmonyModInfo` is a struct with public fields (not properties)
   - Uses `GetField()` instead of `GetProperty()` to access `Name` and `Version`
   - Fields are accessed via reflection: `nameField.GetValue(modInfo)`

3. **Data Format**:
   - Uses the same `oxide_plugins` measurement as Oxide plugins
   - Format: `plugin="ModName" hookTime=1`
   - No type tags or version tags (per user requirement for seamless integration)

4. **Collection Frequency**:
   - Called every 5 seconds via `InvokeRepeating(OnHarmonyModMetrics, UnityEngine.Random.Range(1f, 2f), 5f)`
   - Less frequent than Oxide plugins (which report on every frame)

5. **Error Handling**:
   - Comprehensive error logging with `Debug.LogWarning()` for debugging
   - Graceful failure - if HarmonyMod tracking fails, Oxide plugin tracking continues
   - Logs assembly names searched if type resolution fails

**Code Flow**:
```
StartLoggingMetrics()
  └─> InvokeRepeating(OnHarmonyModMetrics, ...)
       └─> OnHarmonyModMetrics()
            ├─> Find HarmonyLoader type via reflection
            ├─> Call HarmonyLoader.GetHarmonyMods()
            ├─> Iterate through HarmonyModInfo structs
            ├─> Extract Name field from each mod
            └─> UploadPacket("oxide_plugins", modName, ...)
                 └─> ReportUploader.AddToSendBuffer()
```

### Changes Made

#### 1. MetricsLogger.cs
**Added**:
- `OnHarmonyModMetrics()` method (lines 244-360)
  - Reflection-based HarmonyLoader access
  - HarmonyModInfo struct field access
  - Iteration and metric upload logic
  - Comprehensive error logging

- `PluginMetricsTracker` class (lines 40-70)
  - Tracks previous hook times to calculate deltas
  - Separates initialization from runtime metrics
  - Tracks peak, average, and minimum running hook times
  - Prevents initialization overhead from skewing averages

- Enhanced `OnOxidePluginMetrics()` method
  - Calculates hook time deltas between measurements
  - Detects initialization period (first 2 minutes after server start)
  - Tracks initialization time separately from runtime
  - Uploads multiple metrics: `initTime`, `peakRunningTime`, `avgRunningTime`, `minRunningTime`
  - Maintains backward compatibility with `hookTime` field

**Modified**:
- `StartLoggingMetrics()` method (line 158)
  - Added: `InvokeRepeating(OnHarmonyModMetrics, UnityEngine.Random.Range(1f, 2f), 5f);`
  - Starts periodic HarmonyMod metric collection

- `OnServerStarted()` method (lines 80-102)
  - Updated HarmonyLoader access to use reflection (to avoid compile-time dependency)
  - Searches for HarmonyLoader in loaded assemblies if not found by name
  - Tracks server start time for initialization period detection

#### 2. README.md
**Updated**:
- Added section explaining HarmonyMod plugin tracking
- Clarified that both Oxide and HarmonyMod plugins appear in the same `oxide_plugins` measurement
- Documented that HarmonyMod plugins use `hookTime=1` to indicate loaded status

## Data Flow

### Metric Collection Flow
```
Game Event / Periodic Timer
  └─> Harmony Patch / MetricsLogger Method
       └─> MetricsTimeStorage.LogTime() / UploadPacket()
            └─> ReportUploader.AddToSendBuffer()
                 └─> _sendBuffer (List<string>)
```

### Upload Flow
```
ReportUploader Coroutine
  └─> Batch metrics from _sendBuffer
       └─> Build HTTP payload (InfluxDB line protocol)
            └─> UnityWebRequest POST to InfluxDB
                 ├─> Success: Remove from buffer
                 └─> Failure: Retry with backoff
```

## Configuration

**File**: `HarmonyMods_Data/ServerMetrics/Configuration.json`

**Structure** (see `ConfigData.cs`):
```json
{
  "Enabled": true,
  "Influx Database Url": "http://localhost:8086",
  "Influx Database Name": "rust-metrics",
  "Influx Database User": "user",
  "Influx Database Password": "password",
  "Server Tag": "server-01",
  "Debug Logging": false,
  "Amount of metrics to submit in each request": 1000
}
```

## InfluxDB Schema

### Measurements
- `oxide_plugins`: Plugin metrics (Oxide + HarmonyMod)
- `invoke_execution`: Method invocation times
- `rpc_calls`: RPC call execution times
- `work_queue`: Work queue job execution times
- `server_update`: Server update method execution times
- `timewarnings`: Methods exceeding time thresholds
- `console_commands`: Console command usage
- `network_updates`: Network packet statistics
- `players`: Player metrics
- `memory`: Memory usage
- `framerate`: Server FPS
- `frametime`: Frame time statistics
- `connection_latency`: Connection latency
- `client_performance`: Client performance reports
- `tasks`: Task execution metrics
- `entities`: Entity counts

### oxide_plugins Format

**Basic Format** (backward compatible):
```
oxide_plugins,server="server-01",plugin="PluginName" hookTime=0.123 1234567890
```

**Enhanced Format** (with initialization and runtime metrics):
```
oxide_plugins,server="server-01",plugin="PluginName" hookTime=0.123,initTime=45.6,peakRunningTime=2.5,avgRunningTime=1.2,minRunningTime=0.8 1234567890
```

**Tags**:
- `server`: Server identifier from configuration
- `plugin`: Plugin name (Oxide or HarmonyMod)

**Fields**:
- `hookTime`: Cumulative total hook execution time in milliseconds (Oxide) or `1` (HarmonyMod loaded status). **Note**: This is the raw cumulative total from Oxide and includes initialization time, which may skew averages.
- `initTime`: (NEW) Initialization time in milliseconds. Tracks hook execution time during the first 2 minutes after server start. Only reported once per plugin when initialization completes.
- `peakRunningTime`: (NEW) Peak running hook time in milliseconds. Maximum hook execution time during runtime (excluding initialization).
- `avgRunningTime`: (NEW) Average running hook time in milliseconds. Average hook execution time during runtime (excluding initialization).
- `minRunningTime`: (NEW) Minimum running hook time in milliseconds. Minimum hook execution time during runtime (excluding initialization).

**Initialization Detection**:
- The system considers the first 120 seconds (2 minutes) after server start as the "initialization period"
- Hook times during this period are tracked separately as `initTime`
- After initialization completes, all subsequent hook times are tracked as "running" metrics
- This separation prevents one-time initialization operations (like file scanning, large file parsing) from skewing runtime performance averages

## Build Process

### Dependencies
1. Rust server assemblies (publicized via `AssemblyPublicizer.exe`)
2. HarmonyLib (`0Harmony.dll`)
3. Unity assemblies
4. Newtonsoft.Json

### Build Commands

**Recommended: Use the build script**
```powershell
# Run the automated build script (recommended)
.\build.ps1
```

The build script will:
- Optionally update dependencies
- Clean previous builds
- Build the solution for Linux
- Copy the DLL to `D:\!RustServer\HarmonyMods\RustServerMetrics.dll`
- Create backups of existing files

**Manual build (alternative)**
```powershell
# Update dependencies from local Rust server
.\update-all-dependencies.bat

# Build for Linux
msbuild RustServerMetrics.sln /p:Configuration=Linux /p:Platform="Any CPU" /t:Build
```

### Output
- `src/RustServerMetrics/bin/Linux/net48/RustServerMetrics.dll`
- Automatically copied to: `D:\!RustServer\HarmonyMods\RustServerMetrics.dll`

## Debugging

### Log Messages
The mod outputs several log messages prefixed with `[ServerMetrics]`:

**HarmonyMod Tracking**:
- `[ServerMetrics] Collected metrics for X HarmonyMod(s)` - Success (only appears when Debug Logging is enabled in configuration)
- `[ServerMetrics] HarmonyLoader type not found...` - Type resolution failure
- `[ServerMetrics] GetHarmonyMods method not found...` - Method not found
- `[ServerMetrics] GetHarmonyMods returned null...` - No mods loaded
- `[ServerMetrics] HarmonyMods is not enumerable...` - Enumeration failure
- `[ServerMetrics] HarmonyModInfo type not found` - Struct type not found
- `[ServerMetrics] HarmonyModInfo.Name field not found` - Field access failure
- `[ServerMetrics] Failed to collect HarmonyMod metrics: ...` - General exception

### Console Commands
- `servermetrics.reloadcfg`: Reload configuration file
- `servermetrics.status`: Show mod status, upload status, and buffer size

### InfluxDB Queries
```sql
-- Check all plugins in last 10 minutes
SELECT DISTINCT "plugin" FROM "oxide_plugins" WHERE time > now() - 10m

-- Count records per plugin
SELECT COUNT(*) FROM "oxide_plugins" WHERE time > now() - 1h GROUP BY "plugin"

-- Check HarmonyMod plugins specifically
SELECT * FROM "oxide_plugins" WHERE "plugin" =~ /GrimmNPC|NoActiveItemDrop|NoGibs/ AND time > now() - 10m

-- Get runtime performance metrics (excluding initialization)
SELECT "avgRunningTime", "peakRunningTime", "minRunningTime" 
FROM "oxide_plugins" 
WHERE time > now() - 1h 
  AND "avgRunningTime" IS NOT NULL
GROUP BY "plugin"

-- Find plugins with highest average runtime hook times
SELECT MEAN("avgRunningTime") as "avg_runtime" 
FROM "oxide_plugins" 
WHERE time > now() - 1h 
  AND "avgRunningTime" IS NOT NULL
GROUP BY "plugin" 
ORDER BY "avg_runtime" DESC 
LIMIT 10

-- Get initialization times for all plugins
SELECT "initTime" 
FROM "oxide_plugins" 
WHERE time > now() - 24h 
  AND "initTime" IS NOT NULL
GROUP BY "plugin"

-- Compare initialization vs runtime for a specific plugin
SELECT "initTime", "avgRunningTime", "peakRunningTime" 
FROM "oxide_plugins" 
WHERE "plugin" = 'RustVehicles' 
  AND time > now() - 24h
```

## Architecture Decisions

### Why Reflection?
- Avoids compile-time dependency on `Harmony-Assembly`/`Rust.Harmony`
- Allows the mod to work even if HarmonyLoader assembly name changes
- More flexible and resilient to changes in the HarmonyMod loader

### Why Same Measurement?
- User requirement: HarmonyMod plugins should appear seamlessly with Oxide plugins
- Simplifies Grafana dashboard queries (no filtering needed)
- Consistent data structure for all plugin metrics

### Why hookTime=1?
- Oxide plugins use actual hook execution times
- HarmonyMods don't have hook execution times (they're IL patches)
- `1` indicates "loaded" status, making it easy to filter in Grafana if needed
- Simple, consistent value that's easy to query

### Why 5 Second Interval?
- Less frequent than Oxide plugins (which report every frame)
- Reduces database writes for static "loaded" status
- Still frequent enough to track mod loading/unloading
- Random initial delay (1-2 seconds) prevents thundering herd

### Why Separate Initialization from Runtime Metrics?
- Many plugins perform expensive one-time operations during initialization:
  - File system scanning (multiple directories for JSON configs)
  - Large file reading and parsing (e.g., Karuza catalog parsing)
  - Database initialization and table creation
  - Loading large datasets into memory
- These initialization operations can take 30-120+ seconds and skew average hook times
- By separating initialization from runtime, we get:
  - **Accurate runtime performance metrics** - Shows actual plugin performance during normal operation
  - **Initialization time tracking** - Still captures how long initialization takes
  - **Better performance analysis** - Can identify plugins with slow initialization vs slow runtime
- The 2-minute initialization window is configurable via `INITIALIZATION_PERIOD_SECONDS` constant

## Future Enhancements

Potential improvements:
1. Track HarmonyMod version in tags (currently not included per user requirement)
2. Track HarmonyMod load/unload events as separate metrics
3. Add HarmonyMod-specific metrics (patch count, patch execution times)
4. Support for HarmonyMod configuration metrics
5. Health checks for HarmonyMod plugins

## Version History

### Current Version
- **Enhanced Plugin Metrics**: Separates initialization time from runtime hook times
  - Tracks `initTime` separately from runtime metrics
  - Calculates `peakRunningTime`, `avgRunningTime`, and `minRunningTime` (excluding initialization)
  - Prevents initialization overhead from skewing runtime performance averages
  - Maintains backward compatibility with `hookTime` field
- **HarmonyMod Tracking**: Added automatic tracking of HarmonyMod plugins
- **Reflection-Based Access**: Uses reflection to avoid compile-time dependencies
- **Seamless Integration**: HarmonyMod plugins appear in same measurement as Oxide plugins
- **Debug Logging Control**: HarmonyMod success log message only appears when Debug Logging is enabled in configuration

### Previous Versions
- Original: Oxide plugin tracking only
- Metrics collection for server performance, network, players, etc.
