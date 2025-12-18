using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;

namespace MoonsecBot
{
    public class Program
    {
        private DiscordSocketClient _client;
        private InteractionService _interactions;
        private IServiceProvider _services;

        public static async Task Main(string[] args)
        {
            // Load .env for local development
            DotNetEnv.Env.Load();

            // Start a basic web server to keep Render from timing out (Port 3000)
            _ = StartHealthCheckServer();

            await new Program().RunAsync();
        }

        public async Task RunAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds,
                AlwaysDownloadUsers = true
            });

            _interactions = new InteractionService(_client.Rest);

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_interactions)
                .AddSingleton<DeobfuscationService>()
                .BuildServiceProvider();

            _client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };
            _client.Ready += ReadyAsync;
            _client.InteractionCreated += HandleInteractionAsync;

            // Retrieve token from .env or Render Environment Variables
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            if (string.IsNullOrEmpty(token))
                throw new Exception("DISCORD_BOT_TOKEN missing");

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
            
            Console.WriteLine($"🌐 Render health check listening on port {portStr}");
            await app.RunAsync();
        }

        private async Task ReadyAsync()
        {
            await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            await _interactions.RegisterCommandsGloballyAsync(true);

            Console.WriteLine($"✅ Connected as {_client.CurrentUser}");
        }

        private async Task HandleInteractionAsync(SocketInteraction interaction)
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
    }

    public class DeobfuscationModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DeobfuscationService _service;

        public DeobfuscationModule(DeobfuscationService service)
        {
            _service = service;
        }

        [SlashCommand("deobfuscate", "Deobfuscates a MoonSec-protected Lua file.")]
        public async Task Deobfuscate(
            [Summary("file", "Lua or text file")] Attachment file,
            [Choice("Bytecode (.bin)", "bytecode")]
            [Choice("Disassembly (.txt)", "disassembly")]
            string format = "disassembly")
        {
            await DeferAsync();

            if (!file.Filename.EndsWith(".lua") && !file.Filename.EndsWith(".txt"))
            {
                await FollowupAsync("❌ Only `.lua` or `.txt` files are allowed.");
                return;
            }

            try
            {
                using var http = new HttpClient();
                var bytes = await http.GetByteArrayAsync(file.Url);
                var input = Encoding.UTF8.GetString(bytes);

                byte[] output;
                string filename;

                if (format == "bytecode")
                {
                    output = _service.Devirtualize(input);
                    filename = "output.bin";
                }
                else
                {
                    output = Encoding.UTF8.GetBytes(_service.Disassemble(input));
                    filename = "output.txt";
                }

                await FollowupWithFileAsync(
                    new MemoryStream(output),
                    filename,
                    text: "✅ **Deobfuscation complete**"
                );
            }
            catch (Exception ex)
            {
                await FollowupAsync($"❌ Error during deobfuscation: `{ex.Message}`");
            }
        }
    }

    public class DeobfuscationService
    {
        public byte[] Devirtualize(string code)
        {
            var result = new Deobfuscator().Deobfuscate(code);
            using var ms = new MemoryStream();
            using var serializer = new Serializer(ms);
            serializer.Serialize(result);
            return ms.ToArray();
        }

        public string Disassemble(string code)
        {
            var result = new Deobfuscator().Deobfuscate(code);
            // This now calls the class in your separate Disassembler.cs file
            return new Disassembler(result).Disassemble();
        }
    }
}
