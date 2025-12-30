using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Builder;
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

            if (!file.Filename.EndsWith(".lua") && !file.Filename.EndsWith(".txt"))
            {
                await FollowupAsync("❌ Only `.lua` or `.txt` files are supported.");
                return;
            }

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
                
                var decompiledCode = await _service.DeobfuscateAndDecompileAsync(luaCode);
                
                string randomHex = Guid.NewGuid().ToString("N").Substring(0, 16);
                string customFilename = $"{randomHex}.lua";
                
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
            
            _apiKey = Environment.GetEnvironmentVariable("DECOMPILER_API_KEY") 
                      ?? "medal-bot-secure-key-2025";
            
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<string> DeobfuscateAndDecompileAsync(string luaCode)
        {
            Console.WriteLine("🔄 Starting hybrid deobfuscation...");
            
            var deob = new Deobfuscator().Deobfuscate(luaCode);
            
            using var ms = new MemoryStream();
            new Serializer(ms).Serialize(deob);
            var bytecode = ms.ToArray();
            
            Console.WriteLine($"📦 Serialized {bytecode.Length} bytes");
            
            return await SendToApiAsync(bytecode);
        }

        private async Task<string> SendToApiAsync(byte[] bytecode)
        {
            var base64Data = Convert.ToBase64String(bytecode);
            
            var req = new HttpRequestMessage(HttpMethod.Post, _apiEndpoint);
            req.Headers.Add("X-API-Key", _apiKey);
            req.Content = new StringContent(base64Data, Encoding.UTF8, "application/octet-stream");
            
            var resp = await _httpClient.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            
            return await resp.Content.ReadAsStringAsync();
        }
    }
}
