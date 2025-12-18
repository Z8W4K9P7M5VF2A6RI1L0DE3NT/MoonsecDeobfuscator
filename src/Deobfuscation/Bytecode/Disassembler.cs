using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using MoonsecDeobfuscator.Bytecode.Models; 

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode;

public class Disassembler(Function rootFunction)
{
    private const int BASE_REGISTER_OFFSET = 18;
    private readonly StringBuilder _builder = new();
    private int _indent = 0;

    public string Disassemble()
    {
        var stopwatch = Stopwatch.StartNew();
        
        // 1. Generate AST
        var ast = BuildFunction(rootFunction, isAnonymous: false);
        
        // 2. Add Watermark and Timing
        stopwatch.Stop();
        _builder.AppendLine($"-- MoonSecV3 File Decompiled By Enchanted hub.  Time taken to decompile : {stopwatch.Elapsed.TotalMilliseconds:F4} ms");
        _builder.AppendLine();

        // 3. Print AST to Lua
        PrintFunction(ast);

        // 4. Entry Point Execution
        if (!ast.IsAnonymous && !string.IsNullOrEmpty(ast.Name))
        {
            _builder.AppendLine($"\n{ast.Name}()");
        }

        return _builder.ToString();
    }

    private FunctionNode BuildFunction(Function function, bool isAnonymous)
    {
        var locals = new HashSet<int>();
        var usedRegs = new HashSet<int>();
        var statements = new List<AstNode>();
        var declaredRegisters = new HashSet<int>();
        var upvalueNames = new Dictionary<int, string>();

        string Reg(int r) {
            usedRegs.Add(r);
            int displayR = r + BASE_REGISTER_OFFSET; 
            return (r < 4) ? $"v{displayR}" : $"v_u_{displayR}";
        }

        string Declare(int r) {
            locals.Add(r);
            int displayR = r + BASE_REGISTER_OFFSET;
            return (r < 4) ? $"v{displayR}" : $"v_u_{displayR}";
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

        // --- OPCODE DISPATCHER ---
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
                    statements.Add(new AssignNode(target, Const(ins.B), isFirst && target.Contains("_u_")));
                    break;

                case OpCode.GetGlobal:
                    string gName = ((StringConstant)function.Constants[ins.B]).Value;
                    statements.Add(new AssignNode(target, gName, false));
                    break;

                case OpCode.GetUpval:
                    string upName = $"upvalue_{ins.B}";
                    upvalueNames[ins.B] = upName;
                    statements.Add(new AssignNode(target, upName, isFirst));
                    break;

                case OpCode.SetUpval:
                    statements.Add(new AssignNode($"upvalue_{ins.B}", Reg(ins.A), false));
                    break;

                case OpCode.NewTable:
                    statements.Add(new AssignNode(target, "{}", isFirst));
                    break;

                case OpCode.GetTable:
                    statements.Add(new AssignNode(target, $"{Reg(ins.B)}[{Reg(ins.C)}]", isFirst));
                    break;

                case OpCode.SetTable:
                    statements.Add(new AssignNode($"{Reg(ins.A)}[{Reg(ins.B)}]", Reg(ins.C), false));
                    break;

                case OpCode.Call:
                    var args = Enumerable.Range(ins.A + 1, ins.B - 1).Select(Reg).ToList();
                    statements.Add(new CallNode(Reg(ins.A), args));
                    break;

                case OpCode.Closure:
                    // Supports recursive nested functions (Protos)
                    var childFunc = BuildFunction(function.Protos[ins.BX], true);
                    statements.Add(new AssignNode(target, "closure_proto", isFirst));
                    statements.Add(childFunc);
                    break;

                case OpCode.Return:
                    var rets = Enumerable.Range(ins.A, ins.B - 1).Select(Reg).ToList();
                    statements.Add(new ReturnNode(rets));
                    break;
                
                case OpCode.Jmp:
                    statements.Add(new CommentNode($"-- Jump to {i + ins.SBX + 1}"));
                    break;
            }
            declaredRegisters.Add(ins.A);
        }

        // --- UPVALUE RESOLUTION ---
        var upRefs = usedRegs.Except(locals).Select(Reg).ToList();
        upRefs.AddRange(upvalueNames.Values);
        
        var fnName = isAnonymous ? "" : $"v_u_{function.FunctionIndex + BASE_REGISTER_OFFSET}";
        return new FunctionNode(fnName, new Block(statements), upRefs.Distinct().ToList(), isAnonymous);
    }

    // --- LUA PRINTING LOGIC ---
    private void PrintFunction(FunctionNode fn)
    {
        string indent = new string('\t', _indent);
        if (fn.IsAnonymous) _builder.AppendLine($"{indent}function()");
        else _builder.AppendLine($"{indent}local function {fn.Name}()");

        _indent++;
        string inner = new string('\t', _indent);

        // Print Complex Upvalues Header
        if (fn.Upvalues.Count > 0) 
            _builder.AppendLine($"{inner}-- upvalues: {string.Join(", ", fn.Upvalues.Select(u => $"(ref) {u}"))}");

        foreach (var node in fn.Body.Statements) PrintNode(node);

        _indent--;
        _builder.AppendLine($"{new string('\t', _indent)}end");
    }

    private void PrintNode(AstNode node) 
    {
        string indent = new string('\t', _indent);
        switch (node)
        {
            case AssignNode a: 
                _builder.AppendLine($"{indent}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right}"); 
                break;
            case CallNode c: 
                _builder.AppendLine($"{indent}{c.Func}({string.Join(", ", c.Args)})"); 
                break;
            case ReturnNode r: 
                _builder.AppendLine($"{indent}return {string.Join(", ", r.Values)}"); 
                break;
            case CommentNode cm: 
                _builder.AppendLine($"{indent}{cm.Text}"); 
                break;
            case FunctionNode f: 
                PrintFunction(f); 
                break;
        }
    }
}

/* --- AST DATA MODELS --- */
public abstract record AstNode;
public record Block(List<AstNode> Statements) : AstNode;
public record AssignNode(string Left, string Right, bool IsLocal) : AstNode;
public record CallNode(string Func, List<string> Args) : AstNode;
public record ReturnNode(List<string> Values) : AstNode;
public record CommentNode(string Text) : AstNode;
public record FunctionNode(string Name, Block Body, List<string> Upvalues, bool IsAnonymous) : AstNode;

