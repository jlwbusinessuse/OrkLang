using OrkLang2027.Lexing;
using OrkLang2027.Typing;

namespace OrkLang2027.Ast
{
    internal abstract class Expr
    {
        internal sealed class Literal : Expr
        {
            public object? Value { get; }
            public Literal(object? value) => Value = value;
        }

        internal sealed class Variable : Expr
        {
            public Token Name { get; }
            public Variable(Token name) => Name = name;
        }

        internal sealed class Assign : Expr
        {
            public Token Name { get; }
            public Expr Value { get; }
            /// <summary>Declared type of the target, filled in by the type checker.</summary>
            public OrkType? CheckedType { get; set; }
            public Assign(Token name, Expr value) { Name = name; Value = value; }
        }

        internal sealed class Unary : Expr
        {
            public Token Op { get; }
            public Expr Right { get; }
            public Unary(Token op, Expr right) { Op = op; Right = right; }
        }

        internal sealed class Binary : Expr
        {
            public Expr Left { get; }
            public Token Op { get; }
            public Expr Right { get; }
            public Binary(Expr left, Token op, Expr right) { Left = left; Op = op; Right = right; }
        }

        internal sealed class Logical : Expr
        {
            public Expr Left { get; }
            public Token Op { get; }
            public Expr Right { get; }
            public Logical(Expr left, Token op, Expr right) { Left = left; Op = op; Right = right; }
        }

        internal sealed class Grouping : Expr
        {
            public Expr Expression { get; }
            public Grouping(Expr expression) => Expression = expression;
        }

        internal sealed class Call : Expr
        {
            public Expr Callee { get; }
            public Token Paren { get; }
            public List<Expr> Arguments { get; }
            public Call(Expr callee, Token paren, List<Expr> arguments)
            {
                Callee = callee;
                Paren = paren;
                Arguments = arguments;
            }
        }

        internal sealed class ArrayLiteral : Expr
        {
            public Token Bracket { get; }
            public List<Expr> Elements { get; }
            public ArrayLiteral(Token bracket, List<Expr> elements)
            {
                Bracket = bracket;
                Elements = elements;
            }
        }

        internal sealed class IndexGet : Expr
        {
            public Expr Target { get; }
            public Token Bracket { get; }
            public Expr Index { get; }
            public IndexGet(Expr target, Token bracket, Expr index)
            {
                Target = target;
                Bracket = bracket;
                Index = index;
            }
        }

        internal sealed class IndexSet : Expr
        {
            public Expr Target { get; }
            public Token Bracket { get; }
            public Expr Index { get; }
            public Expr Value { get; }
            /// <summary>Element type of the target array, filled in by the type checker.</summary>
            public OrkType? CheckedType { get; set; }
            public IndexSet(Expr target, Token bracket, Expr index, Expr value)
            {
                Target = target;
                Bracket = bracket;
                Index = index;
                Value = value;
            }
        }

        internal sealed class Get : Expr
        {
            public Expr Target { get; }
            public Token Name { get; }
            public Get(Expr target, Token name)
            {
                Target = target;
                Name = name;
            }
        }
    }
}
