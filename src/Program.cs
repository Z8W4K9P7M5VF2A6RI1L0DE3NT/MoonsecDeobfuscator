using Discord;
using Discord.WebSocket;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;

namespace GalacticBytecodeBot
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static readonly ulong TargetChannel = 1444258745336070164;

        private static readonly Dictionary<ulong, bool> Busy = new Dictionary<ulong, bool>();

        public static async Task Main()
        {
            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("BOT_TOKEN missing");
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            });

            _client.Ready += async () =>
            {
                await _client.SetStatusAsync(UserStatus.DoNotDisturb);
                await _client.SetActivityAsync(new Game("Galactic Bytecode Generator"));
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;

            bool inAllowed = msg.Channel.Id == TargetChannel || msg.Channel is SocketDMChannel;

            // ----------- -b command -----------
            if (msg.Content.StartsWith("-b") && msg.Attachments.Count > 0)
            {
                var att = msg.Attachments.First();

                using HttpClient hc = new HttpClient();
                var rawBytes = await hc.GetByteArrayAsync(att.Url);
                string code = System.Text.Encoding.UTF8.GetString(rawBytes);

                string formatted = FormatLua(code);
                string outFile = Rand(8) + "_formatted.lua";

                await File.WriteAllTextAsync(outFile, formatted);

                var embed = new EmbedBuilder()
                    .WithTitle("Formatted Lua File")
                    .WithColor(RandomColor())
                    .WithDescription("Variables renamed to v1 v2 etc\nFunctions renamed to mn1 mn2 etc")
                    .Build();

                using (var fs = new FileStream(outFile, FileMode.Open))
                    await msg.Channel.SendFileAsync(fs, outFile, embed: embed);

                return;
            }

            if (!inAllowed) return;

            // ----------- prevent spam -----------
            if (Busy.ContainsKey(msg.Author.Id))
            {
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} please wait, your previous request is still processing.");
                return;
            }

            Busy[msg.Author.Id] = true;

            try
            {
                string sourceCode = null;

                // loadstring support
                if (msg.Content.Trim().StartsWith("loadstring"))
                {
                    int start = msg.Content.IndexOf("(");
                    int end = msg.Content.LastIndexOf(")");
                    if (start != -1 && end != -1 && end > start)
                    {
                        string inside = msg.Content.Substring(start + 1, end - start - 1).Trim();
                        inside = inside.Trim('"');
                        sourceCode = inside;
                    }
                }

                // github raw fetch
                if (msg.Content.Contains("github.com"))
                {
                    try
                    {
                        string raw = ConvertGithubToRaw(msg.Content.Trim());
                        using HttpClient hc = new HttpClient();
                        sourceCode = await hc.GetStringAsync(raw);
                    }
                    catch { }
                }

                // file support
                if (msg.Attachments.Count > 0)
                {
                    var att = msg.Attachments.First();
                    if (!IsAllowed(att.Filename))
                    {
                        await msg.Channel.SendMessageAsync($"{msg.Author.Mention} this file type is not allowed.");
                        return;
                    }

                    using HttpClient hc = new HttpClient();
                    var bytes = await hc.GetByteArrayAsync(att.Url);
                    sourceCode = System.Text.Encoding.UTF8.GetString(bytes);
                }

                if (sourceCode == null)
                {
                    return;
                }

                // Turning animation
                var statusMsg = await msg.Channel.SendMessageAsync("Turning to bytecode.");

                var cts = new CancellationTokenSource();
                var token = cts.Token;

                _ = Task.Run(async () =>
                {
                    string[] frames = { ".", "..", "..." };
                    int i = 0;

                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(1000);
                        if (token.IsCancellationRequested) break;

                        await statusMsg.ModifyAsync(m => m.Content = "Turning to bytecode" + frames[i]);
                        i = (i + 1) % frames.Length;
                    }
                });

                // deobfuscate -> serialize
                var result = new Deobfuscator().Deobfuscate(sourceCode);

                string bytecodeFile = Rand(10) + ".luac";

                using (var fs = new FileStream(bytecodeFile, FileMode.Create))
                using (var writer = new StreamWriter(fs))
                {
                    writer.WriteLine("-- bytecode by galactic join now");
                    writer.Flush();

                    using (var ser = new Serializer(fs))
                        ser.Serialize(result);
                }

                // stop animation
                cts.Cancel();
                await statusMsg.ModifyAsync(m => m.Content = "Bytecode generated.");

                var embed = new EmbedBuilder()
                    .WithTitle("Luau Bytecode Generated")
                    .WithColor(RandomColor())
                    .WithDescription(
                        "Your file has been converted to Luau bytecode.\n\n" +
                        "How to decompile:\n" +
                        "1. Open luadec.metaworm.site\n" +
                        "2. Upload the .luac file\n" +
                        "3. Wait for decompilation")
                    .WithFooter("Galactic Services")
                    .Build();

                using (var fs = new FileStream(bytecodeFile, FileMode.Open))
                    await msg.Channel.SendFileAsync(
                        fs,
                        bytecodeFile,
                        $"{msg.Author.Mention} here is your bytecode file",
                        embed: embed
                    );

                await msg.DeleteAsync();
            }
            finally
            {
                if (Busy.ContainsKey(msg.Author.Id))
                    Busy.Remove(msg.Author.Id);
            }
        }

        private static string FormatLua(string code)
        {
            int v = 1;
            int m = 1;

            code = System.Text.RegularExpressions.Regex.Replace(
                code,
                @"local\s+([A-Za-z_][A-Za-z0-9_]*)",
                (Match m1) => "local v" + (v++)
            );

            code = System.Text.RegularExpressions.Regex.Replace(
                code,
                @"function\s+([A-Za-z_][A-Za-z0-9_]*)",
                (Match m2) => "function mn" + (m++)
            );

            return code;
        }

        private static Color RandomColor()
        {
            var rand = new Random();
            return new Color(rand.Next(256), rand.Next(256), rand.Next(256));
        }

        private static bool IsAllowed(string file)
        {
            string f = file.ToLower();
            return f.EndsWith(".lua") || f.EndsWith(".luau") || f.EndsWith(".txt");
        }

        private static string ConvertGithubToRaw(string url)
        {
            if (url.Contains("raw.githubusercontent.com"))
                return url;

            url = url.Replace("github.com", "raw.githubusercontent.com");
            url = url.Replace("/blob/", "/");
            return url;
        }

        private static string Rand(int len)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz";
            char[] b = new char[len];
            byte[] d = new byte[1];
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();

            for (int i = 0; i < len; i++)
            {
                rng.GetBytes(d);
                b[i] = chars[d[0] % chars.Length];
            }

            return new string(b);
        }
    }
}