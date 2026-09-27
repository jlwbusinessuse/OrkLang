using System.Text;

namespace OrkLang2027.Lexing
{
    internal sealed class LexException : Exception
    {
        public int Line { get; }
        public LexException(string message, int line) : base(message) => Line = line;
    }

    internal sealed class Lexer
    {
        private static readonly Dictionary<string, TokenType> Keywords = new()
        {
            ["and"] = TokenType.And,
            ["or"] = TokenType.Or,
            ["class"] = TokenType.Class,
            ["else"] = TokenType.Else,
            ["false"] = TokenType.False,
            ["for"] = TokenType.For,
            ["fun"] = TokenType.Fun,
            ["if"] = TokenType.If,
            ["nil"] = TokenType.Nil,
            ["print"] = TokenType.Print,
            ["return"] = TokenType.Return,
            ["super"] = TokenType.Super,
            ["this"] = TokenType.This,
            ["true"] = TokenType.True,
            ["var"] = TokenType.Var,
            ["while"] = TokenType.While,
            ["int"] = TokenType.Int,
            ["double"] = TokenType.Double,
            ["bool"] = TokenType.Bool,
            ["string"] = TokenType.StringType,
            ["void"] = TokenType.Void,
        };

        private readonly string _source;
        private readonly List<Token> _tokens = new();
        private int _start;
        private int _current;
        private int _line = 1;

        public Lexer(string source) => _source = source;

        public List<Token> ScanTokens()
        {
            while (!IsAtEnd())
            {
                _start = _current;
                ScanToken();
            }

            _tokens.Add(new Token(TokenType.Eof, string.Empty, null, _line));
            return _tokens;
        }

        private bool IsAtEnd() => _current >= _source.Length;

        private char Advance() => _source[_current++];

        private bool Match(char expected)
        {
            if (IsAtEnd() || _source[_current] != expected) return false;
            _current++;
            return true;
        }

        private char Peek() => IsAtEnd() ? '\0' : _source[_current];

        private char PeekNext() => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];

        private void ScanToken()
        {
            char c = Advance();
            switch (c)
            {
                case '(': AddToken(TokenType.LeftParen); break;
                case ')': AddToken(TokenType.RightParen); break;
                case '{': AddToken(TokenType.LeftBrace); break;
                case '}': AddToken(TokenType.RightBrace); break;
                case '[': AddToken(TokenType.LeftBracket); break;
                case ']': AddToken(TokenType.RightBracket); break;
                case ',': AddToken(TokenType.Comma); break;
                case '.': AddToken(TokenType.Dot); break;
                case ':': AddToken(TokenType.Colon); break;
                case '-': AddToken(TokenType.Minus); break;
                case '+': AddToken(TokenType.Plus); break;
                case ';': AddToken(TokenType.Semicolon); break;
                case '*': AddToken(TokenType.Star); break;
                case '%': AddToken(TokenType.Percent); break;
                case '!': AddToken(Match('=') ? TokenType.BangEqual : TokenType.Bang); break;
                case '=': AddToken(Match('=') ? TokenType.EqualEqual : TokenType.Equal); break;
                case '<': AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less); break;
                case '>': AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater); break;
                case '&':
                    if (Match('&')) AddToken(TokenType.AmpAmp);
                    else throw new LexException("Unexpected character '&'.", _line);
                    break;
                case '|':
                    if (Match('|')) AddToken(TokenType.PipePipe);
                    else throw new LexException("Unexpected character '|'.", _line);
                    break;
                case '/':
                    if (Match('/'))
                    {
                        while (Peek() != '\n' && !IsAtEnd()) Advance();
                    }
                    else if (Match('*'))
                    {
                        while (!(Peek() == '*' && PeekNext() == '/') && !IsAtEnd())
                        {
                            if (Peek() == '\n') _line++;
                            Advance();
                        }
                        if (!IsAtEnd()) { Advance(); Advance(); }
                    }
                    else
                    {
                        AddToken(TokenType.Slash);
                    }
                    break;
                case ' ':
                case '\r':
                case '\t':
                    break;
                case '\n':
                    _line++;
                    break;
                case '"':
                    ScanString();
                    break;
                default:
                    if (char.IsDigit(c))
                    {
                        ScanNumber();
                    }
                    else if (char.IsLetter(c) || c == '_')
                    {
                        ScanIdentifier();
                    }
                    else
                    {
                        throw new LexException($"Unexpected character '{c}'.", _line);
                    }
                    break;
            }
        }

        private void ScanIdentifier()
        {
            while (char.IsLetterOrDigit(Peek()) || Peek() == '_') Advance();
            string text = _source[_start.._current];
            TokenType type = Keywords.TryGetValue(text, out var kw) ? kw : TokenType.Identifier;
            AddToken(type);
        }

        private void ScanNumber()
        {
            while (char.IsDigit(Peek())) Advance();

            if (Peek() == '.' && char.IsDigit(PeekNext()))
            {
                Advance();
                while (char.IsDigit(Peek())) Advance();
            }

            string text = _source[_start.._current];
            AddToken(TokenType.Number, double.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
        }

        private void ScanString()
        {
            var sb = new StringBuilder();
            while (Peek() != '"' && !IsAtEnd())
            {
                if (Peek() == '\n') _line++;
                if (Peek() == '\\' && PeekNext() == '"')
                {
                    sb.Append('"');
                    Advance();
                    Advance();
                    continue;
                }
                sb.Append(Advance());
            }

            if (IsAtEnd())
            {
                throw new LexException("Unterminated string.", _line);
            }

            Advance(); // closing quote
            AddToken(TokenType.String, sb.ToString());
        }

        private void AddToken(TokenType type) => AddToken(type, null);

        private void AddToken(TokenType type, object? literal)
        {
            string text = _source[_start.._current];
            _tokens.Add(new Token(type, text, literal, _line));
        }
    }
}
