using Bayan.Semantics.Symbols;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Parsing;
using Bayan.Syntax.Text;

namespace Bayan.Semantics;

/// <summary>Functions every module sees without an import.</summary>
public sealed class Builtins
{
    public Builtins()
    {
        Module = new ModuleSymbol("builtin", "") { CName = "builtin" };
        Print = Make("print", BuiltinFunction.Print, PrimitiveType.Void, ("text", PrimitiveType.String));
        Panic = Make("panic", BuiltinFunction.Panic, PrimitiveType.Never, ("message", PrimitiveType.String));
        Todo = Make("todo", BuiltinFunction.Todo, PrimitiveType.Never, ("message", PrimitiveType.String));
        ArenaNew = Make("Arena", BuiltinFunction.ArenaNew, BuiltinType.Arena);
        StrBufNew = Make("StrBuf", BuiltinFunction.StrBufNew, BuiltinType.StrBuf, ("buffer", new SpanType(PrimitiveType.U8)));
        StrBufGrow = Make("StrBuf", BuiltinFunction.StrBufGrow, BuiltinType.StrBuf);
        // The value's type varies: the binder types each call (see `BindOwned`).
        OwnedNew = Make("Owned", BuiltinFunction.OwnedNew, ErrorType.Instance);
        UntrustedNew = Make("Untrusted", BuiltinFunction.UntrustedNew, ErrorType.Instance);
        Trust = Make("trust", BuiltinFunction.Trust, ErrorType.Instance);

        // `IoError` is declared from source so its variants carry docs and locations like any other set.
        var source = SourceText.FromString("<builtin>", IoErrorSource);
        var decl = (ErrorEnumDecl)SyntaxTree.Parse(source).Root.Members[0];
        IoError = new ErrorSetSymbol("IoError", new Location(source, decl.Name.Span), Module, Accessibility.Public, decl);
        IoReadFile = MakeIo("readFile", BuiltinFunction.IoReadFile, new ListType(PrimitiveType.U8), ("path", PrimitiveType.String));
        IoReadText = MakeIo("readText", BuiltinFunction.IoReadText, PrimitiveType.String, ("path", PrimitiveType.String));
        IoWriteFile = MakeIo("writeFile", BuiltinFunction.IoWriteFile, PrimitiveType.Void,
            ("path", PrimitiveType.String), ("bytes", new SpanType(PrimitiveType.U8, readOnly: true)));
        IoListDir = MakeIo("listDir", BuiltinFunction.IoListDir, new ListType(PrimitiveType.String), ("path", PrimitiveType.String));
        IoRun = MakeIo("run", BuiltinFunction.IoRun, BuiltinType.ProcessResult,
            ("program", PrimitiveType.String), ("args", new SpanType(PrimitiveType.String, readOnly: true)));
        IoRunBatch = MakeIo("runBatch", BuiltinFunction.IoRunBatch, BuiltinType.ProcessResult,
            ("path", PrimitiveType.String), ("args", new SpanType(PrimitiveType.String, readOnly: true)));
        IoMonotonicNanos = Make("monotonicNanos", BuiltinFunction.IoMonotonicNanos, PrimitiveType.I64);
        IoStdout = Make("stdout", BuiltinFunction.IoStdout, BuiltinType.IoStream);
        IoStderr = Make("stderr", BuiltinFunction.IoStderr, BuiltinType.IoStream);
        IoStdin = Make("stdin", BuiltinFunction.IoStdin, BuiltinType.IoInput);

        var utf8Source = SourceText.FromString("<builtin>", Utf8ErrorSource);
        var utf8Decl = (ErrorEnumDecl)SyntaxTree.Parse(utf8Source).Root.Members[0];
        Utf8Error = new ErrorSetSymbol("Utf8Error", new Location(utf8Source, utf8Decl.Name.Span), Module, Accessibility.Public, utf8Decl);
        var validationSource = SourceText.FromString("<builtin>", ValidationErrorSource);
        var validationDecl = (ErrorEnumDecl)SyntaxTree.Parse(validationSource).Root.Members[0];
        ValidationError = new ErrorSetSymbol("ValidationError", new Location(validationSource, validationDecl.Name.Span), Module, Accessibility.Public, validationDecl);
        var bytes = ("bytes", (TypeSymbol)new SpanType(PrimitiveType.U8, readOnly: true));
        StringFrom = Make("from", BuiltinFunction.StringFrom, PrimitiveType.String, bytes);
        StringFrom.Fails = true;
        StringFrom.ErrorSets.Add(Utf8Error);
        StringTryFrom = Make("tryFrom", BuiltinFunction.StringTryFrom, new OptionalType(PrimitiveType.String), bytes);
        StringInvalidUtf8At = Make("invalidUtf8At", BuiltinFunction.StringInvalidUtf8At, new OptionalType(PrimitiveType.I64), bytes);

        // The iteration interfaces are declared from source too; `Compilation` declares their members like a user interface's.
        var iterationFile = new FileContext(Module, SyntaxTree.Parse(SourceText.FromString("<builtin>", IterationSource)));
        InterfaceSymbol MakeInterface(int index)
        {
            var d = (InterfaceDecl)iterationFile.Tree.Root.Members[index];
            return new InterfaceSymbol(d.Name.Text, new Location(iterationFile.Source, d.Name.Span), Module, Accessibility.Public, iterationFile) { Decl = d };
        }
        IEnumerator = MakeInterface(0);
        IIndexable = MakeInterface(1);
    }

    private const string IterationSource = """
        public interface IEnumerator {
            type Item;
            bool moveNext(ref this);
            Item current { get; }
        }

        public interface IIndexable {
            type Item;
            i64 count { get; }
            Item get(i64 index);
        }
        """;

    /// <summary><c>interface IEnumerator</c>: <c>for x in e</c> calls <c>moveNext()</c> until false and reads <c>current</c> after each true.</summary>
    public InterfaceSymbol IEnumerator { get; }

    /// <summary><c>interface IIndexable</c>: <c>for x in e</c> visits <c>get(0)</c> … <c>get(count - 1)</c>, reading <c>count</c> once.</summary>
    public InterfaceSymbol IIndexable { get; }

    /// <summary>A built-in interface, or null.</summary>
    public InterfaceSymbol? FindInterface(string name) => name switch
    {
        "IEnumerator" => IEnumerator,
        "IIndexable" => IIndexable,
        _ => null,
    };

    private const string Utf8ErrorSource = """
        public union error Utf8Error {
            /// The bytes are not well-formed UTF-8.
            invalidSequence,
        }
        """;

    /// <summary><c>union error Utf8Error</c>: numbered right after <c>IoError</c>, so <c>invalidSequence</c> is code 8.</summary>
    public ErrorSetSymbol Utf8Error { get; }

    // Error cases have no payload yet, so `invalid` carries no `List<Violation<T>>` (Q220).
    private const string ValidationErrorSource = """
        public union error ValidationError {
            /// The value breaks a `requires` rule of its type.
            invalid,
        }
        """;

    /// <summary>
    /// <c>union error ValidationError&lt;T&gt;</c>, what building a type with <c>requires</c> fails with; one set for every
    /// <c>T</c>, numbered after the user's sets so their codes still start at 9.
    /// </summary>
    public ErrorSetSymbol ValidationError { get; }

    /// <summary><c>string string.from(ReadOnlySpan&lt;u8&gt; bytes) fails Utf8Error</c>: a copy from <c>context.allocator</c>.</summary>
    public FunctionSymbol StringFrom { get; }

    /// <summary><c>string? string.tryFrom(ReadOnlySpan&lt;u8&gt; bytes)</c>: as <c>string.from</c>, null on invalid bytes.</summary>
    public FunctionSymbol StringTryFrom { get; }

    /// <summary><c>i64? string.invalidUtf8At(ReadOnlySpan&lt;u8&gt; bytes)</c>: the byte offset of the first invalid sequence, null when valid.</summary>
    public FunctionSymbol StringInvalidUtf8At { get; }

    /// <summary>A static member of <c>string</c>, or null.</summary>
    public FunctionSymbol? FindString(string name) => name switch
    {
        "from" => StringFrom,
        "tryFrom" => StringTryFrom,
        "invalidUtf8At" => StringInvalidUtf8At,
        _ => null,
    };

    private const string IoErrorSource = """
        public union error IoError {
            /// The file does not exist.
            notFound,
            /// Permission denied.
            permissionDenied,
            /// The file is not valid UTF-8.
            invalidUtf8,
            /// The path exists but is not a directory.
            notADirectory,
            /// An input or output operation failed.
            other,
            /// An argument cannot be passed exactly, so nothing was done.
            invalidInput,
            /// The operation is not available on this platform.
            unsupported,
        }
        """;

    /// <summary><c>union error IoError</c>: its variants are numbered first, so their codes are 1 to 7 (the runtime returns them).</summary>
    public ErrorSetSymbol IoError { get; }

    /// <summary><c>List&lt;u8&gt; Io.readFile(string path) fails IoError</c>.</summary>
    public FunctionSymbol IoReadFile { get; }

    /// <summary><c>string Io.readText(string path) fails IoError</c>.</summary>
    public FunctionSymbol IoReadText { get; }

    /// <summary><c>void Io.writeFile(string path, ReadOnlySpan&lt;u8&gt; bytes) fails IoError</c>.</summary>
    public FunctionSymbol IoWriteFile { get; }

    /// <summary><c>List&lt;string&gt; Io.listDir(string path) fails IoError</c>: entry names sorted by byte order.</summary>
    public FunctionSymbol IoListDir { get; }

    /// <summary><c>ProcessResult Io.run(string program, ReadOnlySpan&lt;string&gt; args) fails IoError</c>.</summary>
    public FunctionSymbol IoRun { get; }

    /// <summary><c>i64 Io.monotonicNanos()</c>.</summary>
    public FunctionSymbol IoMonotonicNanos { get; }

    /// <summary><c>Io.stdout</c> and <c>Io.stderr</c>: streams with <c>write(string)</c>.</summary>
    public FunctionSymbol IoStdout { get; }

    public FunctionSymbol IoStderr { get; }

    /// <summary><c>Io.stdin</c>: a stream with <c>Result&lt;string, IoError&gt; readAll()</c>.</summary>
    public FunctionSymbol IoStdin { get; }

    /// <summary><c>ProcessResult Io.runBatch(string path, ReadOnlySpan&lt;string&gt; args) fails IoError</c>: a <c>.bat</c>/<c>.cmd</c> file through <c>cmd.exe</c>.</summary>
    public FunctionSymbol IoRunBatch { get; }

    /// <summary>A static member of <c>Io</c>, or null.</summary>
    public FunctionSymbol? FindIo(string name) => name switch
    {
        "readFile" => IoReadFile,
        "readText" => IoReadText,
        "writeFile" => IoWriteFile,
        "listDir" => IoListDir,
        "run" => IoRun,
        "runBatch" => IoRunBatch,
        "monotonicNanos" => IoMonotonicNanos,
        "stdout" => IoStdout,
        "stderr" => IoStderr,
        "stdin" => IoStdin,
        _ => null,
    };

    /// <summary><c>Arena()</c>: a new region allocator.</summary>
    public FunctionSymbol ArenaNew { get; }

    /// <summary><c>StrBuf(Span&lt;u8&gt; buffer)</c>: text output into caller-provided bytes.</summary>
    public FunctionSymbol StrBufNew { get; }

    /// <summary><c>StrBuf()</c>: text output that grows from <c>context.allocator</c>.</summary>
    public FunctionSymbol StrBufGrow { get; }

    /// <summary><c>Owned(T value)</c>: copies the value into <c>context.allocator</c>.</summary>
    public FunctionSymbol OwnedNew { get; }

    /// <summary><c>Untrusted(T value)</c>: marks the value as coming from outside the program.</summary>
    public FunctionSymbol UntrustedNew { get; }

    /// <summary><c>trust(Untrusted&lt;T&gt; value, string reason)</c>: clears the mark.</summary>
    public FunctionSymbol Trust { get; }

    public ModuleSymbol Module { get; }

    /// <summary><c>print(string)</c>: writes the text and a newline to standard output.</summary>
    public FunctionSymbol Print { get; }

    /// <summary><c>panic(string)</c>: aborts with a message and location.</summary>
    public FunctionSymbol Panic { get; }

    /// <summary><c>todo(string)</c>: stops with <c>pending: message</c> and exit code 4; a test that calls it is pending.</summary>
    public FunctionSymbol Todo { get; }

    public FunctionSymbol? Find(string name) => name switch
    {
        "print" => Print,
        "panic" => Panic,
        "todo" => Todo,
        "Owned" => OwnedNew,
        "Untrusted" => UntrustedNew,
        "trust" => Trust,
        _ => null,
    };

    private FunctionSymbol Make(string name, BuiltinFunction kind, TypeSymbol returns, params (string Name, TypeSymbol Type)[] parameters)
    {
        var fn = new FunctionSymbol(name, null, Module, FunctionKind.Builtin)
        {
            Builtin = kind, ReturnType = returns, Access = Accessibility.Public,
        };
        foreach (var (n, t) in parameters)
            fn.Parameters.Add(new ParameterSymbol(n, null, t, Syntax.Ast.ParamModifier.None, fn.Parameters.Count));
        return fn;
    }

    private FunctionSymbol MakeIo(string name, BuiltinFunction kind, TypeSymbol returns, params (string Name, TypeSymbol Type)[] parameters)
    {
        var fn = Make(name, kind, returns, parameters);
        fn.Fails = true;
        fn.ErrorSets.Add(IoError);
        return fn;
    }
}
