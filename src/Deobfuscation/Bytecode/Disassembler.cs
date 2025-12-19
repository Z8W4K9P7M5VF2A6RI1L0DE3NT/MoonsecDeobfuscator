#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MoonsecDeobfuscator.Bytecode.Models;
using Function = MoonsecDeobfuscator.Bytecode.Models.Function;

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

public class Disassembler(Function rootFunction)
{
    private readonly StringBuilder _builder = new();
    private int _indent = 0;

    public string Disassemble()
    {
        var ast = BuildFunctionNode(rootFunction, "MainScript", false);
        _builder.AppendLine("-- Reconstructed High-Level Source");
        _builder.AppendLine("-- Universal Property & Service Mapping Enabled\n");
        PrintFunctionNode(ast);
        _builder.AppendLine("\nMainScript()");
        return _builder.ToString();
    }

    private FunctionNode BuildFunctionNode(Function function, string name, bool isAnon)
    {
        var statements = new List<AstNode>();
        var registerState = new Dictionary<int, RegisterIdentity>();
        var declaredRegs = new HashSet<int>();

        // Resolves the current best name for a register
        string GetName(int r) => registerState.TryGetValue(r, out var id) ? id.CurrentName : $"v{r + 1}";

        for (int i = 0; i < function.Instructions.Count; i++)
        {
            var ins = function.Instructions[i];
            bool isFirst = !declaredRegs.Contains(ins.A);
            if (!registerState.ContainsKey(ins.A)) registerState[ins.A] = new RegisterIdentity(ins.A);

            switch (ins.OpCode)
            {
                case OpCode.GetGlobal:
                    string gName = ((StringConstant)function.Constants[ins.B]).Value;
                    registerState[ins.A].Update(gName);
                    statements.Add(new AssignNode(GetName(ins.A), gName, isFirst));
                    break;

                case OpCode.LoadK:
                    string k = FormatConst(function, ins.B);
                    if (k.Contains("http")) registerState[ins.A].Update("LibraryURL");
                    statements.Add(new AssignNode(GetName(ins.A), k, isFirst));
                    break;

                case OpCode.Call:
                    int argCount = Math.Max(0, ins.B - 1);
                    var args = Enumerable.Range(ins.A + 1, argCount).Select(GetName).ToList();
                    string fName = GetName(ins.A);

                    // Universal Service & Method Result Tracking
                    if ((fName == "game" || fName.EndsWith("GetService")) && args.Count > 0)
                    {
                        string serviceName = args[0].Replace("\"", "");
                        registerState[ins.A].Update(serviceName);
                    }
                    else if (fName.StartsWith("Create"))
                    {
                        registerState[ins.A].Update(fName.Replace("Create", ""));
                    }

                    statements.Add(new CallNode(fName, args));
                    break;

                case OpCode.GetTable:
                    string tbl = GetName(ins.B);
                    string key = RK(ins.C, function).Replace("\"", "");
                    
                    // Universal Property Tracking: Name the variable after the key it indexed
                    registerState[ins.A].Update(key);

                    statements.Add(new AssignNode(GetName(ins.A), $"{tbl}.{key}", isFirst));
                    break;

                case OpCode.Closure:
                    var child = BuildFunctionNode(function.Functions[ins.B], "", true);
                    statements.Add(new AssignNode(GetName(ins.A), "function()", isFirst));
                    statements.Add(child);
                    break;

                case OpCode.Move:
                    if (registerState.TryGetValue(ins.B, out var identity))
                        registerState[ins.A].Update(identity.CurrentName);
                    statements.Add(new AssignNode(GetName(ins.A), GetName(ins.B), isFirst));
                    break;
            }
            declaredRegs.Add(ins.A);
        }

        return new FunctionNode(name, new Block(CleanPass(statements)), isAnon);
    }

    private List<AstNode> CleanPass(List<AstNode> nodes)
    {
        return nodes.Where(n => !(n is AssignNode a && a.Left == a.Right)).ToList();
    }

    private void PrintFunctionNode(FunctionNode fn)
    {
        string indent = new string(' ', _indent * 4);
        _builder.AppendLine($"{indent}{(fn.IsAnonymous ? "function()" : $"local function {fn.Name}()")}");
        _indent++;
        foreach (var node in fn.Body.Statements) PrintAstNode(node);
        _indent--;
        _builder.AppendLine($"{indent}end");
    }

    private void PrintAstNode(AstNode node)
    {
        string indent = new string(' ', _indent * 4);
        if (node is AssignNode a) _builder.AppendLine($"{indent}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right}");
        else if (node is CallNode c) _builder.AppendLine($"{indent}{c.Func}({string.Join(", ", c.Args)})");
        else if (node is FunctionNode f) PrintFunctionNode(f);
    }

    private string RK(int val, Function f) => val >= 256 ? FormatConst(f, val - 256) : $"v{val + 1}";
    private string FormatConst(Function f, int i) => f.Constants[i] is StringConstant s ? $"\"{s.Value}\"" : f.Constants[i].ToString();
}

public class RegisterIdentity(int id)
{
    public string CurrentName { get; private set; } = $"v{id + 1}";
    private static readonly HashSet<string> UsedNames = new();

    public void Update(string suggestedName)
    {
        string name = Regex.Replace(suggestedName, @"[^a-zA-Z0-9_]", "");
        if (string.IsNullOrEmpty(name) || name.Length < 2) return;
        if (char.IsDigit(name[0])) name = "var_" + name;

        string uniqueName = name;
        int counter = 1;
        while (UsedNames.Contains(uniqueName) && uniqueName != CurrentName)
        {
            uniqueName = name + "_" + counter++;
        }

        CurrentName = uniqueName;
        UsedNames.Add(uniqueName);
    }
}
