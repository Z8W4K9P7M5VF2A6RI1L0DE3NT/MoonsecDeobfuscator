using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Builder;
using MoonsecDeobfuscator.Deobfuscation;
using MoonsecDeobfuscator.Deobfuscation.Bytecode;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Function = MoonsecDeobfuscator.Bytecode.Models.Function;

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

            // 🚀 Configure AI Renamer services
            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_interactions)
                .AddSingleton<DeobfuscationService>()
                // Add AI Renamer with Groq/Kimi K2
                .AddSingleton(provider => new AIPoweredLuaRenamer(new RenamerConfig
                {
                    ApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "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",
                    ApiEndpoint = "https://api.groq.com/openai/v1/chat/completions",
                    Model = "moonshotai/Kimi-K2-Instruct-0905",
                    AggressiveMode = true,
                    PreserveGlobals = false,
                    MaxFileSizeKB = 500
                }))
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
            var app = builder.Build();
            app.MapGet("/", () => "MoonSec Bot is running.");
            await app.RunAsync($"http://0.0.0.0:{portStr}");
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

        [SlashCommand("deobfuscate", "Deobfuscates MoonSecV3/IB2 Lua file.")]
        public async Task Deobfuscate([Summary("file", "Lua or text file")] IAttachment file)
        {
            await DeferAsync();

            if (!file.Filename.EndsWith(".lua") && !file.Filename.EndsWith(".txt"))
            {
                await FollowupAsync(" Only `.lua` or `.txt` files are allowed.");
                return;
            }

            // 🚀 Check file size
            if (file.Size > 500 * 1024)
            {
                await FollowupAsync(" File too large. Maximum size is 500KB.");
                return;
            }

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var bytes = await http.GetByteArrayAsync(file.Url);
                var input = Encoding.UTF8.GetString(bytes);

                if (string.IsNullOrWhiteSpace(input))
                {
                    await FollowupAsync(" File is empty or could not be read.");
                    return;
                }

                string deobfuscatedText = await _service.DeobfuscateWithAIAsync(input);
                
                if (string.IsNullOrEmpty(deobfuscatedText))
                {
                    await FollowupAsync(" Deobfuscation produced empty output.");
                    return;
                }

                byte[] outputBytes = Encoding.UTF8.GetBytes(deobfuscatedText);
                string randomHex = Guid.NewGuid().ToString("N").Substring(0, 16);
                string customFilename = $"{randomHex}.lua";

                await FollowupWithFileAsync(
                    new MemoryStream(outputBytes),
                    customFilename,
                    text: $"You Out Da Projects Twin 🔫🔫? {Context.User.Mention}"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Deobfuscation error: {ex.Message}\n{ex.StackTrace}");
                await FollowupAsync($" Error: `{ex.GetType().Name}`");
            }
        }
    }

    public class DeobfuscationService
    {
        private readonly AIPoweredLuaRenamer _renamer;

        public DeobfuscationService(AIPoweredLuaRenamer renamer)
        {
            _renamer = renamer;
        }

        public string Disassemble(string code)
        {
            var result = new Deobfuscator().Deobfuscate(code);
            return new Disassembler(result).Disassemble();
        }

        public async Task<string> DeobfuscateWithAIAsync(string code)
        {
            string rawLua = Disassemble(code);
            return await _renamer.DeobfuscateAndRenameAsync(rawLua);
        }
    }

    // 🚀 AI RENAMER CLASSES

    public class RenamerConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ApiEndpoint { get; set; } = "https://api.groq.com/openai/v1/chat/completions";
        public string Model { get; set; } = "moonshotai/Kimi-K2-Instruct-0905";
        public bool AggressiveMode { get; set; } = true;
        public bool PreserveGlobals { get; set; } = false;
        public int ContextWindow { get; set; } = 256000;
        public int MaxFileSizeKB { get; set; } = 500;
    }

    public class LuaSyntaxTree
    {
        public List<LuaFunction> Functions { get; set; } = new();
        public List<LuaVariable> Variables { get; set; } = new();
        public List<LuaTable> Tables { get; set; } = new();
        public Dictionary<string, string> RenameMap { get; set; } = new();
    }

    public class LuaFunction
    {
        public string Name { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public List<string> Parameters { get; set; } = new();
        public List<LuaVariable> LocalVariables { get; set; } = new();
        public int ScopeStart { get; set; }
        public int ScopeEnd { get; set; }
        public string Context { get; set; } = string.Empty;
        public bool IsLocal { get; set; }
    }

    public class LuaVariable
    {
        public string Name { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string TypeHint { get; set; } = string.Empty;
        public int DeclarationLine { get; set; }
        public string Context { get; set; } = string.Empty;
        public bool IsConstant { get; set; }
        public bool IsIterators { get; set; }
    }

    public class LuaTable
    {
        public string Name { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public Dictionary<string, LuaVariable> Fields { get; set; } = new();
        public List<string> MethodCalls { get; set; } = new();
    }

    public class AIPoweredLuaRenamer
    {
        private readonly RenamerConfig _config;
        private readonly HttpClient _httpClient;
        private readonly Regex _functionRegex = new(@"(?:local\s+)?function\s+([a-zA-Z_]\w*)\s*\(?([^)]*)\)?", RegexOptions.Multiline);
        private readonly Regex _variableRegex = new(@"(?:local\s+)?([a-zA-Z_]\w*)\s*=([^;]+)", RegexOptions.Multiline);
        private readonly Regex _tableRegex = new(@"([a-zA-Z_]\w*)\s*=\s*{([^}]+)}", RegexOptions.Multiline);
        private readonly Regex _obfuscatedNameRegex = new(@"^(?:v\d+|v_u_\d+|upvalue_\d+|[a-zA-Z_]{1,3}\d{2,5}|[a-zA-Z_]\d{3,})$", RegexOptions.Multiline);
        private readonly HashSet<string> _luaGlobals = new() { "print", "pairs", "ipairs", "next", "type", "tonumber", "tostring", "table", "string", "math", "os", "debug", "require", "pcall", "xpcall", "error", "assert", "select", "unpack", "load", "loadfile", "dofile", "setmetatable", "getmetatable", "rawset", "rawget", "rawequal", "collectgarbage" };

        public AIPoweredLuaRenamer(RenamerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<string> DeobfuscateAndRenameAsync(string luaCode)
        {
            if (string.IsNullOrWhiteSpace(luaCode))
                throw new ArgumentException("Input code cannot be empty", nameof(luaCode));

            var syntaxTree = ParseSyntaxTree(luaCode);
            IdentifyObfuscatedElements(syntaxTree);
            await GenerateSemanticNamesAsync(syntaxTree);
            return ApplyRenames(luaCode, syntaxTree.RenameMap);
        }

        private LuaSyntaxTree ParseSyntaxTree(string code)
        {
            var tree = new LuaSyntaxTree();
            var lines = code.Split('\n');
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                
                ParseFunctionDefinitions(line, i, tree, lines);
                ParseVariableDeclarations(line, i, tree, lines);
                ParseTableStructures(line, i, tree, lines);
            }
            
            return tree;
        }

        private void ParseFunctionDefinitions(string line, int lineNum, LuaSyntaxTree tree, string[] allLines)
        {
            var match = _functionRegex.Match(line);
            if (!match.Success) return;
            
            var func = new LuaFunction
            {
                OriginalName = match.Groups[1].Value,
                Context = ExtractContext(lineNum, allLines),
                IsLocal = line.Trim().StartsWith("local"),
                ScopeStart = lineNum
            };
            
            var paramsMatch = match.Groups[2].Value.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p));
            func.Parameters.AddRange(paramsMatch);
            
            func.ScopeEnd = FindScopeEnd(lineNum, allLines);
            tree.Functions.Add(func);
            tree.RenameMap[func.OriginalName] = func.OriginalName;
        }

        private void ParseVariableDeclarations(string line, int lineNum, LuaSyntaxTree tree, string[] allLines)
        {
            var match = _variableRegex.Match(line);
            if (!match.Success) return;
            
            var var = new LuaVariable
            {
                OriginalName = match.Groups[1].Value,
                Context = ExtractContext(lineNum, allLines),
                DeclarationLine = lineNum
            };
            
            var.IsIterators = match.Groups[2].Value.Contains("pairs") || match.Groups[2].Value.Contains("ipairs");
            var.IsConstant = DetectConstantPattern(match.Groups[2].Value);
            
            tree.Variables.Add(var);
            tree.RenameMap[var.OriginalName] = var.OriginalName;
        }

        private void ParseTableStructures(string line, int lineNum, LuaSyntaxTree tree, string[] allLines)
        {
            var match = _tableRegex.Match(line);
            if (!match.Success) return;
            
            var table = new LuaTable
            {
                OriginalName = match.Groups[1].Value,
                Context = ExtractContext(lineNum, allLines)
            };
            
            var fieldMatches = Regex.Matches(match.Groups[2].Value, @"([a-zA-Z_]\w*)\s*=\s*([^,}]+)");
            foreach (Match field in fieldMatches)
            {
                table.Fields[field.Groups[1].Value] = new LuaVariable
                {
                    OriginalName = field.Groups[1].Value,
                    Context = field.Groups[2].Value
                };
            }
            
            tree.Tables.Add(table);
            tree.RenameMap[table.OriginalName] = table.OriginalName;
        }

        private void IdentifyObfuscatedElements(LuaSyntaxTree tree)
        {
            foreach (var func in tree.Functions.Where(f => _obfuscatedNameRegex.IsMatch(f.OriginalName)))
            {
                func.Name = func.OriginalName;
            }
            
            foreach (var var in tree.Variables.Where(v => _obfuscatedNameRegex.IsMatch(v.OriginalName)))
            {
                var.Name = var.OriginalName;
            }
            
            foreach (var table in tree.Tables.Where(t => _obfuscatedNameRegex.IsMatch(t.OriginalName)))
            {
                table.Name = table.OriginalName;
            }
        }

        private async Task GenerateSemanticNamesAsync(LuaSyntaxTree tree)
        {
            var batches = CreateBatches(tree);
            var renameTasks = batches.Select(batch => ProcessBatchWithAI(batch)).ToArray();
            
            await Task.WhenAll(renameTasks);
            
            foreach (var task in renameTasks)
            {
                var batchResults = await task;
                foreach (var kvp in batchResults)
                {
                    if (tree.RenameMap.ContainsKey(kvp.Key))
                        tree.RenameMap[kvp.Key] = kvp.Value;
                }
            }
        }

        private List<Dictionary<string, string>> CreateBatches(LuaSyntaxTree tree)
        {
            var allSymbols = new Dictionary<string, string>();
            
            foreach (var func in tree.Functions.Where(f => f.Name == f.OriginalName))
                allSymbols[func.OriginalName] = $"Function: Context={func.Context}; Parameters={string.Join(",", func.Parameters)}; LocalVars={func.LocalVariables.Count}";
            
            foreach (var var in tree.Variables.Where(v => v.Name == v.OriginalName))
                allSymbols[var.OriginalName] = $"Variable: Context={var.Context}; IsConstant={var.IsConstant}; IsIterator={var.IsIterators}";
            
            foreach (var table in tree.Tables.Where(t => t.Name == t.OriginalName))
                allSymbols[table.OriginalName] = $"Table: Context={table.Context}; Fields={string.Join(",", table.Fields.Keys.Take(5))}";
            
            return allSymbols
                .GroupBy(kvp => allSymbols.Keys.ToList().IndexOf(kvp.Key) / 20)
                .Select(g => g.ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
                .ToList();
        }

        private async Task<Dictionary<string, string>> ProcessBatchWithAI(Dictionary<string, string> batch)
        {
            var prompt = $@"You are a Lua deobfuscation expert. Analyze these obfuscated identifiers and generate meaningful, descriptive names based on context, usage patterns, and Lua conventions. Return ONLY a JSON object mapping original names to new names. Names must be camelCase, descriptive, and follow Lua naming conventions. No comments, no explanations.

{JsonSerializer.Serialize(batch, new JsonSerializerOptions { WriteIndented = true })}";

            var requestBody = new
            {
                model = _config.Model,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.1,
                max_tokens = 2000
            };

            var response = await _httpClient.PostAsync(_config.ApiEndpoint, new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
            var responseContent = await response.Content.ReadAsStringAsync();
            
            var aiResponse = JsonSerializer.Deserialize<AIResponse>(responseContent);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(aiResponse.Choices[0].Message.Content);
        }

        private string ApplyRenames(string code, Dictionary<string, string> renameMap)
        {
            var sortedKeys = renameMap.Keys.OrderByDescending(k => k.Length).ThenByDescending(k => k);
            
            foreach (var original in sortedKeys)
            {
                var newName = renameMap[original];
                if (original == newName) continue;
                
                var pattern = $@"\b{Regex.Escape(original)}\b";
                code = Regex.Replace(code, pattern, newName);
            }
            
            return code;
        }

        private string ExtractContext(int lineNum, string[] allLines)
        {
            var start = Math.Max(0, lineNum - 3);
            var end = Math.Min(allLines.Length - 1, lineNum + 3);
            return string.Join(" ", allLines.Skip(start).Take(end - start + 1));
        }

        private int FindScopeEnd(int startLine, string[] allLines)
        {
            int depth = 0;
            for (int i = startLine; i < allLines.Length; i++)
            {
                depth += allLines[i].Count(c => c == '{') - allLines[i].Count(c => c == '}');
                if (depth <= 0) return i;
            }
            return allLines.Length - 1;
        }

        private bool DetectConstantPattern(string value)
        {
            return Regex.IsMatch(value ?? "", @"^\d+$") || 
                   Regex.IsMatch(value ?? "", @"^""[^""]*""$") || 
                   Regex.IsMatch(value ?? "", @"^'[^']*'$");
        }
    }

    public class AIResponse
    {
        public List<AIChoice> Choices { get; set; } = new();
    }

    public class AIChoice
    {
        public AIMessage Message { get; set; } = new();
    }

    public class AIMessage
    {
        public string Content { get; set; } = string.Empty;
    }
}
