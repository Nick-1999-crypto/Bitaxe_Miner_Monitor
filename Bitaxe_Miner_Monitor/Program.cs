using System;
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
