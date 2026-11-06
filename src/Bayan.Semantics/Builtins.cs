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
        // `Stream` is declared from source for its member names, like `LogLevel` below.
        var streamSource = SourceText.FromString("<builtin>", StreamSource);
        var streamTree = SyntaxTree.Parse(streamSource);
        var streamDecl = (EnumDecl)streamTree.Root.Members[0];
        Stream = new EnumSymbol("Stream", new Location(streamSource, streamDecl.Name.Span), Module, Accessibility.Public, streamDecl, new FileContext(Module, streamTree));
        foreach (var m in streamDecl.Members)
            Stream.Members.Add(new EnumMemberSymbol(m.Name.Text, new Location(streamSource, m.Name.Span), Stream, Stream.Members.Count, null));
        var radixSource = SourceText.FromString("<builtin>", RadixSource);
        var radixTree = SyntaxTree.Parse(radixSource);
        var radixDecl = (EnumDecl)radixTree.Root.Members[0];
        Radix = new EnumSymbol("Radix", new Location(radixSource, radixDecl.Name.Span), Module, Accessibility.Public, radixDecl, new FileContext(Module, radixTree));
        foreach (var m in radixDecl.Members)
            Radix.Members.Add(new EnumMemberSymbol(m.Name.Text, new Location(radixSource, m.Name.Span), Radix, Radix.Members.Count, null));
        var edgeSource = SourceText.FromString("<builtin>", EdgeSource);
        var edgeTree = SyntaxTree.Parse(edgeSource);
        var edgeDecl = (EnumDecl)edgeTree.Root.Members[0];
        Edge = new EnumSymbol("Edge", new Location(edgeSource, edgeDecl.Name.Span), Module, Accessibility.Public, edgeDecl, new FileContext(Module, edgeTree));
        foreach (var m in edgeDecl.Members)
            Edge.Members.Add(new EnumMemberSymbol(m.Name.Text, new Location(edgeSource, m.Name.Span), Edge, Edge.Members.Count, null));
        var alignSource = SourceText.FromString("<builtin>", AlignSource);
        var alignTree = SyntaxTree.Parse(alignSource);
        var alignDecl = (EnumDecl)alignTree.Root.Members[0];
        Align = new EnumSymbol("Align", new Location(alignSource, alignDecl.Name.Span), Module, Accessibility.Public, alignDecl, new FileContext(Module, alignTree));
        foreach (var m in alignDecl.Members)
            Align.Members.Add(new EnumMemberSymbol(m.Name.Text, new Location(alignSource, m.Name.Span), Align, Align.Members.Count, null));
        var comparisonSource = SourceText.FromString("<builtin>", ComparisonSource);
        var comparisonTree = SyntaxTree.Parse(comparisonSource);
        var comparisonDecl = (EnumDecl)comparisonTree.Root.Members[0];
        Comparison = new EnumSymbol("Comparison", new Location(comparisonSource, comparisonDecl.Name.Span), Module, Accessibility.Public, comparisonDecl,
            new FileContext(Module, comparisonTree));
        foreach (var m in comparisonDecl.Members)
            Comparison.Members.Add(new EnumMemberSymbol(m.Name.Text, new Location(comparisonSource, m.Name.Span), Comparison, Comparison.Members.Count, null));
        Print = Make("print", BuiltinFunction.Print, PrimitiveType.Void, ("text", PrimitiveType.String), ("to", Stream));
        Panic = Make("panic", BuiltinFunction.Panic, PrimitiveType.Never, ("message", PrimitiveType.String));
        Todo = Make("todo", BuiltinFunction.Todo, PrimitiveType.Never, ("message", PrimitiveType.String));

        // `LogLevel` is declared from source for its member names; it is not a type name users can write yet.
        var logSource = SourceText.FromString("<builtin>", LogLevelSource);
        var logTree = SyntaxTree.Parse(logSource);
        var logDecl = (EnumDecl)logTree.Root.Members[0];
        LogLevel = new EnumSymbol("LogLevel", new Location(logSource, logDecl.Name.Span), Module, Accessibility.Public, logDecl, new FileContext(Module, logTree));
        foreach (var m in logDecl.Members)
            LogLevel.Members.Add(new EnumMemberSymbol(m.Name.Text, new Location(logSource, m.Name.Span), LogLevel, LogLevel.Members.Count, null));
        Log = Make("log", BuiltinFunction.Log, PrimitiveType.Void, ("text", PrimitiveType.String), ("as", LogLevel));
        ArenaNew = Make("Arena", BuiltinFunction.ArenaNew, BuiltinType.Arena);
        StrBufNew = Make("StrBuf", BuiltinFunction.StrBufNew, BuiltinType.StrBuf, ("buffer", new SpanType(PrimitiveType.U8)));
        StrBufGrow = Make("StrBuf", BuiltinFunction.StrBufGrow, BuiltinType.StrBuf);
        StrBufOver = Make("StrBuf.over", BuiltinFunction.StrBufOver, BuiltinType.StrBuf, ("buffer", new SpanType(PrimitiveType.U8)));
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
        IoRemove = MakeIo("remove", BuiltinFunction.IoRemove, PrimitiveType.Void, ("path", PrimitiveType.String));
        IoCreateDirectory = MakeIo("createDirectory", BuiltinFunction.IoCreateDirectory, PrimitiveType.Void, ("path", PrimitiveType.String));
        IoRemoveDirectory = MakeIo("removeDirectory", BuiltinFunction.IoRemoveDirectory, PrimitiveType.Void, ("path", PrimitiveType.String));
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
        var ruleTree = SyntaxTree.Parse(SourceText.FromString("<builtin>", RuleSource));
        RuleFile = new FileContext(Module, ruleTree);
        var ruleDecl = (UnionDecl)ruleTree.Root.Members[0];
        Rule = new UnionSymbol("Rule", new Location(RuleFile.Source, ruleDecl.Name.Span), Module, Accessibility.Public, ruleDecl, RuleFile);
        ViolationDecl = (StructDecl)ruleTree.Root.Members[1];
        var parseSource = SourceText.FromString("<builtin>", ParseErrorSource);
        var parseDecl = (ErrorEnumDecl)SyntaxTree.Parse(parseSource).Root.Members[0];
        ParseError = new ErrorSetSymbol("ParseError", new Location(parseSource, parseDecl.Name.Span), Module, Accessibility.Public, parseDecl);
        var invalidSource = SourceText.FromString("<builtin>", Utf8InvalidSource);
        var invalidTree = SyntaxTree.Parse(invalidSource);
        var invalidDecl = (EnumDecl)invalidTree.Root.Members[0];
        Utf8Invalid = new EnumSymbol("Utf8Invalid", new Location(invalidSource, invalidDecl.Name.Span), Module, Accessibility.Public, invalidDecl, new FileContext(Module, invalidTree));
        foreach (var m in invalidDecl.Members)
            Utf8Invalid.Members.Add(new EnumMemberSymbol(m.Name.Text, new Location(invalidSource, m.Name.Span), Utf8Invalid, Utf8Invalid.Members.Count, null));
        var bytes = ("bytes", (TypeSymbol)new SpanType(PrimitiveType.U8, readOnly: true));
        Utf8Decode = Make("decode", BuiltinFunction.Utf8Decode, PrimitiveType.String, bytes);
        Utf8Decode.Fails = true;
        Utf8Decode.ErrorSets.Add(Utf8Error);
        Utf8TryDecode = Make("tryDecode", BuiltinFunction.Utf8TryDecode, new OptionalType(PrimitiveType.String), bytes);
        Utf8DecodeReplace = Make("decode", BuiltinFunction.Utf8DecodeReplace, PrimitiveType.String, bytes, ("invalid", Utf8Invalid));
        HexEncode = Make("encode", BuiltinFunction.HexEncode, PrimitiveType.String, bytes);
        HexDecode = Make("decode", BuiltinFunction.HexDecode, new ListType(PrimitiveType.U8), ("text", PrimitiveType.String));
        HexDecode.Fails = true;
        HexDecode.ErrorSets.Add(ParseError);
        SymbolIntern = Make("intern", BuiltinFunction.SymbolIntern, BuiltinType.Symbol, ("text", PrimitiveType.String));
        StringInvalidUtf8At = Make("invalidUtf8At", BuiltinFunction.StringInvalidUtf8At, new OptionalType(PrimitiveType.I64), bytes);
        PathIsRooted = Make("isRooted", BuiltinFunction.PathIsRooted, PrimitiveType.Bool, ("path", PrimitiveType.String));
        PathRelative = Make("relative", BuiltinFunction.PathRelative, PrimitiveType.String, ("from", PrimitiveType.String), ("to", PrimitiveType.String));
        PathFileName = Make("fileName", BuiltinFunction.PathFileName, PrimitiveType.String, ("path", PrimitiveType.String));
        PathDirectoryName = Make("directoryName", BuiltinFunction.PathDirectoryName, new OptionalType(PrimitiveType.String), ("path", PrimitiveType.String));
        PathCombine = Make("combine", BuiltinFunction.PathCombine, PrimitiveType.String, ("path1", PrimitiveType.String), ("path2", PrimitiveType.String));
        PathCombineSpan = Make("combine", BuiltinFunction.PathCombineSpan, PrimitiveType.String, ("paths", new SpanType(PrimitiveType.String, readOnly: true)));
        PathFullPath = Make("fullPath", BuiltinFunction.PathFullPath, PrimitiveType.String, ("path", PrimitiveType.String));
        EnvGet = Make("get", BuiltinFunction.EnvGet, new OptionalType(PrimitiveType.String), ("name", PrimitiveType.String));
        FsFileExists = Make("fileExists", BuiltinFunction.FsFileExists, PrimitiveType.Bool, ("path", PrimitiveType.String));

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
            invalidSequence(i64 at),
        }
        """;

    /// <summary><c>union error Utf8Error</c>: numbered right after <c>IoError</c>, so <c>invalidSequence</c> is code 8.</summary>
    public ErrorSetSymbol Utf8Error { get; }

    // SPEC §8 gives `badDigit(Bytes at)`; units are spec-only, so the position is a plain byte offset (Question 331).
    private const string ParseErrorSource = """
        public union error ParseError {
            /// The text has no digit, or a character that is not a digit, at this byte offset.
            badDigit(i64 at),
            /// The number does not fit the type.
            overflow,
        }
        """;

    /// <summary><c>union error ParseError</c>: what <c>T.parse(text)</c> and <c>Hex.decode</c> fail with; numbered after the user's sets.</summary>
    public ErrorSetSymbol ParseError { get; }

    private const string Utf8InvalidSource = """
        public enum Utf8Invalid {
            replace,
        }
        """;

    /// <summary><c>enum Utf8Invalid</c>: the <c>invalid:</c> argument of the lossy <c>Utf8.decode</c>; it is not a type name users can write.</summary>
    public EnumSymbol Utf8Invalid { get; }

    // The template of `ValidationError<T>`; each `T` gets its own set (`Compilation.ValidationErrorOf`), and the bare name is BYN2023.
    private const string ValidationErrorSource = """
        public union error ValidationError {
            /// The value breaks a `requires` rule of its type.
            invalid,
        }
        """;

    /// <summary>
    /// The template of <c>union error ValidationError&lt;T&gt;</c>: its name and case, never a set of its own; its cases are
    /// declared per <c>T</c>.
    /// </summary>
    public ErrorSetSymbol ValidationError { get; }

    // J23: `range` and `length` shapes are recognized, everything else is `custom`; `pattern`, `required` and `kind`
    // are spec-only (Question 337). `Violation<T>`'s field types are set per `T`.
    private const string RuleSource = """
        public union Rule {
            /// `it.runeCount in a..=b`.
            length(Range<i64> bounds)
            /// `it in a..=b`.
            range(Range<i64> bounds)
            /// Any other rule.
            custom
        }

        public struct Violation {
            i64 field
            i64 rule
        }
        """;

    /// <summary><c>union Rule</c>: the shape of a broken <c>requires</c> rule, in a <c>Violation&lt;T&gt;</c>.</summary>
    public UnionSymbol Rule { get; }

    public FileContext RuleFile { get; }

    /// <summary>The declaration each <c>Violation&lt;T&gt; { T.Field field, Rule rule }</c> is made from.</summary>
    public StructDecl ViolationDecl { get; }

    /// <summary><c>string Utf8.decode(ReadOnlySpan&lt;u8&gt; bytes) fails Utf8Error</c>: a copy from <c>context.allocator</c>.</summary>
    public FunctionSymbol Utf8Decode { get; }

    /// <summary><c>string? Utf8.tryDecode(ReadOnlySpan&lt;u8&gt; bytes)</c>: as <c>Utf8.decode</c>, null on invalid bytes.</summary>
    public FunctionSymbol Utf8TryDecode { get; }

    /// <summary><c>string Utf8.decode(ReadOnlySpan&lt;u8&gt; bytes, Utf8Invalid invalid)</c>: each invalid sequence becomes U+FFFD; never fails.</summary>
    public FunctionSymbol Utf8DecodeReplace { get; }

    /// <summary><c>i64? string.invalidUtf8At(ReadOnlySpan&lt;u8&gt; bytes)</c>: the byte offset of the first invalid sequence, null when valid.</summary>
    public FunctionSymbol StringInvalidUtf8At { get; }

    /// <summary><c>string Hex.encode(ReadOnlySpan&lt;u8&gt; bytes)</c>: two lowercase hex digits per byte, from <c>context.allocator</c>.</summary>
    public FunctionSymbol HexEncode { get; }

    /// <summary><c>List&lt;u8&gt; Hex.decode(string text) fails ParseError</c>: two hex digits, either case, per byte.</summary>
    public FunctionSymbol HexDecode { get; }

    /// <summary><c>Symbol Symbol.intern(string text)</c>: the symbol for <c>text</c>, copied into runtime-owned storage when new (spec 008 R3).</summary>
    public FunctionSymbol SymbolIntern { get; }

    /// <summary><c>bool Path.isRooted(string path)</c>: as .NET <c>Path.IsPathRooted</c> on this OS.</summary>
    public FunctionSymbol PathIsRooted { get; }

    /// <summary><c>string Path.relative(string from, string to)</c>: as .NET <c>Path.GetRelativePath</c> on this OS, a fresh copy from <c>context.allocator</c>.</summary>
    public FunctionSymbol PathRelative { get; }

    /// <summary><c>string Path.fileName(string path)</c>: as .NET <c>Path.GetFileName</c> on this OS, a fresh copy from <c>context.allocator</c>.</summary>
    public FunctionSymbol PathFileName { get; }

    /// <summary><c>string? Path.directoryName(string path)</c>: as .NET <c>Path.GetDirectoryName</c> on this OS (null for a root or an empty path), a fresh copy from <c>context.allocator</c>.</summary>
    public FunctionSymbol PathDirectoryName { get; }

    /// <summary><c>string Path.combine(string path1, string path2)</c>: as .NET <c>Path.Combine</c> on this OS (a rooted <c>path2</c> wins), a fresh copy from <c>context.allocator</c>.</summary>
    public FunctionSymbol PathCombine { get; }

    /// <summary><c>string Path.combine(ReadOnlySpan&lt;string&gt; paths)</c>: as .NET <c>Path.Combine(params string[])</c> on this OS (the last rooted part restarts the result), a fresh copy from <c>context.allocator</c>.</summary>
    public FunctionSymbol PathCombineSpan { get; }

    /// <summary><c>string Path.fullPath(string path)</c>: as .NET <c>Path.GetFullPath</c> on this OS (a relative path against the working directory), a fresh copy from <c>context.allocator</c>; panics on an empty path or a NUL byte.</summary>
    public FunctionSymbol PathFullPath { get; }

    /// <summary><c>string? Env.get(string name)</c> <c>uses env</c>: as .NET <c>Environment.GetEnvironmentVariable</c> (null when unset), a fresh copy from <c>context.allocator</c>.</summary>
    public FunctionSymbol EnvGet { get; }

    /// <summary>A static member of <c>Env</c>, or null.</summary>
    public FunctionSymbol? FindEnv(string name) => name switch
    {
        "get" => EnvGet,
        _ => null,
    };

    /// <summary><c>bool Fs.fileExists(string path)</c> <c>uses fs</c>: as .NET <c>File.Exists</c> (false for a directory, a missing entry, an empty path and a path ending in a separator).</summary>
    public FunctionSymbol FsFileExists { get; }

    /// <summary>A static member of <c>Fs</c>, or null.</summary>
    public FunctionSymbol? FindFs(string name) => name switch
    {
        "fileExists" => FsFileExists,
        _ => null,
    };

    /// <summary>A static member of <c>Path</c>, or null.</summary>
    public FunctionSymbol? FindPath(string name) => name switch
    {
        "isRooted" => PathIsRooted,
        "relative" => PathRelative,
        "fileName" => PathFileName,
        "directoryName" => PathDirectoryName,
        "combine" => PathCombine,
        "fullPath" => PathFullPath,
        _ => null,
    };

    /// <summary>A static member of <c>string</c>, or null.</summary>
    public FunctionSymbol? FindString(string name) => name switch
    {
        "invalidUtf8At" => StringInvalidUtf8At,
        _ => null,
    };

    /// <summary>A static member of <c>Utf8</c>, or null; <c>decode</c> with <c>invalid:</c> is <see cref="Utf8DecodeReplace"/>.</summary>
    public FunctionSymbol? FindUtf8(string name) => name switch
    {
        "decode" => Utf8Decode,
        "tryDecode" => Utf8TryDecode,
        _ => null,
    };

    private readonly Dictionary<PrimitiveType, FunctionSymbol> _parses = new();

    /// <summary><c>T T.parse(string text, Radix as = .decimal) fails ParseError</c> on the integer type <paramref name="t"/>.</summary>
    public FunctionSymbol IntParse(PrimitiveType t)
    {
        if (_parses.TryGetValue(t, out var fn)) return fn;
        fn = Make("parse", BuiltinFunction.IntParse, t, ("text", PrimitiveType.String), ("as", Radix));
        fn.Fails = true;
        fn.ErrorSets.Add(ParseError);
        return _parses[t] = fn;
    }

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

    /// <summary><c>void Io.remove(string path) fails IoError</c> <c>uses fs</c>: as .NET <c>File.Delete</c> (a missing file is not an error; a directory fails).</summary>
    public FunctionSymbol IoRemove { get; }

    /// <summary><c>void Io.createDirectory(string path) fails IoError</c> <c>uses fs</c>: as .NET <c>Directory.CreateDirectory</c> (creates missing parents; an existing directory is not an error, an existing file is).</summary>
    public FunctionSymbol IoCreateDirectory { get; }

    /// <summary><c>void Io.removeDirectory(string path) fails IoError</c> <c>uses fs</c>: as .NET <c>Directory.Delete(path)</c> (an empty directory only; a missing path, a file or a non-empty directory fails).</summary>
    public FunctionSymbol IoRemoveDirectory { get; }

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
        "remove" => IoRemove,
        "createDirectory" => IoCreateDirectory,
        "removeDirectory" => IoRemoveDirectory,
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

    /// <summary><c>StrBuf.over(Span&lt;u8&gt; buffer)</c>: text output into caller-provided bytes that spills to <c>context.allocator</c> when outgrown.</summary>
    public FunctionSymbol StrBufOver { get; }

    /// <summary><c>Owned(T value)</c>: copies the value into <c>context.allocator</c>.</summary>
    public FunctionSymbol OwnedNew { get; }

    /// <summary><c>Untrusted(T value)</c>: marks the value as coming from outside the program.</summary>
    public FunctionSymbol UntrustedNew { get; }

    /// <summary><c>trust(Untrusted&lt;T&gt; value, string reason)</c>: clears the mark.</summary>
    public FunctionSymbol Trust { get; }

    public ModuleSymbol Module { get; }

    /// <summary><c>print(string text, Stream to = .stdout)</c>: writes the text and a newline to standard output or standard error.</summary>
    public FunctionSymbol Print { get; }

    private const string StreamSource = """
        public enum Stream {
            stdout,
            stderr,
        }
        """;

    /// <summary><c>enum Stream</c>: where <c>print</c> writes; ordinal 0 is <c>stdout</c>, as in <c>bayan-rt.h</c>.</summary>
    public EnumSymbol Stream { get; }

    private const string RadixSource = """
        public enum Radix {
            binary,
            octal,
            decimal,
            hex,
        }
        """;

    /// <summary><c>enum Radix</c>: the base of <c>string(n, as:)</c>, in the spec's order (§6.2); <c>byn_int_string</c> reads the ordinal.</summary>
    public EnumSymbol Radix { get; }

    private const string EdgeSource = """
        public enum Edge {
            start,
            end,
        }
        """;

    /// <summary><c>enum Edge</c>: the end <c>s.trimmed(from:)</c> trims; <c>byn_str_trimmed</c> reads the ordinal (2 is both ends).</summary>
    public EnumSymbol Edge { get; }

    private const string AlignSource = """
        public enum Align {
            start,
            end,
            center,
        }
        """;

    /// <summary><c>enum Align</c>: a <c>FormatSpec</c>'s <c>align</c>, in the order of <c>byn_format_spec.align</c>.</summary>
    public EnumSymbol Align { get; }

    private const string ComparisonSource = """
        public enum Comparison {
            ordinal,
            caseless,
        }
        """;

    /// <summary><c>enum Comparison</c>: how <c>s.equals(t, as:)</c> compares; <c>.caseless</c> folds ASCII letters only until <c>std.text</c>.</summary>
    public EnumSymbol Comparison { get; }

    private const string LogLevelSource = """
        public enum LogLevel {
            debug,
            info,
            warn,
            error,
        }
        """;

    /// <summary><c>enum LogLevel</c>: the levels of <c>log</c>; ordinal 0 is <c>debug</c>, as in <c>bayan-rt.h</c>.</summary>
    public EnumSymbol LogLevel { get; }

    /// <summary><c>log(string text, LogLevel as = .info)</c>: ambient logging to standard error, not an effect (spec §14).</summary>
    public FunctionSymbol Log { get; }

    /// <summary><c>panic(string)</c>: aborts with a message and location.</summary>
    public FunctionSymbol Panic { get; }

    /// <summary><c>todo(string)</c>: stops with <c>pending: message</c> and exit code 4; a test that calls it is pending.</summary>
    public FunctionSymbol Todo { get; }

    public FunctionSymbol? Find(string name) => name switch
    {
        "print" => Print,
        "log" => Log,
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
