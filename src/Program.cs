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

            _client.Log += msg =>
            {
                Console.WriteLine(msg.ToString());
                return Task.CompletedTask;
            };

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

            if (inChannel && msg.Attachments.Count == 0)
            {
                _ = msg.DeleteAsync();
                return;
            }

            if (msg.Attachments.Count == 0) return;

            if (Stopwatch.GetTimestamp() - LastSent < TimeSpan.FromSeconds(5).Ticks)
                return;

            LastSent = Stopwatch.GetTimestamp();

            var att = msg.Attachments.First();
            var tempInput = Path.GetTempFileName() + ".lua";
            var data = await new HttpClient().GetByteArrayAsync(att.Url);
            await File.WriteAllBytesAsync(tempInput, data);

            var sw = Stopwatch.StartNew();

            var deob = new Deobfuscator().Deobfuscate(File.ReadAllText(tempInput));

            // bytecode serialization
            var bytecodePath = RandomName(9) + ".luac";
            using (var fs = new FileStream(bytecodePath, FileMode.Create))
            using (var ser = new Serializer(fs))
                ser.Serialize(deob);

            // send bytecode to luadec
            var luaClean = await UploadBytecode(bytecodePath);

            // strip all comments
            luaClean = RemoveComments(luaClean);

            // prepend custom header
            luaClean = "-- deobfuscated by galactic services join now https://discord.gg/angmZQJC8a\n\n" + luaClean;

            // save final file
            var luauOut = RandomName(8) + ".luau";
            File.WriteAllText(luauOut, luaClean);

            sw.Stop();

            await msg.Channel.SendMessageAsync(
                $"done in {sw.ElapsedTicks}ns\n" +
                $"deobfuscated file: {luauOut}\n" +
                $"bytecode file: {bytecodePath}\n" +
                $"here is bytecode aswell to see original code paste it into luadec.metaworm.site"
            );

            using (var fs1 = new FileStream(luauOut, FileMode.Open))
                await msg.Channel.SendFileAsync(fs1, luauOut);

            using (var fs2 = new FileStream(bytecodePath, FileMode.Open))
                await msg.Channel.SendFileAsync(fs2, bytecodePath);
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
            return new string(Enumerable.Range(0, len).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        }
    }
}
            string msgText =
                $"yo deobfuscated in {nanos} nanoseconds\n" +
                $"deobfuscated file name: {luaName}\n" +
                $"bytecode file name: {byteName}\n" +
                $"to view reconstructed source paste bytecode at https://luadec.metaworm.site";

            await message.Channel.SendMessageAsync(msgText);

            using (var fs1 = new FileStream(finalLuaPath, FileMode.Open, FileAccess.Read))
                await message.Channel.SendFileAsync(fs1, luaName);

            using (var fs2 = new FileStream(finalBytePath, FileMode.Open, FileAccess.Read))
                await message.Channel.SendFileAsync(fs2, byteName);
        }
    }
}
