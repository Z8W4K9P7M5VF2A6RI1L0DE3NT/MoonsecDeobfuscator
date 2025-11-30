using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;

namespace MoonsecDeobfuscatorBot;

public class Program
{
    private static readonly ulong ChannelId = 1444258745336070164;
    private static readonly TimeSpan RateLimit = TimeSpan.FromSeconds(5);
    private static DateTime _lastSent = DateTime.MinValue;
    private static DiscordSocketClient _client;

    public static async Task Main(string[] args)
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents =
                GatewayIntents.Guilds |
                GatewayIntents.GuildMessages |
                GatewayIntents.DirectMessages |
                GatewayIntents.MessageContent
        });

        _client.MessageReceived += OnMessage;

        var token = Environment.GetEnvironmentVariable("RENDER_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("Missing RENDER_TOKEN env");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
        await Task.Delay(-1);
    }

    private static async Task OnMessage(SocketMessage msg)
    {
        if (msg.Author.Id == _client.CurrentUser.Id) return;

        // allow DM deobfuscation
        if (msg.Channel is IDMChannel)
        {
            await HandleFileOrUrl(msg, msg.Channel);
            return;
        }

        // channel enforcement
        if (msg.Channel.Id != ChannelId) return;

        if (msg.Attachments.Count == 0 && !ContainsUrl(msg.Content))
        {
            await msg.DeleteAsync();
            return;
        }

        await HandleFileOrUrl(msg, msg.Channel);
    }

    private static bool ContainsUrl(string t)
    {
        return Regex.IsMatch(t, @"https?://\S+");
    }

    private static async Task HandleFileOrUrl(SocketMessage msg, IMessageChannel chan)
    {
        if (DateTime.Now - _lastSent < RateLimit) return;

        string? lua = null;

        if (msg.Attachments.Count > 0)
        {
            var a = msg.Attachments.First();
            lua = await Download(a.Url);
        }
        else if (ContainsUrl(msg.Content))
        {
            var m = Regex.Match(msg.Content, @"https?://\S+");
            lua = await Download(m.Value);
        }

        if (lua == null) return;

        string output;
        try
        {
            var result = new Deobfuscator().Deobfuscate(lua);
            output = SerializeToLua(result);
        }
        catch (Exception ex)
        {
            await chan.SendMessageAsync($"Error during deobfuscation: {ex.Message}");
            return;
        }

        output = RemoveDisassembly(output);
        output = RemoveComments(output);
        output = AddHeader(output);

        var bytes = Encoding.UTF8.GetBytes(output);
        using var ms = new MemoryStream(bytes);

        await chan.SendFileAsync(ms, "deobfuscated.lua", "Deobfuscated file is ready!");
        _lastSent = DateTime.Now;
    }

    private static async Task<string> Download(string url)
    {
        using var http = new HttpClient();
        return await http.GetStringAsync(url);
    }

    private static string SerializeToLua(BytecodeFile file)
    {
        var dis = new Disassembler(file);
        var dump = dis.Disassemble();
        return dump;
    }

    private static string RemoveDisassembly(string lua)
    {
        lua = Regex.Replace(
            lua,
            @"function\s+func_[0-9a-fA-F]+\s*\(.*?\).*?end",
            "",
            RegexOptions.Singleline
        );

        lua = Regex.Replace(
            lua,
            @"\[\s*Slots:.*?\]",
            "",
            RegexOptions.Singleline
        );

        return lua;
    }

    private static string RemoveComments(string lua)
    {
        lua = Regex.Replace(lua, @"--\[\[.*?\]\]", "", RegexOptions.Singleline);
        lua = Regex.Replace(lua, @"--.*", "");
        return lua;
    }

    private static string AddHeader(string lua)
    {
        return "-- deobfuscated by galactic services join now https://discord.gg/angmZQJC8a\n" + lua;
    }
}