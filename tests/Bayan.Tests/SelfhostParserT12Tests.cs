using System.Diagnostics;
using Bayan.Backend.C;
using Bayan.Cli;
using Bayan.Semantics;
using Bayan.Syntax.Text;
using Xunit.Abstractions;

namespace Bayan.Tests;

/// <summary>T12: the S-expression dump (<c>dump2.byn</c>) and the span walker (<c>spans.byn</c>),
/// exercised in isolation with a stand-in <c>main</c>, before the parsing tasks merge.</summary>
public sealed class SelfhostParserT12Tests(ITestOutputHelper output)
{
    private static string SelfhostDir => Path.Combine(Golden.RepoRoot, "selfhost");

    private static string ParserDir => Path.Combine(SelfhostDir, "parser");

    /// <summary>Builds the parser files plus <paramref name="main"/>, or the compiler's complaint.</summary>
    private static (string? Path, string Error) BuildParser(string main)
    {
        Environment.SetEnvironmentVariable("BAYAN_CACHE", Path.Combine(Golden.RepoRoot, "tmp", "cache"));
        var sources = Directory.GetFiles(ParserDir, "*.byn").Where(f => Path.GetFileName(f) != "main.byn")
            .Order(StringComparer.Ordinal).Select(f => SourceText.FromFile(f))
            .Append(SourceText.FromString(Path.Combine(ParserDir, "t12-test.byn"), main)).ToList();
        var c = Compilation.Create(sources, packageRoot: SelfhostDir);
        if (c.Diagnostics.HasErrors) return (null, Bayan.Syntax.Diagnostics.DiagnosticRenderer.RenderAll(c.Diagnostics.Items, includePath: true));
        var build = Toolchain.Build(CliApp.EmitC(c, BuildMode.Fast), BuildMode.Fast);
        return build.Succeeded ? (build.ExecutablePath, "") : (null, build.Errors);
    }

    private static (int Exit, string Out, string Err) Run(string program)
    {
        var psi = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, stdout.Result, stderr.Result);
    }

    private string BuildAndRun(string main)
    {
        var (path, error) = BuildParser(main);
        if (path is null)
        {
            var dump = Path.Combine(Golden.RepoRoot, "tmp", "t12-build-error.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(dump)!);
            File.WriteAllText(dump, error);
            Assert.Fail($"selfhost/parser did not build (full error at {dump}):\n{error}");
        }
        var (exit, stdout, stderr) = Run(path!);
        output.WriteLine(stderr);
        return stdout;
    }

    /// <summary>
    /// Builds the module over the source <c>a+b</c> and dumps a binary expression, a named type and a
    /// let-pattern, then runs the span walker over a good tree and a tree with an out-of-range child.
    /// </summary>
    [Fact]
    public void DumpExprTypePatternAndCheckSpans()
    {
        const string main = """
            import lexer
            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    var bytes = List<u8>()
                    bytes.append(0x61)
                    bytes.append(0x2B)
                    bytes.append(0x62)
                    var p = Parser.create(lexer.lex(SourceText.create("<test>", from: move bytes), by: Keywords.build()))
                    let left = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 0, length: 1 }, typeArgs: List<TypeSyntax>() }))
                    let right = Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 2, length: 1 }, name: TextSpan { start: 2, length: 1 }, typeArgs: List<TypeSyntax>() }))
                    let bin = Expr.binaryExpr(Owned(BinaryExpr { span: TextSpan { start: 0, length: 3 }, op: .add, left: move left, right: move right }))
                    var sb = StrBuf()
                    dumpExpr(in bin, from: ref p, to: ref sb)
                    print($"expr {sb.finish()}")

                    var path = List<TextSpan>()
                    path.append(TextSpan { start: 0, length: 1 })
                    let ty = TypeSyntax.namedType(Owned(NamedType { span: TextSpan { start: 0, length: 1 }, path: move path, args: List<TypeSyntax>() }))
                    var tb = StrBuf()
                    dumpType(in ty, from: ref p, to: ref tb)
                    print($"type {tb.finish()}")

                    let pat = Pattern.varPattern(Owned(VarPattern { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 0, length: 1 }, isLet: true }))
                    var pb = StrBuf()
                    dumpPattern(in pat, from: ref p, to: ref pb)
                    print($"pattern {pb.finish()}")

                    var members = List<Decl>()
                    members.append(Decl.globalStatement(Owned(GlobalStatement {
                        span: TextSpan { start: 0, length: 3 },
                        statement: Stmt.exprStmt(Owned(ExprStmt { span: TextSpan { start: 0, length: 3 }, expr: move bin })),
                    })))
                    let unit = CompilationUnit { span: TextSpan { start: 0, length: 3 }, members: move members }
                    var cb = StrBuf()
                    checkSpans(in unit, until: 3, to: ref cb)
                    print($"spans [{cb.finish()}]")

                    var bad = List<Decl>()
                    var badPath = List<TextSpan>()
                    badPath.append(TextSpan { start: 12, length: 3 })
                    bad.append(Decl.importDecl(Owned(ImportDecl { span: TextSpan { start: 0, length: 10 }, path: move badPath, alias: null })))
                    let badUnit = CompilationUnit { span: TextSpan { start: 0, length: 15 }, members: move bad }
                    var bb = StrBuf()
                    checkSpans(in badUnit, until: 15, to: ref bb)
                    print($"bad [{bb.finish()}]")

                    var big = List<Decl>()
                    big.append(Decl.importDecl(Owned(ImportDecl { span: TextSpan { start: 0, length: 10 }, path: List<TextSpan>(), alias: null })))
                    let bigUnit = CompilationUnit { span: TextSpan { start: 0, length: 10 }, members: move big }
                    var bc = StrBuf()
                    checkSpans(in bigUnit, until: 3, to: ref bc)
                    print($"big [{bc.finish()}]")
                }
            }
            """;
        var stdout = BuildAndRun(main);
        Assert.Equal(
            """
            expr (+ a b)
            type a
            pattern (let a)
            spans []
            bad [span out of range: importDecl path 12+3 outside 0+10
            ]
            big [span out of range: compilationUnit 0+10
            span out of range: importDecl 0+10
            ]

            """, stdout);
    }

    /// <summary>
    /// Dumps one node of every <c>Expr</c>, <c>TypeSyntax</c> and <c>Pattern</c> variant and checks
    /// the spelling against the C# <c>AstDump</c>. The remaining span-walker node kinds are covered
    /// by <c>DumpExprTypePatternAndCheckSpans</c>. End-to-end byte-identity against the parse goldens
    /// is T15's, since the parsing tasks T3–T10 are still stubs.
    /// </summary>
    [Fact]
    public void DumpCoversEveryExprTypeAndPatternCase()
    {
        const string main = """
            import lexer
            Expr mkName(i64 pos) => Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: pos, length: 1 }, name: TextSpan { start: pos, length: 1 }, typeArgs: List<TypeSyntax>() }))
            Expr mkInt(i64 v) => Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 0, length: 1 }, kind: .integer, text: $"{v}", suffix: null }))
            TypeSyntax mkNamed(i64 pos) => TypeSyntax.namedType(Owned(NamedType { span: TextSpan { start: pos, length: 1 }, path: [TextSpan { start: pos, length: 1 }], args: List<TypeSyntax>() }))
            Pattern mkDiscard() => Pattern.discardPattern(Owned(DiscardPattern { span: TextSpan { start: 0, length: 1 } }))
            string exprStr(in Expr e, from ref Parser p) {
                var s = StrBuf()
                dumpExpr(in e, from: ref p, to: ref s)
                return $"{s.finish()}"
            }
            string typeStr(in TypeSyntax t, from ref Parser p) {
                var s = StrBuf()
                dumpType(in t, from: ref p, to: ref s)
                return $"{s.finish()}"
            }
            string patternStr(in Pattern pat, from ref Parser p) {
                var s = StrBuf()
                dumpPattern(in pat, from: ref p, to: ref s)
                return $"{s.finish()}"
            }
            List<u8> bytesOf(string text) {
                var result = List<u8>()
                for i in 0..text.length { result.append(text.bytes[i]) }
                return result
            }
            void main() {
                using let arena = Arena()
                provide allocator: arena {
                    var p = Parser.create(lexer.lex(SourceText.create("<t>", from: bytesOf("abcdfxytTm")), by: Keywords.build()))
                    print(exprStr(in mkName(5), from: ref p))
                    print(exprStr(in mkInt(1), from: ref p))
                    print(exprStr(in Expr.nameExpr(Owned(NameExpr { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 5, length: 1 }, typeArgs: [mkNamed(8)] })), from: ref p))
                    print(exprStr(in Expr.thisExpr(Owned(ThisExpr { span: TextSpan { start: 0, length: 1 } })), from: ref p))
                    print(exprStr(in Expr.memberExpr(Owned(MemberExpr { span: TextSpan { start: 0, length: 1 }, target: mkName(5), member: TextSpan { start: 9, length: 1 }, nullConditional: false, typeArgs: List<TypeSyntax>() })), from: ref p))
                    print(exprStr(in Expr.memberExpr(Owned(MemberExpr { span: TextSpan { start: 0, length: 1 }, target: mkName(5), member: TextSpan { start: 9, length: 1 }, nullConditional: true, typeArgs: List<TypeSyntax>() })), from: ref p))
                    print(exprStr(in Expr.targetMemberExpr(Owned(TargetMemberExpr { span: TextSpan { start: 0, length: 1 }, member: TextSpan { start: 9, length: 1 } })), from: ref p))
                    print(exprStr(in Expr.callExpr(Owned(CallExpr { span: TextSpan { start: 0, length: 1 }, callee: mkName(4), args: List<Argument>(), becauseKeyword: null })), from: ref p))
                    print(exprStr(in Expr.callExpr(Owned(CallExpr { span: TextSpan { start: 0, length: 1 }, callee: mkName(4), args: [Argument { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 6, length: 1 }, modifier: .none, value: mkInt(2) }, Argument { span: TextSpan { start: 0, length: 1 }, name: null, modifier: .refKeyword, value: mkName(5) }], becauseKeyword: null })), from: ref p))
                    print(exprStr(in Expr.callExpr(Owned(CallExpr { span: TextSpan { start: 0, length: 1 }, callee: mkName(4), args: List<Argument>(), becauseKeyword: BecauseClause { span: TextSpan { start: 0, length: 1 }, reason: "r" } })), from: ref p))
                    print(exprStr(in Expr.indexExpr(Owned(IndexExpr { span: TextSpan { start: 0, length: 1 }, target: mkName(5), index: mkInt(1) })), from: ref p))
                    print(exprStr(in Expr.unaryExpr(Owned(UnaryExpr { span: TextSpan { start: 0, length: 1 }, op: .negate, operand: mkName(5) })), from: ref p))
                    print(exprStr(in Expr.binaryExpr(Owned(BinaryExpr { span: TextSpan { start: 0, length: 1 }, op: .add, left: mkName(5), right: mkName(6) })), from: ref p))
                    print(exprStr(in Expr.rangeExpr(Owned(RangeExpr { span: TextSpan { start: 0, length: 1 }, start: .none, end: .none, inclusive: false })), from: ref p))
                    print(exprStr(in Expr.rangeExpr(Owned(RangeExpr { span: TextSpan { start: 0, length: 1 }, start: mkName(5), end: mkName(6), inclusive: true })), from: ref p))
                    print(exprStr(in Expr.conditionalExpr(Owned(ConditionalExpr { span: TextSpan { start: 0, length: 1 }, condition: mkName(5), whenTrue: mkInt(1), whenFalse: mkInt(2) })), from: ref p))
                    print(exprStr(in Expr.castExpr(Owned(CastExpr { span: TextSpan { start: 0, length: 1 }, type: mkNamed(8), operand: mkName(5) })), from: ref p))
                    print(exprStr(in Expr.isExpr(Owned(IsExpr { span: TextSpan { start: 0, length: 1 }, value: mkName(5), pattern: mkDiscard() })), from: ref p))
                    print(exprStr(in Expr.exceptExpr(Owned(ExceptExpr { span: TextSpan { start: 0, length: 1 }, value: mkName(5), pattern: mkDiscard() })), from: ref p))
                    print(exprStr(in Expr.inExpr(Owned(InExpr { span: TextSpan { start: 0, length: 1 }, value: mkName(5), range: mkName(6) })), from: ref p))
                    print(exprStr(in Expr.switchExpr(Owned(SwitchExpr { span: TextSpan { start: 0, length: 1 }, value: mkName(5), arms: [SwitchArm { span: TextSpan { start: 0, length: 1 }, pattern: mkDiscard(), guard: .none, value: mkInt(1) }] })), from: ref p))
                    print(exprStr(in Expr.tryExpr(Owned(TryExpr { span: TextSpan { start: 0, length: 1 }, operand: mkName(5) })), from: ref p))
                    print(exprStr(in Expr.moveExpr(Owned(MoveExpr { span: TextSpan { start: 0, length: 1 }, operand: mkName(5) })), from: ref p))
                    print(exprStr(in Expr.catchExpr(Owned(CatchExpr { span: TextSpan { start: 0, length: 1 }, operand: mkName(5), binding: null, variant: .none, handler: mkName(6) })), from: ref p))
                    print(exprStr(in Expr.caughtErrorExpr(Owned(CaughtErrorExpr { span: TextSpan { start: 0, length: 1 } })), from: ref p))
                    print(exprStr(in Expr.jumpExpr(Owned(JumpExpr { span: TextSpan { start: 0, length: 1 }, jump: Stmt.returnStmt(Owned(ReturnStmt { span: TextSpan { start: 0, length: 1 }, value: .none })) })), from: ref p))
                    print(exprStr(in Expr.jumpExpr(Owned(JumpExpr { span: TextSpan { start: 0, length: 1 }, jump: Stmt.returnStmt(Owned(ReturnStmt { span: TextSpan { start: 0, length: 1 }, value: mkName(5) })) })), from: ref p))
                    print(exprStr(in Expr.jumpExpr(Owned(JumpExpr { span: TextSpan { start: 0, length: 1 }, jump: Stmt.breakStmt(Owned(BreakStmt { span: TextSpan { start: 0, length: 1 }, label: null })) })), from: ref p))
                    print(exprStr(in Expr.jumpExpr(Owned(JumpExpr { span: TextSpan { start: 0, length: 1 }, jump: Stmt.continueStmt(Owned(ContinueStmt { span: TextSpan { start: 0, length: 1 }, label: TextSpan { start: 9, length: 1 } })) })), from: ref p))
                    print(exprStr(in Expr.jumpExpr(Owned(JumpExpr { span: TextSpan { start: 0, length: 1 }, jump: Stmt.failStmt(Owned(FailStmt { span: TextSpan { start: 0, length: 1 }, error: mkName(5) })) })), from: ref p))
                    print(exprStr(in Expr.blockExpr(Owned(BlockExpr { span: TextSpan { start: 0, length: 1 }, block: Stmt.blockStmt(Owned(BlockStmt { span: TextSpan { start: 0, length: 1 }, statements: List<Stmt>() })) })), from: ref p))
                    print(exprStr(in Expr.structLiteralExpr(Owned(StructLiteralExpr { span: TextSpan { start: 0, length: 1 }, type: .none, fields: List<FieldInit>() })), from: ref p))
                    print(exprStr(in Expr.withExpr(Owned(WithExpr { span: TextSpan { start: 0, length: 1 }, target: mkName(5), fields: List<FieldInit>() })), from: ref p))
                    print(exprStr(in Expr.spreadExpr(Owned(SpreadExpr { span: TextSpan { start: 0, length: 1 }, operand: mkName(5) })), from: ref p))
                    print(exprStr(in Expr.mapEntryExpr(Owned(MapEntryExpr { span: TextSpan { start: 0, length: 1 }, key: mkInt(1), value: mkInt(2) })), from: ref p))
                    print(exprStr(in Expr.collectionExpr(Owned(CollectionExpr { span: TextSpan { start: 0, length: 1 }, elements: [mkInt(1), mkInt(2)] })), from: ref p))
                    print(exprStr(in Expr.collectionExpr(Owned(CollectionExpr { span: TextSpan { start: 0, length: 1 }, elements: [Expr.mapEntryExpr(Owned(MapEntryExpr { span: TextSpan { start: 0, length: 1 }, key: mkInt(1), value: mkInt(2) }))] })), from: ref p))
                    print(exprStr(in Expr.tupleExpr(Owned(TupleExpr { span: TextSpan { start: 0, length: 1 }, elements: [TupleElement { span: TextSpan { start: 0, length: 1 }, name: null, value: mkInt(1) }, TupleElement { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 5, length: 1 }, value: mkInt(2) }] })), from: ref p))
                    print(exprStr(in Expr.parenExpr(Owned(ParenExpr { span: TextSpan { start: 0, length: 1 }, inner: mkName(5) })), from: ref p))
                    print(exprStr(in Expr.lambdaExpr(Owned(LambdaExpr { span: TextSpan { start: 0, length: 1 }, params: [TextSpan { start: 5, length: 1 }], body: mkName(5), block: .none })), from: ref p))
                    print(exprStr(in Expr.stackallocExpr(Owned(StackallocExpr { span: TextSpan { start: 0, length: 1 }, element: mkNamed(8), count: mkInt(3) })), from: ref p))
                    print(exprStr(in Expr.patternExpr(Owned(PatternExpr { span: TextSpan { start: 0, length: 1 }, pattern: mkDiscard() })), from: ref p))
                    print(exprStr(in Expr.errorExpr(Owned(ErrorExpr { span: TextSpan { start: 0, length: 1 } })), from: ref p))
                    print(exprStr(in Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 0, length: 1 }, kind: .string, text: "a\"b", suffix: null })), from: ref p))
                    print(exprStr(in Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 0, length: 1 }, kind: .cString, text: "a", suffix: null })), from: ref p))
                    print(exprStr(in Expr.literalExpr(Owned(LiteralExpr { span: TextSpan { start: 0, length: 1 }, kind: .trueKeyword, text: "true", suffix: null })), from: ref p))
                    print(exprStr(in Expr.interpolatedExpr(Owned(InterpolatedExpr { span: TextSpan { start: 0, length: 1 }, parts: [InterpolationPart.interpolationText(Owned(InterpolationText { span: TextSpan { start: 0, length: 1 }, text: "a" })), InterpolationPart.interpolationHole(Owned(InterpolationHole { span: TextSpan { start: 0, length: 1 }, expr: mkName(5), specs: List<Argument>() }))] })), from: ref p))
                    print(typeStr(in mkNamed(0), from: ref p))
                    print(typeStr(in TypeSyntax.namedType(Owned(NamedType { span: TextSpan { start: 0, length: 1 }, path: [TextSpan { start: 0, length: 1 }], args: [mkNamed(8)] })), from: ref p))
                    print(typeStr(in TypeSyntax.optionalType(Owned(OptionalType { span: TextSpan { start: 0, length: 1 }, inner: mkNamed(0) })), from: ref p))
                    print(typeStr(in TypeSyntax.arrayType(Owned(ArrayType { span: TextSpan { start: 0, length: 1 }, element: mkNamed(0), size: mkInt(3) })), from: ref p))
                    print(typeStr(in TypeSyntax.tupleType(Owned(TupleType { span: TextSpan { start: 0, length: 1 }, elements: [TupleTypeElement { span: TextSpan { start: 0, length: 1 }, type: mkNamed(0), name: null }, TupleTypeElement { span: TextSpan { start: 0, length: 1 }, type: mkNamed(1), name: TextSpan { start: 7, length: 1 } }] })), from: ref p))
                    print(typeStr(in TypeSyntax.functionTypeSyntax(Owned(FunctionTypeSyntax { span: TextSpan { start: 0, length: 1 }, params: [mkNamed(0)], returnKeyword: mkNamed(1), failsKeyword: null, usesKeyword: null })), from: ref p))
                    print(typeStr(in TypeSyntax.dynType(Owned(DynType { span: TextSpan { start: 0, length: 1 }, isRef: true, isReadOnly: true, inner: mkNamed(0) })), from: ref p))
                    print(typeStr(in TypeSyntax.refinedType(Owned(RefinedType { span: TextSpan { start: 0, length: 1 }, inner: mkNamed(0), states: [TextSpan { start: 5, length: 1 }] })), from: ref p))
                    print(typeStr(in TypeSyntax.pointerType(Owned(PointerType { span: TextSpan { start: 0, length: 1 }, pointee: mkNamed(0), readOnly: true })), from: ref p))
                    print(patternStr(in Pattern.constantPattern(Owned(ConstantPattern { span: TextSpan { start: 0, length: 1 }, value: mkInt(1) })), from: ref p))
                    print(patternStr(in Pattern.relationalPattern(Owned(RelationalPattern { span: TextSpan { start: 0, length: 1 }, op: .lt, value: mkInt(1) })), from: ref p))
                    print(patternStr(in Pattern.varPattern(Owned(VarPattern { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 5, length: 1 }, isLet: true })), from: ref p))
                    print(patternStr(in Pattern.varPattern(Owned(VarPattern { span: TextSpan { start: 0, length: 1 }, name: TextSpan { start: 5, length: 1 }, isLet: false })), from: ref p))
                    print(patternStr(in mkDiscard(), from: ref p))
                    print(patternStr(in Pattern.declarationPattern(Owned(DeclarationPattern { span: TextSpan { start: 0, length: 1 }, type: mkNamed(8), name: TextSpan { start: 7, length: 1 } })), from: ref p))
                    print(patternStr(in Pattern.notPattern(Owned(NotPattern { span: TextSpan { start: 0, length: 1 }, operand: mkDiscard() })), from: ref p))
                    print(patternStr(in Pattern.binaryPattern(Owned(BinaryPattern { span: TextSpan { start: 0, length: 1 }, isAnd: true, left: mkDiscard(), right: mkDiscard() })), from: ref p))
                    print(patternStr(in Pattern.propertyPattern(Owned(PropertyPattern { span: TextSpan { start: 0, length: 1 }, type: .none, properties: [PropertySubpattern { span: TextSpan { start: 0, length: 1 }, path: [TextSpan { start: 0, length: 1 }, TextSpan { start: 1, length: 1 }], pattern: mkDiscard() }], designation: null })), from: ref p))
                    print(patternStr(in Pattern.positionalPattern(Owned(PositionalPattern { span: TextSpan { start: 0, length: 1 }, type: .none, elements: [mkDiscard()] })), from: ref p))
                    print(patternStr(in Pattern.variantPattern(Owned(VariantPattern { span: TextSpan { start: 0, length: 1 }, variant: Expr.targetMemberExpr(Owned(TargetMemberExpr { span: TextSpan { start: 0, length: 1 }, member: TextSpan { start: 9, length: 1 } })), elements: [mkDiscard()], binding: null })), from: ref p))
                    print(patternStr(in Pattern.slicePattern(Owned(SlicePattern { span: TextSpan { start: 0, length: 1 }, inner: .none })), from: ref p))
                    print(patternStr(in Pattern.listPattern(Owned(ListPattern { span: TextSpan { start: 0, length: 1 }, elements: [mkDiscard(), mkDiscard()] })), from: ref p))
                }
            }
            """;
        var stdout = BuildAndRun(main);
        Assert.Equal(
            """
            x
            1
            x<T>
            this
            (. x m)
            (?. x m)
            .m
            (call f)
            (call f (y: 2) (ref x))
            (call f because `r`)
            (index x 1)
            (- x)
            (+ x y)
            (.. _ _)
            (..= x y)
            (? x 1 2)
            (cast T x)
            (is x _)
            (except x _)
            (in x y)
            (switch x (_ => 1))
            (try x)
            (move x)
            (catch x y)
            (caught)
            (return)
            (return x)
            (break)
            (continue m)
            (fail x)
            (block)
            (struct _)
            (with x)
            (spread x)
            (entry 1 2)
            (list 1 2)
            (map (entry 1 2))
            (tuple 1 (x: 2))
            (paren x)
            (lambda (x) x)
            (stackalloc T 3)
            (pattern _)
            <error>
            "a\"b"
            c"a"
            true
            (interp "a" x)
            a
            a<T>
            a?
            a[3]
            (a, b t)
            a -> b
            dyn ref readonly a
            a is .x
            readonly a*
            1
            (< 1)
            (let x)
            (var x)
            _
            (decl T t)
            (not _)
            (and _ _)
            (props (a.b: _))
            (pos _)
            (variant .m _)
            (slice)
            (listp _ _)

            """, stdout);
    }

}
