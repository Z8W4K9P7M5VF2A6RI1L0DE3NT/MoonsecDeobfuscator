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

// --- Clean AST Nodes ---
public abstract record AstNode;
public record Block(List<AstNode> Statements) : AstNode;
public record AssignNode(string Left, string Right, bool IsLocal, string Comment = null) : AstNode;
public record CallNode(string Func, List<string> Args, bool IsMethodCall = false, string AssignTo = null) : AstNode;
public record FunctionNode(string Name, Block Body, List<string> Parameters, bool IsLocal = false) : AstNode;
public record IfNode(string Condition, Block ThenBlock, Block ElseBlock = null) : AstNode;
public record WhileNode(string Condition, Block Body) : AstNode;
public record ForNode(string Var, string Iterable, Block Body) : AstNode;
public record RawNode(string Code, string Comment = null) : AstNode;
public record CommentNode(string Text) : AstNode;

public class DeobfuscatorState
{
    public List<string> Output { get; } = new();
    public int Indent { get; set; } = 0;
    public Dictionary<object, string> Registry { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<string> DeclaredVariables { get; } = new();
    public List<RemoteCall> CallGraph { get; } = new();
    public List<StringRef> StringRefs { get; } = new();
    public Dictionary<string, string> ServiceMap { get; } = new();
    public int ProxyId { get; set; } = 0;
    public string LastHttpUrl { get; set; } = null;
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
    public string OutputFile { get; set; } = "clean_output.lua";
    public bool Verbose { get; set; } = false;
}

public static class ProxyMarkers
{
    public static readonly object ProxyIdMarker = new();
}

public class ProxyFactory
{
    private readonly DeobfuscatorState _state;
    private readonly Dictionary<string, int> _nameCounters = new();
    private readonly HashSet<string> _uiLibraries = new() { "Library", "Rayfield", "OrionLib", "Kavo" };

    public ProxyFactory(DeobfuscatorState state)
    {
        _state = state;
    }

    public string GetVarName(object obj, string suggestedName = null, bool isService = false)
    {
        if (_state.Registry.TryGetValue(obj, out var name)) return name;
        
        // Clean the suggested name
        string baseName = suggestedName ?? "obj";
        baseName = Regex.Replace(baseName, @"[^a-zA-Z0-9_]", "");
        baseName = Regex.Replace(baseName, @"^\d+", "");
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "obj";
        
        // Check for UI library patterns
        if (_uiLibraries.Contains(baseName))
        {
            _state.Registry[obj] = baseName;
            return baseName;
        }
        
        // Services get clean names
        if (isService || baseName.EndsWith("Service"))
        {
            string serviceName = baseName.Replace("Service", "");
            if (!_nameCounters.ContainsKey(serviceName))
            {
                _state.Registry[obj] = serviceName;
                _nameCounters[serviceName] = 1;
                return serviceName;
            }
        }

        // Generate unique name
        string cleanName = _nameCounters.ContainsKey(baseName) ? 
            $"{baseName}_{++_nameCounters[baseName]}" : 
            baseName;
        
        if (!_nameCounters.ContainsKey(baseName))
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
    private readonly Function _rootFunction;
    private int _indentLevel = 0;

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
        var main = BuildFunctionNode(_rootFunction, "Main", false);
        ProcessBlock(main.Body);
        
        AppendStringRefs();
        AppendCallGraph();
        
        return _builder.ToString();
    }

    private void ResetState()
    {
        _builder.Clear();
        _state.Registry.Clear();
        _state.DeclaredVariables.Clear();
        _state.CallGraph.Clear();
        _state.StringRefs.Clear();
        _state.ServiceMap.Clear();
        _state.Indent = 0;
        _state.ProxyId = 0;
        _state.LastHttpUrl = null;
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
            _builder.AppendLine($"-- {call.Type}: {call.Name}({string.Join(", ", call.Args.Select(a => a?.ToString() ?? "nil"))})");
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
                    regs[ins.A] = HandleTableAccess(regs, ins, function);
                    break;

                case OpCode.SetTable:
                    HandlePropertyAssignment(regs, ins, function, statements);
                    break;

                case OpCode.Self:
                    HandleSelfCall(regs, ins, function, statements);
                    break;

                case OpCode.Call:
                    HandleCall(regs, ins, function, statements);
                    break;

                case OpCode.Closure:
                    regs[ins.A] = $"<function_{ins.B}>";
                    break;

                case OpCode.Jmp:
                    // Control flow handled by pattern matching
                    break;

                case OpCode.Return:
                    if (ins.B > 1)
                        statements.Add(new RawNode($"return {FormatRegister(regs, ins.A)}"));
                    break;
            }
        }

        return new FunctionNode(name, new Block(statements), new List<string>(), isAnon);
    }

    private object HandleTableAccess(Dictionary<int, object> regs, Instruction ins, Function f)
    {
        var tbl = GetRegister(regs, ins.B);
        var key = GetRk(regs, f, ins.C)?.ToString().Trim('"');
        
        if (tbl is string tblStr && key != null)
        {
            if (tblStr == "game" && !_state.DeclaredVariables.Contains(key) && key.EndsWith("Service"))
            {
                string serviceVar = key.Replace("Service", "");
                _state.DeclaredVariables.Add(serviceVar);
                return $"game:GetService(\"{key}\")";
            }
            return $"{tblStr}.{key}";
        }
        return $"unknown.{key}";
    }

    private void HandlePropertyAssignment(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
    {
        var obj = GetRegister(regs, ins.A);
        var key = GetRk(regs, f, ins.B)?.ToString().Trim('"');
        var value = FormatValue(GetRk(regs, f, ins.C));
        
        if (obj != null && key != null)
        {
            statements.Add(new AssignNode($"{obj}.{key}", value, false));
        }
    }

    private void HandleSelfCall(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
    {
        var obj = GetRegister(regs, ins.B);
        var method = GetRk(regs, f, ins.C)?.ToString().Trim('"');
        
        if (obj != null && method != null)
        {
            // Store the object for the method call
            regs[ins.A + 1] = obj;
            regs[ins.A] = $"{obj}:{method}";
        }
    }

    private void HandleCall(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
    {
        var func = GetRegister(regs, ins.A);
        var args = Enumerable.Range(ins.A + 1, Math.Max(0, ins.B - 1))
                           .Select(r => GetRegister(regs, r)).ToList();
        
        if (func == null) return;

        string funcStr = func.ToString();
        var argsFormatted = args.Select(FormatValue).ToList();

        // UI Library loadstring pattern
        if (funcStr == "loadstring" && args.Count > 0)
        {
            string httpGetCall = args[0].ToString();
            if (httpGetCall.Contains("game:HttpGet"))
            {
                var match = Regex.Match(httpGetCall, @"game:HttpGet\((""[^""]+"")\)");
                if (match.Success)
                {
                    string url = match.Groups[1].Value;
                    _state.LastHttpUrl = url.Trim('"');
                    _state.StringRefs.Add(new StringRef { Value = _state.LastHttpUrl, Hint = "HTTP URL" });
                    
                    statements.Add(new AssignNode("Library", $"loadstring(game:HttpGet({match.Groups[1].Value}))()", true));
                    regs[ins.A] = "Library";
                    return;
                }
            }
        }

        // UI Method calls
        if (funcStr.StartsWith("Library:") || funcStr.StartsWith("Window:"))
        {
            string methodCall = funcStr;
            if (args.Count > 0)
            {
                string firstArg = argsFormatted[0];
                // Remove duplicate self argument
                if (firstArg == funcStr.Split(':')[0])
                {
                    argsFormatted.RemoveAt(0);
                }
            }

            string resultVar = null;
            if (funcStr.Contains("CreateWindow") || funcStr.Contains("CreateTab"))
            {
                resultVar = funcStr.Contains("CreateWindow") ? "Window" : "Tab";
            }

            if (resultVar != null)
            {
                statements.Add(new AssignNode(resultVar, $"{methodCall}({string.Join(", ", argsFormatted)})", true));
                regs[ins.A] = resultVar;
            }
            else
            {
                statements.Add(new CallNode(methodCall, argsFormatted, true));
            }
            return;
        }

        // Remote call tracking
        if (funcStr.Contains("FireServer"))
        {
            _state.CallGraph.Add(new RemoteCall { Type = "RemoteEvent", Name = funcStr, Args = args.ToList<object>() });
        }
        else if (funcStr.Contains("InvokeServer"))
        {
            _state.CallGraph.Add(new RemoteCall { Type = "RemoteFunction", Name = funcStr, Args = args.ToList<object>() });
        }

        // Regular function calls
        if (funcStr.EndsWith(")")) // Already formatted
        {
            statements.Add(new RawNode(funcStr));
        }
        else
        {
            statements.Add(new CallNode(funcStr, argsFormatted));
        }

        // Handle return value
        if (ins.C - 1 > 0)
        {
            regs[ins.A] = $"result_{++_state.ProxyId}";
        }
    }

    private void ProcessBlock(Block block)
    {
        foreach (var node in block.Statements)
        {
            _builder.AppendLine(FormatNode(node, _indentLevel));
        }
    }

    private string FormatNode(AstNode node, int indent)
    {
        string prefix = new string(' ', indent * 4);
        return node switch
        {
            AssignNode a => $"{prefix}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right};{(a.Comment != null ? $" -- {a.Comment}" : "")}",
            CallNode c when c.IsMethodCall => $"{prefix}{c.Func}({string.Join(", ", c.Args)});",
            CallNode c => $"{prefix}{(c.AssignTo != null ? $"local {c.AssignTo} = " : "")}{c.Func}({string.Join(", ", c.Args)});",
            RawNode r => $"{prefix}{r.Code};{(r.Comment != null ? $" -- {r.Comment}" : "")}",
            CommentNode c => $"{prefix}-- {c.Text}",
            _ => ""
        };
    }

    private object GetRegister(Dictionary<int, object> regs, int r) => 
        regs.TryGetValue(r, out var v) ? v : null;

    private object GetRk(Dictionary<int, object> regs, Function f, int val) => 
        val >= 256 ? LoadConstant(f, val - 256) : GetRegister(regs, val);

    private string FormatValue(object val)
    {
        if (val is string s) return $"\"{s}\"";
        if (val is bool b) return b.ToString().ToLower();
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
