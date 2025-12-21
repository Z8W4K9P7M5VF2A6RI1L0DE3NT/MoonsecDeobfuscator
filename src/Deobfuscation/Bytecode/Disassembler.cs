#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MoonsecDeobfuscator.Bytecode.Models;
using Function = MoonsecDeobfuscator.Bytecode.Models.Function;

namespace MoonsecDeobfuscator.Deobfuscation.Bytecode
{
    // --- High-Level AST Nodes ---
    public abstract record AstNode;
    public record Block(List<AstNode> Statements) : AstNode;
    public record AssignNode(string Left, string Right, bool IsLocal) : AstNode;
    public record CallNode(string Func, List<string> Args) : AstNode;
    public record FunctionNode(string Name, Block Body, bool IsAnonymous) : AstNode;
    public record RawNode(string Code) : AstNode;

    // --- State Management (Ported from Lua's 't' table) ---
    public class DeobfuscatorState
    {
        public List<string> Output { get; } = new();
        public int Indent { get; set; } = 0;
        public Dictionary<object, string> Registry { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<string, object> ReverseRegistry { get; } = new();
        public HashSet<string> NamesUsed { get; } = new();
        public Dictionary<object, object> ParentMap { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<object, Dictionary<string, object>> PropertyStore { get; } = new(ReferenceEqualityComparer.Instance);
        public List<RemoteCall> CallGraph { get; } = new();
        public Dictionary<string, string> VariableTypes { get; } = new();
        public List<StringRef> StringRefs { get; } = new();
        public int ProxyId { get; set; } = 0;
        public int CallbackDepth { get; set; } = 0;
        public bool PendingIterator { get; set; } = false;
        public string LastHttpUrl { get; set; } = null;
        public string LastEmittedLine { get; set; } = null;
        public int RepetitionCount { get; set; } = 0;
        public int CurrentSize { get; set; } = 0;
        public int Ixvixv4Counter { get; set; } = 0;
        public bool LimitReached { get; set; } = false;
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

    // --- Settings (Ported from Lua's 'r' table) ---
    public class DeobfuscatorSettings
    {
        public int MaxDepth { get; set; } = 15;
        public int MaxTableItems { get; set; } = 150;
        public string OutputFile { get; set; } = "dumped_output.lua";
        public bool Verbose { get; set; } = false;
        public bool TraceCallbacks { get; set; } = true;
        public double TimeoutSeconds { get; set; } = 6.7;
        public int MaxRepeatedLines { get; set; } = 6;
        public int MinDeobfLength { get; set; } = 150;
        public int MaxOutputSize { get; set; } = 6 * 1024 * 1024;
        public bool ConstantCollection { get; set; } = true;
        public bool InstrumentLogic { get; set; } = true;
    }

    // --- Proxy Markers ---
    public static class ProxyMarkers
    {
        public static readonly object NumericProxyMarker = new();
        public static readonly object ProxyIdMarker = new();
    }

    // --- Proxy Factory (Ported from Lua proxy logic) ---
    public class ProxyFactory
    {
        private readonly DeobfuscatorState _state;
        private readonly DeobfuscatorSettings _settings;
        private readonly Dictionary<string, int> _nameCounters = new();
        private readonly Dictionary<string, string> _serviceShortcuts = new()
        {
            ["Players"] = "Players", ["UserInputService"] = "UIS", ["RunService"] = "RunService",
            ["ReplicatedStorage"] = "ReplicatedStorage", ["TweenService"] = "TweenService", 
            ["Workspace"] = "Workspace", ["Lighting"] = "Lighting", ["StarterGui"] = "StarterGui",
            ["CoreGui"] = "CoreGui", ["HttpService"] = "HttpService", 
            ["MarketplaceService"] = "MarketplaceService", ["DataStoreService"] = "DataStoreService",
            ["TeleportService"] = "TeleportService", ["SoundService"] = "SoundService", ["Chat"] = "Chat",
            ["Teams"] = "Teams", ["ProximityPromptService"] = "ProximityPromptService",
            ["ContextActionService"] = "ContextActionService", ["CollectionService"] = "CollectionService",
            ["PathfindingService"] = "PathfindingService", ["Debris"] = "Debris"
        };

        private readonly List<PatternCounter> _uiPatterns = new()
        {
            new("window", "Window", "window"), new("tab", "Tab", "tab"), new("section", "Section", "section"),
            new("button", "Button", "button"), new("toggle", "Toggle", "toggle"), new("slider", "Slider", "slider"),
            new("dropdown", "Dropdown", "dropdown"), new("textbox", "Textbox", "textbox"),
            new("input", "Input", "input"), new("label", "Label", "label"), new("keybind", "Keybind", "keybind"),
            new("colorpicker", "ColorPicker", "colorpicker"), new("paragraph", "Paragraph", "paragraph"),
            new("notification", "Notification", "notification"), new("divider", "Divider", "divider"),
            new("bind", "Bind", "bind"), new("picker", "Picker", "picker")
        };

        public ProxyFactory(DeobfuscatorState state, DeobfuscatorSettings settings)
        {
            _state = state;
            _settings = settings;
        }

        public bool IsNumericProxy(object obj) => 
            obj is Dictionary<object, object> dict && 
            dict.ContainsKey(ProxyMarkers.NumericProxyMarker);

        public bool IsProxy(object obj) => 
            obj is Dictionary<object, object> dict && 
            dict.ContainsKey(ProxyMarkers.ProxyIdMarker);

        public double GetNumericValue(object obj)
        {
            if (IsNumericProxy(obj) && obj is Dictionary<object, object> dict)
                return dict.TryGetValue("__value", out var v) && v is double d ? d : 0;
            return obj is double d ? d : 0;
        }

        public string GetProxyId(object obj)
        {
            if (IsProxy(obj) && obj is Dictionary<object, object> dict)
                return dict.TryGetValue("__proxy_id", out var id) ? id?.ToString() : null;
            return null;
        }

        public string GetVarName(object obj, string suggestedName = null, string context = null)
        {
            if (_state.Registry.TryGetValue(obj, out var name)) return name;
            
            if (!string.IsNullOrEmpty(suggestedName) && _serviceShortcuts.TryGetValue(suggestedName, out var shortcut))
                return shortcut;

            if (!string.IsNullOrEmpty(context))
            {
                var lowerContext = context.ToLower();
                foreach (var pattern in _uiPatterns)
                {
                    if (lowerContext.Contains(pattern.Pattern))
                    {
                        var count = IncrementCounter(pattern.Counter);
                        return count == 1 ? pattern.Prefix : $"{pattern.Prefix}{count}";
                    }
                }
            }

            if (suggestedName is "LocalPlayer" or "Character" or "Humanoid" or "HumanoidRootPart" or "Camera")
                return suggestedName;

            if (suggestedName?.StartsWith("Enum.") == true)
                return suggestedName;

            var sanitized = Regex.Replace(suggestedName ?? "var", @"[^\w_]", "_");
            sanitized = Regex.Replace(sanitized, @"^\d+", "_");
            if (string.IsNullOrWhiteSpace(sanitized) || sanitized is "_" or "Object" or "Value" or "result")
                sanitized = "var";

            return GetUniqueName(sanitized);
        }

        private string GetUniqueName(string baseName)
        {
            if (!_nameCounters.ContainsKey(baseName))
            {
                _nameCounters[baseName] = 1;
                return baseName;
            }
            return $"{baseName}_{++_nameCounters[baseName]}";
        }

        private int IncrementCounter(string key)
        {
            _nameCounters.TryGetValue(key, out var count);
            _nameCounters[key] = count + 1;
            return count + 1;
        }

        public Dictionary<object, object> CreateNumericProxy(double value)
        {
            var proxy = CreateBaseProxy();
            proxy[ProxyMarkers.NumericProxyMarker] = true;
            proxy["__value"] = value;
            proxy["__tostring"] = new Func<string>(() => value.ToString());
            
            SetupNumericMetatable(proxy);
            return proxy;
        }

        public Dictionary<object, object> CreateRobloxProxy(string name, object parent = null)
        {
            var proxy = CreateBaseProxy();
            var id = $"proxy_{++_state.ProxyId}";
            proxy[ProxyMarkers.ProxyIdMarker] = id;
            proxy["__proxy_id"] = id;
            
            _state.Registry[proxy] = name;
            _state.ReverseRegistry[name] = proxy;
            
            SetupRobloxMetatable(proxy, name, parent);
            return proxy;
        }

        private Dictionary<object, object> CreateBaseProxy()
        {
            var proxy = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            return proxy;
        }

        private void SetupNumericMetatable(Dictionary<object, object> proxy)
        {
            var meta = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            
            Func<string, Func<object, object, object>> binOp = op => (a, b) =>
            {
                var valA = GetNumericValue(a);
                var valB = GetNumericValue(b);
                double result = op switch
                {
                    "+" => valA + valB,
                    "-" => valA - valB,
                    "*" => valA * valB,
                    "/" => valB != 0 ? valA / valB : 0,
                    "%" => valB != 0 ? valA % valB : 0,
                    "^" => Math.Pow(valA, valB),
                    _ => 0
                };
                return CreateNumericProxy(result);
            };

            meta["__add"] = binOp("+");
            meta["__sub"] = binOp("-");
            meta["__mul"] = binOp("*");
            meta["__div"] = binOp("/");
            meta["__mod"] = binOp("%");
            meta["__pow"] = binOp("^");
            meta["__unm"] = new Func<object, object>(a => CreateNumericProxy(-GetNumericValue(a)));
            meta["__eq"] = new Func<object, object, bool>((a, b) => GetNumericValue(a) == GetNumericValue(b));
            meta["__lt"] = new Func<object, object, bool>((a, b) => GetNumericValue(a) < GetNumericValue(b));
            meta["__le"] = new Func<object, object, bool>((a, b) => GetNumericValue(a) <= GetNumericValue(b));

            proxy["__metatable"] = meta;
        }

        private void SetupRobloxMetatable(Dictionary<object, object> proxy, string name, object parent)
        {
            var meta = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            
            meta["__index"] = new Func<object, object, object>((self, key) =>
            {
                if (key is string s && s.StartsWith("__")) return proxy.GetValueOrDefault(key);
                return CreateRobloxProxy($"{name}.{key}", self);
            });

            meta["__newindex"] = new Action<object, object, object>((self, key, value) =>
            {
                var propName = key.ToString();
                _state.Output.Add($"{name}.{propName} = {FormatValue(value)}");
            });

            meta["__call"] = new Func<object, object[], object>((self, args) =>
            {
                var argsStr = string.Join(", ", args.Select(FormatValue));
                _state.Output.Add($"local {GetVarName(self, "result")} = {name}({argsStr})");
                return CreateRobloxProxy("result");
            });

            proxy["__metatable"] = meta;
        }

        private string FormatValue(object val)
        {
            if (val is string s) return $"\"{s}\"";
            if (IsNumericProxy(val)) return GetNumericValue(val).ToString();
            if (IsProxy(val)) return _state.Registry.GetValueOrDefault(val, "proxy") ?? "proxy";
            return val?.ToString() ?? "nil";
        }
    }

    public class PatternCounter
    {
        public string Pattern { get; }
        public string Prefix { get; }
        public string Counter { get; }
        public PatternCounter(string pattern, string prefix, string counter) => 
            (Pattern, Prefix, Counter) = (pattern, prefix, counter);
    }

    // --- Main Disassembler with Simulation ---
    public class Disassembler
    {
        private readonly StringBuilder _builder = new();
        private readonly DeobfuscatorState _state = new();
        private readonly DeobfuscatorSettings _settings = new();
        private readonly ProxyFactory _proxyFactory;
        private int _indent = 0;
        private readonly Function _rootFunction;

        public Disassembler(Function rootFunction)
        {
            _rootFunction = rootFunction;
            _proxyFactory = new ProxyFactory(_state, _settings);
        }

        public string Disassemble()
        {
            _builder.AppendLine("-- Decompiled with High-Level Flow Recovery");
            _builder.AppendLine("-- Target: Roblox / Moonsec VM");
            _builder.AppendLine("-- Generated using ixvixv4 simulation engine");
            _builder.AppendLine();

            ResetState();
            var main = BuildFunctionNode(_rootFunction, "Main", false);
            PrintBlock(main.Body);
            
            AppendStringRefs();
            AppendCallGraph();
            
            return _builder.ToString();
        }

        private void ResetState()
        {
            _state.Output.Clear();
            _state.Registry.Clear();
            _state.ReverseRegistry.Clear();
            _state.NamesUsed.Clear();
            _state.ParentMap.Clear();
            _state.PropertyStore.Clear();
            _state.CallGraph.Clear();
            _state.VariableTypes.Clear();
            _state.StringRefs.Clear();
            _state.ProxyId = 0;
            _state.Indent = 0;
            _state.PendingIterator = false;
            _state.LastHttpUrl = null;
            _state.LastEmittedLine = null;
            _state.RepetitionCount = 0;
            _state.CurrentSize = 0;
            _state.Ixvixv4Counter = 0;
            _state.LimitReached = false;

            // Initialize global proxies
            var game = _proxyFactory.CreateRobloxProxy("game");
            var workspace = _proxyFactory.CreateRobloxProxy("workspace");
            var script = _proxyFactory.CreateRobloxProxy("script");
            var shared = _proxyFactory.CreateRobloxProxy("shared");
            
            _state.Registry[game] = "game";
            _state.Registry[workspace] = "workspace";
            _state.Registry[script] = "script";
            _state.Registry[shared] = "shared";
        }

        private void AppendStringRefs()
        {
            if (!_state.StringRefs.Any()) return;
            
            _builder.AppendLine("\n-- [String References]");
            _builder.AppendLine("-- " + string.Join("\n-- ", _state.StringRefs.Select(r => 
                $"[{r.Hint}] {r.Value}{(r.FullLength > 0 ? $" (len: {r.FullLength})" : "")}")));
        }

        private void AppendCallGraph()
        {
            if (!_state.CallGraph.Any()) return;
            
            _builder.AppendLine("\n-- [Remote Calls]");
            foreach (var call in _state.CallGraph)
            {
                _builder.AppendLine($"-- {call.Type}: {call.Name}({string.Join(", ", call.Args)})");
            }
        }

        private FunctionNode BuildFunctionNode(Function function, string name, bool isAnon)
        {
            var statements = new List<AstNode>();
            var regs = new Dictionary<int, object>();
            var insts = function.Instructions;
            var endMarkers = new Dictionary<int, string>();
            var scopeStack = new Stack<Dictionary<string, string>>();

            scopeStack.Push(new Dictionary<string, string>());

            for (int i = 0; i < insts.Count; i++)
            {
                if (endMarkers.TryGetValue(i, out var marker))
                    statements.Add(new RawNode(marker));

                var ins = insts[i];
                var op = ins.OpCode;

                // Skip no-ops from obfuscation
                if (op == OpCode.Nop) continue;

                switch (op)
                {
                    case OpCode.GetGlobal:
                        var globalName = ((StringConstant)function.Constants[ins.B]).Value;
                        regs[ins.A] = ResolveGlobal(globalName);
                        break;

                    case OpCode.LoadK:
                        regs[ins.A] = LoadConstant(function, ins.B);
                        break;

                    case OpCode.GetTable:
                        HandleGetTable(regs, ins, function, statements);
                        break;

                    case OpCode.SetTable:
                        HandleSetTable(regs, ins, function, statements);
                        break;

                    case OpCode.Self:
                        HandleSelf(regs, ins, function);
                        break;

                    case OpCode.Call:
                        HandleCall(regs, ins, function, statements);
                        break;

                    case OpCode.Closure:
                        HandleClosure(regs, ins, function, statements);
                        break;

                    case OpCode.Eq:
                    case OpCode.Lt:
                    case OpCode.Le:
                        HandleComparison(op, regs, ins, function, insts, ref i, endMarkers, statements);
                        break;

                    case OpCode.Jmp:
                        HandleJump(ins, i, endMarkers, statements);
                        break;

                    case OpCode.Return:
                        if (ins.B > 1)
                            statements.Add(new RawNode($"return {FormatRegister(regs, ins.A)}"));
                        break;
                }
            }

            scopeStack.Pop();
            return new FunctionNode(name, new Block(statements), isAnon);
        }

        private object ResolveGlobal(string name)
        {
            // Check for UI libraries
            var uiLibs = new[] { "Rayfield", "OrionLib", "Kavo", "Venyx", "Sirius", "Linoria", "Wally" };
            if (uiLibs.Contains(name))
            {
                return _proxyFactory.CreateRobloxProxy(name);
            }

            // Standard Roblox globals
            return name switch
            {
                "game" => _proxyFactory.CreateRobloxProxy("game"),
                "workspace" => _proxyFactory.CreateRobloxProxy("workspace"),
                "script" => _proxyFactory.CreateRobloxProxy("script"),
                "shared" => _proxyFactory.CreateRobloxProxy("shared"),
                _ => _proxyFactory.CreateRobloxProxy(name)
            };
        }

        private object LoadConstant(Function f, int index)
        {
            return f.Constants[index] switch
            {
                StringConstant sc => sc.Value,
                NumberConstant nc => nc.Value,
                BoolConstant bc => bc.Value,
                NilConstant => null,
                _ => null
            };
        }

        private void HandleGetTable(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
        {
            var tbl = GetRegister(regs, ins.B);
            var key = GetRk(regs, f, ins.C)?.ToString().Trim('"');
            
            if (tbl is string tblStr && key != null)
            {
                if (tblStr == "game")
                {
                    var serviceProxy = _proxyFactory.CreateRobloxProxy(key);
                    _state.Registry[serviceProxy] = $"game:GetService(\"{key}\")";
                    regs[ins.A] = serviceProxy;
                    statements.Add(new CallNode("game:GetService", new List<string> { $"\"{key}\"" }));
                }
                else
                {
                    var proxy = _proxyFactory.CreateRobloxProxy($"{tblStr}.{key}");
                    regs[ins.A] = proxy;
                }
            }
            else
            {
                regs[ins.A] = _proxyFactory.CreateRobloxProxy($"unknown.{key}");
            }
        }

        private void HandleSetTable(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
        {
            var obj = GetRegister(regs, ins.A);
            var key = GetRk(regs, f, ins.B)?.ToString().Trim('"');
            var value = FormatValue(GetRk(regs, f, ins.C));
            
            var objName = _state.Registry.GetValueOrDefault(obj, "unknown");
            statements.Add(new AssignNode($"{objName}.{key}", value, false));
        }

        private void HandleSelf(Dictionary<int, object> regs, Instruction ins, Function f)
        {
            var obj = GetRegister(regs, ins.B);
            var method = GetRk(regs, f, ins.C)?.ToString().Trim('"');
            
            regs[ins.A + 1] = obj;
            var methodProxy = _proxyFactory.CreateRobloxProxy($"{_state.Registry.GetValueOrDefault(obj, "obj")}:{method}");
            regs[ins.A] = methodProxy;
        }

        private void HandleCall(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
        {
            var func = GetRegister(regs, ins.A);
            var args = Enumerable.Range(ins.A + 1, Math.Max(0, ins.B - 1))
                               .Select(r => GetRegister(regs, r)).ToList();
            
            var funcName = _state.Registry.GetValueOrDefault(func, "unknown");
            
            // Detect UI library patterns
            if (func is string libName && args.Count > 0 && args[0] is string methodName)
            {
                var uiLibs = new[] { "Rayfield", "OrionLib", "Kavo", "Venyx" };
                if (uiLibs.Contains(libName))
                {
                    var methodArgs = args.Skip(1).Select(FormatValue).ToList();
                    methodArgs.Insert(0, $"\"{methodName}\"");
                    statements.Add(new CallNode($"{libName}:{methodName}", methodArgs));
                    regs[ins.A] = _proxyFactory.CreateRobloxProxy($"{libName}.{methodName}");
                    return;
                }
            }

            // Remote detection
            if (funcName.Contains("FireServer"))
            {
                _state.CallGraph.Add(new RemoteCall
                {
                    Type = "RemoteEvent",
                    Name = funcName,
                    Args = args.ToList<object>()
                });
            }
            else if (funcName.Contains("InvokeServer"))
            {
                _state.CallGraph.Add(new RemoteCall
                {
                    Type = "RemoteFunction",
                    Name = funcName,
                    Args = args.ToList<object>()
                });
            }

            var argsFormatted = args.Select(FormatValue).ToList();
            statements.Add(new CallNode(funcName, argsFormatted));
            
            // Return value proxy
            if (ins.C - 1 > 0)
            {
                regs[ins.A] = _proxyFactory.CreateRobloxProxy("result");
            }
        }

        private void HandleClosure(Dictionary<int, object> regs, Instruction ins, Function f, List<AstNode> statements)
        {
            var child = BuildFunctionNode(f.Functions[ins.B], "", true);
            var closureCode = FormatClosure(child);
            regs[ins.A] = closureCode;
        }

        private void HandleComparison(OpCode op, Dictionary<int, object> regs, Instruction ins, 
            Function f, List<Instruction> insts, ref int i, Dictionary<int, string> endMarkers, List<AstNode> statements)
        {
            if (i + 1 >= insts.Count && insts[i + 1].OpCode == OpCode.Jmp)
            {
                var jmp = insts[i + 1];
                var opStr = op switch { OpCode.Lt => "<", OpCode.Le => "<=", _ => "==" };
                var lhs = FormatValue(GetRk(regs, f, ins.B));
                var rhs = FormatValue(GetRk(regs, f, ins.C));
                var cond = $"{lhs} {opStr} {rhs}";
                
                if (ins.A == 1) cond = $"not ({cond})";

                if (jmp.B < 0)
                {
                    statements.Add(new RawNode($"while {cond} do"));
                }
                else
                {
                    statements.Add(new RawNode($"if {cond} then"));
                    endMarkers[i + jmp.B + 2] = "end";
                }
                i++;
            }
        }

        private void HandleJump(Instruction ins, int currentIndex, Dictionary<int, string> endMarkers, List<AstNode> statements)
        {
            if (ins.B < 0)
            {
                // Loop back - handled in comparison
            }
            else
            {
                endMarkers[currentIndex + ins.B + 1] = "end";
            }
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
            return node switch
            {
                CallNode c => $"{indent}{c.Func}({string.Join(", ", c.Args)})",
                AssignNode a => $"{indent}{(a.IsLocal ? "local " : "")}{a.Left} = {a.Right}",
                RawNode r => $"{indent}{r.Code}",
                _ => ""
            };
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

        private object GetRegister(Dictionary<int, object> regs, int r) => 
            regs.TryGetValue(r, out var v) ? v : $"v{r}";

        private object GetRk(Dictionary<int, object> regs, Function f, int val) => 
            val >= 256 ? LoadConstant(f, val - 256) : GetRegister(regs, val);

        private string FormatValue(object val)
        {
            if (val is string s) return $"\"{s}\"";
            if (_proxyFactory.IsNumericProxy(val)) return _proxyFactory.GetNumericValue(val).ToString();
            if (_proxyFactory.IsProxy(val)) return _state.Registry.GetValueOrDefault(val, "proxy") ?? "proxy";
            return val?.ToString() ?? "nil";
        }
    }

    // --- Dictionary comparer for reference types ---
    public class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    // --- Public API (Ported from Lua's 'q' table) ---
    public class DeobfuscatorAPI
    {
        private readonly DeobfuscatorState _state = new();
        private readonly DeobfuscatorSettings _settings = new();
        
        public void Reset() => new Disassembler(null).GetType(); // Reuse reset logic
        
        public string GetOutput() => string.Join("\n", _state.Output);
        
        public bool Save(string filename = null)
        {
            try
            {
                System.IO.File.WriteAllText(filename ?? _settings.OutputFile, GetOutput());
                return true;
            }
            catch { return false; }
        }
        
        public List<RemoteCall> GetCallGraph() => _state.CallGraph;
        public List<StringRef> GetStringRefs() => _state.StringRefs;
        
        public object GetStats() => new
        {
            total_lines = _state.Output.Count,
            remote_calls = _state.CallGraph.Count,
            suspicious_strings = _state.StringRefs.Count,
            proxies_created = _state.ProxyId
        };
    }
}
