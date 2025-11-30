# SQL Server Database Setup for BitAxe Miner Monitor

This folder contains the SQL scripts needed to set up the database for storing BitAxe monitoring data.

## Prerequisites

- SQL Server (LocalDB, Express, or Full version)
- SQL Server Management Studio (SSMS) or any SQL client tool

## Setup Instructions

### Step 1: Create or Select Your Database

1. Open SQL Server Management Studio (SSMS)
2. Connect to your SQL Server instance
3. Create a new database (or use an existing one):
   ```sql
   CREATE DATABASE BitAxeMonitor;
   GO
   ```

### Step 2: Run the Setup Script

1. Open the file `DatabaseSetup.sql` in SSMS
2. **IMPORTANT**: Update line 5 to use your database name:
   ```sql
   USE [YourDatabaseName]; -- Replace with your actual database name (e.g., BitAxeMonitor)
   ```
3. Execute the script (F5 or Execute button)
4. The script will:
   - Create the `BitAxeDataPoints` table
   - Create the `usp_InsertBitAxeDataPoint` stored procedure
   - Create indexes for better query performance

### Step 3: Configure the Application

1. Open `App.config` in your project
2. Update the connection string in the `<connectionStrings>` section:

   **For LocalDB (default):**
   ```xml
   <add name="BitAxeDatabase" 
        connectionString="Server=(localdb)\MSSQLLocalDB;Database=BitAxeMonitor;Integrated Security=true;Connect Timeout=30;" />
   ```

   **For SQL Server Express:**
   ```xml
   <add name="BitAxeDatabase" 
        connectionString="Server=localhost\SQLEXPRESS;Database=BitAxeMonitor;Integrated Security=true;Connect Timeout=30;" />
   ```

   **For SQL Server with username/password:**
   ```xml
   <add name="BitAxeDatabase" 
        connectionString="Server=localhost;Database=BitAxeMonitor;User Id=sa;Password=YourPassword;Connect Timeout=30;" />
   ```

3. Enable SQL logging in `<appSettings>`:
   ```xml
   <add key="EnableSqlLogging" value="true" />
   ```

### Step 4: Test the Connection

1. Build and run the application
2. Check the console output for:
   - `✓ SQL Server connection initialized successfully` (success)
   - Any error messages (check connection string)

## Database Schema

The `BitAxeDataPoints` table stores all monitoring data with:
- **Id**: Primary key (auto-increment)
- **Timestamp**: When the data was recorded
- All BitAxe metrics (temperature, hashrate, power, shares, etc.)

## Stored Procedure

The `usp_InsertBitAxeDataPoint` stored procedure:
- Inserts a new data point with all fields
- Handles NULL values gracefully
- Includes error handling

## Querying Data

Example queries:

```sql
-- Get latest 100 data points
SELECT TOP 100 * FROM BitAxeDataPoints 
ORDER BY Timestamp DESC;

-- Get average hashrate by hour
SELECT 
    DATEADD(HOUR, DATEDIFF(HOUR, 0, Timestamp), 0) AS Hour,
    AVG(HashRateAvg) AS AvgHashrate
FROM BitAxeDataPoints
GROUP BY DATEADD(HOUR, DATEDIFF(HOUR, 0, Timestamp), 0)
ORDER BY Hour DESC;

-- Get temperature statistics
SELECT 
    MIN(Temperature) AS MinTemp,
    AVG(Temperature) AS AvgTemp,
    MAX(Temperature) AS MaxTemp,
    MIN(VrTemp) AS MinVrTemp,
    AVG(VrTemp) AS AvgVrTemp,
    MAX(VrTemp) AS MaxVrTemp
FROM BitAxeDataPoints
WHERE Timestamp >= DATEADD(DAY, -1, GETDATE());
```

## Troubleshooting

### "Table or stored procedure not found"
- Make sure you ran the `DatabaseSetup.sql` script
- Verify you're using the correct database

### "Cannot open database"
- Check your connection string in `App.config`
- Verify the database exists
- Check SQL Server is running

### "Login failed"
- For Integrated Security: Ensure your Windows user has access
- For username/password: Verify credentials

## Disabling SQL Logging

To disable SQL logging without removing the code:
1. Open `App.config`
2. Set `EnableSqlLogging` to `false`:
   ```xml
   <add key="EnableSqlLogging" value="false" />
   ```

