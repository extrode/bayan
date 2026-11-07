namespace Bayan.Syntax.Diagnostics;

/// <summary>
/// The message table. Every code here has <c>docs/diagnostics/BYN####.md</c> and at least one
/// golden case; <c>DiagnosticCodesTests</c> enforces both.
/// </summary>
public static class DiagnosticCodes
{
    private static readonly List<DiagnosticDescriptor> Registry = new();

    private static DiagnosticDescriptor Error(string code, string format) => Register(new(code, Severity.Error, format));
    private static DiagnosticDescriptor Warning(string code, string format) => Register(new(code, Severity.Warning, format));

    private static DiagnosticDescriptor Register(DiagnosticDescriptor d)
    {
        Registry.Add(d);
        return d;
    }

    public static IReadOnlyList<DiagnosticDescriptor> All => Registry;

    // ── Lexer 0001–0999 ──────────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor BidiControl = Error("BYN0001", "bidirectional control character U+{0} is not allowed outside string literals");
    public static readonly DiagnosticDescriptor UnexpectedCharacter = Error("BYN0002", "unexpected character `{0}`");
    public static readonly DiagnosticDescriptor UnterminatedString = Error("BYN0003", "unterminated string literal");
    public static readonly DiagnosticDescriptor UnterminatedComment = Error("BYN0004", "unterminated block comment");
    public static readonly DiagnosticDescriptor IdentifierNotNfc = Error("BYN0005", "identifier `{0}` is not in Unicode normalization form C");
    public static readonly DiagnosticDescriptor InvalidEscape = Error("BYN0006", "invalid escape sequence `{0}`");
    public static readonly DiagnosticDescriptor EmptyCharLiteral = Error("BYN0007", "empty char literal");
    public static readonly DiagnosticDescriptor UnterminatedCharLiteral = Error("BYN0008", "unterminated char literal");
    public static readonly DiagnosticDescriptor CharLiteralTooLong = Error("BYN0009", "a char literal holds exactly one Unicode scalar");
    public static readonly DiagnosticDescriptor InvalidNumber = Error("BYN0010", "invalid numeric literal: {0}");
    public static readonly DiagnosticDescriptor SuffixOnHexOrBinary = Error("BYN0011", "unit suffix `{0}` is not allowed on a {1} literal");
    public static readonly DiagnosticDescriptor ReservedKeyword = Error("BYN0012", "`{0}` is reserved and cannot be used as an identifier");
    public static readonly DiagnosticDescriptor InvalidUnicodeEscape = Error("BYN0013", "`{0}` is not a Unicode scalar value");
    public static readonly DiagnosticDescriptor UnescapedCloseBrace = Error("BYN0014", "`}}` in an interpolated string must be doubled");
    public static readonly DiagnosticDescriptor InvalidUtf8 = Error("BYN0015", "source is not valid UTF-8 (byte 0x{0})");
    public static readonly DiagnosticDescriptor InterpolationTooDeep = Error("BYN0016", "interpolated strings are nested too deeply");

    // ── Parser 1000–1999 ─────────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor Expected = Error("BYN1001", "expected {0}, found {1}");
    public static readonly DiagnosticDescriptor ExpectedExpression = Error("BYN1002", "expected an expression, found {0}");
    public static readonly DiagnosticDescriptor ExpectedType = Error("BYN1003", "expected a type, found {0}");
    public static readonly DiagnosticDescriptor ExpectedMember = Error("BYN1004", "expected a field, property, constructor or method, found {0}");
    public static readonly DiagnosticDescriptor MissingLineBreak = Error("BYN1005", "expected a line break after the statement");
    public static readonly DiagnosticDescriptor UnlessWithElse = Error("BYN1006", "`unless` cannot have an `else` branch");
    public static readonly DiagnosticDescriptor UnlessNegated = Error("BYN1007", "`unless` with a negated condition is a double negative");
    public static readonly DiagnosticDescriptor PostfixNotSimple = Error("BYN1008", "a postfix `{0}` applies only to a simple statement");
    public static readonly DiagnosticDescriptor PostfixChained = Error("BYN1009", "postfix conditions cannot be chained");
    public static readonly DiagnosticDescriptor Goto = Error("BYN1010", "`goto` does not exist in Bayan");
    public static readonly DiagnosticDescriptor InvalidAssignmentTarget = Error("BYN1011", "cannot assign to this expression");
    public static readonly DiagnosticDescriptor InvalidExpressionStatement = Error("BYN1012", "this expression does nothing as a statement");
    public static readonly DiagnosticDescriptor TernaryInCollection = Error("BYN1013", "a conditional expression inside a collection literal must be parenthesized");
    public static readonly DiagnosticDescriptor StackedCaseLabels = Error("BYN1014", "stacked `case` labels");
    public static readonly DiagnosticDescriptor CStyleFor = Error("BYN1015", "C-style `for` loops do not exist in Bayan");
    public static readonly DiagnosticDescriptor ModifierNotAllowed = Error("BYN1016", "`{0}` is not allowed here");
    // BYN1049 is retired before it was raised: a round-3 draft gave `unless not` this code, which BYN1007 covers (and an earlier draft used it for a return-type order check, now covered by BYN1043 `TypeNameOrder`). Never reuse it.
    public static readonly DiagnosticDescriptor RetiredUnlessNot = Error("BYN1049", "retired: merged into BYN1007");
    public static readonly DiagnosticDescriptor ParenthesizedCondition = Error("BYN1017", "parentheses around a condition are an input form");
    public static readonly DiagnosticDescriptor BodyNeedsBraces = Error("BYN1018", "the body of `{0}` must be a block");
    public static readonly DiagnosticDescriptor ForInputForm = Error("BYN1019", "`for (let x in xs)` is an input form; write `for x in xs`");
    public static readonly DiagnosticDescriptor ErrdeferInput = Error("BYN1020", "`errdefer` is now `defer failing`");
    public static readonly DiagnosticDescriptor FieldInitEquals = Error("BYN1021", "`{0} = …` in a literal is an input form; write `{0}: …`");
    public static readonly DiagnosticDescriptor UnlessCompound = Error("BYN1022", "`unless` with `&&`/`||` reads as a double negative");
    public static readonly DiagnosticDescriptor UnlessNull = Error("BYN1023", "`unless x is null` is not allowed; write `if x is not null`");
    public static readonly DiagnosticDescriptor CatchBinding = Error("BYN1025", "`catch (e)` is an input form; bind the error in a `catch switch` arm");
    public static readonly DiagnosticDescriptor DelegateDecl = Error("BYN1028", "`delegate` is not Bayan; write a function type `type {0} = {1}`");
    public static readonly DiagnosticDescriptor ErrorSetPipe = Error("BYN1029", "an error set is declared `union error {0} = A, B`");
    // BYN1030 is retired (design round 3 §28): the C# primitive names are accepted in type positions and `bayan format` respells them per `style.primitives`. Never reuse it.
    public static readonly DiagnosticDescriptor RetiredCSharpTypeName = Error("BYN1030", "retired: C# primitive names are accepted");
    public static readonly DiagnosticDescriptor FailsShorthand = Error("BYN1031", "`T!E` is an input form; write `T f() fails E`");
    // BYN1032 is retired (design round 3 §5): `if !x {}` and `if not x {}` are both accepted and nothing rewrites them. Never reuse it.
    public static readonly DiagnosticDescriptor RetiredNegatedIfToUnless = Error("BYN1032", "retired: `if !x` is no longer rewritten");
    public static readonly DiagnosticDescriptor UnlessLikelihood = Error("BYN1033", "`unless` takes no likelihood word; write `if unlikely !…` or drop `{0}`");
    public static readonly DiagnosticDescriptor TypeNameOrder = Error("BYN1043", "`{0}` is an input form; write `{1}`");
    public static readonly DiagnosticDescriptor SemicolonInput = Error("BYN1040", "`;` is an input form; a statement ends at the newline");
    public static readonly DiagnosticDescriptor ElseOnNewLine = Error("BYN1041", "`else` must follow `}}` on the same line");
    public static readonly DiagnosticDescriptor OnNewLine = Error("BYN1042", "`{0}` takes its value on the same line");
    /// <summary>BYN1042's second message form, for a stranded postfix modifier; registered once, as <see cref="OnNewLine"/>.</summary>
    public static readonly DiagnosticDescriptor PostfixOnNewLine = new("BYN1042", Severity.Error, "a postfix `{0}` must be on the line of its statement");
    public static readonly DiagnosticDescriptor DeclarationKeywordOrder = Error("BYN1044", "`{0}` is an input form; write `{1}`");
    public static readonly DiagnosticDescriptor PatternOutsideConst = Error("BYN1046", "`pattern` is allowed only after `=` in a `const`");
    public static readonly DiagnosticDescriptor BracketAttribute = Error("BYN1045", "`[{0}]` is an input form; write `@{1}`");
    public static readonly DiagnosticDescriptor BlockComment = Error("BYN1047", "`/* */` is an input form; write `//` lines");
    public static readonly DiagnosticDescriptor CharInput = Error("BYN1048", "`char` is now `rune`");
    public static readonly DiagnosticDescriptor RequiresValue = Error("BYN1050", "`value` in `requires` is now `it`");
    public static readonly DiagnosticDescriptor ExceptInCase = Error("BYN1051", "`except` is not allowed in a `case` pattern");
    public static readonly DiagnosticDescriptor ExceptNotPattern = Error("BYN1052", "`except` takes a pattern, not a condition");
    public static readonly DiagnosticDescriptor OneLineDeclarations = Error("BYN1053", "`{0}` declares its cases on one line; write one per line");
    public static readonly DiagnosticDescriptor TryCoversMore = Error("BYN1054", "`try` covers `{0}`, which cannot fail");
    public static readonly DiagnosticDescriptor FatArrowFunctionType = Error("BYN1055", "`=>` in a type is an input form; write `->`");
    public static readonly DiagnosticDescriptor ReturnErrorInput = Error("BYN1056", "`return .{0}` returns an error; write `fail .{0}`");
    public static readonly DiagnosticDescriptor CastInput = Error("BYN1057", "`({0})x` is an input form; write `{0}(x)`");
    public static readonly DiagnosticDescriptor TrustInput = Error("BYN1062", "`trust(x, reason: …)` is now `x.trustUnchecked() because \"…\"`");
    public static readonly DiagnosticDescriptor RestInput = Error("BYN1058", "`{0}` is an input form; write `rest ReadOnlySpan<{1}> {2}`");
    public static readonly DiagnosticDescriptor StateLineInput = Error("BYN1059", "`state {0}` is an input form; declare the states in one `states {{ … }}` block");
    public static readonly DiagnosticDescriptor InterfaceGetInput = Error("BYN1060", "`{0} {1} {{ get }}` is an input form; write `{0} {1}`");
    public static readonly DiagnosticDescriptor ReceiverWithoutState = Error("BYN1061", "`this` without a state is the default receiver");
    public static readonly DiagnosticDescriptor RequiresOnFunction = Error("BYN1063", "`requires` on a function is an input form; write `expects`");
    public static readonly DiagnosticDescriptor PayloadNeedsName = Error("BYN1064", "payload `{0}` of `.{1}` needs a name");
    public static readonly DiagnosticDescriptor ClauseOrder = Error("BYN1065", "`{0}` must come before `{1}`; the order is `where`, `expects`, `becomes`, `fails`, `uses`");
    public static readonly DiagnosticDescriptor PrefixPointerInput = Error("BYN1066", "`{0}` is an input form; write `{1}`");
    public static readonly DiagnosticDescriptor PointerOutsideExtern = Error("BYN1067", "pointer types are allowed only in `extern` declarations");
    public static readonly DiagnosticDescriptor CompactFormatSpec = Error("BYN1068", "`{0}` is an input form; write `{1}`");
    public static readonly DiagnosticDescriptor StatementAttribute = Error("BYN1069", "attribute `{0}` is not allowed before a statement");

    // ── Semantics 2000–3999 ──────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor UndefinedName = Error("BYN2001", "`{0}` is not defined");
    public static readonly DiagnosticDescriptor UnknownType = Error("BYN2002", "unknown type `{0}`");
    public static readonly DiagnosticDescriptor UnknownMember = Error("BYN2003", "`{0}` is not a member of `{1}`");
    public static readonly DiagnosticDescriptor UnknownModule = Error("BYN2004", "no module `{0}`");
    public static readonly DiagnosticDescriptor NotVisible = Error("BYN2005", "`{0}` is not visible here");
    public static readonly DiagnosticDescriptor DuplicateDeclaration = Error("BYN2006", "`{0}` is declared twice");
    public static readonly DiagnosticDescriptor Shadowing = Error("BYN2007", "`{0}` is already a local here");
    public static readonly DiagnosticDescriptor TypeCasing = Error("BYN2008", "type name `{0}` must be PascalCase");
    public static readonly DiagnosticDescriptor ValueCasing = Error("BYN2009", "`{0}` must be camelCase");
    public static readonly DiagnosticDescriptor ImportCycle = Error("BYN2010", "import cycle: {0}");
    public static readonly DiagnosticDescriptor CircularAlias = Error("BYN2011", "type alias `{0}` refers to itself");
    public static readonly DiagnosticDescriptor CircularConstant = Error("BYN2012", "constant `{0}` depends on itself");
    public static readonly DiagnosticDescriptor DuplicateMain = Error("BYN2013", "the program has two entry points");
    public static readonly DiagnosticDescriptor NoMain = Error("BYN2014", "the program has no entry point");
    public static readonly DiagnosticDescriptor StatementOutsideEntry = Error("BYN2015", "top-level statements are allowed only in the entry module");
    public static readonly DiagnosticDescriptor InstanceMemberWithoutThis = Error("BYN2017", "`{0}` needs an instance");
    public static readonly DiagnosticDescriptor ThisOutsideMethod = Error("BYN2018", "`this` is only available inside instance methods");
    public static readonly DiagnosticDescriptor NotAValue = Error("BYN2019", "`{0}` is a {1}, not a value");
    public static readonly DiagnosticDescriptor UnknownLabel = Error("BYN2020", "no enclosing loop is labeled `{0}`");
    public static readonly DiagnosticDescriptor UnknownParameter = Error("BYN2021", "`{0}` has no parameter `{1}`");
    public static readonly DiagnosticDescriptor Unsupported = Error("BYN2022", "{0} is not supported in v0.1");
    public static readonly DiagnosticDescriptor WrongTypeArgumentCount = Error("BYN2023", "`{0}` takes {1} type argument(s), not {2}");
    public static readonly DiagnosticDescriptor NestedOptional = Error("BYN2024", "`{0}?` is already optional");
    public static readonly DiagnosticDescriptor VoidValue = Error("BYN2025", "`void` is not a value type");
    // BYN2026 is retired: a braceless body of `if`, `unless`, `while` or `for` is reported (BYN1018); a `defer` body may be a single statement, and only a declaration there is rejected (BYN1018), in design round 2. Never reuse it.
    public static readonly DiagnosticDescriptor DeclarationAsBody = Error("BYN2026", "a declaration cannot be the whole body of a statement");
    public static readonly DiagnosticDescriptor NullEquality = Error("BYN2027", "compare with `is null` / `is not null`, not `==`");
    public static readonly DiagnosticDescriptor SameTypeShadowing = Warning("BYN2028", "`{0}` shadows an earlier `{0}` of the same type `{1}`");
    public static readonly DiagnosticDescriptor NestedIt = Error("BYN2029", "`it` inside a lambda that already uses `it`; name one lambda");
    public static readonly DiagnosticDescriptor ToStringCall = Error("BYN2031", "Bayan has no `toString()`; display is `format()` (derived from `formatInto(ref StrBuf)`)");
    public static readonly DiagnosticDescriptor MissingInitializer = Error("BYN2101", "`{0} {1}` needs a value");
    public static readonly DiagnosticDescriptor UseBeforeAssign = Error("BYN2102", "`{0}` is used before it is assigned");
    public static readonly DiagnosticDescriptor OutNotAssigned = Error("BYN2103", "`out` parameter `{0}` is not assigned on every path");
    public static readonly DiagnosticDescriptor ImmutableAssignment = Error("BYN2104", "`{0}` is immutable");
    public static readonly DiagnosticDescriptor ReadonlyField = Error("BYN2105", "field `{0}` is readonly");
    public static readonly DiagnosticDescriptor MutateTemporary = Error("BYN2106", "cannot modify this value");
    public static readonly DiagnosticDescriptor GetterMutates = Error("BYN2107", "property `{0}` modifies the value");
    public static readonly DiagnosticDescriptor RequiredWithDefault = Error("BYN2108", "required field `{0}` has a default value");
    public static readonly DiagnosticDescriptor MissingFields = Error("BYN2109", "`{0}` needs a value for `{1}`");
    public static readonly DiagnosticDescriptor DuplicateFieldInit = Error("BYN2110", "field `{0}` is set twice");
    public static readonly DiagnosticDescriptor LiteralWithConstructor = Error("BYN2111", "`{0}` has a constructor; call it instead of using a struct literal");
    public static readonly DiagnosticDescriptor NoConstructor = Error("BYN2112", "`{0}` has no constructor");
    public static readonly DiagnosticDescriptor RecursiveStruct = Error("BYN2113", "struct `{0}` contains itself");
    public static readonly DiagnosticDescriptor NotAStruct = Error("BYN2114", "`{0}` is not a struct");
    public static readonly DiagnosticDescriptor MutatesWithoutRefThis = Error("BYN2115", "`{0}` modifies `this` but is not declared `ref this`");
    public static readonly DiagnosticDescriptor RefThisOnOperator = Error("BYN2116", "operators cannot take `ref this`");
    public static readonly DiagnosticDescriptor RefThisNotFirst = Error("BYN2117", "`ref this` must be the first parameter");
    public static readonly DiagnosticDescriptor StringIndexer = Error("BYN2201", "a `string` cannot be indexed");
    public static readonly DiagnosticDescriptor TypeMismatch = Error("BYN2202", "expected `{1}`, found `{0}`");
    public static readonly DiagnosticDescriptor LiteralOutOfRange = Error("BYN2203", "{0} does not fit in {1}");
    public static readonly DiagnosticDescriptor ConstantOverflow = Error("BYN2204", "constant arithmetic overflows `{0}`");
    public static readonly DiagnosticDescriptor DivisionByZero = Error("BYN2205", "division by zero");
    public static readonly DiagnosticDescriptor InvalidCast = Error("BYN2206", "cannot convert `{0}` to `{1}`");
    public static readonly DiagnosticDescriptor BadUnaryOperand = Error("BYN2207", "`{0}` cannot be applied to `{1}`");
    public static readonly DiagnosticDescriptor BadBinaryOperands = Error("BYN2208", "`{0}` cannot be applied to `{1}` and `{2}`");
    public static readonly DiagnosticDescriptor StringConcatenation = Error("BYN2209", "strings cannot be joined with `+`; use interpolation");
    public static readonly DiagnosticDescriptor CoalesceNotOptional = Error("BYN2210", "`??` needs an optional value on the left, found `{0}`");
    public static readonly DiagnosticDescriptor MemberOfOptional = Error("BYN2211", "`{0}` is not a member of `{1}`; the value may be null");
    public static readonly DiagnosticDescriptor NotIndexable = Error("BYN2212", "`{0}` cannot be indexed");
    public static readonly DiagnosticDescriptor IndexOutOfRange = Error("BYN2213", "index {0} is out of range for `{1}`");
    public static readonly DiagnosticDescriptor FromEndOutsideIndex = Error("BYN2214", "`^` is only allowed inside an index or slice");
    public static readonly DiagnosticDescriptor OpenRange = Error("BYN2215", "a range needs both ends here");
    public static readonly DiagnosticDescriptor BranchTypeMismatch = Error("BYN2216", "branches have different types: `{0}` and `{1}`");
    public static readonly DiagnosticDescriptor CannotInferType = Error("BYN2217", "cannot infer the type of `{0}`");
    public static readonly DiagnosticDescriptor NoTargetType = Error("BYN2218", "`{0}` needs a known type here");
    public static readonly DiagnosticDescriptor NotConstant = Error("BYN2219", "{0} must be a compile-time constant");
    public static readonly DiagnosticDescriptor NotCallable = Error("BYN2220", "`{0}` cannot be called");
    public static readonly DiagnosticDescriptor NotIterable = Error("BYN2221", "`{0}` cannot be iterated");
    public static readonly DiagnosticDescriptor ArrayLengthMismatch = Error("BYN2222", "`{0}` needs exactly its length in elements, found {1}");
    public static readonly DiagnosticDescriptor TupleNameMismatch = Error("BYN2223", "tuple element `{0}` is named `{1}` in the target type");
    public static readonly DiagnosticDescriptor InterpolationNotAllowed = Error("BYN2224", "an interpolated string must be passed directly to an output");
    public static readonly DiagnosticDescriptor NotFormattable = Error("BYN2225", "`{0}` cannot be formatted");
    public static readonly DiagnosticDescriptor NotDisposable = Error("BYN2226", "`using` needs a value that owns memory, not `{0}`");
    public static readonly DiagnosticDescriptor AmbiguousVariant = Error("BYN2227", "`.{0}` is ambiguous; it exists in `{1}`");
    public static readonly DiagnosticDescriptor UnknownVariant = Error("BYN2228", "`{0}` is not a variant of `{1}`");
    public static readonly DiagnosticDescriptor InvalidConstArray = Error("BYN2229", "module-level `const` array `{0}` must hold literal numbers, `bool`s, `rune`s or `string`s");
    public static readonly DiagnosticDescriptor RenamedMember = Error("BYN2230", "`{0}` is now `{1}`");
    public static readonly DiagnosticDescriptor WhileLetNotOptional = Error("BYN2234", "`while let` needs a `T?` value, got `{0}`");
    public static readonly DiagnosticDescriptor SpreadNotSequence = Error("BYN2235", "`..{0}` spreads a `{1}`, which is not a sequence");
    public static readonly DiagnosticDescriptor MapLiteralMixed = Error("BYN2236", "a map literal needs `key: value` in every element");
    public static readonly DiagnosticDescriptor MissingArgument = Error("BYN2301", "missing argument `{0}` for `{1}`");
    public static readonly DiagnosticDescriptor TooManyArguments = Error("BYN2302", "`{0}` takes {1} argument(s)");
    public static readonly DiagnosticDescriptor DuplicateArgument = Error("BYN2303", "`{0}` is passed twice");
    public static readonly DiagnosticDescriptor PositionalAfterNamed = Error("BYN2304", "a positional argument cannot follow a named one");
    public static readonly DiagnosticDescriptor ArgumentMustBeNamed = Error("BYN2305", "argument for `{0}` must be named");
    public static readonly DiagnosticDescriptor ArgumentModifierMismatch = Error("BYN2306", "argument for `{0}` must be passed as {1}");
    // BYN2307 is retired (design round 3 §15): particles replace the same-type naming rule, so a swapped pair is a particle mismatch (BYN5005). Never reuse it.
    public static readonly DiagnosticDescriptor RetiredArgumentsSwapped = Error("BYN2307", "retired: particles replace the same-type naming rule");
    public static readonly DiagnosticDescriptor SpanParameterNeverWritten = Warning("BYN2310", "`{0}` is never written through; use `ReadOnlySpan<{1}>`");
    public static readonly DiagnosticDescriptor NeverReassignedVar = Warning("BYN2311", "`{0}` is never reassigned; declare it with `let`");
    public static readonly DiagnosticDescriptor RequiresNotBool = Error("BYN2320", "a `requires` clause must be `bool`, got `{0}`");
    public static readonly DiagnosticDescriptor RequiresViolated = Error("BYN2321", "`{0}` violates `requires {1}` of `{2}`");
    // BYN2322 is retired: `requires` on fields and distinct types is checked at construction (round 3 step 5.3b, revision 0006). Never reuse it.
    public static readonly DiagnosticDescriptor RetiredRequiresNotHere = Error("BYN2322", "retired: `requires` is checked at construction");
    public static readonly DiagnosticDescriptor MissingReturn = Error("BYN2401", "`{0}` must return `{1}` on every path");
    public static readonly DiagnosticDescriptor MissingReturnValue = Error("BYN2402", "this function returns `{0}`; `return` needs a value");
    public static readonly DiagnosticDescriptor ReturnValueInVoid = Error("BYN2403", "`{0}` returns nothing, but `return` has a value");
    public static readonly DiagnosticDescriptor NeverReturns = Error("BYN2404", "`{0}` returns `never` but can reach its end");
    public static readonly DiagnosticDescriptor UnreachableCode = Warning("BYN2405", "unreachable code");
    public static readonly DiagnosticDescriptor JumpOutsideLoop = Error("BYN2406", "`{0}` outside a loop");
    public static readonly DiagnosticDescriptor ContinueSwitch = Error("BYN2407", "`continue {0}` refers to a `switch`, not a loop");
    public static readonly DiagnosticDescriptor LabelNotOnLoop = Error("BYN2408", "label `{0}` must be on a loop or `switch`");
    public static readonly DiagnosticDescriptor DuplicateDefault = Error("BYN2409", "`switch` has more than one `default` section");
    public static readonly DiagnosticDescriptor NonExhaustive = Error("BYN2410", "`switch` does not handle every `{0}`");
    public static readonly DiagnosticDescriptor PatternMismatch = Error("BYN2411", "{0} cannot match `{1}`");
    public static readonly DiagnosticDescriptor TryInDefer = Error("BYN2601", "`try` is not allowed in `defer`");
    public static readonly DiagnosticDescriptor UnhandledFailure = Error("BYN2602", "`{0}` can fail; handle the error or pass it on");
    public static readonly DiagnosticDescriptor UndeclaredError = Error("BYN2603", "`{0}` is not in the `fails` clause of `{1}`");
    public static readonly DiagnosticDescriptor PropagateFromInfallible = Error("BYN2604", "`{0}` cannot fail, so it cannot pass on `{1}`");
    public static readonly DiagnosticDescriptor NothingToHandle = Error("BYN2605", "`{0}` needs a call that can fail");
    public static readonly DiagnosticDescriptor ErrdeferWithoutFails = Error("BYN2606", "`defer failing` in a function that cannot fail");
    public static readonly DiagnosticDescriptor PublicInferredFails = Error("BYN2607", "public function `{0}` must list its errors");
    public static readonly DiagnosticDescriptor NotAnErrorSet = Error("BYN2608", "`{0}` is not an error enum");
    public static readonly DiagnosticDescriptor EmptyErrorSet = Error("BYN2609", "error enum `{0}` has no variants");
    public static readonly DiagnosticDescriptor JumpOutOfDefer = Error("BYN2610", "`{0}` cannot leave a `defer` or `defer failing` body");
    public static readonly DiagnosticDescriptor CatchBlockCompletes = Error("BYN2611", "this `catch` block can complete normally; end it with `return`, `break`, `continue` or a panic");
    public static readonly DiagnosticDescriptor PanicHandlerRestricted = Error("BYN2612", "{0} is not allowed in a panic handler");
    public static readonly DiagnosticDescriptor PanicHandlerReads = Error("BYN2613", "a panic handler reads only `let` bindings and parameters");
    public static readonly DiagnosticDescriptor OnPanicPlacement = Error("BYN2614", "`Panic.handler` may be set only as the first statement of `main`");
    public static readonly DiagnosticDescriptor PanicHandlerReadsMoved = Error("BYN2615", "a panic handler reads `{0}`, which is moved after the handler is registered");
    public static readonly DiagnosticDescriptor UnknownUnionVariant = Error("BYN2700", "`{0}` is not a variant of union `{1}`");
    public static readonly DiagnosticDescriptor DuplicateUnionVariant = Error("BYN2701", "union `{0}` already has a variant `{1}`");
    public static readonly DiagnosticDescriptor EmptyUnion = Error("BYN2702", "union `{0}` has no variants");
    public static readonly DiagnosticDescriptor VariantPatternArity = Error("BYN2703", "variant `{0}` has {1} payload field(s), but the pattern lists {2}");
    public static readonly DiagnosticDescriptor PayloadNeedsPattern = Error("BYN2704", "the payload of a `{0}` is read with a pattern, not `.{1}`");
    public static readonly DiagnosticDescriptor VariantNeedsPayload = Error("BYN2705", "variant `{0}` carries a payload");
    public static readonly DiagnosticDescriptor VariantHasNoPayload = Error("BYN2706", "variant `{0}` has no payload");
    public static readonly DiagnosticDescriptor RecursiveUnion = Error("BYN2707", "union `{0}` contains itself");
    public static readonly DiagnosticDescriptor BorrowPayload = Error("BYN2708", "a union payload cannot hold a borrowed `{0}`");
    public static readonly DiagnosticDescriptor UnionValueNotInteger = Error("BYN2709", "a case value must be an integer constant");
    public static readonly DiagnosticDescriptor UnionValueOnPayload = Error("BYN2710", "`{0}` carries data and cannot have a value");
    public static readonly DiagnosticDescriptor DuplicateUnionValue = Error("BYN2711", "`{0}` and `{1}` share the value {2}");
    public static readonly DiagnosticDescriptor WildcardOnLocalUnion = Warning("BYN2712", "`_` hides future cases of `{0}`, declared in this package");
    public static readonly DiagnosticDescriptor UnknownEnumMember = Error("BYN2720", "`{0}` is not a member of `{1}`");
    public static readonly DiagnosticDescriptor DuplicateEnumMember = Error("BYN2721", "enum `{0}` already has a member `{1}`");
    public static readonly DiagnosticDescriptor EmptyEnum = Error("BYN2722", "enum `{0}` has no members");
    public static readonly DiagnosticDescriptor EnumArgumentNotConstant = Error("BYN2723", "argument `{0}` of `{1}` must be a compile-time constant");
    public static readonly DiagnosticDescriptor InvalidEnumParameter = Error("BYN2724", "enum parameter `{0}` must be a number, `bool`, `rune`, `string` or a plain enum");
    public static readonly DiagnosticDescriptor CannotInferTypeArgument = Error("BYN2740", "cannot infer type argument `{0}` of `{1}`");
    public static readonly DiagnosticDescriptor ConflictingTypeArgument = Error("BYN2741", "type argument `{0}` of `{1}` is inferred as both `{2}` and `{3}`");
    public static readonly DiagnosticDescriptor GenericArgumentCount = Error("BYN2742", "`{0}` takes {1} type argument(s), not {2}");
    public static readonly DiagnosticDescriptor TypeParameterOperator = Error("BYN2743", "`{0}` cannot be applied to a value of type parameter `{1}`");
    public static readonly DiagnosticDescriptor TypeParameterMember = Error("BYN2744", "`{0}` is not a member of type parameter `{1}`");
    public static readonly DiagnosticDescriptor InvalidMapKey = Error("BYN2760", "`{0}` cannot be a `Map` key");
    public static readonly DiagnosticDescriptor RecursiveByValue = Error("BYN2761", "`{0}` contains itself by value: {1}");
    // BYN2762 (a `Map` or `StrBuf` cannot be copied) is retired: replaced by BYN2780 in spec 002. Never reuse it.
    public static readonly DiagnosticDescriptor SharedStorageCopy = Error("BYN2762", "a `{0}` cannot be copied");
    public static readonly DiagnosticDescriptor UnknownAttribute = Error("BYN2770", "unknown attribute `{0}`");
    public static readonly DiagnosticDescriptor InvalidMainSignature = Error("BYN2771", "`main` must return `void` or `i32` and take nothing or `ReadOnlySpan<string> args`");
    public static readonly DiagnosticDescriptor UntrustedSink = Error("BYN2786", "`{0}` cannot be passed to `{1}`: it is `Untrusted`");
    public static readonly DiagnosticDescriptor TrustReason = Error("BYN2787", "`trust` needs a reason: a non-empty string literal");
    public static readonly DiagnosticDescriptor TakeUnsupported = Error("BYN2788", "`take()` needs a `List`, `Map`, `StrBuf` or an optional of a unique type, not `{0}`");
    public static readonly DiagnosticDescriptor SwitchMoveSubject = Error("BYN2789", "`switch move …` needs a local or a `move` parameter, not {0}");
    public static readonly DiagnosticDescriptor OwningPatternOverlap = Error("BYN2790", "pattern `{0}` binds the same storage more than once in `switch move …`");
    public static readonly DiagnosticDescriptor OwningBindingMutatedInGuard = Error("BYN2791", "`{0}` is mutated in the guard of `switch move …`; a guard that fails gives the value back");
    public static readonly DiagnosticDescriptor InvalidExitCode = Error("BYN2772", "`@exitCode` needs one integer constant from 1 to 255");
    public static readonly DiagnosticDescriptor AssertNotBool = Error("BYN2775", "`assert` needs a `bool`, got `{0}`");
    public static readonly DiagnosticDescriptor CasesArity = Error("BYN2773", "`@cases` gives {0} values but the test takes {1} parameters");
    public static readonly DiagnosticDescriptor CasesNotConstant = Error("BYN2774", "each `@cases` value must be a constant");
    public static readonly DiagnosticDescriptor ImplicitCopy = Error("BYN2780", "implicit copy of a unique value: {0}");
    public static readonly DiagnosticDescriptor UseAfterMove = Error("BYN2781", "`{0}` is used after it was moved");
    public static readonly DiagnosticDescriptor CannotMove = Error("BYN2782", "cannot move out of {0}");
    public static readonly DiagnosticDescriptor MutateBorrow = Error("BYN2783", "`{0}` borrows from what it was bound to and cannot be changed or moved");
    public static readonly DiagnosticDescriptor CopyUniqueElement = Error("BYN2784", "a `{0}` holding `{1}` cannot be copied: the copy would share its storage");
    public static readonly DiagnosticDescriptor MapSlotNoDefault = Error("BYN2785", "`slot` needs a default value, and `{0}` has none");
    public static readonly DiagnosticDescriptor InterfaceName = Error("BYN2800", "`{0}` must start with `I` and an uppercase letter");
    public static readonly DiagnosticDescriptor MissingInterfaceMember = Error("BYN2801", "`{0}` does not provide `{1}` required by `{2}`");
    public static readonly DiagnosticDescriptor InterfaceMemberMismatch = Error("BYN2802", "`{0}.{1}` is `{2}` but `{3}` requires `{4}`");
    public static readonly DiagnosticDescriptor MissingAssociatedType = Error("BYN2803", "`{0}` does not bind `type {1}` of `{2}`");
    public static readonly DiagnosticDescriptor NotAnInterface = Error("BYN2804", "`{0}` is not an interface");
    public static readonly DiagnosticDescriptor DuplicateConformance = Error("BYN2805", "`{0}` lists `{1}` twice");
    public static readonly DiagnosticDescriptor ConstraintMember = Error("BYN2806", "`{0}` has no member `{1}` in its constraints ({2})");
    public static readonly DiagnosticDescriptor ConstraintNotSatisfied = Error("BYN2807", "`{0}` does not conform to `{1}`: missing `{2}`");
    public static readonly DiagnosticDescriptor NotDynSafe = Error("BYN2808", "`{0}` cannot be used with `dyn`: `{1}` {2}");
    public static readonly DiagnosticDescriptor DynByValue = Error("BYN2809", "`dyn {0}` is not supported yet");
    public static readonly DiagnosticDescriptor StaticAbstract = Error("BYN2810", "`static` interface members are not supported yet");
    public static readonly DiagnosticDescriptor ExtensionConformance = Error("BYN2811", "`extension {0} : {1}` is not supported yet");
    public static readonly DiagnosticDescriptor WhereUnknownParameter = Error("BYN2812", "`where` names `{0}`, which is not a type parameter");
    public static readonly DiagnosticDescriptor DynReadonlyMutation = Error("BYN2813", "`{0}` takes `ref this`, but the receiver is `dyn ref readonly {1}`");
    public static readonly DiagnosticDescriptor RefThisMismatch = Error("BYN2814", "`{0}.{1}` must match `{2}.{1}`, which {3} `ref this`");
    public static readonly DiagnosticDescriptor NonStructConformance = Error("BYN2815", "`{0}` is {1}; only structs can conform yet");
    public static readonly DiagnosticDescriptor InterfaceAsType = Error("BYN2816", "`{0}` is an interface, not a type");
    public static readonly DiagnosticDescriptor LambdaArity = Error("BYN2820", "this lambda takes {0} parameter(s) but `{1}` takes {2}");
    public static readonly DiagnosticDescriptor CannotCapture = Error("BYN2821", "`{0}` cannot be captured: {1}");
    public static readonly DiagnosticDescriptor FunctionTypeNotHere = Error("BYN2822", "a function type is not supported as {0} yet");
    public static readonly DiagnosticDescriptor MemberNotLambda = Error("BYN2823", "`.{0}` is a member, not a lambda");
    public static readonly DiagnosticDescriptor NotAFunctionValue = Error("BYN2824", "`{0}` cannot be used as a value: {1}");
    public static readonly DiagnosticDescriptor HashMapNotSupported = Error("BYN2830", "`HashMap` is not supported yet: `Map` hashes the same way and keeps insertion order");
    public static readonly DiagnosticDescriptor KeyPartNotHashable = Error("BYN2831", "`{0}` cannot be a `Map` key: {1} `{2}` is a `{3}`");
    public static readonly DiagnosticDescriptor StringNotSymbol = Error("BYN2832", "a `string` value is not a `Symbol`; only a string literal converts");
    public static readonly DiagnosticDescriptor BorrowField = Error("BYN3001", "a struct field cannot hold a borrowed `{0}`");
    public static readonly DiagnosticDescriptor BorrowOutlivesSource = Error("BYN3002", "{0} does not live as long as `{1}`, which borrows it");
    public static readonly DiagnosticDescriptor BorrowReturned = Error("BYN3003", "cannot return a borrow of {0}, which is freed when `{1}` returns");
    public static readonly DiagnosticDescriptor ModifiedWhileBorrowed = Error("BYN3004", "`{0}` is modified while {1} still borrows it");
    public static readonly DiagnosticDescriptor RefAndValue = Error("BYN3005", "`{0}` is passed by `ref` and again in the same call");
    public static readonly DiagnosticDescriptor MovedWhileBorrowed = Error("BYN3006", "`{0}` is moved while {1} still borrows it");
    public static readonly DiagnosticDescriptor UnusedValue = Error("BYN3201", "the value of this `{0}` is unused");
    public static readonly DiagnosticDescriptor DuplicateErrorCase = Error("BYN3202", "`{0}` and `{1}` both have a case `{2}`");
    public static readonly DiagnosticDescriptor BareLiteralForDistinct = Error("BYN3204", "`{0}` is not a `{1}`");
    public static readonly DiagnosticDescriptor RuleTargetNotField = Error("BYN3206", "`at .{0}` names no field of `{1}`");
    public static readonly DiagnosticDescriptor LiteralNeedsTry = Error("BYN3207", "a literal of `{0}` can fail its `requires`; write `try {0} {{ … }}`");
    public static readonly DiagnosticDescriptor CheckedFieldAssignment = Error("BYN3208", "fields of `{0}` cannot be assigned one at a time; its `requires` are checked at construction");
    public static readonly DiagnosticDescriptor UniqueConst = Error("BYN3209", "a `unique` type cannot be `const`");
    // BYN3213 is reserved: v0.1 overloads differ by particle set only. Its help, once reported: "change a parameter type or particle so the overloads cannot overlap".
    public static readonly DiagnosticDescriptor OverlappingOverloads = Error("BYN3213", "`{0}` and `{1}` both accept `{2}`");
    public static readonly DiagnosticDescriptor RestEscapes = Error("BYN3214", "`{0}` is borrowed for the call and cannot be stored");
    // BYN3215 is reserved: v0.1 has no references to overloaded names. Its help, once reported: "annotate it: `let f: A -> B = {0}`".
    public static readonly DiagnosticDescriptor AmbiguousOverloadReference = Error("BYN3215", "`{0}` is overloaded; give the reference a function type");
    public static readonly DiagnosticDescriptor FieldWriteOutside = Error("BYN3216", "`{0}.{1}` can be written only by `{0}`'s own methods or with `with`: `{0}` {2}");
    // BYN3217 is reserved: v0.1 has no fibers. Its help, once reported: "copy the data first, or spawn the fiber in a scope that ends before `{1}`".
    public static readonly DiagnosticDescriptor BorrowCrossesFiber = Error("BYN3217", "`{0}` holds a reference into `{1}` and cannot be passed to a fiber that may outlive it");
    public static readonly DiagnosticDescriptor RequiresMixesFields = Error("BYN3218", "this `requires` reads `it` and other fields");
    public static readonly DiagnosticDescriptor UnknownMutationOperator = Error("BYN3219", "`{0}` is not a mutation operator");

    // ── Manifest (`package.byn`) 3100–3199 ───────────────────────────────────────────
    public static readonly DiagnosticDescriptor ManifestNotData = Error("BYN3100", "`package.byn` is data only: expected {0}");
    public static readonly DiagnosticDescriptor ManifestUnknownField = Error("BYN3101", "`{0}` is not a field of `{1}`");
    public static readonly DiagnosticDescriptor ManifestInvalidValue = Error("BYN3102", "`{0}` must be {1}");
    public static readonly DiagnosticDescriptor ManifestMissingField = Error("BYN3103", "`package.byn` has no `{0}`");
    public static readonly DiagnosticDescriptor ManifestEquals = Error("BYN3104", "`package.byn` uses `field: value`");

    // ── Backend 4000–4999 ────────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor CCompilerRejected = Error("BYN4001", "internal compiler error: the C compiler rejected the generated C; this is a bug in the Bayan compiler");
    // ── Grammar (naming patterns, spec §5.2) 5000–5999: errors 50xx, lints 51xx ────
    public static readonly DiagnosticDescriptor ParticipleChangesThis = Error("BYN5001", "`{0}` changes `this`; name it `{1}`");
    public static readonly DiagnosticDescriptor StateMemberMutates = Error("BYN5002", "`{0}` reads as state but modifies `this`");
    public static readonly DiagnosticDescriptor TryWrongReturn = Error("BYN5003", "`try{0}` must return `{1}?` or `bool`; it is derived from `{2}() fails`");
    public static readonly DiagnosticDescriptor ParticleRequired = Error("BYN5004", "`{0}` has {1} unlabeled parameters; give each but one a particle");
    public static readonly DiagnosticDescriptor ArgumentNeedsParticle = Error("BYN5005", "`{0}` is passed without its particle `{1}:`");
    public static readonly DiagnosticDescriptor ParameterNeedsParticle = Error("BYN5006", "`{0}` is a required parameter besides the object and needs a particle");
    public static readonly DiagnosticDescriptor LogLevelAsVerb = Error("BYN5007", "`Log.{0}` names a level, not the act; write `log(…, as: .{0})`");
    public static readonly DiagnosticDescriptor UnknownParticle = Error("BYN5008", "`{0}` is not a particle");
    public static readonly DiagnosticDescriptor ParticleAsMemberName = Error("BYN5009", "`{0}` is a particle; name the member with a verb or a noun");
    /// <summary>BYN5009's second message form, for an instance <c>toX</c> or <c>asX</c> member; registered once, as <see cref="ParticleAsMemberName"/>.</summary>
    public static readonly DiagnosticDescriptor ConversionMember = new("BYN5009", Severity.Error, "`{0}` spells a conversion with `{1}`; write `{2}`");
    public static readonly DiagnosticDescriptor ExpectsSingleParameter = Warning("BYN5011", "this `expects` reads only `{0}`; a type with `requires` checks it once");
    public static readonly DiagnosticDescriptor RepeatedParticle = Error("BYN5013", "`{0}` uses the particle `{1}` twice");
    public static readonly DiagnosticDescriptor UnknownWord = Warning("BYN5014", "`{0}` is not a known verb or noun");
    public static readonly DiagnosticDescriptor NounOnMutator = Warning("BYN5101", "`{0}` is a noun on a `ref this` member; name the action");
    public static readonly DiagnosticDescriptor BoolWithoutPrefix = Warning("BYN5102", "`{0}` is a `bool` state; prefix with `is`, `has` or `can`");
    public static readonly DiagnosticDescriptor LineJoinIndent = Warning("BYN5103", "a line starting with `{0}` joins the statement above; indent it deeper");

    // ── Effects 6000–6999 ────────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor NoallocRemoved = Error("BYN6001", "`{0}` is removed; a function without `uses` is already pure");
    public static readonly DiagnosticDescriptor EffectNotDeclared = Error("BYN6002", "`{0}` uses `{1}` but does not declare it");
    public static readonly DiagnosticDescriptor UnknownEffect = Error("BYN6003", "`{0}` is not an effect");
    public static readonly DiagnosticDescriptor CapabilityExceeded = Error("BYN6004", "`{0}` uses `{1}`, which package `{2}` does not declare in `capabilities`");
    public static readonly DiagnosticDescriptor EffectBoundExceeded = Error("BYN6005", "`{0}` uses `{1}`, beyond the bound `{2}` declared by `{3}`");
    public static readonly DiagnosticDescriptor ContextBlockInput = Error("BYN6006", "`context (…) {{ }}` is now `provide … {{ }}`");
    public static readonly DiagnosticDescriptor UnknownHandler = Error("BYN6007", "`{0}` is not a handler; `provide` takes `alloc`, `net`, `fs`, `clock`, `random`, `env`, `process`, `console` or `log`");

    // ── Typestate 7000–7999 ──────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor WrongState = Error("BYN7001", "`{0}` needs `{1}` to be `.{2}`, but it is `{3}`");
    public static readonly DiagnosticDescriptor PayloadNotInState = Error("BYN7002", "`{0}` is a payload of `.{1}`; `{2}` is not known to be `.{1}`");
    public static readonly DiagnosticDescriptor BecomeUndeclared = Error("BYN7003", "`become` needs `becomes .{0}` on `{1}`");
    /// <summary>BYN7003's second message form, for a signature that declares another target; registered once, as <see cref="BecomeUndeclared"/>.</summary>
    public static readonly DiagnosticDescriptor BecomeOtherState = new("BYN7003", Severity.Error, "`become .{0}` is not declared; `{1}` becomes `.{2}`");
    /// <summary>BYN7003's third message form, for a <c>becomes</c> call on <c>this</c> (revision 0010); registered once, as <see cref="BecomeUndeclared"/>.</summary>
    public static readonly DiagnosticDescriptor BecomesCallUndeclared = new("BYN7003", Severity.Error, "`{0}` calls `{1}`, which changes the state; declare `becomes` on `{0}`");
    public static readonly DiagnosticDescriptor StateSwitchNotExhaustive = Error("BYN7004", "the switch does not handle state `.{0}`");
    public static readonly DiagnosticDescriptor StateLiteralNeedsState = Error("BYN7005", "`{0}` has states; a literal must name one: `{0}.{1} {{ … }}`");
    public static readonly DiagnosticDescriptor SingleStateInline = Error("BYN7006", "`{0} is .{1}` is an input form; write `{2}`");
    public static readonly DiagnosticDescriptor UnknownState = Error("BYN7007", "`{0}` has no state `.{1}`");
    public static readonly DiagnosticDescriptor StateShadowsStatic = Error("BYN7008", "state `.{0}` has the same name as static member `{1}.{0}`");
    public static readonly DiagnosticDescriptor MissingBecome = Error("BYN7009", "`{0}` declares `becomes .{1}` but returns here without `become`");
    public static readonly DiagnosticDescriptor BecomesNeedsRef = Error("BYN7010", "`{0}` declares `becomes .{1}` but takes `{2}`; a transition needs `ref this`");
    public static readonly DiagnosticDescriptor BecomesOnRefParameter = Error("BYN7011", "`{0}` changes the state of `{1}`, a `ref` parameter; a state changes in place only through `ref this`");
    public static readonly DiagnosticDescriptor AssignmentChangesState = Error("BYN7012", "`{0}` would change state here; a state changes only through `become`");

    // ── Security 8000–8999 ───────────────────────────────────────────────────────────
    public static readonly DiagnosticDescriptor MissingBecause = Error("BYN8001", "`{0}` bypasses a safety rule and needs `because \"…\"`");
    public static readonly DiagnosticDescriptor ExpectsReadsUntrusted = Error("BYN8006", "`expects` reads `{0}`, which is `Untrusted`; a violation would trap on outside data");

    // Reserved: round 4 (revisions 0016, 0017). Not reported yet: no descriptor until implemented, so the self-hosted
    // tables stay equal to `All`. Each line is `BYN#### Name`, a note, and the round-4 target; DiagnosticCodesTests reads it.
    // BYN1070 NegatedIsInput: `!(x is .s)`; round 4's draft says BYN1069, which spec 013 gives StatementAttribute. // target: 19xx input form
    // BYN1071 SwitchExpressionNeedsElse: subjectless `switch`. // target: 41xx switch
    // BYN2825 BorrowedClosureStored: closure escape rule. // target: 3xxx borrows
    // BYN7013 FinalStateNotReached: `final` states; round 4's draft says BYN7010, which is BecomesNeedsRef. // target: 7xxx typestate
    // End reserved: round 4.
}
