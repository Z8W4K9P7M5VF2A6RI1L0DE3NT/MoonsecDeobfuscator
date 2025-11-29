using Discord;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using System.Net;
using System.Collections.Concurrent;

namespace MoonsecDeobfuscator;

public class Program
{
    private DiscordSocketClient _client;
    private string _token = Environment.GetEnvironmentVariable("RENDER_TOKEN");
    private const ulong AllowedChannel = 1444258745336070164;

    // cooldown table
    private static readonly ConcurrentDictionary<ulong, DateTime> Cooldowns = new();

    public static Task Main(string[] args)
        => new Program().MainAsync();

    public async Task MainAsync()
    {
        if (string.IsNullOrWhiteSpace(_token))
        {
            Console.WriteLine("Missing RENDER_TOKEN env variable");
            return;
        }

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents =
                GatewayIntents.Guilds |
                GatewayIntents.GuildMessages |
                GatewayIntents.DirectMessages |
                GatewayIntents.MessageContent
        });

        _client.MessageReceived += HandleMessage;

        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();
        await Task.Delay(-1);
    }

    private async Task HandleMessage(SocketMessage msg)
    {
        if (msg.Author.IsBot) return;

        // only allow messages with files
        if (msg.Channel.Id == AllowedChannel && msg.Attachments.Count == 0)
        {
            try { await msg.DeleteAsync(); } catch { }
            return;
        }

        bool hasFile = msg.Attachments.Any(a => a.Filename.EndsWith(".lua") || a.Filename.EndsWith(".txt"));
        bool hasUrl = msg.Content.StartsWith("http://") || msg.Content.StartsWith("https://");

        if (!hasFile && !hasUrl) return;

        // --- cooldown check ---
        if (Cooldowns.TryGetValue(msg.Author.Id, out DateTime last))
        {
            if ((DateTime.UtcNow - last).TotalSeconds < 5)
                return;
        }
        Cooldowns[msg.Author.Id] = DateTime.UtcNow;

        // --- download source ---
        string code = "";

        if (hasFile)
        {
            var file = msg.Attachments.First();
            using var wc = new WebClient();
            code = wc.DownloadString(file.Url);
        }
        else if (hasUrl)
        {
            try
            {
                using var wc = new WebClient();
                code = wc.DownloadString(msg.Content.Trim());
            }
            catch
            {
                await msg.Channel.SendMessageAsync("Invalid URL");
                return;
            }
        }

        // remove comments
        code = RemoveLuaComments(code);

        // pure deobfuscation (no disassembly)
        string result;
        try
        {
            var deob = new Deobfuscator().Deobfuscate(code);
            result = "-- deobfuscated by galactic services join now https://discord.gg/angmZQJC8a\n\n" + deob;
        }
        catch
        {
            await msg.Channel.SendMessageAsync("Error during deobfuscation");
            return;
        }

        // save output
        string outPath = Path.GetTempFileName() + ".lua";
        await File.WriteAllTextAsync(outPath, result);

        // reply with file once
        await msg.Channel.SendFileAsync(outPath, "file deobfuscated here it is");
    }

    private string RemoveLuaComments(string code)
    {
        var lines = code.Split('\n');
        List<string> clean = new();
        foreach (var l in lines)
        {
            string c = l;
            if (c.TrimStart().StartsWith("--")) continue;
            int idx = c.IndexOf("--");
            if (idx >= 0) c = c[..idx];
            clean.Add(c);
        }
        return string.Join("\n", clean);
    }
}