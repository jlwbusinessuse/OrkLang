using OrkLang2027.Bytecode;

namespace OrkLang2027.Typing
{
    internal enum TypeKind
    {
        Nil,
        Bool,
        Number,
        String,
        Array,
        Function,
    }

    /// <summary>
    /// Static type used by the type checker and for runtime type checks.
    /// Arrays carry an element type (<c>number[]</c>); the empty array literal <c>[]</c>
    /// has no element type and is assignable to any array type.
    /// </summary>
    internal sealed class OrkType
    {
        public TypeKind Kind { get; }
        public OrkType? Element { get; }
        public IReadOnlyList<OrkType> Parameters { get; }
        public OrkType? Return { get; }

        private OrkType(TypeKind kind, OrkType? element = null, IReadOnlyList<OrkType>? parameters = null, OrkType? returnType = null)
        {
            Kind = kind;
            Element = element;
            Parameters = parameters ?? Array.Empty<OrkType>();
            Return = returnType;
        }

        public static readonly OrkType Nil = new(TypeKind.Nil);
        public static readonly OrkType Bool = new(TypeKind.Bool);
        public static readonly OrkType Number = new(TypeKind.Number);
        public static readonly OrkType String = new(TypeKind.String);
        public static readonly OrkType EmptyArray = new(TypeKind.Array);

        public static OrkType ArrayOf(OrkType element) => new(TypeKind.Array, element);

        public static OrkType FunctionOf(IReadOnlyList<OrkType> parameters, OrkType returnType) =>
            new(TypeKind.Function, null, parameters, returnType);

        public ValueKind RuntimeKind => Kind switch
        {
            TypeKind.Nil => ValueKind.Nil,
            TypeKind.Bool => ValueKind.Bool,
            TypeKind.Number => ValueKind.Number,
            TypeKind.String => ValueKind.String,
            TypeKind.Array => ValueKind.Array,
            TypeKind.Function => ValueKind.Function,
            _ => throw new InvalidOperationException($"Unknown type kind {Kind}"),
        };

        public bool IsAssignableFrom(OrkType source)
        {
            if (Kind != source.Kind) return false;

            switch (Kind)
            {
                case TypeKind.Array:
                    if (source.Element == null) return true;
                    if (Element == null) return false;
                    return Element.IsAssignableFrom(source.Element);

                case TypeKind.Function:
                    if (Parameters.Count != source.Parameters.Count) return false;
                    for (int i = 0; i < Parameters.Count; i++)
                    {
                        if (!Parameters[i].IsAssignableFrom(source.Parameters[i]) || !source.Parameters[i].IsAssignableFrom(Parameters[i]))
                        {
                            return false;
                        }
                    }
                    return Return!.IsAssignableFrom(source.Return!);

                default:
                    return true;
            }
        }

        public override string ToString() => Kind switch
        {
            TypeKind.Nil => "nil",
            TypeKind.Bool => "bool",
            TypeKind.Number => "number",
            TypeKind.String => "string",
            TypeKind.Array => Element == null ? "[]" : $"{Element}[]",
            TypeKind.Function => $"fun({string.Join(", ", Parameters)}): {Return}",
            _ => "?",
        };
    }
}
