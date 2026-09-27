using OrkLang2027.Bytecode;

namespace OrkLang2027.VM
{
    /// <summary>
    /// Represents a single active function invocation: which function is running,
    /// where its instruction pointer currently is, and where its stack window begins.
    /// </summary>
    internal sealed class CallFrame
    {
        public ObjFunction Function { get; }
        public int InstructionPointer;
        public int StackBase;

        public CallFrame(ObjFunction function, int stackBase)
        {
            Function = function;
            StackBase = stackBase;
        }
    }
}
