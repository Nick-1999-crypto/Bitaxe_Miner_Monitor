# Avalon Nano 3S - Complete Data Points List

This document lists all possible data points that can be collected from the Avalon Nano 3S via the cgminer API.

## Summary Data (from `summary` command)

### Time & Status
- **Elapsed** - Total elapsed time since miner started (seconds)
- **When** - Timestamp of the response
- **Status** - Response status code (S = Success)
- **Code** - Status code number
- **Msg** - Status message
- **Description** - Response description

### Hashrate Metrics
- **MHS av (Average Hashrate)** - Average hashrate in MH/s (convert to TH/s by dividing by 1,000,000)
- **MHS 5s (5 Second Hashrate)** - Current hashrate over last 5 seconds (MH/s)
- **MHS 1m** - 1 minute average hashrate (MH/s)
- **MHS 5m** - 5 minute average hashrate (MH/s)
- **MHS 15m** - 15 minute average hashrate (MH/s)

### Share Statistics
- **Accepted** - Total accepted shares
- **Rejected** - Total rejected shares
- **Rejected Percentage** - Calculated: (Rejected / (Accepted + Rejected)) * 100
- **Found Blocks** - Number of blocks found
- **Discarded** - Number of discarded shares
- **Stale** - Number of stale shares
- **Diff1 Shares** - Difficulty 1 shares

### Work & Difficulty Metrics
- **Getworks** - Total getwork requests
- **Get Failures** - Number of failed getwork requests
- **Local Work** - Local work units
- **Remote Failures** - Number of remote connection failures
- **Network Blocks** - Current network block count
- **Total MH** - Total megahashes processed
- **Diff1 Work** - Difficulty 1 work units
- **Total Hashes** - Total hashes processed
- **Last Valid Work** - Timestamp of last valid work

### Difficulty Metrics
- **Difficulty Accepted** - Total difficulty of accepted shares
- **Difficulty Rejected** - Total difficulty of rejected shares
- **Difficulty Stale** - Total difficulty of stale shares
- **Last Share Difficulty** - Difficulty of the last share submitted

### Device Statistics
- **Hardware Errors** - Number of hardware errors
- **Utility** - Work utility percentage
- **Work Utility** - Work utility value
- **Device Hardware%** - Device hardware percentage
- **Device Rejected%** - Device rejection percentage
- **Pool Rejected%** - Pool rejection percentage
- **Pool Stale%** - Pool stale percentage
- **Best Share** - Best share difficulty found
- **Last getwork** - Timestamp of last getwork

---

## Extended Statistics (from `estats` command)

### Temperature Metrics
- **OTemp (Operating Temperature)** - Current operating temperature (°C)
- **TMax (Max Temperature)** - Maximum temperature reached (°C)
- **TAvg (Average Temperature)** - Average temperature (°C)
- **ITemp (Internal Temperature)** - Internal temperature (often -273 if not available)

### Fan Metrics
- **Fan1** - Fan 1 speed (RPM)
- **FanR** - Fan speed percentage (%)

### Hashrate (from estats)
- **GHSspd** - Current hashrate in GH/s
- **GHSmm** - MM hashrate in GH/s
- **GHSavg** - Average hashrate in GH/s
- **MGHS** - Mega hashrate in GH/s

### Power & Performance
- **PS (Power Status Array)** - Power status array with multiple values
  - Power is typically the 3rd value (in mW, convert to W by dividing by 1000)
- **DHspd (Device Hash Speed %)** - Device hash speed percentage
- **DH (Device Hash %)** - Device hash percentage
- **DHW (Device Hardware)** - Device hardware value
- **HW** - Hardware errors count
- **MH** - Mega hashes

### System Information
- **Ver (Version)** - Firmware version string
- **LVer (Local Version)** - Local version
- **BVer (Build Version)** - Build version
- **FW (Firmware)** - Firmware type (Release/Debug)
- **Core** - ASIC core identifier (e.g., A3197S)
- **BIN** - Binary/configuration version
- **Freq (Frequency)** - Operating frequency
- **TA** - Temperature adjustment value

### Status Flags
- **WORKMODE** - Working mode (1 = Low, 2 = High)
- **WORKLEVEL** - Working level
- **SoftOFF** - Soft off flag
- **ECHU** - ECHU flag
- **ECMM** - ECMM flag
- **PING** - Ping to pool (ms)

### Advanced Metrics
- **LW (Last Work)** - Last work timestamp
- **BOOTBY** - Boot source identifier
- **MEMFREE** - Free memory (bytes)
- **PFCnt** - Power failure count
- **NETFAIL** - Network failure array
- **SYSTEMSTATU** - System status string
- **TarT (Target Temperature)** - Target temperature setting

### Hardware Specific (PLL, SF, PVT arrays)
- **PLL0** - Phase-locked loop settings array
- **SF0** - Scaling factor array
- **PVT_T0** - PVT temperature array (per chip)
- **PVT_V0** - PVT voltage array (per chip)
- **MW0** - MW values array
- **CRC** - CRC error count
- **COMCRC** - Communication CRC count
- **ATA2** - ATA2 configuration string

---

## Version Information (from `version` command)

### Product Information
- **PROD (Product)** - Product name (e.g., "Avalon Nano3s")
- **MODEL** - Model identifier (e.g., "Nano3s")
- **HWTYPE** - Hardware type (e.g., "N_MM1v1_X1")
- **SWTYPE** - Software type (e.g., "MM319")

### Version Numbers
- **LVERSION (Local Version)** - Local firmware version
- **BVERSION (Build Version)** - Build version
- **CGVERSION (CGMiner Version)** - CGMiner version
- **API** - API version number
- **UPAPI** - Update API version
- **CGMiner** - CGMiner version string

### Device Identifiers
- **MAC** - MAC address
- **DNA** - Device DNA/unique identifier

---

## Pool Information (from `pools` command)

### Pool 0 (Primary Pool)
- **Pool URL** - Pool connection URL (stratum+tcp://...)
- **Pool User** - Worker/username
- **Pool Password** - Pool password
- **Pool Status** - Pool status (Alive, Dead, Disabled)
- **Pool Priority** - Pool priority (0 = highest)
- **Pool Quota** - Pool quota/weight
- **Long Poll** - Long polling enabled (Y/N)

### Pool Statistics (Pool 0)
- **Pool Accepted** - Accepted shares from this pool
- **Pool Rejected** - Rejected shares from this pool
- **Pool Stale** - Stale shares from this pool
- **Pool Getworks** - Getwork requests to this pool
- **Pool Works** - Total work units from this pool
- **Pool Get Failures** - Getwork failures
- **Pool Remote Failures** - Remote connection failures
- **Pool Diff1 Shares** - Difficulty 1 shares from pool

### Pool Timing
- **Pool Last Share Time** - Timestamp of last share submitted
- **Pool Last Share Difficulty** - Difficulty of last share
- **Pool Work Difficulty** - Current work difficulty
- **Pool Stratum Difficulty** - Stratum difficulty setting

### Pool Status Details
- **Pool Best Share** - Best share found from this pool
- **Pool Rejected%** - Pool rejection percentage
- **Pool Stale%** - Pool stale percentage
- **Bad Work** - Number of bad work units
- **Stratum Active** - Stratum connection active (true/false)
- **Stratum URL** - Stratum server URL
- **Has Stratum** - Stratum protocol enabled (true/false)
- **Has Vmask** - Version mask enabled (true/false)
- **Has GBT** - GetBlockTemplate enabled (true/false)

### Block Information (Pool 0)
- **Current Block Height** - Current blockchain height
- **Current Block Version** - Current block version

### Pool 1 & Pool 2 (Backup Pools)
- Same fields as Pool 0, but for backup pools

---

## Currently Displayed Data Points

### Top Metric Cards (5 cards)
1. **Hashrate** - Real-time hashrate (TH/s) with Average
2. **Average Temperature** - Current temp (°C) with Average
3. **Operating Temperature** - Current temp (°C) with Average
4. **Accepted Shares** - Total accepted shares
5. **Rejected Shares** - Total rejected shares

### Charts (3 charts)
1. **Hashrate Chart** - Real-time and average hashrate over time
2. **Average Temperature Chart** - Average temperature over time
3. **Operating Temperature Chart** - Operating temperature over time

### Status Cards (Bottom Grid)
- Working Mode
- Working Status
- ASIC Status
- Power Status
- Pool Status
- Current Pool
- Total Shares
- Elapsed Time
- Pool Address
- Worker
- MAC Address
- Firmware Version
- Hardware Type

### All Available Data Points Section
- Operating Temperature
- Max Temperature
- Average Temperature
- (And many more from the comprehensive list above)

---

## Recommendations for Data to Keep

### Essential Data (Keep)
- Hashrate (real-time & average)
- Temperatures (operating & average)
- Accepted/Rejected shares
- Share rejection percentage
- Working status
- Pool status
- Elapsed time
- Power consumption

### Useful Data (Consider Keeping)
- Fan speed
- Ping to pool
- Hardware type
- Firmware version
- MAC address
- Pool address & worker
- Current pool

### Less Critical Data (Can Remove)
- All the PVT, PLL, SF arrays (hardware internals)
- Individual pool details for pools 1 & 2 (if only using pool 0)
- Detailed difficulty breakdowns
- Network failure arrays
- Version details beyond basic firmware version
- Most of the "All Available Data Points" section

---

## Data Not Currently Collected (But Available)

From the API responses, these fields exist but may not be parsed:
- Individual chip temperatures (PVT_T0 array)
- Individual chip voltages (PVT_V0 array)
- Phase-locked loop settings
- Scaling factors
- Detailed power status array values
- Memory usage details
- Boot source information
- LED configuration
- LCD settings

