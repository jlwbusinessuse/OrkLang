namespace OrkLang2027.Bytecode
{
    /// <summary>
    /// The full instruction set understood by the OrkLang virtual machine.
    /// Each instruction is a single byte optionally followed by operand bytes.
    /// </summary>
    internal enum OpCode : byte
    {
        Constant,     // push constants[operand]
        Nil,
        True,
        False,
        Pop,

        GetLocal,     // operand: slot
        SetLocal,     // operand: slot
        GetGlobal,    // operand: constant index (name)
        DefineGlobal, // operand: constant index (name)
        SetGlobal,    // operand: constant index (name)

        Equal,
        Greater,
        Less,
        Add,
        Subtract,
        Multiply,
        Divide,
        Modulo,
        Not,
        Negate,

        Print,

        Jump,         // operand: 2-byte offset, unconditional
        JumpIfFalse,  // operand: 2-byte offset, pops nothing (peeks)
        Loop,         // operand: 2-byte offset, jumps backward

        Call,         // operand: arg count
        Return,

        BuildArray,   // operand: element count, pops N values and pushes an array
        IndexGet,     // pops index, target; pushes target[index]
        IndexSet,     // pops value, index, target; pushes value (leaves it for chained use)
        ArrayLength,  // pops array; pushes its length

        Halt,

        CheckType,    // operand: expected ValueKind; peeks top value, errors on mismatch
    }
}
