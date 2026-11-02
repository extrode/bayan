class DiagnosticCodes {
    public static readonly DiagnosticDescriptor KeywordOrder = Error("BYN1044", "`{0}` is an input form; write `{1}`");
    public static readonly DiagnosticDescriptor TypeNameOrder = Error("BYN1043", "`{0}` is an input form; write `{1}`");
    public static readonly DiagnosticDescriptor OneLine = Error("BYN1053", "`{0}` declares its cases on one line");
    public static readonly DiagnosticDescriptor ContextInput = Error("BYN6006", "`context (…) {{ }}` is now `provide … {{ }}`");
}
