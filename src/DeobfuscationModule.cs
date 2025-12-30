using Discord;
using Discord.Interactions;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DiscordBot;

public class DeobfuscationModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DeobfuscationService _service;

    public DeobfuscationModule(DeobfuscationService service)
    {
        _service = service;
    }

    [SlashCommand("deobfuscate", "Deobfuscates MoonSec/IB2 Lua file")]
    public async Task Deobfuscate([Summary("file", "Lua file to deobfuscate")] Attachment file)
    {
        await DeferAsync();

        if (!file.Filename.EndsWith(".lua"))
        {
            await FollowupAsync("❌ Only `.lua` files are supported.");
            return;
        }

        if (file.Size > 10 * 1024 * 1024)
        {
            await FollowupAsync("❌ File too large (max 10MB).");
            return;
        }

        try
        {
            await FollowupAsync("⚙️ Processing...");
            
            using var http = new HttpClient();
            var luaCode = await http.GetStringAsync(file.Url);
            var decompiledCode = await _service.DeobfuscateAndDecompileAsync(luaCode);
            
            var filename = $"{Guid.NewGuid():N.Substring(0, 16)}.lua";
            
            await FollowupWithFileAsync(
                new MemoryStream(Encoding.UTF8.GetBytes(decompiledCode)),
                filename,
                text: $"You Out Da Projects Twin 🔫🔫? {Context.User.Mention}"
            );
        }
        catch (Exception ex)
        {
            await FollowupAsync($"❌ Error: `{ex.Message}`");
        }
    }
}
