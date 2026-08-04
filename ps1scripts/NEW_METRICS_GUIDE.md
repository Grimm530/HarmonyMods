# How to Access the New Plugin Metrics

## Data Flow

1. **Rust Server Metrics** (HarmonyMod) collects metrics from your Rust server
2. **InfluxDB** stores the metrics (configured in `HarmonyData/ServerMetrics/Configuration.json`)
3. **Grafana Dashboard** queries InfluxDB to display the data

**Important**: The actual data is stored in **InfluxDB**, not in the JSON file. The JSON file only contains the dashboard configuration (queries, panels, etc.).

## New Metrics Available

After deploying the updated Rust Server Metrics mod, you'll have these new fields in the `oxide_plugins` measurement:

- `hookTime` - Cumulative total (backward compatible, includes initialization)
- `initTime` - Initialization time (reported once when initialization completes)
- `avgRunningTime` - Average runtime hook time (excluding initialization)
- `peakRunningTime` - Peak runtime hook time (excluding initialization)
- `minRunningTime` - Minimum runtime hook time (excluding initialization)

## How to Query the Data

### Option 1: Direct InfluxDB Queries

You can query InfluxDB directly using the InfluxDB CLI, HTTP API, or Grafana's Explore view:

```sql
-- Get average runtime hook times for all plugins (excluding initialization)
SELECT MEAN("avgRunningTime") as "avg_runtime" 
FROM "oxide_plugins" 
WHERE time > now() - 1h 
  AND "avgRunningTime" IS NOT NULL
GROUP BY "plugin" 
ORDER BY "avg_runtime" DESC

-- Get initialization times
SELECT "initTime" 
FROM "oxide_plugins" 
WHERE time > now() - 24h 
  AND "initTime" IS NOT NULL
GROUP BY "plugin"

-- Get all metrics for RustVehicles
SELECT "initTime", "avgRunningTime", "peakRunningTime", "minRunningTime" 
FROM "oxide_plugins" 
WHERE "plugin" = 'RustVehicles' 
  AND time > now() - 24h
```

### Option 2: Update Grafana Dashboard Queries

To update your existing dashboard panels to use the new metrics, modify the queries in `Grafana-Dashboard.json`:

#### Example: Update "Plugin Hook Times" Panel

**Current query** (uses `hookTime` which includes initialization):
```json
"select": [
  [
    {
      "params": ["hookTime"],
      "type": "field"
    },
    {
      "params": [],
      "type": "mean"
    }
  ]
]
```

**Updated query** (uses `avgRunningTime` which excludes initialization):
```json
"select": [
  [
    {
      "params": ["avgRunningTime"],
      "type": "field"
    },
    {
      "params": [],
      "type": "mean"
    },
    {
      "params": ["Average Runtime (excl. init)"],
      "type": "alias"
    }
  ]
]
```

#### Example: Add New Panel for Initialization Times

You can add a new panel to show initialization times:

```json
{
  "targets": [
    {
      "measurement": "oxide_plugins",
      "select": [
        [
          {
            "params": ["initTime"],
            "type": "field"
          },
          {
            "params": [],
            "type": "last"
          }
        ]
      ],
      "groupBy": [
        {
          "params": ["plugin"],
          "type": "tag"
        }
      ],
      "tags": [
        {
          "key": "server",
          "operator": "=~",
          "value": "/^$server$/"
        }
      ],
      "where": [
        {
          "key": "initTime",
          "value": "IS NOT NULL"
        }
      ]
    }
  ],
  "title": "Plugin Initialization Times",
  "type": "table"
}
```

## When Will Data Appear?

1. **After deploying the updated mod**: The new metrics will start being collected
2. **Initialization period**: First 2 minutes after server start - metrics tracked as `initTime`
3. **After initialization**: Runtime metrics (`avgRunningTime`, `peakRunningTime`, `minRunningTime`) will start appearing
4. **In Grafana**: Data will appear in InfluxDB immediately, but you may need to:
   - Wait for the initialization period to complete (2 minutes)
   - Update your dashboard queries to use the new field names
   - Refresh your Grafana dashboard

## Verifying Data is Being Collected

1. **Check InfluxDB directly**:
   ```sql
   SHOW FIELD KEYS FROM "oxide_plugins"
   ```
   You should see: `hookTime`, `initTime`, `avgRunningTime`, `peakRunningTime`, `minRunningTime`

2. **Check Grafana Explore**:
   - Go to Explore view
   - Select your InfluxDB datasource
   - Query: `SELECT * FROM "oxide_plugins" WHERE time > now() - 5m LIMIT 100`
   - Look for the new fields in the results

3. **Check Rust Server Logs**:
   - Enable debug logging in `HarmonyData/ServerMetrics/Configuration.json`
   - Look for `[ServerMetrics]` log messages

## Recommended Dashboard Updates

1. **Update existing "Plugin Hook Times" panel** to use `avgRunningTime` instead of `hookTime`
2. **Add new panel** showing `initTime` (bar chart or table)
3. **Add new panel** showing `peakRunningTime` vs `avgRunningTime` (comparison)
4. **Add alert** for plugins with high `avgRunningTime` (e.g., > 100ms)
