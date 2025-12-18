using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using MoonsecDeobfuscator.Bytecode.Models;

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

public class Disassembler(Function rootFunction)
{
    private readonly StringBuilder _builder = new();
    private int _indent = 0;
    private int _v_u_counter = 1;
    
    // Tracks global "v_u_" names for specific objects/upvalues
    private readonly Dictionary<string, string> _nameMap = new();

    public string Decompile()
    {
        var stopwatch = Stopwatch.StartNew();
        var ast = BuildFunctionNode(rootFunction, false, "root");
        stopwatch.Stop();

        // No header in the output body for the snippet look
        PrintNode(ast);
        return _builder.ToString();
    }

    private string GetVUName(string id)
    {
        if (!_nameMap.ContainsKey(id))
            _nameMap[id] = $"v_u_{_v_u_counter++}";
        return _nameMap[id];
    }

    private FunctionNode BuildFunctionNode(Function function, bool isAnonymous, string funcName)
    {
        var statements = new List<AstNode>();
        var virtualStack = new string[256];
        var usedUpvalues = new HashSet<string>();

        // Local helper for Register/Constant resolution
        string RK(int val) => val >= 256 ? GetConst(val - 256, function) : (virtualStack[val] ?? $"v{val}");

        for (int i = 0; i < function.Instructions.Count; i++)
        {
            var ins = function.Instructions[i];

            switch (ins.OpCode)
            {
                case OpCode.LoadK:
                    virtualStack[ins.A] = GetConst(ins.B, function);
                    break;

                case OpCode.GetGlobal:
                    virtualStack[ins.A] = GetConst(ins.B, function).Replace("\"", "");
                    break;

                case OpCode.GetUpval:
                    string upName = GetVUName($"up_{ins.B}");
                    usedUpvalues.Add(upName); // Track for the comment block
                    virtualStack[ins.A] = upName;
                    break;

                case OpCode.GetTable:
                    {
                        string table = virtualStack[ins.B] ?? $"v{ins.B}";
                        string key = RK(ins.C);
                        // Dot notation cleanup: v28.Workspace
                        string index = key.StartsWith("\"") ? $".{key.Trim('"')}" : $"[{key}]";
                        virtualStack[ins.A] = $"{table}{index}";
                    }
                    break;

                case OpCode.SetTable:
                    {
                        string st = virtualStack[ins.A] ?? $"v{ins.A}";
                        string sk = RK(ins.B);
                        string sv = RK(ins.C);
                        string target = sk.StartsWith("\"") ? $"{st}.{sk.Trim('"')}" : $"{st}[{sk}]";
                        statements.Add(new AssignNode(target, sv, false));
                    }
                    break;

                case OpCode.Call:
                    {
                        int argCount = ins.B > 0 ? ins.B - 1 : 0;
                        var args = new List<string>();
                        for (int j = 1; j <= argCount; j++) 
                            args.Add(virtualStack[ins.A + j] ?? $"v{ins.A + j}");

                        string fn = virtualStack[ins.A] ?? $"v{ins.A}";
                        
                        // Colon Call Reconstruction: v19:CreateTab
                        if (fn.Contains(".") && args.Count > 0)
                        {
                            string obj = fn.Split('.')[0];
                            if (args[0] == obj) {
                                fn = fn.Replace(".", ":");
                                args.RemoveAt(0);
                            }
                        }

                        string callExpr = $"{fn}({string.Join(", ", args)})";
                        // If C > 1, it's a value (like CreateWindow result)
                        if (ins.C > 1) virtualStack[ins.A] = callExpr;
                        else statements.Add(new RawNode(callExpr));
                    }
                    break;

                case OpCode.Closure:
                    string closureId = GetVUName($"func_{ins.B}");
                    var child = BuildFunctionNode(function.Functions[ins.B], true, closureId);
                    
                    // In your example, if a register is assigned a function, it's 'local function name()'
                    statements.Add(new RawNode($"local function {closureId}()"));
                    statements.Add(child);
                    virtualStack[ins.A] = closureId;
                    break;

                case OpCode.Jmp:
                    // Simple Control Flow: While/If detection
                    if (ins.B < 0) statements.Add(new RawNode("while true do"));
                    else statements.Add(new RawNode("end"));
                    break;

                case OpCode.Return:
                    if (ins.B > 1) {
                        var rets = new List<string>();
                        for (int j = 0; j < ins.B - 1; j++) rets.Add(virtualStack[ins.A + j]);
                        statements.Add(new ReturnNode(rets));
                    }
                    break;
            }
        }

        var node = new FunctionNode(funcName, new Block(statements), isAnonymous);
        node.UpvalueRefs.AddRange(usedUpvalues);
        return node;
    }

    private string GetConst(int idx, Function f)
    {
        if (idx < 0 || idx >= f.Constants.Count) return "nil";
        var c = f.Constants[idx];
        return c switch {
            StringConstant s => $"\"{s.Value}\"",
            NumberConstant n => n.Value.ToString(),
            _ => "nil"
        };
    }

    private void PrintNode(AstNode node)
    {
        string tabs = new string('\t', _indent);
        switch (node)
        {
            case FunctionNode f:
                if (f.Name != "root") {
                    _indent++;
                    // Print the Upvalue Comment Block like your example: -- upvalues: (ref) v_u_2
                    if (f.UpvalueRefs.Count > 0)
                        _builder.AppendLine($"{tabs}\t-- upvalues: (ref) {string.Join(", ", f.UpvalueRefs)}");
                    
                    foreach (var s in f.Body.Statements) PrintNode(s);
                    _indent--;
                    _builder.AppendLine($"{tabs}end");
                } else {
                    foreach (var s in f.Body.Statements) PrintNode(s);
                }
                break;
            case AssignNode a:
                _builder.AppendLine($"{tabs}{a.Left} = {a.Right}");
                break;
            case RawNode r:
                _builder.AppendLine($"{tabs}{r.Code}");
                break;
            case ReturnNode ret:
                _builder.AppendLine($"{tabs}return {string.Join(", ", ret.Values)}");
                break;
        }
    }
}

// Updated Records to support Upvalue tracking
public abstract record AstNode;
public record Block(List<AstNode> Statements) : AstNode;
public record AssignNode(string Left, string Right, bool IsLocal) : AstNode;
public record ReturnNode(List<string> Values) : AstNode;
public record RawNode(string Code) : AstNode;
public record FunctionNode(string Name, Block Body, bool IsAnonymous) : AstNode
{
    public List<string> UpvalueRefs { get; } = new();
}
