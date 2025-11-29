using Discord;
using Discord.WebSocket;
using System.Text.RegularExpressions;
using MoonsecDeobfuscator.Deobfuscation;

namespace MoonsecDeobfuscator
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static ulong TargetChannelId = 1444258745336070164;
        private static HashSet<ulong> _handled = new HashSet<ulong>();

        public static async Task Main(string[] args)
        {
            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("BOT_TOKEN env missing");
                return;
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.All
            });

            _client.Log += msg =>
            {
                Console.WriteLine(msg.ToString());
                return Task.CompletedTask;
            };

            _client.Ready += async () =>
            {
                await _client.SetStatusAsync(UserStatus.DoNotDisturb);
                await _client.SetActivityAsync(new Game("Galactic Deobfuscation Service • Free", ActivityType.Playing));
            };

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;
            if (_handled.Contains(msg.Id)) return;
            _handled.Add(msg.Id);

            bool okChannel = msg.Channel.Id == TargetChannelId;
            bool okDM = msg.Channel is SocketDMChannel;

            if (!okChannel && !okDM) return;

            string text = msg.Content ?? "";

            // detect loadstring URL patterns
            string url = null;

            var r1 = Regex.Match(text, @"loadstring\s*\(\s*game:HttpGet\s*\(\s*[""'](.*?)[""']\s*\)");
            var r2 = Regex.Match(text, @"loadstring\s*\(\s*request\s*\(\s*\{.*?Url\s*=\s*[""'](.*?)[""'].*?\}\s*\)");

            if (r1.Success) url = r1.Groups[1].Value;
            if (r2.Success) url = r2.Groups[1].Value;

            // loadstring URL deobfuscation
            if (url != null)
            {
                var tempIn = Path.GetTempFileName() + ".lua";
                var tempOut = Path.GetTempFileName() + ".lua";

                using (var client = new HttpClient())
                {
                    try
                    {
                        var data = await client.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(tempIn, data);
                    }
                    catch
                    {
                        await msg.Channel.SendMessageAsync("invalid url");
                        return;
                    }
                }

                await ProcessAndSend(tempIn, tempOut, msg);
                return;
            }

            // file attachments
            if (msg.Attachments.Count > 0)
            {
                var file = msg.Attachments.First();
                var tempIn = Path.GetTempFileName() + ".lua";
                var tempOut = Path.GetTempFileName() + ".lua";

                using (var client = new HttpClient())
                {
                    try
                    {
                        var data = await client.GetByteArrayAsync(file.Url);
                        await File.WriteAllBytesAsync(tempIn, data);
                    }
                    catch
                    {
                        await msg.Channel.SendMessageAsync("failed to download file");
                        return;
                    }
                }

                await ProcessAndSend(tempIn, tempOut, msg);
                return;
            }
        }

        private static async Task ProcessAndSend(string tempIn, string tempOut, SocketMessage msg)
        {
            string code = File.ReadAllText(tempIn);

            code = Regex.Replace(code, @"--.*?$", "", RegexOptions.Multiline);
            code = Regex.Replace(code, @"/\*[\s\S]*?\*/", "");

            var result = new Deobfuscator().Deobfuscate(code);

            string luaText = "";
            if (result is string s) luaText = s;
            else if (result is byte[] b) luaText = System.Text.Encoding.UTF8.GetString(b);
            else luaText = result?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(luaText))
                luaText = "-- failed to extract";

            string final = "-- file deobfuscated by galactic services\n\n" + luaText;

            File.WriteAllText(tempOut, final);

            try
            {
                using var fs = new FileStream(tempOut, FileMode.Open, FileAccess.Read);
                await msg.Channel.SendFileAsync(fs, "deobf.lua");
            }
            catch
            {
                await msg.Channel.SendMessageAsync("failed to send file");
            }
        }
    }
}
