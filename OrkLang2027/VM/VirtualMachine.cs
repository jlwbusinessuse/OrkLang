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
                    case OpCode.Less:
                    case OpCode.Subtract:
                    case OpCode.Multiply:
                    case OpCode.Divide:
                    case OpCode.Modulo:
                        NumericBinary(instruction);
                        break;

                    case OpCode.Add:
                        DoAdd();
                        break;

                    case OpCode.Not:
                        Push(Value.FromBool(!Pop().IsTruthy));
                        break;

                    case OpCode.Negate:
                    {
                        Value operand = Pop();
                        Push(operand.Kind switch
                        {
                            ValueKind.Int => Value.FromInt(unchecked(-operand.AsInt)),
                            ValueKind.Long => Value.FromLong(unchecked(-operand.AsLong)),
                            ValueKind.Float => Value.FromFloat(-operand.AsFloat),
                            ValueKind.Double => Value.FromDouble(-operand.AsDouble),
                            _ => throw new VmRuntimeException("Operand must be a number."),
                        });
                        break;
                    }

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

                    case OpCode.CheckType:
                    {
                        var expected = (ValueKind)ReadByte(frame);
                        ValueKind actual = Peek(0).Kind;
                        if (actual != expected)
                        {
                            throw new VmRuntimeException($"Type error: expected {expected.ToString().ToLowerInvariant()} but got {actual.ToString().ToLowerInvariant()}.");
                        }
                        break;
                    }

                    case OpCode.Convert:
                    {
                        var target = (ValueKind)ReadByte(frame);
                        Value top = Pop();
                        if (!top.IsNumeric)
                        {
                            throw new VmRuntimeException($"Cannot convert {top.Kind.ToString().ToLowerInvariant()} to {target.ToString().ToLowerInvariant()}.");
                        }
                        Push(top.ConvertTo(target));
                        break;
                    }

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
                            Push(Value.FromInt(target.AsArray.Length));
                        }
                        else if (target.Kind == ValueKind.String)
                        {
                            Push(Value.FromInt(target.AsString.Length));
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
            if (!index.IsInteger)
            {
                throw new VmRuntimeException("Array index must be an integer.");
            }

            try
            {
                return target.AsArray.Get((int)index.AsLong);
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
            if (!index.IsInteger)
            {
                throw new VmRuntimeException("Array index must be an integer.");
            }

            try
            {
                target.AsArray.Set((int)index.AsLong, value);
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

            if (a.IsNumeric && b.IsNumeric)
            {
                NumericBinary(OpCode.Add);
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

        /// <summary>
        /// Arithmetic/comparison on two numeric operands. Operands are widened to the wider kind
        /// (int -> long -> float -> double). Integer math wraps on overflow and truncates on division.
        /// </summary>
        private void NumericBinary(OpCode op)
        {
            if (!Peek(0).IsNumeric || !Peek(1).IsNumeric)
            {
                throw new VmRuntimeException("Operands must be numbers.");
            }
            Value b = Pop();
            Value a = Pop();
            ValueKind kind = Value.NumericRank(a.Kind) >= Value.NumericRank(b.Kind) ? a.Kind : b.Kind;
            a = a.ConvertTo(kind);
            b = b.ConvertTo(kind);

            Push(kind switch
            {
                ValueKind.Int => IntOp(op, a.AsInt, b.AsInt),
                ValueKind.Long => LongOp(op, a.AsLong, b.AsLong),
                ValueKind.Float => FloatOp(op, a.AsFloat, b.AsFloat),
                _ => DoubleOp(op, a.AsDouble, b.AsDouble),
            });
        }

        private static Value IntOp(OpCode op, int a, int b) => op switch
        {
            OpCode.Add => Value.FromInt(unchecked(a + b)),
            OpCode.Subtract => Value.FromInt(unchecked(a - b)),
            OpCode.Multiply => Value.FromInt(unchecked(a * b)),
            OpCode.Divide => b == 0 ? throw new VmRuntimeException("Division by zero.") : Value.FromInt(b == -1 ? unchecked(-a) : a / b),
            OpCode.Modulo => b == 0 ? throw new VmRuntimeException("Division by zero.") : Value.FromInt(b == -1 ? 0 : a % b),
            OpCode.Greater => Value.FromBool(a > b),
            OpCode.Less => Value.FromBool(a < b),
            _ => throw new VmRuntimeException($"Unsupported numeric operation {op}."),
        };

        private static Value LongOp(OpCode op, long a, long b) => op switch
        {
            OpCode.Add => Value.FromLong(unchecked(a + b)),
            OpCode.Subtract => Value.FromLong(unchecked(a - b)),
            OpCode.Multiply => Value.FromLong(unchecked(a * b)),
            OpCode.Divide => b == 0 ? throw new VmRuntimeException("Division by zero.") : Value.FromLong(b == -1 ? unchecked(-a) : a / b),
            OpCode.Modulo => b == 0 ? throw new VmRuntimeException("Division by zero.") : Value.FromLong(b == -1 ? 0 : a % b),
            OpCode.Greater => Value.FromBool(a > b),
            OpCode.Less => Value.FromBool(a < b),
            _ => throw new VmRuntimeException($"Unsupported numeric operation {op}."),
        };

        private static Value FloatOp(OpCode op, float a, float b) => op switch
        {
            OpCode.Add => Value.FromFloat(a + b),
            OpCode.Subtract => Value.FromFloat(a - b),
            OpCode.Multiply => Value.FromFloat(a * b),
            OpCode.Divide => Value.FromFloat(a / b),
            OpCode.Modulo => Value.FromFloat(a % b),
            OpCode.Greater => Value.FromBool(a > b),
            OpCode.Less => Value.FromBool(a < b),
            _ => throw new VmRuntimeException($"Unsupported numeric operation {op}."),
        };

        private static Value DoubleOp(OpCode op, double a, double b) => op switch
        {
            OpCode.Add => Value.FromDouble(a + b),
            OpCode.Subtract => Value.FromDouble(a - b),
            OpCode.Multiply => Value.FromDouble(a * b),
            OpCode.Divide => Value.FromDouble(a / b),
            OpCode.Modulo => Value.FromDouble(a % b),
            OpCode.Greater => Value.FromBool(a > b),
            OpCode.Less => Value.FromBool(a < b),
            _ => throw new VmRuntimeException($"Unsupported numeric operation {op}."),
        };

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
