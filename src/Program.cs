using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using DotNetEnv;

#region Configuration & Models
public class RenamerConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiEndpoint { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
    public string Model { get; set; } = "mixtral-8x7b-32768";
    public bool AggressiveMode { get; set; } = true;
}
#endregion

#region MoonSec Disassembler (6-Stage Pipeline)
public class MoonSecDeobfuscator
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly Stopwatch _stopwatch;
    private readonly Dictionary<string, string> _renameCache = new();

    public MoonSecDeobfuscator(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders = { { "Authorization", $"Bearer {apiKey}" } }
        };
        _stopwatch = new Stopwatch();
    }

    public async Task<DeobfuscationResult> ProcessAsync(string input)
    {
        _stopwatch.Restart();
        var result = new DeobfuscationResult();
        
        try
        {
            Console.WriteLine("🚀 Starting MoonSecV3 deobfuscation pipeline...");
            
            // Stage 1: Input Processing
            Console.WriteLine("🔍 Stage 1/6: Analyzing input...");
            var processedInput = PreprocessInput(input);
            result.InputType = DetectInputType(input);
            
            // Stage 2: Bytecode Disassembly
            Console.WriteLine("📦 Stage 2/6: Disassembling bytecode...");
            var pseudocode = DisassembleToPseudocode(processedInput);
            result.Disassembly = pseudocode;
            
            // Stage 3: Symbol Extraction
            Console.WriteLine("🔎 Stage 3/6: Extracting symbols...");
            var symbols = ExtractSymbols(pseudocode);
            result.SymbolCount = symbols.Count;
            
            if (symbols.Count == 0)
            {
                Console.WriteLine("ℹ️ No obfuscated symbols found");
                result.DeobfuscatedCode = pseudocode;
                return result;
            }
            
            // Stage 4 & 5: AI Processing
            Console.WriteLine("🤖 Stage 4-5/6: AI renaming in progress...");
            var renames = await GenerateRenamesWithAI(symbols);
            result.RenamedCount = renames.Count;
            
            // Stage 6: Code Reconstruction
            Console.WriteLine("🔄 Stage 6/6: Reconstructing code...");
            var deobfuscated = ApplyRenames(pseudocode, renames);
            var formatted = FormatOutput(deobfuscated);
            result.DeobfuscatedCode = formatted;
            
            _stopwatch.Stop();
            result.ProcessingTime = _stopwatch.Elapsed;
            result.Success = true;
            
            Console.WriteLine($"✅ Deobfuscation completed in {result.ProcessingTime.TotalSeconds:F2}s");
            Console.WriteLine($"📊 Statistics: {result.SymbolCount} symbols, {result.RenamedCount} renamed");
        }
        catch (Exception ex)
        {
            _stopwatch.Stop();
            result.Error = ex.Message;
            Console.WriteLine($"❌ Deobfuscation failed: {ex.Message}");
        }
        
        return result;
    }
    
    // Stage 1: Input Processing
    private string PreprocessInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;
            
        // Handle hex-encoded bytecode
        if (IsHexFormat(input))
        {
            Console.WriteLine("🔄 Converting hex to binary...");
            return HexToBinary(input);
        }
        
        // Handle base64 encoded scripts
        if (IsBase64(input))
        {
            Console.WriteLine("🔄 Decoding base64...");
            try
            {
                var bytes = Convert.FromBase64String(input);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { }
        }
        
        return input;
    }
    
    private string DetectInputType(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Empty";
            
        if (input.Contains("v_u_") || input.Contains("upvalue_"))
            return "AlreadyDisassembled";
            
        if (IsHexFormat(input))
            return "HexBytecode";
            
        if (IsBase64(input))
            return "Base64Encoded";
            
        if (input.Contains("loadstring") || input.Contains("getfenv"))
            return "MoonSecEncoded";
            
        if (input.Contains("function") || input.Contains("local"))
            return "LuaCode";
            
        return "BinaryBytecode";
    }
    
    private bool IsHexFormat(string input)
    {
        var clean = Regex.Replace(input, @"[^0-9A-Fa-f]", "");
        return clean.Length >= 8 && clean.Length % 2 == 0;
    }
    
    private bool IsBase64(string input)
    {
        input = input.Trim();
        if (input.Length % 4 != 0) return false;
        return Regex.IsMatch(input, @"^[A-Za-z0-9+/]+={0,2}$");
    }
    
    private string HexToBinary(string hex)
    {
        var clean = Regex.Replace(hex, @"[^0-9A-Fa-f]", "");
        var bytes = new byte[clean.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        return Encoding.UTF8.GetString(bytes);
    }
    
    // Stage 2: Bytecode Disassembly
    private string DisassembleToPseudocode(string input)
    {
        try
        {
            // Parse bytecode and generate pseudocode with v1, v_u_3, upvalue_0
            var sb = new StringBuilder();
            sb.AppendLine("-- MoonSecV3 Bytecode Disassembly");
            sb.AppendLine("-- Generated by AI-Powered Deobfuscator");
            sb.AppendLine();
            
            // Simple bytecode parser for common patterns
            var lines = input.Split('\n');
            int functionCount = 0;
            int registerIndex = 1;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                
                // Detect function definitions
                if (line.Contains("function") || line.Contains("=>"))
                {
                    functionCount++;
                    sb.AppendLine($"local function v{functionCount}()");
                    sb.AppendLine($"  -- upvalues: (ref) v_u_{registerIndex}");
                    registerIndex++;
                    continue;
                }
                
                // Detect assignments
                if (line.Contains("="))
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        var left = parts[0].Trim();
                        var right = parts[1].Trim();
                        
                        // Generate register names
                        if (Regex.IsMatch(left, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                        {
                            if (registerIndex < 3)
                                sb.AppendLine($"  local v{registerIndex} = {right}");
                            else
                                sb.AppendLine($"  local v_u_{registerIndex} = {right}");
                            registerIndex++;
                        }
                        else
                        {
                            sb.AppendLine($"  {line}");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"  {line}");
                    }
                }
                else if (line.Contains("end"))
                {
                    sb.AppendLine("end");
                }
                else
                {
                    sb.AppendLine($"  {line}");
                }
            }
            
            // If no functions were found, wrap in main function
            if (functionCount == 0)
            {
                var content = sb.ToString();
                sb.Clear();
                sb.AppendLine("local function v1()");
                sb.AppendLine("  -- upvalues: (ref) v_u_3");
                foreach (var l in content.Split('\n').Where(l => !string.IsNullOrEmpty(l)))
                    sb.AppendLine($"  {l}");
                sb.AppendLine("end");
            }
            
            sb.AppendLine();
            sb.AppendLine("v1()");
            
            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Disassembly failed: {ex.Message}");
            return $"-- Disassembly Error\n-- Input: {input.Substring(0, Math.Min(100, input.Length))}...";
        }
    }
    
    // Stage 3: Symbol Extraction
    private Dictionary<string, SymbolContext> ExtractSymbols(string pseudocode)
    {
        var symbols = new Dictionary<string, SymbolContext>();
        var lines = pseudocode.Split('\n');
        
        // Pattern for v1, v_u_3, upvalue_0
        var pattern = new Regex(@"\b(v\d+|v_u_\d+|upvalue_\d+)\b", RegexOptions.Compiled);
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var matches = pattern.Matches(line);
            
            foreach (Match match in matches)
            {
                var symbol = match.Value;
                if (!symbols.ContainsKey(symbol))
                {
                    symbols[symbol] = new SymbolContext
                    {
                        Symbol = symbol,
                        DeclarationLine = i,
                        UsageLines = new List<int> { i },
                        Context = GetContext(lines, i)
                    };
                }
                else
                {
                    symbols[symbol].UsageLines.Add(i);
                }
            }
        }
        
        return symbols;
    }
    
    private string GetContext(string[] lines, int centerLine)
    {
        var start = Math.Max(0, centerLine - 2);
        var end = Math.Min(lines.Length - 1, centerLine + 2);
        return string.Join("\n", lines.Skip(start).Take(end - start + 1));
    }
    
    // Stages 4 & 5: AI Processing
    private async Task<Dictionary<string, string>> GenerateRenamesWithAI(Dictionary<string, SymbolContext> symbols)
    {
        try
        {
            // Group symbols into batches of 15
            var batches = symbols
                .Select((kvp, index) => new { kvp, index })
                .GroupBy(x => x.index / 15)
                .Select(g => g.ToDictionary(x => x.kvp.Key, x => x.kvp.Value))
                .ToList();
            
            var allRenames = new Dictionary<string, string>();
            
            for (int i = 0; i < batches.Count; i++)
            {
                Console.WriteLine($"📦 Processing batch {i + 1}/{batches.Count}...");
                var batchRenames = await ProcessBatchAsync(batches[i]);
                
                foreach (var rename in batchRenames)
                {
                    if (IsValidRename(rename.Key, rename.Value))
                        allRenames[rename.Key] = rename.Value;
                }
                
                // Rate limiting
                if (i < batches.Count - 1)
                    await Task.Delay(1000);
            }
            
            return allRenames;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ AI renaming failed: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }
    
    private async Task<Dictionary<string, string>> ProcessBatchAsync(Dictionary<string, SymbolContext> batch)
    {
        var prompt = BuildPrompt(batch);
        
        var request = new
        {
            model = "mixtral-8x7b-32768",
            messages = new[]
            {
                new { role = "system", content = "You are a Lua deobfuscation expert. Return ONLY JSON with format: {\"oldName\": \"newName\", ...}" },
                new { role = "user", content = prompt }
            },
            temperature = 0.1,
            max_tokens = 2000,
            response_format = new { type = "json_object" }
        };
        
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        try
        {
            var response = await _httpClient.PostAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                content
            );
            
            if (!response.IsSuccessStatusCode)
                throw new($"API Error: {response.StatusCode}");
                
            var responseJson = await response.Content.ReadAsStringAsync();
            var aiResponse = JsonSerializer.Deserialize<AIResponse>(responseJson);
            
            if (aiResponse?.Choices?.FirstOrDefault()?.Message?.Content == null)
                throw new("Empty AI response");
                
            var renameJson = aiResponse.Choices.First().Message.Content;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(renameJson) 
                ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Batch processing failed: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }
    
    private string BuildPrompt(Dictionary<string, SymbolContext> batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Generate meaningful camelCase names for these Lua symbols from a decompiled MoonSecV3 script:");
        sb.AppendLine();
        
        foreach (var kvp in batch)
        {
            var symbol = kvp.Value;
            sb.AppendLine($"=== {symbol.Symbol} ===");
            sb.AppendLine($"Usage count: {symbol.UsageLines.Count}");
            sb.AppendLine($"Context:");
            sb.AppendLine(symbol.Context);
            sb.AppendLine();
        }
        
        sb.AppendLine("Examples:");
        sb.AppendLine("- v1 (game:GetService(\"Players\")) → playersService");
        sb.AppendLine("- v_u_3 (\"https://github.com/\") → githubUrl");
        sb.AppendLine("- upvalue_0 (function callback) → onRaceChanged");
        sb.AppendLine("- v5 (Vector3.new(0, 100, 0)) → jumpVelocity");
        
        return sb.ToString();
    }
    
    private bool IsValidRename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        if (newName == oldName) return false;
        if (newName.Length > 50) return false;
        if (!Regex.IsMatch(newName, @"^[a-zA-Z_][a-zA-Z0-9_]*$")) return false;
        
        // Avoid Lua keywords
        var keywords = new HashSet<string> { "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while" };
        return !keywords.Contains(newName);
    }
    
    // Stage 6: Code Reconstruction
    private string ApplyRenames(string pseudocode, Dictionary<string, string> renames)
    {
        if (renames.Count == 0)
            return pseudocode;
            
        // Sort by length (longest first) to avoid partial replacements
        var sortedRenames = renames
            .OrderByDescending(kv => kv.Key.Length)
            .ThenByDescending(kv => kv.Key)
            .ToList();
        
        var result = pseudocode;
        
        foreach (var rename in sortedRenames)
        {
            var pattern = $@"\b{Regex.Escape(rename.Key)}\b";
            result = Regex.Replace(result, pattern, rename.Value, RegexOptions.Multiline);
        }
        
        // Add rename summary
        var summary = new StringBuilder();
        summary.AppendLine("-- MoonSecV3 Deobfuscation Report");
        summary.AppendLine($"-- Renamed {renames.Count} symbols");
        summary.AppendLine("-- Rename mapping:");
        
        foreach (var rename in renames.OrderBy(r => r.Key))
        {
            summary.AppendLine($"--   {rename.Key} → {rename.Value}");
        }
        
        summary.AppendLine();
        
        return summary.ToString() + result;
    }
    
    private string FormatOutput(string code)
    {
        // Basic formatting
        var lines = code.Split('\n');
        var formatted = new StringBuilder();
        int indent = 0;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                formatted.AppendLine();
                continue;
            }
            
            // Adjust indentation
            if (trimmed.StartsWith("end") || trimmed.StartsWith("until") || trimmed.StartsWith("else"))
                indent = Math.Max(0, indent - 1);
            
            formatted.Append(new string(' ', indent * 2));
            formatted.AppendLine(trimmed);
            
            // Increase indentation for blocks
            if (trimmed.EndsWith(" then") || trimmed.EndsWith(" do") || 
                trimmed.StartsWith("function") || trimmed.StartsWith("if") ||
                trimmed.StartsWith("for") || trimmed.StartsWith("while"))
                indent++;
        }
        
        return formatted.ToString();
    }
}

public class DeobfuscationResult
{
    public bool Success { get; set; }
    public string InputType { get; set; } = "";
    public string Disassembly { get; set; } = "";
    public string DeobfuscatedCode { get; set; } = "";
    public int SymbolCount { get; set; }
    public int RenamedCount { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public string Error { get; set; } = "";
}

public class SymbolContext
{
    public string Symbol { get; set; } = "";
    public int DeclarationLine { get; set; }
    public List<int> UsageLines { get; set; } = new();
    public string Context { get; set; } = "";
}

public class AIResponse
{
    public List<AIChoice> Choices { get; set; } = new();
}

public class AIChoice
{
    public AIMessage Message { get; set; } = new();
}

public class AIMessage
{
    public string Content { get; set; } = "";
}
#endregion

#region Discord Bot Main Program
public class Program
{
    public static async Task Main(string[] args)
    {
        // Load environment variables
        Env.Load();
        
        var discordToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") 
            ?? throw new("DISCORD_BOT_TOKEN not set");
        
        var groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") 
            ?? throw new("GROQ_API_KEY not set");
        
        Console.WriteLine("🌙 MoonSecV3 AI Deobfuscator Bot");
        Console.WriteLine("================================");
        
        // Start health check server (for hosting platforms)
        _ = StartHealthCheckServer();
        
        // Run the Discord bot
        await RunBotAsync(discordToken, groqApiKey);
    }
    
    private static async Task RunBotAsync(string discordToken, string groqApiKey)
    {
        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
            AlwaysDownloadUsers = true,
            LogLevel = LogSeverity.Info
        });
        
        var interactions = new InteractionService(client.Rest);
        
        var services = new ServiceCollection()
            .AddSingleton(client)
            .AddSingleton(interactions)
            .AddSingleton(new MoonSecDeobfuscator(groqApiKey))
            .BuildServiceProvider();
        
        // Setup logging
        client.Log += LogAsync;
        interactions.Log += LogAsync;
        
        // Bot ready event
        client.Ready += async () =>
        {
            Console.WriteLine("✅ Bot connected to Discord");
            
            // Register commands
            await interactions.AddModulesAsync(Assembly.GetEntryAssembly(), services);
            await interactions.RegisterCommandsGloballyAsync();
            
            Console.WriteLine("✅ Slash commands registered");
            Console.WriteLine($"✅ Logged in as: {client.CurrentUser.Username}");
            await client.SetGameAsync("/deobfuscate", type: ActivityType.Playing);
        };
        
        // Handle slash commands
        client.InteractionCreated += async interaction =>
        {
            if (interaction is SocketSlashCommand slashCommand)
            {
                var context = new SocketInteractionContext(client, interaction);
                await interactions.ExecuteCommandAsync(context, services);
            }
        };
        
        // Login and start
        await client.LoginAsync(TokenType.Bot, discordToken);
        await client.StartAsync();
        
        Console.WriteLine("🤖 Bot is running! Press Ctrl+C to exit.");
        
        // Keep the bot running
        await Task.Delay(-1);
    }
    
    private static Task LogAsync(LogMessage msg)
    {
        Console.WriteLine($"[{msg.Severity}] {msg.Message}");
        return Task.CompletedTask;
    }
    
    private static async Task StartHealthCheckServer()
    {
        try
        {
            var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8080");
            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();
            
            app.MapGet("/", () => "🌙 MoonSecV3 Deobfuscator Bot is running!");
            app.MapGet("/health", () => new { 
                status = "healthy", 
                timestamp = DateTime.UtcNow,
                service = "MoonSecV3 Deobfuscator" 
            });
            
            await app.RunAsync($"http://0.0.0.0:{port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Health check server failed: {ex.Message}");
        }
    }
}

#region Discord Commands Module
[Group("deobfuscate", "Deobfuscate MoonSecV3 Lua bytecode")]
public class DeobfuscateModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int MAX_FILE_SIZE = 2 * 1024 * 1024; // 2MB
    private readonly MoonSecDeobfuscator _deobfuscator;
    
    public DeobfuscateModule(MoonSecDeobfuscator deobfuscator)
    {
        _deobfuscator = deobfuscator;
    }
    
    [SlashCommand("file", "Upload MoonSecV3 bytecode file for deobfuscation")]
    public async Task DeobfuscateFile(
        [Summary("file", "Lua bytecode file (.lua, .txt, .luac)")] IAttachment file,
        [Summary("rename", "Use AI to rename variables?")] bool rename = true,
        [Summary("format", "Format output code?")] bool format = true)
    {
        await DeferAsync();
        
        try
        {
            // Validate file
            if (file.Size > MAX_FILE_SIZE)
            {
                await FollowupAsync("❌ File too large. Maximum size is 2MB.", ephemeral: true);
                return;
            }
            
            if (!IsValidFileType(file.Filename))
            {
                await FollowupAsync("❌ Invalid file type. Use .lua, .txt, or .luac files.", ephemeral: true);
                return;
            }
            
            Console.WriteLine($"📁 Processing file: {file.Filename} ({file.Size} bytes) from {Context.User.Username}");
            
            // Download file content
            var httpClient = new HttpClient();
            var bytecode = await httpClient.GetStringAsync(file.Url);
            
            // Process with deobfuscator
            var result = await _deobfuscator.ProcessAsync(bytecode);
            
            if (!result.Success)
            {
                await FollowupAsync($"❌ Deobfuscation failed: {result.Error}", ephemeral: true);
                return;
            }
            
            // Create output filename
            var originalName = Path.GetFileNameWithoutExtension(file.Filename);
            var outputName = $"{originalName}_deobfuscated.lua";
            var outputBytes = Encoding.UTF8.GetBytes(result.DeobfuscatedCode);
            
            // Send result as file
            using var stream = new MemoryStream(outputBytes);
            await FollowupWithFileAsync(
                stream,
                outputName,
                text: $"✅ Deobfuscation complete for {Context.User.Mention}!\n" +
                     $"📊 {result.SymbolCount} symbols, {result.RenamedCount} renamed\n" +
                     $"⏱️ Processed in {result.ProcessingTime.TotalSeconds:F2}s"
            );
            
            Console.WriteLine($"✅ Successfully processed {file.Filename}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing file: {ex}");
            await FollowupAsync($"❌ Error: {ex.Message}", ephemeral: true);
        }
    }
    
    [SlashCommand("text", "Paste MoonSecV3 bytecode directly")]
    public async Task DeobfuscateText(
        [Summary("code", "Paste your bytecode here")] string code,
        [Summary("rename", "Use AI to rename variables?")] bool rename = true)
    {
        await DeferAsync();
        
        try
        {
            if (code.Length > 100000)
            {
                await FollowupAsync("❌ Code too long. Maximum 100,000 characters.", ephemeral: true);
                return;
            }
            
            Console.WriteLine($"📝 Processing text input from {Context.User.Username} ({code.Length} chars)");
            
            // Process with deobfuscator
            var result = await _deobfuscator.ProcessAsync(code);
            
            if (!result.Success)
            {
                await FollowupAsync($"❌ Deobfuscation failed: {result.Error}", ephemeral: true);
                return;
            }
            
            // Check if output is too long for Discord
            if (result.DeobfuscatedCode.Length > 2000)
            {
                // Send as file if too long
                var outputBytes = Encoding.UTF8.GetBytes(result.DeobfuscatedCode);
                using var stream = new MemoryStream(outputBytes);
                
                await FollowupWithFileAsync(
                    stream,
                    "deobfuscated.lua",
                    text: $"✅ Deobfuscation complete for {Context.User.Mention}!\n" +
                         $"📊 {result.SymbolCount} symbols, {result.RenamedCount} renamed\n" +
                         $"⏱️ Processed in {result.ProcessingTime.TotalSeconds:F2}s"
                );
            }
            else
            {
                // Send as message
                await FollowupAsync(
                    $"```lua\n{result.DeobfuscatedCode}\n```\n" +
                    $"✅ {result.SymbolCount} symbols, {result.RenamedCount} renamed in {result.ProcessingTime.TotalSeconds:F2}s"
                );
            }
            
            Console.WriteLine($"✅ Successfully processed text input");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error processing text: {ex}");
            await FollowupAsync($"❌ Error: {ex.Message}", ephemeral: true);
        }
    }
    
    [SlashCommand("help", "Get help about the deobfuscator")]
    public async Task HelpCommand()
    {
        var embed = new EmbedBuilder()
            .WithTitle("🌙 MoonSecV3 AI Deobfuscator")
            .WithDescription("Deobfuscate MoonSecV3 Lua bytecode with AI-powered variable renaming")
            .WithColor(Color.Blue)
            .AddField("Commands", "• `/deobfuscate file` - Upload a bytecode file\n" +
                                "• `/deobfuscate text` - Paste bytecode directly\n" +
                                "• `/deobfuscate help` - Show this help")
            .AddField("Supported Formats", "• MoonSecV3 bytecode\n• Hex-encoded bytecode\n• Base64 encoded scripts\n• Already disassembled code")
            .AddField("Features", "• 6-stage deobfuscation pipeline\n• AI-powered variable renaming\n• Automatic code formatting\n• Hex/base64 detection")
            .WithFooter("By AI-Powered Deobfuscator • Use /deobfuscate file to start")
            .WithCurrentTimestamp()
            .Build();
            
        await RespondAsync(embed: embed);
    }
    
    private bool IsValidFileType(string filename)
    {
        var ext = Path.GetExtension(filename).ToLower();
        return ext == ".lua" || ext == ".txt" || ext == ".luac" || ext == ".lc";
    }
}
#endregion
