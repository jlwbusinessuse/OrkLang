namespace OrkLang2027.Bytecode
{
    /// <summary>
    /// A compiled unit of bytecode: instruction stream, constants pool and per-instruction line info
    /// (used for runtime error reporting).
    /// </summary>
    internal sealed class Chunk
    {
        private readonly List<byte> _code = new();
        private readonly List<int> _lines = new();
        private readonly List<Value> _constants = new();

        public IReadOnlyList<byte> Code => _code;
        public IReadOnlyList<Value> Constants => _constants;

        public int Count => _code.Count;

        public void Write(byte b, int line)
        {
            _code.Add(b);
            _lines.Add(line);
        }

        public void Write(OpCode op, int line) => Write((byte)op, line);

        public int AddConstant(Value value)
        {
            _constants.Add(value);
            return _constants.Count - 1;
        }

        public int GetLine(int offset) => _lines[offset];

        public byte this[int index]
        {
            get => _code[index];
            set => _code[index] = value;
        }
    }
}
