-- Avalon Nano 3S Miner Monitor Database Setup Script
-- This script creates the table and stored procedure for storing Avalon Nano 3S monitoring data
-- Run this script on your local SQL Server database before using the application

USE BitAxeMonitor; -- Database name for BitAxe and Nano 3S monitoring
GO

-- Create the Nano3SDataPoints table
IF OBJECT_ID('dbo.Nano3SDataPoints', 'U') IS NOT NULL
    DROP TABLE dbo.Nano3SDataPoints;
GO

CREATE TABLE dbo.Nano3SDataPoints
(
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    Timestamp DATETIME2(3) NOT NULL DEFAULT GETDATE(),
    
    -- Time & Status (from summary)
    Elapsed BIGINT NULL, -- Total elapsed time since miner started (seconds)
    When_Time DATETIME2(3) NULL, -- Timestamp of the response
    Status NVARCHAR(10) NULL, -- Response status code (S = Success)
    Code INT NULL, -- Status code number
    Msg NVARCHAR(500) NULL, -- Status message
    Description NVARCHAR(500) NULL, -- Response description
    
    -- Hashrate Metrics (from summary - in MH/s, convert to TH/s by dividing by 1,000,000)
    MHS_av FLOAT NULL, -- Average Hashrate (MH/s)
    MHS_5s FLOAT NULL, -- 5 Second Hashrate (MH/s)
    MHS_1m FLOAT NULL, -- 1 minute average hashrate (MH/s)
    MHS_5m FLOAT NULL, -- 5 minute average hashrate (MH/s)
    MHS_15m FLOAT NULL, -- 15 minute average hashrate (MH/s)
    
    -- Hashrate Metrics (from estats - in GH/s)
    GHSspd FLOAT NULL, -- Current hashrate in GH/s
    GHSmm FLOAT NULL, -- MM hashrate in GH/s
    GHSavg FLOAT NULL, -- Average hashrate in GH/s
    MGHS FLOAT NULL, -- Mega hashrate in GH/s
    
    -- Share Statistics (from summary)
    Accepted BIGINT NULL, -- Total accepted shares
    Rejected BIGINT NULL, -- Total rejected shares
    RejectedPercentage FLOAT NULL, -- Calculated: (Rejected / (Accepted + Rejected)) * 100
    FoundBlocks BIGINT NULL, -- Number of blocks found
    Discarded BIGINT NULL, -- Number of discarded shares
    Stale BIGINT NULL, -- Number of stale shares
    Diff1Shares BIGINT NULL, -- Difficulty 1 shares
    
    -- Work & Difficulty Metrics (from summary)
    Getworks BIGINT NULL, -- Total getwork requests
    GetFailures BIGINT NULL, -- Number of failed getwork requests
    LocalWork BIGINT NULL, -- Local work units
    RemoteFailures BIGINT NULL, -- Number of remote connection failures
    NetworkBlocks BIGINT NULL, -- Current network block count
    TotalMH BIGINT NULL, -- Total megahashes processed
    Diff1Work BIGINT NULL, -- Difficulty 1 work units
    TotalHashes BIGINT NULL, -- Total hashes processed
    LastValidWork DATETIME2(3) NULL, -- Timestamp of last valid work
    
    -- Difficulty Metrics (from summary)
    DifficultyAccepted FLOAT NULL, -- Total difficulty of accepted shares
    DifficultyRejected FLOAT NULL, -- Total difficulty of rejected shares
    DifficultyStale FLOAT NULL, -- Total difficulty of stale shares
    LastShareDifficulty FLOAT NULL, -- Difficulty of the last share submitted
    BestShare FLOAT NULL, -- Best share difficulty found
    
    -- Device Statistics (from summary)
    HardwareErrors BIGINT NULL, -- Number of hardware errors
    Utility FLOAT NULL, -- Work utility percentage
    WorkUtility FLOAT NULL, -- Work utility value
    DeviceHardwarePercent FLOAT NULL, -- Device hardware percentage
    DeviceRejectedPercent FLOAT NULL, -- Device rejection percentage
    PoolRejectedPercent FLOAT NULL, -- Pool rejection percentage
    PoolStalePercent FLOAT NULL, -- Pool stale percentage
    LastGetwork DATETIME2(3) NULL, -- Timestamp of last getwork
    
    -- Temperature Metrics (from estats)
    OTemp FLOAT NULL, -- Operating Temperature (°C)
    TMax FLOAT NULL, -- Max Temperature (°C)
    TAvg FLOAT NULL, -- Average Temperature (°C)
    ITemp FLOAT NULL, -- Internal Temperature (often -273 if not available)
    TarT FLOAT NULL, -- Target Temperature setting
    
    -- Fan Metrics (from estats)
    Fan1 INT NULL, -- Fan 1 speed (RPM)
    FanR FLOAT NULL, -- Fan speed percentage (%)
    
    -- Power & Performance (from estats)
    Power FLOAT NULL, -- Power consumption (W) - extracted from PS array (3rd value, convert from mW)
    PS NVARCHAR(MAX) NULL, -- Power status array (stored as JSON string)
    DHspd FLOAT NULL, -- Device Hash Speed %
    DH FLOAT NULL, -- Device Hash %
    DHW FLOAT NULL, -- Device Hardware value
    HW BIGINT NULL, -- Hardware errors count
    MH BIGINT NULL, -- Mega hashes
    
    -- System Information (from estats)
    Ver NVARCHAR(100) NULL, -- Firmware version string
    LVer NVARCHAR(100) NULL, -- Local version
    BVer NVARCHAR(100) NULL, -- Build version
    FW NVARCHAR(50) NULL, -- Firmware type (Release/Debug)
    Core NVARCHAR(50) NULL, -- ASIC core identifier (e.g., A3197S)
    BIN NVARCHAR(50) NULL, -- Binary/configuration version
    Freq INT NULL, -- Operating frequency
    TA FLOAT NULL, -- Temperature adjustment value
    
    -- Status Flags (from estats)
    WORKMODE INT NULL, -- Working mode (1 = Low, 2 = High)
    WORKLEVEL INT NULL, -- Working level
    SoftOFF INT NULL, -- Soft off flag
    ECHU INT NULL, -- ECHU flag
    ECMM INT NULL, -- ECMM flag
    PING FLOAT NULL, -- Ping to pool (ms)
    
    -- Advanced Metrics (from estats)
    LW DATETIME2(3) NULL, -- Last work timestamp
    BOOTBY NVARCHAR(50) NULL, -- Boot source identifier
    MEMFREE BIGINT NULL, -- Free memory (bytes)
    PFCnt INT NULL, -- Power failure count
    NETFAIL NVARCHAR(MAX) NULL, -- Network failure array (stored as JSON string)
    SYSTEMSTATU NVARCHAR(500) NULL, -- System status string
    
    -- Hardware Specific Arrays (from estats - stored as JSON strings)
    PLL0 NVARCHAR(MAX) NULL, -- Phase-locked loop settings array
    SF0 NVARCHAR(MAX) NULL, -- Scaling factor array
    PVT_T0 NVARCHAR(MAX) NULL, -- PVT temperature array (per chip)
    PVT_V0 NVARCHAR(MAX) NULL, -- PVT voltage array (per chip)
    MW0 NVARCHAR(MAX) NULL, -- MW values array
    CRC BIGINT NULL, -- CRC error count
    COMCRC BIGINT NULL, -- Communication CRC count
    ATA2 NVARCHAR(500) NULL, -- ATA2 configuration string
    
    -- Version Information (from version command)
    PROD NVARCHAR(100) NULL, -- Product name (e.g., "Avalon Nano3s")
    MODEL NVARCHAR(50) NULL, -- Model identifier (e.g., "Nano3s")
    HWTYPE NVARCHAR(100) NULL, -- Hardware type (e.g., "N_MM1v1_X1")
    SWTYPE NVARCHAR(50) NULL, -- Software type (e.g., "MM319")
    LVERSION NVARCHAR(100) NULL, -- Local firmware version
    BVERSION NVARCHAR(100) NULL, -- Build version
    CGVERSION NVARCHAR(100) NULL, -- CGMiner version
    API INT NULL, -- API version number
    UPAPI INT NULL, -- Update API version
    CGMiner NVARCHAR(100) NULL, -- CGMiner version string
    MAC NVARCHAR(20) NULL, -- MAC address
    DNA NVARCHAR(100) NULL, -- Device DNA/unique identifier
    
    -- Pool 0 (Primary Pool) Information (from pools command)
    Pool0_URL NVARCHAR(500) NULL, -- Pool connection URL
    Pool0_User NVARCHAR(500) NULL, -- Worker/username
    Pool0_Password NVARCHAR(500) NULL, -- Pool password
    Pool0_Status NVARCHAR(50) NULL, -- Pool status (Alive, Dead, Disabled)
    Pool0_Priority INT NULL, -- Pool priority (0 = highest)
    Pool0_Quota INT NULL, -- Pool quota/weight
    Pool0_LongPoll NVARCHAR(1) NULL, -- Long polling enabled (Y/N)
    
    -- Pool 0 Statistics
    Pool0_Accepted BIGINT NULL, -- Accepted shares from this pool
    Pool0_Rejected BIGINT NULL, -- Rejected shares from this pool
    Pool0_Stale BIGINT NULL, -- Stale shares from this pool
    Pool0_Getworks BIGINT NULL, -- Getwork requests to this pool
    Pool0_Works BIGINT NULL, -- Total work units from this pool
    Pool0_GetFailures BIGINT NULL, -- Getwork failures
    Pool0_RemoteFailures BIGINT NULL, -- Remote connection failures
    Pool0_Diff1Shares BIGINT NULL, -- Difficulty 1 shares from pool
    
    -- Pool 0 Timing
    Pool0_LastShareTime DATETIME2(3) NULL, -- Timestamp of last share submitted
    Pool0_LastShareDifficulty FLOAT NULL, -- Difficulty of last share
    Pool0_WorkDifficulty FLOAT NULL, -- Current work difficulty
    Pool0_StratumDifficulty FLOAT NULL, -- Stratum difficulty setting
    
    -- Pool 0 Status Details
    Pool0_BestShare FLOAT NULL, -- Best share found from this pool
    Pool0_RejectedPercent FLOAT NULL, -- Pool rejection percentage
    Pool0_StalePercent FLOAT NULL, -- Pool stale percentage
    Pool0_BadWork BIGINT NULL, -- Number of bad work units
    Pool0_StratumActive BIT NULL, -- Stratum connection active
    Pool0_StratumURL NVARCHAR(500) NULL, -- Stratum server URL
    Pool0_HasStratum BIT NULL, -- Stratum protocol enabled
    Pool0_HasVmask BIT NULL, -- Version mask enabled
    Pool0_HasGBT BIT NULL, -- GetBlockTemplate enabled
    
    -- Pool 0 Block Information
    Pool0_CurrentBlockHeight BIGINT NULL, -- Current blockchain height
    Pool0_CurrentBlockVersion INT NULL, -- Current block version
    
    -- Pool 1 (Backup Pool 1) Information
    Pool1_URL NVARCHAR(500) NULL,
    Pool1_User NVARCHAR(500) NULL,
    Pool1_Password NVARCHAR(500) NULL,
    Pool1_Status NVARCHAR(50) NULL,
    Pool1_Priority INT NULL,
    Pool1_Quota INT NULL,
    Pool1_LongPoll NVARCHAR(1) NULL,
    Pool1_Accepted BIGINT NULL,
    Pool1_Rejected BIGINT NULL,
    Pool1_Stale BIGINT NULL,
    Pool1_Getworks BIGINT NULL,
    Pool1_Works BIGINT NULL,
    Pool1_GetFailures BIGINT NULL,
    Pool1_RemoteFailures BIGINT NULL,
    Pool1_Diff1Shares BIGINT NULL,
    Pool1_LastShareTime DATETIME2(3) NULL,
    Pool1_LastShareDifficulty FLOAT NULL,
    Pool1_WorkDifficulty FLOAT NULL,
    Pool1_StratumDifficulty FLOAT NULL,
    Pool1_BestShare FLOAT NULL,
    Pool1_RejectedPercent FLOAT NULL,
    Pool1_StalePercent FLOAT NULL,
    Pool1_BadWork BIGINT NULL,
    Pool1_StratumActive BIT NULL,
    Pool1_StratumURL NVARCHAR(500) NULL,
    Pool1_HasStratum BIT NULL,
    Pool1_HasVmask BIT NULL,
    Pool1_HasGBT BIT NULL,
    Pool1_CurrentBlockHeight BIGINT NULL,
    Pool1_CurrentBlockVersion INT NULL,
    
    -- Pool 2 (Backup Pool 2) Information
    Pool2_URL NVARCHAR(500) NULL,
    Pool2_User NVARCHAR(500) NULL,
    Pool2_Password NVARCHAR(500) NULL,
    Pool2_Status NVARCHAR(50) NULL,
    Pool2_Priority INT NULL,
    Pool2_Quota INT NULL,
    Pool2_LongPoll NVARCHAR(1) NULL,
    Pool2_Accepted BIGINT NULL,
    Pool2_Rejected BIGINT NULL,
    Pool2_Stale BIGINT NULL,
    Pool2_Getworks BIGINT NULL,
    Pool2_Works BIGINT NULL,
    Pool2_GetFailures BIGINT NULL,
    Pool2_RemoteFailures BIGINT NULL,
    Pool2_Diff1Shares BIGINT NULL,
    Pool2_LastShareTime DATETIME2(3) NULL,
    Pool2_LastShareDifficulty FLOAT NULL,
    Pool2_WorkDifficulty FLOAT NULL,
    Pool2_StratumDifficulty FLOAT NULL,
    Pool2_BestShare FLOAT NULL,
    Pool2_RejectedPercent FLOAT NULL,
    Pool2_StalePercent FLOAT NULL,
    Pool2_BadWork BIGINT NULL,
    Pool2_StratumActive BIT NULL,
    Pool2_StratumURL NVARCHAR(500) NULL,
    Pool2_HasStratum BIT NULL,
    Pool2_HasVmask BIT NULL,
    Pool2_HasGBT BIT NULL,
    Pool2_CurrentBlockHeight BIGINT NULL,
    Pool2_CurrentBlockVersion INT NULL,
    
    -- Indexes for better query performance
    INDEX IX_Nano3SDataPoints_Timestamp NONCLUSTERED (Timestamp),
    INDEX IX_Nano3SDataPoints_MAC NONCLUSTERED (MAC),
    INDEX IX_Nano3SDataPoints_OTemp NONCLUSTERED (OTemp),
    INDEX IX_Nano3SDataPoints_MHS_5s NONCLUSTERED (MHS_5s),
    INDEX IX_Nano3SDataPoints_GHSspd NONCLUSTERED (GHSspd)
);
GO

-- Create stored procedure to insert a data point
IF OBJECT_ID('dbo.usp_InsertNano3SDataPoint', 'P') IS NOT NULL
    DROP PROCEDURE dbo.usp_InsertNano3SDataPoint;
GO

CREATE PROCEDURE dbo.usp_InsertNano3SDataPoint
    @Timestamp DATETIME2(3),
    @Elapsed BIGINT = NULL,
    @When_Time DATETIME2(3) = NULL,
    @Status NVARCHAR(10) = NULL,
    @Code INT = NULL,
    @Msg NVARCHAR(500) = NULL,
    @Description NVARCHAR(500) = NULL,
    @MHS_av FLOAT = NULL,
    @MHS_5s FLOAT = NULL,
    @MHS_1m FLOAT = NULL,
    @MHS_5m FLOAT = NULL,
    @MHS_15m FLOAT = NULL,
    @GHSspd FLOAT = NULL,
    @GHSmm FLOAT = NULL,
    @GHSavg FLOAT = NULL,
    @MGHS FLOAT = NULL,
    @Accepted BIGINT = NULL,
    @Rejected BIGINT = NULL,
    @RejectedPercentage FLOAT = NULL,
    @FoundBlocks BIGINT = NULL,
    @Discarded BIGINT = NULL,
    @Stale BIGINT = NULL,
    @Diff1Shares BIGINT = NULL,
    @Getworks BIGINT = NULL,
    @GetFailures BIGINT = NULL,
    @LocalWork BIGINT = NULL,
    @RemoteFailures BIGINT = NULL,
    @NetworkBlocks BIGINT = NULL,
    @TotalMH BIGINT = NULL,
    @Diff1Work BIGINT = NULL,
    @TotalHashes BIGINT = NULL,
    @LastValidWork DATETIME2(3) = NULL,
    @DifficultyAccepted FLOAT = NULL,
    @DifficultyRejected FLOAT = NULL,
    @DifficultyStale FLOAT = NULL,
    @LastShareDifficulty FLOAT = NULL,
    @BestShare FLOAT = NULL,
    @HardwareErrors BIGINT = NULL,
    @Utility FLOAT = NULL,
    @WorkUtility FLOAT = NULL,
    @DeviceHardwarePercent FLOAT = NULL,
    @DeviceRejectedPercent FLOAT = NULL,
    @PoolRejectedPercent FLOAT = NULL,
    @PoolStalePercent FLOAT = NULL,
    @LastGetwork DATETIME2(3) = NULL,
    @OTemp FLOAT = NULL,
    @TMax FLOAT = NULL,
    @TAvg FLOAT = NULL,
    @ITemp FLOAT = NULL,
    @TarT FLOAT = NULL,
    @Fan1 INT = NULL,
    @FanR FLOAT = NULL,
    @Power FLOAT = NULL,
    @PS NVARCHAR(MAX) = NULL,
    @DHspd FLOAT = NULL,
    @DH FLOAT = NULL,
    @DHW FLOAT = NULL,
    @HW BIGINT = NULL,
    @MH BIGINT = NULL,
    @Ver NVARCHAR(100) = NULL,
    @LVer NVARCHAR(100) = NULL,
    @BVer NVARCHAR(100) = NULL,
    @FW NVARCHAR(50) = NULL,
    @Core NVARCHAR(50) = NULL,
    @BIN NVARCHAR(50) = NULL,
    @Freq INT = NULL,
    @TA FLOAT = NULL,
    @WORKMODE INT = NULL,
    @WORKLEVEL INT = NULL,
    @SoftOFF INT = NULL,
    @ECHU INT = NULL,
    @ECMM INT = NULL,
    @PING FLOAT = NULL,
    @LW DATETIME2(3) = NULL,
    @BOOTBY NVARCHAR(50) = NULL,
    @MEMFREE BIGINT = NULL,
    @PFCnt INT = NULL,
    @NETFAIL NVARCHAR(MAX) = NULL,
    @SYSTEMSTATU NVARCHAR(500) = NULL,
    @PLL0 NVARCHAR(MAX) = NULL,
    @SF0 NVARCHAR(MAX) = NULL,
    @PVT_T0 NVARCHAR(MAX) = NULL,
    @PVT_V0 NVARCHAR(MAX) = NULL,
    @MW0 NVARCHAR(MAX) = NULL,
    @CRC BIGINT = NULL,
    @COMCRC BIGINT = NULL,
    @ATA2 NVARCHAR(500) = NULL,
    @PROD NVARCHAR(100) = NULL,
    @MODEL NVARCHAR(50) = NULL,
    @HWTYPE NVARCHAR(100) = NULL,
    @SWTYPE NVARCHAR(50) = NULL,
    @LVERSION NVARCHAR(100) = NULL,
    @BVERSION NVARCHAR(100) = NULL,
    @CGVERSION NVARCHAR(100) = NULL,
    @API INT = NULL,
    @UPAPI INT = NULL,
    @CGMiner NVARCHAR(100) = NULL,
    @MAC NVARCHAR(20) = NULL,
    @DNA NVARCHAR(100) = NULL,
    @Pool0_URL NVARCHAR(500) = NULL,
    @Pool0_User NVARCHAR(500) = NULL,
    @Pool0_Password NVARCHAR(500) = NULL,
    @Pool0_Status NVARCHAR(50) = NULL,
    @Pool0_Priority INT = NULL,
    @Pool0_Quota INT = NULL,
    @Pool0_LongPoll NVARCHAR(1) = NULL,
    @Pool0_Accepted BIGINT = NULL,
    @Pool0_Rejected BIGINT = NULL,
    @Pool0_Stale BIGINT = NULL,
    @Pool0_Getworks BIGINT = NULL,
    @Pool0_Works BIGINT = NULL,
    @Pool0_GetFailures BIGINT = NULL,
    @Pool0_RemoteFailures BIGINT = NULL,
    @Pool0_Diff1Shares BIGINT = NULL,
    @Pool0_LastShareTime DATETIME2(3) = NULL,
    @Pool0_LastShareDifficulty FLOAT = NULL,
    @Pool0_WorkDifficulty FLOAT = NULL,
    @Pool0_StratumDifficulty FLOAT = NULL,
    @Pool0_BestShare FLOAT = NULL,
    @Pool0_RejectedPercent FLOAT = NULL,
    @Pool0_StalePercent FLOAT = NULL,
    @Pool0_BadWork BIGINT = NULL,
    @Pool0_StratumActive BIT = NULL,
    @Pool0_StratumURL NVARCHAR(500) = NULL,
    @Pool0_HasStratum BIT = NULL,
    @Pool0_HasVmask BIT = NULL,
    @Pool0_HasGBT BIT = NULL,
    @Pool0_CurrentBlockHeight BIGINT = NULL,
    @Pool0_CurrentBlockVersion INT = NULL,
    @Pool1_URL NVARCHAR(500) = NULL,
    @Pool1_User NVARCHAR(500) = NULL,
    @Pool1_Password NVARCHAR(500) = NULL,
    @Pool1_Status NVARCHAR(50) = NULL,
    @Pool1_Priority INT = NULL,
    @Pool1_Quota INT = NULL,
    @Pool1_LongPoll NVARCHAR(1) = NULL,
    @Pool1_Accepted BIGINT = NULL,
    @Pool1_Rejected BIGINT = NULL,
    @Pool1_Stale BIGINT = NULL,
    @Pool1_Getworks BIGINT = NULL,
    @Pool1_Works BIGINT = NULL,
    @Pool1_GetFailures BIGINT = NULL,
    @Pool1_RemoteFailures BIGINT = NULL,
    @Pool1_Diff1Shares BIGINT = NULL,
    @Pool1_LastShareTime DATETIME2(3) = NULL,
    @Pool1_LastShareDifficulty FLOAT = NULL,
    @Pool1_WorkDifficulty FLOAT = NULL,
    @Pool1_StratumDifficulty FLOAT = NULL,
    @Pool1_BestShare FLOAT = NULL,
    @Pool1_RejectedPercent FLOAT = NULL,
    @Pool1_StalePercent FLOAT = NULL,
    @Pool1_BadWork BIGINT = NULL,
    @Pool1_StratumActive BIT = NULL,
    @Pool1_StratumURL NVARCHAR(500) = NULL,
    @Pool1_HasStratum BIT = NULL,
    @Pool1_HasVmask BIT = NULL,
    @Pool1_HasGBT BIT = NULL,
    @Pool1_CurrentBlockHeight BIGINT = NULL,
    @Pool1_CurrentBlockVersion INT = NULL,
    @Pool2_URL NVARCHAR(500) = NULL,
    @Pool2_User NVARCHAR(500) = NULL,
    @Pool2_Password NVARCHAR(500) = NULL,
    @Pool2_Status NVARCHAR(50) = NULL,
    @Pool2_Priority INT = NULL,
    @Pool2_Quota INT = NULL,
    @Pool2_LongPoll NVARCHAR(1) = NULL,
    @Pool2_Accepted BIGINT = NULL,
    @Pool2_Rejected BIGINT = NULL,
    @Pool2_Stale BIGINT = NULL,
    @Pool2_Getworks BIGINT = NULL,
    @Pool2_Works BIGINT = NULL,
    @Pool2_GetFailures BIGINT = NULL,
    @Pool2_RemoteFailures BIGINT = NULL,
    @Pool2_Diff1Shares BIGINT = NULL,
    @Pool2_LastShareTime DATETIME2(3) = NULL,
    @Pool2_LastShareDifficulty FLOAT = NULL,
    @Pool2_WorkDifficulty FLOAT = NULL,
    @Pool2_StratumDifficulty FLOAT = NULL,
    @Pool2_BestShare FLOAT = NULL,
    @Pool2_RejectedPercent FLOAT = NULL,
    @Pool2_StalePercent FLOAT = NULL,
    @Pool2_BadWork BIGINT = NULL,
    @Pool2_StratumActive BIT = NULL,
    @Pool2_StratumURL NVARCHAR(500) = NULL,
    @Pool2_HasStratum BIT = NULL,
    @Pool2_HasVmask BIT = NULL,
    @Pool2_HasGBT BIT = NULL,
    @Pool2_CurrentBlockHeight BIGINT = NULL,
    @Pool2_CurrentBlockVersion INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        INSERT INTO dbo.Nano3SDataPoints
        (
            Timestamp,
            Elapsed, When_Time, Status, Code, Msg, Description,
            MHS_av, MHS_5s, MHS_1m, MHS_5m, MHS_15m,
            GHSspd, GHSmm, GHSavg, MGHS,
            Accepted, Rejected, RejectedPercentage, FoundBlocks, Discarded, Stale, Diff1Shares,
            Getworks, GetFailures, LocalWork, RemoteFailures, NetworkBlocks, TotalMH, Diff1Work, TotalHashes, LastValidWork,
            DifficultyAccepted, DifficultyRejected, DifficultyStale, LastShareDifficulty, BestShare,
            HardwareErrors, Utility, WorkUtility, DeviceHardwarePercent, DeviceRejectedPercent, PoolRejectedPercent, PoolStalePercent, LastGetwork,
            OTemp, TMax, TAvg, ITemp, TarT,
            Fan1, FanR,
            Power, PS, DHspd, DH, DHW, HW, MH,
            Ver, LVer, BVer, FW, Core, BIN, Freq, TA,
            WORKMODE, WORKLEVEL, SoftOFF, ECHU, ECMM, PING,
            LW, BOOTBY, MEMFREE, PFCnt, NETFAIL, SYSTEMSTATU,
            PLL0, SF0, PVT_T0, PVT_V0, MW0, CRC, COMCRC, ATA2,
            PROD, MODEL, HWTYPE, SWTYPE, LVERSION, BVERSION, CGVERSION, API, UPAPI, CGMiner, MAC, DNA,
            Pool0_URL, Pool0_User, Pool0_Password, Pool0_Status, Pool0_Priority, Pool0_Quota, Pool0_LongPoll,
            Pool0_Accepted, Pool0_Rejected, Pool0_Stale, Pool0_Getworks, Pool0_Works, Pool0_GetFailures, Pool0_RemoteFailures, Pool0_Diff1Shares,
            Pool0_LastShareTime, Pool0_LastShareDifficulty, Pool0_WorkDifficulty, Pool0_StratumDifficulty,
            Pool0_BestShare, Pool0_RejectedPercent, Pool0_StalePercent, Pool0_BadWork,
            Pool0_StratumActive, Pool0_StratumURL, Pool0_HasStratum, Pool0_HasVmask, Pool0_HasGBT,
            Pool0_CurrentBlockHeight, Pool0_CurrentBlockVersion,
            Pool1_URL, Pool1_User, Pool1_Password, Pool1_Status, Pool1_Priority, Pool1_Quota, Pool1_LongPoll,
            Pool1_Accepted, Pool1_Rejected, Pool1_Stale, Pool1_Getworks, Pool1_Works, Pool1_GetFailures, Pool1_RemoteFailures, Pool1_Diff1Shares,
            Pool1_LastShareTime, Pool1_LastShareDifficulty, Pool1_WorkDifficulty, Pool1_StratumDifficulty,
            Pool1_BestShare, Pool1_RejectedPercent, Pool1_StalePercent, Pool1_BadWork,
            Pool1_StratumActive, Pool1_StratumURL, Pool1_HasStratum, Pool1_HasVmask, Pool1_HasGBT,
            Pool1_CurrentBlockHeight, Pool1_CurrentBlockVersion,
            Pool2_URL, Pool2_User, Pool2_Password, Pool2_Status, Pool2_Priority, Pool2_Quota, Pool2_LongPoll,
            Pool2_Accepted, Pool2_Rejected, Pool2_Stale, Pool2_Getworks, Pool2_Works, Pool2_GetFailures, Pool2_RemoteFailures, Pool2_Diff1Shares,
            Pool2_LastShareTime, Pool2_LastShareDifficulty, Pool2_WorkDifficulty, Pool2_StratumDifficulty,
            Pool2_BestShare, Pool2_RejectedPercent, Pool2_StalePercent, Pool2_BadWork,
            Pool2_StratumActive, Pool2_StratumURL, Pool2_HasStratum, Pool2_HasVmask, Pool2_HasGBT,
            Pool2_CurrentBlockHeight, Pool2_CurrentBlockVersion
        )
        VALUES
        (
            @Timestamp,
            @Elapsed, @When_Time, @Status, @Code, @Msg, @Description,
            @MHS_av, @MHS_5s, @MHS_1m, @MHS_5m, @MHS_15m,
            @GHSspd, @GHSmm, @GHSavg, @MGHS,
            @Accepted, @Rejected, @RejectedPercentage, @FoundBlocks, @Discarded, @Stale, @Diff1Shares,
            @Getworks, @GetFailures, @LocalWork, @RemoteFailures, @NetworkBlocks, @TotalMH, @Diff1Work, @TotalHashes, @LastValidWork,
            @DifficultyAccepted, @DifficultyRejected, @DifficultyStale, @LastShareDifficulty, @BestShare,
            @HardwareErrors, @Utility, @WorkUtility, @DeviceHardwarePercent, @DeviceRejectedPercent, @PoolRejectedPercent, @PoolStalePercent, @LastGetwork,
            @OTemp, @TMax, @TAvg, @ITemp, @TarT,
            @Fan1, @FanR,
            @Power, @PS, @DHspd, @DH, @DHW, @HW, @MH,
            @Ver, @LVer, @BVer, @FW, @Core, @BIN, @Freq, @TA,
            @WORKMODE, @WORKLEVEL, @SoftOFF, @ECHU, @ECMM, @PING,
            @LW, @BOOTBY, @MEMFREE, @PFCnt, @NETFAIL, @SYSTEMSTATU,
            @PLL0, @SF0, @PVT_T0, @PVT_V0, @MW0, @CRC, @COMCRC, @ATA2,
            @PROD, @MODEL, @HWTYPE, @SWTYPE, @LVERSION, @BVERSION, @CGVERSION, @API, @UPAPI, @CGMiner, @MAC, @DNA,
            @Pool0_URL, @Pool0_User, @Pool0_Password, @Pool0_Status, @Pool0_Priority, @Pool0_Quota, @Pool0_LongPoll,
            @Pool0_Accepted, @Pool0_Rejected, @Pool0_Stale, @Pool0_Getworks, @Pool0_Works, @Pool0_GetFailures, @Pool0_RemoteFailures, @Pool0_Diff1Shares,
            @Pool0_LastShareTime, @Pool0_LastShareDifficulty, @Pool0_WorkDifficulty, @Pool0_StratumDifficulty,
            @Pool0_BestShare, @Pool0_RejectedPercent, @Pool0_StalePercent, @Pool0_BadWork,
            @Pool0_StratumActive, @Pool0_StratumURL, @Pool0_HasStratum, @Pool0_HasVmask, @Pool0_HasGBT,
            @Pool0_CurrentBlockHeight, @Pool0_CurrentBlockVersion,
            @Pool1_URL, @Pool1_User, @Pool1_Password, @Pool1_Status, @Pool1_Priority, @Pool1_Quota, @Pool1_LongPoll,
            @Pool1_Accepted, @Pool1_Rejected, @Pool1_Stale, @Pool1_Getworks, @Pool1_Works, @Pool1_GetFailures, @Pool1_RemoteFailures, @Pool1_Diff1Shares,
            @Pool1_LastShareTime, @Pool1_LastShareDifficulty, @Pool1_WorkDifficulty, @Pool1_StratumDifficulty,
            @Pool1_BestShare, @Pool1_RejectedPercent, @Pool1_StalePercent, @Pool1_BadWork,
            @Pool1_StratumActive, @Pool1_StratumURL, @Pool1_HasStratum, @Pool1_HasVmask, @Pool1_HasGBT,
            @Pool1_CurrentBlockHeight, @Pool1_CurrentBlockVersion,
            @Pool2_URL, @Pool2_User, @Pool2_Password, @Pool2_Status, @Pool2_Priority, @Pool2_Quota, @Pool2_LongPoll,
            @Pool2_Accepted, @Pool2_Rejected, @Pool2_Stale, @Pool2_Getworks, @Pool2_Works, @Pool2_GetFailures, @Pool2_RemoteFailures, @Pool2_Diff1Shares,
            @Pool2_LastShareTime, @Pool2_LastShareDifficulty, @Pool2_WorkDifficulty, @Pool2_StratumDifficulty,
            @Pool2_BestShare, @Pool2_RejectedPercent, @Pool2_StalePercent, @Pool2_BadWork,
            @Pool2_StratumActive, @Pool2_StratumURL, @Pool2_HasStratum, @Pool2_HasVmask, @Pool2_HasGBT,
            @Pool2_CurrentBlockHeight, @Pool2_CurrentBlockVersion
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

PRINT 'Avalon Nano 3S DataPoints table and stored procedure created successfully!';
GO

