namespace OrkLang2027.Bytecode
{
    /// <summary>
    /// A compiled function: its own bytecode chunk plus arity, used both for the implicit
    /// top-level script and for user-defined functions.
    /// </summary>
    internal sealed class ObjFunction
    {
        public string Name { get; }
        public int Arity { get; }
        public Chunk Chunk { get; } = new();

        public ObjFunction(string name, int arity)
        {
            Name = name;
            Arity = arity;
        }

        public override string ToString() => string.IsNullOrEmpty(Name) ? "<script>" : $"<fun {Name}>";
    }
}
