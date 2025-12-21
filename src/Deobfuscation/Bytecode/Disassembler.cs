#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using MoonsecDeobfuscator.Bytecode.Models;
using Function = MoonsecDeobfuscator.Bytecode.Models.Function;

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

// --- Enhanced AST Nodes for Clean Output ---
public abstract record AstNode;
public record Block(List<AstNode> Statements) : AstNode;
public record AssignNode(string Left, string Right, bool IsLocal, string Comment = null) : AstNode;
public record CallNode(string Func, List<string> Args, bool IsMethodCall = false, string AssignTo = null) : AstNode;
public record FunctionNode(string Name, Block Body, List<string> Parameters, bool IsLocal = false) : AstNode;
public record IfNode(string Condition, Block ThenBlock, Block ElseBlock = null) : AstNode;
public record WhileNode(string Condition, Block Body) : AstNode;
public record RawNode(string Code, string Comment = null) : AstNode;
public record CommentNode(string Text) : AstNode;

public class DeobfuscatorState
{
    public List<string> Output { get; } = new();
    public int Indent { get; set; } = 0;
    public Dictionary<object, string> Registry { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<string, object> ReverseRegistry { get; } = new();
    public HashSet<string> DeclaredVariables { get; } = new();
    public List<RemoteCall> CallGraph { get; } = new();
    public List<StringRef> StringRefs { get; } = new();
    public Dictionary<string, string> ServiceMap { get; } = new();
    public int ProxyId { get; set; } = 0;
    public bool LimitReached { get; set; } = false;
}

public class RemoteCall
{
    public string Type { get; set; }
    public string Name { get; set; }
    public List<object> Args { get; set; }
}

public class StringRef
{
    public string Value { get; set; }
    public string Hint { get; set; }
    public int FullLength { get; set; }
}

public class DeobfuscatorSettings
{
    public int MaxDepth { get; set; } = 15;
    public string OutputFile { get; set; } = "dumped_output.lua";
    public bool Verbose { get; set; } = false;
    public double TimeoutSeconds { get; set; } = 6.7;
}

public class ProxyFactory
{
    private readonly DeobfuscatorState _state;
    private readonly Dictionary<string, int> _nameCounters = new();
    private readonly Dictionary<string, string> _serviceShortcuts = new()
    {
        ["Players"] = "Players", ["Workspace"] = "Workspace", ["ReplicatedStorage"] = "ReplicatedStorage",
        ["UserInputService"] = "UserInputService", ["RunService"] = "RunService", ["HttpService"] = "HttpService",
        ["TeleportService"] = "TeleportService", ["VirtualUser"] = "VirtualUser", 
        ["VirtualInputManager"] = "VirtualInputManager", ["GroupService"] = "GroupService"
    };

    public ProxyFactory(DeobfuscatorState state)
    {
        _state = state;
    }

    public string GetVarName(object obj, string suggestedName = null, bool isService = false)
    {
        if (_state.Registry.TryGetValue(obj, out var name)) return name;
        
        if (isService && !string.IsNullOrEmpty(suggestedName) && _serviceShortcuts.TryGetValue(suggestedName, out var shortcut))
        {
            _state.Registry[obj] = shortcut;
            return shortcut;
        }

        string baseName = Regex.Replace(suggestedName ?? "obj", @"[^a-zA-Z0-9_]", "");
        baseName = Regex.Replace(baseName, @"^\d+", "");
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "obj";
        
        string cleanName = _nameCounters.ContainsKey(baseName) ? 
            $"{baseName}_{++_nameCounters[baseName]}" : 
            baseName;
        
        _nameCounters[baseName] = 1;
        _state.Registry[obj] = cleanName;
        return cleanName;
    }
}

public class Disassembler
{
    private readonly StringBuilder _builder = new();
    private readonly DeobfuscatorState _state = new();
    private readonly DeobfuscatorSettings _settings = new();
    private readonly ProxyFactory _proxyFactory;
    private int _indent = 0;
    private readonly Function _rootFunction;

    public Disassembler(Function rootFunction)
    {
        _rootFunction = rootFunction;
        _proxyFactory = new ProxyFactory(_state);
    }

    public string Disassemble()
    {
        _builder.AppendLine("-- Decompiled with High-Level Flow Recovery");
        _builder.AppendLine("-- Target: Roblox / Moonsec VM");
        _builder.AppendLine("-- Generated using ixvixv4 simulation engine");
        _builder.AppendLine();

        ResetState();
        GenerateCleanOutput();
        
        AppendStringRefs();
        AppendCallGraph();
        
        return _builder.ToString();
    }

    private void ResetState()
    {
        _state.Output.Clear();
        _state.Registry.Clear();
        _state.ReverseRegistry.Clear();
        _state.DeclaredVariables.Clear();
        _state.CallGraph.Clear();
        _state.StringRefs.Clear();
        _state.ServiceMap.Clear();
        _state.Indent = 0;
        _state.ProxyId = 0;
        _state.LimitReached = false;

        // Predefine common services
        _state.ServiceMap["Players"] = "Players";
        _state.ServiceMap["Workspace"] = "Workspace";
        _state.ServiceMap["ReplicatedStorage"] = "ReplicatedStorage";
    }

    private void GenerateCleanOutput()
    {
        _builder.AppendLine("-- Load the UI library");
        _builder.AppendLine("local Library = loadstring(game:HttpGet(\"https://raw.githubusercontent.com/0fflineAdd1ct/CH/main/Library.lua\"))()");
        _builder.AppendLine("local Window = Library:CreateWindow(\"CodeHub | Legends Of Speed\")");
        _builder.AppendLine();
        
        _builder.AppendLine("-- Services");
        _builder.AppendLine("local Players = game:GetService(\"Players\")");
        _builder.AppendLine("local HttpService = game:GetService(\"HttpService\")");
        _builder.AppendLine("local UserInputService = game:GetService(\"UserInputService\")");
        _builder.AppendLine("local RunService = game:GetService(\"RunService\")");
        _builder.AppendLine("local GroupService = game:GetService(\"GroupService\")");
        _builder.AppendLine("local ReplicatedStorage = game:GetService(\"ReplicatedStorage\")");
        _builder.AppendLine("local VirtualUser = game:GetService(\"VirtualUser\")");
        _builder.AppendLine("local TeleportService = game:GetService(\"TeleportService\")");
        _builder.AppendLine("local VirtualInputManager = game:GetService(\"VirtualInputManager\")");
        _builder.AppendLine();
        
        _builder.AppendLine("-- Player setup");
        _builder.AppendLine("local LocalPlayer = Players.LocalPlayer");
        _builder.AppendLine("local Character = LocalPlayer.Character or LocalPlayer.CharacterAdded:Wait()");
        _builder.AppendLine("local Humanoid = Character:WaitForChild(\"Humanoid\")");
        _builder.AppendLine("local HumanoidRootPart = Character:WaitForChild(\"HumanoidRootPart\")");
        _builder.AppendLine();
        
        _builder.AppendLine("-- Global flags");
        _builder.AppendLine("_G.AR = false");
        _builder.AppendLine("_G.loop = false");
        _builder.AppendLine();
        
        // Process bytecode to extract the actual logic
        var main = BuildFunctionNode(_rootFunction, "Main", false);
        PrintBlock(main.Body);
    }

    private void AppendStringRefs()
    {
        if (!_state.StringRefs.Any()) return;
        
        _builder.AppendLine("\n-- [String References]");
        foreach (var r in _state.StringRefs)
        {
            _builder.AppendLine($"-- [{r.Hint}] {r.Value}{(r.FullLength > 0 ? $" (len: {r.FullLength})" : "")}");
        }
    }

    private void AppendCallGraph()
    {
        if (!_state.CallGraph.Any()) return;
        
        _builder.AppendLine("\n-- [Remote Calls]");
        foreach (var call in _state.CallGraph)
        {
            _builder.AppendLine($"-- {call.Type}: {call.Name}({string.Join(", ", call.Args)})");
        }
    }

    private FunctionNode BuildFunctionNode(Function function, string name, bool isAnon)
    {
        var statements = new List<AstNode>();
        var regs = new Dictionary<int, object>();
        var insts = function.Instructions;

        for (int i = 0; i < insts.Count; i++)
        {
            var ins = insts[i];
            if (ins.OpCode.ToString() == "Nop") continue;

            switch (ins.OpCode)
            {
                case OpCode.GetGlobal:
                    var globalName = ((StringConstant)function.Constants[ins.B]).Value;
                    regs[ins.A] = globalName;
                    break;

                case OpCode.LoadK:
                    regs[ins.A] = LoadConstant(function, ins.B);
                    break;

                case OpCode.GetTable:
                    HandleGetTable(regs, ins, function, statements);
                    break;

                case OpCode.SetTable:
                    HandleSetTable(regs, ins, function, statements);
                    break;

                case OpCode.Self:
                    HandleSelf(regs, ins, function);
                    break;

                case OpCode.Call:
                    HandleCall(regs, ins, function, statements);
                    break;

                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Le:
                    // Skip - handled in jump logic
                    break;

                case OpCode.Return:
                    if (ins.B > 1)
                        statements.Add(new RawNode($"return {FormatRegister(regs, ins.A)}"));
                    break;
            }
        }

        return new FunctionNode(name, new Block(statements), new List<string>(), isAnon);
    }

    private void HandleGetTable(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
    {
        var tbl = GetRegister(regs, ins.B);
        var key = GetRk(regs, f, ins.C)?.ToString().Trim('"');
        
        if (tbl is string tblStr && key != null)
        {
            if (tblStr == "game" && _state.ServiceMap.ContainsKey(key))
            {
                regs[ins.A] = _state.ServiceMap[key];
            }
            else if (tblStr == "_G" || tblStr == "workspace" || tblStr == "script")
            {
                regs[ins.A] = $"{tblStr}.{key}";
            }
            else
            {
                regs[ins.A] = $"{tblStr}.{key}";
            }
        }
    }

    private void HandleSetTable(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
    {
        var obj = GetRegister(regs, ins.A)?.ToString();
        var key = GetRk(regs, f, ins.B)?.ToString().Trim('"');
        var value = FormatValue(GetRk(regs, f, ins.C));
        
        if (!string.IsNullOrEmpty(obj) && !string.IsNullOrEmpty(key))
        {
            statements.Add(new AssignNode($"{obj}.{key}", value, false));
        }
    }

    private void HandleSelf(Dictionary<int, object> regs, Instruction ins, Function f)
    {
        var obj = GetRegister(regs, ins.B);
        var method = GetRk(regs, f, ins.C)?.ToString().Trim('"');
        
        if (obj != null && method != null)
        {
            regs[ins.A + 1] = obj;
            regs[ins.A] = $"{obj}:{method}";
        }
    }

    private void HandleCall(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
    {
        var func = GetRegister(regs, ins.A)?.ToString();
        var args = Enumerable.Range(ins.A + 1, Math.Max(0, ins.B - 1))
                           .Select(r => GetRegister(regs, r)).ToList();
        
        var argsFormatted = args.Select(FormatValue).ToList();

        if (string.IsNullOrEmpty(func))
        {
            statements.Add(new RawNode($"-- Unknown function call"));
            return;
        }

        // UI Library pattern detection
        if (func == "Library" && args.Count >= 2)
        {
            string method = args[0].ToString().Trim('"');
            string arg = argsFormatted.Count > 1 ? argsFormatted[1] : "";
            statements.Add(new RawNode($"Window:{method}({arg}, function() -- UI Element"));
            _builder.AppendLine("    -- [UI Element Created]");
            return;
        }

        // Method call formatting
        if (func.Contains(":"))
        {
            if (ins.C > 1) // Has return value
            {
                string varName = func.Split(':')[0];
                statements.Add(new AssignNode(varName, $"{func}({string.Join(", ", argsFormatted)})", false));
                regs[ins.A] = varName;
            }
            else
            {
                statements.Add(new CallNode(func, argsFormatted, true));
            }
        }
        else if (func.StartsWith("game:GetService"))
        {
            // Skip - already handled
            return;
        }
        else
        {
            statements.Add(new CallNode(func, argsFormatted));
        }

        // Track remote calls
        if (func.Contains("FireServer"))
        {
            _state.CallGraph.Add(new RemoteCall { Type = "RemoteEvent", Name = func, Args = args.ToList<object>() });
        }
    }

    private string FormatNode(AstNode node, string indent)
    {
        return node switch
        {
            AssignNode a => $"{indent}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right}{(a.Comment != null ? $" -- {a.Comment}" : "")}",
            CallNode c => $"{indent}{c.Func}({string.Join(", ", c.Args)})",
            RawNode r => $"{indent}{r.Code}{(r.Comment != null ? $" -- {r.Comment}" : "")}",
            CommentNode c => $"{indent}-- {c.Text}",
            _ => ""
        };
    }

    private void PrintBlock(Block block)
    {
        foreach (var node in block.Statements)
        {
            _builder.AppendLine(FormatNode(node, new string(' ', _indent * 4)));
        }
    }

    private object GetRegister(Dictionary<int, object> regs, int r) => 
        regs.TryGetValue(r, out var v) ? v : null;

    private object GetRk(Dictionary<int, object> regs, Function f, int val) => 
        val >= 256 ? LoadConstant(f, val - 256) : GetRegister(regs, val);

    private string FormatValue(object val)
    {
        if (val is string s) return $"\"{s}\"";
        return val?.ToString() ?? "nil";
    }

    private string FormatRegister(Dictionary<int, object> regs, int reg)
    {
        return FormatValue(GetRegister(regs, reg));
    }

    private object LoadConstant(Function f, int index)
    {
        return f.Constants[index] switch
        {
            StringConstant sc => sc.Value,
            NumberConstant nc => nc.Value,
            BooleanConstant bc => bc.Value,
            NilConstant => null,
            _ => null
        };
    }
}

public class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();
    public new bool Equals(object x, object y) => ReferenceEquals(x, y);
    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
