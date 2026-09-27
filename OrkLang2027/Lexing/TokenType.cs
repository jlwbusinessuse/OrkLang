namespace OrkLang2027.Lexing
{
    internal enum TokenType
    {
        // Single-character tokens
        LeftParen, RightParen, LeftBrace, RightBrace, LeftBracket, RightBracket,
        Comma, Dot, Minus, Plus, Semicolon, Slash, Star, Percent,

        // One or two character tokens
        Bang, BangEqual,
        Equal, EqualEqual,
        Greater, GreaterEqual,
        Less, LessEqual,
        AmpAmp, PipePipe,

        // Literals
        Identifier, String, Number,

        // Keywords
        And, Or, Class, Else, False, For, Fun, If, Nil, Print,
        Return, Super, This, True, Var, While, Int, Double, Bool, StringType, Void,

        Eof
    }
}
