namespace OrkLang2027.Bytecode
{
    internal enum ValueKind
    {
        Nil,
        Bool,
        Number,
        String,
        Function,
        Array,
    }

    /// <summary>
    /// Tagged-union style runtime value used by the VM stack, constants pool and variables.
    /// </summary>
    internal readonly struct Value : IEquatable<Value>
    {
        public ValueKind Kind { get; }
        private readonly double _number;
        private readonly bool _boolean;
        private readonly object? _obj;

        public static readonly Value Nil = new(ValueKind.Nil, 0, false, null);

        private Value(ValueKind kind, double number, bool boolean, object? obj)
        {
            Kind = kind;
            _number = number;
            _boolean = boolean;
            _obj = obj;
        }

        public static Value FromNumber(double n) => new(ValueKind.Number, n, false, null);
        public static Value FromBool(bool b) => new(ValueKind.Bool, 0, b, null);
        public static Value FromString(string s) => new(ValueKind.String, 0, false, s);
        public static Value FromFunction(object fn) => new(ValueKind.Function, 0, false, fn);
        public static Value FromArray(OrkArray array) => new(ValueKind.Array, 0, false, array);

        public double AsNumber => _number;
        public bool AsBool => _boolean;
        public string AsString => (string)_obj!;
        public object AsFunction => _obj!;
        public OrkArray AsArray => (OrkArray)_obj!;

        public bool IsNil => Kind == ValueKind.Nil;
        public bool IsTruthy => Kind switch
        {
            ValueKind.Nil => false,
            ValueKind.Bool => _boolean,
            _ => true,
        };

        public bool Equals(Value other)
        {
            if (Kind != other.Kind) return false;
            return Kind switch
            {
                ValueKind.Nil => true,
                ValueKind.Bool => _boolean == other._boolean,
                ValueKind.Number => _number.Equals(other._number),
                ValueKind.String => AsString == other.AsString,
                ValueKind.Function => ReferenceEquals(_obj, other._obj),
                ValueKind.Array => ReferenceEquals(_obj, other._obj),
                _ => false,
            };
        }

        public override bool Equals(object? obj) => obj is Value v && Equals(v);
        public override int GetHashCode() => Kind switch
        {
            ValueKind.Nil => 0,
            ValueKind.Bool => _boolean.GetHashCode(),
            ValueKind.Number => _number.GetHashCode(),
            ValueKind.String => AsString.GetHashCode(),
            ValueKind.Function => _obj!.GetHashCode(),
            ValueKind.Array => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_obj),
            _ => 0,
        };

        public override string ToString() => Kind switch
        {
            ValueKind.Nil => "nil",
            ValueKind.Bool => _boolean ? "true" : "false",
            ValueKind.Number => _number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ValueKind.String => AsString,
            ValueKind.Function => "<function>",
            ValueKind.Array => AsArray.ToString(),
            _ => "?",
        };
    }
}
