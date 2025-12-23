-- Query to retrieve only the relevant and important data displayed on the Nano 3S webpage
-- This includes data for the top metric cards, charts, and status cards

SELECT TOP (1000) 
    [Id],
    [Timestamp],
    
    -- Hashrate Metrics (for Top Cards and Hashrate Chart)
    [MHS_5s] AS RealtimeHashrate,  -- Real-time hashrate (MH/s, convert to TH/s by dividing by 1,000,000)
    [MHS_av] AS AverageHashrate,   -- Average hashrate (MH/s)
    [GHSspd] AS CurrentHashrateGHs, -- Current hashrate in GH/s
    
    -- Temperature Metrics (for Top Cards and Temperature Charts)
    [OTemp] AS OperatingTemperature,  -- Operating temperature (°C)
    [TAvg] AS AverageTemperature,     -- Average temperature (°C)
    [TMax] AS MaxTemperature,        -- Max temperature (°C)
    
    -- Share Statistics (for Top Cards)
    [Accepted] AS AcceptedShares,
    [Rejected] AS RejectedShares,
    [RejectedPercentage] AS RejectedPercentage,
    ([Accepted] + [Rejected]) AS TotalShares,  -- Calculated total shares
    
    -- Power and Fan (useful status information)
    [Power] AS PowerConsumption,  -- Power in watts
    [Fan1] AS FanSpeedRPM,       -- Fan speed in RPM
    [FanR] AS FanSpeedPercent,   -- Fan speed percentage
    
    -- Status Information (for Status Cards)
    [Elapsed] AS ElapsedTime,    -- Elapsed time in seconds
    [WORKMODE] AS WorkingMode,    -- Working mode (1 = Low, 2 = High)
    [PING] AS PoolPing,          -- Ping to pool (ms)
    
    -- Pool Information (for Status Cards)
    [Pool0_URL] AS PoolAddress,
    [Pool0_User] AS PoolWorker,
    [Pool0_Status] AS PoolStatus,
    
    -- Device Information (for Status Cards)
    [MAC] AS MacAddress,
    [LVERSION] AS FirmwareVersion,
    [HWTYPE] AS HardwareType,
    [PROD] AS ProductName
    
FROM [BitAxeMonitor].[dbo].[Nano3SDataPoints]
ORDER BY [Timestamp] DESC;

