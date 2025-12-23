using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
        private static HttpClient avalonClient = null; // Separate client for Avalon Nano with cookie support
        private static string bitaxeIp = "192.168.0.196"; // REPLACE IP HERE
        private static string avalonNanoIp = "192.168.0.192"; // REPLACE IP HERE
        private static string avalonAuthCookie = "9e68bc1137fb1b797af81412f2e9c8f3"; // Auth cookie for Avalon Nano
        private static int refreshInterval = 5; // seconds
        private static HttpListener listener;
        private static int webPort = 8080;
        private static string sessionDataFile = null; // Path to session data file
        private static readonly object fileLock = new object(); // Lock for thread-safe file writing
        private static string sqlConnectionString = null; // SQL Server connection string
        private static bool enableSqlLogging = false; // Whether SQL logging is enabled
        private static readonly object sqlLock = new object(); // Lock for thread-safe SQL operations
        
        // Rolling window for Nano 3S hashrate values to calculate accurate average
        // Keep ALL values for the entire session to match webpage calculation exactly
        private static readonly List<double> nano3SHashrateValues = new List<double>();
        private static readonly object nano3SHashrateLock = new object(); // Lock for thread-safe access

        static async Task Main(string[] args)
        {
            // Parse command line arguments
            // Usage: Bitaxe_Miner_Monitor.exe [BITAXE_IP] [REFRESH_INTERVAL] [WEB_PORT] [AVALON_IP] [AVALON_AUTH_COOKIE]
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

            if (args.Length > 3)
            {
                avalonNanoIp = args[3];
            }

            if (args.Length > 4)
            {
                avalonAuthCookie = args[4];
            }

            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║              BITAXE GAMMA WEB MONITOR                        ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"BitAxe IP: {bitaxeIp}");
            Console.WriteLine($"Avalon Nano 3S IP: {avalonNanoIp} (TCP port 4028)");
            Console.WriteLine($"Web server: http://localhost:{webPort}");
            Console.WriteLine($"Refresh interval: {refreshInterval} seconds");
            
            // Initialize Avalon Nano (uses TCP socket API)
            InitializeAvalonClient();
            
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
                else if (path.StartsWith("/images/") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") || path.EndsWith(".png") || path.EndsWith(".gif"))
                {
                    // Serve image files
                    try
                    {
                        string imagePath = path.TrimStart('/');
                        string[] possiblePaths = {
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath),
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", imagePath),
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", imagePath),
                            imagePath
                        };
                        
                        string fullPath = null;
                        foreach (string possiblePath in possiblePaths)
                        {
                            string fullPossiblePath = Path.GetFullPath(possiblePath);
                            if (File.Exists(fullPossiblePath))
                            {
                                fullPath = fullPossiblePath;
                                break;
                            }
                        }
                        
                        if (fullPath != null && File.Exists(fullPath))
                        {
                            byte[] imageBytes = File.ReadAllBytes(fullPath);
                            response.ContentLength64 = imageBytes.Length;
                            
                            // Set content type based on file extension
                            if (path.EndsWith(".jpg") || path.EndsWith(".jpeg"))
                                response.ContentType = "image/jpeg";
                            else if (path.EndsWith(".png"))
                                response.ContentType = "image/png";
                            else if (path.EndsWith(".gif"))
                                response.ContentType = "image/gif";
                            else
                                response.ContentType = "image/jpeg"; // Default
                            
                            response.StatusCode = 200;
                            await response.OutputStream.WriteAsync(imageBytes, 0, imageBytes.Length);
                        }
                        else
                        {
                            response.StatusCode = 404;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Failed to serve image: {ex.Message}");
                        response.StatusCode = 500;
                    }
                }
                else if (path == "/api/avalon/stats")
                {
                    try
                    {
                        // Serve JSON stats for Avalon Nano 3S
                        // Isolate errors so they don't affect other functionality
                        response.ContentType = "application/json; charset=utf-8";
                        response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                        response.AddHeader("Pragma", "no-cache");
                        response.AddHeader("Expires", "0");
                        
                        AvalonNanoStats stats = null;
                        try
                        {
                            stats = await GetAvalonNanoStats();
                        }
                        catch (Exception avalonEx)
                        {
                            // Error handled gracefully - return null without logging to reduce console noise
                            stats = null;
                        }
                        
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = false
                        };
                        
                        string json;
                        if (stats != null)
                        {
                            // Save data point to SQL Server
                            SaveNano3SDataPointToSql(stats);
                            
                            json = JsonSerializer.Serialize(stats, options);
                        }
                        else
                        {
                            json = "null";
                        }
                        
                        byte[] buffer = Encoding.UTF8.GetBytes(json);
                        response.ContentLength64 = buffer.Length;
                        response.StatusCode = 200;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    catch (Exception apiEx)
                    {
                        // Catch all exceptions to prevent them from affecting other endpoints
                        Console.WriteLine($"[ERROR] Exception in /api/avalon/stats handler: {apiEx.Message}");
                        response.StatusCode = 200; // Return 200 with error JSON instead of 500
                        string errorJson = $"{{\"error\": \"{apiEx.Message.Replace("\"", "\\\"")}\"}}";
                        byte[] buffer = Encoding.UTF8.GetBytes(errorJson);
                        response.ContentLength64 = buffer.Length;
                        response.ContentType = "application/json; charset=utf-8";
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                }
                else if (path == "/api/stats")
                {
                    try
                    {
                        // Serve JSON stats for BitAxe
                        response.ContentType = "application/json; charset=utf-8";
                        // Prevent caching
                        response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                        response.AddHeader("Pragma", "no-cache");
                        response.AddHeader("Expires", "0");
                        
                        BitaxeStats stats = null;
                        try
                        {
                            stats = await GetSystemInfo();
                        }
                        catch (Exception bitaxeEx)
                        {
                            // Log error but don't let it break the response
                            Console.WriteLine($"[WARNING] Failed to get BitAxe stats: {bitaxeEx.Message}");
                            Console.WriteLine($"[WARNING] BitAxe may be offline or unreachable at {bitaxeIp}");
                            stats = null;
                        }
                        
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
                            }
                            else
                            {
                                json = "null";
                            }
                        }
                        catch (Exception serializeEx)
                        {
                            Console.WriteLine($"[ERROR] Failed to serialize stats: {serializeEx.Message}");
                            json = "null"; // Return null instead of throwing
                        }
                        
                        try
                        {
                            byte[] buffer = Encoding.UTF8.GetBytes(json);
                            response.ContentLength64 = buffer.Length;
                            response.StatusCode = 200;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                        catch (Exception writeEx)
                        {
                            Console.WriteLine($"[ERROR] Failed to write response: {writeEx.Message}");
                            // Still try to send an error response
                            string errorJson = "null";
                            byte[] buffer = Encoding.UTF8.GetBytes(errorJson);
                            response.ContentLength64 = buffer.Length;
                            response.StatusCode = 200;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                    }
                    catch (Exception apiEx)
                    {
                        // Catch all exceptions to prevent them from affecting other endpoints
                        Console.WriteLine($"[ERROR] Exception in /api/stats handler: {apiEx.Message}");
                        Console.WriteLine($"[ERROR] Exception type: {apiEx.GetType().Name}");
                        if (apiEx.InnerException != null)
                        {
                            Console.WriteLine($"[ERROR] Inner exception: {apiEx.InnerException.Message}");
                        }
                        // Return null JSON instead of throwing
                        try
                        {
                            string errorJson = "null";
                            byte[] buffer = Encoding.UTF8.GetBytes(errorJson);
                            response.ContentLength64 = buffer.Length;
                            response.StatusCode = 200;
                            response.ContentType = "application/json; charset=utf-8";
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                        catch
                        {
                            // If we can't even send an error response, just close
                        }
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

        static void InitializeAvalonClient()
        {
            // No longer needed - we use TCP sockets for cgminer API on port 4028
            // Keeping for backward compatibility but not used
            Console.WriteLine($"✓ Avalon Nano will use TCP socket API on port 4028");
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
                        ReadCommentHandling = JsonCommentHandling.Skip, // Skip comments if any
                        NumberHandling = JsonNumberHandling.AllowReadingFromString // Allow numbers to be read from strings (e.g., "fanspeed": "100")
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
                    Console.WriteLine($"[ERROR] Request timeout in GetSystemInfo: BitAxe at {bitaxeIp} did not respond within 10 seconds");
                    Console.WriteLine($"[ERROR] Check if BitAxe is online and accessible at http://{bitaxeIp}/api/system/info");
                }
                else
                {
                    Console.WriteLine($"[ERROR] TaskCanceledException in GetSystemInfo: {ex.Message}");
                    Console.WriteLine($"[ERROR] BitAxe may be unreachable at {bitaxeIp}");
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

        // Send command to Avalon Nano via TCP socket (cgminer API)
        static async Task<string> SendAvalonTcpCommand(string command)
        {
            TcpClient client = null;
            NetworkStream stream = null;
            try
            {
                // Connect to Avalon Nano on port 4028 (cgminer API port)
                client = new TcpClient();
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                await client.ConnectAsync(avalonNanoIp, 4028);
                
                if (!client.Connected)
                {
                    Console.WriteLine($"[ERROR] Failed to connect to Avalon Nano at {avalonNanoIp}:4028");
                    return null;
                }
                
                stream = client.GetStream();
                stream.ReadTimeout = 5000; // 5 second timeout
                stream.WriteTimeout = 5000;
                
                // Send command - documentation shows echo -n (no newline)
                // Try without newline first, as per the example: echo -n "summary" | socat ...
                byte[] commandBytes = Encoding.ASCII.GetBytes(command);
                await stream.WriteAsync(commandBytes, 0, commandBytes.Length);
                await stream.FlushAsync();
                
                // Read response - cgminer responses can be multi-line or single line
                // Read until we get a complete response (typically ends with | or newline)
                StringBuilder responseBuilder = new StringBuilder();
                byte[] buffer = new byte[4096];
                int totalBytesRead = 0;
                int maxBytes = 8192; // Max response size
                DateTime startTime = DateTime.Now;
                TimeSpan timeout = TimeSpan.FromSeconds(3); // 3 second timeout for reading
                
                // Read until we get a complete response or timeout
                while (totalBytesRead < maxBytes && (DateTime.Now - startTime) < timeout)
                {
                    // Check if data is available before reading
                    if (!stream.DataAvailable)
                    {
                        // Wait a bit for data to arrive
                        await Task.Delay(50);
                        if (!stream.DataAvailable && totalBytesRead > 0)
                        {
                            // We have some data and no more is coming, break
                            break;
                        }
                        if (!stream.DataAvailable && totalBytesRead == 0)
                        {
                            // No data yet, continue waiting
                            continue;
                        }
                    }
                    
                    int bytesRead = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, maxBytes - totalBytesRead));
                    if (bytesRead == 0)
                    {
                        // Connection closed or no more data
                        break;
                    }
                    
                    string chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    responseBuilder.Append(chunk);
                    totalBytesRead += bytesRead;
                    
                    // cgminer responses typically end with | character or newline
                    // If we see a | at the end, we likely have a complete response
                    string currentResponse = responseBuilder.ToString();
                    if (currentResponse.EndsWith("|") || currentResponse.EndsWith("\n"))
                    {
                        // Give it a small moment to see if more data comes
                        await Task.Delay(50);
                        if (!stream.DataAvailable)
                        {
                            break;
                        }
                    }
                }
                
                string response = responseBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(response))
                {
                    return response;
                }
                
                return null;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[ERROR] Socket error connecting to Avalon Nano: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error sending TCP command to Avalon Nano: {ex.Message}");
                return null;
            }
            finally
            {
                try
                {
                    stream?.Close();
                    client?.Close();
                }
                catch { }
            }
        }
        
        // Parse cgminer summary response into AvalonNanoStats
        static AvalonNanoStats ParseAvalonSummary(string summaryResponse)
        {
            if (string.IsNullOrWhiteSpace(summaryResponse))
                return null;
            
            var stats = new AvalonNanoStats();
            
            // Parse key-value pairs from cgminer format
            // Format: STATUS=S,When=...,Code=11,Msg=Summary,...|SUMMARY,Elapsed=1520,MHS av=3054523.99,...
            string[] parts = summaryResponse.Split('|');
            
            // Parse STATUS section (first part)
            if (parts.Length > 0)
            {
                string statusPart = parts[0];
                string[] statusFields = statusPart.Split(',');
                foreach (string field in statusFields)
                {
                    int equalsIndex = field.IndexOf('=');
                    if (equalsIndex <= 0) continue;
                    
                    string key = field.Substring(0, equalsIndex).Trim();
                    string value = field.Substring(equalsIndex + 1).Trim();
                    
                    try
                    {
                        switch (key.ToUpper())
                        {
                            case "STATUS":
                                stats.Status = value;
                                break;
                            case "WHEN":
                                if (long.TryParse(value, out long when))
                                    stats.When = when;
                                break;
                            case "CODE":
                                if (int.TryParse(value, out int code))
                                    stats.Code = code;
                                break;
                            case "MSG":
                                stats.Msg = value;
                                break;
                            case "DESCRIPTION":
                                stats.Description = value;
                                break;
                        }
                    }
                    catch { }
                }
            }
            
            // Parse SUMMARY section (second part)
            if (parts.Length < 2)
                return stats;
            
            string summaryPart = parts[1]; // Get the SUMMARY section
            string[] fields = summaryPart.Split(',');
            
            foreach (string field in fields)
            {
                int equalsIndex = field.IndexOf('=');
                if (equalsIndex <= 0) continue;
                
                string key = field.Substring(0, equalsIndex).Trim();
                string value = field.Substring(equalsIndex + 1).Trim();
                
                try
                {
                    switch (key.ToUpper())
                    {
                        case "ELAPSED":
                            if (long.TryParse(value, out long elapsed))
                                stats.Elapsed = elapsed;
                            break;
                        case "MHS AV":
                        case "MHSAV":
                            if (double.TryParse(value, out double avgHash))
                            {
                                stats.MhsAv = avgHash;
                                stats.AverageHash = avgHash / 1000000.0; // Convert MH/s to TH/s
                            }
                            break;
                        case "MHS 5S":
                        case "MHS5S":
                            if (double.TryParse(value, out double realtimeHash))
                            {
                                stats.Mhs5s = realtimeHash;
                                stats.RealtimeHash = realtimeHash / 1000000.0; // Convert MH/s to TH/s
                            }
                            break;
                        case "MHS 1M":
                        case "MHS1M":
                            // 1 minute average - store for reference
                            if (double.TryParse(value, out double mhs1m))
                            {
                                // Could add a property for this if needed
                            }
                            break;
                        case "MHS 5M":
                        case "MHS5M":
                            // 5 minute average - store for reference
                            if (double.TryParse(value, out double mhs5m))
                            {
                                // Could add a property for this if needed
                            }
                            break;
                        case "MHS 15M":
                        case "MHS15M":
                            // 15 minute average - store for reference
                            if (double.TryParse(value, out double mhs15m))
                            {
                                // Could add a property for this if needed
                            }
                            break;
                        case "ACCEPTED":
                            if (long.TryParse(value, out long accepted))
                                stats.Accepted = accepted;
                            break;
                        case "REJECTED":
                            if (long.TryParse(value, out long rejected))
                                stats.Reject = rejected;
                            break;
                        case "FOUND BLOCKS":
                        case "FOUNDBLOCKS":
                            if (long.TryParse(value, out long foundBlocks))
                                stats.FoundBlocks = foundBlocks;
                            break;
                        case "GETWORKS":
                            if (long.TryParse(value, out long getworks))
                                stats.Getworks = getworks;
                            break;
                        case "DISCARDED":
                            if (long.TryParse(value, out long discarded))
                                stats.Discarded = discarded;
                            break;
                        case "STALE":
                            if (long.TryParse(value, out long stale))
                                stats.Stale = stale;
                            break;
                        case "GET FAILURES":
                        case "GETFAILURES":
                            if (long.TryParse(value, out long getFailures))
                                stats.GetFailures = getFailures;
                            break;
                        case "LOCAL WORK":
                        case "LOCALWORK":
                            if (long.TryParse(value, out long localWork))
                                stats.LocalWork = localWork;
                            break;
                        case "REMOTE FAILURES":
                        case "REMOTEFAILURES":
                            if (long.TryParse(value, out long remoteFailures))
                                stats.RemoteFailures = remoteFailures;
                            break;
                        case "NETWORK BLOCKS":
                        case "NETWORKBLOCKS":
                            if (long.TryParse(value, out long networkBlocks))
                                stats.NetworkBlocks = networkBlocks;
                            break;
                        case "TOTAL MH":
                        case "TOTALMH":
                            if (double.TryParse(value, out double totalMh))
                                stats.TotalMh = totalMh;
                            break;
                        case "DIFF1 WORK":
                        case "DIFF1WORK":
                            if (long.TryParse(value, out long diff1Work))
                                stats.Diff1Work = diff1Work;
                            break;
                        case "DIFFICULTY ACCEPTED":
                        case "DIFFICULTYACCEPTED":
                            if (double.TryParse(value, out double diffAccepted))
                                stats.DifficultyAccepted = diffAccepted;
                            break;
                        case "DIFFICULTY REJECTED":
                        case "DIFFICULTYREJECTED":
                            if (double.TryParse(value, out double diffRejected))
                                stats.DifficultyRejected = diffRejected;
                            break;
                        case "DIFFICULTY STALE":
                        case "DIFFICULTYSTALE":
                            if (double.TryParse(value, out double diffStale))
                                stats.DifficultyStale = diffStale;
                            break;
                        case "LAST SHARE DIFFICULTY":
                        case "LASTSHAREDIFFICULTY":
                            if (double.TryParse(value, out double lastShareDiff))
                                stats.LastShareDifficulty = lastShareDiff;
                            break;
                        case "LAST VALID WORK":
                        case "LASTVALIDWORK":
                            if (long.TryParse(value, out long lastValidWork))
                                stats.LastValidWork = lastValidWork;
                            break;
                        case "TOTAL HASHES":
                        case "TOTALHASHES":
                            if (long.TryParse(value, out long totalHashes))
                                stats.TotalHashes = totalHashes;
                            break;
                        case "DIFF1 SHARES":
                        case "DIFF1SHARES":
                            if (long.TryParse(value, out long diff1Shares))
                                stats.Diff1Shares = diff1Shares;
                            break;
                        case "HARDWARE ERRORS":
                        case "HARDWAREERRORS":
                            // Hardware errors - could add property for this
                            break;
                        case "UTILITY":
                            // Utility - could add property for this
                            break;
                        case "WORK UTILITY":
                        case "WORKUTILITY":
                            // Work utility - could add property for this
                            break;
                        case "BEST SHARE":
                        case "BESTSHARE":
                            if (double.TryParse(value, out double bestShare))
                                stats.BestShare = bestShare;
                            break;
                        case "DEVICE HARDWARE%":
                        case "DEVICEHARDWARE%":
                            // Device hardware percentage - could add property for this
                            break;
                        case "DEVICE REJECTED%":
                        case "DEVICEREJECTED%":
                            // Device rejected percentage - could add property for this
                            break;
                        case "POOL REJECTED%":
                        case "POOLREJECTED%":
                            // Pool rejected percentage - could add property for this
                            break;
                        case "POOL STALE%":
                        case "POOLSTALE%":
                            // Pool stale percentage - could add property for this
                            break;
                        case "LAST GETWORK":
                        case "LASTGETWORK":
                            // Last getwork time - could add property for this
                            break;
                        case "POWER":
                            if (double.TryParse(value, out double power))
                                stats.Power = power; // Power in watts from summary
                            break;
                    }
                }
                catch
                {
                    // Ignore parse errors for individual fields
                }
            }
            
            return stats;
        }
        
        // Parse cgminer estats response for detailed information
        static void ParseAvalonEstats(string estatsResponse, AvalonNanoStats stats)
        {
            if (string.IsNullOrWhiteSpace(estatsResponse) || stats == null)
                return;
            
            // Parse the MM ID0 section which contains detailed stats
            // Format: MM ID0=Ver[...] OTemp[75] TMax[83] TAvg[80] Fan1[1040] FanR[21%] ...
            if (estatsResponse.Contains("MM ID0="))
            {
                int mmStart = estatsResponse.IndexOf("MM ID0=");
                string mmSection = estatsResponse.Substring(mmStart);
                
                // Extract Ver field first
                if (mmSection.Contains("Ver["))
                {
                    int verStart = mmSection.IndexOf("Ver[") + 4;
                    int verEnd = mmSection.IndexOf("]", verStart);
                    if (verEnd > verStart)
                    {
                        stats.Ver = mmSection.Substring(verStart, verEnd - verStart).Trim();
                        stats.MMVer = stats.Ver;
                    }
                }
                
                // Extract values using regex-like parsing
                ExtractValue(mmSection, "OTemp[", "]", out int otemp);
                ExtractValue(mmSection, "TMax[", "]", out int tmax);
                ExtractValue(mmSection, "TAvg[", "]", out int tavg);
                ExtractValue(mmSection, "Fan1[", "]", out int fan1);
                ExtractValue(mmSection, "FanR[", "%]", out int fanR);
                ExtractValue(mmSection, "PING[", "]", out int ping);
                ExtractValue(mmSection, "GHSspd[", "]", out double ghsSpd);
                ExtractValue(mmSection, "WORKMODE[", "]", out int workMode);
                
                // Extract power from PS field: PS[0 0 27505 4 0 3964 132]
                // Power is at index 6 (PS[6] = 132W), which matches the official dashboard value
                if (mmSection.Contains("PS["))
                {
                    int psStart = mmSection.IndexOf("PS[") + 3;
                    int psEnd = mmSection.IndexOf("]", psStart);
                    if (psEnd > psStart)
                    {
                        string psValues = mmSection.Substring(psStart, psEnd - psStart).Trim();
                        stats.PS = psValues; // Store raw PS values
                        
                        string[] psParts = psValues.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        // Power is at index 6 (the 7th value, 0-indexed)
                        if (psParts.Length > 6 && int.TryParse(psParts[6], out int powerValue))
                        {
                            // Only set power from PS field if it hasn't been set from summary response
                            if (stats.Power == 0)
                            {
                                stats.Power = powerValue; // Power is already in watts
                            }
                        }
                    }
                }
                
                // Set values
                stats.OTemp = otemp;
                stats.TMax = tmax;
                stats.TAvg = tavg;
                stats.FanStatus = fan1;
                stats.Fan1 = fan1;
                stats.FanR = fanR;
                stats.Ping = ping;
                stats.GHSspd = ghsSpd;
                stats.WorkingMode = workMode.ToString();
                
                // Try to determine working status from temperature and other factors
                if (otemp > 0 && tmax > 0)
                {
                    stats.WorkingStatus = "1"; // Fine if we have valid temp readings
                }
            }
        }
        
        // Helper to extract integer values from bracket notation like "Key[Value]"
        static void ExtractValue(string text, string prefix, string suffix, out int result)
        {
            result = 0;
            int startIndex = text.IndexOf(prefix);
            if (startIndex >= 0)
            {
                startIndex += prefix.Length;
                int endIndex = text.IndexOf(suffix, startIndex);
                if (endIndex > startIndex)
                {
                    string valueStr = text.Substring(startIndex, endIndex - startIndex).Trim();
                    int.TryParse(valueStr, out result);
                }
            }
        }
        
        // Helper to extract double values from bracket notation like "Key[Value]"
        static void ExtractValue(string text, string prefix, string suffix, out double result)
        {
            result = 0.0;
            int startIndex = text.IndexOf(prefix);
            if (startIndex >= 0)
            {
                startIndex += prefix.Length;
                int endIndex = text.IndexOf(suffix, startIndex);
                if (endIndex > startIndex)
                {
                    string valueStr = text.Substring(startIndex, endIndex - startIndex).Trim();
                    double.TryParse(valueStr, out result);
                }
            }
        }

        static async Task<AvalonNanoStats> GetAvalonNanoStats()
        {
            try
            {
                // Use cgminer API over TCP port 4028
                // First get summary for basic stats
                string summaryResponse = await SendAvalonTcpCommand("summary");
                if (string.IsNullOrWhiteSpace(summaryResponse))
                {
                    return null;
                }
                
                var stats = ParseAvalonSummary(summaryResponse);
                if (stats == null)
                {
                    return null;
                }
                
                // Get detailed stats from estats command
                string estatsResponse = await SendAvalonTcpCommand("estats");
                if (!string.IsNullOrWhiteSpace(estatsResponse))
                {
                    ParseAvalonEstats(estatsResponse, stats);
                }
                
                
                // Get version info for firmware version
                string versionResponse = await SendAvalonTcpCommand("version");
                if (!string.IsNullOrWhiteSpace(versionResponse))
                {
                    // Parse all fields from version response
                    string[] parts = versionResponse.Split('|');
                    foreach (string part in parts)
                    {
                        string[] fields = part.Split(',');
                        foreach (string field in fields)
                        {
                            int equalsIndex = field.IndexOf('=');
                            if (equalsIndex <= 0) continue;
                            
                            string key = field.Substring(0, equalsIndex).Trim();
                            string value = field.Substring(equalsIndex + 1).Trim();
                            
                            try
                            {
                                switch (key.ToUpper())
                                {
                                    case "LVERSION":
                                    case "VERSION":
                                        stats.Version = value;
                                        stats.FwVer = value;
                                        break;
                                    case "MAC":
                                        stats.Mac = value;
                                        break;
                                    case "PROD":
                                        stats.Prod = value;
                                        stats.HwType = value;
                                        break;
                                    case "HWTYPE":
                                        // Store HWTYPE separately, but also use as HwType if Prod not set
                                        if (string.IsNullOrEmpty(stats.HwType))
                                        {
                                            stats.HwType = value;
                                        }
                                        break;
                                    case "MODEL":
                                        // Store model information
                                        break;
                                    case "SWTYPE":
                                        // Store software type
                                        break;
                                    case "API":
                                        stats.Api = value;
                                        break;
                                    case "COMPILER":
                                        stats.Compiler = value;
                                        break;
                                    case "TYPE":
                                        stats.Type = value;
                                        break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                
                // Get pool information
                string poolsResponse = await SendAvalonTcpCommand("pools");
                if (!string.IsNullOrWhiteSpace(poolsResponse))
                {
                    // Parse all pools (POOL=0, POOL=1, POOL=2)
                    string[] poolParts = poolsResponse.Split('|');
                    foreach (string poolPart in poolParts)
                    {
                        if (poolPart.Contains("POOL="))
                        {
                            // Extract pool number
                            int poolNum = -1;
                            if (poolPart.Contains("POOL="))
                            {
                                int poolStart = poolPart.IndexOf("POOL=") + 5;
                                int poolEnd = poolPart.IndexOf(",", poolStart);
                                if (poolEnd > poolStart && int.TryParse(poolPart.Substring(poolStart, poolEnd - poolStart), out poolNum))
                                {
                                    // Parse pool fields
                                    string[] fields = poolPart.Split(',');
                                    foreach (string field in fields)
                                    {
                                        int equalsIndex = field.IndexOf('=');
                                        if (equalsIndex <= 0) continue;
                                        
                                        string key = field.Substring(0, equalsIndex).Trim();
                                        string value = field.Substring(equalsIndex + 1).Trim();
                                        
                                        try
                                        {
                                            switch (key.ToUpper())
                                            {
                                                case "URL":
                                                    if (poolNum == 0) stats.Address = value;
                                                    if (poolNum == 0) stats.PoolUrl = value;
                                                    if (poolNum == 0) stats.Pool1 = value;
                                                    if (poolNum == 1) stats.Pool2 = value;
                                                    if (poolNum == 2) stats.Pool3 = value;
                                                    break;
                                                case "USER":
                                                    if (poolNum == 0) stats.Worker = value;
                                                    if (poolNum == 0) stats.PoolUser = value;
                                                    if (poolNum == 0) stats.Worker1 = value;
                                                    if (poolNum == 1) stats.Worker2 = value;
                                                    if (poolNum == 2) stats.Worker3 = value;
                                                    break;
                                                case "PASS":
                                                case "PASSWORD":
                                                    if (poolNum == 0) stats.PoolPass = value;
                                                    if (poolNum == 0) stats.Passwd1 = value;
                                                    if (poolNum == 1) stats.Passwd2 = value;
                                                    if (poolNum == 2) stats.Passwd3 = value;
                                                    break;
                                                case "STATUS":
                                                    if (poolNum == 0) stats.PoolStatusDetail = value;
                                                    if (value == "Alive" || value == "Enabled") stats.PoolStatus = "1";
                                                    break;
                                                case "PRIORITY":
                                                    if (int.TryParse(value, out int priority))
                                                    {
                                                        if (poolNum == 0) stats.PoolPriority = priority;
                                                    }
                                                    break;
                                                case "QUOTA":
                                                    if (int.TryParse(value, out int quota))
                                                    {
                                                        if (poolNum == 0) stats.PoolQuota = quota;
                                                    }
                                                    break;
                                                case "ACCEPTED":
                                                    if (long.TryParse(value, out long poolAccepted))
                                                    {
                                                        if (poolNum == 0) stats.PoolAccepted = poolAccepted;
                                                    }
                                                    break;
                                                case "REJECTED":
                                                    if (long.TryParse(value, out long poolRejected))
                                                    {
                                                        if (poolNum == 0) stats.PoolRejected = poolRejected;
                                                    }
                                                    break;
                                                case "STALE":
                                                    if (long.TryParse(value, out long poolStale))
                                                    {
                                                        if (poolNum == 0) stats.PoolStale = poolStale;
                                                    }
                                                    break;
                                                case "DIFF":
                                                    if (double.TryParse(value, out double poolDiff))
                                                    {
                                                        if (poolNum == 0) stats.PoolDiff = poolDiff;
                                                    }
                                                    break;
                                                case "LAST SHARE TIME":
                                                case "LASTSHARETIME":
                                                    if (long.TryParse(value, out long lastShareTime))
                                                    {
                                                        if (poolNum == 0) stats.PoolLastShareTime = lastShareTime;
                                                    }
                                                    break;
                                                case "GET FAILURES":
                                                case "GETFAILURES":
                                                    if (long.TryParse(value, out long poolGetFailures))
                                                    {
                                                        if (poolNum == 0) stats.PoolGetFailures = poolGetFailures;
                                                    }
                                                    break;
                                                case "REMOTE FAILURES":
                                                case "REMOTEFAILURES":
                                                    if (long.TryParse(value, out long poolRemoteFailures))
                                                    {
                                                        if (poolNum == 0) stats.PoolRemoteFailures = poolRemoteFailures;
                                                    }
                                                    break;
                                                case "CURRENT BLOCK HEIGHT":
                                                case "CURRENTBLOCKHEIGHT":
                                                    // Block height - could add property
                                                    break;
                                                case "STRATUM ACTIVE":
                                                case "STRATUMACTIVE":
                                                    // Stratum active status
                                                    if (value == "true" && poolNum == 0)
                                                    {
                                                        stats.PoolStatus = "1";
                                                    }
                                                    break;
                                            }
                                        }
                                        catch { }
                                    }
                                    
                                    // Set current pool if this one is active
                                    if (poolPart.Contains("Status=Alive") || poolPart.Contains("Status=Enabled"))
                                    {
                                        stats.CurrentPool = poolNum.ToString();
                                        if (poolNum == 0)
                                        {
                                            stats.PoolStatus = "1"; // Connected
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Calculate rejected percentage
                if (stats.Accepted + stats.Reject > 0)
                {
                    stats.RejectedPercentage = (stats.Reject / (double)(stats.Accepted + stats.Reject)) * 100.0;
                }
                
                // Set status fields based on actual data
                // WorkingStatus is set in ParseAvalonEstats if we have valid temp readings
                // If not set, try to infer from other data
                if (string.IsNullOrEmpty(stats.WorkingStatus))
                {
                    if (stats.OTemp > 0 || stats.Power > 0)
                    {
                        stats.WorkingStatus = "1"; // Fine if we have valid readings
                    }
                }
                
                // AsicStatus - check if we have valid hash rate
                if (string.IsNullOrEmpty(stats.AsicStatus))
                {
                    if (stats.RealtimeHash > 0 || stats.AverageHash > 0)
                    {
                        stats.AsicStatus = "0"; // Fine if hashing
                    }
                }
                
                // PowerStatus - check if we have valid power reading
                if (string.IsNullOrEmpty(stats.PowerStatus))
                {
                    if (stats.Power > 0)
                    {
                        stats.PowerStatus = "0"; // Fine if we have power
                    }
                }
                
                // PoolStatus - check if we have pool info
                if (string.IsNullOrEmpty(stats.PoolStatus))
                {
                    if (!string.IsNullOrEmpty(stats.Address) || !string.IsNullOrEmpty(stats.PoolUrl))
                    {
                        stats.PoolStatus = "1"; // Connected if we have pool info
                    }
                }
                
                // CurrentPool - use parsed value or default
                if (string.IsNullOrEmpty(stats.CurrentPool))
                {
                    stats.CurrentPool = "1"; // Default to pool 1
                }
                
                // Set HwType from Prod if not already set
                if (string.IsNullOrEmpty(stats.HwType) && !string.IsNullOrEmpty(stats.Prod))
                {
                    stats.HwType = stats.Prod;
                }
                
                return stats;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Unexpected exception in GetAvalonNanoStats: {ex.GetType().Name} - {ex.Message}");
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

        static void SaveNano3SDataPointToSql(AvalonNanoStats stats)
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

                    using (SqlCommand command = new SqlCommand("usp_InsertNano3SDataPoint", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.CommandTimeout = 30; // 30 second timeout

                        // Add return value parameter (stored procedure returns 0 for success, -1 for failure)
                        SqlParameter returnValueParam = new SqlParameter("@ReturnValue", SqlDbType.Int)
                        {
                            Direction = ParameterDirection.ReturnValue
                        };
                        command.Parameters.Add(returnValueParam);

                        // Convert When timestamp (Unix timestamp) to DateTime if available
                        DateTime? whenTime = null;
                        if (stats.When > 0)
                        {
                            try
                            {
                                whenTime = DateTimeOffset.FromUnixTimeSeconds(stats.When).DateTime;
                            }
                            catch { }
                        }

                        // Convert LastValidWork timestamp if available
                        DateTime? lastValidWork = null;
                        if (stats.LastValidWork > 0)
                        {
                            try
                            {
                                lastValidWork = DateTimeOffset.FromUnixTimeSeconds(stats.LastValidWork).DateTime;
                            }
                            catch { }
                        }

                        // Convert LastGetwork timestamp if available (would need to be added to AvalonNanoStats if not present)
                        DateTime? lastGetwork = null;
                        // Note: LastGetwork may not be in AvalonNanoStats, leaving as null for now

                        // Add all parameters - Time & Status
                        command.Parameters.AddWithValue("@Timestamp", DateTime.Now);
                        command.Parameters.AddWithValue("@Elapsed", (object)stats.Elapsed ?? DBNull.Value);
                        command.Parameters.AddWithValue("@When_Time", whenTime.HasValue ? (object)whenTime.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@Status", string.IsNullOrEmpty(stats.Status) ? (object)DBNull.Value : (object)stats.Status);
                        command.Parameters.AddWithValue("@Code", (object)stats.Code ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Msg", string.IsNullOrEmpty(stats.Msg) ? (object)DBNull.Value : (object)stats.Msg);
                        command.Parameters.AddWithValue("@Description", string.IsNullOrEmpty(stats.Description) ? (object)DBNull.Value : (object)stats.Description);

                        // Hashrate Metrics (from summary)
                        // Calculate our own average hashrate from ALL session values (like the webpage does)
                        double calculatedAverageHashrate = 0;
                        lock (nano3SHashrateLock)
                        {
                            // Add current real-time hashrate to the session collection
                            double currentHashrate = stats.Mhs5s > 0 ? stats.Mhs5s : (stats.GHSspd > 0 ? stats.GHSspd * 1000 : 0);
                            if (currentHashrate > 0)
                            {
                                nano3SHashrateValues.Add(currentHashrate);
                                
                                // Calculate average from ALL values collected during this session
                                // This matches exactly how the webpage calculates the average
                                if (nano3SHashrateValues.Count > 0)
                                {
                                    calculatedAverageHashrate = nano3SHashrateValues.Sum() / nano3SHashrateValues.Count;
                                }
                            }
                        }
                        
                        // Use calculated average if available, otherwise fall back to device's value
                        double averageHashrateToStore = calculatedAverageHashrate > 0 ? calculatedAverageHashrate : (stats.MhsAv > 0 ? stats.MhsAv : 0);
                        command.Parameters.AddWithValue("@MHS_av", averageHashrateToStore > 0 ? (object)averageHashrateToStore : DBNull.Value);
                        command.Parameters.AddWithValue("@MHS_5s", (object)stats.Mhs5s ?? DBNull.Value);
                        // MHS_1m, MHS_5m, MHS_15m - not currently in AvalonNanoStats, leaving as null
                        command.Parameters.AddWithValue("@MHS_1m", DBNull.Value);
                        command.Parameters.AddWithValue("@MHS_5m", DBNull.Value);
                        command.Parameters.AddWithValue("@MHS_15m", DBNull.Value);

                        // Hashrate Metrics (from estats)
                        command.Parameters.AddWithValue("@GHSspd", (object)stats.GHSspd ?? DBNull.Value);
                        // GHSmm, GHSavg, MGHS - not currently in AvalonNanoStats, leaving as null
                        command.Parameters.AddWithValue("@GHSmm", DBNull.Value);
                        command.Parameters.AddWithValue("@GHSavg", DBNull.Value);
                        command.Parameters.AddWithValue("@MGHS", DBNull.Value);

                        // Share Statistics
                        command.Parameters.AddWithValue("@Accepted", (object)stats.Accepted ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Rejected", (object)stats.Reject ?? DBNull.Value);
                        command.Parameters.AddWithValue("@RejectedPercentage", (object)stats.RejectedPercentage ?? DBNull.Value);
                        command.Parameters.AddWithValue("@FoundBlocks", (object)stats.FoundBlocks ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Discarded", (object)stats.Discarded ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Stale", (object)stats.Stale ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Diff1Shares", (object)stats.Diff1Shares ?? DBNull.Value);

                        // Work & Difficulty Metrics
                        command.Parameters.AddWithValue("@Getworks", (object)stats.Getworks ?? DBNull.Value);
                        command.Parameters.AddWithValue("@GetFailures", (object)stats.GetFailures ?? DBNull.Value);
                        command.Parameters.AddWithValue("@LocalWork", (object)stats.LocalWork ?? DBNull.Value);
                        command.Parameters.AddWithValue("@RemoteFailures", (object)stats.RemoteFailures ?? DBNull.Value);
                        command.Parameters.AddWithValue("@NetworkBlocks", (object)stats.NetworkBlocks ?? DBNull.Value);
                        command.Parameters.AddWithValue("@TotalMH", (object)stats.TotalMh ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Diff1Work", (object)stats.Diff1Work ?? DBNull.Value);
                        command.Parameters.AddWithValue("@TotalHashes", (object)stats.TotalHashes ?? DBNull.Value);
                        command.Parameters.AddWithValue("@LastValidWork", lastValidWork.HasValue ? (object)lastValidWork.Value : DBNull.Value);

                        // Difficulty Metrics
                        command.Parameters.AddWithValue("@DifficultyAccepted", (object)stats.DifficultyAccepted ?? DBNull.Value);
                        command.Parameters.AddWithValue("@DifficultyRejected", (object)stats.DifficultyRejected ?? DBNull.Value);
                        command.Parameters.AddWithValue("@DifficultyStale", (object)stats.DifficultyStale ?? DBNull.Value);
                        command.Parameters.AddWithValue("@LastShareDifficulty", (object)stats.LastShareDifficulty ?? DBNull.Value);
                        command.Parameters.AddWithValue("@BestShare", (object)stats.BestShare ?? DBNull.Value);

                        // Device Statistics - many not in AvalonNanoStats, leaving as null
                        command.Parameters.AddWithValue("@HardwareErrors", DBNull.Value);
                        command.Parameters.AddWithValue("@Utility", DBNull.Value);
                        command.Parameters.AddWithValue("@WorkUtility", DBNull.Value);
                        command.Parameters.AddWithValue("@DeviceHardwarePercent", DBNull.Value);
                        command.Parameters.AddWithValue("@DeviceRejectedPercent", DBNull.Value);
                        command.Parameters.AddWithValue("@PoolRejectedPercent", DBNull.Value);
                        command.Parameters.AddWithValue("@PoolStalePercent", DBNull.Value);
                        command.Parameters.AddWithValue("@LastGetwork", lastGetwork.HasValue ? (object)lastGetwork.Value : DBNull.Value);

                        // Temperature Metrics
                        command.Parameters.AddWithValue("@OTemp", (object)stats.OTemp ?? DBNull.Value);
                        command.Parameters.AddWithValue("@TMax", (object)stats.TMax ?? DBNull.Value);
                        command.Parameters.AddWithValue("@TAvg", (object)stats.TAvg ?? DBNull.Value);
                        command.Parameters.AddWithValue("@ITemp", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@TarT", DBNull.Value); // Not in AvalonNanoStats

                        // Fan Metrics
                        command.Parameters.AddWithValue("@Fan1", (object)stats.Fan1 ?? DBNull.Value);
                        command.Parameters.AddWithValue("@FanR", (object)stats.FanR ?? DBNull.Value);

                        // Power & Performance
                        command.Parameters.AddWithValue("@Power", (object)stats.Power ?? DBNull.Value);
                        command.Parameters.AddWithValue("@PS", string.IsNullOrEmpty(stats.PS) ? (object)DBNull.Value : (object)stats.PS);
                        // DHspd, DH, DHW, HW, MH - not in AvalonNanoStats
                        command.Parameters.AddWithValue("@DHspd", DBNull.Value);
                        command.Parameters.AddWithValue("@DH", DBNull.Value);
                        command.Parameters.AddWithValue("@DHW", DBNull.Value);
                        command.Parameters.AddWithValue("@HW", DBNull.Value);
                        command.Parameters.AddWithValue("@MH", DBNull.Value);

                        // System Information
                        command.Parameters.AddWithValue("@Ver", string.IsNullOrEmpty(stats.Ver) ? (object)DBNull.Value : (object)stats.Ver);
                        command.Parameters.AddWithValue("@LVer", string.IsNullOrEmpty(stats.MMVer) ? (object)DBNull.Value : (object)stats.MMVer);
                        command.Parameters.AddWithValue("@BVer", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@FW", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Core", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@BIN", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Freq", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@TA", DBNull.Value); // Not in AvalonNanoStats

                        // Status Flags
                        int? workMode = null;
                        if (!string.IsNullOrEmpty(stats.WorkingMode) && int.TryParse(stats.WorkingMode, out int wm))
                            workMode = wm;
                        command.Parameters.AddWithValue("@WORKMODE", workMode.HasValue ? (object)workMode.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@WORKLEVEL", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@SoftOFF", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@ECHU", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@ECMM", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@PING", (object)stats.Ping ?? DBNull.Value);

                        // Advanced Metrics
                        command.Parameters.AddWithValue("@LW", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@BOOTBY", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@MEMFREE", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@PFCnt", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@NETFAIL", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@SYSTEMSTATU", string.IsNullOrEmpty(stats.SysStatus) ? (object)DBNull.Value : (object)stats.SysStatus);

                        // Hardware Specific Arrays - not in AvalonNanoStats
                        command.Parameters.AddWithValue("@PLL0", DBNull.Value);
                        command.Parameters.AddWithValue("@SF0", DBNull.Value);
                        command.Parameters.AddWithValue("@PVT_T0", DBNull.Value);
                        command.Parameters.AddWithValue("@PVT_V0", DBNull.Value);
                        command.Parameters.AddWithValue("@MW0", DBNull.Value);
                        command.Parameters.AddWithValue("@CRC", DBNull.Value);
                        command.Parameters.AddWithValue("@COMCRC", DBNull.Value);
                        command.Parameters.AddWithValue("@ATA2", DBNull.Value);

                        // Version Information
                        command.Parameters.AddWithValue("@PROD", string.IsNullOrEmpty(stats.Prod) ? (object)DBNull.Value : (object)stats.Prod);
                        command.Parameters.AddWithValue("@MODEL", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@HWTYPE", string.IsNullOrEmpty(stats.HwType) ? (object)DBNull.Value : (object)stats.HwType);
                        command.Parameters.AddWithValue("@SWTYPE", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@LVERSION", string.IsNullOrEmpty(stats.FwVer) ? (object)DBNull.Value : (object)stats.FwVer);
                        command.Parameters.AddWithValue("@BVERSION", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@CGVERSION", DBNull.Value); // Not in AvalonNanoStats
                        int? apiVersion = null;
                        if (!string.IsNullOrEmpty(stats.Api) && int.TryParse(stats.Api, out int api))
                            apiVersion = api;
                        command.Parameters.AddWithValue("@API", apiVersion.HasValue ? (object)apiVersion.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@UPAPI", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@CGMiner", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@MAC", string.IsNullOrEmpty(stats.Mac) ? (object)DBNull.Value : (object)stats.Mac);
                        command.Parameters.AddWithValue("@DNA", DBNull.Value); // Not in AvalonNanoStats

                        // Pool 0 (Primary Pool) Information
                        command.Parameters.AddWithValue("@Pool0_URL", string.IsNullOrEmpty(stats.PoolUrl) ? (object)DBNull.Value : (object)stats.PoolUrl);
                        command.Parameters.AddWithValue("@Pool0_User", string.IsNullOrEmpty(stats.PoolUser) ? (object)DBNull.Value : (object)stats.PoolUser);
                        command.Parameters.AddWithValue("@Pool0_Password", string.IsNullOrEmpty(stats.PoolPass) ? (object)DBNull.Value : (object)stats.PoolPass);
                        command.Parameters.AddWithValue("@Pool0_Status", string.IsNullOrEmpty(stats.PoolStatusDetail) ? (object)DBNull.Value : (object)stats.PoolStatusDetail);
                        command.Parameters.AddWithValue("@Pool0_Priority", (object)stats.PoolPriority ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_Quota", (object)stats.PoolQuota ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_LongPoll", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_Accepted", (object)stats.PoolAccepted ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_Rejected", (object)stats.PoolRejected ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_Stale", (object)stats.PoolStale ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_Getworks", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_Works", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_GetFailures", (object)stats.PoolGetFailures ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_RemoteFailures", (object)stats.PoolRemoteFailures ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_Diff1Shares", DBNull.Value); // Not in AvalonNanoStats
                        
                        // Convert PoolLastShareTime from Unix timestamp if available
                        DateTime? pool0LastShareTime = null;
                        if (stats.PoolLastShareTime > 0)
                        {
                            try
                            {
                                pool0LastShareTime = DateTimeOffset.FromUnixTimeSeconds(stats.PoolLastShareTime).DateTime;
                            }
                            catch { }
                        }
                        command.Parameters.AddWithValue("@Pool0_LastShareTime", pool0LastShareTime.HasValue ? (object)pool0LastShareTime.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_LastShareDifficulty", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_WorkDifficulty", (object)stats.PoolDiff ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_StratumDifficulty", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_BestShare", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_RejectedPercent", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_StalePercent", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_BadWork", DBNull.Value); // Not in AvalonNanoStats
                        bool? pool0StratumActive = null;
                        if (!string.IsNullOrEmpty(stats.PoolStatus) && stats.PoolStatus == "1")
                            pool0StratumActive = true;
                        command.Parameters.AddWithValue("@Pool0_StratumActive", pool0StratumActive.HasValue ? (object)pool0StratumActive.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_StratumURL", string.IsNullOrEmpty(stats.PoolUrl) ? (object)DBNull.Value : (object)stats.PoolUrl);
                        command.Parameters.AddWithValue("@Pool0_HasStratum", pool0StratumActive.HasValue ? (object)pool0StratumActive.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@Pool0_HasVmask", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_HasGBT", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_CurrentBlockHeight", DBNull.Value); // Not in AvalonNanoStats
                        command.Parameters.AddWithValue("@Pool0_CurrentBlockVersion", DBNull.Value); // Not in AvalonNanoStats

                        // Pool 1 & Pool 2 - not fully populated in AvalonNanoStats, setting basic info
                        command.Parameters.AddWithValue("@Pool1_URL", string.IsNullOrEmpty(stats.Pool2) ? (object)DBNull.Value : (object)stats.Pool2);
                        command.Parameters.AddWithValue("@Pool1_User", string.IsNullOrEmpty(stats.Worker2) ? (object)DBNull.Value : (object)stats.Worker2);
                        command.Parameters.AddWithValue("@Pool1_Password", string.IsNullOrEmpty(stats.Passwd2) ? (object)DBNull.Value : (object)stats.Passwd2);
                        command.Parameters.AddWithValue("@Pool1_Status", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_Priority", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_Quota", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_LongPoll", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_Accepted", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_Rejected", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_Stale", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_Getworks", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_Works", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_GetFailures", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_RemoteFailures", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_Diff1Shares", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_LastShareTime", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_LastShareDifficulty", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_WorkDifficulty", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_StratumDifficulty", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_BestShare", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_RejectedPercent", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_StalePercent", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_BadWork", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_StratumActive", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_StratumURL", string.IsNullOrEmpty(stats.Pool2) ? (object)DBNull.Value : (object)stats.Pool2);
                        command.Parameters.AddWithValue("@Pool1_HasStratum", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_HasVmask", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_HasGBT", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_CurrentBlockHeight", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool1_CurrentBlockVersion", DBNull.Value);

                        command.Parameters.AddWithValue("@Pool2_URL", string.IsNullOrEmpty(stats.Pool3) ? (object)DBNull.Value : (object)stats.Pool3);
                        command.Parameters.AddWithValue("@Pool2_User", string.IsNullOrEmpty(stats.Worker3) ? (object)DBNull.Value : (object)stats.Worker3);
                        command.Parameters.AddWithValue("@Pool2_Password", string.IsNullOrEmpty(stats.Passwd3) ? (object)DBNull.Value : (object)stats.Passwd3);
                        command.Parameters.AddWithValue("@Pool2_Status", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_Priority", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_Quota", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_LongPoll", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_Accepted", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_Rejected", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_Stale", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_Getworks", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_Works", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_GetFailures", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_RemoteFailures", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_Diff1Shares", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_LastShareTime", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_LastShareDifficulty", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_WorkDifficulty", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_StratumDifficulty", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_BestShare", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_RejectedPercent", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_StalePercent", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_BadWork", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_StratumActive", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_StratumURL", string.IsNullOrEmpty(stats.Pool3) ? (object)DBNull.Value : (object)stats.Pool3);
                        command.Parameters.AddWithValue("@Pool2_HasStratum", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_HasVmask", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_HasGBT", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_CurrentBlockHeight", DBNull.Value);
                        command.Parameters.AddWithValue("@Pool2_CurrentBlockVersion", DBNull.Value);

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
                                // Console.WriteLine($"[SQL DEBUG] Nano 3S data point saved successfully (return code: {returnCode})");
                            }
                        }
                        catch (SqlException executeEx)
                        {
                            // Log detailed SQL error information
                            Console.WriteLine($"[SQL ERROR] Failed to execute stored procedure: {executeEx.Message}");
                            Console.WriteLine($"[SQL ERROR] Error Number: {executeEx.Number}, State: {executeEx.State}, Line: {executeEx.LineNumber}");
                            Console.WriteLine($"[SQL ERROR] Procedure: usp_InsertNano3SDataPoint");
                        
                            // Handle specific SQL errors
                            switch (executeEx.Number)
                            {
                                case 208: // Invalid object name
                                    Console.WriteLine($"[SQL ERROR] Table or stored procedure 'usp_InsertNano3SDataPoint' not found.");
                                    Console.WriteLine($"[SQL ERROR] Please run the SQL setup script: SQL/Nano3S_DatabaseSetup.sql");
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
                Console.WriteLine($"[SQL ERROR] SQL Exception in SaveNano3SDataPointToSql: {sqlEx.Message}");
                Console.WriteLine($"[SQL ERROR] Error Number: {sqlEx.Number}, State: {sqlEx.State}");
                Console.WriteLine($"[SQL ERROR] Server: {sqlEx.Server}");
                
                if (sqlEx.Number == 208) // Invalid object name
                {
                    Console.WriteLine($"[SQL ERROR] Table or stored procedure not found. Please run the SQL setup script: SQL/Nano3S_DatabaseSetup.sql");
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
                Console.WriteLine($"[SQL ERROR] Unexpected error in SaveNano3SDataPointToSql: {ex.GetType().Name}");
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
        public int? FanSpeed { get; set; }

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

    public class AvalonNanoStats
    {
        [JsonPropertyName("hwtype")]
        public string HwType { get; set; } = "";

        [JsonPropertyName("sys_status")]
        public string SysStatus { get; set; } = "";

        [JsonPropertyName("elapsed")]
        public long Elapsed { get; set; } // Elapsed time in seconds

        [JsonPropertyName("workingmode")]
        public string WorkingMode { get; set; } = ""; // "1" = Low, "2" = High

        [JsonPropertyName("workingstatus")]
        public string WorkingStatus { get; set; } = ""; // "1" = Fine

        [JsonPropertyName("power")]
        public double Power { get; set; } // Watts

        [JsonPropertyName("realtime_hash")]
        public double RealtimeHash { get; set; } // TH/s

        [JsonPropertyName("average_hash")]
        public double AverageHash { get; set; } // TH/s

        [JsonPropertyName("accepted")]
        public long Accepted { get; set; }

        [JsonPropertyName("reject")]
        public long Reject { get; set; }

        [JsonPropertyName("rejected_percentage")]
        public double RejectedPercentage { get; set; }

        [JsonPropertyName("fan_status")]
        public int FanStatus { get; set; } // RPM

        [JsonPropertyName("fanr")]
        public int FanR { get; set; } // Fan percentage

        [JsonPropertyName("asic_status")]
        public string AsicStatus { get; set; } = "";

        [JsonPropertyName("ping")]
        public int Ping { get; set; } // ms

        [JsonPropertyName("power_status")]
        public string PowerStatus { get; set; } = "";

        [JsonPropertyName("pool_status")]
        public string PoolStatus { get; set; } = "";

        [JsonPropertyName("current_pool")]
        public string CurrentPool { get; set; } = "";

        [JsonPropertyName("address")]
        public string Address { get; set; } = ""; // Pool address

        [JsonPropertyName("worker")]
        public string Worker { get; set; } = ""; // Worker name

        [JsonPropertyName("mac")]
        public string Mac { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = ""; // Firmware version

        [JsonPropertyName("pool1")]
        public string Pool1 { get; set; } = "";

        [JsonPropertyName("worker1")]
        public string Worker1 { get; set; } = "";

        [JsonPropertyName("passwd1")]
        public string Passwd1 { get; set; } = "";

        [JsonPropertyName("pool2")]
        public string Pool2 { get; set; } = "";

        [JsonPropertyName("worker2")]
        public string Worker2 { get; set; } = "";

        [JsonPropertyName("passwd2")]
        public string Passwd2 { get; set; } = "";

        [JsonPropertyName("pool3")]
        public string Pool3 { get; set; } = "";

        [JsonPropertyName("worker3")]
        public string Worker3 { get; set; } = "";

        [JsonPropertyName("passwd3")]
        public string Passwd3 { get; set; } = "";

        // Additional fields from cgminer API
        [JsonPropertyName("mhs_av")]
        public double MhsAv { get; set; } // MH/s average

        [JsonPropertyName("mhs_5s")]
        public double Mhs5s { get; set; } // MH/s 5 second

        [JsonPropertyName("found_blocks")]
        public long FoundBlocks { get; set; }

        [JsonPropertyName("getworks")]
        public long Getworks { get; set; }

        [JsonPropertyName("discarded")]
        public long Discarded { get; set; }

        [JsonPropertyName("stale")]
        public long Stale { get; set; }

        [JsonPropertyName("get_failures")]
        public long GetFailures { get; set; }

        [JsonPropertyName("local_work")]
        public long LocalWork { get; set; }

        [JsonPropertyName("remote_failures")]
        public long RemoteFailures { get; set; }

        [JsonPropertyName("network_blocks")]
        public long NetworkBlocks { get; set; }

        [JsonPropertyName("total_mh")]
        public double TotalMh { get; set; }

        [JsonPropertyName("diff1_work")]
        public long Diff1Work { get; set; }

        [JsonPropertyName("difficulty_accepted")]
        public double DifficultyAccepted { get; set; }

        [JsonPropertyName("difficulty_rejected")]
        public double DifficultyRejected { get; set; }

        [JsonPropertyName("difficulty_stale")]
        public double DifficultyStale { get; set; }

        [JsonPropertyName("last_share_difficulty")]
        public double LastShareDifficulty { get; set; }

        [JsonPropertyName("best_share")]
        public double BestShare { get; set; }

        [JsonPropertyName("last_valid_work")]
        public long LastValidWork { get; set; }

        [JsonPropertyName("total_hashes")]
        public long TotalHashes { get; set; }

        [JsonPropertyName("diff1_shares")]
        public long Diff1Shares { get; set; }

        [JsonPropertyName("proxy")]
        public string Proxy { get; set; } = "";

        [JsonPropertyName("proxy_type")]
        public string ProxyType { get; set; } = "";

        [JsonPropertyName("proxy_address")]
        public string ProxyAddress { get; set; } = "";

        [JsonPropertyName("proxy_port")]
        public int ProxyPort { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("when")]
        public long When { get; set; }

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("msg")]
        public string Msg { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        // From estats (MM section)
        [JsonPropertyName("otemp")]
        public int OTemp { get; set; } // Operating temperature

        [JsonPropertyName("tmax")]
        public int TMax { get; set; } // Max temperature

        [JsonPropertyName("tavg")]
        public int TAvg { get; set; } // Average temperature

        [JsonPropertyName("fan1")]
        public int Fan1 { get; set; } // Fan 1 speed

        [JsonPropertyName("ghsspd")]
        public double GHSspd { get; set; } // GH/s speed

        [JsonPropertyName("ps")]
        public string PS { get; set; } = ""; // Power status array

        [JsonPropertyName("ver")]
        public string Ver { get; set; } = ""; // Version from MM

        [JsonPropertyName("mm_ver")]
        public string MMVer { get; set; } = ""; // MM version

        // From version command
        [JsonPropertyName("api")]
        public string Api { get; set; } = "";

        [JsonPropertyName("fw_ver")]
        public string FwVer { get; set; } = "";

        [JsonPropertyName("prod")]
        public string Prod { get; set; } = "";

        [JsonPropertyName("compiler")]
        public string Compiler { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        // From pools command
        [JsonPropertyName("pool_url")]
        public string PoolUrl { get; set; } = "";

        [JsonPropertyName("pool_user")]
        public string PoolUser { get; set; } = "";

        [JsonPropertyName("pool_pass")]
        public string PoolPass { get; set; } = "";

        [JsonPropertyName("pool_status_detail")]
        public string PoolStatusDetail { get; set; } = "";

        [JsonPropertyName("pool_priority")]
        public int PoolPriority { get; set; }

        [JsonPropertyName("pool_quota")]
        public int PoolQuota { get; set; }

        [JsonPropertyName("pool_accepted")]
        public long PoolAccepted { get; set; }

        [JsonPropertyName("pool_rejected")]
        public long PoolRejected { get; set; }

        [JsonPropertyName("pool_stale")]
        public long PoolStale { get; set; }

        [JsonPropertyName("pool_diff")]
        public double PoolDiff { get; set; }

        [JsonPropertyName("pool_last_share_time")]
        public long PoolLastShareTime { get; set; }

        [JsonPropertyName("pool_get_failures")]
        public long PoolGetFailures { get; set; }

        [JsonPropertyName("pool_remote_failures")]
        public long PoolRemoteFailures { get; set; }
    }
}
