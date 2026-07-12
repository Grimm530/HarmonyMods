# Windows Setup Guide for Rust Server Metrics

This guide will walk you through installing and configuring InfluxDB 1.8 and Grafana v9+ on Windows for the Rust Server Metrics mod.

## Prerequisites

- Windows 10/11
- Administrator privileges
- Internet connection for downloads

---

## Part 1: Installing InfluxDB 1.8

### Step 1: Download InfluxDB 1.8

1. Visit the InfluxDB 1.8 download page: https://portal.influxdata.com/downloads/
2. Select **InfluxDB 1.8.x** (NOT 2.0 or higher - it's not compatible)
3. Download the Windows 64-bit version (`.zip` file)
4. Extract the ZIP file to a location like `C:\InfluxDB\` or `D:\InfluxDB\`

### Step 2: Configure InfluxDB

1. Navigate to the extracted InfluxDB folder
2. Locate the `influxdb.conf` file (it may be named `influxdb.conf.example` - if so, rename it to `influxdb.conf`)
3. Open `influxdb.conf` in a text editor (Notepad++ or VS Code recommended)

4. Find the `[http]` section and ensure it looks like this:
   ```ini
   [http]
     enabled = true
     bind-address = ":8086"
     auth-enabled = true
   ```

5. Find the `[data]` section and add/modify these critical settings:
   ```ini
   [data]
     max-values-per-tag = 0
     max-series-per-database = 0
   ```
   
   **CRITICAL**: These settings MUST be set to 0. Failure to do this will cause metrics submission failures.

6. Save the configuration file

### Step 3: Install InfluxDB as a Windows Service

1. Open PowerShell as Administrator
2. Navigate to your InfluxDB directory:
   ```powershell
   cd C:\InfluxDB
   ```
   (Replace with your actual path)

3. Install InfluxDB as a service:
   ```powershell
   .\influxd.exe config -config influxdb.conf
   .\influxd.exe install -config influxdb.conf
   ```

4. Start the InfluxDB service:
   ```powershell
   net start influxdb
   ```

   Alternatively, you can start it from Services (services.msc) - look for "InfluxDB"

### Step 4: Verify InfluxDB is Running

1. Open a web browser and navigate to: `http://localhost:8086/debug/vars`
2. If you see JSON data, InfluxDB is running correctly

---

## Part 2: Setting Up InfluxDB Database and User

### Step 1: Access InfluxDB CLI

1. Open PowerShell or Command Prompt
2. Navigate to your InfluxDB directory
3. Run:
   ```powershell
   .\influx.exe
   ```

### Step 2: Create Admin User (if not already created)

```influx
CREATE USER grimm530 WITH PASSWORD '!APsMb42sgXSnbt' WITH ALL PRIVILEGES
```

### Step 3: Create the Database

```influx
CREATE DATABASE db01
```

### Step 4: Create Retention Policy

```influx
CREATE RETENTION POLICY "12weeks" ON "db01" DURATION 12w REPLICATION 1 SHARD DURATION 24h DEFAULT
```

This creates a retention policy that:
- Keeps data for 12 weeks
- Uses 24-hour shard groups (recommended for performance)

### Step 5: Grant Permissions to User

```influx
GRANT ALL ON db01 TO grimm530
```

### Step 6: Verify Setup

```influx
SHOW DATABASES
SHOW USERS
```

You should see `db01` in the databases list and `grimm530` in the users list.

### Step 7: Exit InfluxDB CLI

```influx
exit
```

---

## Part 3: Installing Grafana

### Step 1: Download Grafana

1. Visit: https://grafana.com/grafana/download?platform=windows
2. Download Grafana v9.x or higher (Windows 64-bit installer)
3. Run the installer (`grafana-*.exe`)

### Step 2: Install Grafana

1. Follow the installation wizard
2. Accept the license agreement
3. Choose installation directory (default is fine: `C:\Program Files\GrafanaLabs\grafana`)
4. Complete the installation

### Step 3: Start Grafana Service

1. Open Services (press `Win + R`, type `services.msc`, press Enter)
2. Find "Grafana" service
3. Right-click and select "Start" (or set it to "Automatic" so it starts on boot)

### Step 4: Access Grafana Web Interface

1. Open a web browser
2. Navigate to: `http://localhost:3000`
3. Default login credentials:
   - Username: `admin`
   - Password: `admin`
4. You'll be prompted to change the password on first login

---

## Part 4: Configuring Grafana Data Source

### Step 1: Add InfluxDB Data Source

1. In Grafana, click the **Configuration** icon (gear) in the left sidebar
2. Click **Data Sources**
3. Click **Add data source**
4. Select **InfluxDB**

### Step 2: Configure InfluxDB Connection

Fill in the following settings:

- **Name**: `Rust Server Metrics` (or any name you prefer)
- **Query Language**: Select **InfluxQL**
- **URL**: `http://localhost:8086`
- **Database**: `db01`
- **User**: `grimm530`
- **Password**: `!APsMb42sgXSnbt`
- **HTTP Method**: `GET`

### Step 3: Test and Save

1. Scroll down and click **Save & Test**
2. You should see a green success message: "Data source is working"

---

## Part 5: Importing the Grafana Dashboard

### Step 1: Import Dashboard

1. In Grafana, click the **+** icon in the left sidebar
2. Click **Import dashboard**
3. Click **Upload JSON file**
4. Navigate to: `D:\!RustServer\HarmonyMods\Rust-Server-Metrics-1.7.1\res\Grafana-Dashboard.json`
5. Select the file and click **Open**

### Step 2: Configure Dashboard Settings

1. **Name**: Keep default or change to "Rust Server Metrics"
2. **Folder**: Keep default or select a folder
3. **InfluxDB**: Select the data source you created (`Rust Server Metrics`)
4. Click **Import**

### Step 3: Verify Dashboard

1. The dashboard should now be visible
2. You may not see data yet until your Rust server starts sending metrics
3. Check that the data source variable is correctly set

---

## Part 6: Verifying Your Configuration

### Check Your Configuration File

Your configuration file at `D:\!RustServer\HarmonyMods_Data\ServerMetrics\Configuration.json` should match:

```json
{
  "Enabled": true,
  "Influx Database Url": "http://localhost:8086",
  "Influx Database Name": "db01",
  "Influx Database User": "grimm530",
  "Influx Database Password": "!APsMb42sgXSnbt",
  "Server Tag": "svr1_pve",
  "Debug Logging": false,
  "Amount of metrics to submit in each request": 1000,
  "Gather Player Averages (Client FPS, Client Latency, Player FPS, Player Memory, Player Latency, Player Packet Loss)": true
}
```

This configuration looks correct! ✅

---

## Part 7: Testing the Setup

### Step 1: Start Your Rust Server

1. Make sure your Rust server is stopped
2. Ensure `RustServerMetrics.dll` is in the `HarmonyMods` folder
3. Start your Rust server

### Step 2: Reload Configuration

Once the server has started, in the Rust server console, run:
```
servermetrics.reloadcfg
```

### Step 3: Check Status

Check if metrics are being collected:
```
servermetrics.status
```

You should see:
- Mod ready status
- Uploader status
- Number of records in buffer

### Step 4: Verify Data in Grafana

1. Open Grafana dashboard
2. Wait a few minutes for data to start flowing
3. You should see metrics appearing in the dashboard panels

---

## Troubleshooting

### InfluxDB Won't Start

1. Check Windows Event Viewer for errors
2. Verify the `influxdb.conf` file syntax is correct
3. Ensure port 8086 is not in use by another application
4. Try running `influxd.exe` manually from command line to see error messages

### Can't Connect to InfluxDB from Grafana

1. Verify InfluxDB is running: `http://localhost:8086/debug/vars`
2. Check that authentication is enabled in `influxdb.conf`
3. Verify username and password are correct
4. Check Windows Firewall isn't blocking port 8086

### No Data in Grafana

1. Check `servermetrics.status` in Rust server console
2. Verify configuration file is correct
3. Check InfluxDB has data:
   ```powershell
   .\influx.exe
   USE db01
   SHOW MEASUREMENTS
   ```
4. Verify the Grafana data source is using the correct database name

### Metrics Not Being Sent

1. Check `Debug Logging` is set to `true` temporarily to see error messages
2. Verify the InfluxDB URL is accessible from your server
3. Check that `max-values-per-tag` and `max-series-per-database` are both set to `0` in `influxdb.conf`
4. Restart InfluxDB after making config changes

---

## Security Recommendations

### For Production Use

1. **Change Default Passwords**: Use strong, unique passwords
2. **Enable HTTPS**: Set up SSL certificates for InfluxDB (see README.md for details)
3. **Firewall Rules**: Only allow trusted IPs to access port 8086
4. **Regular Backups**: Set up automated backups of your InfluxDB data
5. **Keep Updated**: Regularly update both InfluxDB and Grafana

---

## Useful Commands

### InfluxDB CLI Commands

```influx
# Show all databases
SHOW DATABASES

# Use a database
USE db01

# Show all measurements (tables)
SHOW MEASUREMENTS

# Show retention policies
SHOW RETENTION POLICIES ON db01

# Query recent data
SELECT * FROM "server_metrics" WHERE time > now() - 1h LIMIT 100
```

### Windows Service Management

```powershell
# Start InfluxDB
net start influxdb

# Stop InfluxDB
net stop influxdb

# Start Grafana
net start grafana

# Stop Grafana
net stop grafana
```

---

## Next Steps

1. ✅ InfluxDB installed and configured
2. ✅ Database and user created
3. ✅ Grafana installed
4. ✅ Data source configured
5. ✅ Dashboard imported
6. ⏳ Start your Rust server and verify metrics are flowing

Once everything is set up, your Rust server metrics will be automatically collected and displayed in Grafana!

