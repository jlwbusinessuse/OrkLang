using OrkLang2027.Ast;
using OrkLang2027.Bytecode;
using OrkLang2027.Lexing;
using OrkLang2027.Typing;

namespace OrkLang2027.Compiling
{
    internal sealed class CompileException : Exception
    {
        public CompileException(string message) : base(message) { }
    }

    /// <summary>
    /// Compiles an AST (produced by the Parser) into OrkLang bytecode (<see cref="Chunk"/>s
    /// wrapped in <see cref="ObjFunction"/>). Each user-defined function gets its own chunk;
    /// the top-level statements are compiled into an implicit "script" function.
    ///
    /// Functions do not close over enclosing locals (no upvalues) - they may only reference
    /// their own parameters/locals and global variables. This keeps the VM's call convention
    /// simple: a call frame is just a chunk + instruction pointer + stack base.
    /// </summary>
    internal sealed class Compiler
    {
        private sealed class Local
        {
            public string Name = string.Empty;
            public int Depth;
        }

        private readonly ObjFunction _function;
        private readonly List<Local> _locals = new();
        private int _scopeDepth;
        private OrkType? _returnType;

        private Compiler(string name, int arity)
        {
            _function = new ObjFunction(name, arity);
        }

        public static ObjFunction CompileScript(List<Stmt> statements)
        {
            var compiler = new Compiler(string.Empty, 0);
            foreach (var stmt in statements)
            {
                compiler.CompileStmt(stmt);
            }
            compiler.Emit(OpCode.Halt, 0);
            return compiler._function;
        }

        private void CompileStmt(Stmt stmt)
        {
            switch (stmt)
            {
                case Stmt.Expression exprStmt:
                    CompileExpr(exprStmt.Expr);
                    Emit(OpCode.Pop, 0);
                    break;

                case Stmt.Print printStmt:
                    CompileExpr(printStmt.Expr);
                    Emit(OpCode.Print, 0);
                    break;

                case Stmt.VarDecl varDecl:
                    CompileVarDecl(varDecl);
                    break;

                case Stmt.Block block:
                    BeginScope();
                    foreach (var s in block.Statements) CompileStmt(s);
                    EndScope();
                    break;

                case Stmt.If ifStmt:
                    CompileIf(ifStmt);
                    break;

                case Stmt.While whileStmt:
                    CompileWhile(whileStmt);
                    break;

                case Stmt.FunctionDecl fnDecl:
                    CompileFunctionDecl(fnDecl);
                    break;

                case Stmt.Return returnStmt:
                    if (returnStmt.Value != null)
                    {
                        CompileExpr(returnStmt.Value);
                        if (_returnType != null) EmitConvert(returnStmt.Value.Type, _returnType, returnStmt.Keyword.Line);
                    }
                    else
                    {
                        Emit(OpCode.Nil, returnStmt.Keyword.Line);
                    }
                    if (_returnType != null) EmitCheckType(_returnType, returnStmt.Keyword.Line);
                    Emit(OpCode.Return, returnStmt.Keyword.Line);
                    break;

                default:
                    throw new CompileException($"Unhandled statement type {stmt.GetType().Name}");
            }
        }

        private void CompileFunctionDecl(Stmt.FunctionDecl fnDecl)
        {
            var fnCompiler = new Compiler(fnDecl.Name.Lexeme, fnDecl.Parameters.Count);
            fnCompiler._scopeDepth = 1;
            fnCompiler._returnType = fnDecl.ReturnType;
            // Slot 0 in every call frame is reserved for the function value itself
            // (the callee sits below its arguments on the stack), so reserve it here.
            fnCompiler._locals.Add(new Local { Name = string.Empty, Depth = 1 });
            foreach (var param in fnDecl.Parameters)
            {
                fnCompiler._locals.Add(new Local { Name = param.Lexeme, Depth = 1 });
            }

            for (int i = 0; i < fnDecl.Parameters.Count; i++)
            {
                int line = fnDecl.Parameters[i].Line;
                fnCompiler.Emit(OpCode.GetLocal, line);
                fnCompiler.EmitByte((byte)(i + 1), line);
                fnCompiler.EmitCheckType(fnDecl.ParameterTypes[i], line);
                fnCompiler.Emit(OpCode.Pop, line);
            }

            foreach (var bodyStmt in fnDecl.Body)
            {
                fnCompiler.CompileStmt(bodyStmt);
            }
            // implicit return nil if the body falls through
            fnCompiler.Emit(OpCode.Nil, fnDecl.Name.Line);
            fnCompiler.EmitCheckType(fnDecl.ReturnType, fnDecl.Name.Line);
            fnCompiler.Emit(OpCode.Return, fnDecl.Name.Line);

            var fnValue = Value.FromFunction(fnCompiler._function);
            int constIndex = _function.Chunk.AddConstant(fnValue);
            Emit(OpCode.Constant, fnDecl.Name.Line);
            EmitByte((byte)constIndex, fnDecl.Name.Line);

            DefineVariable(fnDecl.Name);
        }

        private void CompileWhile(Stmt.While whileStmt)
        {
            int loopStart = _function.Chunk.Count;
            CompileExpr(whileStmt.Condition);
            int exitJump = EmitJump(OpCode.JumpIfFalse);
            Emit(OpCode.Pop, 0);
            CompileStmt(whileStmt.Body);
            EmitLoop(loopStart);
            PatchJump(exitJump);
            Emit(OpCode.Pop, 0);
        }

        private void CompileIf(Stmt.If ifStmt)
        {
            CompileExpr(ifStmt.Condition);
            int thenJump = EmitJump(OpCode.JumpIfFalse);
            Emit(OpCode.Pop, 0);
            CompileStmt(ifStmt.Then);

            int elseJump = EmitJump(OpCode.Jump);
            PatchJump(thenJump);
            Emit(OpCode.Pop, 0);

            if (ifStmt.Else != null)
            {
                CompileStmt(ifStmt.Else);
            }
            PatchJump(elseJump);
        }

        private void CompileVarDecl(Stmt.VarDecl varDecl)
        {
            CompileExpr(varDecl.Initializer);
            EmitConvert(varDecl.Initializer.Type, varDecl.Type, varDecl.Name.Line);
            EmitCheckType(varDecl.Type, varDecl.Name.Line);
            DefineVariable(varDecl.Name);
        }

        private void EmitConvert(OrkType? from, OrkType to, int line)
        {
            if (from == null || !from.IsNumeric || !to.IsNumeric || from.Kind == to.Kind) return;
            Emit(OpCode.Convert, line);
            EmitByte((byte)to.RuntimeKind, line);
        }

        private void EmitCheckType(OrkType type, int line)
        {
            Emit(OpCode.CheckType, line);
            EmitByte((byte)type.RuntimeKind, line);
        }

        private void DefineVariable(Token name)
        {
            if (_scopeDepth > 0)
            {
                _locals.Add(new Local { Name = name.Lexeme, Depth = _scopeDepth });
                return;
            }

            int constIndex = _function.Chunk.AddConstant(Value.FromString(name.Lexeme));
            Emit(OpCode.DefineGlobal, name.Line);
            EmitByte((byte)constIndex, name.Line);
        }

        private void BeginScope() => _scopeDepth++;

        private void EndScope()
        {
            _scopeDepth--;
            while (_locals.Count > 0 && _locals[^1].Depth > _scopeDepth)
            {
                Emit(OpCode.Pop, 0);
                _locals.RemoveAt(_locals.Count - 1);
            }
        }

        private void CompileExpr(Expr expr)
        {
            switch (expr)
            {
                case Expr.Literal literal:
                    CompileLiteral(literal);
                    break;

                case Expr.Grouping grouping:
                    CompileExpr(grouping.Expression);
                    break;

                case Expr.Unary unary:
                    CompileExpr(unary.Right);
                    Emit(unary.Op.Type == TokenType.Minus ? OpCode.Negate : OpCode.Not, unary.Op.Line);
                    break;

                case Expr.Binary binary:
                    CompileBinary(binary);
                    break;

                case Expr.Logical logical:
                    CompileLogical(logical);
                    break;

                case Expr.Variable variable:
                    CompileVariableGet(variable.Name);
                    break;

                case Expr.Assign assign:
                    CompileExpr(assign.Value);
                    if (assign.CheckedType != null)
                    {
                        EmitConvert(assign.Value.Type, assign.CheckedType, assign.Name.Line);
                        EmitCheckType(assign.CheckedType, assign.Name.Line);
                    }
                    CompileVariableSet(assign.Name);
                    break;

                case Expr.Call call:
                    CompileCall(call);
                    break;

                case Expr.ArrayLiteral arrayLiteral:
                    foreach (var element in arrayLiteral.Elements)
                    {
                        CompileExpr(element);
                        if (arrayLiteral.Type?.Element != null) EmitConvert(element.Type, arrayLiteral.Type.Element, arrayLiteral.Bracket.Line);
                    }
                    Emit(OpCode.BuildArray, arrayLiteral.Bracket.Line);
                    EmitByte((byte)arrayLiteral.Elements.Count, arrayLiteral.Bracket.Line);
                    break;

                case Expr.IndexGet indexGet:
                    CompileExpr(indexGet.Target);
                    CompileExpr(indexGet.Index);
                    Emit(OpCode.IndexGet, indexGet.Bracket.Line);
                    break;

                case Expr.IndexSet indexSet:
                    CompileExpr(indexSet.Target);
                    CompileExpr(indexSet.Index);
                    CompileExpr(indexSet.Value);
                    if (indexSet.CheckedType != null)
                    {
                        EmitConvert(indexSet.Value.Type, indexSet.CheckedType, indexSet.Bracket.Line);
                        EmitCheckType(indexSet.CheckedType, indexSet.Bracket.Line);
                    }
                    Emit(OpCode.IndexSet, indexSet.Bracket.Line);
                    break;

                case Expr.Get get:
                    if (get.Name.Lexeme != "length")
                    {
                        throw new CompileException($"Unknown property '.{get.Name.Lexeme}'.");
                    }
                    CompileExpr(get.Target);
                    Emit(OpCode.ArrayLength, get.Name.Line);
                    break;

                default:
                    throw new CompileException($"Unhandled expression type {expr.GetType().Name}");
            }
        }

        private void CompileCall(Expr.Call call)
        {
            CompileExpr(call.Callee);
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                CompileExpr(call.Arguments[i]);
                if (call.Callee.Type is { Kind: TypeKind.Function } fnType && i < fnType.Parameters.Count)
                {
                    EmitConvert(call.Arguments[i].Type, fnType.Parameters[i], call.Paren.Line);
                }
            }
            Emit(OpCode.Call, call.Paren.Line);
            EmitByte((byte)call.Arguments.Count, call.Paren.Line);
        }

        private void CompileLogical(Expr.Logical logical)
        {
            if (logical.Op.Type == TokenType.AmpAmp)
            {
                CompileExpr(logical.Left);
                int endJump = EmitJump(OpCode.JumpIfFalse);
                Emit(OpCode.Pop, logical.Op.Line);
                CompileExpr(logical.Right);
                PatchJump(endJump);
            }
            else
            {
                CompileExpr(logical.Left);
                int elseJump = EmitJump(OpCode.JumpIfFalse);
                int endJump = EmitJump(OpCode.Jump);
                PatchJump(elseJump);
                Emit(OpCode.Pop, logical.Op.Line);
                CompileExpr(logical.Right);
                PatchJump(endJump);
            }
        }

        private int ResolveLocal(string name)
        {
            for (int i = _locals.Count - 1; i >= 0; i--)
            {
                if (_locals[i].Name == name) return i;
            }
            return -1;
        }

        private void CompileVariableGet(Token name)
        {
            int slot = ResolveLocal(name.Lexeme);
            if (slot != -1)
            {
                Emit(OpCode.GetLocal, name.Line);
                EmitByte((byte)slot, name.Line);
            }
            else
            {
                int constIndex = _function.Chunk.AddConstant(Value.FromString(name.Lexeme));
                Emit(OpCode.GetGlobal, name.Line);
                EmitByte((byte)constIndex, name.Line);
            }
        }

        private void CompileVariableSet(Token name)
        {
            int slot = ResolveLocal(name.Lexeme);
            if (slot != -1)
            {
                Emit(OpCode.SetLocal, name.Line);
                EmitByte((byte)slot, name.Line);
            }
            else
            {
                int constIndex = _function.Chunk.AddConstant(Value.FromString(name.Lexeme));
                Emit(OpCode.SetGlobal, name.Line);
                EmitByte((byte)constIndex, name.Line);
            }
        }

        private void CompileBinary(Expr.Binary binary)
        {
            OrkType? operandType = binary.Left.Type is { IsNumeric: true } l && binary.Right.Type is { IsNumeric: true } r
                ? OrkType.Widest(l, r)
                : null;

            CompileExpr(binary.Left);
            if (operandType != null) EmitConvert(binary.Left.Type, operandType, binary.Op.Line);
            CompileExpr(binary.Right);
            if (operandType != null) EmitConvert(binary.Right.Type, operandType, binary.Op.Line);

            switch (binary.Op.Type)
            {
                case TokenType.Plus: Emit(OpCode.Add, binary.Op.Line); break;
                case TokenType.Minus: Emit(OpCode.Subtract, binary.Op.Line); break;
                case TokenType.Star: Emit(OpCode.Multiply, binary.Op.Line); break;
                case TokenType.Slash: Emit(OpCode.Divide, binary.Op.Line); break;
                case TokenType.Percent: Emit(OpCode.Modulo, binary.Op.Line); break;
                case TokenType.EqualEqual: Emit(OpCode.Equal, binary.Op.Line); break;
                case TokenType.BangEqual: Emit(OpCode.Equal, binary.Op.Line); Emit(OpCode.Not, binary.Op.Line); break;
                case TokenType.Greater: Emit(OpCode.Greater, binary.Op.Line); break;
                case TokenType.GreaterEqual: Emit(OpCode.Less, binary.Op.Line); Emit(OpCode.Not, binary.Op.Line); break;
                case TokenType.Less: Emit(OpCode.Less, binary.Op.Line); break;
                case TokenType.LessEqual: Emit(OpCode.Greater, binary.Op.Line); Emit(OpCode.Not, binary.Op.Line); break;
                default: throw new CompileException($"Unknown binary operator {binary.Op.Type}");
            }
        }

        private void CompileLiteral(Expr.Literal literal)
        {
            Value value = literal.Value switch
            {
                null => Value.Nil,
                bool b => Value.FromBool(b),
                double d => Value.FromDouble(d),
                float f => Value.FromFloat(f),
                int i => Value.FromInt(i),
                long l => Value.FromLong(l),
                string s => Value.FromString(s),
                _ => throw new CompileException($"Unsupported literal {literal.Value}"),
            };

            if (value.Kind == ValueKind.Nil) { Emit(OpCode.Nil, 0); return; }
            if (value.Kind == ValueKind.Bool) { Emit(value.AsBool ? OpCode.True : OpCode.False, 0); return; }

            int index = _function.Chunk.AddConstant(value);
            Emit(OpCode.Constant, 0);
            EmitByte((byte)index, 0);
        }

        private void Emit(OpCode op, int line) => _function.Chunk.Write(op, line);
        private void EmitByte(byte b, int line) => _function.Chunk.Write(b, line);

        private int EmitJump(OpCode op)
        {
            Emit(op, 0);
            EmitByte(0xff, 0);
            EmitByte(0xff, 0);
            return _function.Chunk.Count - 2;
        }

        private void PatchJump(int offset)
        {
            int jump = _function.Chunk.Count - offset - 2;
            if (jump > ushort.MaxValue)
            {
                throw new CompileException("Too much code to jump over.");
            }
            _function.Chunk[offset] = (byte)((jump >> 8) & 0xff);
            _function.Chunk[offset + 1] = (byte)(jump & 0xff);
        }

        private void EmitLoop(int loopStart)
        {
            Emit(OpCode.Loop, 0);
            int offset = _function.Chunk.Count - loopStart + 2;
            if (offset > ushort.MaxValue)
            {
                throw new CompileException("Loop body too large.");
            }
            EmitByte((byte)((offset >> 8) & 0xff), 0);
            EmitByte((byte)(offset & 0xff), 0);
        }
    }
}
