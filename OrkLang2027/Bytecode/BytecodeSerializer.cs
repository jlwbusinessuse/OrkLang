using System.Text;

namespace OrkLang2027.Bytecode
{
    /// <summary>
    /// Serializes/deserializes compiled OrkLang bytecode (<see cref="ObjFunction"/> trees) to/from
    /// a compact binary format, so scripts can be pre-compiled to ".orkbc" files and run directly
    /// by the VM without re-lexing/parsing/compiling every time.
    /// </summary>
    internal static class BytecodeSerializer
    {
        private const uint Magic = 0x4B524F4F; // "OORK" little-endian-ish tag
        private const byte Version = 1;

        public static void Save(ObjFunction function, string path)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(Magic);
            writer.Write(Version);
            WriteFunction(writer, function);
        }

        public static ObjFunction Load(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            uint magic = reader.ReadUInt32();
            if (magic != Magic)
            {
                throw new InvalidDataException("Not a valid OrkLang bytecode file.");
            }
            byte version = reader.ReadByte();
            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported bytecode version {version}.");
            }

            return ReadFunction(reader);
        }

        private static void WriteFunction(BinaryWriter writer, ObjFunction function)
        {
            writer.Write(function.Name);
            writer.Write(function.Arity);
            WriteChunk(writer, function.Chunk);
        }

        private static ObjFunction ReadFunction(BinaryReader reader)
        {
            string name = reader.ReadString();
            int arity = reader.ReadInt32();
            var function = new ObjFunction(name, arity);
            ReadChunk(reader, function.Chunk);
            return function;
        }

        private static void WriteChunk(BinaryWriter writer, Chunk chunk)
        {
            writer.Write(chunk.Count);
            for (int i = 0; i < chunk.Count; i++)
            {
                writer.Write(chunk[i]);
            }
            for (int i = 0; i < chunk.Count; i++)
            {
                writer.Write(chunk.GetLine(i));
            }

            writer.Write(chunk.Constants.Count);
            foreach (var constant in chunk.Constants)
            {
                WriteValue(writer, constant);
            }
        }

        private static void ReadChunk(BinaryReader reader, Chunk chunk)
        {
            int codeCount = reader.ReadInt32();
            var bytes = new byte[codeCount];
            for (int i = 0; i < codeCount; i++)
            {
                bytes[i] = reader.ReadByte();
            }
            var lines = new int[codeCount];
            for (int i = 0; i < codeCount; i++)
            {
                lines[i] = reader.ReadInt32();
            }
            for (int i = 0; i < codeCount; i++)
            {
                chunk.Write(bytes[i], lines[i]);
            }

            int constantCount = reader.ReadInt32();
            for (int i = 0; i < constantCount; i++)
            {
                chunk.AddConstant(ReadValue(reader));
            }
        }

        private static void WriteValue(BinaryWriter writer, Value value)
        {
            writer.Write((byte)value.Kind);
            switch (value.Kind)
            {
                case ValueKind.Nil:
                    break;
                case ValueKind.Bool:
                    writer.Write(value.AsBool);
                    break;
                case ValueKind.Number:
                    writer.Write(value.AsNumber);
                    break;
                case ValueKind.String:
                    writer.Write(value.AsString);
                    break;
                case ValueKind.Function:
                    WriteFunction(writer, (ObjFunction)value.AsFunction);
                    break;
                default:
                    throw new InvalidDataException($"Cannot serialize value kind {value.Kind}.");
            }
        }

        private static Value ReadValue(BinaryReader reader)
        {
            var kind = (ValueKind)reader.ReadByte();
            return kind switch
            {
                ValueKind.Nil => Value.Nil,
                ValueKind.Bool => Value.FromBool(reader.ReadBoolean()),
                ValueKind.Number => Value.FromNumber(reader.ReadDouble()),
                ValueKind.String => Value.FromString(reader.ReadString()),
                ValueKind.Function => Value.FromFunction(ReadFunction(reader)),
                _ => throw new InvalidDataException($"Cannot deserialize value kind {kind}."),
            };
        }
    }
}
