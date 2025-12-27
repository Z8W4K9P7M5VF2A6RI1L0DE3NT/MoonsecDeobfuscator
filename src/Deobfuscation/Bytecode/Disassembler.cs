#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.IO;

#region Disassembler Core
public class MoonSecDisassembler
{
    // Lua 5.1 bytecode opcodes
    private enum OpCode
    {
        MOVE, LOADK, LOADBOOL, LOADNIL, GETUPVAL, GETGLOBAL, GETTABLE,
        SETGLOBAL, SETUPVAL, SETTABLE, NEWTABLE, SELF, ADD, SUB, MUL,
        DIV, MOD, POW, UNM, NOT, LEN, CONCAT, JMP, EQ, LT, LE, TEST,
        TESTSET, CALL, TAILCALL, RETURN, FORLOOP, FORPREP, TFORLOOP,
        SETLIST, CLOSE, CLOSURE, VARARG
    }
    
    private class Instruction
    {
        public OpCode OpCode { get; set; }
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public int Bx { get; set; }
        public int sBx { get; set; }
    }
    
    private class Function
    {
        public List<Instruction> Instructions { get; set; } = new();
        public List<Constant> Constants { get; set; } = new();
        public List<Upvalue> Upvalues { get; set; } = new();
        public List<Function> Functions { get; set; } = new();
    }
    
    private abstract class Constant { }
    private class StringConstant : Constant { public string Value { get; set; } = ""; }
    private class NumberConstant : Constant { public double Value { get; set; } }
    private class Upvalue { public string Name { get; set; } = ""; }
    
    public string Disassemble(byte[] bytecode)
    {
        try
        {
            var function = ParseBytecode(bytecode);
            return GeneratePseudocode(function);
        }
        catch (Exception ex)
        {
            return $"-- Disassembly Error: {ex.Message}\n-- Bytecode length: {bytecode.Length} bytes";
        }
    }
    
    private Function ParseBytecode(byte[] data)
    {
        var function = new Function();
        
        // Simple bytecode parser for common patterns
        int pos = 0;
        
        while (pos < data.Length)
        {
            if (pos + 4 > data.Length) break;
            
            var opcode = data[pos];
            if (opcode > 37) break; // Invalid opcode
            
            var instruction = new Instruction { OpCode = (OpCode)opcode };
            pos++;
            
            // Parse instruction based on opcode
            switch (instruction.OpCode)
            {
                case OpCode.MOVE:
                case OpCode.LOADNIL:
                    if (pos + 2 <= data.Length)
                    {
                        instruction.A = data[pos++];
                        instruction.B = data[pos++];
                    }
                    break;
                    
                case OpCode.LOADK:
                case OpCode.GETGLOBAL:
                case OpCode.SETGLOBAL:
                    if (pos + 3 <= data.Length)
                    {
                        instruction.A = data[pos++];
                        instruction.Bx = BitConverter.ToUInt16(data, pos);
                        pos += 2;
                    }
                    break;
                    
                case OpCode.LOADBOOL:
                case OpCode.GETUPVAL:
                case OpCode.SETUPVAL:
                case OpCode.GETTABLE:
                case OpCode.SETTABLE:
                case OpCode.NEWTABLE:
                case OpCode.SELF:
                    if (pos + 3 <= data.Length)
                    {
                        instruction.A = data[pos++];
                        instruction.B = data[pos++];
                        instruction.C = data[pos++];
                    }
                    break;
                    
                case OpCode.CALL:
                case OpCode.TAILCALL:
                case OpCode.RETURN:
                    if (pos + 3 <= data.Length)
                    {
                        instruction.A = data[pos++];
                        instruction.B = data[pos++];
                        instruction.C = data[pos++];
                    }
                    break;
                    
                case OpCode.CLOSURE:
                    if (pos + 3 <= data.Length)
                    {
                        instruction.A = data[pos++];
                        instruction.Bx = BitConverter.ToUInt16(data, pos);
                        pos += 2;
                        // Create child function placeholder
                        function.Functions.Add(new Function());
                    }
                    break;
            }
            
            function.Instructions.Add(instruction);
        }
        
        // Add some placeholder constants
        function.Constants.Add(new StringConstant { Value = "game" });
        function.Constants.Add(new StringConstant { Value = "Players" });
        function.Constants.Add(new StringConstant { Value = "GetService" });
        
        return function;
    }
    
    private string GeneratePseudocode(Function function)
    {
        var sb = new StringBuilder();
        var registerMap = new Dictionary<int, string>();
        int registerCounter = 1;
        int upvalueCounter = 0;
        
        string GetRegisterName(int reg)
        {
            if (!registerMap.ContainsKey(reg))
                registerMap[reg] = reg < 2 ? $"v{reg + 1}" : $"v_u_{reg + 1}";
            return registerMap[reg];
        }
        
        string GetConstant(int idx)
        {
            if (idx < 0 || idx >= function.Constants.Count)
                return $"\"CONST_{idx}\"";
                
            var constant = function.Constants[idx];
            if (constant is StringConstant s)
                return $"\"{s.Value.Replace("\"", "\\\"")}\"";
            else if (constant is NumberConstant n)
                return n.Value.ToString();
                
            return "nil";
        }
        
        // Generate function header
        sb.AppendLine("local function v1()");
        sb.AppendLine("  -- upvalues: (ref) v_u_3");
        sb.AppendLine();
        
        // Process instructions
        foreach (var ins in function.Instructions)
        {
            switch (ins.OpCode)
            {
                case OpCode.MOVE:
                    sb.AppendLine($"  local {GetRegisterName(ins.A)} = {GetRegisterName(ins.B)}");
                    break;
                    
                case OpCode.LOADK:
                    sb.AppendLine($"  local {GetRegisterName(ins.A)} = {GetConstant(ins.Bx)}");
                    break;
                    
                case OpCode.GETGLOBAL:
                    sb.AppendLine($"  local {GetRegisterName(ins.A)} = game:GetService({GetConstant(ins.Bx)})");
                    break;
                    
                case OpCode.GETUPVAL:
                    sb.AppendLine($"  local {GetRegisterName(ins.A)} = upvalue_{upvalueCounter++}");
                    break;
                    
                case OpCode.SETUPVAL:
                    sb.AppendLine($"  upvalue_{upvalueCounter++} = {GetRegisterName(ins.A)}");
                    break;
                    
                case OpCode.CALL:
                    int nargs = Math.Max(0, ins.B - 1);
                    var args = Enumerable.Range(ins.A + 1, nargs)
                        .Select(GetRegisterName);
                    sb.AppendLine($"  {GetRegisterName(ins.A)}({string.Join(", ", args)})");
                    break;
                    
                case OpCode.RETURN:
                    int nret = Math.Max(0, ins.B - 1);
                    var rets = Enumerable.Range(ins.A, nret)
                        .Select(GetRegisterName);
                    sb.AppendLine($"  return {string.Join(", ", rets)}");
                    break;
                    
                case OpCode.CLOSURE:
                    sb.AppendLine($"  local {GetRegisterName(ins.A)} = function()");
                    sb.AppendLine($"    -- nested function {ins.Bx}");
                    sb.AppendLine($"  end");
                    break;
                    
                default:
                    sb.AppendLine($"  -- {ins.OpCode} A={ins.A} B={ins.B} C={ins.C}");
                    break;
            }
        }
        
        sb.AppendLine("end");
        sb.AppendLine();
        sb.AppendLine("v1()");
        
        return sb.ToString();
    }
}
#endregion

#region Symbol Renamer (AI-Powered)
public class AISymbolRenamer
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public AISymbolRenamer(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders = { { "Authorization", $"Bearer {apiKey}" } }
        };
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }
    
    public async Task<string> RenameSymbolsAsync(string pseudocode)
    {
        // Extract symbols
        var symbols = ExtractSymbolsWithContext(pseudocode);
        if (symbols.Count == 0)
            return pseudocode;
            
        // Generate renames with AI
        var renames = await GenerateRenamesAsync(symbols);
        if (renames.Count == 0)
            return pseudocode;
            
        // Apply renames
        return ApplyRenames(pseudocode, renames);
    }
    
    private Dictionary<string, SymbolData> ExtractSymbolsWithContext(string code)
    {
        var symbols = new Dictionary<string, SymbolData>();
        var lines = code.Split('\n');
        
        // Pattern for obfuscated identifiers
        var pattern = new Regex(@"\b(v\d+|v_u_\d+|upvalue_\d+)\b");
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var matches = pattern.Matches(line);
            
            foreach (Match match in matches)
            {
                var symbol = match.Value;
                
                if (!symbols.ContainsKey(symbol))
                {
                    symbols[symbol] = new SymbolData
                    {
                        Name = symbol,
                        UsageLines = new List<int> { i },
                        Context = GetLineContext(lines, i),
                        Type = InferSymbolType(line, symbol)
                    };
                }
                else
                {
                    symbols[symbol].UsageLines.Add(i);
                }
            }
        }
        
        return symbols;
    }
    
    private string GetLineContext(string[] lines, int lineIndex)
    {
        var start = Math.Max(0, lineIndex - 2);
        var end = Math.Min(lines.Length - 1, lineIndex + 2);
        return string.Join("\n", lines.Skip(start).Take(end - start + 1));
    }
    
    private string InferSymbolType(string line, string symbol)
    {
        if (line.Contains("game:GetService")) return "Service";
        if (line.Contains("\"")) return "String";
        if (Regex.IsMatch(line, @"\b\d+(\.\d+)?\b")) return "Number";
        if (line.Contains("function()")) return "Function";
        if (line.Contains("{}")) return "Table";
        if (line.Contains("Vector3")) return "Vector";
        return "Variable";
    }
    
    private async Task<Dictionary<string, string>> GenerateRenamesAsync(Dictionary<string, SymbolData> symbols)
    {
        try
        {
            // Prepare prompt for AI
            var prompt = CreateAIPrompt(symbols);
            
            var request = new
            {
                model = "mixtral-8x7b-32768",
                messages = new[]
                {
                    new { role = "system", content = "You are a Lua deobfuscation expert. Return ONLY JSON with format: {\"oldName\": \"newName\", ...}" },
                    new { role = "user", content = prompt }
                },
                temperature = 0.1,
                max_tokens = 2000,
                response_format = new { type = "json_object" }
            };
            
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                content
            );
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"API Error: {response.StatusCode}");
                return new Dictionary<string, string>();
            }
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var aiResponse = JsonSerializer.Deserialize<AIResponse>(responseJson, _jsonOptions);
            
            if (aiResponse?.Choices?.FirstOrDefault()?.Message?.Content == null)
                return new Dictionary<string, string>();
                
            var renameJson = aiResponse.Choices.First().Message.Content;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(renameJson, _jsonOptions)
                ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AI Renaming Error: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }
    
    private string CreateAIPrompt(Dictionary<string, SymbolData> symbols)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Generate meaningful camelCase names for these Lua symbols from a decompiled MoonSecV3 script:");
        sb.AppendLine();
        
        foreach (var kvp in symbols.Take(30)) // Limit to 30 symbols
        {
            var symbol = kvp.Value;
            sb.AppendLine($"=== {symbol.Name} ===");
            sb.AppendLine($"Type: {symbol.Type}");
            sb.AppendLine($"Usage count: {symbol.UsageLines.Count}");
            sb.AppendLine($"Context:");
            sb.AppendLine(symbol.Context);
            sb.AppendLine();
        }
        
        sb.AppendLine("Examples:");
        sb.AppendLine("- v1 → playersService");
        sb.AppendLine("- v_u_3 → libraryUrl");
        sb.AppendLine("- upvalue_0 → callbackFunc");
        sb.AppendLine("- v5 → playerPosition");
        
        return sb.ToString();
    }
    
    private string ApplyRenames(string code, Dictionary<string, string> renames)
    {
        if (renames.Count == 0)
            return code;
            
        // Sort by length (longest first) to avoid partial replacements
        var sortedRenames = renames
            .OrderByDescending(kv => kv.Key.Length)
            .ThenByDescending(kv => kv.Key)
            .ToList();
        
        var result = code;
        
        foreach (var rename in sortedRenames)
        {
            if (rename.Key == rename.Value)
                continue;
                
            var pattern = $@"\b{Regex.Escape(rename.Key)}\b";
            result = Regex.Replace(result, pattern, rename.Value, RegexOptions.Multiline);
        }
        
        // Add rename summary
        var summary = new StringBuilder();
        summary.AppendLine("-- AI Renaming Summary");
        summary.AppendLine($"-- {renames.Count} symbols renamed");
        
        foreach (var rename in renames.OrderBy(r => r.Key))
        {
            summary.AppendLine($"-- {rename.Key} → {rename.Value}");
        }
        
        summary.AppendLine();
        
        return summary.ToString() + result;
    }
}

public class SymbolData
{
    public string Name { get; set; } = "";
    public List<int> UsageLines { get; set; } = new();
    public string Context { get; set; } = "";
    public string Type { get; set; } = "";
}

public class AIResponse
{
    [JsonPropertyName("choices")]
    public List<AIChoice> Choices { get; set; } = new();
}

public class AIChoice
{
    [JsonPropertyName("message")]
    public AIMessage Message { get; set; } = new();
}

public class AIMessage
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}
#endregion

#region Main Decompiler Interface
public class MoonSecDecompiler
{
    private readonly MoonSecDisassembler _disassembler;
    private readonly AISymbolRenamer _renamer;
    
    public MoonSecDecompiler(string apiKey)
    {
        _disassembler = new MoonSecDisassembler();
        _renamer = new AISymbolRenamer(apiKey);
    }
    
    public async Task<string> DecompileAsync(byte[] bytecode, bool renameSymbols = true)
    {
        Console.WriteLine($"🔍 Processing {bytecode.Length} bytes of bytecode...");
        
        // Stage 1: Disassemble bytecode to pseudocode
        var pseudocode = _disassembler.Disassemble(bytecode);
        Console.WriteLine($"📝 Generated {pseudocode.Length} chars of pseudocode");
        
        if (!renameSymbols)
            return pseudocode;
        
        // Stage 2: AI-powered symbol renaming
        Console.WriteLine("🤖 Renaming symbols with AI...");
        var renamed = await _renamer.RenameSymbolsAsync(pseudocode);
        Console.WriteLine($"✅ Renaming complete");
        
        return renamed;
    }
    
    public async Task<string> DecompileFileAsync(string filePath, bool renameSymbols = true)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            return await DecompileAsync(bytes, renameSymbols);
        }
        catch (Exception ex)
        {
            return $"-- File Error: {ex.Message}";
        }
    }
}
#endregion

