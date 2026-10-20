using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Parsing;

/// <summary>A parsed file: its tree plus every lexer and parser diagnostic.</summary>
public sealed record SyntaxTree(SourceText Source, CompilationUnit Root, IReadOnlyList<Diagnostic> Diagnostics)
{
    public static SyntaxTree Parse(SourceText source) => Parser.Parse(source);
}

/// <summary>
/// Recursive-descent parser with precedence climbing for binary operators. Recovers at statement
/// and declaration boundaries; a missing <c>;</c> before a new line is reported once and parsing
/// carries on as if it were there.
/// </summary>
public sealed class Parser
{
    private readonly SourceText _source;
    private readonly Token[] _tokens;
    private readonly int _count;
    private readonly DiagnosticBag _diagnostics = new();
    private int _pos;
    private int _prevEnd;
    private int _lastErrorAt = -1;

    private Parser(LexResult lexed)
    {
        _source = lexed.Source;
        _tokens = lexed.Buffer;
        _count = lexed.Count;
        _diagnostics.AddRange(lexed.Diagnostics);
    }

    public static SyntaxTree Parse(SourceText source)
    {
        var parser = new Parser(Lexer.Lex(source));
        var root = parser.ParseCompilationUnit();
        return new SyntaxTree(source, root, parser._diagnostics.Items);
    }

    // ── Token access ─────────────────────────────────────────────────────────────────

    private Token Current => _tokens[_pos];
    private TokenKind Kind => _tokens[_pos].Kind;

    private Token PeekToken(int n) => _tokens[Math.Min(_pos + n, _count - 1)];
    private TokenKind Peek(int n) => PeekToken(n).Kind;
    private TokenKind KindAt(int index) => _tokens[Math.Min(index, _count - 1)].Kind;

    private bool AtEnd => Kind == TokenKind.EndOfFile;

    private Token Advance()
    {
        var token = _tokens[_pos];
        if (token.Kind != TokenKind.EndOfFile)
        {
            _pos++;
            _prevEnd = token.End;
        }
        return token;
    }

    private bool Accept(TokenKind kind)
    {
        if (Kind != kind) return false;
        Advance();
        return true;
    }

    private string Text(Token token) => _source.GetText(token.Span);

    private TextSpan SpanFrom(int start) => TextSpan.FromBounds(start, Math.Max(start, _prevEnd));

    /// <summary>Whether the current and next token are both <c>&gt;</c> with nothing between (a shift).</summary>
    private bool AtShiftRight(int offset = 0) =>
        Peek(offset) == TokenKind.Greater && Peek(offset + 1) == TokenKind.Greater && !PeekToken(offset + 1).HasTriviaBefore;

    // ── Diagnostics ──────────────────────────────────────────────────────────────────

    private void Report(DiagnosticDescriptor descriptor, TextSpan span, object?[]? args = null,
        string? label = null, string? help = null, Fix? fix = null)
    {
        if (span.Start == _lastErrorAt) return;
        _lastErrorAt = span.Start;
        _diagnostics.Report(descriptor, _source, span, args, label, help, fix);
    }

    private int ErrorCount => _diagnostics.Items.Count;

    private string Describe(Token token) => token.Kind switch
    {
        TokenKind.EndOfFile => "end of file",
        TokenKind.Identifier => $"`{Text(token)}`",
        TokenKind.IntegerLiteral or TokenKind.FloatLiteral => $"`{Text(token)}`",
        _ when token.Kind.IsKeyword() => $"keyword `{Text(token)}`",
        TokenKind.CharLiteral => "a char literal",
        TokenKind.InterpolatedStringStart => "a string literal",
        _ when token.Kind.IsStringLiteral() => "a string literal",
        _ => $"`{token.Kind.Text()}`",
    };

    private Token Expect(TokenKind kind, string? label = null)
    {
        if (Kind == kind) return Advance();
        if (kind == TokenKind.Semicolon)
        {
            ReportMissingSemicolon();
        }
        else
        {
            Report(DiagnosticCodes.Expected, Current.Span, [$"`{kind.Text()}`", Describe(Current)], label);
        }
        return new Token(kind, Current.Start, 0, 0, 0, 0);
    }

    private void ReportMissingSemicolon()
    {
        // The token after the statement was already reported (e.g. as a bad expression); one error is enough.
        if (_lastErrorAt == Current.Start) return;
        var at = new TextSpan(_prevEnd, 0);
        Report(DiagnosticCodes.MissingSemicolon, at, label: Current.HasNewlineBefore ? "add `;` here" : $"found {Describe(Current)}",
            fix: new Fix(at, ";"));
    }

    private Name ExpectName(string what = "a name")
    {
        if (Kind == TokenKind.Identifier)
        {
            var token = Advance();
            return new Name(token.Span, Text(token));
        }
        Report(DiagnosticCodes.Expected, Current.Span, [what, Describe(Current)]);
        return new Name(new TextSpan(Current.Start, 0), "<missing>");
    }

    private Name NameOf(Token token) => new(token.Span, Text(token));

    // ── Recovery ─────────────────────────────────────────────────────────────────────

    private static bool IsStatementKeyword(TokenKind kind) => kind is
        TokenKind.LetKeyword or TokenKind.VarKeyword or TokenKind.ConstKeyword or TokenKind.IfKeyword or
        TokenKind.UnlessKeyword or TokenKind.WhileKeyword or TokenKind.DoKeyword or TokenKind.ForKeyword or
        TokenKind.ForeachKeyword or TokenKind.SwitchKeyword or TokenKind.ReturnKeyword or TokenKind.BreakKeyword or
        TokenKind.ContinueKeyword or TokenKind.DeferKeyword or TokenKind.ErrdeferKeyword or TokenKind.UsingKeyword or
        TokenKind.ContextKeyword or TokenKind.StructKeyword or TokenKind.PublicKeyword or TokenKind.PrivateKeyword or
        TokenKind.ImportKeyword or TokenKind.ErrorKeyword or TokenKind.TestKeyword or TokenKind.TypeKeyword or
        TokenKind.StaticKeyword or TokenKind.ReadonlyKeyword or TokenKind.RequiredKeyword or TokenKind.UniqueKeyword or
        TokenKind.ExternKeyword or TokenKind.UnsafeKeyword;

    /// <summary>Skips to a plausible statement start: past a <c>;</c>, before a <c>}</c>, or at a line starting a statement.</summary>
    private void Synchronize()
    {
        var depth = 0;
        var first = true;
        while (!AtEnd)
        {
            var token = Current;
            if (!first && depth == 0 && token.HasNewlineBefore && (IsStatementKeyword(token.Kind) || token.Kind == TokenKind.Identifier))
                return;
            first = false;
            switch (token.Kind)
            {
                case TokenKind.Semicolon when depth == 0:
                    Advance();
                    return;
                case TokenKind.OpenBrace:
                    depth++;
                    break;
                case TokenKind.CloseBrace:
                    if (depth == 0) return;
                    depth--;
                    if (depth == 0)
                    {
                        Advance();
                        return;
                    }
                    break;
            }
            Advance();
        }
    }

    /// <summary>After an error inside a statement, skip only if the parser is not already at a clean restart point.</summary>
    private void RecoverIfNeeded(int errorsBefore)
    {
        if (ErrorCount == errorsBefore) return;
        if (AtEnd || Kind == TokenKind.CloseBrace) return;
        if (Current.HasNewlineBefore && (IsStatementKeyword(Kind) || Kind == TokenKind.Identifier)) return;
        Synchronize();
    }

    // ── Compilation unit and declarations ───────────────────────────────────────────

    private CompilationUnit ParseCompilationUnit()
    {
        var members = new List<Decl>();
        while (!AtEnd)
        {
            var start = _pos;
            var errors = ErrorCount;
            if (Kind == TokenKind.CloseBrace)
            {
                Report(DiagnosticCodes.Expected, Current.Span, ["a declaration or statement", Describe(Current)], help: "this `}` has no matching `{`");
                Advance();
                continue;
            }
            var decl = ParseDeclaration(topLevel: true, structName: null);
            if (decl is not null) members.Add(decl);
            RecoverIfNeeded(errors);
            if (_pos == start) Advance();
        }
        return new CompilationUnit(new TextSpan(0, _source.Length), members.ToArray());
    }

    private string? DocOf(Token token) =>
        token.HasDoc ? LiteralDecoder.DecodeDoc(_source.GetText(token.Doc)) : null;

    private Modifiers ParseModifiers()
    {
        var modifiers = Modifiers.None;
        while (true)
        {
            var flag = Kind switch
            {
                TokenKind.PublicKeyword => Modifiers.Public,
                TokenKind.PrivateKeyword => Modifiers.Private,
                TokenKind.StaticKeyword => Modifiers.Static,
                TokenKind.ReadonlyKeyword => Modifiers.Readonly,
                TokenKind.RequiredKeyword => Modifiers.Required,
                TokenKind.UniqueKeyword => Modifiers.Unique,
                TokenKind.ExternKeyword => Modifiers.Extern,
                TokenKind.UnsafeKeyword => Modifiers.Unsafe,
                // `const` starts a statement at module level unless it modifies a declaration.
                TokenKind.ConstKeyword when IsTypeThenName(_pos + 1) && KindAt(ScanType(_pos + 1) + 1) is not TokenKind.Equals and not TokenKind.Semicolon
                    => Modifiers.Const,
                _ => Modifiers.None,
            };
            if (flag == Modifiers.None) return modifiers;
            if ((modifiers & flag) != 0)
                Report(DiagnosticCodes.ModifierNotAllowed, Current.Span, [Text(Current)], help: "the modifier is repeated");
            modifiers |= flag;
            Advance();
        }
    }

    private void RejectModifiers(Modifiers modifiers, Modifiers allowed, int start)
    {
        var bad = modifiers & ~allowed;
        if (bad == Modifiers.None) return;
        // Point at the first offending keyword.
        for (var i = start; i < _pos; i++)
        {
            var flag = _tokens[i].Kind switch
            {
                TokenKind.PublicKeyword => Modifiers.Public,
                TokenKind.PrivateKeyword => Modifiers.Private,
                TokenKind.StaticKeyword => Modifiers.Static,
                TokenKind.ReadonlyKeyword => Modifiers.Readonly,
                TokenKind.RequiredKeyword => Modifiers.Required,
                TokenKind.ConstKeyword => Modifiers.Const,
                TokenKind.UniqueKeyword => Modifiers.Unique,
                TokenKind.ExternKeyword => Modifiers.Extern,
                TokenKind.UnsafeKeyword => Modifiers.Unsafe,
                _ => Modifiers.None,
            };
            if ((bad & flag) != 0)
            {
                Report(DiagnosticCodes.ModifierNotAllowed, _tokens[i].Span, [Text(_tokens[i])]);
                return;
            }
        }
    }

    private Decl? ParseDeclaration(bool topLevel, string? structName)
    {
        var first = Current;
        var doc = DocOf(first);
        var startIndex = _pos;
        var start = first.Start;
        var attributes = ParseAttributes();
        doc ??= DocOf(Current);
        var modifiers = ParseModifiers();

        Decl? decl;
        switch (Kind)
        {
            case TokenKind.ImportKeyword when topLevel:
                RejectModifiers(modifiers, Modifiers.None, startIndex);
                decl = ParseImport(start);
                break;
            case TokenKind.StructKeyword:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private | Modifiers.Readonly | Modifiers.Unique, startIndex);
                decl = ParseStruct(start, modifiers);
                break;
            case TokenKind.ErrorKeyword when Peek(1) == TokenKind.EnumKeyword:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                decl = ParseErrorEnum(start, modifiers);
                break;
            case TokenKind.TypeKeyword when Peek(1) == TokenKind.Identifier && Peek(2) == TokenKind.Equals:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                decl = ParseTypeAlias(start, modifiers);
                break;
            case TokenKind.TestKeyword when topLevel && modifiers == Modifiers.None:
                decl = ParseTest(start);
                break;
            default:
                if (structName is not null && Kind == TokenKind.Identifier && Text(Current) == structName && Peek(1) == TokenKind.OpenParen)
                {
                    RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                    decl = ParseConstructor(start, modifiers);
                }
                else if (IsFunctionStart())
                {
                    RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private | Modifiers.Static | Modifiers.Unsafe, startIndex);
                    decl = ParseFunction(start, modifiers);
                }
                else if (structName is not null)
                {
                    decl = ParseFieldOrProperty(start, modifiers);
                }
                else if (modifiers != Modifiers.None)
                {
                    Report(DiagnosticCodes.Expected, Current.Span, ["a declaration after the modifiers", Describe(Current)]);
                    decl = null;
                }
                else
                {
                    var statement = ParseStatement();
                    decl = new GlobalStatement(statement.Span, statement);
                }
                break;
        }
        return decl is null ? null : decl with { Doc = doc, Attributes = attributes };
    }

    private bool IsAttributeStart() =>
        Kind == TokenKind.OpenBracket && Peek(1) == TokenKind.Identifier && Peek(2) is TokenKind.OpenParen or TokenKind.CloseBracket;

    /// <summary><c>[Name]</c> or <c>[Name(args)]</c>, repeated; the names are checked by the compiler.</summary>
    private AttributeSyntax[] ParseAttributes()
    {
        if (!IsAttributeStart()) return [];
        var attributes = new List<AttributeSyntax>();
        while (IsAttributeStart())
        {
            var start = Current.Start;
            Advance();
            var name = ExpectName("an attribute name");
            var args = Kind == TokenKind.OpenParen ? ParseArguments() : [];
            Expect(TokenKind.CloseBracket);
            attributes.Add(new AttributeSyntax(SpanFrom(start), name, args));
        }
        return attributes.ToArray();
    }

    private bool IsTypeThenName(int index)
    {
        var after = ScanType(index);
        return after > 0 && KindAt(after) == TokenKind.Identifier;
    }

    private bool IsFunctionStart()
    {
        var after = ScanType(_pos);
        return after > 0 && KindAt(after) == TokenKind.Identifier && KindAt(after + 1) == TokenKind.OpenParen;
    }

    private ImportDecl ParseImport(int start)
    {
        Advance();
        var path = new List<Name> { ExpectName("a module name") };
        while (Accept(TokenKind.Dot)) path.Add(ExpectName("a module name"));
        Name? alias = null;
        if (Accept(TokenKind.AsKeyword)) alias = ExpectName("an alias");
        Expect(TokenKind.Semicolon);
        return new ImportDecl(SpanFrom(start), path.ToArray(), alias);
    }

    private StructDecl ParseStruct(int start, Modifiers modifiers)
    {
        Advance();
        var name = ExpectName("a struct name");
        var open = Expect(TokenKind.OpenBrace);
        var members = new List<Decl>();
        while (Kind != TokenKind.CloseBrace && !AtEnd)
        {
            var before = _pos;
            var errors = ErrorCount;
            var member = ParseDeclaration(topLevel: false, structName: name.Text);
            if (member is not null) members.Add(member);
            RecoverIfNeeded(errors);
            if (_pos == before) Advance();
        }
        ExpectClose(open, "struct");
        return new StructDecl(SpanFrom(start), modifiers, name, members.ToArray());
    }

    private void ExpectClose(Token open, string what)
    {
        if (Accept(TokenKind.CloseBrace)) return;
        var (line, _) = _source.GetLinePosition(open.Start);
        Report(DiagnosticCodes.Expected, Current.Span, ["`}`", Describe(Current)],
            help: open.Length > 0 ? $"the {what} opened on line {line} is not closed" : null);
    }

    private Decl ParseFieldOrProperty(int start, Modifiers modifiers)
    {
        if (!IsTypeThenName(_pos))
        {
            Report(DiagnosticCodes.ExpectedMember, Current.Span, [Describe(Current)]);
            Synchronize();
            return new GlobalStatement(SpanFrom(start), new ErrorStmt(SpanFrom(start)));
        }
        var type = ParseType();
        var name = ExpectName();
        if (Accept(TokenKind.FatArrow))
        {
            var body = ParseExpression();
            Expect(TokenKind.Semicolon);
            return new PropertyDecl(SpanFrom(start), modifiers, type, name, body);
        }
        Expr? init = null;
        if (Accept(TokenKind.Equals)) init = ParseExpression();
        Expect(TokenKind.Semicolon);
        return new FieldDecl(SpanFrom(start), modifiers, type, name, init);
    }

    private ConstructorDecl ParseConstructor(int start, Modifiers modifiers)
    {
        var name = NameOf(Advance());
        var parameters = ParseParams();
        var body = ParseBlock();
        return new ConstructorDecl(SpanFrom(start), modifiers, name, parameters, body);
    }

    private FunctionDecl ParseFunction(int start, Modifiers modifiers)
    {
        var returnType = ParseType();
        var name = ExpectName();
        var parameters = ParseParams();
        FailsClause? fails = null;
        if (Kind == TokenKind.FailsKeyword)
        {
            var failsStart = Advance().Start;
            var errors = new List<TypeSyntax>();
            if (Kind == TokenKind.Identifier)
            {
                errors.Add(ParseType());
                while (Accept(TokenKind.Comma)) errors.Add(ParseType());
            }
            fails = new FailsClause(SpanFrom(failsStart), errors.ToArray());
        }
        BlockStmt? body = null;
        Expr? expressionBody = null;
        if (Kind == TokenKind.OpenBrace)
        {
            body = ParseBlock();
        }
        else if (Accept(TokenKind.FatArrow))
        {
            expressionBody = ParseExpression();
            Expect(TokenKind.Semicolon);
        }
        else
        {
            Expect(TokenKind.OpenBrace);
        }
        return new FunctionDecl(SpanFrom(start), modifiers, returnType, name, parameters, fails, body, expressionBody);
    }

    private Param[] ParseParams()
    {
        Expect(TokenKind.OpenParen);
        var list = new List<Param>();
        if (Kind != TokenKind.CloseParen)
        {
            do
            {
                var start = Current.Start;
                var modifier = Kind switch
                {
                    TokenKind.RefKeyword => ParamModifier.Ref,
                    TokenKind.MoveKeyword => ParamModifier.Move,
                    TokenKind.OutKeyword => ParamModifier.Out,
                    TokenKind.InKeyword => ParamModifier.In,
                    _ => ParamModifier.None,
                };
                if (modifier != ParamModifier.None) Advance();
                var type = ParseType();
                var name = ExpectName("a parameter name");
                Expr? defaultValue = null;
                if (Accept(TokenKind.Equals)) defaultValue = ParseExpression();
                list.Add(new Param(SpanFrom(start), modifier, type, name, defaultValue));
            } while (Accept(TokenKind.Comma));
        }
        Expect(TokenKind.CloseParen);
        return list.ToArray();
    }

    private ErrorEnumDecl ParseErrorEnum(int start, Modifiers modifiers)
    {
        Advance();
        Advance();
        var name = ExpectName("an error set name");
        var open = Expect(TokenKind.OpenBrace);
        var variants = new List<ErrorVariant>();
        while (Kind == TokenKind.Identifier || IsAttributeStart())
        {
            var first = Current;
            var attributes = ParseAttributes();
            var doc = DocOf(first) ?? DocOf(Current);
            var variant = ExpectName("a variant name");
            variants.Add(new ErrorVariant(variant.Span, variant, doc) { Attributes = attributes });
            if (!Accept(TokenKind.Comma)) break;
        }
        ExpectClose(open, "error enum");
        return new ErrorEnumDecl(SpanFrom(start), modifiers, name, variants.ToArray());
    }

    private TypeAliasDecl ParseTypeAlias(int start, Modifiers modifiers)
    {
        Advance();
        var name = ExpectName();
        Expect(TokenKind.Equals);
        var target = ParseType();
        Expect(TokenKind.Semicolon);
        return new TypeAliasDecl(SpanFrom(start), modifiers, name, target);
    }

    private TestDecl ParseTest(int start)
    {
        Advance();
        var title = "";
        if (Kind == TokenKind.StringLiteral)
            title = LiteralDecoder.DecodeString(Text(Advance()));
        else
            Report(DiagnosticCodes.Expected, Current.Span, ["the test's name as a string literal", Describe(Current)]);
        var body = ParseBlock();
        return new TestDecl(SpanFrom(start), title, body);
    }

    // ── Types ────────────────────────────────────────────────────────────────────────

    private static bool IsTypeNameToken(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.NeverKeyword;

    /// <summary>Speculatively scans a type starting at <paramref name="index"/>; returns the index after it, or -1.</summary>
    private int ScanType(int index)
    {
        int i;
        if (KindAt(index) == TokenKind.OpenParen)
        {
            i = index + 1;
            var elements = 0;
            while (true)
            {
                i = ScanType(i);
                if (i < 0) return -1;
                if (KindAt(i) == TokenKind.Identifier) i++;
                elements++;
                if (KindAt(i) == TokenKind.Comma)
                {
                    i++;
                    continue;
                }
                if (KindAt(i) != TokenKind.CloseParen || elements < 2) return -1;
                i++;
                break;
            }
        }
        else
        {
            if (!IsTypeNameToken(KindAt(index))) return -1;
            i = index + 1;
            while (KindAt(i) == TokenKind.Dot && KindAt(i + 1) == TokenKind.Identifier) i += 2;
            if (KindAt(i) == TokenKind.Less)
            {
                i = ScanTypeArgs(i);
                if (i < 0) return -1;
            }
        }
        while (true)
        {
            if (KindAt(i) == TokenKind.Question)
            {
                i++;
            }
            else if (KindAt(i) == TokenKind.OpenBracket
                     && KindAt(i + 1) is TokenKind.IntegerLiteral or TokenKind.Identifier
                     && KindAt(i + 2) == TokenKind.CloseBracket)
            {
                i += 3;
            }
            else
            {
                return i;
            }
        }
    }

    /// <summary>Scans <c>&lt;T, U&gt;</c> starting at the <c>&lt;</c>; returns the index after <c>&gt;</c>, or -1.</summary>
    private int ScanTypeArgs(int index)
    {
        var i = index + 1;
        while (true)
        {
            i = ScanType(i);
            if (i < 0) return -1;
            if (KindAt(i) == TokenKind.Comma)
            {
                i++;
                continue;
            }
            return KindAt(i) == TokenKind.Greater ? i + 1 : -1;
        }
    }

    private TypeSyntax ParseType()
    {
        var start = Current.Start;
        TypeSyntax type;
        if (Kind == TokenKind.OpenParen && ScanType(_pos) > 0)
        {
            Advance();
            var elements = new List<TupleTypeElement>();
            do
            {
                var elementStart = Current.Start;
                var elementType = ParseType();
                Name? name = Kind == TokenKind.Identifier ? NameOf(Advance()) : null;
                elements.Add(new TupleTypeElement(SpanFrom(elementStart), elementType, name));
            } while (Accept(TokenKind.Comma));
            Expect(TokenKind.CloseParen);
            type = new TupleType(SpanFrom(start), elements.ToArray());
        }
        else if (IsTypeNameToken(Kind))
        {
            var path = new List<Name> { NameOf(Advance()) };
            while (Kind == TokenKind.Dot && Peek(1) == TokenKind.Identifier)
            {
                Advance();
                path.Add(NameOf(Advance()));
            }
            var args = Array.Empty<TypeSyntax>();
            if (Kind == TokenKind.Less && ScanTypeArgs(_pos) > 0) args = ParseTypeArgs();
            type = new NamedType(SpanFrom(start), path.ToArray(), args);
        }
        else
        {
            Report(DiagnosticCodes.ExpectedType, Current.Span, [Describe(Current)]);
            return new NamedType(new TextSpan(start, 0), [new Name(new TextSpan(start, 0), "<missing>")], []);
        }

        while (true)
        {
            if (Kind == TokenKind.Question)
            {
                Advance();
                type = new OptionalType(SpanFrom(start), type);
            }
            else if (Kind == TokenKind.OpenBracket && Peek(1) is TokenKind.IntegerLiteral or TokenKind.Identifier && Peek(2) == TokenKind.CloseBracket)
            {
                Advance();
                var size = ParsePrimary();
                Expect(TokenKind.CloseBracket);
                type = new ArrayType(SpanFrom(start), type, size);
            }
            else
            {
                return type;
            }
        }
    }

    private TypeSyntax[] ParseTypeArgs()
    {
        Advance();
        var args = new List<TypeSyntax> { ParseType() };
        while (Accept(TokenKind.Comma)) args.Add(ParseType());
        Expect(TokenKind.Greater);
        return args.ToArray();
    }

    // ── Statements ───────────────────────────────────────────────────────────────────

    private BlockStmt ParseBlock()
    {
        var start = Current.Start;
        var open = Expect(TokenKind.OpenBrace);
        if (open.Length == 0) return new BlockStmt(new TextSpan(start, 0), []);
        var statements = new List<Stmt>();
        while (Kind != TokenKind.CloseBrace && !AtEnd)
        {
            // Functions do not nest: a function header on a new line means this block was never closed.
            if (Current.HasNewlineBefore && IsFunctionStart()) break;
            var before = _pos;
            var errors = ErrorCount;
            statements.Add(ParseStatement());
            RecoverIfNeeded(errors);
            if (_pos == before) Advance();
        }
        ExpectClose(open, "block");
        return new BlockStmt(SpanFrom(start), statements.ToArray());
    }

    private Stmt ParseStatement()
    {
        var start = Current.Start;
        switch (Kind)
        {
            case TokenKind.OpenBrace:
                return RejectPostfixAfterCompound(ParseBlock());
            case TokenKind.Semicolon:
                Advance();
                return new EmptyStmt(SpanFrom(start));
            case TokenKind.LetKeyword:
            case TokenKind.VarKeyword:
            case TokenKind.ConstKeyword:
            case TokenKind.UsingKeyword:
                return ParseLocal(start);
            case TokenKind.IfKeyword:
                return RejectPostfixAfterCompound(ParseIf(start));
            case TokenKind.UnlessKeyword:
                return RejectPostfixAfterCompound(ParseUnless(start));
            case TokenKind.WhileKeyword:
                Advance();
                {
                    var condition = ParseParenCondition();
                    var body = ParseEmbedded();
                    return new WhileStmt(SpanFrom(start), condition, body);
                }
            case TokenKind.DoKeyword:
                Advance();
                {
                    var body = ParseEmbedded();
                    Expect(TokenKind.WhileKeyword);
                    var condition = ParseParenCondition();
                    Expect(TokenKind.Semicolon);
                    return new DoWhileStmt(SpanFrom(start), body, condition);
                }
            case TokenKind.ForKeyword:
            case TokenKind.ForeachKeyword:
                return ParseFor(start);
            case TokenKind.SwitchKeyword:
                return ParseSwitchStatement(start);
            case TokenKind.DeferKeyword:
                Advance();
                return new DeferStmt(SpanFrom(start), ParseEmbedded());
            case TokenKind.ErrdeferKeyword:
                Advance();
                {
                    Name? error = null;
                    if (Kind == TokenKind.OpenParen && Peek(1) == TokenKind.Identifier && Peek(2) == TokenKind.CloseParen)
                    {
                        Advance();
                        error = NameOf(Advance());
                        Advance();
                    }
                    return new ErrdeferStmt(SpanFrom(start), error, ParseEmbedded());
                }
            case TokenKind.ContextKeyword:
                return ParseContext(start);
            case TokenKind.GotoKeyword:
                return ParseGoto(start);
            case TokenKind.Identifier when Peek(1) == TokenKind.Colon:
                {
                    var label = NameOf(Advance());
                    Advance();
                    return new LabeledStmt(SpanFrom(start), label, ParseStatement());
                }
            case TokenKind.ElseKeyword:
                Report(DiagnosticCodes.Expected, Current.Span, ["a statement", Describe(Current)], help: "this `else` has no matching `if`");
                Advance();
                return new ErrorStmt(SpanFrom(start));
        }

        if (IsLocalDeclarationStart())
            return ParseLocal(start);

        var simple = ParseSimpleStatement(start);
        return FinishSimpleStatement(start, simple);
    }

    /// <summary>A statement used as the body of <c>if</c>, loops and cleanup: a block or a single statement.</summary>
    private Stmt ParseEmbedded() => ParseStatement();

    /// <summary>C#-style <c>T name = …;</c> or <c>T name;</c>.</summary>
    private bool IsLocalDeclarationStart()
    {
        if (!IsTypeNameToken(Kind) && Kind != TokenKind.OpenParen) return false;
        var after = ScanType(_pos);
        return after > 0 && KindAt(after) == TokenKind.Identifier && KindAt(after + 1) is TokenKind.Equals or TokenKind.Semicolon;
    }

    private Stmt ParseLocal(int start)
    {
        var isUsing = Accept(TokenKind.UsingKeyword);
        var kind = BindingKind.Var;
        if (Accept(TokenKind.LetKeyword)) kind = BindingKind.Let;
        else if (Accept(TokenKind.VarKeyword)) kind = BindingKind.Var;
        else if (Accept(TokenKind.ConstKeyword)) kind = BindingKind.Const;
        else if (isUsing) kind = BindingKind.Let;

        TypeSyntax? type = null;
        if (IsTypeThenName(_pos)) type = ParseType();
        var name = ExpectName("a variable name");
        Expr? init = null;
        if (Accept(TokenKind.Equals)) init = ParseExpression();
        if (Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword && !Current.HasNewlineBefore)
        {
            Report(DiagnosticCodes.PostfixNotSimple, Current.Span, [Text(Current)],
                help: "a declaration cannot be conditional; declare the variable, then assign it conditionally");
            Advance();
            ParseExpression();
        }
        Expect(TokenKind.Semicolon);
        return new LocalDecl(SpanFrom(start), kind, isUsing, type, name, init);
    }

    private Expr ParseParenCondition()
    {
        Expect(TokenKind.OpenParen);
        var condition = ParseExpression();
        Expect(TokenKind.CloseParen);
        return condition;
    }

    private Stmt ParseIf(int start)
    {
        Advance();
        var condition = ParseParenCondition();
        var then = ParseEmbedded();
        Stmt? otherwise = null;
        if (Accept(TokenKind.ElseKeyword)) otherwise = ParseEmbedded();
        return new IfStmt(SpanFrom(start), condition, then, otherwise);
    }

    private Stmt ParseUnless(int start)
    {
        var keyword = Advance();
        var condition = ParseParenCondition();
        var closeEnd = _prevEnd;
        if (condition is UnaryExpr { Op: UnaryOp.Not } negated)
        {
            var fixSpan = TextSpan.FromBounds(keyword.Start, closeEnd);
            var operand = _source.GetText(negated.Operand.Span);
            Report(DiagnosticCodes.UnlessNegated, TextSpan.FromBounds(keyword.Start, closeEnd), label: "`unless` already negates",
                help: "use `if` with the condition as written", fix: new Fix(fixSpan, $"if ({operand})"));
        }
        var body = ParseEmbedded();
        if (Kind == TokenKind.ElseKeyword)
        {
            Report(DiagnosticCodes.UnlessWithElse, Current.Span, label: "`else` after `unless`",
                help: "use `if` with the opposite condition, so both branches read positively");
            Advance();
            ParseEmbedded();
        }
        return new UnlessStmt(SpanFrom(start), condition, body);
    }

    private Stmt ParseFor(int start)
    {
        Advance();
        Expect(TokenKind.OpenParen);
        var kind = BindingKind.Let;
        TypeSyntax? type = null;
        if (Accept(TokenKind.LetKeyword)) kind = BindingKind.Let;
        else if (Accept(TokenKind.VarKeyword)) kind = BindingKind.Var;
        if (IsTypeThenName(_pos)) type = ParseType();
        var name = ExpectName("a loop variable");
        if (Kind != TokenKind.InKeyword)
        {
            Report(DiagnosticCodes.CStyleFor, Current.Span, label: "expected `in`",
                help: "loop over a range instead: `for (let i in 0..n) { … }`");
            SkipPastCloseParen();
            var body0 = ParseEmbedded();
            return new ForStmt(SpanFrom(start), kind, type, name, new ErrorExpr(name.Span), body0);
        }
        Advance();
        var iterable = ParseExpression();
        Expect(TokenKind.CloseParen);
        var body = ParseEmbedded();
        return new ForStmt(SpanFrom(start), kind, type, name, iterable, body);
    }

    private void SkipPastCloseParen()
    {
        var depth = 0;
        while (!AtEnd)
        {
            if (Kind == TokenKind.OpenParen) depth++;
            else if (Kind == TokenKind.CloseParen && depth-- == 0)
            {
                Advance();
                return;
            }
            else if (Kind is TokenKind.OpenBrace or TokenKind.CloseBrace) return;
            Advance();
        }
    }

    private Stmt ParseSwitchStatement(int start)
    {
        Advance();
        var value = ParseParenCondition();
        var open = Expect(TokenKind.OpenBrace);
        var sections = new List<SwitchSection>();
        while (Kind is TokenKind.CaseKeyword or TokenKind.DefaultKeyword)
        {
            var sectionStart = Current.Start;
            var label = ParseSwitchLabel();
            while (Kind is TokenKind.CaseKeyword or TokenKind.DefaultKeyword)
            {
                var stackedAt = Current.Span;
                var next = ParseSwitchLabel();
                Report(DiagnosticCodes.StackedCaseLabels, stackedAt, label: "a second label for the same statements",
                    help: "combine the patterns into one label, e.g. `case .sat or .sun:`");
                if (label.Pattern is not null && next.Pattern is not null)
                    label = label with { Pattern = new BinaryPattern(TextSpan.FromBounds(label.Pattern.Span.Start, next.Pattern.Span.End), false, label.Pattern, next.Pattern) };
            }
            var statements = new List<Stmt>();
            while (Kind is not (TokenKind.CaseKeyword or TokenKind.DefaultKeyword or TokenKind.CloseBrace or TokenKind.EndOfFile))
            {
                var before = _pos;
                var errors = ErrorCount;
                statements.Add(ParseStatement());
                RecoverIfNeeded(errors);
                if (_pos == before) Advance();
            }
            sections.Add(new SwitchSection(SpanFrom(sectionStart), label, statements.ToArray()));
        }
        if (Kind != TokenKind.CloseBrace && !AtEnd)
            Report(DiagnosticCodes.Expected, Current.Span, ["`case`, `default` or `}`", Describe(Current)]);
        while (Kind != TokenKind.CloseBrace && !AtEnd) Advance();
        ExpectClose(open, "switch");
        return new SwitchStmt(SpanFrom(start), value, sections.ToArray());
    }

    private SwitchLabel ParseSwitchLabel()
    {
        var start = Current.Start;
        if (Accept(TokenKind.DefaultKeyword))
        {
            Expect(TokenKind.Colon);
            return new SwitchLabel(SpanFrom(start), null, null);
        }
        Advance();
        var pattern = ParsePattern();
        Expr? guard = null;
        if (AtContextual("when"))
        {
            Advance();
            guard = ParseExpression();
        }
        Expect(TokenKind.Colon);
        return new SwitchLabel(SpanFrom(start), pattern, guard);
    }

    private bool AtContextual(string word) => Kind == TokenKind.Identifier && Text(Current) == word;

    private Stmt ParseContext(int start)
    {
        Advance();
        Expect(TokenKind.OpenParen);
        var values = new List<NamedArg>();
        if (Kind != TokenKind.CloseParen)
        {
            do
            {
                var argStart = Current.Start;
                var name = ExpectName("a context name such as `allocator`");
                Expect(TokenKind.Colon);
                var value = ParseExpression();
                values.Add(new NamedArg(SpanFrom(argStart), name, value));
            } while (Accept(TokenKind.Comma));
        }
        Expect(TokenKind.CloseParen);
        var body = ParseBlock();
        return new ContextStmt(SpanFrom(start), values.ToArray(), body);
    }

    private Stmt ParseGoto(int start)
    {
        var keyword = Advance();
        if (Kind == TokenKind.CaseKeyword || Kind == TokenKind.DefaultKeyword)
        {
            Report(DiagnosticCodes.Goto, keyword.Span, label: "`goto case` does not exist",
                help: "switch sections never fall through; combine patterns with `or`, or move shared code into a function");
            Synchronize();
            return new ErrorStmt(SpanFrom(start));
        }
        Report(DiagnosticCodes.Goto, keyword.Span, label: "not a keyword in Bayan",
            help: "to leave a loop early, label it (`outer: for (…)`) and use `break outer;`",
            fix: new Fix(keyword.Span, "break"));
        Name? label = Kind == TokenKind.Identifier ? NameOf(Advance()) : null;
        Expect(TokenKind.Semicolon);
        return new BreakStmt(SpanFrom(start), label);
    }

    /// <summary>A statement that may take a postfix <c>if</c>/<c>unless</c>; the trailing <c>;</c> is not consumed.</summary>
    private Stmt ParseSimpleStatement(int start)
    {
        switch (Kind)
        {
            case TokenKind.ReturnKeyword:
                Advance();
                {
                    Expr? value = CanStartExpression(Kind) ? ParseExpression() : null;
                    return new ReturnStmt(SpanFrom(start), value);
                }
            case TokenKind.BreakKeyword:
                Advance();
                return new BreakStmt(SpanFrom(start), Kind == TokenKind.Identifier ? NameOf(Advance()) : null);
            case TokenKind.ContinueKeyword:
                Advance();
                return new ContinueStmt(SpanFrom(start), Kind == TokenKind.Identifier ? NameOf(Advance()) : null);
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
                {
                    var increment = Advance().Kind == TokenKind.PlusPlus;
                    var target = ParseUnary();
                    CheckAssignable(target);
                    return new IncDecStmt(SpanFrom(start), target, increment);
                }
        }

        var errors = ErrorCount;
        var expr = ParseExpression();
        if (TryAssignOp(out var op, out var width))
        {
            for (var i = 0; i < width; i++) Advance();
            CheckAssignable(expr);
            var value = ParseExpression();
            return new AssignStmt(SpanFrom(start), expr, op, value);
        }
        if (Kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            var increment = Advance().Kind == TokenKind.PlusPlus;
            CheckAssignable(expr);
            return new IncDecStmt(SpanFrom(start), expr, increment);
        }
        if (ErrorCount == errors && !IsStatementExpression(expr))
        {
            Report(DiagnosticCodes.InvalidExpressionStatement, expr.Span, label: "the value is computed and discarded",
                help: "only calls, assignments and `++`/`--` can stand alone; to discard a call's result write `_ = f();`");
        }
        return new ExprStmt(SpanFrom(start), expr);
    }

    private static bool IsStatementExpression(Expr expr) => expr switch
    {
        CallExpr => true,
        TryExpr t => IsStatementExpression(t.Operand),
        CatchExpr c => IsStatementExpression(c.Operand),
        BinaryExpr { Op: BinaryOp.Coalesce, Right: JumpExpr } => true,
        ErrorExpr => true,
        _ => false,
    };

    private bool TryAssignOp(out AssignOp op, out int width)
    {
        width = 1;
        op = Kind switch
        {
            TokenKind.Equals => AssignOp.Assign,
            TokenKind.PlusEquals => AssignOp.Add,
            TokenKind.MinusEquals => AssignOp.Sub,
            TokenKind.StarEquals => AssignOp.Mul,
            TokenKind.SlashEquals => AssignOp.Div,
            TokenKind.PercentEquals => AssignOp.Rem,
            TokenKind.AmpEquals => AssignOp.And,
            TokenKind.BarEquals => AssignOp.Or,
            TokenKind.CaretEquals => AssignOp.Xor,
            TokenKind.LessLessEquals => AssignOp.Shl,
            TokenKind.QuestionQuestionEquals => AssignOp.Coalesce,
            _ => (AssignOp)(-1),
        };
        if (op != (AssignOp)(-1)) return true;
        if (Kind == TokenKind.Greater && Peek(1) == TokenKind.GreaterEquals && !PeekToken(1).HasTriviaBefore)
        {
            op = AssignOp.Shr;
            width = 2;
            return true;
        }
        return false;
    }

    private void CheckAssignable(Expr target)
    {
        switch (target)
        {
            case NameExpr { Name.Text: "_" }:
            case NameExpr:
            case MemberExpr { NullConditional: false }:
            case IndexExpr:
            case ErrorExpr:
                return;
            case ParenExpr p:
                CheckAssignable(p.Inner);
                return;
            default:
                Report(DiagnosticCodes.InvalidAssignmentTarget, target.Span,
                    help: "only variables, fields and indexed elements can be assigned");
                return;
        }
    }

    private Stmt FinishSimpleStatement(int start, Stmt statement)
    {
        if (Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword)
        {
            var unless = Advance().Kind == TokenKind.UnlessKeyword;
            var conditionStart = Current.Start;
            var condition = ParseExpression();
            if (unless && condition is UnaryExpr { Op: UnaryOp.Not } negated)
            {
                var keywordSpan = new TextSpan(_tokens[IndexOfOffset(conditionStart) - 1].Start, "unless".Length);
                var fixSpan = TextSpan.FromBounds(keywordSpan.Start, condition.Span.End);
                Report(DiagnosticCodes.UnlessNegated, fixSpan, label: "`unless` already negates",
                    help: "use `if` with the condition as written",
                    fix: new Fix(fixSpan, "if " + _source.GetText(negated.Operand.Span)));
            }
            statement = new GuardedStmt(SpanFrom(start), statement, unless, condition);
            if (Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword)
            {
                Report(DiagnosticCodes.PostfixChained, Current.Span, label: "a second condition",
                    help: "combine the conditions with `&&` in one postfix `if`");
                Advance();
                ParseExpression();
            }
        }
        Expect(TokenKind.Semicolon);
        return statement;
    }

    private int IndexOfOffset(int offset)
    {
        var i = _pos;
        while (i > 0 && _tokens[i].Start > offset) i--;
        return i;
    }

    /// <summary><c>{ … } if x;</c> and <c>if (a) { … } if b;</c> on one line: postfix conditions only attach to simple statements.</summary>
    private Stmt RejectPostfixAfterCompound(Stmt statement)
    {
        if (Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword && !Current.HasNewlineBefore && Peek(1) != TokenKind.OpenParen)
        {
            Report(DiagnosticCodes.PostfixNotSimple, Current.Span, [Text(Current)],
                help: "put the condition first: `if (cond) { … }`");
            Advance();
            ParseExpression();
            Accept(TokenKind.Semicolon);
        }
        return statement;
    }

    // ── Expressions ──────────────────────────────────────────────────────────────────

    private enum Prec
    {
        None,
        Coalesce,
        LogicalOr,
        LogicalAnd,
        BitOr,
        BitXor,
        BitAnd,
        Equality,
        Relational,
        Range,
        Shift,
        Additive,
        Multiplicative,
    }

    private static bool CanStartExpression(TokenKind kind) => kind is
        TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral or TokenKind.CharLiteral or
        TokenKind.StringLiteral or TokenKind.RawStringLiteral or TokenKind.CStringLiteral or TokenKind.U16StringLiteral or
        TokenKind.InterpolatedStringStart or TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace or
        TokenKind.Dot or TokenKind.DotDot or TokenKind.DotDotEquals or TokenKind.Plus or TokenKind.Minus or TokenKind.Bang or
        TokenKind.Tilde or TokenKind.Caret or TokenKind.TrueKeyword or TokenKind.FalseKeyword or TokenKind.NullKeyword or
        TokenKind.ThisKeyword or TokenKind.TryKeyword or TokenKind.NewKeyword or TokenKind.ReturnKeyword or
        TokenKind.BreakKeyword or TokenKind.ContinueKeyword;

    public Expr ParseExpression()
    {
        var start = Current.Start;
        var condition = ParseBinary(Prec.Coalesce);
        if (Kind != TokenKind.Question) return condition;
        Advance();
        var whenTrue = ParseExpression();
        Expect(TokenKind.Colon);
        var whenFalse = ParseExpression();
        return new ConditionalExpr(SpanFrom(start), condition, whenTrue, whenFalse);
    }

    private (BinaryOp Op, Prec Prec, int Width) BinaryOperator()
    {
        return Kind switch
        {
            TokenKind.QuestionQuestion => (BinaryOp.Coalesce, Prec.Coalesce, 1),
            TokenKind.BarBar => (BinaryOp.LogicalOr, Prec.LogicalOr, 1),
            TokenKind.AmpAmp => (BinaryOp.LogicalAnd, Prec.LogicalAnd, 1),
            TokenKind.Bar => (BinaryOp.BitOr, Prec.BitOr, 1),
            TokenKind.Caret => (BinaryOp.BitXor, Prec.BitXor, 1),
            TokenKind.Amp => (BinaryOp.BitAnd, Prec.BitAnd, 1),
            TokenKind.EqualsEquals => (BinaryOp.Eq, Prec.Equality, 1),
            TokenKind.BangEquals => (BinaryOp.Ne, Prec.Equality, 1),
            TokenKind.Less => (BinaryOp.Lt, Prec.Relational, 1),
            TokenKind.LessEquals => (BinaryOp.Le, Prec.Relational, 1),
            TokenKind.Greater when AtShiftRight() => (BinaryOp.Shr, Prec.Shift, 2),
            TokenKind.Greater when Peek(1) == TokenKind.GreaterEquals && !PeekToken(1).HasTriviaBefore => (default, Prec.None, 0),
            TokenKind.Greater => (BinaryOp.Gt, Prec.Relational, 1),
            TokenKind.GreaterEquals => (BinaryOp.Ge, Prec.Relational, 1),
            TokenKind.LessLess => (BinaryOp.Shl, Prec.Shift, 1),
            TokenKind.Plus => (BinaryOp.Add, Prec.Additive, 1),
            TokenKind.Minus => (BinaryOp.Sub, Prec.Additive, 1),
            TokenKind.PlusPercent => (BinaryOp.AddWrap, Prec.Additive, 1),
            TokenKind.MinusPercent => (BinaryOp.SubWrap, Prec.Additive, 1),
            TokenKind.PlusBar => (BinaryOp.AddSat, Prec.Additive, 1),
            TokenKind.MinusBar => (BinaryOp.SubSat, Prec.Additive, 1),
            TokenKind.Star => (BinaryOp.Mul, Prec.Multiplicative, 1),
            TokenKind.Slash => (BinaryOp.Div, Prec.Multiplicative, 1),
            TokenKind.Percent => (BinaryOp.Rem, Prec.Multiplicative, 1),
            TokenKind.StarPercent => (BinaryOp.MulWrap, Prec.Multiplicative, 1),
            TokenKind.StarBar => (BinaryOp.MulSat, Prec.Multiplicative, 1),
            _ => (default, Prec.None, 0),
        };
    }

    private Expr ParseBinary(Prec minPrec)
    {
        var start = Current.Start;
        Expr left;
        if (Kind is TokenKind.DotDot or TokenKind.DotDotEquals && minPrec <= Prec.Range)
        {
            var inclusive = Advance().Kind == TokenKind.DotDotEquals;
            Expr? end = CanStartRangeEnd() ? ParseBinary(Prec.Shift) : null;
            left = new RangeExpr(SpanFrom(start), null, end, inclusive);
        }
        else
        {
            left = ParseUnary();
        }

        while (true)
        {
            if (Kind == TokenKind.IsKeyword && minPrec <= Prec.Relational)
            {
                Advance();
                var pattern = ParsePattern();
                left = new IsExpr(SpanFrom(start), left, pattern);
                continue;
            }
            if (Kind is TokenKind.DotDot or TokenKind.DotDotEquals && minPrec <= Prec.Range)
            {
                var inclusive = Advance().Kind == TokenKind.DotDotEquals;
                Expr? end = CanStartRangeEnd() ? ParseBinary(Prec.Shift) : null;
                left = new RangeExpr(SpanFrom(start), left, end, inclusive);
                continue;
            }
            if (Kind == TokenKind.CatchKeyword && minPrec <= Prec.Coalesce)
            {
                left = ParseCatch(start, left);
                continue;
            }
            var (op, prec, width) = BinaryOperator();
            if (prec == Prec.None || prec < minPrec) return left;
            for (var i = 0; i < width; i++) Advance();
            // `??` is right-associative; everything else is left-associative.
            var right = op == BinaryOp.Coalesce ? ParseBinary(prec) : ParseBinary(prec + 1);
            left = new BinaryExpr(SpanFrom(start), op, left, right);
        }
    }

    private bool CanStartRangeEnd() =>
        CanStartExpression(Kind) && Kind is not (TokenKind.OpenBrace or TokenKind.DotDot or TokenKind.DotDotEquals);

    private Expr ParseCatch(int start, Expr operand)
    {
        Advance();
        Name? binding = null;
        Expr? variant = null;
        if (Kind == TokenKind.OpenParen && Peek(1) == TokenKind.Identifier && Peek(2) == TokenKind.CloseParen)
        {
            Advance();
            binding = NameOf(Advance());
            Advance();
        }
        else if (Kind == TokenKind.OpenParen)
        {
            Advance();
            variant = ParseExpression();
            Expect(TokenKind.CloseParen);
        }

        Expr handler;
        if (Kind == TokenKind.SwitchKeyword)
        {
            var switchStart = Current.Start;
            Expr subject = binding is not null
                ? new NameExpr(binding.Span, binding, [])
                : new ErrorExpr(new TextSpan(switchStart, 0));
            if (binding is null)
                Report(DiagnosticCodes.Expected, Current.Span, ["`(e)` naming the error before `switch`", Describe(Current)],
                    help: "write `catch (e) switch { … }`");
            Advance();
            handler = ParseSwitchArms(switchStart, subject);
        }
        else
        {
            handler = ParseBinary(Prec.LogicalOr);
        }
        return new CatchExpr(SpanFrom(start), operand, binding, variant, handler);
    }

    private Expr ParseUnary()
    {
        var start = Current.Start;
        UnaryOp? op = Kind switch
        {
            TokenKind.Plus => UnaryOp.Plus,
            TokenKind.Minus => UnaryOp.Negate,
            TokenKind.Bang => UnaryOp.Not,
            TokenKind.Tilde => UnaryOp.Complement,
            TokenKind.Caret => UnaryOp.FromEnd,
            _ => null,
        };
        if (op is not null)
        {
            Advance();
            var operand = ParseUnary();
            return new UnaryExpr(SpanFrom(start), op.Value, operand);
        }
        switch (Kind)
        {
            case TokenKind.TryKeyword:
                Advance();
                return new TryExpr(SpanFrom(start), ParseUnary());
            case TokenKind.NewKeyword:
                // Accepted for C# compatibility; the formatter removes it.
                Advance();
                return ParseUnary();
            case TokenKind.ReturnKeyword:
                {
                    Advance();
                    Expr? value = CanStartExpression(Kind) && Kind is not (TokenKind.ReturnKeyword or TokenKind.BreakKeyword or TokenKind.ContinueKeyword)
                        ? ParseBinary(Prec.LogicalOr)
                        : null;
                    return new JumpExpr(SpanFrom(start), new ReturnStmt(SpanFrom(start), value));
                }
            case TokenKind.BreakKeyword:
                Advance();
                return new JumpExpr(SpanFrom(start), new BreakStmt(SpanFrom(start), Kind == TokenKind.Identifier ? NameOf(Advance()) : null));
            case TokenKind.ContinueKeyword:
                Advance();
                return new JumpExpr(SpanFrom(start), new ContinueStmt(SpanFrom(start), Kind == TokenKind.Identifier ? NameOf(Advance()) : null));
            case TokenKind.OpenParen when IsCast():
                {
                    Advance();
                    var type = ParseType();
                    Expect(TokenKind.CloseParen);
                    var operand = ParseUnary();
                    return new CastExpr(SpanFrom(start), type, operand);
                }
        }

        var expr = ParsePostfix(ParsePrimary());
        while (true)
        {
            if (Kind == TokenKind.SwitchKeyword && Peek(1) == TokenKind.OpenBrace)
            {
                Advance();
                expr = ParseSwitchArms(start, expr);
            }
            else if (Kind == TokenKind.WithKeyword && Peek(1) == TokenKind.OpenBrace)
            {
                Advance();
                var fields = ParseFieldInits();
                expr = new WithExpr(SpanFrom(start), expr, fields);
            }
            else
            {
                return expr;
            }
        }
    }

    private bool IsCast()
    {
        var after = ScanType(_pos + 1);
        if (after < 0 || KindAt(after) != TokenKind.CloseParen) return false;
        return KindAt(after + 1) is
            TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral or TokenKind.CharLiteral or
            TokenKind.StringLiteral or TokenKind.RawStringLiteral or TokenKind.InterpolatedStringStart or
            TokenKind.OpenParen or TokenKind.Bang or TokenKind.Tilde or TokenKind.ThisKeyword or
            TokenKind.TrueKeyword or TokenKind.FalseKeyword or TokenKind.NullKeyword or TokenKind.TryKeyword or TokenKind.NewKeyword;
    }

    private Expr ParseSwitchArms(int start, Expr value)
    {
        var open = Expect(TokenKind.OpenBrace);
        var arms = new List<SwitchArm>();
        while (Kind != TokenKind.CloseBrace && !AtEnd)
        {
            var armStart = Current.Start;
            var before = _pos;
            var pattern = ParsePattern();
            Expr? guard = null;
            if (AtContextual("when"))
            {
                Advance();
                guard = ParseExpression();
            }
            Expect(TokenKind.FatArrow);
            var result = ParseExpression();
            arms.Add(new SwitchArm(SpanFrom(armStart), pattern, guard, result));
            if (!Accept(TokenKind.Comma)) break;
            if (_pos == before) break;
        }
        ExpectClose(open, "switch");
        return new SwitchExpr(SpanFrom(start), value, arms.ToArray());
    }

    private Expr ParsePostfix(Expr expr)
    {
        var start = expr.Span.Start;
        while (true)
        {
            switch (Kind)
            {
                case TokenKind.Dot when Peek(1) == TokenKind.Identifier:
                    Advance();
                    {
                        var member = NameOf(Advance());
                        var typeArgs = Array.Empty<TypeSyntax>();
                        if (Kind == TokenKind.Less && IsTypeArgsBeforeCall()) typeArgs = ParseTypeArgs();
                        expr = new MemberExpr(SpanFrom(start), expr, member, false) { TypeArgs = typeArgs };
                    }
                    break;
                case TokenKind.QuestionDot:
                    Advance();
                    expr = new MemberExpr(SpanFrom(start), expr, ExpectName("a member name"), true);
                    break;
                case TokenKind.OpenParen:
                    {
                        var args = ParseArguments();
                        expr = new CallExpr(SpanFrom(start), expr, args);
                    }
                    break;
                case TokenKind.OpenBracket:
                    Advance();
                    {
                        var index = ParseExpression();
                        Expect(TokenKind.CloseBracket);
                        expr = new IndexExpr(SpanFrom(start), expr, index);
                    }
                    break;
                default:
                    return expr;
            }
        }
    }

    private bool IsTypeArgsBeforeCall()
    {
        var after = ScanTypeArgs(_pos);
        return after > 0 && KindAt(after) == TokenKind.OpenParen;
    }

    private Argument[] ParseArguments()
    {
        Expect(TokenKind.OpenParen);
        var args = new List<Argument>();
        if (Kind != TokenKind.CloseParen)
        {
            do
            {
                if (Kind == TokenKind.CloseParen) break;
                var start = Current.Start;
                Name? name = null;
                if (Kind == TokenKind.Identifier && Peek(1) == TokenKind.Colon)
                {
                    name = NameOf(Advance());
                    Advance();
                }
                var modifier = Kind switch
                {
                    TokenKind.RefKeyword => ArgModifier.Ref,
                    TokenKind.MoveKeyword => ArgModifier.Move,
                    TokenKind.OutKeyword => ArgModifier.Out,
                    TokenKind.InKeyword => ArgModifier.In,
                    _ => ArgModifier.None,
                };
                if (modifier != ArgModifier.None) Advance();
                var value = ParseExpression();
                args.Add(new Argument(SpanFrom(start), name, modifier, value));
            } while (Accept(TokenKind.Comma));
        }
        Expect(TokenKind.CloseParen);
        return args.ToArray();
    }

    private Expr ParsePrimary()
    {
        var start = Current.Start;
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.IntegerLiteral:
            case TokenKind.FloatLiteral:
                {
                    Advance();
                    Name? suffix = null;
                    if (Kind == TokenKind.NumericSuffix && !Current.HasTriviaBefore) suffix = NameOf(Advance());
                    var kind = token.Kind == TokenKind.IntegerLiteral ? LiteralKind.Integer : LiteralKind.Float;
                    return new LiteralExpr(SpanFrom(start), kind, Text(token), suffix);
                }
            case TokenKind.CharLiteral:
                Advance();
                return new LiteralExpr(token.Span, LiteralKind.Char, Text(token), null);
            case TokenKind.StringLiteral:
                Advance();
                return new LiteralExpr(token.Span, LiteralKind.String, LiteralDecoder.DecodeString(Text(token)), null);
            case TokenKind.CStringLiteral:
                Advance();
                return new LiteralExpr(token.Span, LiteralKind.CString, LiteralDecoder.DecodeString(Text(token)), null);
            case TokenKind.U16StringLiteral:
                Advance();
                return new LiteralExpr(token.Span, LiteralKind.U16String, LiteralDecoder.DecodeString(Text(token)), null);
            case TokenKind.RawStringLiteral:
                Advance();
                return new LiteralExpr(token.Span, LiteralKind.RawString, LiteralDecoder.DecodeRawString(Text(token)), null);
            case TokenKind.TrueKeyword:
                Advance();
                return new LiteralExpr(token.Span, LiteralKind.True, "true", null);
            case TokenKind.FalseKeyword:
                Advance();
                return new LiteralExpr(token.Span, LiteralKind.False, "false", null);
            case TokenKind.NullKeyword:
                Advance();
                return new LiteralExpr(token.Span, LiteralKind.Null, "null", null);
            case TokenKind.ThisKeyword:
                Advance();
                return new ThisExpr(token.Span);
            case TokenKind.InterpolatedStringStart:
                return ParseInterpolated();
            case TokenKind.Dot when Peek(1) == TokenKind.Identifier:
            {
                Advance();
                var member = NameOf(Advance());
                return new TargetMemberExpr(SpanFrom(start), member);
            }
            case TokenKind.OpenParen:
                return ParseParenOrTuple();
            case TokenKind.OpenBracket:
                return ParseCollection();
            case TokenKind.OpenBrace:
                {
                    var fields = ParseFieldInits();
                    return new StructLiteralExpr(SpanFrom(start), null, fields);
                }
            case TokenKind.Identifier:
                return ParseNameOrStructLiteral();
            case TokenKind.NeverKeyword:
                Advance();
                return new NameExpr(token.Span, NameOf(token), []);
        }
        Report(DiagnosticCodes.ExpectedExpression, Current.Span, [Describe(Current)]);
        return new ErrorExpr(new TextSpan(start, 0));
    }

    private Expr ParseNameOrStructLiteral()
    {
        var start = Current.Start;
        var token = Advance();
        var name = NameOf(token);

        if (name.Text == "stackalloc" && IsTypeNameToken(Kind))
        {
            var element = ParseTypeNameOnly();
            Expect(TokenKind.OpenBracket);
            var count = ParseExpression();
            Expect(TokenKind.CloseBracket);
            return new StackallocExpr(SpanFrom(start), element, count);
        }

        var typeArgs = Array.Empty<TypeSyntax>();
        if (Kind == TokenKind.Less)
        {
            var after = ScanTypeArgs(_pos);
            if (after > 0 && KindAt(after) is TokenKind.OpenParen or TokenKind.Dot or TokenKind.CloseParen or TokenKind.Comma
                    or TokenKind.Semicolon or TokenKind.CloseBracket or TokenKind.CloseBrace or TokenKind.OpenBrace)
                typeArgs = ParseTypeArgs();
        }

        if (Kind == TokenKind.OpenBrace && IsStructLiteralBody() && char.IsUpper(name.Text[0]))
        {
            var type = new NamedType(SpanFrom(start), [name], typeArgs);
            var fields = ParseFieldInits();
            return new StructLiteralExpr(SpanFrom(start), type, fields);
        }
        return new NameExpr(SpanFrom(start), name, typeArgs);
    }

    /// <summary>A type written without array or optional suffixes (for <c>stackalloc T[n]</c>).</summary>
    private TypeSyntax ParseTypeNameOnly()
    {
        var start = Current.Start;
        var path = new List<Name> { NameOf(Advance()) };
        var args = Array.Empty<TypeSyntax>();
        if (Kind == TokenKind.Less && ScanTypeArgs(_pos) > 0) args = ParseTypeArgs();
        return new NamedType(SpanFrom(start), path.ToArray(), args);
    }

    /// <summary>At <c>{</c>: is this <c>{ }</c> or <c>{ name = …</c>, i.e. a struct literal rather than a block?</summary>
    private bool IsStructLiteralBody() =>
        Peek(1) == TokenKind.CloseBrace || (Peek(1) == TokenKind.Identifier && Peek(2) == TokenKind.Equals);

    private FieldInit[] ParseFieldInits()
    {
        var open = Expect(TokenKind.OpenBrace);
        var fields = new List<FieldInit>();
        while (Kind != TokenKind.CloseBrace && !AtEnd)
        {
            var start = Current.Start;
            var before = _pos;
            var name = ExpectName("a field name");
            Expect(TokenKind.Equals);
            var value = ParseExpression();
            fields.Add(new FieldInit(SpanFrom(start), name, value));
            if (!Accept(TokenKind.Comma)) break;
            if (_pos == before) break;
        }
        ExpectClose(open, "struct literal");
        return fields.ToArray();
    }

    private Expr ParseParenOrTuple()
    {
        var start = Current.Start;
        Advance();
        if (Kind == TokenKind.CloseParen)
        {
            Report(DiagnosticCodes.ExpectedExpression, Current.Span, [Describe(Current)], help: "`()` is not a value; a tuple has at least two elements");
            Advance();
            return new ErrorExpr(SpanFrom(start));
        }
        var elements = new List<TupleElement>();
        var first = ParseTupleElement();
        if (first.Name is null && Kind == TokenKind.CloseParen)
        {
            Advance();
            return new ParenExpr(SpanFrom(start), first.Value);
        }
        elements.Add(first);
        while (Accept(TokenKind.Comma)) elements.Add(ParseTupleElement());
        Expect(TokenKind.CloseParen);
        return new TupleExpr(SpanFrom(start), elements.ToArray());
    }

    private TupleElement ParseTupleElement()
    {
        var start = Current.Start;
        Name? name = null;
        if (Kind == TokenKind.Identifier && Peek(1) == TokenKind.Colon)
        {
            name = NameOf(Advance());
            Advance();
        }
        var value = ParseExpression();
        return new TupleElement(SpanFrom(start), name, value);
    }

    private Expr ParseCollection()
    {
        var start = Current.Start;
        Advance();
        var elements = new List<Expr>();
        while (Kind != TokenKind.CloseBracket && !AtEnd)
        {
            var before = _pos;
            var elementStart = Current.Start;
            Expr element;
            if (Kind == TokenKind.DotDot)
            {
                Advance();
                element = new SpreadExpr(SpanFrom(elementStart), ParseExpression());
            }
            else
            {
                element = ParseExpression();
                if (element is ConditionalExpr)
                {
                    Report(DiagnosticCodes.TernaryInCollection, element.Span,
                        help: "wrap it in parentheses",
                        fix: new Fix(element.Span, "(" + _source.GetText(element.Span) + ")"));
                }
            }
            elements.Add(element);
            if (!Accept(TokenKind.Comma)) break;
            if (_pos == before) break;
        }
        Expect(TokenKind.CloseBracket);
        return new CollectionExpr(SpanFrom(start), elements.ToArray());
    }

    private Expr ParseInterpolated()
    {
        var start = Current.Start;
        Advance();
        var parts = new List<InterpolationPart>();
        while (true)
        {
            switch (Kind)
            {
                case TokenKind.InterpolatedStringText:
                    {
                        var token = Advance();
                        parts.Add(new InterpolationText(token.Span, LiteralDecoder.DecodeInterpolatedText(Text(token))));
                        continue;
                    }
                case TokenKind.InterpolationStart:
                    {
                        var holeStart = Advance().Start;
                        var expr = ParseExpression();
                        if (Kind != TokenKind.InterpolationEnd)
                        {
                            Report(DiagnosticCodes.Expected, Current.Span, ["`}` to end the interpolated expression", Describe(Current)]);
                            while (Kind is not (TokenKind.InterpolationEnd or TokenKind.InterpolatedStringEnd or TokenKind.EndOfFile or TokenKind.Semicolon))
                                Advance();
                        }
                        Accept(TokenKind.InterpolationEnd);
                        parts.Add(new InterpolationHole(SpanFrom(holeStart), expr));
                        continue;
                    }
                case TokenKind.InterpolatedStringEnd:
                    Advance();
                    return new InterpolatedExpr(SpanFrom(start), parts.ToArray());
                default:
                    // Unterminated: the lexer has reported it.
                    return new InterpolatedExpr(SpanFrom(start), parts.ToArray());
            }
        }
    }

    // ── Patterns ─────────────────────────────────────────────────────────────────────

    private Pattern ParsePattern()
    {
        var start = Current.Start;
        var left = ParseAndPattern();
        while (Kind == TokenKind.OrKeyword)
        {
            Advance();
            var right = ParseAndPattern();
            left = new BinaryPattern(SpanFrom(start), false, left, right);
        }
        return left;
    }

    private Pattern ParseAndPattern()
    {
        var start = Current.Start;
        var left = ParseNotPattern();
        while (Kind == TokenKind.AndKeyword)
        {
            Advance();
            var right = ParseNotPattern();
            left = new BinaryPattern(SpanFrom(start), true, left, right);
        }
        return left;
    }

    private Pattern ParseNotPattern()
    {
        var start = Current.Start;
        if (Accept(TokenKind.NotKeyword)) return new NotPattern(SpanFrom(start), ParseNotPattern());
        return ParsePrimaryPattern();
    }

    private Pattern ParsePrimaryPattern()
    {
        var start = Current.Start;
        switch (Kind)
        {
            case TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals:
                {
                    var op = Advance().Kind switch
                    {
                        TokenKind.Less => BinaryOp.Lt,
                        TokenKind.LessEquals => BinaryOp.Le,
                        TokenKind.Greater => BinaryOp.Gt,
                        _ => BinaryOp.Ge,
                    };
                    var value = ParseBinary(Prec.Shift);
                    return new RelationalPattern(SpanFrom(start), op, value);
                }
            case TokenKind.VarKeyword:
                Advance();
                return new VarPattern(SpanFrom(start), ExpectName("a variable name"));
            case TokenKind.Identifier when Text(Current) == "_" && Peek(1) is not (TokenKind.Dot or TokenKind.OpenParen):
                Advance();
                return new DiscardPattern(SpanFrom(start));
            case TokenKind.OpenBrace:
                return ParsePropertyPattern(start, null);
            case TokenKind.OpenBracket:
                return ParseListPattern(start);
            case TokenKind.OpenParen:
                {
                    Advance();
                    var elements = new List<Pattern> { ParsePattern() };
                    if (Accept(TokenKind.CloseParen)) return elements[0];
                    while (Accept(TokenKind.Comma)) elements.Add(ParsePattern());
                    Expect(TokenKind.CloseParen);
                    return new PositionalPattern(SpanFrom(start), null, elements.ToArray());
                }
            case TokenKind.Identifier when char.IsUpper(Text(Current)[0]):
                {
                    var after = ScanType(_pos);
                    if (after > 0 && KindAt(after) == TokenKind.OpenBrace)
                    {
                        var type = ParseType();
                        return ParsePropertyPattern(start, type);
                    }
                    if (after > 0 && KindAt(after) == TokenKind.OpenParen)
                    {
                        var type = ParseType();
                        Advance();
                        var elements = new List<Pattern>();
                        if (Kind != TokenKind.CloseParen)
                        {
                            elements.Add(ParsePattern());
                            while (Accept(TokenKind.Comma)) elements.Add(ParsePattern());
                        }
                        Expect(TokenKind.CloseParen);
                        return new PositionalPattern(SpanFrom(start), type, elements.ToArray());
                    }
                    break;
                }
        }
        var constant = ParseBinary(Prec.Range);
        return new ConstantPattern(SpanFrom(start), constant);
    }

    private Pattern ParsePropertyPattern(int start, TypeSyntax? type)
    {
        var open = Expect(TokenKind.OpenBrace);
        var properties = new List<PropertySubpattern>();
        while (Kind != TokenKind.CloseBrace && !AtEnd)
        {
            var before = _pos;
            var subStart = Current.Start;
            var path = new List<Name> { ExpectName("a property name") };
            while (Accept(TokenKind.Dot)) path.Add(ExpectName("a property name"));
            Expect(TokenKind.Colon);
            var pattern = ParsePattern();
            properties.Add(new PropertySubpattern(SpanFrom(subStart), path.ToArray(), pattern));
            if (!Accept(TokenKind.Comma)) break;
            if (_pos == before) break;
        }
        ExpectClose(open, "property pattern");
        Name? designation = null;
        if (Kind == TokenKind.Identifier && !AtContextual("when")) designation = NameOf(Advance());
        return new PropertyPattern(SpanFrom(start), type, properties.ToArray(), designation);
    }

    private Pattern ParseListPattern(int start)
    {
        Advance();
        var elements = new List<Pattern>();
        while (Kind != TokenKind.CloseBracket && !AtEnd)
        {
            var before = _pos;
            var elementStart = Current.Start;
            if (Accept(TokenKind.DotDot))
            {
                Pattern? inner = Kind is TokenKind.Comma or TokenKind.CloseBracket ? null : ParsePattern();
                elements.Add(new SlicePattern(SpanFrom(elementStart), inner));
            }
            else
            {
                elements.Add(ParsePattern());
            }
            if (!Accept(TokenKind.Comma)) break;
            if (_pos == before) break;
        }
        Expect(TokenKind.CloseBracket);
        return new ListPattern(SpanFrom(start), elements.ToArray());
    }
}
