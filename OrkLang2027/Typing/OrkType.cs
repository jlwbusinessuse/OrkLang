using OrkLang2027.Bytecode;

namespace OrkLang2027.Typing
{
	internal enum TypeKind
	{
		Nil,
		Bool,
		Int,
		Long,
		Float,
		Double,
		String,
		Array,
		Function,
	}

	/// <summary>
	/// Static type used by the type checker and for runtime type checks.
	/// Numeric types widen implicitly: int -> long -> float -> double.
	/// Arrays carry an element type (<c>int[]</c>) and are invariant in it; the empty array
	/// literal <c>[]</c> has no element type and is assignable to any array type.
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
		public static readonly OrkType Int = new(TypeKind.Int);
		public static readonly OrkType Long = new(TypeKind.Long);
		public static readonly OrkType Float = new(TypeKind.Float);
		public static readonly OrkType Double = new(TypeKind.Double);
		public static readonly OrkType String = new(TypeKind.String);
		public static readonly OrkType EmptyArray = new(TypeKind.Array);

		public static OrkType ArrayOf(OrkType element) => new(TypeKind.Array, element);

		public static OrkType FunctionOf(IReadOnlyList<OrkType> parameters, OrkType returnType) =>
			new(TypeKind.Function, null, parameters, returnType);

		public bool IsNumeric => NumericRank >= 0;
		public bool IsInteger => Kind is TypeKind.Int or TypeKind.Long;

		/// <summary>Widening rank: int &lt; long &lt; float &lt; double. -1 for non-numeric types.</summary>
		public int NumericRank => Kind switch
		{
			TypeKind.Int => 0,
			TypeKind.Long => 1,
			TypeKind.Float => 2,
			TypeKind.Double => 3,
			_ => -1,
		};

		/// <summary>The wider of two numeric types.</summary>
		public static OrkType Widest(OrkType a, OrkType b) => a.NumericRank >= b.NumericRank ? a : b;

		public ValueKind RuntimeKind => Kind switch
		{
			TypeKind.Nil => ValueKind.Nil,
			TypeKind.Bool => ValueKind.Bool,
			TypeKind.Int => ValueKind.Int,
			TypeKind.Long => ValueKind.Long,
			TypeKind.Float => ValueKind.Float,
			TypeKind.Double => ValueKind.Double,
			TypeKind.String => ValueKind.String,
			TypeKind.Array => ValueKind.Array,
			TypeKind.Function => ValueKind.Function,
			_ => throw new InvalidOperationException($"Unknown type kind {Kind}"),
		};

		/// <summary>True if a value of <paramref name="source"/> may be stored where this type is expected (including numeric widening).</summary>
		public bool IsAssignableFrom(OrkType source)
		{
			if (IsNumeric && source.IsNumeric) return NumericRank >= source.NumericRank;
			return Matches(source);
		}

		/// <summary>Exact type match, except the empty array literal matches any array type.</summary>
		public bool Matches(OrkType source)
		{
			if (Kind != source.Kind) return false;

			switch (Kind)
			{
				case TypeKind.Array:
					if (source.Element == null) return true;
					if (Element == null) return false;
					return Element.Matches(source.Element);

				case TypeKind.Function:
					if (Parameters.Count != source.Parameters.Count) return false;
					for (int i = 0; i < Parameters.Count; i++)
					{
						if (!Parameters[i].Matches(source.Parameters[i]) || !source.Parameters[i].Matches(Parameters[i]))
						{
							return false;
						}
					}
					return Return!.Matches(source.Return!);

				default:
					return true;
			}
		}

		public override string ToString() => Kind switch
		{
			TypeKind.Nil => "nil",
			TypeKind.Bool => "bool",
			TypeKind.Int => "int",
			TypeKind.Long => "long",
			TypeKind.Float => "float",
			TypeKind.Double => "double",
			TypeKind.String => "string",
			TypeKind.Array => Element == null ? "[]" : $"{Element}[]",
			TypeKind.Function => $"fun({string.Join(", ", Parameters)}): {Return}",
			_ => "?",
		};
	}
}
