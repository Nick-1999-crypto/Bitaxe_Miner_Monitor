-- BitAxe Miner Monitor Database Setup Script
-- This script creates the table and stored procedure for storing BitAxe monitoring data
-- Run this script on your local SQL Server database before using the application

USE [YourDatabaseName]; -- Replace with your actual database name
GO

-- Create the BitAxeDataPoints table
IF OBJECT_ID('dbo.BitAxeDataPoints', 'U') IS NOT NULL
    DROP TABLE dbo.BitAxeDataPoints;
GO

CREATE TABLE dbo.BitAxeDataPoints
(
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    Timestamp DATETIME2(3) NOT NULL DEFAULT GETDATE(),
    
    -- Temperature and Cooling
    Temperature FLOAT NULL,
    VrTemp FLOAT NULL,
    Temp2 FLOAT NULL,
    FanSpeed INT NULL,
    FanRpm INT NULL,
    FanPercentage INT NULL,
    
    -- Hashrate Metrics
    HashRate FLOAT NULL,
    HashRateAvg FLOAT NULL,
    ExpectedHashrate FLOAT NULL,
    
    -- Power and Voltage
    Power FLOAT NULL,
    Voltage FLOAT NULL,
    Current FLOAT NULL,
    CoreVoltage FLOAT NULL,
    CoreVoltageActual FLOAT NULL,
    MaxPower FLOAT NULL,
    NominalVoltage FLOAT NULL,
    
    -- Performance
    Frequency INT NULL,
    OverclockEnabled INT NULL,
    
    -- Shares and Mining Stats
    SharesAccepted BIGINT NULL,
    SharesRejected BIGINT NULL,
    ErrorPercentage FLOAT NULL,
    
    -- Difficulty Metrics
    BestDiff FLOAT NULL,
    BestSessionDiff FLOAT NULL,
    PoolDifficulty FLOAT NULL,
    NetworkDifficulty BIGINT NULL,
    StratumSuggestedDifficulty FLOAT NULL,
    
    -- Uptime
    UptimeSeconds BIGINT NULL,
    UptimeMs BIGINT NULL,
    
    -- Pool/Stratum Configuration
    StratumURL NVARCHAR(500) NULL,
    StratumPort INT NULL,
    StratumUser NVARCHAR(500) NULL,
    IsUsingFallbackStratum INT NULL,
    PoolAddrFamily INT NULL,
    ResponseTime FLOAT NULL,
    
    -- Fallback Pool Configuration
    FallbackStratumURL NVARCHAR(500) NULL,
    FallbackStratumPort INT NULL,
    FallbackStratumUser NVARCHAR(500) NULL,
    
    -- Network Information
    WifiStatus NVARCHAR(50) NULL,
    WifiRSSI INT NULL,
    Ssid NVARCHAR(100) NULL,
    MacAddr NVARCHAR(20) NULL,
    Ipv4 NVARCHAR(50) NULL,
    Ipv6 NVARCHAR(100) NULL,
    
    -- Device Information
    Hostname NVARCHAR(100) NULL,
    Version NVARCHAR(50) NULL,
    AxeOSVersion NVARCHAR(50) NULL,
    BoardVersion NVARCHAR(50) NULL,
    IdfVersion NVARCHAR(50) NULL,
    ASICModel NVARCHAR(50) NULL,
    RunningPartition NVARCHAR(50) NULL,
    Display NVARCHAR(100) NULL,
    
    -- Memory Information
    FreeHeap BIGINT NULL,
    FreeHeapInternal BIGINT NULL,
    FreeHeapSpiram BIGINT NULL,
    
    -- Core and Hardware Info
    SmallCoreCount INT NULL,
    OverheatMode INT NULL,
    
    -- Blockchain Information
    BlockHeight BIGINT NULL,
    BlockFound INT NULL,
    
    -- Indexes for better query performance
    INDEX IX_BitAxeDataPoints_Timestamp NONCLUSTERED (Timestamp),
    INDEX IX_BitAxeDataPoints_Hostname NONCLUSTERED (Hostname),
    INDEX IX_BitAxeDataPoints_Temperature NONCLUSTERED (Temperature),
    INDEX IX_BitAxeDataPoints_HashRate NONCLUSTERED (HashRate)
);
GO

-- Create stored procedure to insert a data point
IF OBJECT_ID('dbo.usp_InsertBitAxeDataPoint', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_InsertBitAxeDataPoint;
GO

CREATE PROCEDURE dbo.usp_InsertBitAxeDataPoint
    @Timestamp DATETIME2(3),
    @Temperature FLOAT = NULL,
    @VrTemp FLOAT = NULL,
    @Temp2 FLOAT = NULL,
    @FanSpeed INT = NULL,
    @FanRpm INT = NULL,
    @FanPercentage INT = NULL,
    @HashRate FLOAT = NULL,
    @HashRateAvg FLOAT = NULL,
    @ExpectedHashrate FLOAT = NULL,
    @Power FLOAT = NULL,
    @Voltage FLOAT = NULL,
    @Current FLOAT = NULL,
    @CoreVoltage FLOAT = NULL,
    @CoreVoltageActual FLOAT = NULL,
    @MaxPower FLOAT = NULL,
    @NominalVoltage FLOAT = NULL,
    @Frequency INT = NULL,
    @OverclockEnabled INT = NULL,
    @SharesAccepted BIGINT = NULL,
    @SharesRejected BIGINT = NULL,
    @ErrorPercentage FLOAT = NULL,
    @BestDiff FLOAT = NULL,
    @BestSessionDiff FLOAT = NULL,
    @PoolDifficulty FLOAT = NULL,
    @NetworkDifficulty BIGINT = NULL,
    @StratumSuggestedDifficulty FLOAT = NULL,
    @UptimeSeconds BIGINT = NULL,
    @UptimeMs BIGINT = NULL,
    @StratumURL NVARCHAR(500) = NULL,
    @StratumPort INT = NULL,
    @StratumUser NVARCHAR(500) = NULL,
    @IsUsingFallbackStratum INT = NULL,
    @PoolAddrFamily INT = NULL,
    @ResponseTime FLOAT = NULL,
    @FallbackStratumURL NVARCHAR(500) = NULL,
    @FallbackStratumPort INT = NULL,
    @FallbackStratumUser NVARCHAR(500) = NULL,
    @WifiStatus NVARCHAR(50) = NULL,
    @WifiRSSI INT = NULL,
    @Ssid NVARCHAR(100) = NULL,
    @MacAddr NVARCHAR(20) = NULL,
    @Ipv4 NVARCHAR(50) = NULL,
    @Ipv6 NVARCHAR(100) = NULL,
    @Hostname NVARCHAR(100) = NULL,
    @Version NVARCHAR(50) = NULL,
    @AxeOSVersion NVARCHAR(50) = NULL,
    @BoardVersion NVARCHAR(50) = NULL,
    @IdfVersion NVARCHAR(50) = NULL,
    @ASICModel NVARCHAR(50) = NULL,
    @RunningPartition NVARCHAR(50) = NULL,
    @Display NVARCHAR(100) = NULL,
    @FreeHeap BIGINT = NULL,
    @FreeHeapInternal BIGINT = NULL,
    @FreeHeapSpiram BIGINT = NULL,
    @SmallCoreCount INT = NULL,
    @OverheatMode INT = NULL,
    @BlockHeight BIGINT = NULL,
    @BlockFound INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        INSERT INTO dbo.BitAxeDataPoints
        (
            Timestamp,
            Temperature, VrTemp, Temp2,
            FanSpeed, FanRpm, FanPercentage,
            HashRate, HashRateAvg, ExpectedHashrate,
            Power, Voltage, Current,
            CoreVoltage, CoreVoltageActual,
            MaxPower, NominalVoltage,
            Frequency, OverclockEnabled,
            SharesAccepted, SharesRejected, ErrorPercentage,
            BestDiff, BestSessionDiff,
            PoolDifficulty, NetworkDifficulty, StratumSuggestedDifficulty,
            UptimeSeconds, UptimeMs,
            StratumURL, StratumPort, StratumUser,
            IsUsingFallbackStratum, PoolAddrFamily, ResponseTime,
            FallbackStratumURL, FallbackStratumPort, FallbackStratumUser,
            WifiStatus, WifiRSSI, Ssid,
            MacAddr, Ipv4, Ipv6,
            Hostname, Version, AxeOSVersion,
            BoardVersion, IdfVersion, ASICModel,
            RunningPartition, Display,
            FreeHeap, FreeHeapInternal, FreeHeapSpiram,
            SmallCoreCount, OverheatMode,
            BlockHeight, BlockFound
        )
        VALUES
        (
            @Timestamp,
            @Temperature, @VrTemp, @Temp2,
            @FanSpeed, @FanRpm, @FanPercentage,
            @HashRate, @HashRateAvg, @ExpectedHashrate,
            @Power, @Voltage, @Current,
            @CoreVoltage, @CoreVoltageActual,
            @MaxPower, @NominalVoltage,
            @Frequency, @OverclockEnabled,
            @SharesAccepted, @SharesRejected, @ErrorPercentage,
            @BestDiff, @BestSessionDiff,
            @PoolDifficulty, @NetworkDifficulty, @StratumSuggestedDifficulty,
            @UptimeSeconds, @UptimeMs,
            @StratumURL, @StratumPort, @StratumUser,
            @IsUsingFallbackStratum, @PoolAddrFamily, @ResponseTime,
            @FallbackStratumURL, @FallbackStratumPort, @FallbackStratumUser,
            @WifiStatus, @WifiRSSI, @Ssid,
            @MacAddr, @Ipv4, @Ipv6,
            @Hostname, @Version, @AxeOSVersion,
            @BoardVersion, @IdfVersion, @ASICModel,
            @RunningPartition, @Display,
            @FreeHeap, @FreeHeapInternal, @FreeHeapSpiram,
            @SmallCoreCount, @OverheatMode,
            @BlockHeight, @BlockFound
        );
        
        RETURN 0; -- Success
    END TRY
    BEGIN CATCH
        -- Log the error (you can customize this to log to a table)
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();
        
        -- Rethrow the error
        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
        RETURN -1; -- Failure
    END CATCH
END
GO

PRINT 'BitAxe DataPoints table and stored procedure created successfully!';
GO

