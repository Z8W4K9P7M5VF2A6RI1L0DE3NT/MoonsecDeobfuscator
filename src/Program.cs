using Discord;
using Discord.WebSocket;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using System.Diagnostics;
using System.Security.Cryptography;

namespace MoonsecDeobfuscator
{
    public static class Program
    {
        private static DiscordSocketClient _client;
        private static readonly ulong TargetChannelId = 1444258745336070164;

        private static long LastSent = 0;

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

            _client.MessageReceived += HandleMessage;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private static async Task HandleMessage(SocketMessage msg)
        {
            if (msg.Author.IsBot) return;

            bool inChannel = msg.Channel.Id == TargetChannelId;
            bool inDM = msg.Channel is SocketDMChannel;

            if (!inChannel && !inDM)
                return;

            // delete non-file messages inside target channel
            if (inChannel && msg.Attachments.Count == 0)
            {
                _ = msg.DeleteAsync();
                return;
            }

            if (msg.Attachments.Count == 0) return;

            // cooldown
            if (Stopwatch.GetTimestamp() - LastSent < TimeSpan.FromSeconds(5).Ticks)
                return;

            LastSent = Stopwatch.GetTimestamp();

            // download file
            var att = msg.Attachments.First();
            var tempInput = Path.GetTempFileName() + ".lua";
            var fileBytes = await new HttpClient().GetByteArrayAsync(att.Url);
            await File.WriteAllBytesAsync(tempInput, fileBytes);

            var sw = Stopwatch.StartNew();

            // run deobfuscator
            var raw = File.ReadAllText(tempInput);
            var output = new Deobfuscator().Deobfuscate(raw);

            // output CLEAN TEXT
            string cleanText;

            if (output is Function fn)
            {
                // serialize bytecode
                var byteFile = RandomName(9) + ".luac";
                using (var fs = new FileStream(byteFile, FileMode.Create))
                using (var ser = new Serializer(fs))
                    ser.Serialize(fn);

                // upload bytecode to luadec.metaworm.site
                cleanText = await UploadBytecode(byteFile);

                // strip comments
                cleanText = RemoveComments(cleanText);

                // prepend header
                cleanText = "-- deobfuscated by galactic services join now https://discord.gg/angmZQJC8a\n\n" + cleanText;

                // generate output name
                var luaOut = RandomName(8) + ".luau";

                // save clean file
                File.WriteAllText(luaOut, cleanText);

                sw.Stop();
                long nanos = (long)(sw.Elapsed.TotalMilliseconds * 1_000_000);

                // send message
                await msg.Channel.SendMessageAsync(
                    $"yo finished in {nanos}ns\n" +
                    $"deobfuscated file: {luaOut}\n" +
                    $"bytecode file: {byteFile}\n" +
                    $"here is bytecode aswell to see original code paste it into luadec.metaworm.site"
                );

                // send files
                using (var fs1 = new FileStream(luaOut, FileMode.Open))
                    await msg.Channel.SendFileAsync(fs1, luaOut);
                using (var fs2 = new FileStream(byteFile, FileMode.Open))
                    await msg.Channel.SendFileAsync(fs2, byteFile);

                return;
            }
            else
            {
                // the result is NOT bytecode output — only a normal deob string
                cleanText = output.ToString();

                cleanText = RemoveComments(cleanText);
                cleanText = "-- deobfuscated by galactic services join now https://discord.gg/angmZQJC8a\n\n" + cleanText;

                var luaOut = RandomName(8) + ".luau";
                File.WriteAllText(luaOut, cleanText);

                sw.Stop();
                long nanos = (long)(sw.Elapsed.TotalMilliseconds * 1_000_000);

                await msg.Channel.SendMessageAsync(
                    $"yo finished in {nanos}ns\n" +
                    $"deobfuscated file: {luaOut}\n" +
                    $"no bytecode generated cause file wasnt function bytecode"
                );

                using (var fs = new FileStream(luaOut, FileMode.Open))
                    await msg.Channel.SendFileAsync(fs, luaOut);
            }
        }

        private static async Task<string> UploadBytecode(string path)
        {
            using var hc = new HttpClient();
            using var form = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(path);
            var file = new ByteArrayContent(bytes);
            form.Add(file, "file", Path.GetFileName(path));

            var res = await hc.PostAsync("https://luadec.metaworm.site/decompile", form);
            return await res.Content.ReadAsStringAsync();
        }

        private static string RemoveComments(string src)
        {
            var lines = src.Split('\n');
            var clean = lines.Where(l => !l.TrimStart().StartsWith("--"));
            return string.Join("\n", clean);
        }

        private static string RandomName(int len)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz";
            return new string(Enumerable.Range(0, len)
                .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        }
    }
}
