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
            DotNetEnv.Env.Load();
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

        [SlashCommand("deobfuscate", "Deobfuscates  MoonSecV3/IB2 Lua file.")]
        public async Task Deobfuscate([Summary("file", "Lua or text file")] Attachment file)
        {
            await DeferAsync();

            if (!file.Filename.EndsWith(".lua") && !file.Filename.EndsWith(".txt"))
            {
                await FollowupAsync(" Only `.lua` or `.txt` files are allowed.");
                return;
            }

            try
            {
                using var http = new HttpClient();
                var bytes = await http.GetByteArrayAsync(file.Url);
                var input = Encoding.UTF8.GetString(bytes);

                // Run disassembly
                string deobfuscatedText = _service.Disassemble(input);
                byte[] outputBytes = Encoding.UTF8.GetBytes(deobfuscatedText);

                // Generate random 16-char hex name like: 20852b512aea2d9a.lua
                string randomHex = Guid.NewGuid().ToString("N").Substring(0, 16);
                string customFilename = $"{randomHex}.lua";

                // Respond with the specific "twin" message tagging the user
                await FollowupWithFileAsync(
                    new MemoryStream(outputBytes),
                    customFilename,
                    text: $"You Out Da Projects Twin 🔫🔫? {Context.User.Mention}"
                );
            }
            catch (Exception ex)
            {
                // This will now catch fewer range errors due to the Disassembler logic fix
                await FollowupAsync($" Error during deobfuscation: `{ex.Message}`");
            }
        }
    }

    public class DeobfuscationService
    {
        public string Disassemble(string code)
        {
            var result = new Deobfuscator().Deobfuscate(code);
            return new Disassembler(result).Disassemble();
        }
    }
}
