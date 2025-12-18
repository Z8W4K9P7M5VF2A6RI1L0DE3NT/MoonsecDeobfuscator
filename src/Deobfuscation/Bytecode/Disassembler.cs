using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using MoonsecDeobfuscator.Bytecode.Models;

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

public class Disassembler(Function rootFunction)
{
    private const int BASE_REGISTER_OFFSET = 1; 
    private readonly StringBuilder _builder = new();
    private int _indent = 0;

    public string Disassemble()
    {
        var stopwatch = Stopwatch.StartNew();
        var ast = BuildFunctionNode(rootFunction, isAnonymous: false);
        stopwatch.Stop();

        _builder.AppendLine($"-- MoonSecV3 Decompiled by Enchanted Hub");
        _builder.AppendLine($"-- Time taken: {stopwatch.Elapsed.TotalMilliseconds:F4} ms\n");

        PrintFunctionNode(ast);
        return _builder.ToString();
    }

    private FunctionNode BuildFunctionNode(Function function, bool isAnonymous)
    {
        var statements = new List<AstNode>();
        var virtualStack = new Dictionary<int, string>();
        var declaredRegisters = new HashSet<int>();
        var upvalueNames = new Dictionary<int, string>();

        string GetVal(int r) => virtualStack.TryGetValue(r, out var val) ? val : $"v{r + BASE_REGISTER_OFFSET}";

        string Const(int idx) {
            if (idx < 0 || idx >= function.Constants.Count) return "nil";
            var c = function.Constants[idx];
            return c switch {
                StringConstant s => $"\"{s.Value.Replace("\"", "\\\"")}\"",
                NumberConstant n => n.Value.ToString(),
                _ => "nil"
            };
        }

        for (int i = 0; i < function.Instructions.Count; i++)
        {
            var ins = function.Instructions[i];
            string targetName = $"v{ins.A + BASE_REGISTER_OFFSET}";

            switch (ins.OpCode)
            {
                case OpCode.Move:
                    virtualStack[ins.A] = GetVal(ins.B);
                    break;
                case OpCode.LoadK:
                    virtualStack[ins.A] = Const(ins.B);
                    statements.Add(new AssignNode(targetName, virtualStack[ins.A], !declaredRegisters.Contains(ins.A)));
                    break;
                case OpCode.GetGlobal:
                    virtualStack[ins.A] = Const(ins.B).Replace("\"", "");
                    break;
                case OpCode.GetUpval:
                    string upName = $"upvalue_{ins.B}";
                    upvalueNames[ins.B] = upName;
                    virtualStack[ins.A] = upName;
                    break;
                case OpCode.SetUpval:
                    statements.Add(new AssignNode($"upvalue_{ins.B}", GetVal(ins.A), false));
                    break;
                case OpCode.GetTable:
                    string key = RK(ins.C, function, GetVal).Replace("\"", "");
                    virtualStack[ins.A] = $"{GetVal(ins.B)}.{key}";
                    break;
                case OpCode.Call:
                    int argCount = Math.Max(0, ins.B - 1);
                    var args = Enumerable.Range(ins.A + 1, argCount).Select(GetVal).ToList();
                    string func = GetVal(ins.A);
                    if (func.Contains(".GetService")) func = func.Replace(".GetService", ":GetService");
                    
                    if (ins.C > 1) virtualStack[ins.A] = $"{func}({string.Join(", ", args)})";
                    else statements.Add(new CallNode(func, args));
                    break;
                case OpCode.Closure:
                    var child = BuildFunctionNode(function.Functions[ins.B], true);
                    statements.Add(new AssignNode(targetName, "function()", !declaredRegisters.Contains(ins.A)));
                    statements.Add(child);
                    virtualStack[ins.A] = targetName;
                    break;
                case OpCode.Jmp:
                    if (ins.B < 0) statements.Add(new RawNode("while true do"));
                    else statements.Add(new RawNode("end"));
                    break;
                case OpCode.Return:
                    int retCount = Math.Max(0, ins.B - 1);
                    if (retCount > 0)
                        statements.Add(new ReturnNode(Enumerable.Range(ins.A, retCount).Select(GetVal).ToList()));
                    break;
            }
            declaredRegisters.Add(ins.A);
        }

        return new FunctionNode(isAnonymous ? "" : function.Name, new Block(statements), upvalueNames.Values.Distinct().ToList(), isAnonymous);
    }

    private void PrintFunctionNode(FunctionNode fn)
    {
        string indentStr = new string('\t', _indent);
        bool hasHeader = fn.IsAnonymous || !string.IsNullOrEmpty(fn.Name);

        if (hasHeader) {
            _builder.AppendLine($"{indentStr}{(fn.IsAnonymous ? "function()" : $"local function {fn.Name}()")}");
            _indent++;
            if (fn.Upvalues.Count > 0)
                _builder.AppendLine($"{new string('\t', _indent)}-- upvalues: {string.Join(", ", fn.Upvalues.Select(u => $"(ref) {u}"))}");
        }

        foreach (var node in fn.Body.Statements) PrintAstNode(node);

        if (hasHeader) {
            _indent--;
            _builder.AppendLine($"{new string('\t', _indent)}end");
        }
    }

    private void PrintAstNode(AstNode node) 
    {
        string indent = new string('\t', _indent);
        switch (node)
        {
            case AssignNode a: 
                if (a.Right == "game" || a.Right == "workspace" || a.Right.Contains(":GetService")) return;
                _builder.AppendLine($"{indent}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right}"); 
                break;
            case CallNode c: _builder.AppendLine($"{indent}{c.Func}({string.Join(", ", c.Args)})"); break;
            case ReturnNode r: _builder.AppendLine($"{indent}return {string.Join(", ", r.Values)}"); break;
            case RawNode raw: _builder.AppendLine($"{indent}{raw.Code}"); break;
            case FunctionNode f: PrintFunctionNode(f); break;
        }
    }

    private string RK(int val, Function f, Func<int, string> getVal) => val >= 256 ? (f.Constants[val - 256] is StringConstant s ? $"\"{s.Value}\"" : "nil") : getVal(val);
}

public abstract record AstNode;
public record Block(List<AstNode> Statements) : AstNode;
public record AssignNode(string Left, string Right, bool IsLocal) : AstNode;
public record CallNode(string Func, List<string> Args) : AstNode;
public record ReturnNode(List<string> Values) : AstNode;
public record RawNode(string Code) : AstNode;
public record FunctionNode(string Name, Block Body, List<string> Upvalues, bool IsAnonymous) : AstNode;
