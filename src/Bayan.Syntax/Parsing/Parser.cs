using System.Text;
using Bayan.Syntax.Ast;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Lexing;
using Bayan.Syntax.Text;

namespace Bayan.Syntax.Parsing;

/// <summary>A parsed file: its tree plus every lexer and parser diagnostic.</summary>
public sealed record SyntaxTree(SourceText Source, CompilationUnit Root, IReadOnlyList<Diagnostic> Diagnostics)
{
    public static SyntaxTree Parse(SourceText source) => Parser.Parse(source);

    /// <summary>The line-join lint (BYN5103): reported by a compilation and fixed by <c>bayan format</c>, not by <c>bayan parse</c>.</summary>
    public IReadOnlyList<Diagnostic> Lints { get; init; } = [];
}

/// <summary>
/// Recursive-descent parser with precedence climbing for binary operators. A statement ends at a
/// line break, <c>}</c> or the end of the file (the waqf rules, spec §2.5); a <c>;</c> is an input
/// form. Recovers at statement and declaration boundaries.
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
    /// <summary>Reports other than <see cref="InputForms"/>; only these make <see cref="RecoverIfNeeded"/> resynchronize.</summary>
    private int _syntaxErrors;
    /// <summary>Token index where the bare condition being parsed starts, or -1 (see <see cref="BraceLiteralAllowed"/>).</summary>
    private int _conditionStart = -1;
    /// <summary>Per token: whether its innermost bracket is <c>(</c>, <c>[</c> or an interpolation hole, where a line break never ends a statement.</summary>
    private readonly bool[] _inGroup;
    /// <summary>Whether the declaration being parsed carries <c>extern</c>, where pointer types are allowed (BYN1067).</summary>
    private bool _inExtern;
    /// <summary>Whether a name or <c>( … )</c> before <c>=&gt;</c> is not a lambda: in a pattern, an arm's guard or an <c>expects</c> condition.</summary>
    private bool _noLambda;

    /// <summary>Set while type arguments are scanned inside an expression, where <c>=&gt;</c> is never a type's arrow (<c>a &lt; b =&gt; c &gt; (x)</c>).</summary>
    private bool _exprTypeArgs;

    private Parser(LexResult lexed)
    {
        _source = lexed.Source;
        _tokens = lexed.Buffer;
        _count = lexed.Count;
        _diagnostics.AddRange(lexed.Diagnostics);
        _inGroup = new bool[_count];
        var groups = new Stack<bool>();
        for (var i = 0; i < _count; i++)
        {
            _inGroup[i] = groups.Count > 0 && groups.Peek();
            switch (_tokens[i].Kind)
            {
                case TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.InterpolationStart:
                    groups.Push(true);
                    break;
                case TokenKind.OpenBrace:
                    groups.Push(false);
                    break;
                case TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.InterpolationEnd or TokenKind.CloseBrace:
                    groups.TryPop(out _);
                    break;
            }
        }
    }

    public static SyntaxTree Parse(SourceText source)
    {
        var parser = new Parser(Lexer.Lex(source));
        var root = parser.ParseCompilationUnit();
        return new SyntaxTree(source, root, parser._diagnostics.Items) { Lints = parser.LintLineJoins() };
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

    /// <summary>
    /// Input forms reported on a construct that still parsed in full, so the parser is at a clean restart point; a
    /// missing line break (BYN1005) also leaves the parser at the start of the next statement.
    /// </summary>
    private static readonly HashSet<DiagnosticDescriptor> InputForms =
    [
        DiagnosticCodes.UnlessNegated, DiagnosticCodes.ParenthesizedCondition, DiagnosticCodes.BodyNeedsBraces,
        DiagnosticCodes.ForInputForm, DiagnosticCodes.ErrdeferInput, DiagnosticCodes.FieldInitEquals, DiagnosticCodes.UnlessCompound,
        DiagnosticCodes.UnlessNull, DiagnosticCodes.CatchBinding, DiagnosticCodes.DelegateDecl, DiagnosticCodes.ErrorSetPipe,
        DiagnosticCodes.FailsShorthand, DiagnosticCodes.UnlessLikelihood,
        DiagnosticCodes.SemicolonInput, DiagnosticCodes.ElseOnNewLine, DiagnosticCodes.OnNewLine, DiagnosticCodes.PostfixOnNewLine,
        DiagnosticCodes.OneLineDeclarations, DiagnosticCodes.MissingLineBreak, DiagnosticCodes.TypeNameOrder,
        DiagnosticCodes.BracketAttribute, DiagnosticCodes.CharInput, DiagnosticCodes.RequiresValue,
        DiagnosticCodes.MissingBecause, DiagnosticCodes.ExceptInCase, DiagnosticCodes.NoallocRemoved, DiagnosticCodes.ContextBlockInput,
        DiagnosticCodes.TrustInput,
        DiagnosticCodes.RestInput, DiagnosticCodes.StateLineInput, DiagnosticCodes.InterfaceGetInput, DiagnosticCodes.ReceiverWithoutState,
        DiagnosticCodes.RequiresOnFunction, DiagnosticCodes.ClauseOrder, DiagnosticCodes.PrefixPointerInput,
        DiagnosticCodes.CompactFormatSpec, DiagnosticCodes.SingleStateInline, DiagnosticCodes.FatArrowFunctionType,
        DiagnosticCodes.StatementAttribute,
    ];

    private Diagnostic? Report(DiagnosticDescriptor descriptor, TextSpan span, object?[]? args = null,
        string? label = null, string? help = null, Fix? fix = null)
    {
        if (span.Start == _lastErrorAt) return null;
        _lastErrorAt = span.Start;
        if (!InputForms.Contains(descriptor)) _syntaxErrors++;
        return _diagnostics.Report(descriptor, _source, span, args, label, help, fix);
    }

    private int ErrorCount => _diagnostics.Items.Count;

    private string Describe(Token token) => token.Kind switch
    {
        TokenKind.EndOfFile => "end of file",
        TokenKind.Identifier => $"`{Text(token)}`",
        TokenKind.IntegerLiteral or TokenKind.FloatLiteral => $"`{Text(token)}`",
        _ when token.Kind.IsKeyword() => $"keyword `{Text(token)}`",
        TokenKind.CharLiteral => "a rune literal",
        TokenKind.InterpolatedStringStart => "a string literal",
        _ when token.Kind.IsStringLiteral() => "a string literal",
        _ => $"`{token.Kind.Text()}`",
    };

    private Token Expect(TokenKind kind, string? label = null)
    {
        if (Kind == kind) return Advance();
        Report(DiagnosticCodes.Expected, Current.Span, [$"`{kind.Text()}`", Describe(Current)], label);
        return new Token(kind, Current.Start, 0, 0, 0, 0);
    }

    /// <summary>
    /// The end of a statement or declaration: a line break, <c>}</c> or the end of the file. Each <c>;</c> here is
    /// the input form (BYN1040); another statement on the same line is BYN1005.
    /// </summary>
    private void ExpectEnd()
    {
        if (Kind == TokenKind.Semicolon)
        {
            while (Kind == TokenKind.Semicolon) ReportSemicolon(Advance());
            return;
        }
        if (AtEnd || Kind == TokenKind.CloseBrace || Current.HasNewlineBefore) return;
        // The token after the statement was already reported (e.g. as a bad expression); one error is enough.
        if (_lastErrorAt == Current.Start) return;
        Report(DiagnosticCodes.MissingLineBreak, new TextSpan(_prevEnd, 0), label: $"found {Describe(Current)}",
            help: "put the next statement on its own line",
            fix: new Fix(TextSpan.FromBounds(_prevEnd, Current.Start), "\n" + IndentAt(_prevEnd)));
    }

    /// <summary>BYN1040 for a <c>;</c> just consumed: deleted, or a line break when a statement follows on its line.</summary>
    private void ReportSemicolon(Token semicolon)
    {
        var bytes = _source.Bytes.Span;
        var from = semicolon.Start;
        while (from > 0 && bytes[from - 1] is (byte)' ' or (byte)'\t') from--;
        if (Current.HasNewlineBefore || Kind is TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile)
        {
            Report(DiagnosticCodes.SemicolonInput, semicolon.Span, label: "remove `;`", help: "remove the `;`",
                fix: new Fix(TextSpan.FromBounds(from, semicolon.End), ""));
            return;
        }
        var to = semicolon.End;
        while (to < bytes.Length && bytes[to] is (byte)' ' or (byte)'\t') to++;
        Report(DiagnosticCodes.SemicolonInput, semicolon.Span, label: "remove `;`", help: "put the next statement on its own line",
            fix: new Fix(TextSpan.FromBounds(from, to), "\n" + IndentAt(semicolon.Start)));
    }

    /// <summary>The spaces and tabs that start the line holding <paramref name="offset"/>.</summary>
    private string IndentAt(int offset)
    {
        var line = _source.GetLineSpan(_source.GetLineIndex(offset));
        var bytes = _source.Bytes.Span;
        var end = line.Start;
        while (end < line.End && bytes[end] is (byte)' ' or (byte)'\t') end++;
        return _source.GetText(TextSpan.FromBounds(line.Start, end));
    }

    // ── Line breaks (waqf) ───────────────────────────────────────────────────────────

    /// <summary>Whether a line break before token <paramref name="index"/> can end a statement: one outside <c>()</c>, <c>[]</c> and interpolation holes.</summary>
    private bool BreaksLine(int index) => index < _count && _tokens[index].HasNewlineBefore && !_inGroup[index];

    /// <summary>Whether a line of only whitespace lies between token <paramref name="index"/> and the one before it; a comment line is not blank.</summary>
    private bool HasBlankLineBefore(int index)
    {
        if (index <= 0 || index >= _count) return false;
        var lineIsBlank = false;
        foreach (var b in _source.Bytes.Span[_tokens[index - 1].End.._tokens[index].Start])
        {
            if (b == (byte)'\n')
            {
                if (lineIsBlank) return true;
                lineIsBlank = true;
            }
            else if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r'))
            {
                lineIsBlank = false;
            }
        }
        return false;
    }

    /// <summary>A token that joins the line it starts to the line before (waṣl).</summary>
    private bool IsWasl(int index) => KindAt(index) is TokenKind.Dot or TokenKind.QuestionDot or TokenKind.QuestionQuestion
        or TokenKind.AmpAmp or TokenKind.BarBar or TokenKind.AndKeyword or TokenKind.OrKeyword or TokenKind.CatchKeyword
        or TokenKind.ExceptKeyword or TokenKind.RequiresKeyword or TokenKind.FailsKeyword
        or TokenKind.UsesKeyword
        || KindAt(index) == TokenKind.Identifier && Text(_tokens[index]) switch
        {
            "where" => KindAt(index + 1) == TokenKind.Identifier && KindAt(index + 2) == TokenKind.Colon,
            "becomes" => KindAt(index + 1) == TokenKind.Dot,
            "expects" => KindAt(index + 1) is TokenKind.Identifier or TokenKind.Bang or TokenKind.NotKeyword or TokenKind.ThisKeyword,
            _ => false,
        };

    /// <summary>Whether the line before token <paramref name="index"/> ends in an operator, <c>,</c>, <c>=</c>, <c>=&gt;</c> or an opening bracket (lā), or the token is waṣl.</summary>
    private bool LineContinues(int index) => IsWasl(index) || AfterLa(index);

    /// <summary>Whether the token before <paramref name="index"/> is an operator, <c>,</c>, <c>=</c>, <c>=&gt;</c> or an opening bracket (lā).</summary>
    private bool AfterLa(int index) => index > 0 && KindAt(index - 1) is
        TokenKind.Comma or TokenKind.FatArrow or TokenKind.OpenBrace or TokenKind.OpenParen or TokenKind.OpenBracket or
        TokenKind.Dot or TokenKind.DotDot or TokenKind.DotDotEquals or TokenKind.Question or TokenKind.QuestionQuestion or
        TokenKind.QuestionDot or TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent or
        TokenKind.PlusPercent or TokenKind.MinusPercent or TokenKind.StarPercent or TokenKind.PlusBar or TokenKind.MinusBar or
        TokenKind.StarBar or TokenKind.Amp or TokenKind.AmpAmp or TokenKind.Bar or TokenKind.BarBar or TokenKind.Caret or
        TokenKind.EqualsEquals or TokenKind.BangEquals or TokenKind.Less or TokenKind.LessEquals or TokenKind.GreaterEquals or
        TokenKind.LessLess or TokenKind.AndKeyword or TokenKind.OrKeyword or TokenKind.IsKeyword or TokenKind.InKeyword or
        TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals or TokenKind.StarEquals or TokenKind.SlashEquals or
        TokenKind.PercentEquals or TokenKind.AmpEquals or TokenKind.BarEquals or TokenKind.CaretEquals or
        TokenKind.LessLessEquals or TokenKind.QuestionQuestionEquals;

    /// <summary>Whether the expression being parsed ends before the current token: it starts a new line, and is not waṣl or follows a blank line.</summary>
    private bool StopsHere => BreaksLine(_pos) && (HasBlankLineBefore(_pos) || !IsWasl(_pos));

    /// <summary>Whether a statement ends before token <paramref name="index"/>, for lookahead: a <c>;</c>, <c>}</c>, the end, or a line break.</summary>
    private bool EndsBefore(int index) => KindAt(index) is TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile || BreaksLine(index);

    // ── Line-join lint (BYN5103) ─────────────────────────────────────────────────────

    /// <summary>
    /// BYN5103 on each waṣl line indented no deeper (in leading space and tab bytes) than the first line of the statement
    /// it joins; <c>catch</c> right after <c>}</c> is exempt. The fix indents the line four spaces past the statement.
    /// </summary>
    private IReadOnlyList<Diagnostic> LintLineJoins()
    {
        var lints = new DiagnosticBag();
        var opener = new int[_count];
        var open = new Stack<int>();
        for (var i = 0; i < _count; i++)
        {
            opener[i] = -1;
            switch (_tokens[i].Kind)
            {
                case TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace or TokenKind.InterpolationStart:
                    open.Push(i);
                    break;
                case TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.InterpolationEnd or TokenKind.CloseBrace:
                    if (open.TryPop(out var o)) opener[i] = o;
                    break;
            }
        }
        // Each line's first token, and the first token of the line its statement starts on, filled forward: the statement
        // starts on the first line reached by stepping back over joined lines and bracketed groups, and every step goes back.
        var first = new int[_count];
        var statement = new int[_count];
        for (var i = 0; i < _count; i++)
        {
            first[i] = i > 0 && !_tokens[i].HasNewlineBefore ? first[i - 1] : i;
            statement[i] = i;
            if (first[i] != i) continue;
            if (opener[i] >= 0) statement[i] = statement[first[opener[i]]];
            else if (i > 0 && JoinsLineBefore(i)) statement[i] = statement[first[i - 1]];
        }
        for (var i = 1; i < _count; i++)
        {
            if (!BreaksLine(i) || HasBlankLineBefore(i) || !IsWasl(i)) continue;
            // After lā the line above already continues; after `{` or `,` the line starts an entry (a statement, an arm).
            if (AfterLa(i)) continue;
            if (KindAt(i) == TokenKind.CatchKeyword && KindAt(i - 1) == TokenKind.CloseBrace) continue;
            // At a field's indentation `requires` is a struct-level rule (O3, spec §2.5), so its indentation is meant.
            if (KindAt(i) == TokenKind.RequiresKeyword) continue;
            var start = statement[first[i - 1]];
            var indent = IndentAt(_tokens[i].Start);
            var statementIndent = IndentAt(_tokens[start].Start);
            if (indent.Length > statementIndent.Length) continue;
            var line = _source.GetLineIndex(_tokens[start].Start) + 1;
            var lineStart = _source.GetLineSpan(_source.GetLineIndex(_tokens[i].Start)).Start;
            lints.Report(DiagnosticCodes.LineJoinIndent, _source, _tokens[i].Span, [Text(_tokens[i])],
                label: $"joins the statement on line {line}", help: "indent it deeper than the line its statement starts on",
                fix: new Fix(new TextSpan(lineStart, indent.Length), statementIndent + "    "));
        }
        return lints.Items;
    }

    /// <summary>Whether the line starting at token <paramref name="index"/> continues the line before: inside <c>()</c> or <c>[]</c>, waṣl, or after lā (not <c>{</c>, which opens a block).</summary>
    private bool JoinsLineBefore(int index) =>
        !BreaksLine(index) || !HasBlankLineBefore(index) && KindAt(index - 1) != TokenKind.OpenBrace && LineContinues(index);

    private Name ExpectName(string what = "a name")
    {
        if (IsName(Kind))
        {
            var token = Advance();
            var text = Text(token);
            // SPEC §2.4: a C# primitive name is a type, never a declared name; a member after `.` only refers to one.
            // Not through Report: the name parsed in full, so no recovery is needed.
            if (Keywords.IsCSharpPrimitive(text) && (_pos < 2 || KindAt(_pos - 2) != TokenKind.Dot))
                _diagnostics.Report(DiagnosticCodes.ReservedKeyword, _source, token.Span, [text],
                    label: "a C# primitive type name", help: "choose another name");
            return new Name(token.Span, text);
        }
        Report(DiagnosticCodes.Expected, Current.Span, [what, Describe(Current)]);
        return new Name(new TextSpan(Current.Start, 0), "<missing>");
    }

    private Name NameOf(Token token) => new(token.Span, Text(token));

    /// <summary>An identifier, or a declaration keyword used as a name (spec 004 R3).</summary>
    private static bool IsName(TokenKind kind) => kind == TokenKind.Identifier || kind.IsContextualKeyword();

    // ── Recovery ─────────────────────────────────────────────────────────────────────

    private static bool IsStatementKeyword(TokenKind kind) => kind is
        TokenKind.LetKeyword or TokenKind.VarKeyword or TokenKind.ConstKeyword or TokenKind.IfKeyword or
        TokenKind.UnlessKeyword or TokenKind.WhileKeyword or TokenKind.DoKeyword or TokenKind.ForKeyword or
        TokenKind.ForeachKeyword or TokenKind.SwitchKeyword or TokenKind.ReturnKeyword or TokenKind.FailKeyword or TokenKind.BreakKeyword or
        TokenKind.ContinueKeyword or TokenKind.DeferKeyword or TokenKind.ErrdeferKeyword or TokenKind.UsingKeyword or
        TokenKind.ContextKeyword or TokenKind.ProvideKeyword or TokenKind.BecomeKeyword or TokenKind.StructKeyword or TokenKind.PublicKeyword or TokenKind.PrivateKeyword or
        TokenKind.ImportKeyword or TokenKind.ErrorKeyword or TokenKind.TestKeyword or TokenKind.TypeKeyword or
        TokenKind.StaticKeyword or TokenKind.ReadonlyKeyword or TokenKind.RequiredKeyword or TokenKind.UniqueKeyword or
        TokenKind.ExternKeyword or TokenKind.UnsafeKeyword or TokenKind.UnionKeyword or TokenKind.DistinctKeyword;

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

    /// <summary>After a syntax error inside a statement, skip only if the parser is not already at a clean restart point.</summary>
    private void RecoverIfNeeded(int syntaxErrorsBefore)
    {
        if (_syntaxErrors == syntaxErrorsBefore) return;
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
            var errors = _syntaxErrors;
            if (Kind == TokenKind.CloseBrace)
            {
                Report(DiagnosticCodes.Expected, Current.Span, ["a declaration or statement", Describe(Current)], help: "remove this `}`; it has no matching `{`");
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
                TokenKind.ConstKeyword when IsTypeThenName(_pos + 1) && KindAt(ScanType(_pos + 1) + 1) != TokenKind.Equals && !EndsBefore(ScanType(_pos + 1) + 1)
                    => Modifiers.Const,
                _ => Modifiers.None,
            };
            if (flag == Modifiers.None) return modifiers;
            if ((modifiers & flag) != 0)
                Report(DiagnosticCodes.ModifierNotAllowed, Current.Span, [Text(Current)], help: "remove the repeated modifier");
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
        var outerExtern = _inExtern;
        _inExtern |= (modifiers & Modifiers.Extern) != 0;

        Decl? decl;
        switch (Kind)
        {
            case TokenKind.ImportKeyword when topLevel:
                RejectModifiers(modifiers, Modifiers.None, startIndex);
                decl = ParseImport(start);
                break;
            case TokenKind.StructKeyword:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private | Modifiers.Readonly | Modifiers.Unique, startIndex);
                for (var i = startIndex; i < _pos; i++)
                    if (_tokens[i].Kind == TokenKind.UniqueKeyword) ReportKeywordOrder(_tokens[i], Current, "struct unique");
                decl = ParseStruct(start, modifiers);
                break;
            case TokenKind.UnionKeyword when Peek(1) == TokenKind.ErrorKeyword && IsName(Peek(2)):
            case TokenKind.ErrorKeyword when Peek(1) is TokenKind.EnumKeyword or TokenKind.UnionKeyword or TokenKind.SetKeyword:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                decl = ParseErrorUnion(start, modifiers);
                break;
            case TokenKind.EnumKeyword when topLevel:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                decl = ParseEnum(start, modifiers);
                break;
            case TokenKind.UnionKeyword when topLevel:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                decl = ParseUnion(start, modifiers);
                break;
            case TokenKind.InterfaceKeyword when topLevel:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                decl = ParseInterface(start, modifiers);
                break;
            case TokenKind.ExtensionKeyword when topLevel:
                RejectModifiers(modifiers, Modifiers.None, startIndex);
                decl = ParseExtension(start);
                break;
            case TokenKind.TypeKeyword when Peek(1) == TokenKind.Identifier && Peek(2) == TokenKind.Equals:
            case TokenKind.TypeKeyword or TokenKind.DistinctKeyword when Peek(1) is TokenKind.DistinctKeyword or TokenKind.TypeKeyword
                && Peek(1) != Kind && IsName(Peek(2)) && Peek(3) == TokenKind.Equals:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                decl = ParseTypeAlias(start, modifiers);
                break;
            case TokenKind.EffectKeyword when topLevel && IsName(Peek(1)) && Peek(2) == TokenKind.Equals:
                RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                decl = ParseEffect(start, modifiers);
                break;
            case TokenKind.TestKeyword when topLevel && modifiers == Modifiers.None:
                decl = ParseTest(start);
                break;
            case TokenKind.DelegateKeyword:
                {
                    var keyword = Advance();
                    var signature = _pos;
                    while (Kind is not (TokenKind.Semicolon or TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.EndOfFile) && !BreaksLine(_pos)) Advance();
                    Accept(TokenKind.Semicolon);
                    var span = TextSpan.FromBounds(keyword.Start, _prevEnd);
                    var alias = DelegateAlias(signature);
                    var (aliasName, aliasType) = alias ?? ("Name", "(Param) -> Result");
                    Report(DiagnosticCodes.DelegateDecl, span, [aliasName, aliasType], label: "a C# delegate",
                        help: $"declare it as `type {aliasName} = {aliasType}`", fix: alias is null ? null : new Fix(span, $"type {aliasName} = {aliasType}"));
                    decl = null;
                    break;
                }
            default:
                if (structName is not null && Kind == TokenKind.Identifier && Text(Current) == structName && Peek(1) == TokenKind.OpenParen)
                {
                    RejectModifiers(modifiers, Modifiers.Public | Modifiers.Private, startIndex);
                    decl = ParseConstructor(start, modifiers);
                }
                else if (IsFunctionStart() || IsNameFirstFunctionStart(allowNoBody: false))
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
        _inExtern = outerExtern;
        return decl is null ? null : decl with { Doc = doc, Attributes = attributes };
    }

    private bool IsAttributeStart() =>
        Kind == TokenKind.At && IsName(Peek(1))
        || Kind == TokenKind.OpenBracket && Peek(1) == TokenKind.Identifier && Peek(2) is TokenKind.OpenParen or TokenKind.CloseBracket;

    /// <summary>
    /// <c>@name</c> or <c>@name(args)</c>, each with an optional <c>because "…"</c>, repeated; the names are checked by
    /// the compiler. <c>[Name(args)]</c> is the BYN1045 input form, kept under its camelCase name.
    /// </summary>
    private AttributeSyntax[] ParseAttributes()
    {
        if (!IsAttributeStart()) return [];
        var attributes = new List<AttributeSyntax>();
        while (IsAttributeStart())
        {
            var start = Current.Start;
            var bracket = Advance().Kind == TokenKind.OpenBracket;
            var name = ExpectName("an attribute name");
            var argsStart = Current.Start;
            var args = Kind == TokenKind.OpenParen && (bracket || !BreaksLine(_pos)) ? ParseArguments() : [];
            var argsText = _prevEnd > argsStart ? _source.GetText(TextSpan.FromBounds(argsStart, _prevEnd)) : "";
            if (bracket)
            {
                Expect(TokenKind.CloseBracket);
                var camel = char.ToLowerInvariant(name.Text[0]) + name.Text[1..];
                var span = SpanFrom(start);
                Report(DiagnosticCodes.BracketAttribute, new TextSpan(start, 1), [name.Text + argsText, camel + argsText],
                    help: "write the attribute with `@` and a camelCase name", fix: new Fix(span, "@" + camel + argsText));
                name = name with { Text = camel };
            }
            var attribute = new AttributeSyntax(SpanFrom(start), name, args);
            var because = bracket ? null : ParseBecause("@" + name.Text, attribute.Span);
            if (because is null && !bracket && (name.Text == "allow" || name.Text == "json" && args.Any(a => a is { Name: null, Value: NameExpr { Name.Text: "expose" } })))
                ReportMissingBecause("@" + name.Text + (name.Text == "json" ? "(expose)" : ""), attribute.Span);
            attributes.Add(attribute with { Because = because });
        }
        return attributes.ToArray();
    }

    /// <summary>An optional <c>because "reason"</c> on the line of an attribute or a call (J6); an empty reason is BYN8001.</summary>
    private BecauseClause? ParseBecause(string construct, TextSpan constructSpan)
    {
        if (Kind != TokenKind.BecauseKeyword || BreaksLine(_pos)) return null;
        var start = Advance().Start;
        if (Kind != TokenKind.StringLiteral)
        {
            Report(DiagnosticCodes.Expected, Current.Span, ["the reason as a string literal", Describe(Current)]);
            return null;
        }
        var reason = LiteralDecoder.DecodeString(Text(Advance()));
        if (string.IsNullOrWhiteSpace(reason)) ReportMissingBecause(construct, constructSpan);
        return new BecauseClause(SpanFrom(start), reason);
    }

    /// <summary>
    /// Attributes before a statement (R7): <c>@unobservable because "…"</c> or <c>@unobservable(operator) because "…"</c>,
    /// applying to the next statement only. Any other attribute is BYN1069 and is kept so parsing continues.
    /// </summary>
    private AttributeSyntax[] ParseStatementAttributes()
    {
        var attributes = new List<AttributeSyntax>();
        while (Kind == TokenKind.At && IsName(Peek(1)))
        {
            var start = Advance().Start;
            var name = ExpectName("an attribute name");
            var args = Kind == TokenKind.OpenParen && !BreaksLine(_pos) ? ParseArguments() : [];
            var attribute = new AttributeSyntax(SpanFrom(start), name, args);
            var because = ParseBecause("@" + name.Text, attribute.Span);
            if (name.Text != "unobservable")
                Report(DiagnosticCodes.StatementAttribute, attribute.Span, ["@" + name.Text], label: "before a statement",
                    help: "remove the attribute; only `@unobservable` may come before a statement");
            else if (because is null)
                ReportMissingBecause("@" + name.Text, attribute.Span);
            attributes.Add(attribute with { Because = because });
        }
        return attributes.ToArray();
    }

    private void ReportMissingBecause(string construct, TextSpan span) =>
        Report(DiagnosticCodes.MissingBecause, span, [construct], label: "bypasses a safety rule",
            help: "explain the bypass in a `because` clause");

    /// <summary>
    /// The alias name and function type of the C# delegate signature <c>R Name(A a, B b)</c> from token <paramref name="index"/>
    /// to the current one, or null when the line is not that signature (generic, or with parameter modifiers).
    /// </summary>
    private (string Name, string Type)? DelegateAlias(int index)
    {
        var returnEnd = ScanType(index);
        if (returnEnd < 0 || !IsName(KindAt(returnEnd)) || KindAt(returnEnd + 1) != TokenKind.OpenParen) return null;
        var types = new List<string>();
        var loneFunction = false;
        var i = returnEnd + 2;
        if (KindAt(i) != TokenKind.CloseParen)
        {
            while (true)
            {
                var typeEnd = ScanType(i, nested: false, out loneFunction);
                if (typeEnd < 0 || !IsName(KindAt(typeEnd))) return null;
                types.Add(TokensText(i, typeEnd));
                i = typeEnd + 1;
                if (KindAt(i) != TokenKind.Comma) break;
                i++;
            }
        }
        if (KindAt(i) != TokenKind.CloseParen) return null;
        i++;
        if (i != _pos && !(KindAt(i) == TokenKind.Semicolon && i + 1 == _pos)) return null;
        // A lone tuple type keeps its own parentheses inside the parameter list's, and a lone function type needs them
        // since `->` is right-associative.
        var parameters = types is [var one] && !one.StartsWith('(') && !loneFunction ? one : "(" + string.Join(", ", types) + ")";
        return (Text(_tokens[returnEnd]), parameters + " -> " + TokensText(index, returnEnd));
    }

    /// <summary>The source text of tokens <paramref name="from"/> up to, not including, <paramref name="to"/>.</summary>
    private string TokensText(int from, int to) => _source.GetText(TextSpan.FromBounds(_tokens[from].Start, _tokens[to - 1].End));

    private bool IsTypeThenName(int index)
    {
        var after = ScanType(index);
        return after > 0 && IsName(KindAt(after));
    }

    private bool IsFunctionStart()
    {
        var after = SkipFailsShorthand(ScanType(_pos));
        if (after <= 0 || !IsName(KindAt(after))) return false;
        if (KindAt(after + 1) == TokenKind.Less && ScanTypeArgs(after + 1) is var close and > 0) return KindAt(close) == TokenKind.OpenParen;
        return KindAt(after + 1) == TokenKind.OpenParen;
    }

    /// <summary>
    /// The input form <c>f(…) -&gt; T</c>, or <c>f(…)</c> with no return type before <c>{</c> or <c>fails</c> on its line
    /// (BYN1043); with <paramref name="allowNoBody"/> (an interface member) only the <c>-&gt;</c> form.
    /// </summary>
    private bool IsNameFirstFunctionStart(bool allowNoBody)
    {
        if (!IsName(Kind)) return false;
        var open = _pos + 1;
        if (KindAt(open) == TokenKind.Less) open = ScanTypeArgs(open);
        if (open <= 0 || KindAt(open) != TokenKind.OpenParen) return false;
        var close = MatchingClose(open);
        if (close < 0) return false;
        var next = close + 1;
        if (KindAt(next) == TokenKind.Arrow) return true;
        return !allowNoBody && !_tokens[Math.Min(next, _count - 1)].HasNewlineBefore && KindAt(next) is TokenKind.OpenBrace or TokenKind.FailsKeyword;
    }

    /// <summary>Whether <c>name:</c> starts at <paramref name="index"/>: the input form <c>name: Type</c> of a declaration (BYN1043).</summary>
    private bool IsNameThenType(int index) => IsName(KindAt(index)) && KindAt(index + 1) == TokenKind.Colon;

    /// <summary>The input form <c>name: Type</c> of a parameter, field or payload (BYN1043); the fix writes <c>Type name</c>.</summary>
    private (TypeSyntax Type, Name Name) ParseNameThenType()
    {
        var name = NameOf(Advance());
        Advance();
        var type = ParseType();
        var span = TextSpan.FromBounds(name.Span.Start, _prevEnd);
        var typeText = _source.GetText(type.Span);
        Report(DiagnosticCodes.TypeNameOrder, span, [$"{name.Text}: {typeText}", $"{typeText} {name.Text}"], label: "name before type",
            help: "put the type before the name; declarations are type-first", fix: new Fix(span, $"{typeText} {name.Text}"));
        return (type, name);
    }

    /// <summary>Past the input form <c>!E</c> or <c>!(A, B)</c> after a return type at <paramref name="index"/>, if any.</summary>
    private int SkipFailsShorthand(int index)
    {
        if (index <= 0 || KindAt(index) != TokenKind.Bang) return index;
        var i = index + 1;
        if (KindAt(i) != TokenKind.OpenParen) return SkipErrorType(i);
        do i = SkipErrorType(i + 1);
        while (i > 0 && KindAt(i) == TokenKind.Comma);
        return i > 0 && KindAt(i) == TokenKind.CloseParen ? i + 1 : -1;
    }

    /// <summary>Past an error type at <paramref name="index"/>: a name with optional type arguments and <c>?</c> (not valid after <c>fails</c>, but still the input form).</summary>
    private int SkipErrorType(int index)
    {
        var i = SkipQualifiedName(index);
        if (i > 0 && KindAt(i) == TokenKind.Less) i = ScanTypeArgs(i);
        if (i > 0 && KindAt(i) == TokenKind.Question) i++;
        return i;
    }

    /// <summary>Past <c>a.b.c</c> at <paramref name="index"/>, or -1 if no name starts there.</summary>
    private int SkipQualifiedName(int index)
    {
        if (!IsName(KindAt(index))) return -1;
        while (KindAt(index + 1) == TokenKind.Dot && IsName(KindAt(index + 2))) index += 2;
        return index + 1;
    }

    /// <summary><c>&lt;T, U&gt;</c> after a declaration name, or nothing.</summary>
    private Name[] ParseTypeParams()
    {
        if (Kind != TokenKind.Less) return [];
        Advance();
        var names = new List<Name> { ExpectName("a type parameter name") };
        while (Accept(TokenKind.Comma)) names.Add(ExpectName("a type parameter name"));
        Expect(TokenKind.Greater);
        return names.ToArray();
    }

    private ImportDecl ParseImport(int start)
    {
        Advance();
        var path = new List<Name> { ExpectName("a module name") };
        while (Accept(TokenKind.Dot)) path.Add(ExpectName("a module name"));
        Name? alias = null;
        if (Accept(TokenKind.AsKeyword)) alias = ExpectName("an alias");
        ExpectEnd();
        return new ImportDecl(SpanFrom(start), path.ToArray(), alias);
    }

    private StructDecl ParseStruct(int start, Modifiers modifiers)
    {
        Advance();
        if (Accept(TokenKind.UniqueKeyword)) modifiers |= Modifiers.Unique;
        var name = ExpectName("a struct name");
        var typeParams = ParseTypeParams();
        var bases = ParseBases();
        var where = ParseWhere();
        var rules = new List<RequiresRule>();
        UnionVariant[]? states = null;
        var stateLines = new List<(Token Keyword, UnionVariant State)>();
        var members = ParseBody("struct", () =>
        {
            if (AtContextual("states") && Peek(1) == TokenKind.OpenBrace)
            {
                var keyword = Current;
                var block = ParseStatesBlock(name.Text);
                if (states is null) states = block;
                else Report(DiagnosticCodes.DuplicateDeclaration, keyword.Span, ["states"], label: "a second `states` block",
                    help: "declare every state in the first `states` block");
                return null;
            }
            if (AtContextual("state") && IsName(Peek(1)) && (Peek(2) == TokenKind.OpenParen || EndsBefore(_pos + 2)))
            {
                var keyword = Advance();
                var stateStart = Current.Start;
                var stateName = ExpectName("a state name");
                var payload = ParsePayload(stateName);
                stateLines.Add((keyword, new UnionVariant(SpanFrom(stateStart), stateName, payload, DocOf(keyword))));
                ExpectEnd();
                return null;
            }
            if (Kind != TokenKind.RequiresKeyword) return ParseDeclaration(topLevel: false, structName: name.Text);
            rules.Add(ParseRequiresRule());
            ExpectEnd();
            return null;
        });
        if (stateLines.Count > 0) ReportStateLines(stateLines, states is not null);
        // O3: a field's rule that names another field, or has `at .f`, belongs to the struct; one that also reads `it` is BYN3218.
        var fields = members.OfType<FieldDecl>().Select(f => f.Name.Text).ToHashSet();
        for (var i = 0; i < members.Length; i++)
        {
            if (members[i] is not FieldDecl { Requires.Length: > 0 } field) continue;
            var mixed = field.Requires.Where(r => NamesAny(r, fields) && NamesAny(r, ItName)).ToArray();
            foreach (var r in mixed)
                Report(DiagnosticCodes.RequiresMixesFields, r.Span, help: "split it: an `it` rule under the field, and a struct-level rule with `at .field`");
            var own = field.Requires.Where(r => r.At is null && !NamesAny(r, fields)).ToArray();
            rules.AddRange(field.Requires.Where(r => !own.Contains(r) && !mixed.Contains(r)));
            members[i] = field with { Requires = own };
        }
        return new StructDecl(SpanFrom(start), modifiers, name, members)
        {
            TypeParams = typeParams, Bases = bases, Where = where, Rules = rules.OrderBy(r => r.Span.Start).ToArray(),
            States = [.. states ?? [], .. stateLines.Select(s => s.State)],
        };
    }

    /// <summary><c>states { a  b(T x) }</c>: one state per line, each with an optional named payload (J58).</summary>
    private UnionVariant[] ParseStatesBlock(string structName)
    {
        Advance();
        var open = Expect(TokenKind.OpenBrace);
        var states = new List<UnionVariant>();
        var commas = new List<Token>();
        while (IsName(Kind))
        {
            var doc = DocOf(Current);
            var stateStart = Current.Start;
            var state = ExpectName("a state name");
            var payload = ParsePayload(state);
            states.Add(new UnionVariant(SpanFrom(stateStart), state, payload, doc));
            if (!NextCase(commas)) break;
        }
        while (Kind == TokenKind.Semicolon) ReportSemicolon(Advance());
        ReportCaseCommas(structName, open, states.Select(s => s.Span).ToList(), commas);
        ExpectClose(open, "states block");
        ExpectEnd();
        return states.ToArray();
    }

    /// <summary>
    /// BYN1059 at each <c>state x</c> line. Every line shares one fix: the lines leave their places and one
    /// <c>states</c> block takes the place of the last, after the members between them. No fix when the struct
    /// already has a block, or a line shares its line with other code.
    /// </summary>
    private void ReportStateLines(List<(Token Keyword, UnionVariant State)> lines, bool hasBlock)
    {
        // The whole line of each `state x`, through its line break; -1 when other code or a comment shares the line.
        (int Start, int End) Segment(Token keyword, UnionVariant state)
        {
            var bytes = _source.Bytes.Span;
            var start = _source.GetLineSpan(_source.GetLineIndex(keyword.Start)).Start;
            for (var i = start; i < keyword.Start; i++)
                if (bytes[i] is not ((byte)' ' or (byte)'\t')) return (-1, -1);
            for (var i = state.Span.End; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\n') return (start, i + 1);
                if (bytes[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r')) return (-1, -1);
            }
            return (-1, -1);
        }
        Fix? fix = null;
        var segments = lines.Select(l => Segment(l.Keyword, l.State)).ToList();
        if (!hasBlock && segments.All(s => s.Start >= 0))
        {
            var indent = IndentAt(lines[0].Keyword.Start);
            var block = new StringBuilder();
            for (var i = 0; i + 1 < segments.Count; i++)
                block.Append(_source.GetText(TextSpan.FromBounds(segments[i].End, segments[i + 1].Start)));
            block.Append(indent).Append("states {\n");
            foreach (var (_, state) in lines) block.Append(indent).Append("    ").Append(_source.GetText(state.Span)).Append('\n');
            block.Append(indent).Append("}\n");
            fix = new Fix(TextSpan.FromBounds(segments[0].Start, segments[^1].End), block.ToString());
        }
        foreach (var (keyword, state) in lines)
            Report(DiagnosticCodes.StateLineInput, TextSpan.FromBounds(keyword.Start, state.Span.End), [_source.GetText(state.Span)],
                label: "the input form", help: "move each state into the struct's `states` block",
                fix: fix);
    }

    private static readonly HashSet<string> ItName = ["it"];

    /// <summary>Whether <paramref name="rule"/>'s condition names one of <paramref name="fields"/> (not after a <c>.</c>).</summary>
    private bool NamesAny(RequiresRule rule, HashSet<string> fields)
    {
        for (var i = TokenAt(rule.Condition.Span.Start); i < _count && _tokens[i].Start < rule.Condition.Span.End; i++)
            if (_tokens[i].Kind == TokenKind.Identifier && KindAt(i - 1) != TokenKind.Dot && fields.Contains(Text(_tokens[i]))) return true;
        return false;
    }

    /// <summary>The index of the first token starting at or after <paramref name="offset"/>.</summary>
    private int TokenAt(int offset)
    {
        int lo = 0, hi = _count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_tokens[mid].Start < offset) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>
    /// <c>requires condition</c>, or <c>requires condition at .field</c>; <c>it</c> is the value checked, and a bare
    /// <c>value</c> is the BYN1050 input form.
    /// </summary>
    private RequiresRule ParseRequiresRule()
    {
        var start = Advance().Start;
        var first = _pos;
        var condition = ParseExpression();
        for (var i = first; i < _pos; i++)
            if (_tokens[i].Kind == TokenKind.Identifier && Text(_tokens[i]) == "value" && KindAt(i - 1) != TokenKind.Dot)
                Report(DiagnosticCodes.RequiresValue, _tokens[i].Span, label: "the input form", help: "rename `value` to `it`",
                    fix: new Fix(_tokens[i].Span, "it"));
        Name? at = null;
        if (AtContextual("at") && !BreaksLine(_pos) && Peek(1) == TokenKind.Dot)
        {
            Advance();
            Advance();
            at = ExpectName("a field name");
        }
        return new RequiresRule(SpanFrom(start), condition, at);
    }

    /// <summary><c>{ members }</c> of a struct, interface or extension.</summary>
    private Decl[] ParseBody(string what, Func<Decl?> parseMember)
    {
        var open = Expect(TokenKind.OpenBrace);
        var members = new List<Decl>();
        while (Kind != TokenKind.CloseBrace && !AtEnd)
        {
            var before = _pos;
            var errors = _syntaxErrors;
            var member = parseMember();
            if (member is not null) members.Add(member);
            RecoverIfNeeded(errors);
            if (_pos == before) Advance();
        }
        ExpectClose(open, what);
        return members.ToArray();
    }

    /// <summary><c>: I, J</c> after a type's name, or nothing.</summary>
    private NamedType[] ParseBases()
    {
        if (!Accept(TokenKind.Colon)) return [];
        var bases = new List<NamedType> { ParseNamedType() };
        while (Accept(TokenKind.Comma)) bases.Add(ParseNamedType());
        return bases.ToArray();
    }

    /// <summary><c>where T : IA, IB</c>, repeated, or nothing.</summary>
    private WhereClause[] ParseWhere()
    {
        var clauses = new List<WhereClause>();
        while (AtContextual("where"))
        {
            var start = Advance().Start;
            var param = ExpectName("a type parameter name");
            Expect(TokenKind.Colon);
            var bounds = new List<NamedType> { ParseNamedType() };
            while (Accept(TokenKind.Comma)) bounds.Add(ParseNamedType());
            clauses.Add(new WhereClause(SpanFrom(start), param, bounds.ToArray()));
        }
        return clauses.ToArray();
    }

    private InterfaceDecl ParseInterface(int start, Modifiers modifiers)
    {
        Advance();
        var name = ExpectName("an interface name");
        var typeParams = ParseTypeParams();
        var where = ParseWhere();
        var members = ParseBody("interface", ParseInterfaceMember);
        return new InterfaceDecl(SpanFrom(start), modifiers, name, members) { TypeParams = typeParams, Where = where };
    }

    /// <summary>A method (abstract when the line ends after its header), <c>T name</c>, <c>T name =&gt; e</c> or <c>type Name</c>; <c>T name { get }</c> is BYN1060.</summary>
    private Decl? ParseInterfaceMember()
    {
        var first = Current;
        var doc = DocOf(first);
        var startIndex = _pos;
        var start = first.Start;
        var attributes = ParseAttributes();
        doc ??= DocOf(Current);
        var modifiers = ParseModifiers();
        RejectModifiers(modifiers, Modifiers.Static, startIndex);

        Decl decl;
        var after = ScanType(_pos);
        if (Kind == TokenKind.TypeKeyword && IsName(Peek(1)) && EndsBefore(_pos + 2))
        {
            Advance();
            var name = ExpectName();
            ExpectEnd();
            decl = new AssociatedTypeDecl(SpanFrom(start), name);
        }
        else if (IsFunctionStart() || IsNameFirstFunctionStart(allowNoBody: true))
        {
            decl = ParseFunction(start, modifiers, allowNoBody: true);
        }
        else if (after > 0 && IsName(KindAt(after)) && KindAt(after + 1) == TokenKind.OpenBrace)
        {
            var type = ParseType();
            var name = ExpectName();
            var open = Expect(TokenKind.OpenBrace);
            var get = AtContextual("get");
            if (get) Advance();
            else Report(DiagnosticCodes.Expected, Current.Span, ["`get`", Describe(Current)]);
            ExpectEnd();
            Expect(TokenKind.CloseBrace);
            if (get)
            {
                var typeText = _source.GetText(type.Span);
                Report(DiagnosticCodes.InterfaceGetInput, TextSpan.FromBounds(open.Start, _prevEnd), [typeText, name.Text],
                    label: "the input form", help: "remove `{ get }`: a field-shaped interface member is a read-only property",
                    fix: new Fix(TextSpan.FromBounds(name.Span.End, _prevEnd), ""));
            }
            decl = new GetterDecl(SpanFrom(start), modifiers, type, name);
        }
        else if (after > 0 && IsName(KindAt(after)) && EndsBefore(after + 1))
        {
            var type = ParseType();
            var name = ExpectName();
            ExpectEnd();
            decl = new GetterDecl(SpanFrom(start), modifiers, type, name);
        }
        else if (after > 0 && IsName(KindAt(after)) && KindAt(after + 1) == TokenKind.FatArrow)
        {
            decl = ParseFieldOrProperty(start, modifiers);
        }
        else
        {
            Report(DiagnosticCodes.Expected, Current.Span, ["a method, getter or `type Name`", Describe(Current)]);
            Synchronize();
            return null;
        }
        return decl with { Doc = doc, Attributes = attributes };
    }

    /// <summary><c>extension T : I { … }</c> or <c>extension T : I</c> alone on its line.</summary>
    private ExtensionDecl ParseExtension(int start)
    {
        Advance();
        var name = ExpectName("a type name");
        var bases = ParseBases();
        Decl[] members = [];
        if (Kind != TokenKind.OpenBrace && EndsBefore(_pos)) ExpectEnd();
        else members = ParseBody("extension", () => ParseDeclaration(topLevel: false, structName: name.Text));
        return new ExtensionDecl(SpanFrom(start), name, bases, members);
    }

    private void ExpectClose(Token open, string what)
    {
        if (Accept(TokenKind.CloseBrace)) return;
        var (line, _) = _source.GetLinePosition(open.Start);
        Report(DiagnosticCodes.Expected, Current.Span, ["`}`", Describe(Current)],
            help: open.Length > 0 ? $"close the {what} opened on line {line}" : null);
    }

    private Decl ParseFieldOrProperty(int start, Modifiers modifiers)
    {
        TypeSyntax type;
        Name name;
        if (IsNameThenType(_pos))
        {
            (type, name) = ParseNameThenType();
        }
        else if (IsTypeThenName(_pos))
        {
            type = ParseType();
            name = ExpectName();
            type = ParseRefinement(type, name);
        }
        else
        {
            Report(DiagnosticCodes.ExpectedMember, Current.Span, [Describe(Current)]);
            Synchronize();
            return new GlobalStatement(SpanFrom(start), new ErrorStmt(SpanFrom(start)));
        }
        if (Accept(TokenKind.FatArrow))
        {
            var body = ParseExpression();
            ExpectEnd();
            return new PropertyDecl(SpanFrom(start), modifiers, type, name, body);
        }
        if (Kind == TokenKind.OpenBrace)
            return new PropertyDecl(SpanFrom(start), modifiers, type, name, null) { Block = ParseBlock() };
        Expr? init = null;
        if (Accept(TokenKind.Equals)) init = ParseExpression();
        var requires = ParseRequiresLines();
        ExpectEnd();
        return new FieldDecl(SpanFrom(start), modifiers, type, name, init) { Requires = requires };
    }

    /// <summary>The <c>requires</c> rules of a field or type: on its line, then one per line with no blank line between.</summary>
    private RequiresRule[] ParseRequiresLines()
    {
        var rules = new List<RequiresRule>();
        while (Kind == TokenKind.RequiresKeyword && !HasBlankLineBefore(_pos)) rules.Add(ParseRequiresRule());
        return rules.ToArray();
    }

    private ConstructorDecl ParseConstructor(int start, Modifiers modifiers)
    {
        var name = NameOf(Advance());
        var parameters = ParseParams();
        var body = ParseBlock();
        return new ConstructorDecl(SpanFrom(start), modifiers, name, parameters, body);
    }

    /// <param name="allowNoBody">An interface member: no body (the line ends after the header) declares it abstract.</param>
    private FunctionDecl ParseFunction(int start, Modifiers modifiers, bool allowNoBody = false)
    {
        var nameFirst = !IsFunctionStart();
        var returnType = nameFirst ? null : ParseType();
        var typeEnd = _prevEnd;
        var shorthand = new List<TypeSyntax>();
        var bang = Current;
        if (!nameFirst && Accept(TokenKind.Bang))
        {
            if (Accept(TokenKind.OpenParen))
            {
                do shorthand.Add(ParseType());
                while (Accept(TokenKind.Comma));
                Expect(TokenKind.CloseParen);
            }
            else
            {
                shorthand.Add(ParseType());
            }
        }
        var shorthandEnd = _prevEnd;
        var name = ExpectName();
        var typeParams = ParseTypeParams();
        var paramsStart = Current.End;
        var parameters = ParseParams(out var refThis, out var moveThis, out var receiverStates);
        var paramsEnd = _prevEnd;
        returnType ??= ParseNameFirstReturn(name, paramsEnd);
        var where = new List<WhereClause>();
        var requires = new List<Expr>();
        Name? becomes = null;
        FailsClause? fails = null;
        UsesClause? uses = null;
        var clauses = new List<(int Rank, Token Keyword, int End)>();
        ReportRemovedEffects();
        while (true)
        {
            var keyword = Current;
            int rank;
            if (AtContextual("where"))
            {
                rank = 0;
                where.AddRange(ParseWhere());
            }
            else if (AtContextual("expects") || Kind == TokenKind.RequiresKeyword)
            {
                rank = 1;
                Advance();
                if (keyword.Kind == TokenKind.RequiresKeyword)
                    Report(DiagnosticCodes.RequiresOnFunction, keyword.Span, label: "the input form",
                        help: "use `expects` for a precondition; `requires` is checked at construction", fix: new Fix(keyword.Span, "expects"));
                requires.Add(WithoutLambdas(ParseBareCondition));
            }
            else if (AtContextual("becomes") && Peek(1) == TokenKind.Dot)
            {
                rank = 2;
                Advance();
                Advance();
                becomes = ExpectName("a state name");
            }
            else if (Kind == TokenKind.FailsKeyword)
            {
                rank = 3;
                var failsStart = Advance().Start;
                var errors = new List<TypeSyntax>();
                if (Kind == TokenKind.Identifier && !AtContextual("where") && !AtContextual("expects") && !(AtContextual("becomes") && Peek(1) == TokenKind.Dot))
                {
                    errors.Add(ParseType());
                    while (Accept(TokenKind.Comma)) errors.Add(ParseType());
                }
                fails = new FailsClause(SpanFrom(failsStart), errors.ToArray());
            }
            else if (Kind == TokenKind.UsesKeyword)
            {
                rank = 4;
                var usesStart = Advance().Start;
                var effects = new List<Name> { ExpectName("an effect name") };
                while (Accept(TokenKind.Comma)) effects.Add(ExpectName("an effect name"));
                uses = new UsesClause(SpanFrom(usesStart), effects.ToArray());
            }
            else break;
            clauses.Add((rank, keyword, _prevEnd));
            ReportRemovedEffects();
        }
        ReportClauseOrder(clauses);
        if (shorthand.Count > 0)
        {
            // `T!E f() fails F` keeps both sets; the fix rewrites the header through the explicit clause.
            var all = shorthand.Concat(fails?.Errors ?? []).DistinctBy(t => _source.GetText(t.Span)).ToArray();
            var errors = string.Join(", ", all.Select(t => _source.GetText(t.Span)));
            // `fails` takes error type names (spec §8): `E?` and `Result<E>` get no fix rather than a wrong one.
            var plain = all.All(t => t is NamedType { Args.Length: 0 });
            Report(DiagnosticCodes.FailsShorthand, TextSpan.FromBounds(bang.Start, shorthandEnd), label: "the input form",
                help: plain ? $"write `fails {errors}` after the parameters" : "write the error types in a `fails` clause after the parameters",
                fix: !plain ? null : new Fix(TextSpan.FromBounds(typeEnd, fails?.Span.End ?? paramsEnd), " " + _source.GetText(TextSpan.FromBounds(name.Span.Start, paramsEnd)) + " fails " + errors));
            fails = new FailsClause(fails?.Span ?? TextSpan.FromBounds(bang.Start, shorthandEnd), all);
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
            ExpectEnd();
        }
        else if (allowNoBody && EndsBefore(_pos))
        {
            ExpectEnd();
        }
        else
        {
            Expect(TokenKind.OpenBrace);
        }
        return new FunctionDecl(SpanFrom(start), modifiers, returnType, name, parameters, fails, body, expressionBody)
        {
            TypeParams = typeParams, RefThis = refThis, MoveThis = moveThis, ReceiverStates = receiverStates, ParamsStart = paramsStart,
            Requires = requires.ToArray(), Where = where.ToArray(), Becomes = becomes, Uses = uses,
        };
    }

    /// <summary>
    /// BYN1065 at the first signature clause that follows one of a later rank (<c>where</c>, <c>expects</c>,
    /// <c>becomes</c>, <c>fails</c>, <c>uses</c>); the fix sorts the clauses and keeps the gaps between them.
    /// </summary>
    private void ReportClauseOrder(List<(int Rank, Token Keyword, int End)> clauses)
    {
        for (var i = 1; i < clauses.Count; i++)
        {
            var before = clauses.Take(i).FirstOrDefault(c => c.Rank > clauses[i].Rank);
            if (before.End == 0) continue;
            var texts = clauses.Select(c => _source.GetText(TextSpan.FromBounds(c.Keyword.Start, c.End))).ToArray();
            var sorted = clauses.Select((c, k) => (c.Rank, Text: texts[k])).OrderBy(c => c.Rank).ToArray();
            var sb = new StringBuilder(sorted[0].Text);
            for (var k = 1; k < clauses.Count; k++)
                sb.Append(_source.GetText(TextSpan.FromBounds(clauses[k - 1].End, clauses[k].Keyword.Start))).Append(sorted[k].Text);
            var moved = Text(clauses[i].Keyword);
            Report(DiagnosticCodes.ClauseOrder, clauses[i].Keyword.Span, [moved, Text(before.Keyword)], label: "out of order",
                help: $"move `{moved}` before `{Text(before.Keyword)}`",
                fix: new Fix(TextSpan.FromBounds(clauses[0].Keyword.Start, clauses[^1].End), sb.ToString()));
            return;
        }
    }

    private static readonly HashSet<string> Particles = ["from", "to", "until", "through", "at", "by", "for", "as"];

    /// <summary>
    /// The particle that leads a parameter (<c>to ref Span&lt;u8&gt; dst</c>): <c>for</c>, <c>as</c>, or a word followed by a
    /// passing mode or by a type and a name. A word that is not a particle is BYN5008.
    /// </summary>
    private Name? ParseParticle()
    {
        if (Kind is not (TokenKind.ForKeyword or TokenKind.AsKeyword))
        {
            if (Kind != TokenKind.Identifier || Text(_tokens[_pos]) == "dyn") return null;
            if (Peek(1) is not (TokenKind.RefKeyword or TokenKind.MoveKeyword or TokenKind.OutKeyword or TokenKind.InKeyword)
                && !IsTypeThenName(_pos + 1)) return null;
        }
        var word = NameOf(Advance());
        if (Particles.Contains(word.Text)) return word;
        Report(DiagnosticCodes.UnknownParticle, word.Span, [word.Text], label: "not a particle",
            help: "choose one of `from`, `to`, `until`, `through`, `at`, `by`, `for`, `as`");
        return null;
    }

    /// <summary>The removed effect words <c>noalloc</c>, <c>nosuspend</c> and <c>suspends</c> after a signature (BYN6001); the fix deletes each.</summary>
    private void ReportRemovedEffects()
    {
        while (Kind is TokenKind.NoallocKeyword or TokenKind.NosuspendKeyword or TokenKind.SuspendsKeyword)
        {
            var word = Advance();
            var text = Text(word);
            Report(DiagnosticCodes.NoallocRemoved, word.Span, [text], label: "removed", help: $"remove `{text}`",
                fix: new Fix(TextSpan.FromBounds(_tokens[_pos - 2].End, word.End), ""));
        }
    }

    /// <summary>
    /// The return type of the input form <c>f(…) -&gt; T</c>, or <c>void</c> when none is written (BYN1043); the fix
    /// moves it before the name.
    /// </summary>
    private TypeSyntax ParseNameFirstReturn(Name name, int paramsEnd)
    {
        var header = _source.GetText(TextSpan.FromBounds(name.Span.Start, paramsEnd));
        if (Kind == TokenKind.Arrow)
        {
            var arrow = Advance();
            var type = ParseType();
            var typeText = _source.GetText(type.Span);
            Report(DiagnosticCodes.TypeNameOrder, arrow.Span, [$"{name.Text}(…) -> {typeText}", $"{typeText} {name.Text}(…)"],
                label: "return type after the parameters", help: "put the return type before the name",
                fix: new Fix(TextSpan.FromBounds(name.Span.Start, _prevEnd), $"{typeText} {header}"));
            return type;
        }
        Report(DiagnosticCodes.TypeNameOrder, name.Span, [$"{name.Text}(…)", $"void {name.Text}(…)"], label: "no return type",
            help: "put `void` before the name when the function returns nothing", fix: new Fix(new TextSpan(name.Span.Start, 0), "void "));
        var at = new TextSpan(name.Span.Start, 0);
        return new NamedType(at, [new Name(at, "void")], []);
    }

    private Param[] ParseParams() => ParseParams(out _, out _, out _, allowRefThis: false);

    /// <summary>
    /// A member's parameters; <c>ref this</c> is accepted only first (BYN2117) and returned in <paramref name="refThis"/>, a
    /// first <c>move this</c> in <paramref name="moveThis"/>. A first receiver <c>this is .s</c>, <c>ref this is .s</c> or
    /// <c>move this is .s</c> returns its states in <paramref name="receiverStates"/>; a bare <c>this</c> is BYN1061.
    /// </summary>
    private Param[] ParseParams(out TextSpan? refThis, out TextSpan? moveThis, out Name[] receiverStates, bool allowRefThis = true)
    {
        refThis = null;
        moveThis = null;
        receiverStates = [];
        Expect(TokenKind.OpenParen);
        var list = new List<Param>();
        if (Kind != TokenKind.CloseParen)
        {
            do
            {
                var start = Current.Start;
                if (allowRefThis && Kind == TokenKind.RefKeyword && KindAt(_pos + 1) == TokenKind.ThisKeyword)
                {
                    Advance();
                    Advance();
                    var span = SpanFrom(start);
                    if (list.Count > 0 || refThis is not null || moveThis is not null)
                        Report(DiagnosticCodes.RefThisNotFirst, span, [], label: "after other parameters",
                            help: "move `ref this` to the start of the parameter list");
                    refThis ??= span;
                    if (list.Count == 0 && receiverStates.Length == 0) receiverStates = ParseReceiverState();
                    continue;
                }
                var first = list.Count == 0 && refThis is null && moveThis is null && receiverStates.Length == 0;
                if (allowRefThis && first && Kind == TokenKind.MoveKeyword && KindAt(_pos + 1) == TokenKind.ThisKeyword)
                {
                    Advance();
                    Advance();
                    moveThis = SpanFrom(start);
                    receiverStates = ParseReceiverState();
                    continue;
                }
                if (allowRefThis && first && Kind == TokenKind.ThisKeyword && KindAt(_pos + 1) == TokenKind.IsKeyword)
                {
                    Advance();
                    receiverStates = ParseReceiverState();
                    continue;
                }
                if (allowRefThis && first && Kind == TokenKind.ThisKeyword && KindAt(_pos + 1) is TokenKind.Comma or TokenKind.CloseParen)
                {
                    var self = Advance();
                    // `this, a` loses `this, `; `(this)` loses `this`.
                    var to = Kind == TokenKind.Comma ? PeekToken(1).Start : self.End;
                    Report(DiagnosticCodes.ReceiverWithoutState, self.Span, label: "the default receiver",
                        help: "remove `this`, or add a state: `this is .s`", fix: new Fix(TextSpan.FromBounds(self.Start, to), ""));
                    continue;
                }
                var rest = AtContextual("rest") && IsTypeThenName(_pos + 1);
                if (rest) Advance();
                else if (AtContextual("params") && IsVariadicArray(_pos + 1))
                {
                    list.Add(ParseParamsInput(start));
                    continue;
                }
                var particle = rest ? null : ParseParticle();
                var modifier = Kind switch
                {
                    TokenKind.RefKeyword => ParamModifier.Ref,
                    TokenKind.MoveKeyword => ParamModifier.Move,
                    TokenKind.OutKeyword => ParamModifier.Out,
                    TokenKind.InKeyword => ParamModifier.In,
                    _ => ParamModifier.None,
                };
                if (modifier != ParamModifier.None) Advance();
                TypeSyntax type;
                Name name;
                if (IsNameThenType(_pos)) (type, name) = ParseNameThenType();
                else
                {
                    type = ParseType(fatArrow: true, lists: true);
                    if (Kind == TokenKind.DotDot && Peek(1) == TokenKind.Dot && !PeekToken(1).HasTriviaBefore && IsName(Peek(2)))
                    {
                        list.Add(ParseEllipsisInput(start, type));
                        continue;
                    }
                    name = ExpectName("a parameter name");
                }
                type = ParseRefinement(type, name);
                Expr? defaultValue = null;
                if (Accept(TokenKind.Equals)) defaultValue = ParseExpression();
                var param = new Param(SpanFrom(start), modifier, type, name, defaultValue) { Particle = particle, Rest = rest };
                // `rest` is the last positional parameter; only named optional parameters may follow it (SPEC §10).
                if (list.Any(p => p.Rest) && (rest || defaultValue is null))
                    Report(DiagnosticCodes.Expected, param.Span, ["a named optional parameter after `rest`", $"`{_source.GetText(param.Span)}`"],
                        label: rest ? "a second `rest` parameter" : "a positional parameter after `rest`",
                        help: rest ? "keep one `rest` parameter" : "give it a default value, or move it before the `rest` parameter");
                list.Add(param);
            } while (Accept(TokenKind.Comma));
        }
        Expect(TokenKind.CloseParen);
        return list.ToArray();
    }

    /// <summary>The <c>is .a or .b</c> after a receiver's <c>this</c>, if written.</summary>
    private Name[] ParseReceiverState() => Accept(TokenKind.IsKeyword) ? ParseStateList() : [];

    /// <summary><c>.a or .b</c> after <c>is</c>: the states of a receiver or a refinement.</summary>
    private Name[] ParseStateList()
    {
        var states = new List<Name>();
        do
        {
            Expect(TokenKind.Dot);
            states.Add(ExpectName("a state name"));
        } while (Accept(TokenKind.OrKeyword));
        return states.ToArray();
    }

    /// <summary>
    /// <c>is .a or .b</c> after a parameter or field <paramref name="name"/>, or after the type of a local annotation or
    /// alias, where <paramref name="name"/> is null (J57), wrapping <paramref name="type"/>; one state is BYN7006, fixed
    /// to <c>T.s name</c> or <c>T.s</c>.
    /// </summary>
    private TypeSyntax ParseRefinement(TypeSyntax type, Name? name = null)
    {
        if (Kind != TokenKind.IsKeyword || Peek(1) != TokenKind.Dot) return type;
        var keyword = Advance();
        var states = ParseStateList();
        if (states.Length == 1)
        {
            var typeText = _source.GetText(type.Span);
            var stateType = $"{typeText}.{states[0].Text}";
            var written = name is null ? typeText : $"{typeText} {name.Text}";
            var rewrite = name is null ? stateType : $"{stateType} {name.Text}";
            Report(DiagnosticCodes.SingleStateInline, keyword.Span, [written, states[0].Text, rewrite], label: "one state",
                help: $"write the state type `{stateType}`",
                fix: new Fix(TextSpan.FromBounds(type.Span.Start, _prevEnd), rewrite));
        }
        return new RefinedType(TextSpan.FromBounds(type.Span.Start, _prevEnd), type, states);
    }

    /// <summary>Whether <c>T[] name</c> starts at <paramref name="index"/>: the C# <c>params</c> array.</summary>
    private bool IsVariadicArray(int index)
    {
        var end = ScanType(index);
        return end > 0 && KindAt(end) == TokenKind.OpenBracket && KindAt(end + 1) == TokenKind.CloseBracket && IsName(KindAt(end + 2));
    }

    /// <summary><c>params T[] xs</c>, the BYN1058 input form for <c>rest ReadOnlySpan&lt;T&gt; xs</c>.</summary>
    private Param ParseParamsInput(int start)
    {
        var keyword = Advance();
        var element = ParseType();
        Advance();
        Advance();
        var typeEnd = _prevEnd;
        var name = ExpectName("a parameter name");
        return ReportRestInput(start, keyword.Span, element, typeEnd, name);
    }

    /// <summary><c>T... xs</c>, the BYN1058 input form for <c>rest ReadOnlySpan&lt;T&gt; xs</c>; the parser is at the <c>...</c>.</summary>
    private Param ParseEllipsisInput(int start, TypeSyntax element)
    {
        var dots = TextSpan.FromBounds(Current.Start, PeekToken(1).End);
        Advance();
        Advance();
        var typeEnd = _prevEnd;
        var name = ExpectName("a parameter name");
        return ReportRestInput(start, dots, element, typeEnd, name);
    }

    private Param ReportRestInput(int start, TextSpan at, TypeSyntax element, int typeEnd, Name name)
    {
        var elementText = _source.GetText(element.Span);
        Report(DiagnosticCodes.RestInput, at, [_source.GetText(TextSpan.FromBounds(start, name.Span.End)), elementText, name.Text],
            label: "the input form", help: "declare the variadic parameter with `rest`",
            fix: new Fix(TextSpan.FromBounds(start, typeEnd), $"rest ReadOnlySpan<{elementText}>"));
        var span = TextSpan.FromBounds(element.Span.Start, typeEnd);
        var type = new NamedType(span, [new Name(span, "ReadOnlySpan")], [element]);
        return new Param(SpanFrom(start), ParamModifier.None, type, name, null) { Rest = true };
    }

    /// <summary>
    /// <c>union error Name { a b }</c>, one case per line, or <c>union error Name = A, B except .c</c>, a composition.
    /// <c>error enum</c> and <c>error union</c> are input forms (BYN1044); <c>error set</c> is BYN1029.
    /// </summary>
    private Decl ParseErrorUnion(int start, Modifiers modifiers)
    {
        var first = Advance();
        var second = Advance();
        if (first.Kind == TokenKind.ErrorKeyword && second.Kind != TokenKind.SetKeyword) ReportKeywordOrder(first, second, "union error");
        var name = ExpectName("an error set name");
        if (Kind == TokenKind.Equals || second.Kind == TokenKind.SetKeyword) return ParseErrorComposition(start, modifiers, first, second, name);
        var open = Expect(TokenKind.OpenBrace);
        var variants = new List<ErrorVariant>();
        var cases = new List<TextSpan>();
        var commas = new List<Token>();
        while (IsName(Kind) || IsAttributeStart())
        {
            var caseStart = Current;
            var attributes = ParseAttributes();
            var doc = DocOf(caseStart) ?? DocOf(Current);
            var variant = ExpectName("a variant name");
            var fields = ParsePayload(variant);
            variants.Add(new ErrorVariant(variant.Span, variant, doc) { Attributes = attributes, Fields = fields });
            cases.Add(TextSpan.FromBounds(caseStart.Start, _prevEnd));
            if (!NextCase(commas)) break;
        }
        ReportCaseCommas(name.Text, open, cases, commas);
        ExpectClose(open, "union error");
        return new ErrorEnumDecl(SpanFrom(start), modifiers, name, variants.ToArray());
    }

    /// <summary>
    /// <c>union error Name = A, B</c>, optionally narrowed by <c>except .a, .b</c>. The round-2 <c>error set Name = A | B</c>
    /// is the input form (BYN1029); its fix writes <c>union error</c> and <c>,</c> between the members.
    /// </summary>
    private ErrorSetDecl ParseErrorComposition(int start, Modifiers modifiers, Token first, Token second, Name name)
    {
        Expect(TokenKind.Equals);
        var members = new List<TypeSyntax> { ParseType() };
        var pipes = new List<TextSpan>();
        while (Kind is TokenKind.Comma or TokenKind.Bar)
        {
            var memberEnd = _prevEnd;
            if (Advance().Kind == TokenKind.Bar) pipes.Add(TextSpan.FromBounds(memberEnd, _prevEnd));
            members.Add(ParseType());
        }
        var except = new List<Name>();
        if (Kind == TokenKind.ExceptKeyword)
        {
            Advance();
            Expect(TokenKind.Dot);
            except.Add(ExpectName("a case name"));
            while (Kind == TokenKind.Comma && Peek(1) == TokenKind.Dot)
            {
                Advance();
                Advance();
                except.Add(ExpectName("a case name"));
            }
        }
        if (second.Kind == TokenKind.SetKeyword || pipes.Count > 0)
        {
            var text = new StringBuilder(second.Kind == TokenKind.SetKeyword ? "union error" : _source.GetText(TextSpan.FromBounds(first.Start, second.End)));
            var at = second.End;
            foreach (var pipe in pipes)
            {
                text.Append(_source.GetText(TextSpan.FromBounds(at, pipe.Start))).Append(',');
                at = pipe.End;
            }
            text.Append(_source.GetText(TextSpan.FromBounds(at, _prevEnd)));
            var where = second.Kind == TokenKind.SetKeyword ? TextSpan.FromBounds(first.Start, second.End) : pipes[0];
            Report(DiagnosticCodes.ErrorSetPipe, where, [name.Text], label: "the input form", help: "write `union error`, with `,` between the members",
                fix: new Fix(TextSpan.FromBounds(first.Start, _prevEnd), text.ToString()));
        }
        ExpectEnd();
        return new ErrorSetDecl(SpanFrom(start), modifiers, name, members.ToArray())
        {
            Head = TextSpan.FromBounds(first.Start, second.End), Except = except.ToArray(),
        };
    }

    /// <summary>
    /// BYN1044 for a modifier word before the declaration kind (<c>unique struct</c>, <c>distinct type</c>, <c>error enum</c>,
    /// <c>error union</c>); the fix puts the kind first, keeping any words written between them.
    /// </summary>
    private void ReportKeywordOrder(Token modifier, Token kind, string canonical)
    {
        var span = TextSpan.FromBounds(modifier.Start, kind.End);
        var between = _source.GetText(TextSpan.FromBounds(modifier.End, kind.Start)).TrimStart();
        Report(DiagnosticCodes.DeclarationKeywordOrder, span, [$"{Text(modifier)} {Text(kind)}", canonical], label: "the input form",
            help: $"put the kind first: `{canonical}`", fix: new Fix(span, between + canonical));
    }

    /// <summary>
    /// <c>enum Name { a b c }</c>, one member per line, or <c>enum Name(T x) { a(1) b(2) }</c>; members separated by
    /// <c>,</c> are the input form (BYN1053), and a <c>;</c> after the last is BYN1040.
    /// </summary>
    private EnumDecl ParseEnum(int start, Modifiers modifiers)
    {
        Advance();
        var name = ExpectName("an enum name");
        var parameters = Kind == TokenKind.OpenParen ? ParseParams() : null;
        var bases = ParseBases();
        var where = ParseWhere();
        var open = Expect(TokenKind.OpenBrace);
        var members = new List<EnumMember>();
        var commas = new List<Token>();
        while (IsName(Kind))
        {
            var doc = DocOf(Current);
            var memberStart = Current.Start;
            var member = ExpectName("a member name");
            var args = Kind == TokenKind.OpenParen ? ParseArguments() : null;
            members.Add(new EnumMember(SpanFrom(memberStart), member, doc, args));
            if (!NextCase(commas)) break;
        }
        while (Kind == TokenKind.Semicolon) ReportSemicolon(Advance());
        ReportCaseCommas(name.Text, open, members.Select(m => m.Span).ToList(), commas);
        ExpectClose(open, "enum");
        return new EnumDecl(SpanFrom(start), modifiers, name, parameters, members.ToArray()) { Bases = bases, Where = where };
    }

    /// <summary>
    /// <c>union Name { circle(f32 r) rect(f32 w, f32 h) none }</c> or <c>union Mode { read = 1 write = 2 }</c>, one
    /// variant per line; variants separated by <c>,</c> are the input form (BYN1053), and a <c>;</c> after the last is BYN1040.
    /// </summary>
    private UnionDecl ParseUnion(int start, Modifiers modifiers)
    {
        Advance();
        var name = ExpectName("a union name");
        var typeParams = ParseTypeParams();
        var bases = ParseBases();
        var where = ParseWhere();
        var open = Expect(TokenKind.OpenBrace);
        var variants = new List<UnionVariant>();
        var commas = new List<Token>();
        while (IsName(Kind))
        {
            var doc = DocOf(Current);
            var variantStart = Current.Start;
            var variant = ExpectName("a variant name");
            var fields = ParsePayload(variant);
            var value = Accept(TokenKind.Equals) ? ParseExpression() : null;
            variants.Add(new UnionVariant(SpanFrom(variantStart), variant, fields, doc, value));
            if (!NextCase(commas)) break;
        }
        while (Kind == TokenKind.Semicolon) ReportSemicolon(Advance());
        ReportCaseCommas(name.Text, open, variants.Select(v => v.Span).ToList(), commas);
        ExpectClose(open, "union");
        return new UnionDecl(SpanFrom(start), modifiers, name, variants.ToArray()) { TypeParams = typeParams, Bases = bases, Where = where };
    }

    /// <summary>The <c>(T name, …)</c> payload of a union variant or a state, if written; a type without a name is BYN1064.</summary>
    private UnionField[] ParsePayload(Name variant)
    {
        if (Kind != TokenKind.OpenParen) return [];
        Advance();
        var fields = new List<UnionField>();
        while (Kind != TokenKind.CloseParen && !AtEnd)
        {
            var before = _pos;
            var fieldStart = Current.Start;
            TypeSyntax type;
            Name field;
            if (IsNameThenType(_pos)) (type, field) = ParseNameThenType();
            else
            {
                type = ParseType();
                if (Kind is TokenKind.Comma or TokenKind.CloseParen && _pos != before)
                {
                    var typeText = _source.GetText(type.Span);
                    Report(DiagnosticCodes.PayloadNeedsName, type.Span, [typeText, variant.Text], label: "no name",
                        help: $"name the payload: `.{variant.Text}({typeText} name)`");
                    field = new Name(new TextSpan(_prevEnd, 0), "<missing>");
                }
                else field = ExpectName("a payload field name");
            }
            fields.Add(new UnionField(SpanFrom(fieldStart), type, field));
            if (!Accept(TokenKind.Comma) || _pos == before) break;
        }
        Expect(TokenKind.CloseParen);
        return fields.ToArray();
    }

    /// <summary>After a case of an enum, union or union error: whether another follows, on its own line or after a <c>,</c> (kept in <paramref name="commas"/>).</summary>
    private bool NextCase(List<Token> commas)
    {
        if (Kind == TokenKind.Comma)
        {
            commas.Add(Advance());
            return true;
        }
        return Current.HasNewlineBefore;
    }

    /// <summary>BYN1053 at the first <c>,</c> between cases; the fix puts each case on its own line and drops the commas.</summary>
    private void ReportCaseCommas(string typeName, Token open, List<TextSpan> cases, List<Token> commas)
    {
        // A lone case with a trailing comma (`union error E { bad, }`) is still one case on one line.
        if (commas.Count == 0 || cases.Count < 2) return;
        Fix? fix = null;
        if (Kind == TokenKind.CloseBrace && cases.Count > 0)
        {
            var outer = IndentAt(open.Start);
            var sameLine = (int from, int to) => !_source.GetText(TextSpan.FromBounds(from, to)).Contains('\n');
            var caseIndent = sameLine(open.End, cases[0].Start) ? outer + "    " : IndentAt(cases[0].Start);
            var text = new StringBuilder();
            var lead = _source.GetText(TextSpan.FromBounds(open.End, cases[0].Start));
            text.Append(sameLine(open.End, cases[0].Start) ? KeptComment(lead) + "\n" + caseIndent : lead);
            for (var i = 0; i < cases.Count; i++)
            {
                text.Append(_source.GetText(cases[i]));
                var gapEnd = i + 1 < cases.Count ? cases[i + 1].Start : Current.Start;
                var gap = WithoutCommas(cases[i].End, gapEnd, commas);
                if (!gap.Contains('\n')) gap = KeptComment(gap) + "\n" + (i + 1 < cases.Count ? caseIndent : outer);
                text.Append(gap);
            }
            fix = new Fix(TextSpan.FromBounds(open.End, Current.Start), text.ToString());
        }
        Report(DiagnosticCodes.OneLineDeclarations, commas[0].Span, [typeName], label: "the first `,`",
            help: "write each case on its own line", fix: fix);
    }

    /// <summary>A <c>/* */</c> comment in a one-line gap between cases, kept at the end of its line; empty when there is none.</summary>
    private static string KeptComment(string gap) => gap.Trim(' ', '\t') is { Length: > 0 } comment ? " " + comment : "";

    /// <summary>The source from <paramref name="from"/> to <paramref name="to"/> without its <c>,</c> tokens and the spaces before them.</summary>
    private string WithoutCommas(int from, int to, List<Token> commas)
    {
        var bytes = _source.Bytes.Span;
        var text = new StringBuilder();
        var at = from;
        foreach (var comma in commas)
        {
            if (comma.Start < from || comma.Start >= to) continue;
            var cut = comma.Start;
            while (cut > at && bytes[cut - 1] is (byte)' ' or (byte)'\t') cut--;
            text.Append(_source.GetText(TextSpan.FromBounds(at, cut)));
            at = comma.End;
        }
        var rest = _source.GetText(TextSpan.FromBounds(at, to));
        // A comma ends its line: the spaces it left before a line break go too.
        if (at > from && rest.TrimStart(' ', '\t').StartsWith('\n')) rest = rest.TrimStart(' ', '\t');
        return text.Append(rest).ToString();
    }

    /// <summary><c>type Name = T</c>, or <c>type distinct Name = T</c>; <c>distinct type</c> is the input form (BYN1044).</summary>
    private TypeAliasDecl ParseTypeAlias(int start, Modifiers modifiers)
    {
        var first = Advance();
        var distinct = first.Kind == TokenKind.DistinctKeyword || Kind == TokenKind.DistinctKeyword;
        if (first.Kind == TokenKind.DistinctKeyword) ReportKeywordOrder(first, Advance(), "type distinct");
        else if (distinct) Advance();
        var name = ExpectName();
        Expect(TokenKind.Equals);
        var target = ParseRefinement(ParseType(fatArrow: true, lists: true));
        var requires = ParseRequiresLines();
        ExpectEnd();
        return new TypeAliasDecl(SpanFrom(start), modifiers, name, target) { Distinct = distinct, Requires = requires };
    }

    /// <summary><c>effect name = net, fs</c>.</summary>
    private EffectDecl ParseEffect(int start, Modifiers modifiers)
    {
        Advance();
        var name = ExpectName();
        Expect(TokenKind.Equals);
        var effects = new List<Name> { ExpectName("an effect name") };
        while (Accept(TokenKind.Comma)) effects.Add(ExpectName("an effect name"));
        ExpectEnd();
        return new EffectDecl(SpanFrom(start), modifiers, name, effects.ToArray());
    }

    private TestDecl ParseTest(int start)
    {
        Advance();
        var title = "";
        if (Kind == TokenKind.StringLiteral)
            title = LiteralDecoder.DecodeString(Text(Advance()));
        else
            Report(DiagnosticCodes.Expected, Current.Span, ["the test's name as a string literal", Describe(Current)]);
        var parameters = Kind == TokenKind.OpenParen ? ParseParams() : [];
        var body = ParseBlock();
        return new TestDecl(SpanFrom(start), title, parameters, body);
    }

    // ── Types ────────────────────────────────────────────────────────────────────────

    private static bool IsTypeNameToken(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.NeverKeyword;

    /// <summary>Whether <c>dyn</c> at <paramref name="index"/> starts a type: <c>dyn ref I</c> or <c>dyn I</c>.</summary>
    private bool IsDynAt(int index) =>
        KindAt(index) == TokenKind.Identifier && Text(_tokens[index]) == "dyn"
        && (KindAt(index + 1) == TokenKind.RefKeyword || IsTypeNameToken(KindAt(index + 1)));

    /// <summary>Speculatively scans a type starting at <paramref name="index"/>; returns the index after it, or -1.</summary>
    private int ScanType(int index) => ScanType(index, nested: false, out _);

    /// <summary>
    /// <see cref="ScanType(int)"/>; <paramref name="nested"/> (inside a type's parentheses or type arguments, or where
    /// <see cref="ParseType"/> takes BYN1055) scans <c>=&gt;</c> as a function type's arrow, and <paramref name="function"/>
    /// says whether the type scanned is a function type.
    /// </summary>
    private int ScanType(int index, bool nested, out bool function)
    {
        function = false;
        int i;
        if (KindAt(index) == TokenKind.OpenParen)
        {
            i = index + 1;
            var elements = 0;
            var lone = false;
            var names = true;
            if (KindAt(i) != TokenKind.CloseParen)
            {
                while (true)
                {
                    var elementStart = i;
                    i = ScanType(i, nested: true, out lone);
                    if (i < 0) return -1;
                    names &= i == elementStart + 1;
                    if (IsName(KindAt(i)))
                    {
                        i++;
                        names = false;
                    }
                    elements++;
                    if (KindAt(i) != TokenKind.Comma) break;
                    i++;
                }
            }
            if (KindAt(i) != TokenKind.CloseParen) return -1;
            i++;
            // `(a, b) => …` at a statement start is a lambda, not a tuple type.
            if (!nested && names && KindAt(i) == TokenKind.FatArrow) return -1;
            if (IsTypeArrow(i, nested)) return ScanFunctionType(i, nested, out function);
            if (elements < 2 && !(elements == 1 && lone)) return -1;
        }
        else
        {
            if (IsDynAt(index))
            {
                index++;
                if (KindAt(index) == TokenKind.RefKeyword && KindAt(++index) == TokenKind.ReadonlyKeyword) index++;
            }
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
            else if (KindAt(i) == TokenKind.Star && !_tokens[i].HasTriviaBefore)
            {
                // `u8* p` is a pointer type; `a*b` is a product.
                var j = i;
                while (KindAt(j) == TokenKind.Star && !_tokens[j].HasTriviaBefore) j++;
                if (j >= _count || !_tokens[j].HasTriviaBefore) return i;
                i = j;
            }
            else if (IsTypeArrow(i, nested))
            {
                return ScanFunctionType(i, nested, out function);
            }
            else
            {
                return i;
            }
        }
    }

    /// <summary>Whether a function type's arrow is at <paramref name="index"/>: <c>-&gt;</c>, or <c>=&gt;</c> when <paramref name="nested"/>.</summary>
    private bool IsTypeArrow(int index, bool nested) => KindAt(index) == TokenKind.Arrow || nested && !_exprTypeArgs && KindAt(index) == TokenKind.FatArrow;

    /// <summary>Scans <c>-&gt; R fails E uses e</c> starting at the arrow; returns the index after it, or -1.</summary>
    private int ScanFunctionType(int index, bool nested, out bool function)
    {
        function = true;
        var i = ScanType(index + 1, nested, out _);
        if (i > 0 && KindAt(i) == TokenKind.FailsKeyword) i = ScanClauseList(i + 1, names: false);
        if (i > 0 && KindAt(i) == TokenKind.UsesKeyword) i = ScanClauseList(i + 1, names: true);
        return i;
    }

    /// <summary>Scans the types after <c>fails</c> or the names after <c>uses</c>, bare or parenthesized; returns the index after them, or -1.</summary>
    private int ScanClauseList(int index, bool names)
    {
        var paren = KindAt(index) == TokenKind.OpenParen;
        var i = paren ? index + 1 : index;
        while (true)
        {
            i = names ? IsName(KindAt(i)) ? i + 1 : -1 : ScanType(i, nested: true, out _);
            if (i < 0 || KindAt(i) != TokenKind.Comma) break;
            i++;
        }
        if (!paren || i < 0) return i;
        return KindAt(i) == TokenKind.CloseParen ? i + 1 : -1;
    }

    /// <summary>Scans <c>&lt;T, U&gt;</c> starting at the <c>&lt;</c>; returns the index after <c>&gt;</c>, or -1.</summary>
    private int ScanTypeArgs(int index)
    {
        var i = index + 1;
        while (true)
        {
            i = ScanType(i, nested: true, out _);
            if (i < 0) return -1;
            if (KindAt(i) == TokenKind.Comma)
            {
                i++;
                continue;
            }
            return KindAt(i) == TokenKind.Greater ? i + 1 : -1;
        }
    }

    /// <summary><see cref="ScanTypeArgs"/> for type arguments after a name in an expression.</summary>
    private int ScanExprTypeArgs(int index)
    {
        var saved = _exprTypeArgs;
        _exprTypeArgs = true;
        try { return ScanTypeArgs(index); }
        finally { _exprTypeArgs = saved; }
    }

    /// <summary>C# type names written in a type position with the Bayan type each means.</summary>
    private static readonly Dictionary<string, string> CSharpTypeNames = new()
    {
        ["int"] = "i32", ["long"] = "i64", ["short"] = "i16", ["byte"] = "u8",
        ["uint"] = "u32", ["ulong"] = "u64", ["float"] = "f32", ["double"] = "f64",
    };

    /// <summary>
    /// A type. With <paramref name="fatArrow"/> (a parameter type, an alias target, a local's annotation, a type argument)
    /// <c>=&gt;</c> after a type is a function type's input form (BYN1055); elsewhere it is left to an expression body. With
    /// <paramref name="lists"/> (the same places but a type argument) a function type's <c>fails</c> and <c>uses</c> take
    /// comma lists; elsewhere one name or a parenthesized list (Q352).
    /// </summary>
    private TypeSyntax ParseType(bool fatArrow = false, bool lists = false)
    {
        var start = Current.Start;
        TypeSyntax type;
        if (Kind == TokenKind.Star) return ParsePrefixPointer();
        if (Kind == TokenKind.ReadonlyKeyword && ScanType(_pos + 1) is var end && end > 0 && KindAt(end - 1) == TokenKind.Star)
        {
            Advance();
            return ReadOnlyPointee(ParseType(), start);
        }
        if (Kind == TokenKind.OpenParen && ScanType(_pos, fatArrow, out _) > 0)
        {
            Advance();
            var elements = new List<TupleTypeElement>();
            if (Kind != TokenKind.CloseParen)
            {
                do
                {
                    var elementStart = Current.Start;
                    var elementType = ParseType(fatArrow: true, lists);
                    Name? name = IsName(Kind) ? NameOf(Advance()) : null;
                    elements.Add(new TupleTypeElement(SpanFrom(elementStart), elementType, name));
                } while (Accept(TokenKind.Comma));
            }
            Expect(TokenKind.CloseParen);
            if (Kind == TokenKind.Arrow || fatArrow && Kind == TokenKind.FatArrow) return ParseFunctionType(start, elements, fatArrow, lists);
            // `(Token -> bool) p`: the parentheses around a function type only group it.
            type = elements is [{ Name: null, Type: FunctionTypeSyntax lone }]
                ? lone with { Span = SpanFrom(start) }
                : new TupleType(SpanFrom(start), elements.ToArray());
        }
        else if (IsDynAt(_pos))
        {
            Advance();
            var isRef = Accept(TokenKind.RefKeyword);
            var isReadOnly = isRef && Accept(TokenKind.ReadonlyKeyword);
            var inner = ParseNamedType();
            type = new DynType(SpanFrom(start), isRef, isReadOnly, inner);
        }
        else if (IsTypeNameToken(Kind))
        {
            type = ParseNamedType();
        }
        else
        {
            return MissingType();
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
            else if (Kind == TokenKind.Star && !Current.HasTriviaBefore)
            {
                Advance();
                type = new PointerType(SpanFrom(start), type, false);
                if (Kind != TokenKind.Star) ReportPointerOutsideExtern(type);
            }
            else if (Kind == TokenKind.Arrow || fatArrow && Kind == TokenKind.FatArrow)
            {
                return ParseFunctionType(start, [new TupleTypeElement(type.Span, type, null)], fatArrow, lists);
            }
            else
            {
                return type;
            }
        }
    }

    /// <summary>
    /// <c>-&gt; R fails E uses e</c> after the parameter types of a function type, right-associative: <c>A -&gt; B -&gt; C</c>
    /// returns <c>B -&gt; C</c>. <c>=&gt;</c> for the arrow is BYN1055, fixed to <c>-&gt;</c>.
    /// </summary>
    private FunctionTypeSyntax ParseFunctionType(int start, List<TupleTypeElement> parameters, bool fatArrow, bool lists)
    {
        foreach (var p in parameters)
            if (p.Name is not null)
                Report(DiagnosticCodes.Expected, p.Name.Span, ["`,` or `)`", $"`{p.Name.Text}`"], label: "a parameter name",
                    help: "remove the name; a function type lists only its parameter types");
        var arrow = Advance();
        if (arrow.Kind == TokenKind.FatArrow)
            Report(DiagnosticCodes.FatArrowFunctionType, arrow.Span, label: "the input form", help: "write the function type with `->`",
                fix: new Fix(arrow.Span, "->"));
        var result = ParseType(fatArrow, lists);
        FailsClause? fails = null;
        UsesClause? uses = null;
        if (Kind == TokenKind.FailsKeyword)
        {
            var failsStart = Advance().Start;
            fails = new FailsClause(SpanFrom(failsStart), ParseClauseList(lists, () => ParseType()));
        }
        if (Kind == TokenKind.UsesKeyword)
        {
            var usesStart = Advance().Start;
            uses = new UsesClause(SpanFrom(usesStart), ParseClauseList(lists, () => ExpectName("an effect name")));
        }
        return new FunctionTypeSyntax(SpanFrom(start), parameters.Select(p => p.Type).ToArray(), result, fails, uses);
    }

    /// <summary>
    /// The error types after a function type's <c>fails</c> or the effects after its <c>uses</c>: a comma list with
    /// <paramref name="lists"/>, else one, or a parenthesized list (Q352).
    /// </summary>
    private T[] ParseClauseList<T>(bool lists, Func<T> parseOne)
    {
        var paren = Kind == TokenKind.OpenParen;
        if (paren) Advance();
        var items = new List<T> { parseOne() };
        while ((lists || paren) && Accept(TokenKind.Comma)) items.Add(parseOne());
        if (paren) Expect(TokenKind.CloseParen);
        return items.ToArray();
    }

    /// <summary><c>readonly T*</c>: the innermost pointer of <paramref name="type"/> gets a pointee that cannot be written.</summary>
    private static TypeSyntax ReadOnlyPointee(TypeSyntax type, int start)
    {
        if (type is not PointerType pointer) return type;
        var span = TextSpan.FromBounds(start, pointer.Span.End);
        return pointer.Pointee is PointerType
            ? pointer with { Span = span, Pointee = ReadOnlyPointee(pointer.Pointee, start) }
            : pointer with { Span = span, ReadOnly = true };
    }

    /// <summary><c>*T</c>, <c>*mut T</c> and <c>*const T</c>, the BYN1066 input forms of <c>T*</c> and <c>readonly T*</c>.</summary>
    private TypeSyntax ParsePrefixPointer()
    {
        var start = Current.Start;
        var star = Advance();
        var readOnly = Accept(TokenKind.ConstKeyword);
        if (!readOnly && AtContextual("mut") && IsTypeNameToken(Peek(1))) Advance();
        var pointee = ParseType();
        var span = SpanFrom(start);
        var canonical = (readOnly ? "readonly " : "") + PointeeText(pointee) + "*";
        // `**T` nests input forms; the inner one gets the fix first, and has already raised BYN1067.
        var nested = pointee is PointerType;
        Report(DiagnosticCodes.PrefixPointerInput, star.Span, [_source.GetText(span), canonical], label: "the input form",
            help: $"write the pointer after the type: `{canonical}`", fix: nested ? null : new Fix(span, canonical));
        var type = new PointerType(span, pointee, readOnly);
        if (!nested) ReportPointerOutsideExtern(type);
        return type;
    }

    /// <summary>The canonical text of a pointee: its source, or for a nested pointer the postfix spelling.</summary>
    private string PointeeText(TypeSyntax pointee) => pointee is PointerType p
        ? (p.ReadOnly ? "readonly " : "") + PointeeText(p.Pointee) + "*"
        : _source.GetText(pointee.Span);

    private void ReportPointerOutsideExtern(TypeSyntax type)
    {
        if (_inExtern) return;
        // Not through Report: the type parsed in full, so no recovery is needed, and BYN1066 for a prefix pointer
        // starts at the same `*` and would make Report's same-start check drop this one.
        _diagnostics.Report(DiagnosticCodes.PointerOutsideExtern, _source, type.Span, label: "a pointer type",
            help: "wrap the C call in an `extern` declaration");
    }

    /// <summary><c>Name</c>, <c>m.Name</c> or <c>Name&lt;A&gt;</c> without suffixes: a type, a base, a <c>where</c> bound or after <c>dyn</c>.</summary>
    private NamedType ParseNamedType()
    {
        if (!IsTypeNameToken(Kind)) return MissingType();
        var start = Current.Start;
        var path = new List<Name> { NameOf(Advance()) };
        while (Kind == TokenKind.Dot && Peek(1) == TokenKind.Identifier)
        {
            Advance();
            path.Add(NameOf(Advance()));
        }
        var args = Array.Empty<TypeSyntax>();
        if (Kind == TokenKind.Less && ScanTypeArgs(_pos) > 0) args = ParseTypeArgs();
        if (path.Count == 1 && args.Length == 0 && CSharpTypeNames.TryGetValue(path[0].Text, out var bayan))
            // Only types reach here, so `int` as a value or variable name is untouched.
            path[0] = path[0] with { Text = bayan };
        else if (path.Count == 1 && args.Length == 0 && path[0].Text == "char")
        {
            Report(DiagnosticCodes.CharInput, path[0].Span, label: "the input form", help: "rename the type to `rune`",
                fix: new Fix(path[0].Span, "rune"));
            path[0] = path[0] with { Text = "rune" };
        }
        return new NamedType(SpanFrom(start), path.ToArray(), args);
    }

    private NamedType MissingType()
    {
        var start = Current.Start;
        Report(DiagnosticCodes.ExpectedType, Current.Span, [Describe(Current)]);
        return new NamedType(new TextSpan(start, 0), [new Name(new TextSpan(start, 0), "<missing>")], []);
    }

    private TypeSyntax[] ParseTypeArgs()
    {
        Advance();
        var args = new List<TypeSyntax> { ParseType(fatArrow: true) };
        while (Accept(TokenKind.Comma)) args.Add(ParseType(fatArrow: true));
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
            var errors = _syntaxErrors;
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
        if (Kind == TokenKind.At && IsName(Peek(1)))
        {
            var attributes = ParseStatementAttributes();
            if (AtEnd || Kind == TokenKind.CloseBrace)
            {
                Report(DiagnosticCodes.Expected, Current.Span, ["a statement after the attribute", Describe(Current)]);
                return new ErrorStmt(SpanFrom(start)) { Attributes = attributes };
            }
            return ParseStatement() with { Span = SpanFrom(start), Attributes = attributes };
        }
        switch (Kind)
        {
            case TokenKind.OpenBrace:
                return RejectPostfixAfterCompound(ParseBlock());
            case TokenKind.Semicolon:
                ReportSemicolon(Advance());
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
                var whileKeyword = Advance();
                if (Accept(TokenKind.LetKeyword))
                {
                    var variable = ExpectName("a variable name");
                    Expect(TokenKind.Equals);
                    var value = ParseBareCondition();
                    return new WhileLetStmt(SpanFrom(start), variable, value, ParseBlock());
                }
                {
                    var condition = ParseCondition(out var parenthesized, out var parens);
                    var body = parenthesized ? ParseBody(whileKeyword, ref parens) : ParseBlock();
                    return new WhileStmt(SpanFrom(start), condition, body);
                }
            case TokenKind.DoKeyword:
                {
                    var body = ParseBody(Advance());
                    Expect(TokenKind.WhileKeyword);
                    var condition = Kind == TokenKind.OpenParen && EndsBefore(MatchingClose(_pos) + 1)
                        ? ParseParenCondition(out _)
                        : ParseBareCondition();
                    ExpectEnd();
                    return new DoWhileStmt(SpanFrom(start), body, condition);
                }
            case TokenKind.ForKeyword:
            case TokenKind.ForeachKeyword:
                return ParseFor(start);
            case TokenKind.SwitchKeyword:
                return ParseSwitchStatement(start);
            case TokenKind.DeferKeyword:
            case TokenKind.ErrdeferKeyword:
                return ParseDefer(start);
            case TokenKind.ContextKeyword:
            case TokenKind.ProvideKeyword:
                return ParseContext(start);
            case TokenKind.BecomeKeyword:
                {
                    Advance();
                    Expect(TokenKind.Dot);
                    var state = ExpectName("a state name");
                    var args = Kind == TokenKind.OpenParen && !Current.HasNewlineBefore ? ParseArguments() : [];
                    ExpectEnd();
                    return new BecomeStmt(SpanFrom(start), state, args);
                }
            case TokenKind.GotoKeyword:
                return ParseGoto(start);
            case TokenKind.AssertKeyword:
                Advance();
                {
                    // `assert(x)` is `assert (x)`: the parentheses are part of the expression.
                    var condition = ParseExpression();
                    // `assert f() fails E.x`: the call must fail with that error.
                    Expr? fails = null;
                    if (Kind == TokenKind.FailsKeyword && !BreaksLine(_pos))
                    {
                        Advance();
                        fails = ParseExpression();
                    }
                    ExpectEnd();
                    return new AssertStmt(SpanFrom(start), condition) { Fails = fails };
                }
            case TokenKind.Identifier when Peek(1) == TokenKind.Colon:
                {
                    var label = NameOf(Advance());
                    Advance();
                    return new LabeledStmt(SpanFrom(start), label, ParseStatement());
                }
            case TokenKind.ElseKeyword:
                Report(DiagnosticCodes.Expected, Current.Span, ["a statement", Describe(Current)], help: "remove this `else`; it has no matching `if`");
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

    /// <summary>C#-style <c>T name = …</c> or <c>T name</c> at the end of its line.</summary>
    private bool IsLocalDeclarationStart()
    {
        if (!IsTypeNameToken(Kind) && Kind != TokenKind.OpenParen) return false;
        var after = ScanType(_pos);
        return after > 0 && IsName(KindAt(after)) && !_tokens[after].HasNewlineBefore
            && (KindAt(after + 1) == TokenKind.Equals || EndsBefore(after + 1));
    }

    private Stmt ParseLocal(int start)
    {
        var isUsing = Accept(TokenKind.UsingKeyword);
        var kind = BindingKind.Var;
        var keyword = Kind is TokenKind.LetKeyword or TokenKind.VarKeyword or TokenKind.ConstKeyword;
        if (Accept(TokenKind.LetKeyword)) kind = BindingKind.Let;
        else if (Accept(TokenKind.VarKeyword)) kind = BindingKind.Var;
        else if (Accept(TokenKind.ConstKeyword)) kind = BindingKind.Const;
        else if (isUsing) kind = BindingKind.Let;

        TypeSyntax? type = null;
        Name name;
        if (IsNameThenType(_pos))
        {
            name = ExpectName("a variable name");
            Advance();
            type = ParseRefinement(ParseType(fatArrow: true, lists: true));
        }
        else if (IsTypeThenName(_pos))
        {
            type = ParseType(lists: true);
            name = ExpectName("a variable name");
            // A `let` needs a value, so a local declared without one is a `var`.
            if (!keyword && !isUsing) kind = Kind != TokenKind.Equals || IsWrittenLater(name.Text) ? BindingKind.Var : BindingKind.Let;
            ReportTypeFirstLocal(kind, keyword, type, name);
        }
        else name = ExpectName("a variable name");
        Expr? init = null;
        if (Accept(TokenKind.Equals)) init = kind == BindingKind.Const && IsPatternExprStart() ? ParsePatternExpr() : ParseExpression();
        if (Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword && !Current.HasNewlineBefore)
        {
            Report(DiagnosticCodes.PostfixNotSimple, Current.Span, [Text(Current)],
                help: "declare the variable, then assign it conditionally; a declaration cannot be conditional");
            Advance();
            ParseExpression();
        }
        ExpectEnd();
        return new LocalDecl(SpanFrom(start), kind, isUsing, type, name, init);
    }

    /// <summary>The input form <c>T x</c> of a local binding (BYN1043); the fix writes <c>let x: T</c>, keeping a <c>let</c>, <c>var</c> or <c>const</c> already written.</summary>
    private void ReportTypeFirstLocal(BindingKind kind, bool keyword, TypeSyntax type, Name name)
    {
        var span = TextSpan.FromBounds(type.Span.Start, name.Span.End);
        var typeText = _source.GetText(type.Span);
        var word = kind switch { BindingKind.Let => "let", BindingKind.Const => "const", _ => "var" };
        Report(DiagnosticCodes.TypeNameOrder, span, [$"{typeText} {name.Text}", $"{word} {name.Text}: {typeText}"], label: "type before name",
            help: $"write `{word} {name.Text}: {typeText}`; local bindings put the name first",
            fix: new Fix(span, (keyword ? "" : word + " ") + $"{name.Text}: {typeText}"));
    }

    /// <summary>
    /// Whether the rest of the enclosing block assigns <paramref name="name"/>, increments it or passes it as <c>ref</c> or
    /// <c>out</c>, so the BYN1043 fix declares it <c>var</c> (J55). A write through a mutating method is not seen here; the
    /// binder reports it (BYN2104) with a fix to <c>var</c>.
    /// </summary>
    private bool IsWrittenLater(string name)
    {
        var depth = 0;
        for (var i = _pos; i < _count; i++)
        {
            var kind = KindAt(i);
            if (kind is TokenKind.OpenBrace) depth++;
            else if (kind is TokenKind.CloseBrace && --depth < 0) return false;
            else if (kind is TokenKind.EndOfFile) return false;
            if (kind != TokenKind.Identifier || Text(_tokens[i]) != name || i > 0 && KindAt(i - 1) is TokenKind.Dot or TokenKind.QuestionDot) continue;
            if (i > 0 && KindAt(i - 1) is TokenKind.RefKeyword or TokenKind.OutKeyword or TokenKind.PlusPlus or TokenKind.MinusMinus) return true;
            var j = i + 1;
            while (true)
            {
                if (KindAt(j) == TokenKind.Dot && IsName(KindAt(j + 1))) j += 2;
                else if (KindAt(j) == TokenKind.OpenBracket && MatchingClose(j) is var close and > 0) j = close + 1;
                else break;
            }
            if (KindAt(j) is TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals or TokenKind.StarEquals or
                TokenKind.SlashEquals or TokenKind.PercentEquals or TokenKind.AmpEquals or TokenKind.BarEquals or
                TokenKind.CaretEquals or TokenKind.LessLessEquals or TokenKind.QuestionQuestionEquals or
                TokenKind.PlusPlus or TokenKind.MinusMinus || KindAt(j) == TokenKind.Greater && KindAt(j + 1) == TokenKind.GreaterEquals)
                return true;
        }
        return false;
    }

    /// <summary>The input form <c>(c)</c>; the parentheses are reported (BYN1017) unless a top-level <c>{</c> needs them.</summary>
    private Expr ParseParenCondition(out Diagnostic? parens)
    {
        parens = null;
        var open = Current;
        var keep = InGroup(_pos, TokenKind.OpenBrace);
        var errors = ErrorCount;
        Expect(TokenKind.OpenParen);
        var condition = ParseExpression();
        var close = Expect(TokenKind.CloseParen);
        if (!keep && close.Length > 0 && ErrorCount == errors)
        {
            var span = TextSpan.FromBounds(open.Start, close.End);
            var inner = _source.GetText(TextSpan.FromBounds(open.End, close.Start)).Trim();
            parens = Report(DiagnosticCodes.ParenthesizedCondition, span, label: "not needed", help: "remove the parentheses",
                fix: new Fix(span, (open.HasTriviaBefore ? "" : " ") + inner));
        }
        return condition;
    }

    /// <summary>Whether the group opened at <paramref name="open"/> has a <paramref name="kind"/> token at its top level.</summary>
    private bool InGroup(int open, TokenKind kind)
    {
        var depth = 0;
        for (var i = open; i < _count; i++)
        {
            var k = KindAt(i);
            if (depth == 1 && k == kind) return true;
            switch (k)
            {
                case TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace:
                    depth++;
                    break;
                case TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace:
                    if (--depth == 0) return false;
                    break;
                case TokenKind.EndOfFile:
                    return false;
            }
        }
        return false;
    }

    /// <summary>
    /// The body of <paramref name="keyword"/> after a parenthesized condition: a block, or the input form of a single
    /// statement, reported (BYN1018) with a fix that wraps it in <c>{ }</c>.
    /// </summary>
    private Stmt ParseBody(Token keyword)
    {
        Diagnostic? header = null;
        return ParseBody(keyword, ref header);
    }

    /// <param name="header">The report on the header (BYN1017, BYN1007, BYN1019, …), whose fix also needs the braces to parse;
    /// updated to the report that replaces it.</param>
    private Stmt ParseBody(Token keyword, ref Diagnostic? header)
    {
        if (Kind == TokenKind.OpenBrace) return ParseBlock();
        var gapStart = _prevEnd;
        var errors = ErrorCount;
        var seen = _diagnostics.Items.Count;
        var bodyPos = _pos;
        var body = ParseStatement();
        if (ErrorCount != errors && body is ErrorStmt) return body;
        // At the end of the file the statement takes no token: there is no text to wrap in braces.
        if (_pos == bodyPos && Kind == TokenKind.EndOfFile) return body;
        // Only an inner fix overlaps the braces fix; a diagnostic without one (e.g. BYN1006) leaves it applicable.
        header = ReportBraceless(keyword, gapStart, body, _diagnostics.Items.Skip(seen).All(d => d.Fix is null), header);
        return body;
    }

    // The last BYN1018 report and its own braces fix, so ParseUnless can hand the fix back when the header fix is dropped.
    private Diagnostic? _bracesReport;
    private Fix? _bracesFix;

    /// <summary>
    /// BYN1018 on <paramref name="body"/>, a single statement after <paramref name="keyword"/>. A <paramref name="header"/> fix
    /// takes the braces too, so that fix parses applied alone and BYN1018 has none of its own. Returns the header's report.
    /// </summary>
    private Diagnostic? ReportBraceless(Token keyword, int gapStart, Stmt body, bool clean, Diagnostic? header)
    {
        // A body that took no token (an `ErrorExpr` before `}`) starts past `_prevEnd`: the braces then wrap nothing.
        var start = Math.Min(body.Span.Start, _prevEnd);
        var text = _source.GetText(TextSpan.FromBounds(start, _prevEnd));
        var gap = _source.GetText(TextSpan.FromBounds(gapStart, start));
        string replacement;
        if (gap.Contains('\n'))
        {
            var line = _source.GetText(_source.GetLineSpan(_source.GetLineIndex(keyword.Start)));
            var indent = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
            replacement = " {" + gap + text + "\n" + indent + "}";
        }
        else
        {
            replacement = " { " + text + " }";
        }
        // A fix inside the body (e.g. a nested `if (x) s;`) would overlap this one, which copies the body's text: only the inner one
        // gets a fix, and the header's fix is dropped because without the braces it does not parse.
        var fix = clean ? new Fix(TextSpan.FromBounds(gapStart, _prevEnd), replacement) : null;
        var own = fix;
        if (header?.Fix is { } h)
        {
            var merged = fix is null ? null : new Fix(TextSpan.FromBounds(h.Span.Start, fix.Span.End),
                h.Replacement + _source.GetText(TextSpan.FromBounds(h.Span.End, fix.Span.Start)) + fix.Replacement);
            // The merged fix copies the source up to this body (an `if`'s `then` block before a braceless `else`); a fix in
            // there would overlap it, so the header loses its fix and BYN1018 keeps its own.
            var overlaps = merged is not null && _diagnostics.Items.Any(d => !ReferenceEquals(d, header) && d.Fix is { } f
                && f.Span.Start < merged.Span.End && f.Span.End > merged.Span.Start);
            var replaced = header with { Fix = overlaps ? null : merged };
            _diagnostics.Replace(header, replaced);
            header = replaced;
            if (!overlaps) fix = null;
        }
        _bracesReport = Report(DiagnosticCodes.BodyNeedsBraces, body.Span, [Text(keyword)], label: "a single statement",
            help: "wrap the statement in `{ }`", fix: fix);
        _bracesFix = own;
        return header;
    }

    /// <summary>
    /// The condition of <c>if</c>, <c>unless</c>, <c>while</c> or <c>switch</c>. <c>(c)</c> followed by a statement is the
    /// parenthesized input form (<paramref name="parenthesized"/>); otherwise the condition is bare and its body a block.
    /// </summary>
    /// <param name="parens">The BYN1017 report on the parentheses, if any, for a caller whose own fix removes them.</param>
    private Expr ParseCondition(out bool parenthesized, out Diagnostic? parens)
    {
        parens = null;
        parenthesized = IsParenthesizedCondition();
        return parenthesized ? ParseParenCondition(out parens) : ParseBareCondition();
    }

    /// <summary>
    /// At <c>(</c>: whether the group is the whole condition, i.e. a statement or <c>{</c> follows its <c>)</c>,
    /// rather than the start of a longer bare condition such as <c>(a + b) * c &gt; d {</c>.
    /// </summary>
    private bool IsParenthesizedCondition()
    {
        // `switch (a, b) {` is a bare tuple condition.
        if (Kind != TokenKind.OpenParen || InGroup(_pos, TokenKind.Comma)) return false;
        var next = MatchingClose(_pos) + 1;
        var kind = KindAt(next);
        if (kind is TokenKind.OpenBrace or TokenKind.Semicolon || _tokens[Math.Min(next, _count - 1)].HasNewlineBefore) return true;
        if (kind is TokenKind.Identifier) return true;
        if (kind.IsKeyword())
            return kind is not (TokenKind.IsKeyword or TokenKind.AsKeyword or TokenKind.CatchKeyword or TokenKind.WithKeyword or TokenKind.InKeyword);
        // An operator: a bare condition reaches its `{` body before the statement ends; a statement such as `-x` does not.
        return !BodyBraceFirst(next);
    }

    /// <summary>
    /// Whether, from <paramref name="from"/>, a top-level <c>{</c> comes before any <c>;</c>, <c>}</c>, assignment, line
    /// break that ends the statement, or the end. A condition has no assignment, so <c>(ok) p = P { x: 1 }</c> is a statement.
    /// </summary>
    private bool BodyBraceFirst(int from)
    {
        var depth = 0;
        for (var i = from; i < _count; i++)
        {
            if (depth == 0 && i > from && BreaksLine(i) && !LineContinues(i)) return false;
            switch (KindAt(i))
            {
                case TokenKind.OpenParen or TokenKind.OpenBracket:
                    depth++;
                    break;
                case TokenKind.CloseParen or TokenKind.CloseBracket:
                    depth--;
                    break;
                case TokenKind.OpenBrace when depth == 0:
                    return true;
                case TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile:
                case TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals or TokenKind.StarEquals or TokenKind.SlashEquals
                    or TokenKind.PercentEquals or TokenKind.AmpEquals or TokenKind.BarEquals or TokenKind.CaretEquals
                    or TokenKind.LessLessEquals or TokenKind.QuestionQuestionEquals when depth == 0:
                    return false;
            }
        }
        return false;
    }

    /// <summary>The index of the <c>)</c> closing the <c>(</c> at <paramref name="open"/>, or the end of file.</summary>
    private int MatchingClose(int open)
    {
        var depth = 0;
        for (var i = open; i < _count; i++)
        {
            switch (KindAt(i))
            {
                case TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace:
                    depth++;
                    break;
                case TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace:
                    if (--depth == 0) return i;
                    break;
            }
        }
        return _count - 1;
    }

    /// <summary>A condition without parentheses: a <c>{</c> at its top level opens the body, not a struct literal (spec §7.2).</summary>
    private Expr ParseBareCondition()
    {
        var saved = _conditionStart;
        _conditionStart = _pos;
        try
        {
            return ParseExpression();
        }
        finally
        {
            _conditionStart = saved;
        }
    }

    /// <summary>Whether a <c>{</c> here may open a struct literal or property pattern: not at the top level of a bare condition.</summary>
    private bool BraceLiteralAllowed()
    {
        if (_conditionStart < 0) return true;
        var depth = 0;
        for (var i = _conditionStart; i < _pos; i++)
        {
            switch (KindAt(i))
            {
                case TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace:
                    depth++;
                    break;
                case TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace:
                    depth--;
                    break;
            }
        }
        return depth > 0;
    }

    /// <summary><c>unlikely</c> after <c>if</c>, as a word rather than a variable: something follows it after a space.</summary>
    private bool AtUnlikely() =>
        AtContextual("unlikely") && PeekToken(1).HasTriviaBefore && Peek(1) != TokenKind.OpenBrace && CanStartExpression(Peek(1));

    private Stmt ParseIf(int start, bool elseIf = false)
    {
        var keyword = Advance();
        var likelihood = Likelihood.None;
        if (Kind == TokenKind.NeverKeyword && Peek(1) != TokenKind.OpenBrace && CanStartExpression(Peek(1)))
        {
            Advance();
            likelihood = Likelihood.Never;
        }
        else if (AtUnlikely())
        {
            Advance();
            likelihood = Likelihood.Unlikely;
        }
        var condition = ParseCondition(out var parenthesized, out var parens);
        // The BYN1017 fix leaves a bare condition, after which `then` and `else` must both be blocks: it braces both.
        var then = parenthesized ? ParseBody(keyword, ref parens) : ParseBlock();
        Stmt? otherwise = null;
        if (Kind == TokenKind.ElseKeyword)
        {
            if (Current.HasNewlineBefore && then is BlockStmt && KindAt(_pos - 1) == TokenKind.CloseBrace)
                Report(DiagnosticCodes.ElseOnNewLine, Current.Span, label: "on a new line", help: "move `else` up to the line of `}`",
                    fix: new Fix(TextSpan.FromBounds(_prevEnd, Current.Start), " "));
            var elseKeyword = Advance();
            otherwise = Kind == TokenKind.IfKeyword ? RejectPostfixAfterCompound(ParseIf(Current.Start, elseIf: true))
                : parenthesized ? ParseBody(elseKeyword, ref parens) : ParseBlock();
        }
        return new IfStmt(SpanFrom(start), condition, then, otherwise) { Likelihood = likelihood };
    }

    private static Expr Unwrap(Expr e) => e is ParenExpr p ? Unwrap(p.Inner) : e;

    /// <summary><c>x is null</c>.</summary>
    private static bool IsNullTest(Expr e) =>
        e is IsExpr { Pattern: ConstantPattern { Value: LiteralExpr { Kind: LiteralKind.Null } } };

    /// <summary>
    /// After <c>unless</c> (statement or postfix): the input forms <c>unless !x</c> (BYN1007), <c>unless a &amp;&amp; b</c>
    /// (BYN1022) and <c>unless x is null</c> (BYN1023), each fixed to an <c>if</c> over <paramref name="fixSpan"/>.
    /// Returns the report, if any.
    /// </summary>
    private Diagnostic? CheckUnlessCondition(Expr condition, TextSpan fixSpan)
    {
        condition = Unwrap(condition);
        if (condition is UnaryExpr { Op: UnaryOp.Not } negated)
        {
            return Report(DiagnosticCodes.UnlessNegated, fixSpan, label: "`unless` already negates",
                help: "use `if` with the condition as written", fix: new Fix(fixSpan, "if " + _source.GetText(negated.Operand.Span)));
        }
        if (condition is BinaryExpr { Op: BinaryOp.LogicalAnd or BinaryOp.LogicalOr })
        {
            return Report(DiagnosticCodes.UnlessCompound, fixSpan, label: "a compound condition",
                help: "use `if` with the negated condition", fix: new Fix(fixSpan, "if !(" + _source.GetText(condition.Span) + ")"));
        }
        if (IsNullTest(condition))
        {
            return Report(DiagnosticCodes.UnlessNull, fixSpan, label: "a negated null test",
                help: "use `if` with `is not null`", fix: new Fix(fixSpan, "if " + _source.GetText(((IsExpr)condition).Value.Span) + " is not null"));
        }
        return null;
    }

    /// <summary>Withdraws <paramref name="d"/> when a later fix over the same span replaces it.</summary>
    private void Unreport(Diagnostic? d)
    {
        if (d is not null) _diagnostics.Remove(d);
    }

    /// <summary>A likelihood word after <c>unless</c> (BYN1033), removed by the fix.</summary>
    private void SkipUnlessLikelihood()
    {
        if (!(Kind == TokenKind.NeverKeyword && Peek(1) != TokenKind.OpenBrace && CanStartExpression(Peek(1))) && !AtUnlikely()) return;
        var word = Advance();
        Report(DiagnosticCodes.UnlessLikelihood, word.Span, [Text(word)], label: "a likelihood word",
            help: "remove the word, or write `if unlikely` with the negated condition",
            fix: new Fix(TextSpan.FromBounds(word.Start, Current.Start), ""));
    }

    private Stmt ParseUnless(int start)
    {
        var keyword = Advance();
        SkipUnlessLikelihood();
        var condition = ParseCondition(out var parenthesized, out var parens);
        // The `if` fix spans the parentheses and drops them, so it replaces the BYN1017 report.
        var check = CheckUnlessCondition(condition, TextSpan.FromBounds(keyword.Start, _prevEnd));
        if (check is not null) Unreport(parens);
        var header = check ?? parens;
        var body = parenthesized ? ParseBody(keyword, ref header) : ParseBlock();
        var (bracesReport, bracesFix) = parenthesized && _bracesReport?.Span == body.Span ? (_bracesReport, _bracesFix) : (null, null);
        if (Kind == TokenKind.ElseKeyword)
        {
            Report(DiagnosticCodes.UnlessWithElse, Current.Span, label: "`else` after `unless`",
                help: "use `if` with the opposite condition, so both branches read positively");
            Advance();
            // The `if` fix of BYN1007/1022/1023 leaves a bare condition, which needs a block after `else`.
            if (ParseEmbedded() is not BlockStmt && check is not null && header?.Fix is not null)
            {
                _diagnostics.Replace(header, header with { Fix = null });
                // The braces the header fix carried go back to BYN1018, so one fix still applies alone.
                if (bracesReport is { Fix: null } && bracesFix is not null) _diagnostics.Replace(bracesReport, bracesReport with { Fix = bracesFix });
            }
        }
        return new UnlessStmt(SpanFrom(start), condition, body);
    }

    private Stmt ParseFor(int start)
    {
        var keyword = Advance();
        var isTry = Accept(TokenKind.TryKeyword);
        if (keyword.Kind == TokenKind.ForeachKeyword || Kind == TokenKind.OpenParen && !IsTupleLoopVariables())
            return ParseParenFor(start, keyword) with { Try = isTry };

        TypeSyntax? type = null;
        Name variable;
        var names = new List<Name>();
        if (Kind == TokenKind.OpenParen)
        {
            var groupStart = Current.Start;
            Advance();
            do names.Add(ExpectName("a loop variable"));
            while (Accept(TokenKind.Comma));
            Expect(TokenKind.CloseParen);
            variable = new Name(SpanFrom(groupStart), "_");
            // `for (x) in xs`: parentheses around one loop variable, accepted like the other input forms (§15).
            if (names.Count == 1)
            {
                variable = names[0];
                names.Clear();
            }
        }
        else if (IsNameThenType(_pos))
        {
            variable = ExpectName("a loop variable");
            Advance();
            type = ParseType();
        }
        else if (IsTypeThenName(_pos))
        {
            // `for T x in e` follows the local rule, not the declaration rule (§25): BYN1043 with a fix to `for x: T in e`.
            type = ParseType();
            variable = ExpectName("a loop variable");
            var span = TextSpan.FromBounds(type.Span.Start, variable.Span.End);
            var typeText = _source.GetText(type.Span);
            Report(DiagnosticCodes.TypeNameOrder, span, [$"for {typeText} {variable.Text}", $"for {variable.Text}: {typeText}"],
                label: "type before name", help: $"write `for {variable.Text}: {typeText}`; loop variables put the name first",
                fix: new Fix(span, $"{variable.Text}: {typeText}"));
        }
        else
        {
            variable = ExpectName("a loop variable");
        }
        Expect(TokenKind.InKeyword);
        var iterable = ParseBareCondition();
        var body = ParseBlock();
        return new ForStmt(SpanFrom(start), BindingKind.Let, type, variable, iterable, body) { Try = isTry, Names = names.ToArray() };
    }

    /// <summary>
    /// At <c>(</c> after <c>for</c>: the tuple <c>(a, b) in</c>, as opposed to the input form <c>(let x in xs)</c>, which has
    /// <c>let</c>, <c>var</c>, <c>in</c> or <c>;</c> at the group's top level.
    /// </summary>
    private bool IsTupleLoopVariables() =>
        !InGroup(_pos, TokenKind.LetKeyword) && !InGroup(_pos, TokenKind.VarKeyword)
        && !InGroup(_pos, TokenKind.InKeyword) && !InGroup(_pos, TokenKind.Semicolon);

    /// <summary>The input form <c>for (let x in xs)</c>, <c>foreach (var x in xs)</c>, reported (BYN1019) with a fix to <c>for x in xs</c>.</summary>
    private ForStmt ParseParenFor(int start, Token keyword)
    {
        var open = Expect(TokenKind.OpenParen);
        var kind = BindingKind.Let;
        TypeSyntax? type = null;
        if (Accept(TokenKind.LetKeyword)) kind = BindingKind.Let;
        else if (Accept(TokenKind.VarKeyword)) kind = BindingKind.Var;
        if (IsTypeThenName(_pos)) type = ParseType();
        var name = ExpectName("a loop variable");
        if (Kind != TokenKind.InKeyword)
        {
            Report(DiagnosticCodes.CStyleFor, Current.Span, label: "expected `in`",
                help: "loop over a range instead: `for i in 0..n { … }`");
            SkipPastCloseParen();
            var body0 = ParseEmbedded();
            return new ForStmt(SpanFrom(start), kind, type, name, new ErrorExpr(name.Span), body0);
        }
        Advance();
        var errors = ErrorCount;
        var iterable = ParseExpression();
        var close = Expect(TokenKind.CloseParen);
        Diagnostic? input = null;
        if (close.Length > 0 && ErrorCount == errors)
        {
            var span = TextSpan.FromBounds(keyword.Kind == TokenKind.ForeachKeyword ? keyword.Start : open.Start, close.End);
            var header = (type is null ? "" : _source.GetText(type.Span) + " ") + name.Text + " in " + _source.GetText(iterable.Span);
            input = Report(DiagnosticCodes.ForInputForm, span, label: "the input form",
                help: "write the loop variable and iterable without parentheses",
                fix: new Fix(span, keyword.Kind == TokenKind.ForeachKeyword ? "for " + header : open.HasTriviaBefore ? header : " " + header));
        }
        var body = ParseBody(keyword, ref input);
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
        var value = ParseCondition(out _, out _);
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
                var errors = _syntaxErrors;
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
        var pattern = ParseArmPattern("case ");
        Expr? guard = null;
        if (AtContextual("when"))
        {
            Advance();
            guard = ParseExpression();
        }
        Expect(TokenKind.Colon);
        return new SwitchLabel(SpanFrom(start), pattern, guard);
    }

    /// <summary>
    /// The pattern of a <c>case</c> or a switch arm. <c>except</c> is not allowed there (BYN1051); <c>_ except p</c> is
    /// fixed to <c>not p</c>, and any other left side parses as <c>left and not p</c>.
    /// </summary>
    private Pattern ParseArmPattern(string head)
    {
        var start = Current.Start;
        var pattern = ParsePattern();
        if (Kind != TokenKind.ExceptKeyword) return pattern;
        var keyword = Advance();
        var excluded = ParsePattern();
        var excludedText = _source.GetText(excluded.Span);
        var not = new NotPattern(excluded.Span, excluded);
        var help = $"write the arm as `{head}not {excludedText}`";
        if (pattern is DiscardPattern)
        {
            Report(DiagnosticCodes.ExceptInCase, keyword.Span, label: "not allowed here", help: help,
                fix: new Fix(SpanFrom(start), $"not {excludedText}"));
            return not;
        }
        Report(DiagnosticCodes.ExceptInCase, keyword.Span, label: "not allowed here", help: help);
        return new BinaryPattern(SpanFrom(start), true, pattern, not);
    }

    /// <summary><c>defer</c>, <c>defer failing</c>, <c>defer panicking</c> (contextual words right after <c>defer</c>) or <c>errdefer</c>, each kind but the first with an optional <c>(name)</c>.</summary>
    private DeferStmt ParseDefer(int start)
    {
        var keyword = Advance();
        var kind = keyword.Kind == TokenKind.ErrdeferKeyword ? DeferKind.Failing : DeferKind.Always;
        if (kind == DeferKind.Failing)
            Report(DiagnosticCodes.ErrdeferInput, keyword.Span, label: "the input form", help: "write `defer failing`",
                fix: new Fix(keyword.Span, "defer failing"));
        // `failing` and `panicking` are words only here, and only when a statement or a `(name)` binding follows:
        // `defer failing()` still calls a function named `failing`. `failing(x)` is a binding only when a statement starts
        // after its `)` on the same line; `defer failing(x)` or `defer failing(x).close()` is a call.
        if (kind == DeferKind.Always && (AtContextual("failing") || AtContextual("panicking"))
            && (StartsDeferBody(1) || IsBinding(1) && StartsDeferBody(4)))
            kind = Text(Advance()) == "failing" ? DeferKind.Failing : DeferKind.Panicking;
        Name? binding = null;
        if (kind != DeferKind.Always && IsBinding(0))
        {
            Advance();
            binding = NameOf(Advance());
            Advance();
        }
        var span = SpanFrom(start);
        var gapStart = _prevEnd;
        var errors = ErrorCount;
        var body = ParseEmbedded();
        // `defer stmt` is legal (spec §7.6), but a declaration there declares a local nothing can see.
        if (body is LocalDecl) ReportBraceless(keyword, gapStart, body, ErrorCount == errors, null);
        return new DeferStmt(span, kind, binding, body);
    }

    /// <summary>Whether a defer body starts at <paramref name="n"/>: a statement on the same line.</summary>
    private bool StartsDeferBody(int n) =>
        !PeekToken(n).HasNewlineBefore && (Peek(n) is TokenKind.Identifier or TokenKind.OpenBrace || Peek(n).IsKeyword());

    private bool IsBinding(int n) => Peek(n) == TokenKind.OpenParen && IsName(Peek(n + 1)) && Peek(n + 2) == TokenKind.CloseParen;

    private bool AtContextual(string word) => Kind == TokenKind.Identifier && Text(Current) == word;

    /// <summary><c>provide allocator: a { … }</c>, or the input form <c>context (allocator: a) { … }</c> (BYN6006).</summary>
    private Stmt ParseContext(int start)
    {
        var keyword = Advance();
        var parenthesized = keyword.Kind == TokenKind.ContextKeyword;
        if (parenthesized) Expect(TokenKind.OpenParen);
        var values = new List<NamedArg>();
        if (!parenthesized || Kind != TokenKind.CloseParen)
        {
            do
            {
                var argStart = Current.Start;
                var name = ExpectName("a context name such as `allocator`");
                Expect(TokenKind.Colon);
                // Without parentheses the block follows the value, so `a { … }` is not a struct literal.
                var value = parenthesized ? ParseExpression() : ParseBareCondition();
                values.Add(new NamedArg(SpanFrom(argStart), name, value));
            } while (Accept(TokenKind.Comma));
        }
        if (parenthesized)
        {
            Expect(TokenKind.CloseParen);
            var text = values.Count == 0 ? "…" : _source.GetText(TextSpan.FromBounds(values[0].Span.Start, values[^1].Span.End));
            Report(DiagnosticCodes.ContextBlockInput, keyword.Span, label: "the input form",
                help: $"write `provide {text} {{ }}` without the parentheses",
                fix: values.Count == 0 ? null : new Fix(TextSpan.FromBounds(keyword.Start, _prevEnd), $"provide {text}"));
        }
        var body = ParseBlock();
        return new ContextStmt(SpanFrom(start), values.ToArray(), body);
    }

    private Stmt ParseGoto(int start)
    {
        var keyword = Advance();
        if (Kind == TokenKind.CaseKeyword || Kind == TokenKind.DefaultKeyword)
        {
            Report(DiagnosticCodes.Goto, keyword.Span, label: "`goto case` does not exist",
                help: "combine patterns with `or`, or move shared code into a function; switch sections never fall through");
            Synchronize();
            return new ErrorStmt(SpanFrom(start));
        }
        Report(DiagnosticCodes.Goto, keyword.Span, label: "not a keyword in Bayan",
            help: "label the loop (`outer: for x in xs { … }`) and use `break outer` to leave it early",
            fix: new Fix(keyword.Span, "break"));
        Name? label = Kind == TokenKind.Identifier && !Current.HasNewlineBefore ? NameOf(Advance()) : null;
        ExpectEnd();
        return new BreakStmt(SpanFrom(start), label);
    }

    /// <summary>A statement that may take a postfix <c>if</c>/<c>unless</c>; its end is not consumed.</summary>
    private Stmt ParseSimpleStatement(int start)
    {
        switch (Kind)
        {
            case TokenKind.ReturnKeyword:
                {
                    var keyword = Advance();
                    var hasValue = CanStartExpression(Kind) && !BreaksLine(_pos);
                    // A value alone on the next line (one token, a literal or `.case`) or indented past `return` reads as the
                    // operand that wandered off (BYN1042); a line at the same indent that can be a statement of its own, such
                    // as a call, does not.
                    var valueOnly = EndsBefore(_pos + 1) || Kind.IsStringLiteral() || Kind is TokenKind.IntegerLiteral
                        or TokenKind.FloatLiteral or TokenKind.CharLiteral or TokenKind.InterpolatedStringStart or TokenKind.Dot
                        || IndentAt(Current.Start).Length > IndentAt(keyword.Start).Length;
                    if (BreaksLine(_pos) && !HasBlankLineBefore(_pos) && valueOnly && CanStartExpression(Kind) && !IsStatementKeyword(Kind)
                        && Kind is not (TokenKind.OpenBrace or TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.Minus
                            or TokenKind.ReturnKeyword or TokenKind.BreakKeyword or TokenKind.ContinueKeyword))
                    {
                        ReportOnNewLine(keyword);
                        hasValue = true;
                    }
                    Expr? value = hasValue ? ParseExpression() : null;
                    return new ReturnStmt(SpanFrom(start), value);
                }
            case TokenKind.FailKeyword:
                {
                    var keyword = Advance();
                    if (BreaksLine(_pos) && !HasBlankLineBefore(_pos) && CanStartExpression(Kind) && !IsStatementKeyword(Kind)) ReportOnNewLine(keyword);
                    return new FailStmt(SpanFrom(start), ParseExpression());
                }
            case TokenKind.BreakKeyword:
                return new BreakStmt(SpanFrom(start), ParseJumpLabel(Advance()));
            case TokenKind.ContinueKeyword:
                return new ContinueStmt(SpanFrom(start), ParseJumpLabel(Advance()));
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
        if (!BreaksLine(_pos) && TryAssignOp(out var op, out var width))
        {
            for (var i = 0; i < width; i++) Advance();
            CheckAssignable(expr);
            var value = ParseExpression();
            return new AssignStmt(SpanFrom(start), expr, op, value);
        }
        if (Kind is TokenKind.PlusPlus or TokenKind.MinusMinus && !BreaksLine(_pos))
        {
            var increment = Advance().Kind == TokenKind.PlusPlus;
            CheckAssignable(expr);
            return new IncDecStmt(SpanFrom(start), expr, increment);
        }
        if (ErrorCount == errors && !IsStatementExpression(expr))
        {
            Report(DiagnosticCodes.InvalidExpressionStatement, expr.Span, label: "the value is computed and discarded",
                help: "write `_ = f()` to discard a call's result; only calls, assignments and `++`/`--` can stand alone");
        }
        return new ExprStmt(SpanFrom(start), expr);
    }

    /// <summary>The label of <c>break</c>/<c>continue</c>: on its line, or alone on the next (BYN1042).</summary>
    private Name? ParseJumpLabel(Token keyword)
    {
        if (Kind != TokenKind.Identifier) return null;
        if (!Current.HasNewlineBefore) return NameOf(Advance());
        if (!BreaksLine(_pos) || HasBlankLineBefore(_pos) || !EndsBefore(_pos + 1)) return null;
        ReportOnNewLine(keyword);
        return NameOf(Advance());
    }

    /// <summary>BYN1042: the operand of <paramref name="keyword"/> starts the next line; the fix joins it to the keyword's line.</summary>
    private void ReportOnNewLine(Token keyword) =>
        Report(DiagnosticCodes.OnNewLine, Current.Span, [Text(keyword)], label: "on the next line",
            help: $"move the value to the line of `{Text(keyword)}`",
            fix: new Fix(TextSpan.FromBounds(_prevEnd, Current.Start), " "));

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
            // `map.slot(k)` is a reference; the binder checks the receiver is a map.
            case CallExpr { Callee: MemberExpr { Member.Text: "slot", NullConditional: false } }:
                return;
            case ParenExpr p:
                CheckAssignable(p.Inner);
                return;
            default:
                Report(DiagnosticCodes.InvalidAssignmentTarget, target.Span,
                    help: "assign to a variable, a field or an indexed element");
                return;
        }
    }

    /// <summary>
    /// A postfix <c>if</c>/<c>unless</c> on the statement's line, then the end of the statement. On the next line, a
    /// condition with no body is a postfix that wandered off (BYN1042); with a body it is the next statement.
    /// </summary>
    private Stmt FinishSimpleStatement(int start, Stmt statement)
    {
        var stranded = Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword && BreaksLine(_pos) && !HasBlankLineBefore(_pos)
            && Peek(1) != TokenKind.OpenParen && !BodyBraceFirst(_pos + 1);
        if (stranded)
            Report(DiagnosticCodes.PostfixOnNewLine, Current.Span, [Text(Current)], label: "on the next line",
                help: $"move the `{Text(Current)}` clause up to the statement's line",
                fix: new Fix(TextSpan.FromBounds(_prevEnd, Current.Start), " "));
        if (Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword && (stranded || !Current.HasNewlineBefore))
        {
            var keyword = Advance();
            var unless = keyword.Kind == TokenKind.UnlessKeyword;
            var unlikely = !unless && AtUnlikely();
            if (unlikely) Advance();
            if (unless) SkipUnlessLikelihood();
            var condition = ParseExpression();
            if (unless) CheckUnlessCondition(condition, TextSpan.FromBounds(keyword.Start, condition.Span.End));
            statement = new GuardedStmt(SpanFrom(start), statement, unless, condition) { Unlikely = unlikely };
            if (Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword && !BreaksLine(_pos))
            {
                Report(DiagnosticCodes.PostfixChained, Current.Span, label: "a second condition",
                    help: "combine the conditions with `&&` in one postfix `if`");
                Advance();
                ParseExpression();
            }
        }
        ExpectEnd();
        return statement;
    }

    /// <summary><c>{ … } if x</c> and <c>if (a) { … } if b</c> on one line: postfix conditions only attach to simple statements.</summary>
    private Stmt RejectPostfixAfterCompound(Stmt statement)
    {
        if (Kind is TokenKind.IfKeyword or TokenKind.UnlessKeyword && !Current.HasNewlineBefore && Peek(1) != TokenKind.OpenParen
            && !BodyBraceFirst(_pos + 1))
        {
            Report(DiagnosticCodes.PostfixNotSimple, Current.Span, [Text(Current)],
                help: "put the condition first: `if cond { … }`");
            Advance();
            ParseExpression();
            ExpectEnd();
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

    private static bool CanStartExpression(TokenKind kind) => kind.IsContextualKeyword() || kind is
        TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral or TokenKind.CharLiteral or
        TokenKind.StringLiteral or TokenKind.RawStringLiteral or TokenKind.CStringLiteral or TokenKind.U16StringLiteral or
        TokenKind.InterpolatedStringStart or TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace or
        TokenKind.Dot or TokenKind.DotDot or TokenKind.DotDotEquals or TokenKind.Plus or TokenKind.Minus or TokenKind.Bang or
        TokenKind.NotKeyword or TokenKind.Tilde or TokenKind.Caret or TokenKind.TrueKeyword or TokenKind.FalseKeyword or TokenKind.NullKeyword or
        TokenKind.ThisKeyword or TokenKind.TryKeyword or TokenKind.NewKeyword or TokenKind.MoveKeyword or TokenKind.ReturnKeyword or
        TokenKind.FailKeyword or TokenKind.BreakKeyword or TokenKind.ContinueKeyword;

    public Expr ParseExpression()
    {
        var start = Current.Start;
        var condition = ParseBinary(Prec.Coalesce);
        if (Kind != TokenKind.Question || StopsHere) return condition;
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
            TokenKind.BarBar or TokenKind.OrKeyword => (BinaryOp.LogicalOr, Prec.LogicalOr, 1),
            TokenKind.AmpAmp or TokenKind.AndKeyword => (BinaryOp.LogicalAnd, Prec.LogicalAnd, 1),
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
        return ParseBinaryRest(start, left, minPrec);
    }

    /// <summary>The operators after <paramref name="left"/>, which started at <paramref name="start"/>.</summary>
    private Expr ParseBinaryRest(int start, Expr left, Prec minPrec)
    {
        while (true)
        {
            if (StopsHere) return left;
            if (Kind == TokenKind.IsKeyword && minPrec <= Prec.Relational)
            {
                Advance();
                var pattern = ParsePattern();
                left = new IsExpr(SpanFrom(start), left, pattern);
                continue;
            }
            if (Kind == TokenKind.ExceptKeyword && minPrec <= Prec.Relational)
            {
                Advance();
                left = new ExceptExpr(SpanFrom(start), left, ParseExceptPattern());
                continue;
            }
            if (Kind == TokenKind.InKeyword && minPrec <= Prec.Relational)
            {
                Advance();
                var range = ParseBinary(Prec.Range);
                left = new InExpr(SpanFrom(start), left, range);
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
            // A line ending in an operator continues (lā), but not across a blank line outside brackets.
            if (BreaksLine(_pos) && HasBlankLineBefore(_pos))
                Report(DiagnosticCodes.ExpectedExpression, new TextSpan(_prevEnd, 0), ["a blank line"], label: "the statement ends here",
                    help: "remove the blank line, or finish the expression on this line");
            // `??` is right-associative; everything else is left-associative.
            var right = op == BinaryOp.Coalesce ? ParseBinary(prec) : ParseBinary(prec + 1);
            left = new BinaryExpr(SpanFrom(start), op, left, right);
        }
    }

    /// <summary>
    /// The pattern after <c>except</c>. A constant followed by a comparison or logical operator is a condition (BYN1052);
    /// the whole condition is parsed so the statement still ends where it should.
    /// </summary>
    private Pattern ParseExceptPattern()
    {
        var pattern = ParsePattern();
        if (pattern is not ConstantPattern constant || StopsHere) return pattern;
        if (BinaryOperator().Prec is Prec.None or Prec.Coalesce && Kind is not (TokenKind.IsKeyword or TokenKind.InKeyword)) return pattern;
        var condition = ParseBinaryRest(constant.Span.Start, constant.Value, Prec.LogicalOr);
        Report(DiagnosticCodes.ExceptNotPattern, condition.Span, label: "a condition", help: "use `where` for a condition");
        return new ConstantPattern(condition.Span, condition);
    }

    private bool CanStartRangeEnd() =>
        CanStartExpression(Kind) && Kind is not (TokenKind.OpenBrace or TokenKind.DotDot or TokenKind.DotDotEquals);

    private Expr ParseCatch(int start, Expr operand)
    {
        Advance();
        Name? binding = null;
        Expr? variant = null;
        if (Kind == TokenKind.OpenParen && IsName(Peek(1)) && Peek(2) == TokenKind.CloseParen)
        {
            var open = Advance();
            binding = NameOf(Advance());
            var span = TextSpan.FromBounds(open.Start, Advance().End);
            if (Kind == TokenKind.SwitchKeyword)
                Report(DiagnosticCodes.CatchBinding, span, label: "the input form",
                    help: $"remove `({binding.Text})` and bind the error in each arm: `E {binding.Text} => …`",
                    fix: new Fix(TextSpan.FromBounds(open.Start, Current.Start), ""));
            else
                Report(DiagnosticCodes.CatchBinding, span, label: "the input form",
                    help: $"write `catch switch {{ E {binding.Text} => … }}`, binding the error in each arm");
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
                : new CaughtErrorExpr(Current.Span);
            Advance();
            handler = ParseSwitchArms(switchStart, subject);
        }
        else if (Kind == TokenKind.OpenBrace && !(IsName(Peek(1)) && Peek(2) == TokenKind.Colon && !BraceBodyHasStatementBreak()))
        {
            // `catch { x: 1 }` stays a target-typed struct literal; `catch {}`, `catch { x = 1 }` and a body of
            // several statements, one per line, are blocks.
            var block = ParseBlock();
            handler = new BlockExpr(block.Span, block);
        }
        else
        {
            handler = ParseBinary(Prec.LogicalOr);
        }
        return new CatchExpr(SpanFrom(start), operand, binding, variant, handler);
    }

    private Expr ParseUnary()
    {
        // `with` binds looser than every prefix operator: `move tx with { … }` is `(move tx) with { … }` (revision 0008).
        var start = Current.Start;
        var expr = ParsePrefixed();
        while (!BreaksLine(_pos))
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
                break;
            }
        }
        return expr;
    }

    private Expr ParsePrefixed()
    {
        var start = Current.Start;
        UnaryOp? op = Kind switch
        {
            TokenKind.Plus => UnaryOp.Plus,
            TokenKind.Minus => UnaryOp.Negate,
            TokenKind.Bang or TokenKind.NotKeyword => UnaryOp.Not,
            TokenKind.Tilde => UnaryOp.Complement,
            TokenKind.Caret => UnaryOp.FromEnd,
            _ => null,
        };
        if (op is not null)
        {
            Advance();
            var operand = ParsePrefixed();
            return new UnaryExpr(SpanFrom(start), op.Value, operand);
        }
        switch (Kind)
        {
            case TokenKind.TryKeyword:
                // `try` covers the whole expression to its right, down to `??`; `?:` and assignment stay outside (§18, J18).
                Advance();
                {
                    var covered = ParseBinary(Prec.Coalesce);
                    return new TryExpr(SpanFrom(start), covered);
                }
            case TokenKind.MoveKeyword:
                Advance();
                return new MoveExpr(SpanFrom(start), ParsePrefixed());
            case TokenKind.NewKeyword:
                // Accepted for C# compatibility; the formatter removes it.
                Advance();
                return ParseUnary();
            case TokenKind.ReturnKeyword:
                {
                    Advance();
                    Expr? value = CanStartExpression(Kind) && !BreaksLine(_pos)
                        && Kind is not (TokenKind.ReturnKeyword or TokenKind.BreakKeyword or TokenKind.ContinueKeyword)
                        ? ParseBinary(Prec.LogicalOr)
                        : null;
                    return new JumpExpr(SpanFrom(start), new ReturnStmt(SpanFrom(start), value));
                }
            case TokenKind.FailKeyword:
                Advance();
                return new JumpExpr(SpanFrom(start), new FailStmt(SpanFrom(start), ParseBinary(Prec.LogicalOr)));
            case TokenKind.BreakKeyword:
                Advance();
                return new JumpExpr(SpanFrom(start), new BreakStmt(SpanFrom(start), Kind == TokenKind.Identifier && !Current.HasNewlineBefore ? NameOf(Advance()) : null));
            case TokenKind.ContinueKeyword:
                Advance();
                return new JumpExpr(SpanFrom(start), new ContinueStmt(SpanFrom(start), Kind == TokenKind.Identifier && !Current.HasNewlineBefore ? NameOf(Advance()) : null));
            case TokenKind.OpenParen when IsCast():
                {
                    Advance();
                    var type = ParseType();
                    Expect(TokenKind.CloseParen);
                    var typeSpan = SpanFrom(start);
                    var operand = ParsePrefixed();
                    var typeText = _source.GetText(type.Span);
                    var inner = operand is ParenExpr p ? p.Inner : operand;
                    Report(DiagnosticCodes.CastInput, typeSpan, [typeText], label: "a cast",
                        help: $"call the conversion: `{typeText}(x)`",
                        fix: new Fix(SpanFrom(start), $"{typeText}({_source.GetText(inner.Span)})"));
                    return new CastExpr(SpanFrom(start), type, operand);
                }
        }

        // `switch` stays tighter than prefix operators: `move x switch { … }` is `move (x switch { … })` (§9.3).
        var expr = ParsePostfix(ParsePrimary());
        while (!BreaksLine(_pos) && Kind == TokenKind.SwitchKeyword && Peek(1) == TokenKind.OpenBrace)
        {
            Advance();
            expr = ParseSwitchArms(start, expr);
        }
        return expr;
    }

    private bool IsCast()
    {
        var after = ScanType(_pos + 1);
        if (after < 0 || KindAt(after) != TokenKind.CloseParen) return false;
        return KindAt(after + 1).IsContextualKeyword() || KindAt(after + 1) is
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
            var pattern = ParseArmPattern("");
            Expr? guard = null;
            if (AtContextual("when"))
            {
                Advance();
                guard = WithoutLambdas(ParseExpression);
            }
            Expect(TokenKind.FatArrow);
            // A `catch switch` arm may be a block, read as a `catch` block is (J49); elsewhere `{` starts a struct literal.
            Expr result;
            if (value is CaughtErrorExpr && Kind == TokenKind.OpenBrace && !(IsName(Peek(1)) && Peek(2) == TokenKind.Colon && !BraceBodyHasStatementBreak()))
            {
                var block = ParseBlock();
                result = new BlockExpr(block.Span, block);
            }
            else
            {
                result = ParseExpression();
            }
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
            if (StopsHere) return expr;
            switch (Kind)
            {
                case TokenKind.Dot when IsName(Peek(1)):
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
                        var call = new CallExpr(SpanFrom(start), expr, args);
                        ReportTrustInput(call);
                        expr = call with { Because = ParseBecause(_source.GetText(expr.Span) + "()", call.Span) };
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
        var after = ScanExprTypeArgs(_pos);
        return after > 0 && KindAt(after) == TokenKind.OpenParen;
    }

    /// <summary>
    /// BYN1062 at <c>trust(x, reason: …)</c>; the fix writes <c>x.trustUnchecked() because "…"</c> when the reason is a
    /// string literal, with <c>x</c> in parentheses unless it is a primary expression, and the whole in parentheses
    /// unless the call ends its expression.
    /// </summary>
    private void ReportTrustInput(CallExpr call)
    {
        if (call.Callee is not NameExpr { Name.Text: "trust", TypeArgs.Length: 0 } callee
            || call.Args is not [{ Name: null, Modifier: ArgModifier.None } value, { Name.Text: "reason", Modifier: ArgModifier.None } reason])
            return;
        Fix? fix = null;
        if (reason.Value is LiteralExpr { Kind: LiteralKind.String })
        {
            var text = _source.GetText(value.Value.Span);
            if (value.Value is not (NameExpr or MemberExpr or CallExpr or IndexExpr or LiteralExpr or ParenExpr)) text = "(" + text + ")";
            text = $"{text}.trustUnchecked() because {_source.GetText(reason.Value.Span)}";
            // Anything after the call other than a closer would read as part of the reason.
            var ends = Current.HasNewlineBefore || Kind is TokenKind.CloseParen or TokenKind.Comma or TokenKind.CloseBracket
                or TokenKind.CloseBrace or TokenKind.InterpolationEnd or TokenKind.EndOfFile;
            fix = new Fix(call.Span, ends ? text : "(" + text + ")");
        }
        Report(DiagnosticCodes.TrustInput, callee.Span, label: "the input form",
            help: "call `trustUnchecked()` on the value and give the reason in `because`", fix: fix);
    }

    private Argument[] ParseArguments() => WithoutLambdas(ParseArgumentList, off: false);

    private Argument[] ParseArgumentList()
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
                // `for` and `as` are keywords, but labels in argument position (`for: user`).
                if ((IsName(Kind) || Kind is TokenKind.ForKeyword or TokenKind.AsKeyword) && Peek(1) == TokenKind.Colon)
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
            case TokenKind.Dot when IsName(Peek(1)):
            {
                Advance();
                var member = NameOf(Advance());
                return new TargetMemberExpr(SpanFrom(start), member);
            }
            case TokenKind.OpenParen when !_noLambda && KindAt(MatchingClose(_pos) + 1) == TokenKind.FatArrow:
                return ParseLambda();
            case TokenKind.OpenParen:
                return ParseParenOrTuple();
            case TokenKind.OpenBracket:
                return ParseCollection();
            case var kind when IsName(kind) && Peek(1) == TokenKind.FatArrow && !_noLambda:
                return ParseLambda();
            case TokenKind.OpenBrace when BraceLiteralAllowed():
                {
                    var fields = ParseFieldInits();
                    return new StructLiteralExpr(SpanFrom(start), null, fields);
                }
            case TokenKind.Identifier when IsPatternExprStart():
                Report(DiagnosticCodes.PatternOutsideConst, token.Span, [], label: "`pattern` outside a `const`",
                    help: "declare the pattern as `const name = pattern T { … }`");
                return ParsePatternExpr();
            case TokenKind.Identifier:
            case var kind when kind.IsContextualKeyword():
                return ParseNameOrStructLiteral();
            case TokenKind.NeverKeyword:
                Advance();
                return new NameExpr(token.Span, NameOf(token), []);
        }
        Report(DiagnosticCodes.ExpectedExpression, Current.Span, [Describe(Current)]);
        return new ErrorExpr(new TextSpan(start, 0));
    }

    /// <summary>
    /// <c>x =&gt; body</c>, <c>(a, b) =&gt; body</c> or <c>() =&gt; body</c> (R5); the body is a block on <c>{</c>, else an
    /// expression, which may start on the line after <c>=&gt;</c>. A parameter is a name only: a type, a modifier, a particle
    /// or a default is an error.
    /// </summary>
    private LambdaExpr ParseLambda()
    {
        var start = Current.Start;
        var parameters = new List<Name>();
        if (Kind != TokenKind.OpenParen)
        {
            parameters.Add(NameOf(Advance()));
        }
        else
        {
            var close = MatchingClose(_pos);
            Advance();
            while (_pos < close)
            {
                if (IsName(Kind) && Peek(1) is TokenKind.Comma or TokenKind.CloseParen)
                {
                    parameters.Add(NameOf(Advance()));
                    if (_pos < close) Advance();
                    continue;
                }
                if (Kind == TokenKind.Comma)
                {
                    Report(DiagnosticCodes.Expected, Current.Span, ["a lambda parameter name", "`,`"], label: "an empty parameter",
                        help: "write the parameter's name");
                    Advance();
                    continue;
                }
                var from = _pos;
                var depth = 0;
                for (; _pos < close && !(depth == 0 && Kind == TokenKind.Comma); Advance())
                {
                    if (Kind is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace) depth++;
                    else if (Kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace) depth--;
                }
                var text = TokensText(from, _pos);
                Report(DiagnosticCodes.Expected, TextSpan.FromBounds(_tokens[from].Start, _prevEnd), ["a lambda parameter name", $"`{text}`"],
                    label: "not a bare name", help: "write the parameter's name only; a lambda takes its parameter types from the function type it is passed to");
                if (_pos < close) Advance();
            }
            Expect(TokenKind.CloseParen);
            // A mismatched closer (`(a] => 1`) was reported by Expect; step over it to the `=>`.
            if (_pos == close && Kind != TokenKind.FatArrow) Advance();
        }
        Expect(TokenKind.FatArrow);
        if (Kind == TokenKind.OpenBrace) return new LambdaExpr(SpanFrom(start), parameters.ToArray(), null, ParseBlock());
        var body = ParseExpression();
        return new LambdaExpr(SpanFrom(start), parameters.ToArray(), body, null);
    }

    /// <summary>
    /// The contextual <c>pattern</c> followed on its line by a type name; <c>pattern</c> is a name everywhere else, and a
    /// name followed by a name is never an expression.
    /// </summary>
    private bool IsPatternExprStart() =>
        AtContextual("pattern") && Peek(1) == TokenKind.Identifier && !PeekToken(1).HasNewlineBefore;

    private PatternExpr ParsePatternExpr()
    {
        var start = Advance().Start;
        var pattern = ParsePattern();
        return new PatternExpr(SpanFrom(start), pattern);
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
            var after = ScanExprTypeArgs(_pos);
            if (after > 0 && KindAt(after) is TokenKind.OpenParen or TokenKind.Dot or TokenKind.CloseParen or TokenKind.Comma
                    or TokenKind.Semicolon or TokenKind.CloseBracket or TokenKind.CloseBrace or TokenKind.OpenBrace)
                typeArgs = ParseTypeArgs();
        }

        if (Kind == TokenKind.OpenBrace && !BreaksLine(_pos) && IsStructLiteralBody() && char.IsUpper(name.Text[0]) && BraceLiteralAllowed())
        {
            var type = new NamedType(SpanFrom(start), [name], typeArgs);
            var fields = ParseFieldInits();
            return new StructLiteralExpr(SpanFrom(start), type, fields);
        }
        // A state literal `Order.paid { … }` (spec §3.17).
        if (Kind == TokenKind.Dot && Peek(1) == TokenKind.Identifier && char.IsLower(Text(PeekToken(1))[0]) && Peek(2) == TokenKind.OpenBrace
            && !BreaksLine(_pos + 2) && (Peek(3) == TokenKind.CloseBrace || (IsName(Peek(3)) && Peek(4) is TokenKind.Colon or TokenKind.Equals))
            && char.IsUpper(name.Text[0]) && BraceLiteralAllowed())
        {
            Advance();
            var state = NameOf(Advance());
            var type = new NamedType(SpanFrom(start), [name, state], typeArgs);
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
        if (path.Count == 1 && args.Length == 0 && CSharpTypeNames.TryGetValue(path[0].Text, out var bayan))
            // Only types reach here, so `int` as a value or variable name is untouched.
            path[0] = path[0] with { Text = bayan };
        else if (path.Count == 1 && args.Length == 0 && path[0].Text == "char")
        {
            Report(DiagnosticCodes.CharInput, path[0].Span, label: "the input form", help: "rename the type to `rune`",
                fix: new Fix(path[0].Span, "rune"));
            path[0] = path[0] with { Text = "rune" };
        }
        return new NamedType(SpanFrom(start), path.ToArray(), args);
    }

    /// <summary>
    /// Whether the brace body opening at the current token separates statements at its own depth: a line break
    /// not after <c>,</c> or <c>{</c> (fields of a struct literal end their lines with <c>,</c>), or a <c>;</c>.
    /// </summary>
    private bool BraceBodyHasStatementBreak()
    {
        var depth = 0;
        for (var i = _pos + 1; i < _count; i++)
        {
            if (depth == 0 && _tokens[i].HasNewlineBefore && KindAt(i) != TokenKind.CloseBrace
                && KindAt(i - 1) is not (TokenKind.Comma or TokenKind.OpenBrace))
                return true;
            switch (KindAt(i))
            {
                case TokenKind.OpenBrace or TokenKind.OpenParen or TokenKind.OpenBracket:
                    depth++;
                    break;
                case TokenKind.CloseBrace or TokenKind.CloseParen or TokenKind.CloseBracket:
                    if (depth-- == 0) return false;
                    break;
                case TokenKind.Semicolon when depth == 0:
                    return true;
                case TokenKind.EndOfFile:
                    return false;
            }
        }
        return false;
    }

    /// <summary>At <c>{</c>: is this <c>{ }</c> or <c>{ name: …</c> (or the older <c>{ name = …</c>), i.e. a struct literal rather than a block?</summary>
    private bool IsStructLiteralBody() =>
        Peek(1) == TokenKind.CloseBrace || (IsName(Peek(1)) && Peek(2) is TokenKind.Colon or TokenKind.Equals);

    private FieldInit[] ParseFieldInits()
    {
        var open = Expect(TokenKind.OpenBrace);
        var fields = new List<FieldInit>();
        while (Kind != TokenKind.CloseBrace && !AtEnd)
        {
            var start = Current.Start;
            var before = _pos;
            var name = ExpectName("a field name");
            if (Kind == TokenKind.Equals)
            {
                var nameEnd = _prevEnd;
                var equals = Advance();
                Report(DiagnosticCodes.FieldInitEquals, equals.Span, [name.Text], label: "the input form",
                    help: "write `:` after the field name", fix: new Fix(TextSpan.FromBounds(nameEnd, equals.End), ":"));
            }
            else
            {
                Expect(TokenKind.Colon);
            }
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
            Report(DiagnosticCodes.ExpectedExpression, Current.Span, [Describe(Current)], help: "write a value; `()` is not one, and a tuple has at least two elements");
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
        if (IsName(Kind) && Peek(1) == TokenKind.Colon)
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
                var operand = ParseExpression();
                element = new SpreadExpr(SpanFrom(elementStart), operand);
            }
            else
            {
                element = ParseCollectionPart();
                if (Accept(TokenKind.Colon))
                    element = new MapEntryExpr(SpanFrom(elementStart), element, ParseCollectionPart());
            }
            elements.Add(element);
            if (!Accept(TokenKind.Comma)) break;
            if (_pos == before) break;
        }
        Expect(TokenKind.CloseBracket);
        return new CollectionExpr(SpanFrom(start), elements.ToArray());
    }

    /// <summary>An element, map key or map value; a bare conditional is BYN1013.</summary>
    private Expr ParseCollectionPart()
    {
        var part = ParseExpression();
        if (part is ConditionalExpr)
        {
            Report(DiagnosticCodes.TernaryInCollection, part.Span,
                help: "wrap it in parentheses",
                fix: new Fix(part.Span, "(" + _source.GetText(part.Span) + ")"));
        }
        return part;
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
                        var specs = Kind switch
                        {
                            TokenKind.Comma => ParseFormatSpecs(),
                            TokenKind.Colon => ParseCompactSpec(),
                            _ => [],
                        };
                        if (Kind != TokenKind.InterpolationEnd)
                        {
                            Report(DiagnosticCodes.Expected, Current.Span, ["`}` to end the interpolated expression", Describe(Current)]);
                            while (Kind is not (TokenKind.InterpolationEnd or TokenKind.InterpolatedStringEnd or TokenKind.EndOfFile or TokenKind.Semicolon))
                                Advance();
                        }
                        Accept(TokenKind.InterpolationEnd);
                        parts.Add(new InterpolationHole(SpanFrom(holeStart), expr) { Specs = specs });
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

    /// <summary><c>, as: .hex, width: 6</c> after a hole's value: the named format specs (SPEC §6.6).</summary>
    private Argument[] ParseFormatSpecs()
    {
        var specs = new List<Argument>();
        while (Accept(TokenKind.Comma))
        {
            var specStart = Current.Start;
            // `as` is a keyword, but the name of a spec.
            if (!(IsName(Kind) || Kind == TokenKind.AsKeyword) || Peek(1) != TokenKind.Colon)
            {
                Report(DiagnosticCodes.Expected, Current.Span, ["a named format spec such as `as: .hex`", Describe(Current)]);
                break;
            }
            var name = NameOf(Advance());
            Advance();
            specs.Add(new Argument(SpanFrom(specStart), name, ArgModifier.None, ParseExpression()));
        }
        return specs.ToArray();
    }

    /// <summary>
    /// <c>:hex</c>, <c>:binary</c>, <c>:octal</c>, <c>:iso</c>, <c>:debug</c> or <c>:.2</c> after a hole's value: the
    /// BYN1068 input forms of <c>, as: .hex</c> and <c>, digits: 2</c>, parsed as those specs.
    /// </summary>
    private Argument[] ParseCompactSpec()
    {
        var colon = Advance();
        var first = _pos;
        while (Kind is not (TokenKind.InterpolationEnd or TokenKind.InterpolatedStringEnd or TokenKind.EndOfFile)) Advance();
        if (_pos == first)
        {
            Report(DiagnosticCodes.Expected, Current.Span, ["a format spec", Describe(Current)]);
            return [];
        }
        var specSpan = TextSpan.FromBounds(_tokens[first].Start, _prevEnd);
        var spec = _source.GetText(specSpan);
        string? name = null, value = null;
        if (spec is "hex" or "binary" or "octal" or "iso" or "debug") (name, value) = ("as", "." + spec);
        else if (spec.Length > 1 && spec[0] == '.' && spec[1..].All(char.IsAsciiDigit)) (name, value) = ("digits", spec[1..]);
        if (name is null || value is null)
        {
            Report(DiagnosticCodes.Expected, TextSpan.FromBounds(colon.Start, _prevEnd), ["a named format spec such as `, as: .hex`", $"`:{spec}`"]);
            return [];
        }
        var canonical = $"{name}: {value}";
        Report(DiagnosticCodes.CompactFormatSpec, TextSpan.FromBounds(colon.Start, _prevEnd), [":" + spec, ", " + canonical],
            label: "the input form", help: $"write the spec as named arguments: `, {canonical}`",
            fix: new Fix(TextSpan.FromBounds(colon.Start, _prevEnd), ", " + canonical));
        Expr specValue = value[0] == '.'
            ? new TargetMemberExpr(specSpan, new Name(specSpan, value[1..]))
            : new LiteralExpr(specSpan, LiteralKind.Integer, value, null);
        return [new Argument(specSpan, new Name(colon.Span, name), ArgModifier.None, specValue)];
    }

    // ── Patterns ─────────────────────────────────────────────────────────────────────

    private Pattern ParsePattern() => WithoutLambdas(() =>
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
    });

    /// <summary>Runs <paramref name="parse"/> with <see cref="_noLambda"/> set, or cleared when <paramref name="off"/> is false.</summary>
    private T WithoutLambdas<T>(Func<T> parse, bool off = true)
    {
        var saved = _noLambda;
        _noLambda = off;
        try
        {
            return parse();
        }
        finally
        {
            _noLambda = saved;
        }
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
            case TokenKind.VarKeyword or TokenKind.LetKeyword:
            {
                var isLet = Advance().Kind == TokenKind.LetKeyword;
                return new VarPattern(SpanFrom(start), ExpectName("a variable name"), isLet);
            }
            case TokenKind.Dot when IsName(Peek(1)) && Peek(2) == TokenKind.OpenParen:
            {
                Advance();
                var name = NameOf(Advance());
                var variant = new TargetMemberExpr(SpanFrom(start), name);
                Advance();
                var elements = new List<Pattern>();
                if (Kind != TokenKind.CloseParen)
                {
                    elements.Add(ParsePattern());
                    while (Accept(TokenKind.Comma)) elements.Add(ParsePattern());
                }
                Expect(TokenKind.CloseParen);
                return new VariantPattern(SpanFrom(start), variant, elements.ToArray());
            }
            // `.percentOff p`: binds the whole value, narrowed to the case; `when` starts a guard.
            case TokenKind.Dot when IsName(Peek(1)) && Peek(2) == TokenKind.Identifier && !PeekToken(2).HasNewlineBefore
                && Text(PeekToken(2)) != "when":
            {
                Advance();
                var variant = new TargetMemberExpr(SpanFrom(start), NameOf(Advance()));
                var binding = NameOf(Advance());
                return new VariantPattern(SpanFrom(start), variant, []) { Binding = binding };
            }
            case TokenKind.Identifier when Text(Current) == "_" && Peek(1) is not (TokenKind.Dot or TokenKind.OpenParen):
                Advance();
                return new DiscardPattern(SpanFrom(start));
            case TokenKind.OpenBrace when BraceLiteralAllowed():
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
                    if (after > 0 && KindAt(after) == TokenKind.OpenBrace && BraceLiteralAllowed())
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
                    if (after > 0 && KindAt(after) == TokenKind.Identifier && Text(_tokens[after]) != "when")
                    {
                        // `DbError e`: a declaration pattern binding the matched value at the written type.
                        var type = ParseType();
                        var name = NameOf(Advance());
                        return new DeclarationPattern(SpanFrom(start), type, name);
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
        if (IsName(Kind) && !AtContextual("when") && !Current.HasNewlineBefore) designation = NameOf(Advance());
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
