using OrkLang2027.Ast;
using OrkLang2027.Lexing;
using OrkLang2027.Typing;

namespace OrkLang2027.Parsing
{
    internal sealed class ParseException : Exception
    {
        public ParseException(string message) : base(message) { }
    }

    /// <summary>
    /// Recursive-descent parser producing an AST from a token stream.
    /// Grammar (roughly):
    /// program        -> declaration* EOF
    /// declaration    -> funDecl | varDecl | statement
    /// funDecl        -> "fun" IDENTIFIER "(" parameters? ")" ":" type block
    /// parameters     -> IDENTIFIER ":" type ( "," IDENTIFIER ":" type )*
    /// varDecl        -> "var" IDENTIFIER ":" type "=" expression ";"
    /// type           -> ("number"|"string"|"bool"|"nil") ( "[" "]" )*
    /// statement      -> exprStmt | printStmt | block | ifStmt | whileStmt | forStmt | returnStmt
    /// expression     -> assignment
    /// assignment     -> IDENTIFIER "=" assignment | logic_or
    /// logic_or       -> logic_and ( "||" logic_and )*
    /// logic_and      -> equality ( "&&" equality )*
    /// equality       -> comparison ( ("==" | "!=") comparison )*
    /// comparison     -> term ( ("<"|"<="|">"|">=") term )*
    /// term           -> factor ( ("+"|"-") factor )*
    /// factor         -> unary ( ("*"|"/"|"%") unary )*
    /// unary          -> ("!"|"-") unary | call
    /// call           -> primary ( "(" arguments? ")" )*
    /// primary        -> NUMBER | STRING | "true" | "false" | "nil" | IDENTIFIER | "(" expression ")"
    /// </summary>
    internal sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _current;

        public Parser(List<Token> tokens) => _tokens = tokens;

        public List<Stmt> Parse()
        {
            var statements = new List<Stmt>();
            while (!IsAtEnd())
            {
                statements.Add(Declaration());
            }
            return statements;
        }

        private Stmt Declaration()
        {
            if (Match(TokenType.Fun)) return FunctionDeclaration("function");
            if (Match(TokenType.Var)) return VarDeclaration();
            return Statement();
        }

        private OrkType ParseType()
        {
            OrkType type;
            if (Match(TokenType.StringType)) type = OrkType.String;
            else if (Match(TokenType.Bool)) type = OrkType.Bool;
            else if (Match(TokenType.Nil)) type = OrkType.Nil;
            else if (Check(TokenType.Identifier) && Peek().Lexeme == "number") { Advance(); type = OrkType.Number; }
            else throw Error(Peek(), "Expect type (number, string, bool, nil or T[]).");

            while (Match(TokenType.LeftBracket))
            {
                Consume(TokenType.RightBracket, "Expect ']' in array type.");
                type = OrkType.ArrayOf(type);
            }
            return type;
        }

        private Stmt FunctionDeclaration(string kind)
        {
            Token name = Consume(TokenType.Identifier, $"Expect {kind} name.");
            Consume(TokenType.LeftParen, "Expect '(' after function name.");
            var parameters = new List<Token>();
            var parameterTypes = new List<OrkType>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    Token param = Consume(TokenType.Identifier, "Expect parameter name.");
                    Consume(TokenType.Colon, $"Expect ':' and a type after parameter '{param.Lexeme}'.");
                    parameters.Add(param);
                    parameterTypes.Add(ParseType());
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightParen, "Expect ')' after parameters.");
            Consume(TokenType.Colon, $"Expect ':' and a return type after parameters of '{name.Lexeme}'.");
            OrkType returnType = ParseType();
            Consume(TokenType.LeftBrace, "Expect '{' before function body.");
            var body = Block();
            return new Stmt.FunctionDecl(name, parameters, parameterTypes, returnType, body);
        }

        private Stmt VarDeclaration()
        {
            Token name = Consume(TokenType.Identifier, "Expect variable name.");
            Consume(TokenType.Colon, $"Expect ':' and a type after variable name '{name.Lexeme}'.");
            OrkType type = ParseType();
            Consume(TokenType.Equal, $"Variable '{name.Lexeme}' must be initialized.");
            Expr initializer = Expression();
            Consume(TokenType.Semicolon, "Expect ';' after variable declaration.");
            return new Stmt.VarDecl(name, type, initializer);
        }

        private Stmt Statement()
        {
            if (Match(TokenType.Print)) return PrintStatement();
            if (Match(TokenType.LeftBrace)) return new Stmt.Block(Block());
            if (Match(TokenType.If)) return IfStatement();
            if (Match(TokenType.While)) return WhileStatement();
            if (Match(TokenType.For)) return ForStatement();
            if (Match(TokenType.Return)) return ReturnStatement();
            return ExpressionStatement();
        }

        private Stmt ReturnStatement()
        {
            Token keyword = Previous();
            Expr? value = null;
            if (!Check(TokenType.Semicolon))
            {
                value = Expression();
            }
            Consume(TokenType.Semicolon, "Expect ';' after return value.");
            return new Stmt.Return(keyword, value);
        }

        private Stmt ForStatement()
        {
            Consume(TokenType.LeftParen, "Expect '(' after 'for'.");

            Stmt? initializer;
            if (Match(TokenType.Semicolon))
            {
                initializer = null;
            }
            else if (Match(TokenType.Var))
            {
                initializer = VarDeclaration();
            }
            else
            {
                initializer = ExpressionStatement();
            }

            Expr condition = Check(TokenType.Semicolon) ? new Expr.Literal(true) : Expression();
            Consume(TokenType.Semicolon, "Expect ';' after loop condition.");

            Expr? increment = Check(TokenType.RightParen) ? null : Expression();
            Consume(TokenType.RightParen, "Expect ')' after for clauses.");

            Stmt body = Statement();

            if (increment != null)
            {
                body = new Stmt.Block(new List<Stmt> { body, new Stmt.Expression(increment) });
            }

            body = new Stmt.While(condition, body);

            if (initializer != null)
            {
                body = new Stmt.Block(new List<Stmt> { initializer, body });
            }

            return body;
        }

        private Stmt WhileStatement()
        {
            Consume(TokenType.LeftParen, "Expect '(' after 'while'.");
            Expr condition = Expression();
            Consume(TokenType.RightParen, "Expect ')' after condition.");
            Stmt body = Statement();
            return new Stmt.While(condition, body);
        }

        private Stmt IfStatement()
        {
            Consume(TokenType.LeftParen, "Expect '(' after 'if'.");
            Expr condition = Expression();
            Consume(TokenType.RightParen, "Expect ')' after if condition.");
            Stmt thenBranch = Statement();
            Stmt? elseBranch = null;
            if (Match(TokenType.Else))
            {
                elseBranch = Statement();
            }
            return new Stmt.If(condition, thenBranch, elseBranch);
        }

        private List<Stmt> Block()
        {
            var statements = new List<Stmt>();
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                statements.Add(Declaration());
            }
            Consume(TokenType.RightBrace, "Expect '}' after block.");
            return statements;
        }

        private Stmt PrintStatement()
        {
            Expr value = Expression();
            Consume(TokenType.Semicolon, "Expect ';' after value.");
            return new Stmt.Print(value);
        }

        private Stmt ExpressionStatement()
        {
            Expr expr = Expression();
            Consume(TokenType.Semicolon, "Expect ';' after expression.");
            return new Stmt.Expression(expr);
        }

        private Expr Expression() => Assignment();

        private Expr Assignment()
        {
            Expr expr = Or();

            if (Match(TokenType.Equal))
            {
                Token equals = Previous();
                Expr value = Assignment();

                if (expr is Expr.Variable variable)
                {
                    return new Expr.Assign(variable.Name, value);
                }

                if (expr is Expr.IndexGet indexGet)
                {
                    return new Expr.IndexSet(indexGet.Target, indexGet.Bracket, indexGet.Index, value);
                }

                throw Error(equals, "Invalid assignment target.");
            }

            return expr;
        }

        private Expr Or()
        {
            Expr expr = And();
            while (Match(TokenType.PipePipe))
            {
                Token op = Previous();
                Expr right = And();
                expr = new Expr.Logical(expr, op, right);
            }
            return expr;
        }

        private Expr And()
        {
            Expr expr = Equality();
            while (Match(TokenType.AmpAmp))
            {
                Token op = Previous();
                Expr right = Equality();
                expr = new Expr.Logical(expr, op, right);
            }
            return expr;
        }

        private Expr Equality()
        {
            Expr expr = Comparison();
            while (Match(TokenType.BangEqual, TokenType.EqualEqual))
            {
                Token op = Previous();
                Expr right = Comparison();
                expr = new Expr.Binary(expr, op, right);
            }
            return expr;
        }

        private Expr Comparison()
        {
            Expr expr = Term();
            while (Match(TokenType.Greater, TokenType.GreaterEqual, TokenType.Less, TokenType.LessEqual))
            {
                Token op = Previous();
                Expr right = Term();
                expr = new Expr.Binary(expr, op, right);
            }
            return expr;
        }

        private Expr Term()
        {
            Expr expr = Factor();
            while (Match(TokenType.Plus, TokenType.Minus))
            {
                Token op = Previous();
                Expr right = Factor();
                expr = new Expr.Binary(expr, op, right);
            }
            return expr;
        }

        private Expr Factor()
        {
            Expr expr = Unary();
            while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
            {
                Token op = Previous();
                Expr right = Unary();
                expr = new Expr.Binary(expr, op, right);
            }
            return expr;
        }

        private Expr Unary()
        {
            if (Match(TokenType.Bang, TokenType.Minus))
            {
                Token op = Previous();
                Expr right = Unary();
                return new Expr.Unary(op, right);
            }
            return Call();
        }

        private Expr Call()
        {
            Expr expr = Primary();

            while (true)
            {
                if (Match(TokenType.LeftParen))
                {
                    expr = FinishCall(expr);
                }
                else if (Match(TokenType.LeftBracket))
                {
                    Token bracket = Previous();
                    Expr index = Expression();
                    Consume(TokenType.RightBracket, "Expect ']' after index.");
                    expr = new Expr.IndexGet(expr, bracket, index);
                }
                else if (Match(TokenType.Dot))
                {
                    Token name = Consume(TokenType.Identifier, "Expect property name after '.'.");
                    expr = new Expr.Get(expr, name);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expr FinishCall(Expr callee)
        {
            var arguments = new List<Expr>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    arguments.Add(Expression());
                } while (Match(TokenType.Comma));
            }
            Token paren = Consume(TokenType.RightParen, "Expect ')' after arguments.");
            return new Expr.Call(callee, paren, arguments);
        }

        private Expr Primary()
        {
            if (Match(TokenType.False)) return new Expr.Literal(false);
            if (Match(TokenType.True)) return new Expr.Literal(true);
            if (Match(TokenType.Nil)) return new Expr.Literal(null);
            if (Match(TokenType.Number, TokenType.String)) return new Expr.Literal(Previous().Literal);
            if (Match(TokenType.Identifier)) return new Expr.Variable(Previous());

            if (Match(TokenType.LeftParen))
            {
                Expr expr = Expression();
                Consume(TokenType.RightParen, "Expect ')' after expression.");
                return new Expr.Grouping(expr);
            }

            if (Match(TokenType.LeftBracket))
            {
                Token bracket = Previous();
                var elements = new List<Expr>();
                if (!Check(TokenType.RightBracket))
                {
                    do
                    {
                        elements.Add(Expression());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightBracket, "Expect ']' after array elements.");
                return new Expr.ArrayLiteral(bracket, elements);
            }

            throw Error(Peek(), "Expect expression.");
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();
            throw Error(Peek(), message);
        }

        private static ParseException Error(Token token, string message)
        {
            string where = token.Type == TokenType.Eof ? "end" : $"'{token.Lexeme}'";
            return new ParseException($"[line {token.Line}] Error at {where}: {message}");
        }

        private bool Match(params TokenType[] types)
        {
            foreach (var type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }
            return false;
        }

        private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private bool IsAtEnd() => Peek().Type == TokenType.Eof;

        private Token Peek() => _tokens[_current];

        private Token Previous() => _tokens[_current - 1];
    }
}
