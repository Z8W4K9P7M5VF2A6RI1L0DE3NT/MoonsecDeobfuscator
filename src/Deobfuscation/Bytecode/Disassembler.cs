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
        _builder.AppendLine("-- Reconstructed High-Level Script");
        var main = BuildFunctionNode(rootFunction, "Main", false);
        PrintBlock(main.Body);
        return _builder.ToString();
    }

    private FunctionNode BuildFunctionNode(Function function, string name, bool isAnon)
    {
        var statements = new List<AstNode>();
        var regs = new Dictionary<int, string>();
        var insts = function.Instructions;
        
        // Block tracking to close 'if' and 'while'
        var endMarkers = new Dictionary<int, string>();

        for (int i = 0; i < insts.Count; i++)
        {
            if (endMarkers.ContainsKey(i)) 
                statements.Add(new RawNode(endMarkers[i]));

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
                    // Roblox specific: game.GetService -> game:GetService
                    regs[ins.A] = (tbl == "game") ? $"game:GetService(\"{key}\")" : $"{tbl}.{key}";
                    break;

                case OpCode.SetTable:
                    statements.Add(new AssignNode($"{GetReg(regs, ins.A)}.{RK(ins.B, function).Replace("\"", "")}", RK(ins.C, function), false));
                    break;

                case OpCode.Call:
                    string fName = GetReg(regs, ins.A);
                    var args = Enumerable.Range(ins.A + 1, Math.Max(0, ins.B - 1))
                                         .Select(r => GetReg(regs, r)).ToList();
                    
                    // Identify event connections (e.g., .Changed:Connect)
                    if (args.Any(a => a.StartsWith("function")))
                        statements.Add(new CallNode($"{fName}:Connect", args));
                    else
                        statements.Add(new CallNode(fName, args));
                    break;

                case OpCode.Closure:
                    var childNode = BuildFunctionNode(function.Functions[ins.B], "", true);
                    regs[ins.A] = FormatClosure(childNode);
                    break;

                case OpCode.Eq:
                case OpCode.Lt:
                case OpCode.Le:
                    // RECOVERY: Lua 5.1 conditional jumps always follow an Eq/Lt/Le
                    var jmp = insts[i + 1];
                    string cond = $"{RK(ins.B, function)} {GetOp(ins.OpCode)} {RK(ins.C, function)}";
                    if (ins.A == 1) cond = $"not ({cond})";
                    
                    if (jmp.B < 0) { // Jumping backwards = while loop
                        statements.Add(new RawNode($"while {cond} do"));
                    } else { // Jumping forwards = if statement
                        statements.Add(new RawNode($"if {cond} then"));
                        endMarkers[i + jmp.B + 2] = "end";
                    }
                    i++; // Skip the JMP instruction we just processed
                    break;
            }
        }
        return new FunctionNode(name, new Block(statements), isAnon);
    }

    private string GetOp(OpCode op) => op switch { OpCode.Eq => "==", OpCode.Lt => "<", OpCode.Le => "<=", _ => "==" };

    private string FormatClosure(FunctionNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine("function(...)");
        // We simulate a print pass to get the body string
        int oldIndent = _indent; _indent = 1;
        foreach (var s in node.Body.Statements) sb.AppendLine(FormatNode(s, "    "));
        _indent = oldIndent;
        sb.Append("end");
        return sb.ToString();
    }

    private string FormatNode(AstNode node, string indent) {
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
    private string RK(int val, Function f) => val >= 256 ? FormatConst(f, val - 256) : $"v{val}";
    private string FormatConst(Function f, int i) => f.Constants[i] is StringConstant s ? $"\"{s.Value}\"" : f.Constants[i].ToString();
}

public record RawNode(string Code) : AstNode;
