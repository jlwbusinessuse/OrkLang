using OrkLang2027.Bytecode;
using OrkLang2027.Compiling;
using OrkLang2027.Lexing;
using OrkLang2027.Parsing;
using OrkLang2027.Typing;
using OrkLang2027.VM;

namespace OrkLang2027
{
    /// <summary>
    /// Entry point wiring together the OrkLang pipeline:
    /// source text -> Lexer -> tokens -> Parser -> AST -> Compiler -> bytecode -> VirtualMachine.
    /// </summary>
    internal class Program
    {
        private const string SampleScript = @"
fun fib(n: int): int {
    if (n < 2) {
        return n;
    }
    return fib(n - 1) + fib(n - 2);
}

var i: int = 0;
while (i < 10) {
    print fib(i);
    i = i + 1;
}

var message: string = ""Hello"" + "", "" + ""OrkLang!"";
print message;
";

        private const string OrkSourceExtension = ".ork";
        private const string OrkBytecodeExtension = ".orkc";

        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                return RunSource(SampleScript);
            }

            switch (args[0])
            {
                case "-r":
                case "--run":
                    return HandleRun(args);

                case "-c":
                case "--compile":
                    return HandleCompile(args);

                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;

                default:
                    // Back-compat: a bare path argument is just run directly.
                    if (File.Exists(args[0]))
                    {
                        return RunPath(args[0]);
                    }
                    PrintUsage();
                    return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("OrkLang2027 - Java-styled scripting language VM");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  OrkLang2027 -r <file.ork|file.orkc>   Run a source or compiled bytecode file");
            Console.WriteLine("  OrkLang2027 -c <file.ork> [-o out.orkc]  Compile source to bytecode (.orkc)");
            Console.WriteLine("  OrkLang2027                            Run the built-in sample script");
        }

        private static int HandleRun(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Error: -r requires a file path.");
                PrintUsage();
                return 1;
            }

            return RunPath(args[1]);
        }

        private static int RunPath(string path)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Error: file not found '{path}'.");
                return 1;
            }

            try
            {
                ObjFunction script = string.Equals(Path.GetExtension(path), OrkBytecodeExtension, StringComparison.OrdinalIgnoreCase)
                    ? BytecodeSerializer.Load(path)
                    : CompileSourceFile(path);

                var vm = new VirtualMachine();
                InterpretResult result = vm.Run(script);
                return result == InterpretResult.Ok ? 0 : 1;
            }
            catch (Exception ex) when (ex is LexException or ParseException or TypeException or CompileException or InvalidDataException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        private static int HandleCompile(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Error: -c requires a source file path.");
                PrintUsage();
                return 1;
            }

            string inputPath = args[1];
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Error: file not found '{inputPath}'.");
                return 1;
            }

            string outputPath = Path.ChangeExtension(inputPath, OrkBytecodeExtension);
            for (int i = 2; i < args.Length - 1; i++)
            {
                if (args[i] is "-o" or "--output")
                {
                    outputPath = args[i + 1];
                    break;
                }
            }

            try
            {
                ObjFunction script = CompileSourceFile(inputPath);
                BytecodeSerializer.Save(script, outputPath);
                Console.WriteLine($"Compiled '{inputPath}' -> '{outputPath}'.");
                return 0;
            }
            catch (Exception ex) when (ex is LexException or ParseException or TypeException or CompileException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        private static ObjFunction CompileSourceFile(string path) => CompileSource(File.ReadAllText(path));

        private static ObjFunction CompileSource(string source)
        {
            var lexer = new Lexer(source);
            List<Token> tokens = lexer.ScanTokens();

            var parser = new Parser(tokens);
            var statements = parser.Parse();

            TypeChecker.Check(statements);

            return Compiler.CompileScript(statements);
        }

        private static int RunSource(string source)
        {
            try
            {
                ObjFunction script = CompileSource(source);

                var vm = new VirtualMachine();
                InterpretResult result = vm.Run(script);

                return result == InterpretResult.Ok ? 0 : 1;
            }
            catch (LexException ex)
            {
                Console.Error.WriteLine($"Lex error [line {ex.Line}]: {ex.Message}");
                return 1;
            }
            catch (ParseException ex)
            {
                Console.Error.WriteLine($"Parse error: {ex.Message}");
                return 1;
            }
            catch (TypeException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            catch (CompileException ex)
            {
                Console.Error.WriteLine($"Compile error: {ex.Message}");
                return 1;
            }
        }
    }
}
