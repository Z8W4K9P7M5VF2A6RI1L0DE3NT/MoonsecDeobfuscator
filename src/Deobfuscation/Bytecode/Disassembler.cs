#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        _builder.AppendLine("-- Decompiled with High-Level Flow Recovery");
        var main = BuildFunctionNode(rootFunction, "Main", false);
        PrintBlock(main.Body);
        return _builder.ToString();
    }

    private FunctionNode BuildFunctionNode(Function function, string name, bool isAnon)
    {
        var statements = new List<AstNode>();
        var regs = new Dictionary<int, string>();
        var insts = function.Instructions;

        for (int i = 0; i < insts.Count; i++)
        {
            var ins = insts[i];

            // 1. Check for Loops (Jumping backwards)
            if (ins.OpCode == OpCode.Jmp && ins.B < 0)
            {
                statements.Add(new RawNode("end -- loop end"));
                continue;
            }

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
                    regs[ins.A] = (tbl == "game") ? $"game:GetService(\"{key}\")" : $"{tbl}.{key}";
                    break;

                case OpCode.Call:
                    string fName = GetReg(regs, ins.A);
                    var args = Enumerable.Range(ins.A + 1, Math.Max(0, ins.B - 1))
                                         .Select(r => GetReg(regs, r)).ToList();
                    
                    // Detect if this call is actually an event connection or UI method
                    if (args.Any(a => a.Contains("function")))
                        statements.Add(new CallNode($"{fName}:Connect", args));
                    else
                        statements.Add(new CallNode(fName, args));
                    break;

                case OpCode.Closure:
                    var childFunc = function.Functions[ins.B];
                    var childNode = BuildFunctionNode(childFunc, "", true);
                    regs[ins.A] = FormatAnonymousFunc(childNode);
                    break;

                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Le:
                    // Pattern for "if condition then"
                    string cond = $"{RK(ins.B, function)} {(ins.OpCode == OpCode.Eq ? "==" : "<=")} {RK(ins.C, function)}";
                    statements.Add(new RawNode($"if {(ins.A == 0 ? cond : "not " + cond)} then"));
                    break;
            }
        }

        return new FunctionNode(name, new Block(statements), isAnon);
    }

    private string FormatAnonymousFunc(FunctionNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine("function(...)");
        foreach (var stat in node.Body.Statements)
        {
            if (stat is CallNode c) sb.AppendLine($"    {c.Func}({string.Join(", ", c.Args)})");
            if (stat is RawNode r) sb.AppendLine($"    {r.Code}");
        }
        sb.Append("end");
        return sb.ToString();
    }

    private void PrintBlock(Block block)
    {
        string indentStr = new string(' ', _indent * 4);
        foreach (var node in block.Statements)
        {
            if (node is CallNode c) 
                _builder.AppendLine($"{indentStr}{c.Func}({string.Join(", ", c.Args)})");
            else if (node is RawNode r)
            {
                if (r.Code.StartsWith("end")) _indent--;
                _builder.AppendLine($"{new string(' ', _indent * 4)}{r.Code}");
                if (r.Code.EndsWith("then") || r.Code.Contains("do")) _indent++;
            }
        }
    }

    private string GetReg(Dictionary<int, string> regs, int r) => regs.TryGetValue(r, out var v) ? v : $"v{r}";
    private string RK(int val, Function f) => val >= 256 ? FormatConst(f, val - 256) : $"v{val}";
    private string FormatConst(Function f, int i) => f.Constants[i] is StringConstant s ? $"\"{s.Value}\"" : f.Constants[i].ToString();
}
