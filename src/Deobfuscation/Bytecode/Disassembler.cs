using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using MoonsecDeobfuscator.Bytecode.Models; 

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

public class Disassembler(Function rootFunction)
{
    // Changed to 1 so registers start at v1
    private const int BASE_REGISTER_OFFSET = 1; 
    private readonly StringBuilder _builder = new();
    private int _indent = 0;

    public string Disassemble()
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Build AST
        var ast = BuildFunctionNode(rootFunction, isAnonymous: false);
        
        stopwatch.Stop();

        // Watermark Header
        _builder.AppendLine($"-- MoonSecV3 File Decompiled By Enchanted hub.  Time taken to decompile : {stopwatch.Elapsed.TotalMilliseconds:F4} ms");
        _builder.AppendLine();

        // Print Structured Lua
        PrintFunctionNode(ast);

        // Entry Point Execution
        if (!ast.IsAnonymous && !string.IsNullOrEmpty(ast.Name))
        {
            _builder.AppendLine($"\n{ast.Name}()");
        }

        return _builder.ToString();
    }

    private FunctionNode BuildFunctionNode(Function function, bool isAnonymous)
    {
        var locals = new HashSet<int>();
        var usedRegs = new HashSet<int>();
        var statements = new List<AstNode>();
        var declaredRegisters = new HashSet<int>();
        var upvalueNames = new Dictionary<int, string>();

        // Logic to determine if we use v or v_u_
        string Reg(int r) {
            usedRegs.Add(r);
            int displayR = r + BASE_REGISTER_OFFSET;
            // Simple logic: if it's a higher register or flagged, use v_u_
            return (r < 2) ? $"v{displayR}" : $"v_u_{displayR}";
        }

        string Declare(int r) {
            locals.Add(r);
            int displayR = r + BASE_REGISTER_OFFSET;
            return (r < 2) ? $"v{displayR}" : $"v_u_{displayR}";
        }

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
            bool isFirst = !declaredRegisters.Contains(ins.A);
            string target = Declare(ins.A);

            switch (ins.OpCode)
            {
                case OpCode.Move:
                    statements.Add(new AssignNode(target, Reg(ins.B), isFirst));
                    break;

                case OpCode.LoadK:
                    statements.Add(new AssignNode(target, Const(ins.B), isFirst));
                    break;

                case OpCode.GetGlobal:
                    string gName = ((StringConstant)function.Constants[ins.B]).Value;
                    statements.Add(new AssignNode(target, $"game:GetService(\"{gName}\")", false));
                    break;

                case OpCode.GetUpval:
                    string upName = $"upvalue_{ins.B}";
                    upvalueNames[ins.B] = upName;
                    statements.Add(new AssignNode(target, upName, isFirst));
                    break;

                case OpCode.SetUpval:
                    statements.Add(new AssignNode($"upvalue_{ins.B}", Reg(ins.A), false));
                    break;

                case OpCode.GetTable:
                    statements.Add(new AssignNode(target, $"{Reg(ins.B)}.{RK(ins.C, function).Replace("\"", "")}", isFirst));
                    break;

                case OpCode.Call:
                    var args = Enumerable.Range(ins.A + 1, ins.B - 1).Select(Reg).ToList();
                    statements.Add(new CallNode(Reg(ins.A), args));
                    break;

                case OpCode.Closure:
                    var childFunc = BuildFunctionNode(function.Functions[ins.B], true);
                    statements.Add(new AssignNode(target, "function()", isFirst));
                    statements.Add(childFunc);
                    break;

                case OpCode.Return:
                    statements.Add(new ReturnNode(Enumerable.Range(ins.A, ins.B - 1).Select(Reg).ToList()));
                    break;
            }
            declaredRegisters.Add(ins.A);
        }

        var allUpvalues = usedRegs.Except(locals).Select(Reg).Concat(upvalueNames.Values).Distinct().ToList();
        var fnName = isAnonymous ? "" : $"v{function.FunctionIndex + BASE_REGISTER_OFFSET}";
        return new FunctionNode(fnName, new Block(statements), allUpvalues, isAnonymous);
    }

    private void PrintFunctionNode(FunctionNode fn)
    {
        string indentStr = new string('\t', _indent);
        if (fn.IsAnonymous) _builder.AppendLine($"{indentStr}function()");
        else _builder.AppendLine($"{indentStr}local function {fn.Name}()");

        _indent++;
        string inner = new string('\t', _indent);

        if (fn.Upvalues.Count > 0) 
            _builder.AppendLine($"{inner}-- upvalues: {string.Join(", ", fn.Upvalues.Select(u => $"(ref) {u}"))}");

        foreach (var node in fn.Body.Statements) PrintAstNode(node);

        _indent--;
        _builder.AppendLine($"{new string('\t', _indent)}end");
    }

    private void PrintAstNode(AstNode node) 
    {
        string indent = new string('\t', _indent);
        switch (node)
        {
            case AssignNode a: _builder.AppendLine($"{indent}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right}"); break;
            case CallNode c: _builder.AppendLine($"{indent}{c.Func}({string.Join(", ", c.Args)})"); break;
            case ReturnNode r: _builder.AppendLine($"{indent}return {string.Join(", ", r.Values)}"); break;
            case FunctionNode f: PrintFunctionNode(f); break;
        }
    }

    private string RK(int val, Function f) => val >= 256 ? FormatConst(f, val - 256) : GetRegName(val);
    private string GetRegName(int r) => (r < 2) ? $"v{r + BASE_REGISTER_OFFSET}" : $"v_u_{r + BASE_REGISTER_OFFSET}";
    private string FormatConst(Function f, int i) => f.Constants[i] is StringConstant s ? $"\"{s.Value}\"" : f.Constants[i].ToString();
}

/* AST MODELS */
public abstract record AstNode;
public record Block(List<AstNode> Statements) : AstNode;
public record AssignNode(string Left, string Right, bool IsLocal) : AstNode;
public record CallNode(string Func, List<string> Args) : AstNode;
public record ReturnNode(List<string> Values) : AstNode;
public record FunctionNode(string Name, Block Body, List<string> Upvalues, bool IsAnonymous) : AstNode;
