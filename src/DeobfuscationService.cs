using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;

namespace DiscordBot;

public class DeobfuscationService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey = "medal-bot-secure-key-2025";
    private readonly string _apiEndpoint = "https://henne4g.onrender.com/decompile";

    public DeobfuscationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<string> DeobfuscateAndDecompileAsync(string luaCode)
    {
        Console.WriteLine("🔍 Deobfuscating locally...");
        var deobfuscationResult = new Deobfuscator().Deobfuscate(luaCode);

        Console.WriteLine("💾 Serializing to bytecode...");
        using var memoryStream = new MemoryStream();
        using var serializer = new Serializer(memoryStream);
        serializer.Serialize(deobfuscationResult);
        var bytecode = memoryStream.ToArray();

        Console.WriteLine($"📤 Sending {bytecode.Length} bytes to API...");
        return await SendToDecompilerApiAsync(bytecode);
    }

    private async Task<string> SendToDecompilerApiAsync(byte[] bytecode)
    {
        var base64Data = Convert.ToBase64String(bytecode);
        
        var request = new HttpRequestMessage(HttpMethod.Post, _apiEndpoint);
        request.Headers.Add("X-API-Key", _apiKey);
        request.Content = new StringContent(base64Data, Encoding.UTF8, "application/octet-stream");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsStringAsync();
    }
}
