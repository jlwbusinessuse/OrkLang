namespace OrkLang2027.Bytecode
{
	internal enum ValueKind
	{
		Nil,
		Bool,
		Double,
		String,
		Function,
		Array,
		Int,
		Long,
		Float,
	}

	/// <summary>
	/// Tagged-union style runtime value used by the VM stack, constants pool and variables.
	/// Integer kinds (int/long) share a 64-bit slot; floating kinds (float/double) share a double slot.
	/// </summary>
	internal readonly struct Value : IEquatable<Value>
	{
		public ValueKind Kind { get; }
		private readonly double _number;
		private readonly long _integer;
		private readonly bool _boolean;
		private readonly object? _obj;

		public static readonly Value Nil = new(ValueKind.Nil, 0, 0, false, null);

		private Value(ValueKind kind, double number, long integer, bool boolean, object? obj)
		{
			Kind = kind;
			_number = number;
			_integer = integer;
			_boolean = boolean;
			_obj = obj;
		}

		public static Value FromInt(int n) => new(ValueKind.Int, 0, n, false, null);
		public static Value FromLong(long n) => new(ValueKind.Long, 0, n, false, null);
		public static Value FromFloat(float n) => new(ValueKind.Float, n, 0, false, null);
		public static Value FromDouble(double n) => new(ValueKind.Double, n, 0, false, null);
		public static Value FromBool(bool b) => new(ValueKind.Bool, 0, 0, b, null);
		public static Value FromString(string s) => new(ValueKind.String, 0, 0, false, s);
		public static Value FromFunction(object fn) => new(ValueKind.Function, 0, 0, false, fn);
		public static Value FromArray(OrkArray array) => new(ValueKind.Array, 0, 0, false, array);

		public int AsInt => (int)_integer;
		public long AsLong => _integer;
		public float AsFloat => (float)_number;
		public double AsDouble => IsInteger ? _integer : _number;
		public bool AsBool => _boolean;
		public string AsString => (string)_obj!;
		public object AsFunction => _obj!;
		public OrkArray AsArray => (OrkArray)_obj!;

		public bool IsNil => Kind == ValueKind.Nil;
		public bool IsInteger => Kind is ValueKind.Int or ValueKind.Long;
		public bool IsNumeric => Kind is ValueKind.Int or ValueKind.Long or ValueKind.Float or ValueKind.Double;

		/// <summary>Widening rank: int &lt; long &lt; float &lt; double. -1 for non-numeric kinds.</summary>
		public static int NumericRank(ValueKind kind) => kind switch
		{
			ValueKind.Int => 0,
			ValueKind.Long => 1,
			ValueKind.Float => 2,
			ValueKind.Double => 3,
			_ => -1,
		};

		/// <summary>Converts a numeric value to the given numeric kind (C#-style casts).</summary>
		public Value ConvertTo(ValueKind kind)
		{
			if (kind == Kind) return this;
			return kind switch
			{
				ValueKind.Int => FromInt(IsInteger ? unchecked((int)_integer) : (int)_number),
				ValueKind.Long => FromLong(IsInteger ? _integer : (long)_number),
				ValueKind.Float => FromFloat(IsInteger ? _integer : (float)_number),
				ValueKind.Double => FromDouble(AsDouble),
				_ => throw new InvalidOperationException($"Cannot convert {Kind} to {kind}."),
			};
		}

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
				ValueKind.Int or ValueKind.Long => _integer == other._integer,
				ValueKind.Float or ValueKind.Double => _number.Equals(other._number),
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
			ValueKind.Int or ValueKind.Long => _integer.GetHashCode(),
			ValueKind.Float or ValueKind.Double => _number.GetHashCode(),
			ValueKind.String => AsString.GetHashCode(),
			ValueKind.Function => _obj!.GetHashCode(),
			ValueKind.Array => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_obj),
			_ => 0,
		};

		public override string ToString() => Kind switch
		{
			ValueKind.Nil => "nil",
			ValueKind.Bool => _boolean ? "true" : "false",
			ValueKind.Int or ValueKind.Long => _integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
			ValueKind.Float => AsFloat.ToString(System.Globalization.CultureInfo.InvariantCulture),
			ValueKind.Double => _number.ToString(System.Globalization.CultureInfo.InvariantCulture),
			ValueKind.String => AsString,
			ValueKind.Function => "<function>",
			ValueKind.Array => AsArray.ToString(),
			_ => "?",
		};
	}
}
