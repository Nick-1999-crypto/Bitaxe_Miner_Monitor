using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace Bitaxe_Miner_Monitor
{
    internal class Program
    {
        private static readonly HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10) // 10 second timeout
        };
        private static string bitaxeIp = "192.168.0.195"; // REPLACE IP HERE
        private static int refreshInterval = 5; // seconds
        private static HttpListener listener;
        private static int webPort = 8080;
        private static string sessionDataFile = null; // Path to session data file
        private static readonly object fileLock = new object(); // Lock for thread-safe file writing
        private static string sqlConnectionString = null; // SQL Server connection string
        private static bool enableSqlLogging = false; // Whether SQL logging is enabled
        private static readonly object sqlLock = new object(); // Lock for thread-safe SQL operations

        static async Task Main(string[] args)
        {
            // Parse command line arguments
            // Usage: Bitaxe_Miner_Monitor.exe [BITAXE_IP] [REFRESH_INTERVAL] [WEB_PORT]
            if (args.Length > 0)
            {
                bitaxeIp = args[0];
            }

            if (args.Length > 1 && int.TryParse(args[1], out int interval) && interval > 0)
            {
                refreshInterval = interval;
            }

            if (args.Length > 2 && int.TryParse(args[2], out int port) && port > 0 && port < 65536)
            {
                webPort = port;
            }

            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║              BITAXE GAMMA WEB MONITOR                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"BitAxe IP: {bitaxeIp}");
            Console.WriteLine($"Web server: http://localhost:{webPort}");
            Console.WriteLine($"Refresh interval: {refreshInterval} seconds");
            
            // Initialize session data file
            InitializeSessionDataFile();
            
            // Initialize SQL Server connection (non-blocking, errors won't stop startup)
            try
            {
                InitializeSqlConnection();
            }
            catch
            {
                // Silently catch any exceptions - SQL logging will just be disabled
                enableSqlLogging = false;
                sqlConnectionString = null;
            }
            
            Console.WriteLine("\nPress Ctrl+C to stop the server...\n");

            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{webPort}/");
            listener.Start();
            Console.WriteLine($"✓ Web server started on http://localhost:{webPort}\n");

            // Handle requests
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
            {
                try
                {
                        var context = await listener.GetContextAsync();
                        _ = Task.Run(() => HandleRequest(context));
                    }
                    catch (HttpListenerException)
                    {
                        // Listener closed
                        break;
                    }
                }
            });

            Console.WriteLine($"\n✓ Server is ready! Opening http://localhost:{webPort} in your browser...");
            
            // Open browser automatically
            try
            {
                string url = $"http://localhost:{webPort}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                Console.WriteLine($"✓ Browser opened to {url}");
                }
                catch (Exception ex)
                {
                Console.WriteLine($"Could not open browser automatically: {ex.Message}");
                Console.WriteLine($"Please manually open: http://localhost:{webPort}");
            }
            
            Console.WriteLine("\nPress Ctrl+C to stop the server...");

            // Handle Ctrl+C gracefully
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n\nShutting down server...");
                listener?.Stop();
            };

            // Keep the server running until stopped
            try
            {
                while (listener.IsListening)
                {
                    await Task.Delay(1000);
                }
            }
            catch (HttpListenerException)
            {
                // Listener was stopped
            }
            finally
            {
                listener?.Stop();
                listener?.Close();
                Console.WriteLine("Server stopped.");
            }
        }

        static async void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // Enable CORS
                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                return;
            }

                string path = request.Url.AbsolutePath;

                if (path == "/" || path == "/index.html")
                {
                    // Serve HTML page
                    response.ContentType = "text/html; charset=utf-8";
                    // Prevent caching to ensure we always serve the latest HTML
                    response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                    response.AddHeader("Pragma", "no-cache");
                    response.AddHeader("Expires", "0");
                    string html = GetHtmlPage();
                    byte[] buffer = Encoding.UTF8.GetBytes(html);
                    response.ContentLength64 = buffer.Length;
                    response.StatusCode = 200;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else if (path == "/api/stats")
                {
                    try
                    {
                        Console.WriteLine($"[DEBUG] Received /api/stats request");
                        // Serve JSON stats
                        response.ContentType = "application/json; charset=utf-8";
                        // Prevent caching
                        response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                        response.AddHeader("Pragma", "no-cache");
                        response.AddHeader("Expires", "0");
                        
                        Console.WriteLine($"[DEBUG] Fetching stats from BitAxe at http://{bitaxeIp}/api/system/info");
                        var stats = await GetSystemInfo();
                        
                        Console.WriteLine($"[DEBUG] GetSystemInfo returned: {(stats == null ? "null" : "valid object")}");
                        
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = false
                        };
                        
                        // Return null or error object if stats is null
                        string json;
                        try
                        {
                            if (stats != null)
                            {
                                // Use expectedHashrate as fallback for hashRateAvg if hashRateAvg is 0
                                if (stats.HashRateAvg == 0 && stats.ExpectedHashrate > 0)
                                {
                                    stats.HashRateAvg = stats.ExpectedHashrate;
                                }
                                
                                // Save data point to session file
                                SaveDataPoint(stats);
                                
                                // Save data point to SQL Server
                                SaveDataPointToSql(stats);
                                
                                json = JsonSerializer.Serialize(stats, options);
                                Console.WriteLine($"[DEBUG] Stats serialized successfully. Length: {json.Length} chars");
                                Console.WriteLine($"[DEBUG] Temperature: {stats.Temperature}, FanSpeed: {stats.FanSpeed}, FanRpm: {stats.FanRpm}, HashRateAvg: {stats.HashRateAvg}, ExpectedHashrate: {stats.ExpectedHashrate}, UptimeMs: {stats.UptimeMs}");
                            }
                            else
                            {
                                json = "null";
                                Console.WriteLine($"[DEBUG] Stats is null - BitAxe may be unreachable");
                            }
                        }
                        catch (Exception serializeEx)
                        {
                            Console.WriteLine($"[ERROR] Failed to serialize stats: {serializeEx.Message}");
                            Console.WriteLine($"[ERROR] Serialize exception type: {serializeEx.GetType().Name}");
                            throw;
                        }
                        
                        try
                        {
                            byte[] buffer = Encoding.UTF8.GetBytes(json);
                            response.ContentLength64 = buffer.Length;
                            response.StatusCode = 200;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                            Console.WriteLine($"[DEBUG] Response sent successfully ({buffer.Length} bytes)");
                        }
                        catch (Exception writeEx)
                        {
                            Console.WriteLine($"[ERROR] Failed to write response: {writeEx.Message}");
                            throw;
                        }
                    }
                    catch (Exception apiEx)
                    {
                        Console.WriteLine($"[ERROR] Exception in /api/stats handler: {apiEx.Message}");
                        Console.WriteLine($"[ERROR] Exception type: {apiEx.GetType().Name}");
                        if (apiEx.InnerException != null)
                        {
                            Console.WriteLine($"[ERROR] Inner exception: {apiEx.InnerException.Message}");
                        }
                        throw; // Re-throw to be caught by outer catch
                    }
            }
            else
            {
                    response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[ERROR] Exception in HandleRequest:");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Type: {ex.GetType().Name}");
                Exception innerEx = ex.InnerException;
                int depth = 0;
                while (innerEx != null && depth < 5)
                {
                    Console.WriteLine($"Inner Exception (depth {depth}): {innerEx.Message}");
                    Console.WriteLine($"Inner Type: {innerEx.GetType().Name}");
                    if (innerEx is System.Reflection.ReflectionTypeLoadException rtle)
                    {
                        Console.WriteLine($"Loader Exceptions:");
                        foreach (var le in rtle.LoaderExceptions)
                        {
                            if (le != null)
                                Console.WriteLine($"  - {le.GetType().Name}: {le.Message}");
                        }
                    }
                    innerEx = innerEx.InnerException;
                    depth++;
                }
                Console.WriteLine($"Stack trace: {ex.StackTrace}\n");
                
                response.StatusCode = 500;
                try
                {
                    StringBuilder errorJsonBuilder = new StringBuilder();
                    errorJsonBuilder.Append($"{{\"error\": \"{ex.Message.Replace("\"", "\\\"")}\", \"type\": \"{ex.GetType().Name}\"");
                    if (ex.InnerException != null)
                    {
                        errorJsonBuilder.Append($", \"innerException\": \"{ex.InnerException.Message.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")}\"");
                        errorJsonBuilder.Append($", \"innerType\": \"{ex.InnerException.GetType().Name}\"");
                    }
                    if (ex is System.TypeInitializationException typeInitEx && typeInitEx.InnerException != null)
                    {
                        errorJsonBuilder.Append($", \"typeInitializerInnerException\": \"{typeInitEx.InnerException.Message.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")}\"");
                        errorJsonBuilder.Append($", \"typeInitializerInnerType\": \"{typeInitEx.InnerException.GetType().Name}\"");
                    }
                    errorJsonBuilder.Append("}");
                    
                    string errorJson = errorJsonBuilder.ToString();
                    byte[] buffer = Encoding.UTF8.GetBytes(errorJson);
                    response.ContentLength64 = buffer.Length;
                    response.ContentType = "application/json; charset=utf-8";
                    response.AddHeader("Access-Control-Allow-Origin", "*");
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[ERROR] Failed to write error response: {ex2.Message}");
                }
            }
            finally
            {
                try
                {
                    response.Close();
                }
                catch
                {
                    // Ignore close errors
                }
            }
        }

        static string GetHtmlPage()
        {
            // Try multiple locations for the HTML file
            string[] possiblePaths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "index.html"), // Source directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "index.html"),
                "index.html" // Current directory
            };
            
            string htmlTemplate = null;
            string htmlPath = null;
            
            foreach (string path in possiblePaths)
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    htmlPath = fullPath;
                    htmlTemplate = File.ReadAllText(fullPath);
                    Console.WriteLine($"[DEBUG] Loaded HTML from: {fullPath}");
                    break;
                }
            }
            
            if (htmlTemplate == null)
            {
                // Fallback: Use embedded HTML
                Console.WriteLine("[WARNING] index.html not found, using embedded fallback HTML");
                htmlTemplate = @"<!DOCTYPE html>
<html><head><title>BitAxe Monitor</title></head>
<body><h1>BitAxe Gamma Monitor</h1>
<div>Refresh Interval: __REFRESH_INTERVAL__ seconds</div>
<div>BitAxe IP: __BITAXE_IP__</div>
<script>
let refreshInterval = __REFRESH_INTERVAL__ * 1000;
let bitaxeIp = '__BITAXE_IP__';
function fetchStats() { fetch('/api/stats').then(r => r.json()).then(d => console.log(d)); }
setInterval(fetchStats, refreshInterval);
</script></body></html>";
            }
            
            return htmlTemplate
                .Replace("__REFRESH_INTERVAL__", refreshInterval.ToString())
                .Replace("__BITAXE_IP__", bitaxeIp.Replace("'", "\\'"));
        }

        static async Task<BitaxeStats> GetSystemInfo()
        {
            try
            {
                using (var httpResponse = await client.GetAsync($"http://{bitaxeIp}/api/system/info"))
                {
                    // Check if response is successful
                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[WARNING] BitAxe API returned status code: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
                        return null;
                    }
                    
                    var response = await httpResponse.Content.ReadAsStringAsync();
                    
                    // Validate response is not empty or null
                    if (string.IsNullOrWhiteSpace(response))
                    {
                        Console.WriteLine($"[WARNING] BitAxe API returned empty response");
                        return null;
                    }
                    
                    // Check if response looks like valid JSON (starts with { and ends with })
                    response = response.Trim();
                    if (!response.StartsWith("{") || !response.EndsWith("}"))
                    {
                        Console.WriteLine($"[WARNING] BitAxe API returned invalid JSON format. Response length: {response.Length}");
                        Console.WriteLine($"[WARNING] Response preview: {response.Substring(0, Math.Min(100, response.Length))}...");
                        return null;
                    }
                    
                var options = new JsonSerializerOptions
                {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true, // Allow trailing commas in JSON
                        ReadCommentHandling = JsonCommentHandling.Skip // Skip comments if any
                };
                    
                return JsonSerializer.Deserialize<BitaxeStats>(response, options);
            }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"[ERROR] HttpRequestException in GetSystemInfo: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                // Check if it's a timeout or cancellation
                if (ex.InnerException is TimeoutException)
                {
                    Console.WriteLine($"[ERROR] Request timeout in GetSystemInfo: {ex.Message}");
                }
                else
                {
                    Console.WriteLine($"[ERROR] TaskCanceledException in GetSystemInfo: {ex.Message}");
                }
                return null;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[ERROR] JsonException in GetSystemInfo: {ex.Message}");
                Console.WriteLine($"[ERROR] JSON path: {ex.Path ?? "N/A"}, Line: {ex.LineNumber}, Position: {ex.BytePositionInLine}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Unexpected exception in GetSystemInfo: {ex.GetType().Name} - {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[ERROR] Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                }
                return null;
            }
        }

        static void InitializeSessionDataFile()
        {
            try
            {
                // Create C:\Temp directory if it doesn't exist
                string tempDir = @"C:\Temp";
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                    Console.WriteLine($"✓ Created directory: {tempDir}");
                }

                // Create a unique filename with timestamp for this session
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string filename = $"BitAxe_Monitor_Session_{timestamp}.jsonl";
                sessionDataFile = Path.Combine(tempDir, filename);

                // Write header comment as first line (JSONL format supports comments via metadata)
                string header = $"{{\"session_start\":\"{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"bitaxe_ip\":\"{bitaxeIp}\",\"refresh_interval\":{refreshInterval}}}";
                File.WriteAllText(sessionDataFile, header + Environment.NewLine);
                
                Console.WriteLine($"✓ Session data file created: {sessionDataFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to initialize session data file: {ex.Message}");
                sessionDataFile = null; // Disable logging if we can't create the file
            }
        }

        static void SaveDataPoint(BitaxeStats stats)
        {
            if (string.IsNullOrEmpty(sessionDataFile)) return;

            try
            {
                lock (fileLock)
                {
                    // Create a data point object with timestamp - includes all important metrics
                    var dataPoint = new
                    {
                        timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        temperature = stats.Temperature,
                        vrTemp = stats.VrTemp,
                        hashRate = stats.HashRate,
                        hashRateAvg = stats.HashRateAvg,
                        expectedHashrate = stats.ExpectedHashrate,
                        power = stats.Power,
                        voltage = stats.Voltage,
                        current = stats.Current,
                        coreVoltage = stats.CoreVoltage,
                        coreVoltageActual = stats.CoreVoltageActual,
                        frequency = stats.Frequency,
                        fanSpeed = stats.FanSpeed,
                        fanRpm = stats.FanRpm,
                        sharesAccepted = stats.SharesAccepted,
                        sharesRejected = stats.SharesRejected,
                        bestDiff = stats.BestDiff,
                        bestSessionDiff = stats.BestSessionDiff,
                        poolDifficulty = stats.PoolDifficulty,
                        networkDifficulty = stats.NetworkDifficulty,
                        errorPercentage = stats.ErrorPercentage,
                        uptimeMs = stats.UptimeMs,
                        responseTime = stats.ResponseTime,
                        blockHeight = stats.BlockHeight,
                        blockFound = stats.BlockFound,
                        wifiStatus = stats.WifiStatus,
                        wifiRSSI = stats.WifiRSSI,
                        stratumURL = stats.StratumURL,
                        stratumPort = stats.StratumPort,
                        stratumUser = stats.StratumUser,
                        overclockEnabled = stats.OverclockEnabled,
                        hostname = stats.Hostname,
                        version = stats.Version
                    };

                    // Serialize to JSON (compact format)
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = false
                    };
                    string json = JsonSerializer.Serialize(dataPoint, options);

                    // Append to file (JSONL format - one JSON object per line)
                    File.AppendAllText(sessionDataFile, json + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Don't spam console with file write errors, but log occasionally
                if (DateTime.Now.Second % 30 == 0) // Log roughly once per 30 seconds max
                {
                    Console.WriteLine($"[WARNING] Failed to save data point to file: {ex.Message}");
                }
            }
        }

        static void InitializeSqlConnection()
        {
            try
            {
                // Check if SQL logging is enabled in app.config
                string enableSqlSetting = null;
                try
                {
                    enableSqlSetting = ConfigurationManager.AppSettings["EnableSqlLogging"];
                }
                catch (Exception configEx)
                {
                    Console.WriteLine($"[WARNING] Failed to read App.config setting: {configEx.Message}");
                    enableSqlLogging = false;
                    return;
                }

                if (string.IsNullOrEmpty(enableSqlSetting) || !bool.TryParse(enableSqlSetting, out enableSqlLogging))
                {
                    enableSqlLogging = false; // Default to disabled
                }

                if (!enableSqlLogging)
                {
                    Console.WriteLine("ℹ SQL Server logging is disabled in App.config");
                    return;
                }

                // Get connection string from app.config
                ConnectionStringSettings connectionSettings = null;
                try
                {
                    connectionSettings = ConfigurationManager.ConnectionStrings["BitAxeDatabase"];
                }
                catch (Exception connEx)
                {
                    Console.WriteLine($"[WARNING] Failed to read connection string from App.config: {connEx.Message}");
                    Console.WriteLine("[WARNING] SQL logging will be disabled.");
                    enableSqlLogging = false;
                    return;
                }

                if (connectionSettings == null || string.IsNullOrEmpty(connectionSettings.ConnectionString))
                {
                    Console.WriteLine("[WARNING] SQL Server connection string 'BitAxeDatabase' not found in App.config. SQL logging disabled.");
                    enableSqlLogging = false;
                    return;
                }

                sqlConnectionString = connectionSettings.ConnectionString;
                Console.WriteLine("ℹ SQL Server logging is enabled. Connection will be tested on first data save.");
                
                // Don't test connection during startup to avoid blocking
                // Connection will be tested when first saving data, and errors will be handled gracefully
            }
            catch (SqlException sqlEx)
            {
                Console.WriteLine($"[WARNING] SQL Server exception during initialization: {sqlEx.Message}");
                Console.WriteLine("[WARNING] SQL logging will be disabled. SQL Server may not be installed or configured.");
                enableSqlLogging = false;
                sqlConnectionString = null;
            }
            catch (System.Configuration.ConfigurationException configEx)
            {
                Console.WriteLine($"[WARNING] Configuration error: {configEx.Message}");
                Console.WriteLine("[WARNING] SQL logging will be disabled. Please check your App.config file.");
                enableSqlLogging = false;
                sqlConnectionString = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Failed to initialize SQL Server configuration: {ex.Message}");
                Console.WriteLine($"[WARNING] Exception type: {ex.GetType().Name}");
                Console.WriteLine("[WARNING] SQL logging will be disabled. Please check your configuration in App.config");
                enableSqlLogging = false;
                sqlConnectionString = null;
            }
        }

        static void SaveDataPointToSql(BitaxeStats stats)
        {
            if (!enableSqlLogging || string.IsNullOrEmpty(sqlConnectionString))
            {
                return;
            }

                SqlConnection connection = null;
                try
                {
                    lock (sqlLock)
                    {
                        // Create connection
                        connection = new SqlConnection(sqlConnectionString);
                    
                        // Open connection (exceptions will bubble up to outer catch blocks)
                        connection.Open();
                    
                        // Check connection state
                        if (connection.State != ConnectionState.Open)
                        {
                            throw new InvalidOperationException($"Connection is not open. State: {connection.State}");
                        }

                        using (SqlCommand command = new SqlCommand("usp_InsertBitAxeDataPoint", connection))
                        {
                            command.CommandType = CommandType.StoredProcedure;
                            command.CommandTimeout = 30; // 30 second timeout

                            // Add return value parameter (stored procedure returns 0 for success, -1 for failure)
                            SqlParameter returnValueParam = new SqlParameter("@ReturnValue", SqlDbType.Int)
                            {
                                Direction = ParameterDirection.ReturnValue
                            };
                            command.Parameters.Add(returnValueParam);

                            // Add all parameters
                                command.Parameters.AddWithValue("@Timestamp", DateTime.Now);
                                command.Parameters.AddWithValue("@Temperature", (object)stats.Temperature ?? DBNull.Value);
                                command.Parameters.AddWithValue("@VrTemp", (object)stats.VrTemp ?? DBNull.Value);
                                command.Parameters.AddWithValue("@Temp2", (object)stats.Temp2 ?? DBNull.Value);
                                command.Parameters.AddWithValue("@FanSpeed", (object)stats.FanSpeed ?? DBNull.Value);
                                command.Parameters.AddWithValue("@FanRpm", (object)stats.FanRpm ?? DBNull.Value);
                                command.Parameters.AddWithValue("@FanPercentage", (object)stats.FanPercentage ?? DBNull.Value);
                                command.Parameters.AddWithValue("@HashRate", (object)stats.HashRate ?? DBNull.Value);
                                command.Parameters.AddWithValue("@HashRateAvg", (object)stats.HashRateAvg ?? DBNull.Value);
                                command.Parameters.AddWithValue("@ExpectedHashrate", (object)stats.ExpectedHashrate ?? DBNull.Value);
                                command.Parameters.AddWithValue("@Power", (object)stats.Power ?? DBNull.Value);
                                command.Parameters.AddWithValue("@Voltage", (object)stats.Voltage ?? DBNull.Value);
                                command.Parameters.AddWithValue("@Current", (object)stats.Current ?? DBNull.Value);
                                command.Parameters.AddWithValue("@CoreVoltage", (object)stats.CoreVoltage ?? DBNull.Value);
                                command.Parameters.AddWithValue("@CoreVoltageActual", (object)stats.CoreVoltageActual ?? DBNull.Value);
                                command.Parameters.AddWithValue("@MaxPower", (object)stats.MaxPower ?? DBNull.Value);
                                command.Parameters.AddWithValue("@NominalVoltage", (object)stats.NominalVoltage ?? DBNull.Value);
                                command.Parameters.AddWithValue("@Frequency", (object)stats.Frequency ?? DBNull.Value);
                                command.Parameters.AddWithValue("@OverclockEnabled", (object)stats.OverclockEnabled ?? DBNull.Value);
                                command.Parameters.AddWithValue("@SharesAccepted", (object)stats.SharesAccepted ?? DBNull.Value);
                                command.Parameters.AddWithValue("@SharesRejected", (object)stats.SharesRejected ?? DBNull.Value);
                                command.Parameters.AddWithValue("@ErrorPercentage", (object)stats.ErrorPercentage ?? DBNull.Value);
                                command.Parameters.AddWithValue("@BestDiff", (object)stats.BestDiff ?? DBNull.Value);
                                command.Parameters.AddWithValue("@BestSessionDiff", (object)stats.BestSessionDiff ?? DBNull.Value);
                                command.Parameters.AddWithValue("@PoolDifficulty", (object)stats.PoolDifficulty ?? DBNull.Value);
                                command.Parameters.AddWithValue("@NetworkDifficulty", (object)stats.NetworkDifficulty ?? DBNull.Value);
                                command.Parameters.AddWithValue("@StratumSuggestedDifficulty", (object)stats.StratumSuggestedDifficulty ?? DBNull.Value);
                                command.Parameters.AddWithValue("@UptimeSeconds", (object)stats.UptimeSeconds ?? DBNull.Value);
                                command.Parameters.AddWithValue("@UptimeMs", (object)stats.UptimeMs ?? DBNull.Value);
                                command.Parameters.AddWithValue("@StratumURL", string.IsNullOrEmpty(stats.StratumURL) ? (object)DBNull.Value : (object)stats.StratumURL);
                                command.Parameters.AddWithValue("@StratumPort", (object)stats.StratumPort ?? DBNull.Value);
                                command.Parameters.AddWithValue("@StratumUser", string.IsNullOrEmpty(stats.StratumUser) ? (object)DBNull.Value : (object)stats.StratumUser);
                                command.Parameters.AddWithValue("@IsUsingFallbackStratum", (object)stats.IsUsingFallbackStratum ?? DBNull.Value);
                                command.Parameters.AddWithValue("@PoolAddrFamily", (object)stats.PoolAddrFamily ?? DBNull.Value);
                                command.Parameters.AddWithValue("@ResponseTime", (object)stats.ResponseTime ?? DBNull.Value);
                                command.Parameters.AddWithValue("@FallbackStratumURL", string.IsNullOrEmpty(stats.FallbackStratumURL) ? (object)DBNull.Value : (object)stats.FallbackStratumURL);
                                command.Parameters.AddWithValue("@FallbackStratumPort", (object)stats.FallbackStratumPort ?? DBNull.Value);
                                command.Parameters.AddWithValue("@FallbackStratumUser", string.IsNullOrEmpty(stats.FallbackStratumUser) ? (object)DBNull.Value : (object)stats.FallbackStratumUser);
                                command.Parameters.AddWithValue("@WifiStatus", string.IsNullOrEmpty(stats.WifiStatus) ? (object)DBNull.Value : (object)stats.WifiStatus);
                                command.Parameters.AddWithValue("@WifiRSSI", (object)stats.WifiRSSI ?? DBNull.Value);
                                command.Parameters.AddWithValue("@Ssid", string.IsNullOrEmpty(stats.Ssid) ? (object)DBNull.Value : (object)stats.Ssid);
                                command.Parameters.AddWithValue("@MacAddr", string.IsNullOrEmpty(stats.MacAddr) ? (object)DBNull.Value : (object)stats.MacAddr);
                                command.Parameters.AddWithValue("@Ipv4", string.IsNullOrEmpty(stats.Ipv4) ? (object)DBNull.Value : (object)stats.Ipv4);
                                command.Parameters.AddWithValue("@Ipv6", string.IsNullOrEmpty(stats.Ipv6) ? (object)DBNull.Value : (object)stats.Ipv6);
                                command.Parameters.AddWithValue("@Hostname", string.IsNullOrEmpty(stats.Hostname) ? (object)DBNull.Value : (object)stats.Hostname);
                                command.Parameters.AddWithValue("@Version", string.IsNullOrEmpty(stats.Version) ? (object)DBNull.Value : (object)stats.Version);
                                command.Parameters.AddWithValue("@AxeOSVersion", string.IsNullOrEmpty(stats.AxeOSVersion) ? (object)DBNull.Value : (object)stats.AxeOSVersion);
                                command.Parameters.AddWithValue("@BoardVersion", string.IsNullOrEmpty(stats.BoardVersion) ? (object)DBNull.Value : (object)stats.BoardVersion);
                                command.Parameters.AddWithValue("@IdfVersion", string.IsNullOrEmpty(stats.IdfVersion) ? (object)DBNull.Value : (object)stats.IdfVersion);
                                command.Parameters.AddWithValue("@ASICModel", string.IsNullOrEmpty(stats.ASICModel) ? (object)DBNull.Value : (object)stats.ASICModel);
                                command.Parameters.AddWithValue("@RunningPartition", string.IsNullOrEmpty(stats.RunningPartition) ? (object)DBNull.Value : (object)stats.RunningPartition);
                                command.Parameters.AddWithValue("@Display", string.IsNullOrEmpty(stats.Display) ? (object)DBNull.Value : (object)stats.Display);
                                command.Parameters.AddWithValue("@FreeHeap", (object)stats.FreeHeap ?? DBNull.Value);
                                command.Parameters.AddWithValue("@FreeHeapInternal", (object)stats.FreeHeapInternal ?? DBNull.Value);
                                command.Parameters.AddWithValue("@FreeHeapSpiram", (object)stats.FreeHeapSpiram ?? DBNull.Value);
                                command.Parameters.AddWithValue("@SmallCoreCount", (object)stats.SmallCoreCount ?? DBNull.Value);
                                command.Parameters.AddWithValue("@OverheatMode", (object)stats.OverheatMode ?? DBNull.Value);
                                command.Parameters.AddWithValue("@BlockHeight", (object)stats.BlockHeight ?? DBNull.Value);
                                command.Parameters.AddWithValue("@BlockFound", (object)stats.BlockFound ?? DBNull.Value);

                                // Execute the stored procedure
                                try
                                {
                                    command.ExecuteNonQuery();
                                
                                    // Check the return value (0 = success, -1 = failure)
                                    int returnCode = (int)returnValueParam.Value;
                                
                                    if (returnCode != 0)
                                    {
                                        Console.WriteLine($"[SQL ERROR] Stored procedure returned {returnCode} (expected 0). Insert may have failed.");
                                        Console.WriteLine($"[SQL ERROR] The stored procedure indicates the insert was not successful.");
                                        // Log this error but continue - don't disable SQL logging for one failure
                                    }
                                    else
                                    {
                                        // Success - optionally log occasionally for debugging
                                        // Console.WriteLine($"[SQL DEBUG] Data point saved successfully (return code: {returnCode})");
                                    }
                                }
                                catch (SqlException executeEx)
                                {
                                    // Log detailed SQL error information
                                    Console.WriteLine($"[SQL ERROR] Failed to execute stored procedure: {executeEx.Message}");
                                    Console.WriteLine($"[SQL ERROR] Error Number: {executeEx.Number}, State: {executeEx.State}, Line: {executeEx.LineNumber}");
                                    Console.WriteLine($"[SQL ERROR] Procedure: usp_InsertBitAxeDataPoint");
                                
                                    // Handle specific SQL errors
                                    switch (executeEx.Number)
                                    {
                                        case 208: // Invalid object name
                                            Console.WriteLine($"[SQL ERROR] Table or stored procedure 'usp_InsertBitAxeDataPoint' not found.");
                                            Console.WriteLine($"[SQL ERROR] Please run the SQL setup script: SQL/DatabaseSetup.sql");
                                            break;
                                        case 4060: // Cannot open database
                                            Console.WriteLine($"[SQL ERROR] Database does not exist or is not accessible.");
                                            Console.WriteLine($"[SQL ERROR] Check your connection string in App.config");
                                            break;
                                        case 18456: // Login failed
                                            Console.WriteLine($"[SQL ERROR] SQL Server login failed. Check your connection string credentials.");
                                            break;
                                        case -2: // Timeout
                                            Console.WriteLine($"[SQL ERROR] Command timeout. Database may be slow or unavailable.");
                                            break;
                                        default:
                                            if (executeEx.InnerException != null)
                                            {
                                                Console.WriteLine($"[SQL ERROR] Inner Exception: {executeEx.InnerException.Message}");
                                            }
                                            break;
                                    }
                                
                                    // Don't rethrow - errors already logged, just exit
                                }
                                catch (Exception executeEx)
                                {
                                    Console.WriteLine($"[SQL ERROR] Unexpected error executing stored procedure: {executeEx.GetType().Name}");
                                    Console.WriteLine($"[SQL ERROR] Message: {executeEx.Message}");
                                    if (executeEx.InnerException != null)
                                    {
                                        Console.WriteLine($"[SQL ERROR] Inner Exception: {executeEx.InnerException.GetType().Name} - {executeEx.InnerException.Message}");
                                    }
                                    // Errors already logged, exit the lock block
                                }
                            }
                        }
                }           
        
            catch (SqlException sqlEx)
            {
                // Log SQL errors with full details
                Console.WriteLine($"[SQL ERROR] SQL Exception in SaveDataPointToSql: {sqlEx.Message}");
                Console.WriteLine($"[SQL ERROR] Error Number: {sqlEx.Number}, State: {sqlEx.State}");
                Console.WriteLine($"[SQL ERROR] Server: {sqlEx.Server}");
                
                if (sqlEx.Number == 208) // Invalid object name
                {
                    Console.WriteLine($"[SQL ERROR] Table or stored procedure not found. Please run the SQL setup script: SQL/DatabaseSetup.sql");
                }
                
                if (sqlEx.InnerException != null)
                {
                    Console.WriteLine($"[SQL ERROR] Inner Exception: {sqlEx.InnerException.Message}");
                }
            }
            catch (InvalidOperationException invalidOpEx)
            {
                Console.WriteLine($"[SQL ERROR] Invalid operation: {invalidOpEx.Message}");
                Console.WriteLine($"[SQL ERROR] Connection may have been closed or is in an invalid state.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SQL ERROR] Unexpected error in SaveDataPointToSql: {ex.GetType().Name}");
                Console.WriteLine($"[SQL ERROR] Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[SQL ERROR] Inner Exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                }
                Console.WriteLine($"[SQL ERROR] Stack Trace: {ex.StackTrace}");
            }
            finally
            {
                // Ensure connection is properly closed
                if (connection != null)
                {
                    try
                    {
                        if (connection.State == ConnectionState.Open)
                        {
                            connection.Close();
                        }
                    }
                    catch (Exception closeEx)
                    {
                        Console.WriteLine($"[SQL WARNING] Error closing connection: {closeEx.Message}");
                    }
                    finally
                    {
                        connection?.Dispose();
                    }
                }
            }
        }
    }

    public class BitaxeStats
    {
        [JsonPropertyName("power")]
        public double Power { get; set; }

        [JsonPropertyName("voltage")]
        public double Voltage { get; set; }

        [JsonPropertyName("current")]
        public double Current { get; set; }

        [JsonPropertyName("temp")]
        public double Temperature { get; set; }

        [JsonPropertyName("vrTemp")]
        public double VrTemp { get; set; }

        [JsonPropertyName("hashRate")]
        public double HashRate { get; set; }

        [JsonPropertyName("hashRateAvg")]
        public double HashRateAvg { get; set; }

        [JsonPropertyName("expectedHashrate")]
        public double ExpectedHashrate { get; set; }

        [JsonPropertyName("bestDiff")]
        public double BestDiff { get; set; }

        [JsonPropertyName("bestSessionDiff")]
        public double BestSessionDiff { get; set; }

        [JsonPropertyName("freeHeap")]
        public long FreeHeap { get; set; }

        [JsonPropertyName("coreVoltage")]
        public double CoreVoltage { get; set; }

        [JsonPropertyName("coreVoltageActual")]
        public double CoreVoltageActual { get; set; }

        [JsonPropertyName("frequency")]
        public int Frequency { get; set; }

        [JsonPropertyName("ssid")]
        public string Ssid { get; set; } = "";

        [JsonPropertyName("wifiStatus")]
        public string WifiStatus { get; set; } = "";

        [JsonPropertyName("sharesAccepted")]
        public long SharesAccepted { get; set; }

        [JsonPropertyName("sharesRejected")]
        public long SharesRejected { get; set; }

        [JsonPropertyName("uptimeSeconds")]
        public long UptimeSeconds { get; set; }

        [JsonPropertyName("uptimeMs")]
        public long UptimeMs { get; set; }

        [JsonPropertyName("stratumURL")]
        public string StratumURL { get; set; } = "";

        [JsonPropertyName("stratumPort")]
        public int StratumPort { get; set; }

        [JsonPropertyName("stratumUser")]
        public string StratumUser { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("boardVersion")]
        public string BoardVersion { get; set; } = "";

        [JsonPropertyName("runningPartition")]
        public string RunningPartition { get; set; } = "";

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = "";

        [JsonPropertyName("fanspeed")]
        public int FanSpeed { get; set; }

        [JsonPropertyName("fanrpm")]
        public int FanRpm { get; set; }

        [JsonPropertyName("fanperc")]
        public int FanPercentage { get; set; }

        [JsonPropertyName("temp2")]
        public double Temp2 { get; set; }

        [JsonPropertyName("maxPower")]
        public double MaxPower { get; set; }

        [JsonPropertyName("nominalVoltage")]
        public double NominalVoltage { get; set; }

        [JsonPropertyName("errorPercentage")]
        public double ErrorPercentage { get; set; }

        [JsonPropertyName("poolDifficulty")]
        public double PoolDifficulty { get; set; }

        [JsonPropertyName("isUsingFallbackStratum")]
        public int IsUsingFallbackStratum { get; set; }

        [JsonPropertyName("poolAddrFamily")]
        public int PoolAddrFamily { get; set; }

        [JsonPropertyName("freeHeapInternal")]
        public long FreeHeapInternal { get; set; }

        [JsonPropertyName("freeHeapSpiram")]
        public long FreeHeapSpiram { get; set; }

        [JsonPropertyName("wifiRSSI")]
        public int WifiRSSI { get; set; }

        [JsonPropertyName("macAddr")]
        public string MacAddr { get; set; } = "";

        [JsonPropertyName("ipv4")]
        public string Ipv4 { get; set; } = "";

        [JsonPropertyName("ipv6")]
        public string Ipv6 { get; set; } = "";

        [JsonPropertyName("smallCoreCount")]
        public int SmallCoreCount { get; set; }

        [JsonPropertyName("ASICModel")]
        public string ASICModel { get; set; } = "";

        [JsonPropertyName("stratumSuggestedDifficulty")]
        public double StratumSuggestedDifficulty { get; set; }

        [JsonPropertyName("fallbackStratumURL")]
        public string FallbackStratumURL { get; set; } = "";

        [JsonPropertyName("fallbackStratumPort")]
        public int FallbackStratumPort { get; set; }

        [JsonPropertyName("fallbackStratumUser")]
        public string FallbackStratumUser { get; set; } = "";

        [JsonPropertyName("responseTime")]
        public double ResponseTime { get; set; }

        [JsonPropertyName("axeOSVersion")]
        public string AxeOSVersion { get; set; } = "";

        [JsonPropertyName("idfVersion")]
        public string IdfVersion { get; set; } = "";

        [JsonPropertyName("overheat_mode")]
        public int OverheatMode { get; set; }

        [JsonPropertyName("overclockEnabled")]
        public int OverclockEnabled { get; set; }

        [JsonPropertyName("display")]
        public string Display { get; set; } = "";

        [JsonPropertyName("blockFound")]
        public int BlockFound { get; set; }

        [JsonPropertyName("blockHeight")]
        public long BlockHeight { get; set; }

        [JsonPropertyName("networkDifficulty")]
        public long NetworkDifficulty { get; set; }
    }
}
