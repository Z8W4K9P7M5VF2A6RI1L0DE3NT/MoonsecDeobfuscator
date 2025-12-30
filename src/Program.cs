using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net.Http;
using System.IO;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using Microsoft.Extensions.Logging;

namespace MoonsecBot
{
    public class Program
    {
        private DiscordSocketClient _client;
        private InteractionService _interactions;
        private IServiceProvider _services;

        public static async Task Main(string[] args)
        {
            // Load environment variables
            DotNetEnv.Env.Load();
            
            // Verify required environment variables
            var requiredVars = new[] { "DISCORD_BOT_TOKEN", "DECOMPILER_API_KEY" };
            foreach (var varName in requiredVars)
            {
                var value = Environment.GetEnvironmentVariable(varName);
                if (string.IsNullOrEmpty(value))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ ERROR: {varName} environment variable not set!");
                    Console.ResetColor();
                    return;
                }
                Console.WriteLine($"✅ {varName} is set");
            }
            
            // Start health check server in background
            _ = StartHealthCheckServer();
            
            // Run bot
            await new Program().RunAsync();
        }

        public async Task RunAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds,
                AlwaysDownloadUsers = true,
                LogLevel = LogSeverity.Info
            });

            _interactions = new InteractionService(_client.Rest);

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_interactions)
                .AddSingleton<HybridDecompilationService>()
                .AddSingleton<HttpClient>()
                .BuildServiceProvider();

            // Logging
            _client.Log += msg => 
            {
                Console.WriteLine($"[{msg.Severity}] {msg.Message}");
                return Task.CompletedTask;
            };
            
            _client.Ready += ReadyAsync;
            _client.InteractionCreated += HandleInteractionAsync;

            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private static async Task StartHealthCheckServer()
        {
            var portStr = Environment.GetEnvironmentVariable("PORT") ?? "3000";
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Any, int.Parse(portStr)));
            var app = builder.Build();
            app.MapGet("/", () => "MoonSec Bot is running.");
            await app.RunAsync();
        }

        private async Task ReadyAsync()
        {
            await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            await _interactions.RegisterCommandsGloballyAsync(true);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("🚀 Bot is ready and commands registered globally!");
            Console.ResetColor();
        }

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
    }

    // ==================== DISCORD MODULE ====================
    public class DeobfuscationModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly HybridDecompilationService _service;

        public DeobfuscationModule(HybridDecompilationService service)
        {
            _service = service;
        }

        [SlashCommand("deobfuscate", "Deobfuscates MoonSecV3/IB2 Lua file to clean bytecode")]
        public async Task Deobfuscate([Summary("file", "Lua file to deobfuscate")] Attachment file)
        {
            await DeferAsync();

            // Validate file type
            if (!file.Filename.EndsWith(".lua") && !file.Filename.EndsWith(".txt"))
            {
                await FollowupAsync("❌ Only `.lua` or `.txt` files are supported.");
                return;
            }

            // Validate file size (10MB limit)
            if (file.Size > 10 * 1024 * 1024)
            {
                await FollowupAsync("❌ File too large. Maximum size is 10MB.");
                return;
            }

            try
            {
                await FollowupAsync("🔍 Deobfuscating locally...");
                
                using var http = new HttpClient();
                var luaCode = await http.GetStringAsync(file.Url);
                
                // Deobfuscate and decompile through external API
                var decompiledCode = await _service.DeobfuscateAndDecompileAsync(luaCode);
                
                // Generate random hex filename
                string randomHex = Guid.NewGuid().ToString("N").Substring(0, 16);
                string customFilename = $"{randomHex}.lua";
                
                // Send result as file
                await FollowupWithFileAsync(
                    new MemoryStream(Encoding.UTF8.GetBytes(decompiledCode)),
                    customFilename,
                    text: $"You Out Da Projects Twin 🔫🔫? {Context.User.Mention}"
                );
            }
            catch (HttpRequestException ex)
            {
                await FollowupAsync($"❌ API connection error: `{ex.Message}`");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Error] {ex}");
                Console.ResetColor();
                await FollowupAsync($"❌ Error: `{ex.Message}`");
            }
        }
    }

    // ==================== HYBRID DECOMPILATION SERVICE ====================
    public class HybridDecompilationService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiEndpoint;
        private readonly string _apiKey;

        public HybridDecompilationService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _apiEndpoint = Environment.GetEnvironmentVariable("DECOMPILER_API_ENDPOINT") 
                           ?? "https://henne4g.onrender.com/decompile";
            
            // Use the fixed API key
            _apiKey = Environment.GetEnvironmentVariable("DECOMPILER_API_KEY") 
                      ?? "medal-bot-secure-key-2025";
            
            // Set timeout
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<string> DeobfuscateAndDecompileAsync(string luaCode)
        {
            Console.WriteLine("🔄 Starting hybrid deobfuscation process...");
            
            // Step 1: Deobfuscate using MoonsecDeobfuscator
            Console.WriteLine("⚙️  Running local deobfuscator...");
            var deobfuscationResult = new Deobfuscator().Deobfuscate(luaCode);
            Console.WriteLine("✅ Deobfuscation complete");
            
            // Step 2: Serialize to bytecode
            Console.WriteLine("💾 Serializing bytecode...");
            using var memoryStream = new MemoryStream();
            using var serializer = new Serializer(memoryStream);
            serializer.Serialize(deobfuscationResult);
            var bytecode = memoryStream.ToArray();
            Console.WriteLine($"📦 Serialized {bytecode.Length} bytes of bytecode");
            
            // Step 3: Send to external decompiler API
            Console.WriteLine($"🌐 Sending to decompiler API...");
            return await SendToDecompilerApiAsync(bytecode);
        }

        private async Task<string> SendToDecompilerApiAsync(byte[] bytecode)
        {
            try
            {
                // Convert to base64
                string base64Data = Convert.ToBase64String(bytecode);
                
                // Create request
                var request = new HttpRequestMessage(HttpMethod.Post, _apiEndpoint);
                request.Headers.Add("X-API-Key", _apiKey);
                request.Content = new StringContent(base64Data, Encoding.UTF8, "application/octet-stream");
                
                Console.WriteLine($"📤 POST {_apiEndpoint} ({base64Data.Length} bytes)");
                
                // Send request
                var response = await _httpClient.SendAsync(request);
                
                // Handle response
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ API Error: {response.StatusCode} - {errorContent}");
                    Console.ResetColor();
                    throw new Exception($"Decompiler API returned {response.StatusCode}: {errorContent}");
                }
                
                var decompiledCode = await response.Content.ReadAsStringAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Decompilation successful! ({decompiledCode.Length} chars)");
                Console.ResetColor();
                
                return decompiledCode;
            }
            catch (Exception ex)
            {
                throw new Exception($"External decompiler failed: {ex.Message}", ex);
            }
        }
    }
}
