using OrkLang2027.Bytecode;

namespace OrkLang2027.VM
{
    internal sealed class VmRuntimeException : Exception
    {
        public VmRuntimeException(string message) : base(message) { }
    }

    internal enum InterpretResult
    {
        Ok,
        RuntimeError,
    }

    /// <summary>
    /// Stack-based bytecode virtual machine. Executes an <see cref="ObjFunction"/> chunk,
    /// maintaining an operand stack and a stack of call frames for function invocations.
    /// This mirrors a very small subset of how the JVM/CLR or clox execute compiled code.
    /// </summary>
    internal sealed class VirtualMachine
    {
        private const int MaxFrames = 256;
        private const int StackMax = MaxFrames * 256;

        private readonly Value[] _stack = new Value[StackMax];
        private int _stackTop;

        private readonly List<CallFrame> _frames = new();
        private readonly Dictionary<string, Value> _globals = new();

        public TextWriter Output { get; set; } = Console.Out;

        public InterpretResult Run(ObjFunction script)
        {
            Push(Value.FromFunction(script));
            _frames.Add(new CallFrame(script, 0));

            try
            {
                return RunLoop();
            }
            catch (VmRuntimeException ex)
            {
                Console.Error.WriteLine($"Runtime error: {ex.Message}");
                return InterpretResult.RuntimeError;
            }
        }

        private CallFrame CurrentFrame => _frames[^1];

        private InterpretResult RunLoop()
        {
            while (true)
            {
                CallFrame frame = CurrentFrame;
                Chunk chunk = frame.Function.Chunk;
                OpCode instruction = (OpCode)ReadByte(frame);

                switch (instruction)
                {
                    case OpCode.Constant:
                        Push(chunk.Constants[ReadByte(frame)]);
                        break;

                    case OpCode.Nil:
                        Push(Value.Nil);
                        break;

                    case OpCode.True:
                        Push(Value.FromBool(true));
                        break;

                    case OpCode.False:
                        Push(Value.FromBool(false));
                        break;

                    case OpCode.Pop:
                        Pop();
                        break;

                    case OpCode.GetLocal:
                    {
                        int slot = ReadByte(frame);
                        Push(_stack[frame.StackBase + slot]);
                        break;
                    }

                    case OpCode.SetLocal:
                    {
                        int slot = ReadByte(frame);
                        _stack[frame.StackBase + slot] = Peek(0);
                        break;
                    }

                    case OpCode.GetGlobal:
                    {
                        string name = chunk.Constants[ReadByte(frame)].AsString;
                        if (!_globals.TryGetValue(name, out var value))
                        {
                            throw new VmRuntimeException($"Undefined variable '{name}'.");
                        }
                        Push(value);
                        break;
                    }

                    case OpCode.DefineGlobal:
                    {
                        string name = chunk.Constants[ReadByte(frame)].AsString;
                        _globals[name] = Pop();
                        break;
                    }

                    case OpCode.SetGlobal:
                    {
                        string name = chunk.Constants[ReadByte(frame)].AsString;
                        if (!_globals.ContainsKey(name))
                        {
                            throw new VmRuntimeException($"Undefined variable '{name}'.");
                        }
                        _globals[name] = Peek(0);
                        break;
                    }

                    case OpCode.Equal:
                    {
                        Value b = Pop();
                        Value a = Pop();
                        Push(Value.FromBool(a.Equals(b)));
                        break;
                    }

                    case OpCode.Greater:
                        BinaryNumberOp((a, b) => Value.FromBool(a > b));
                        break;

                    case OpCode.Less:
                        BinaryNumberOp((a, b) => Value.FromBool(a < b));
                        break;

                    case OpCode.Add:
                        DoAdd();
                        break;

                    case OpCode.Subtract:
                        BinaryNumberOp((a, b) => Value.FromNumber(a - b));
                        break;

                    case OpCode.Multiply:
                        BinaryNumberOp((a, b) => Value.FromNumber(a * b));
                        break;

                    case OpCode.Divide:
                        BinaryNumberOp((a, b) => Value.FromNumber(a / b));
                        break;

                    case OpCode.Modulo:
                        BinaryNumberOp((a, b) => Value.FromNumber(a % b));
                        break;

                    case OpCode.Not:
                        Push(Value.FromBool(!Pop().IsTruthy));
                        break;

                    case OpCode.Negate:
                        if (Peek(0).Kind != ValueKind.Number)
                        {
                            throw new VmRuntimeException("Operand must be a number.");
                        }
                        Push(Value.FromNumber(-Pop().AsNumber));
                        break;

                    case OpCode.Print:
                        Output.WriteLine(Pop().ToString());
                        break;

                    case OpCode.Jump:
                    {
                        int offset = ReadShort(frame);
                        frame.InstructionPointer += offset;
                        break;
                    }

                    case OpCode.JumpIfFalse:
                    {
                        int offset = ReadShort(frame);
                        if (!Peek(0).IsTruthy)
                        {
                            frame.InstructionPointer += offset;
                        }
                        break;
                    }

                    case OpCode.Loop:
                    {
                        int offset = ReadShort(frame);
                        frame.InstructionPointer -= offset;
                        break;
                    }

                    case OpCode.Call:
                    {
                        int argCount = ReadByte(frame);
                        CallValue(Peek(argCount), argCount);
                        break;
                    }

                    case OpCode.Return:
                    {
                        Value result = Pop();
                        CallFrame finished = _frames[^1];
                        _frames.RemoveAt(_frames.Count - 1);
                        _stackTop = finished.StackBase;
                        if (_frames.Count == 0)
                        {
                            return InterpretResult.Ok;
                        }
                        Push(result);
                        break;
                    }

                    case OpCode.Halt:
                        return InterpretResult.Ok;

                    case OpCode.BuildArray:
                    {
                        int count = ReadByte(frame);
                        var items = new List<Value>(count);
                        for (int i = 0; i < count; i++)
                        {
                            items.Add(_stack[_stackTop - count + i]);
                        }
                        _stackTop -= count;
                        Push(Value.FromArray(new OrkArray(items)));
                        break;
                    }

                    case OpCode.IndexGet:
                    {
                        Value index = Pop();
                        Value target = Pop();
                        Push(ArrayIndexGet(target, index));
                        break;
                    }

                    case OpCode.IndexSet:
                    {
                        Value value = Pop();
                        Value index = Pop();
                        Value target = Pop();
                        ArrayIndexSet(target, index, value);
                        Push(value);
                        break;
                    }

                    case OpCode.ArrayLength:
                    {
                        Value target = Pop();
                        if (target.Kind == ValueKind.Array)
                        {
                            Push(Value.FromNumber(target.AsArray.Length));
                        }
                        else if (target.Kind == ValueKind.String)
                        {
                            Push(Value.FromNumber(target.AsString.Length));
                        }
                        else
                        {
                            throw new VmRuntimeException("Only arrays and strings have a '.length'.");
                        }
                        break;
                    }

                    default:
                        throw new VmRuntimeException($"Unknown opcode {instruction}");
                }
            }
        }

        private static Value ArrayIndexGet(Value target, Value index)
        {
            if (target.Kind != ValueKind.Array)
            {
                throw new VmRuntimeException("Only arrays can be indexed.");
            }
            if (index.Kind != ValueKind.Number)
            {
                throw new VmRuntimeException("Array index must be a number.");
            }

            try
            {
                return target.AsArray.Get((int)index.AsNumber);
            }
            catch (IndexOutOfRangeException ex)
            {
                throw new VmRuntimeException(ex.Message);
            }
        }

        private static void ArrayIndexSet(Value target, Value index, Value value)
        {
            if (target.Kind != ValueKind.Array)
            {
                throw new VmRuntimeException("Only arrays can be indexed.");
            }
            if (index.Kind != ValueKind.Number)
            {
                throw new VmRuntimeException("Array index must be a number.");
            }

            try
            {
                target.AsArray.Set((int)index.AsNumber, value);
            }
            catch (IndexOutOfRangeException ex)
            {
                throw new VmRuntimeException(ex.Message);
            }
        }

        private void CallValue(Value callee, int argCount)
        {
            if (callee.Kind != ValueKind.Function || callee.AsFunction is not ObjFunction fn)
            {
                throw new VmRuntimeException("Can only call functions.");
            }

            if (argCount != fn.Arity)
            {
                throw new VmRuntimeException($"Expected {fn.Arity} arguments but got {argCount}.");
            }

            if (_frames.Count >= MaxFrames)
            {
                throw new VmRuntimeException("Stack overflow.");
            }

            int stackBase = _stackTop - argCount - 1;
            _frames.Add(new CallFrame(fn, stackBase));
        }

        private void DoAdd()
        {
            Value b = Peek(0);
            Value a = Peek(1);

            if (a.Kind == ValueKind.Number && b.Kind == ValueKind.Number)
            {
                Pop(); Pop();
                Push(Value.FromNumber(a.AsNumber + b.AsNumber));
            }
            else if (a.Kind == ValueKind.String || b.Kind == ValueKind.String)
            {
                Pop(); Pop();
                Push(Value.FromString(a.ToString() + b.ToString()));
            }
            else
            {
                throw new VmRuntimeException("Operands must be two numbers or include a string.");
            }
        }

        private void BinaryNumberOp(Func<double, double, Value> op)
        {
            if (Peek(0).Kind != ValueKind.Number || Peek(1).Kind != ValueKind.Number)
            {
                throw new VmRuntimeException("Operands must be numbers.");
            }
            double b = Pop().AsNumber;
            double a = Pop().AsNumber;
            Push(op(a, b));
        }

        private byte ReadByte(CallFrame frame) => frame.Function.Chunk[frame.InstructionPointer++];

        private int ReadShort(CallFrame frame)
        {
            int hi = ReadByte(frame);
            int lo = ReadByte(frame);
            return (hi << 8) | lo;
        }

        private void Push(Value value) => _stack[_stackTop++] = value;
        private Value Pop() => _stack[--_stackTop];
        private Value Peek(int distance) => _stack[_stackTop - 1 - distance];
    }
}
