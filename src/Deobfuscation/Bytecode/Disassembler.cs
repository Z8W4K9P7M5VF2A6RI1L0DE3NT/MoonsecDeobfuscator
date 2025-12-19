#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MoonsecDeobfuscator.Bytecode.Models;
using Function = MoonsecDeobfuscator.Bytecode.Models.Function;

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

// --- High-Level AST Nodes ---
public abstract record AstNode;
public record Block(List<AstNode> Statements) : AstNode;
public record AssignNode(string Left, string Right, bool IsLocal) : AstNode;
public record CallNode(string Func, List<string> Args) : AstNode;
public record FunctionNode(string Name, Block Body, bool IsAnonymous) : AstNode;
public record RawNode(string Code) : AstNode;

public class Disassembler(Function rootFunction)
{
    private readonly StringBuilder _builder = new();
    private int _indent = 0;

    public string Disassemble()
    {
        _builder.AppendLine("-- Decompiled with High-Level Flow Recovery");
        _builder.AppendLine("-- Target: Roblox / Moonsec VM");
        var main = BuildFunctionNode(rootFunction, "Main", false);
        PrintBlock(main.Body);
        return _builder.ToString();
    }

    private FunctionNode BuildFunctionNode(Function function, string name, bool isAnon)
    {
        var statements = new List<AstNode>();
        var regs = new Dictionary<int, string>();
        var insts = function.Instructions;
        var endMarkers = new Dictionary<int, string>();

        for (int i = 0; i < insts.Count; i++)
        {
            if (endMarkers.TryGetValue(i, out var marker))
                statements.Add(new RawNode(marker));

            var ins = insts[i];

            switch (ins.OpCode)
            {
                case OpCode.GetGlobal:
                    regs[ins.A] = ((StringConstant)function.Constants[ins.B]).Value;
                    break;

                case OpCode.LoadK:
                    regs[ins.A] = FormatConst(function, ins.B);
                    break;

                case OpCode.GetTable:
                    string tbl = GetReg(regs, ins.B);
                    string key = RK(ins.C, function).Replace("\"", "");
                    // Fix game.Players -> game:GetService("Players")
                    regs[ins.A] = (tbl == "game") ? $"game:GetService(\"{key}\")" : $"{tbl}.{key}";
                    break;

                case OpCode.Self:
                    // R(A+1) := R(B); R(A) := R(B)[RK(C)]
                    string obj = GetReg(regs, ins.B);
                    string meth = RK(ins.C, function).Replace("\"", "");
                    regs[ins.A] = $"{obj}:{meth}";
                    regs[ins.A + 1] = obj;
                    break;

                case OpCode.Call:
                    string rawFunc = GetReg(regs, ins.A);
                    var args = Enumerable.Range(ins.A + 1, Math.Max(0, ins.B - 1))
                                         .Select(r => GetReg(regs, r)).ToList();

                    // Pattern Recognition for Roblox Libraries
                    if (rawFunc.StartsWith("\"") && args.Count > 0)
                    {
                        // Handles: "Library"("Button", func) -> Library:CreateButton(func)
                        string libName = rawFunc.Replace("\"", "");
                        string method = args[0].Replace("\"", "");
                        args.RemoveAt(0);
                        statements.Add(new CallNode($"{libName}:{method}", args));
                    }
                    else if (rawFunc == "game" && args.Count > 0)
                    {
                        statements.Add(new CallNode("game:GetService", args));
                    }
                    else if (args.Any(a => a.Contains("function")))
                    {
                        // Logic for event connections
                        string fName = rawFunc.Contains(":") ? rawFunc : $"{rawFunc}:Connect";
                        statements.Add(new CallNode(fName, args));
                    }
                    else
                    {
                        statements.Add(new CallNode(rawFunc, args));
                    }
                    break;

                case OpCode.SetTable:
                    statements.Add(new AssignNode($"{GetReg(regs, ins.A)}.{RK(ins.B, function).Replace("\"", "")}", RK(ins.C, function), false));
                    break;

                case OpCode.Closure:
                    var child = BuildFunctionNode(function.Functions[ins.B], "", true);
                    regs[ins.A] = FormatClosure(child);
                    break;

                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Le:
                    // Pattern for Control Flow (If/While)
                    if (i + 1 < insts.Count && insts[i + 1].OpCode == OpCode.Jmp)
                    {
                        var jmp = insts[i + 1];
                        string op = ins.OpCode switch { OpCode.Lt => "<", OpCode.Le => "<=", _ => "==" };
                        string cond = $"{RK(ins.B, function)} {op} {RK(ins.C, function)}";
                        if (ins.A == 1) cond = $"not ({cond})";

                        if (jmp.B < 0) {
                            statements.Add(new RawNode($"while {cond} do"));
                        } else {
                            statements.Add(new RawNode($"if {cond} then"));
                            endMarkers[i + jmp.B + 2] = "end";
                        }
                        i++; // Skip JMP
                    }
                    break;

                case OpCode.Return:
                    if (ins.B > 1) statements.Add(new RawNode($"return {GetReg(regs, ins.A)}"));
                    break;
            }
        }
        return new FunctionNode(name, new Block(statements), isAnon);
    }

    private string FormatClosure(FunctionNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine("function(...)");
        foreach (var s in node.Body.Statements) 
            sb.AppendLine("    " + FormatNode(s, ""));
        sb.Append("end");
        return sb.ToString();
    }

    private string FormatNode(AstNode node, string indent)
    {
        if (node is CallNode c) return $"{indent}{c.Func}({string.Join(", ", c.Args)})";
        if (node is AssignNode a) return $"{indent}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right}";
        if (node is RawNode r) return $"{indent}{r.Code}";
        return "";
    }

    private void PrintBlock(Block block)
    {
        foreach (var node in block.Statements)
        {
            if (node is RawNode r && r.Code == "end") _indent--;
            _builder.AppendLine(FormatNode(node, new string(' ', _indent * 4)));
            if (node is RawNode r2 && (r2.Code.EndsWith("then") || r2.Code.EndsWith("do"))) _indent++;
        }
    }

    private string GetReg(Dictionary<int, string> regs, int r) => regs.TryGetValue(r, out var v) ? v : $"v{r}";
    private string RK(int val, Function f) => val >= 256 ? FormatConst(f, val - 256) : GetReg(new Dictionary<int, string>(), val);
    private string FormatConst(Function f, int i) => f.Constants[i] is StringConstant s ? $"\"{s.Value}\"" : f.Constants[i].ToString();
}
