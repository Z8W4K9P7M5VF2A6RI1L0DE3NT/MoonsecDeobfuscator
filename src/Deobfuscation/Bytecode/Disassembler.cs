#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MoonsecDeobfuscator.Bytecode.Models;
using Function = MoonsecDeobfuscator.Bytecode.Models.Function;

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

public abstract record AstNode;
public record Block(List<AstNode> Statements) : AstNode;
public record AssignNode(string Left, string Right, bool IsLocal) : AstNode;
public record CallNode(string Func, List<string> Args) : AstNode;
public record FunctionNode(string Name, Block Body, bool IsAnonymous) : AstNode;

public class Disassembler(Function rootFunction)
{
    private readonly StringBuilder _builder = new();
    private int _indent = 0;

    public string Disassemble()
    {
        var ast = BuildFunctionNode(rootFunction, "MainScript", false);
        _builder.AppendLine("-- Reconstructed High-Level Source");
        _builder.AppendLine("-- Fixed VM Call Shadowing & Variable Redundancy\n");
        PrintFunctionNode(ast);
        _builder.AppendLine("\nMainScript()");
        return _builder.ToString();
    }

    private FunctionNode BuildFunctionNode(Function function, string name, bool isAnon)
    {
        var statements = new List<AstNode>();
        var registerState = new Dictionary<int, string>();
        var declaredInScope = new HashSet<string>();

        string Get(int r) => registerState.TryGetValue(r, out var val) ? val : $"v{r}";

        for (int i = 0; i < function.Instructions.Count; i++)
        {
            var ins = function.Instructions[i];

            switch (ins.OpCode)
            {
                case OpCode.GetGlobal:
                    string gName = ((StringConstant)function.Constants[ins.B]).Value;
                    registerState[ins.A] = gName;
                    AddLocal(statements, gName, gName, declaredInScope);
                    break;

                case OpCode.LoadK:
                    string k = FormatConst(function, ins.B);
                    registerState[ins.A] = k;
                    // Don't write every constant to a variable unless it's a long string/URL
                    if (k.Length > 15) AddLocal(statements, $"c{ins.A}", k, declaredInScope);
                    break;

                case OpCode.Call:
                    int argCount = Math.Max(0, ins.B - 1);
                    var args = Enumerable.Range(ins.A + 1, argCount).Select(Get).ToList();
                    string caller = Get(ins.A);

                    // --- VM RESOLVER ---
                    // Detect if calling 'game' to get a service
                    if (caller == "game" && args.Count > 0) {
                        string service = args[0].Replace("\"", "");
                        registerState[ins.A] = service;
                        AddLocal(statements, service, $"game:GetService({args[0]})", declaredInScope);
                    } else {
                        statements.Add(new CallNode(caller, args));
                    }
                    break;

                case OpCode.GetTable:
                    string tbl = Get(ins.B);
                    string key = RK(ins.C, function).Replace("\"", "");
                    string fullName = (tbl == "game" || tbl == "workspace") ? key : $"{tbl}.{key}";
                    
                    registerState[ins.A] = key;
                    AddLocal(statements, key, fullName, declaredInScope);
                    break;

                case OpCode.Closure:
                    var child = BuildFunctionNode(function.Functions[ins.B], "", true);
                    registerState[ins.A] = "func_" + ins.B;
                    statements.Add(new AssignNode(registerState[ins.A], "function()", true));
                    statements.Add(child);
                    break;
            }
        }

        return new FunctionNode(name, new Block(statements), isAnon);
    }

    private void AddLocal(List<AstNode> stats, string name, string val, HashSet<string> declared)
    {
        // Prevent shadowing and redundant re-definitions
        if (name.StartsWith("\"") || char.IsDigit(name[0])) return;
        bool isNew = declared.Add(name);
        stats.Add(new AssignNode(name, val, isNew));
    }

    private void PrintFunctionNode(FunctionNode fn)
    {
        string indent = new string(' ', _indent * 4);
        if (!fn.IsAnonymous) _builder.AppendLine($"{indent}local function {fn.Name}()");
        _indent++;
        foreach (var node in fn.Body.Statements) PrintAstNode(node);
        _indent--;
        if (!fn.IsAnonymous) _builder.AppendLine($"{indent}end");
        else _builder.AppendLine($"{indent}end");
    }

    private void PrintAstNode(AstNode node)
    {
        string indent = new string(' ', _indent * 4);
        if (node is AssignNode a) _builder.AppendLine($"{indent}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right}");
        else if (node is CallNode c) _builder.AppendLine($"{indent}{c.Func}({string.Join(", ", c.Args)})");
        else if (node is FunctionNode f) PrintFunctionNode(f);
    }

    private string RK(int val, Function f) => val >= 256 ? FormatConst(f, val - 256) : $"v{val}";
    private string FormatConst(Function f, int i) => f.Constants[i] is StringConstant s ? $"\"{s.Value}\"" : f.Constants[i].ToString();
}
