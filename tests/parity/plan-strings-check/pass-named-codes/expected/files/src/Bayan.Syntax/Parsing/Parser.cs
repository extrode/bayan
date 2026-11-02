class Parser {
    void A() { Report(KeywordOrder, help: $"put the kind first: `{canonical}`"); }
    void B() { Report(TypeNameOrder, help: "put the type before the name; declarations are type-first"); }
    void C() { Report(OneLine, help: "write each case on its own line"); }
    void D() { Report(ContextInput, help: "write `provide`"); }
}
