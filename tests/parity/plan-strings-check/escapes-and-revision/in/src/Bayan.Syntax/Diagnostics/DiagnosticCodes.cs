class DiagnosticCodes {
    public static readonly DiagnosticDescriptor StateLiteralNeedsState = Error("BYN7005", "`{0}` has states; write `{0}.{1} {{ … }}`");
    public static readonly DiagnosticDescriptor StateShadowsStatic = Error("BYN7008", "state `.{0}` shadows `{1}.{0}`");
}
