using OrkLang2027.Ast;
using OrkLang2027.Lexing;

namespace OrkLang2027.Typing
{
    internal sealed class TypeException : Exception
    {
        public IReadOnlyList<string> Errors { get; }
        public TypeException(IReadOnlyList<string> errors) : base(string.Join(Environment.NewLine, errors)) => Errors = errors;
    }

    /// <summary>
    /// Static type checker run between parsing and compiling. Every variable, parameter and
    /// return value is annotated, so types are checked in a single pass with no inference
    /// beyond expressions. All errors are collected and reported together.
    ///
    /// Scoping mirrors the compiler: function bodies see only their own params/locals and globals.
    /// Top-level functions and variables are hoisted so function bodies may reference globals
    /// declared later, while top-level code must declare a global before using it.
    /// </summary>
    internal sealed class TypeChecker
    {
        private sealed class CheckError : Exception
        {
            public CheckError(string message) : base(message) { }
        }

        private readonly Dictionary<string, OrkType> _globals = new();
        private readonly HashSet<string> _definedGlobals = new();
        private readonly List<string> _errors = new();
        private List<Dictionary<string, OrkType>> _scopes = new();
        private OrkType? _currentReturn;

        public static void Check(List<Stmt> statements)
        {
            var checker = new TypeChecker();
            checker.Hoist(statements);
            foreach (var stmt in statements)
            {
                checker.CheckStmt(stmt);
            }
            if (checker._errors.Count > 0)
            {
                throw new TypeException(checker._errors);
            }
        }

        private void Hoist(List<Stmt> statements)
        {
            foreach (var stmt in statements)
            {
                (Token? name, OrkType? type) = stmt switch
                {
                    Stmt.VarDecl v => (v.Name, v.Type),
                    Stmt.FunctionDecl f => (f.Name, f.FunctionType),
                    _ => ((Token?)null, (OrkType?)null),
                };
                if (name == null || type == null) continue;

                if (!_globals.TryAdd(name.Lexeme, type))
                {
                    _errors.Add(Format(name.Line, $"'{name.Lexeme}' is already declared."));
                }
            }
        }

        private void CheckStmt(Stmt stmt)
        {
            try
            {
                CheckStmtCore(stmt);
            }
            catch (CheckError ex)
            {
                _errors.Add(ex.Message);
            }
        }

        private void CheckStmtCore(Stmt stmt)
        {
            switch (stmt)
            {
                case Stmt.Expression exprStmt:
                    Infer(exprStmt.Expr);
                    break;

                case Stmt.Print printStmt:
                    Infer(printStmt.Expr);
                    break;

                case Stmt.VarDecl varDecl:
                    try
                    {
                        OrkType init = Infer(varDecl.Initializer);
                        if (!varDecl.Type.IsAssignableFrom(init))
                        {
                            throw Error(varDecl.Name.Line, $"Cannot initialize '{varDecl.Name.Lexeme}' of type {varDecl.Type} with a value of type {init}.");
                        }
                    }
                    finally
                    {
                        Declare(varDecl.Name, varDecl.Type);
                    }
                    break;

                case Stmt.Block block:
                    _scopes.Add(new Dictionary<string, OrkType>());
                    try
                    {
                        foreach (var s in block.Statements) CheckStmt(s);
                    }
                    finally
                    {
                        _scopes.RemoveAt(_scopes.Count - 1);
                    }
                    break;

                case Stmt.If ifStmt:
                    RequireBool(ifStmt.Condition, "'if' condition");
                    CheckStmt(ifStmt.Then);
                    if (ifStmt.Else != null) CheckStmt(ifStmt.Else);
                    break;

                case Stmt.While whileStmt:
                    RequireBool(whileStmt.Condition, "Loop condition");
                    CheckStmt(whileStmt.Body);
                    break;

                case Stmt.FunctionDecl fnDecl:
                    CheckFunction(fnDecl);
                    break;

                case Stmt.Return returnStmt:
                {
                    int line = returnStmt.Keyword.Line;
                    if (_currentReturn == null)
                    {
                        throw Error(line, "Cannot return from top-level code.");
                    }
                    OrkType actual = returnStmt.Value == null ? OrkType.Nil : Infer(returnStmt.Value);
                    if (!_currentReturn.IsAssignableFrom(actual))
                    {
                        throw Error(line, $"Expected return value of type {_currentReturn} but got {actual}.");
                    }
                    break;
                }

                default:
                    throw Error(0, $"Unhandled statement type {stmt.GetType().Name}");
            }
        }

        private void CheckFunction(Stmt.FunctionDecl fnDecl)
        {
            Declare(fnDecl.Name, fnDecl.FunctionType);

            var savedScopes = _scopes;
            var savedReturn = _currentReturn;
            var parameters = new Dictionary<string, OrkType>();
            for (int i = 0; i < fnDecl.Parameters.Count; i++)
            {
                Token param = fnDecl.Parameters[i];
                if (!parameters.TryAdd(param.Lexeme, fnDecl.ParameterTypes[i]))
                {
                    _errors.Add(Format(param.Line, $"Duplicate parameter '{param.Lexeme}'."));
                }
            }

            _scopes = new List<Dictionary<string, OrkType>> { parameters };
            _currentReturn = fnDecl.ReturnType;
            try
            {
                foreach (var bodyStmt in fnDecl.Body) CheckStmt(bodyStmt);

                if (fnDecl.ReturnType.Kind != TypeKind.Nil && !AlwaysReturns(fnDecl.Body))
                {
                    _errors.Add(Format(fnDecl.Name.Line, $"Function '{fnDecl.Name.Lexeme}' must return a value of type {fnDecl.ReturnType} on all paths."));
                }
            }
            finally
            {
                _scopes = savedScopes;
                _currentReturn = savedReturn;
            }
        }

        private static bool AlwaysReturns(List<Stmt> statements) => statements.Any(AlwaysReturns);

        private static bool AlwaysReturns(Stmt stmt) => stmt switch
        {
            Stmt.Return => true,
            Stmt.Block block => AlwaysReturns(block.Statements),
            Stmt.If ifStmt => ifStmt.Else != null && AlwaysReturns(ifStmt.Then) && AlwaysReturns(ifStmt.Else),
            _ => false,
        };

        private void Declare(Token name, OrkType type)
        {
            if (_scopes.Count == 0)
            {
                _definedGlobals.Add(name.Lexeme);
                return;
            }

            if (!_scopes[^1].TryAdd(name.Lexeme, type))
            {
                throw Error(name.Line, $"'{name.Lexeme}' is already declared in this scope.");
            }
        }

        private OrkType Lookup(Token name)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(name.Lexeme, out var local)) return local;
            }

            bool inFunction = _currentReturn != null;
            if (_globals.TryGetValue(name.Lexeme, out var global) && (inFunction || _definedGlobals.Contains(name.Lexeme)))
            {
                return global;
            }

            throw Error(name.Line, $"Undefined variable '{name.Lexeme}'.");
        }

        private void RequireBool(Expr expr, string what)
        {
            OrkType type = Infer(expr);
            if (type.Kind != TypeKind.Bool)
            {
                throw Error(LineOf(expr), $"{what} must be bool, got {type}.");
            }
        }

        private OrkType Infer(Expr expr)
        {
            OrkType type = InferCore(expr);
            expr.Type = type;
            return type;
        }

        private OrkType InferCore(Expr expr)
        {
            switch (expr)
            {
                case Expr.Literal literal:
                    return literal.Value switch
                    {
                        null => OrkType.Nil,
                        bool => OrkType.Bool,
                        int => OrkType.Int,
                        long => OrkType.Long,
                        float => OrkType.Float,
                        double => OrkType.Double,
                        string => OrkType.String,
                        _ => throw Error(0, $"Unsupported literal {literal.Value}"),
                    };

                case Expr.Grouping grouping:
                    return Infer(grouping.Expression);

                case Expr.Unary unary:
                {
                    OrkType right = Infer(unary.Right);
                    if (unary.Op.Type == TokenType.Minus)
                    {
                        if (!right.IsNumeric)
                        {
                            throw Error(unary.Op.Line, $"Operator '-' requires a numeric operand, got {right}.");
                        }
                        return right;
                    }
                    if (right.Kind != TypeKind.Bool)
                    {
                        throw Error(unary.Op.Line, $"Operator '{unary.Op.Lexeme}' requires bool, got {right}.");
                    }
                    return OrkType.Bool;
                }

                case Expr.Binary binary:
                    return InferBinary(binary);

                case Expr.Logical logical:
                {
                    OrkType left = Infer(logical.Left);
                    OrkType right = Infer(logical.Right);
                    if (left.Kind != TypeKind.Bool || right.Kind != TypeKind.Bool)
                    {
                        throw Error(logical.Op.Line, $"Operator '{logical.Op.Lexeme}' requires bool operands, got {left} and {right}.");
                    }
                    return OrkType.Bool;
                }

                case Expr.Variable variable:
                    return Lookup(variable.Name);

                case Expr.Assign assign:
                {
                    OrkType target = Lookup(assign.Name);
                    if (target.Kind == TypeKind.Function)
                    {
                        throw Error(assign.Name.Line, $"Cannot assign to function '{assign.Name.Lexeme}'.");
                    }
                    OrkType value = Infer(assign.Value);
                    if (!target.IsAssignableFrom(value))
                    {
                        throw Error(assign.Name.Line, $"Cannot assign a value of type {value} to '{assign.Name.Lexeme}' of type {target}.");
                    }
                    assign.CheckedType = target;
                    return target;
                }

                case Expr.Call call:
                {
                    OrkType callee = Infer(call.Callee);
                    if (callee.Kind != TypeKind.Function)
                    {
                        throw Error(call.Paren.Line, $"Can only call functions, got {callee}.");
                    }
                    if (callee.Parameters.Count != call.Arguments.Count)
                    {
                        throw Error(call.Paren.Line, $"Expected {callee.Parameters.Count} arguments but got {call.Arguments.Count}.");
                    }
                    for (int i = 0; i < call.Arguments.Count; i++)
                    {
                        OrkType arg = Infer(call.Arguments[i]);
                        if (!callee.Parameters[i].IsAssignableFrom(arg))
                        {
                            throw Error(call.Paren.Line, $"Argument {i + 1} expects {callee.Parameters[i]} but got {arg}.");
                        }
                    }
                    return callee.Return!;
                }

                case Expr.ArrayLiteral arrayLiteral:
                {
                    if (arrayLiteral.Elements.Count == 0) return OrkType.EmptyArray;
                    OrkType element = Infer(arrayLiteral.Elements[0]);
                    for (int i = 1; i < arrayLiteral.Elements.Count; i++)
                    {
                        OrkType next = Infer(arrayLiteral.Elements[i]);
                        if (element.IsNumeric && next.IsNumeric) { element = OrkType.Widest(element, next); continue; }
                        if (element.Matches(next)) continue;
                        if (next.Matches(element)) { element = next; continue; }
                        throw Error(arrayLiteral.Bracket.Line, $"Array elements must all have the same type; expected {element} but got {next}.");
                    }
                    return OrkType.ArrayOf(element);
                }

                case Expr.IndexGet indexGet:
                    return InferElement(indexGet.Target, indexGet.Index, indexGet.Bracket);

                case Expr.IndexSet indexSet:
                {
                    OrkType element = InferElement(indexSet.Target, indexSet.Index, indexSet.Bracket);
                    OrkType value = Infer(indexSet.Value);
                    if (!element.IsAssignableFrom(value))
                    {
                        throw Error(indexSet.Bracket.Line, $"Cannot store a value of type {value} in an array of {element}.");
                    }
                    indexSet.CheckedType = element;
                    return element;
                }

                case Expr.Get get:
                {
                    OrkType target = Infer(get.Target);
                    if (get.Name.Lexeme != "length")
                    {
                        throw Error(get.Name.Line, $"Unknown property '.{get.Name.Lexeme}'.");
                    }
                    if (target.Kind != TypeKind.Array && target.Kind != TypeKind.String)
                    {
                        throw Error(get.Name.Line, $"Only arrays and strings have a '.length', got {target}.");
                    }
                    return OrkType.Int;
                }

                default:
                    throw Error(0, $"Unhandled expression type {expr.GetType().Name}");
            }
        }

        private OrkType InferElement(Expr targetExpr, Expr indexExpr, Token bracket)
        {
            OrkType target = Infer(targetExpr);
            if (target.Kind != TypeKind.Array)
            {
                throw Error(bracket.Line, $"Only arrays can be indexed, got {target}.");
            }
            OrkType index = Infer(indexExpr);
            if (!index.IsInteger)
            {
                throw Error(bracket.Line, $"Array index must be int or long, got {index}.");
            }
            if (target.Element == null)
            {
                throw Error(bracket.Line, "Cannot index an empty array literal.");
            }
            return target.Element;
        }

        private OrkType InferBinary(Expr.Binary binary)
        {
            OrkType left = Infer(binary.Left);
            OrkType right = Infer(binary.Right);
            int line = binary.Op.Line;
            string op = binary.Op.Lexeme;

            switch (binary.Op.Type)
            {
                case TokenType.Plus:
                    if (left.IsNumeric && right.IsNumeric) return OrkType.Widest(left, right);
                    if (left.Kind == TypeKind.String || right.Kind == TypeKind.String) return OrkType.String;
                    throw Error(line, $"Operator '+' requires two numeric operands or a string operand, got {left} and {right}.");

                case TokenType.Minus:
                case TokenType.Star:
                case TokenType.Slash:
                case TokenType.Percent:
                    RequireNumbers(left, right, op, line);
                    return OrkType.Widest(left, right);

                case TokenType.Greater:
                case TokenType.GreaterEqual:
                case TokenType.Less:
                case TokenType.LessEqual:
                    RequireNumbers(left, right, op, line);
                    return OrkType.Bool;

                case TokenType.EqualEqual:
                case TokenType.BangEqual:
                    if (!(left.IsNumeric && right.IsNumeric) && !left.Matches(right) && !right.Matches(left))
                    {
                        throw Error(line, $"Cannot compare {left} with {right} using '{op}'.");
                    }
                    return OrkType.Bool;

                default:
                    throw Error(line, $"Unknown binary operator '{op}'.");
            }
        }

        private static void RequireNumbers(OrkType left, OrkType right, string op, int line)
        {
            if (!left.IsNumeric || !right.IsNumeric)
            {
                throw Error(line, $"Operator '{op}' requires numeric operands, got {left} and {right}.");
            }
        }

        private static int LineOf(Expr expr) => expr switch
        {
            Expr.Variable v => v.Name.Line,
            Expr.Assign a => a.Name.Line,
            Expr.Unary u => u.Op.Line,
            Expr.Binary b => b.Op.Line,
            Expr.Logical l => l.Op.Line,
            Expr.Grouping g => LineOf(g.Expression),
            Expr.Call c => c.Paren.Line,
            Expr.ArrayLiteral a => a.Bracket.Line,
            Expr.IndexGet i => i.Bracket.Line,
            Expr.IndexSet i => i.Bracket.Line,
            Expr.Get g => g.Name.Line,
            _ => 0,
        };

        private static string Format(int line, string message) =>
            line > 0 ? $"[line {line}] Type error: {message}" : $"Type error: {message}";

        private static CheckError Error(int line, string message) => new(Format(line, message));
    }
}
