using OrkLang2027.Lexing;
using OrkLang2027.Typing;

namespace OrkLang2027.Ast
{
    internal abstract class Stmt
    {
        internal sealed class Expression : Stmt
        {
            public Expr Expr { get; }
            public Expression(Expr expr) => Expr = expr;
        }

        internal sealed class Print : Stmt
        {
            public Expr Expr { get; }
            public Print(Expr expr) => Expr = expr;
        }

        internal sealed class VarDecl : Stmt
        {
            public Token Name { get; }
            public OrkType Type { get; }
            public Expr Initializer { get; }
            public VarDecl(Token name, OrkType type, Expr initializer) { Name = name; Type = type; Initializer = initializer; }
        }

        internal sealed class Block : Stmt
        {
            public List<Stmt> Statements { get; }
            public Block(List<Stmt> statements) => Statements = statements;
        }

        internal sealed class If : Stmt
        {
            public Expr Condition { get; }
            public Stmt Then { get; }
            public Stmt? Else { get; }
            public If(Expr condition, Stmt then, Stmt? elseBranch) { Condition = condition; Then = then; Else = elseBranch; }
        }

        internal sealed class While : Stmt
        {
            public Expr Condition { get; }
            public Stmt Body { get; }
            public While(Expr condition, Stmt body) { Condition = condition; Body = body; }
        }

        internal sealed class FunctionDecl : Stmt
        {
            public Token Name { get; }
            public List<Token> Parameters { get; }
            public List<OrkType> ParameterTypes { get; }
            public OrkType ReturnType { get; }
            public List<Stmt> Body { get; }
            public FunctionDecl(Token name, List<Token> parameters, List<OrkType> parameterTypes, OrkType returnType, List<Stmt> body)
            {
                Name = name;
                Parameters = parameters;
                ParameterTypes = parameterTypes;
                ReturnType = returnType;
                Body = body;
            }

            public OrkType FunctionType => OrkType.FunctionOf(ParameterTypes, ReturnType);
        }

        internal sealed class Return : Stmt
        {
            public Token Keyword { get; }
            public Expr? Value { get; }
            public Return(Token keyword, Expr? value) { Keyword = keyword; Value = value; }
        }
    }
}
