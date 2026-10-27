using Bayan.Semantics;
using Bayan.Syntax.Diagnostics;
using Bayan.Syntax.Text;

namespace Bayan.Tests;

/// <summary>
/// T12: <c>selfhost/semantics/declare.byn</c> against the C# <c>Compilation</c>.
/// </summary>
public sealed class SelfhostSemanticsT12Tests
{
    private static string MainSource() => """
        import lexer
        import parser

        i32 main() {
            var c = Ctx {
                files: List<SourceFile>(),
                modules: List<Mod>(),
                diagnostics: List<Diag>(),
                bags: List<DiagBag>(),
            }

            // ── 1. loc & accessOf ──────────────────────────────────────────
            let l = loc(0, at: TextSpan { start: 10, length: 5 })
            let pub = accessOf(1)
            let priv = accessOf(2)
            let none = accessOf(0)
            print("== smoke")
            print($"file: {l.file} span: {l.span.start} pub: {pub == .publicKeyword} priv: {priv == .privateKeyword} module: {none == .module}")

            // ── Setup AST Files 0 and 1 ──────────────────────────────────
            let src0 = "struct UniqueType { }\nvoid uniqueFn() { }\nconst uniqueConst = 100\nvoid fPos(i32 a) { }\nvoid fPos(i32 a, i32 b) { }\nvoid fPart(from i32 a) { }\nvoid fPart(to i32 a) { }\nvoid fRest(rest ReadOnlySpan<i32> args) { }\nvoid ovFn(i32 a) { }\nvoid ovFn(i32 b) { }\nunion error MyError {\n    okCase\n    payCase(string msg, i32 code)\n    okCase\n    @cases badAttr\n    @exitCode(1) goodExit\n    @exitCode(2) @exitCode(3) dupExit\n    @unknownAttr unknownCase\n    badFnPay(() -> void cb)\n    dupPay(i32 a, string a)\n}\nunion error EmptyError { }\nunion error BaseA {\n    caseA\n    sharedCase\n}\nunion error BaseB {\n    caseB\n    sharedCase\n}\nunion error CompSet = BaseA, BaseB\nenum SampleEnum(ref i32 badRef, i32 defVal = 1, UniqueType badType, i32 name, i32 val1, i32 val1) {\n    first\n    second\n    first\n}\nenum EmptyEnum { }\nunion SampleUnion {\n    uCaseA\n    uValDec = 10\n    uValNeg = -5\n    uValHex = 0x20\n    uDupVal = 10\n    uPayVal(i32 x) = 1\n    uBadVal = \"text\"\n    uPay(i32 p1, string p1, () -> void cb)\n    uCaseA\n}\nunion EmptyUnion { }\ninterface IBase {\n    type Item\n}\nstruct SampleStruct : IBase, IBase {\n    static i32 badStatic\n    required i32 reqDef = 5\n    i32 fld1\n    i32 fld1\n    i32 propVal => 42\n    i32 propVal => 43\n    (() -> void) fnProp => () => { }\n    void m1(i32 x) { }\n    void m1(i32 y) { }\n    void mGeneric<T>() { }\n    type Item = i32\n    type BadAssoc = bool\n    SampleStruct() { }\n    SampleStruct(i32 x) { }\n}\n"

            var bytes0 = List<u8>()
            for b in src0.bytes { bytes0.append(b) }
            var copy0 = List<u8>()
            for b in src0.bytes { copy0.append(b) }
            let source0 = lexer.SourceText.create("test.byn", from: move bytes0)
            var p0 = parser.Parser.create(lexer.lex(move source0, by: lexer.Keywords.build()))
            let unit0 = parser.parseCompilationUnit(ref p0)
            let sf0 = SourceFile {
                path: "test.byn",
                displayPath: "test.byn",
                text: move copy0,
                unit: move unit0,
                module: 0,
                isEntry: true,
                isTestFile: false,
                imports: Map<string, ModId>(),
            }
            c.files.append(move sf0)

            let src1 = "struct OtherStruct { }\n"
            var bytes1 = List<u8>()
            for b in src1.bytes { bytes1.append(b) }
            var copy1 = List<u8>()
            for b in src1.bytes { copy1.append(b) }
            let source1 = lexer.SourceText.create("other.byn", from: move bytes1)
            var p1 = parser.Parser.create(lexer.lex(move source1, by: lexer.Keywords.build()))
            let unit1 = parser.parseCompilationUnit(ref p1)
            let sf1 = SourceFile {
                path: "other.byn",
                displayPath: "other.byn",
                text: move copy1,
                unit: move unit1,
                module: 0,
                isEntry: false,
                isTestFile: false,
                imports: Map<string, ModId>(),
            }
            c.files.append(move sf1)

            var modFiles = List<FileId>()
            modFiles.append(0)
            modFiles.append(1)
            c.modules.append(Mod {
                name: "test",
                files: move modFiles,
                directory: ".",
                cName: "test",
            })

            // ── 2. checkUnique ─────────────────────────────────────────────
            var spanUniqueType = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[0] {
                case .structDecl(let n): spanUniqueType = n.value.name
                default: break
            }
            var spanUniqueFn = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[1] {
                case .functionDecl(let n): spanUniqueFn = n.value.name
                default: break
            }
            var spanUniqueConst = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[2] {
                case .globalStatement(let n):
                    switch n.value.statement {
                        case .localDecl(let ld): spanUniqueConst = ld.value.name
                        default: break
                    }
                default: break
            }

            let u0 = checkUnique(ref c, from: 0, at: spanUniqueType)
            let s0 = newStruct(ref c, by: StructSymbol { name: "UniqueType", nameSpan: spanUniqueType, span: spanUniqueType, module: 0, file: 0, declIndex: 0, memberIndex: -1 })
            c.modules[0].types["UniqueType"] = structTypeOf(s0)
            let u1 = checkUnique(ref c, from: 0, at: spanUniqueType)

            let fn0 = newFunction(ref c, by: FunctionSymbol { name: "uniqueFn", nameSpan: spanUniqueFn, span: spanUniqueFn, module: 0, file: 0, declIndex: 1, memberIndex: -1 })
            var fnList = List<FuncId>()
            fnList.append(fn0)
            c.modules[0].functions["uniqueFn"] = move fnList
            let u2 = checkUnique(ref c, from: 0, at: spanUniqueFn)

            let cn0 = newConst(ref c, by: ConstSymbol { name: "uniqueConst", nameSpan: spanUniqueConst, span: spanUniqueConst, module: 0, file: 0, declIndex: 2 })
            c.modules[0].constants["uniqueConst"] = cn0
            let u3 = checkUnique(ref c, from: 0, at: spanUniqueConst)

            print("== checkUnique")
            print($"unique: {u0} dupType: {u1} dupFn: {u2} dupConst: {u3}")

            // ── 3. reportDuplicate ─────────────────────────────────────────
            let preDiags = c.diagnostics.length
            reportDuplicate(ref c, from: 0, at: spanUniqueType, as: loc(0, at: spanUniqueType))
            let diagSame = c.diagnostics[c.diagnostics.length - 1].label
            reportDuplicate(ref c, from: 0, at: spanUniqueType, as: loc(1, at: TextSpan { start: 7, length: 11 }))
            let diagDiff = c.diagnostics[c.diagnostics.length - 1].label
            print("== reportDuplicate")
            print($"sameFile: {diagSame} diffFile: {diagDiff}")

            // ── 4. overlap ─────────────────────────────────────────────────
            // 3: fPos(i32 a)
            let fnPos1 = newFunction(ref c, by: FunctionSymbol { name: "fPos", nameSpan: TextSpan { start: 0, length: 4 }, span: TextSpan { start: 0, length: 19 }, module: 0, file: 0, declIndex: 3, memberIndex: -1 })
            // 4: fPos(i32 a, i32 b)
            let fnPos2 = newFunction(ref c, by: FunctionSymbol { name: "fPos", nameSpan: TextSpan { start: 0, length: 4 }, span: TextSpan { start: 0, length: 26 }, module: 0, file: 0, declIndex: 4, memberIndex: -1 })
            // 5: fPart(from i32 a)
            let fnPart1 = newFunction(ref c, by: FunctionSymbol { name: "fPart", nameSpan: TextSpan { start: 0, length: 5 }, span: TextSpan { start: 0, length: 26 }, module: 0, file: 0, declIndex: 5, memberIndex: -1 })
            // 6: fPart(to i32 a)
            let fnPart2 = newFunction(ref c, by: FunctionSymbol { name: "fPart", nameSpan: TextSpan { start: 0, length: 5 }, span: TextSpan { start: 0, length: 24 }, module: 0, file: 0, declIndex: 6, memberIndex: -1 })
            // 7: fRest(rest ReadOnlySpan<i32> args)
            let fnRest = newFunction(ref c, by: FunctionSymbol { name: "fRest", nameSpan: TextSpan { start: 0, length: 5 }, span: TextSpan { start: 0, length: 40 }, module: 0, file: 0, declIndex: 7, memberIndex: -1 })

            let ovSamePos = overlap(ref c, for: fnPos1, as: fnPos1)
            let ovDiffCount = overlap(ref c, for: fnPos1, as: fnPos2)
            let ovDiffPart = overlap(ref c, for: fnPart1, as: fnPart2)
            let ovSamePart = overlap(ref c, for: fnPart1, as: fnPart1)
            let ovRest = overlap(ref c, for: fnRest, as: fnPos2)

            // Synthesized functions without AST
            var fnSynth1 = FunctionSymbol { name: "synth1", nameSpan: TextSpan { start: 0, length: 0 }, span: TextSpan { start: 0, length: 0 }, module: 0, file: -1, declIndex: -1, memberIndex: -1 }
            let pSynth1 = newLocal(ref c, by: LocalSymbol { name: "p1", isParameter: true, ordinal: 0 })
            fnSynth1.parameters.append(pSynth1)
            let idSynth1 = newFunction(ref c, by: move fnSynth1)

            var fnSynth2 = FunctionSymbol { name: "synth2", nameSpan: TextSpan { start: 0, length: 0 }, span: TextSpan { start: 0, length: 0 }, module: 0, file: -1, declIndex: -1, memberIndex: -1 }
            let pSynth2 = newLocal(ref c, by: LocalSymbol { name: "p2", isParameter: true, ordinal: 0 })
            fnSynth2.parameters.append(pSynth2)
            let idSynth2 = newFunction(ref c, by: move fnSynth2)

            var fnSynth3 = FunctionSymbol { name: "synth3", nameSpan: TextSpan { start: 0, length: 0 }, span: TextSpan { start: 0, length: 0 }, module: 0, file: -1, declIndex: -1, memberIndex: -1 }
            let pSynth3a = newLocal(ref c, by: LocalSymbol { name: "p3a", isParameter: true, ordinal: 0 })
            let pSynth3b = newLocal(ref c, by: LocalSymbol { name: "p3b", isParameter: true, ordinal: 1 })
            fnSynth3.parameters.append(pSynth3a)
            fnSynth3.parameters.append(pSynth3b)
            let idSynth3 = newFunction(ref c, by: move fnSynth3)

            let ovSynthSame = overlap(ref c, for: idSynth1, as: idSynth2)
            let ovSynthDiff = overlap(ref c, for: idSynth1, as: idSynth3)

            print("== overlap")
            print($"samePos: {ovSamePos} diffPosCount: {ovDiffCount} diffPart: {ovDiffPart} samePart: {ovSamePart} rest: {ovRest} synthSame: {ovSynthSame} synthDiff: {ovSynthDiff}")

            // ── 5. checkOverloads ──────────────────────────────────────────
            // 8: ovFn(i32 a), 9: ovFn(i32 b)
            let idOv1 = newFunction(ref c, by: FunctionSymbol { name: "ovFn", nameSpan: TextSpan { start: 0, length: 4 }, span: TextSpan { start: 0, length: 18 }, module: 0, file: 0, declIndex: 8, memberIndex: -1 })
            let idOv2 = newFunction(ref c, by: FunctionSymbol { name: "ovFn", nameSpan: TextSpan { start: 0, length: 4 }, span: TextSpan { start: 0, length: 18 }, module: 0, file: 0, declIndex: 9, memberIndex: -1 })
            var ovList = List<FuncId>()
            ovList.append(idOv1)
            ovList.append(idOv2)
            c.modules[0].functions["ovFn"] = move ovList
            c.modules[0].allFunctions.append(idOv1)
            c.modules[0].allFunctions.append(idOv2)

            var partList = List<FuncId>()
            partList.append(fnPart1)
            partList.append(fnPart2)
            c.modules[0].functions["fPart"] = move partList
            c.modules[0].allFunctions.append(fnPart1)
            c.modules[0].allFunctions.append(fnPart2)

            checkOverloads(ref c, for: 0)
            let ovFnCount = c.modules[0].functions["ovFn"].length
            let fPartCount = c.modules[0].functions["fPart"].length
            print("== checkOverloads")
            print($"ovFnCount: {ovFnCount} fPartCount: {fPartCount}")

            // ── 6. errorSets (declareVariants & declarePayloads) ───────────
            // 10: union error MyError
            var spanMyErr = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[10] {
                case .errorEnumDecl(let n): spanMyErr = n.value.name
                default: break
            }
            let myErrId = newErrorSet(ref c, by: ErrorSetSymbol { name: "MyError", nameSpan: spanMyErr, span: spanMyErr, module: 0, file: 0, declIndex: 10 })
            c.modules[0].errorSets.append(myErrId)
            declareVariants(ref c, for: myErrId)
            declarePayloads(ref c, for: myErrId)

            // 11: union error EmptyError
            var spanEmptyErr = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[11] {
                case .errorEnumDecl(let n): spanEmptyErr = n.value.name
                default: break
            }
            let emptyErrId = newErrorSet(ref c, by: ErrorSetSymbol { name: "EmptyError", nameSpan: spanEmptyErr, span: spanEmptyErr, module: 0, file: 0, declIndex: 11 })
            c.modules[0].errorSets.append(emptyErrId)
            declareVariants(ref c, for: emptyErrId)

            let payCaseId = c.errorSets[myErrId].variants[1]
            print("== errorSets")
            print($"variants: {c.errorSets[myErrId].variants.length} payFields: {c.errorVariants[payCaseId].fields.length} emptyVars: {c.errorSets[emptyErrId].variants.length}")

            // ── 7. checkComposition ────────────────────────────────────────
            // 12: BaseA, 13: BaseB
            var spanBaseA = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[12] {
                case .errorEnumDecl(let n): spanBaseA = n.value.name
                default: break
            }
            let baseAId = newErrorSet(ref c, by: ErrorSetSymbol { name: "BaseA", nameSpan: spanBaseA, span: spanBaseA, module: 0, file: 0, declIndex: 12 })
            declareVariants(ref c, for: baseAId)

            var spanBaseB = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[13] {
                case .errorEnumDecl(let n): spanBaseB = n.value.name
                default: break
            }
            let baseBId = newErrorSet(ref c, by: ErrorSetSymbol { name: "BaseB", nameSpan: spanBaseB, span: spanBaseB, module: 0, file: 0, declIndex: 13 })
            declareVariants(ref c, for: baseBId)

            var compSets = List<ErrorSetId>()
            compSets.append(baseAId)
            compSets.append(baseBId)
            let excluded = List<VariantId>()
            let compType = errorUnionOf(ref c, for: in compSets, as: in excluded)

            var spanComp = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[14] {
                case .errorSetDecl(let n): spanComp = n.value.name
                default: break
            }
            let aliasId = newAlias(ref c, by: AliasPlaceholder { name: "CompSet", nameSpan: spanComp, span: spanComp, file: 0, declIndex: 14, target: compType })
            switch c.files[0].unit.members[14] {
                case .errorSetDecl(let n):
                    checkComposition(ref c, for: -1, from: in n.value)
                    checkComposition(ref c, for: c.aliases.length, from: in n.value)
                    checkComposition(ref c, for: aliasId, from: in n.value)
                default: break
            }

            var foundDupErrorCase = false
            for d in c.diagnostics {
                if d.code == "BYN3202" { foundDupErrorCase = true }
            }
            print("== composition")
            print($"hasDupCase: {foundDupErrorCase}")

            // ── 8. enums (declareEnumFields & declareEnumMembers) ───────────
            declareEnumMembers(ref c, for: -1)
            declareEnumMembers(ref c, for: c.enums.length)
            // 15: SampleEnum
            var spanSampleEnum = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[15] {
                case .enumDecl(let n): spanSampleEnum = n.value.name
                default: break
            }
            let enumId = newEnum(ref c, by: EnumSymbol { name: "SampleEnum", nameSpan: spanSampleEnum, span: spanSampleEnum, module: 0, file: 0, declIndex: 15, fieldsOf: -1 })
            declareEnumMembers(ref c, for: enumId)

            // 16: EmptyEnum
            var spanEmptyEnum = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[16] {
                case .enumDecl(let n): spanEmptyEnum = n.value.name
                default: break
            }
            let emptyEnumId = newEnum(ref c, by: EnumSymbol { name: "EmptyEnum", nameSpan: spanEmptyEnum, span: spanEmptyEnum, module: 0, file: 0, declIndex: 16, fieldsOf: -1 })
            declareEnumMembers(ref c, for: emptyEnumId)

            var validFields = 0
            var validName = ""
            for fId in c.enums[enumId].fields {
                if !isError(c.enumFields[fId].type) {
                    validFields += 1
                    validName = c.enumFields[fId].name
                }
            }
            var hasRefErr = false
            for d in c.diagnostics {
                if d.label == "`ref` is not allowed here" { hasRefErr = true }
            }
            print("== enums")
            print($"members: {c.enums[enumId].members.length} fields: {c.enums[enumId].fields.length} validFields: {validFields} validName: {validName} hasRefErr: {hasRefErr} hasFromOrd: {c.enums[enumId].fromOrdinal >= 0} hasParse: {c.enums[enumId].parse >= 0} emptyMembers: {c.enums[emptyEnumId].members.length}")

            // ── 9. unions (declareUnionVariants) ───────────────────────────
            declareUnionVariants(ref c, for: -1)
            declareUnionVariants(ref c, for: c.unions.length)
            // 17: SampleUnion
            var spanSampleUnion = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[17] {
                case .unionDecl(let n): spanSampleUnion = n.value.name
                default: break
            }
            let unionId = newUnion(ref c, by: UnionSymbol { name: "SampleUnion", nameSpan: spanSampleUnion, span: spanSampleUnion, module: 0, file: 0, declIndex: 17 })
            declareUnionVariants(ref c, for: unionId)

            // 18: EmptyUnion
            var spanEmptyUnion = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[18] {
                case .unionDecl(let n): spanEmptyUnion = n.value.name
                default: break
            }
            let emptyUnionId = newUnion(ref c, by: UnionSymbol { name: "EmptyUnion", nameSpan: spanEmptyUnion, span: spanEmptyUnion, module: 0, file: 0, declIndex: 18 })
            declareUnionVariants(ref c, for: emptyUnionId)

            let tagDec = c.unionVariants[c.unions[unionId].variants[1]].tag
            let tagNeg = c.unionVariants[c.unions[unionId].variants[2]].tag
            let tagHex = c.unionVariants[c.unions[unionId].variants[3]].tag
            let badBin = t12ParseInt("0b2")
            print("== unions")
            print($"variants: {c.unions[unionId].variants.length} decTag: {tagDec} negTag: {tagNeg} hexTag: {tagHex} badBinNull: {badBin is null} emptyVars: {c.unions[emptyUnionId].variants.length}")

            // ── 10. members (declareMembers) ───────────────────────────────
            declareMembers(ref c, for: -1)
            declareMembers(ref c, for: c.structs.length)
            // 19: IBase
            var spanIBase = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[19] {
                case .interfaceDecl(let n): spanIBase = n.value.name
                default: break
            }
            let iBaseId = newInterface(ref c, by: InterfaceSymbol { name: "IBase", nameSpan: spanIBase, span: spanIBase, module: 0, file: 0, declIndex: 19 })
            c.modules[0].interfaces.append(iBaseId)
            c.modules[0].types["IBase"] = interfaceTypeOf(iBaseId)
            let assocTp = newTypeParam(ref c, by: TypeParam { name: "Item", constraints: List<InterfaceId>() })
            c.interfaces[iBaseId].associatedTypes.append(assocTp)

            // 20: SampleStruct
            var spanSampleStruct = TextSpan { start: 0, length: 0 }
            switch c.files[0].unit.members[20] {
                case .structDecl(let n): spanSampleStruct = n.value.name
                default: break
            }
            let structId = newStruct(ref c, by: StructSymbol { name: "SampleStruct", nameSpan: spanSampleStruct, span: spanSampleStruct, module: 0, file: 0, declIndex: 20 })
            c.modules[0].structs.append(structId)
            declareMembers(ref c, for: structId)

            var unsuppCount = 0
            for d in c.diagnostics {
                if d.code == "BYN2022" { unsuppCount += 1 }
            }
            print("== members")
            print($"bases: {c.structs[structId].bases.length} fields: {c.structs[structId].fields.length} props: {c.structs[structId].properties.length} methods: {c.structs[structId].methods.length} ctors: {c.structs[structId].constructors.length} unsupp: {unsuppCount}")

            // ── 11. diagnostics ───────────────────────────────────────────
            print("== diagnostics")
            for d in c.diagnostics {
                print($"{d.code}")
            }

            return 0
        }
        """;

    private static readonly Lazy<Dictionary<string, List<string>>> Sections = new(() => SelfhostSemanticsHarness.RunSections(MainSource()));

    [Fact]
    public void SmokeMatches()
    {
        var expected = new List<string> { "file: 0 span: 10 pub: true priv: true module: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "smoke", expected);
    }

    [Fact]
    public void CheckUniqueMatches()
    {
        var expected = new List<string> { "unique: true dupType: false dupFn: false dupConst: false" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "checkUnique", expected);
    }

    [Fact]
    public void ReportDuplicateMatches()
    {
        var expected = new List<string> { "sameFile: already declared on line 1 diffFile: already declared on other.byn:1" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "reportDuplicate", expected);
    }

    [Fact]
    public void OverlapMatches()
    {
        var expected = new List<string> { "samePos: true diffPosCount: false diffPart: false samePart: true rest: true synthSame: true synthDiff: false" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "overlap", expected);
    }

    [Fact]
    public void CheckOverloadsMatches()
    {
        var expected = new List<string> { "ovFnCount: 1 fPartCount: 2" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "checkOverloads", expected);
    }

    [Fact]
    public void ErrorSetsMatches()
    {
        var expected = new List<string> { "variants: 8 payFields: 2 emptyVars: 0" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "errorSets", expected);
    }

    [Fact]
    public void CompositionMatches()
    {
        var expected = new List<string> { "hasDupCase: true" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "composition", expected);
    }

    [Fact]
    public void EnumsMatches()
    {
        var expected = new List<string> { "members: 2 fields: 6 validFields: 1 validName: val1 hasRefErr: true hasFromOrd: true hasParse: true emptyMembers: 0" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "enums", expected);
    }

    [Fact]
    public void UnionsMatches()
    {
        var expected = new List<string> { "variants: 8 decTag: 10 negTag: -5 hexTag: 32 badBinNull: true emptyVars: 0" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "unions", expected);
    }

    [Fact]
    public void MembersMatches()
    {
        var expected = new List<string> { "bases: 1 fields: 3 props: 2 methods: 2 ctors: 1 unsupp: 3" };
        SelfhostSemanticsHarness.AssertSection(Sections.Value, "members", expected);
    }

    [Fact]
    public void DiagnosticsMatches()
    {
        var diags = Sections.Value["diagnostics"];
        Assert.Contains("BYN2006", diags); // duplicateDeclaration
        Assert.Contains("BYN2609", diags); // emptyErrorSet
        Assert.Contains("BYN2722", diags); // emptyEnum
        Assert.Contains("BYN2702", diags); // emptyUnion
        Assert.Contains("BYN2108", diags); // requiredWithDefault
        Assert.Contains("BYN2805", diags); // duplicateConformance
        Assert.Contains("BYN2724", diags); // invalidEnumParameter
        Assert.Contains("BYN2710", diags); // unionValueOnPayload
        Assert.Contains("BYN2709", diags); // unionValueNotInteger
        Assert.Contains("BYN2711", diags); // duplicateUnionValue
        Assert.Contains("BYN2721", diags); // duplicateEnumMember
        Assert.Contains("BYN2701", diags); // duplicateUnionVariant
        Assert.Contains("BYN2770", diags); // unknownAttribute
        Assert.Contains("BYN1016", diags); // modifierNotAllowed
        Assert.Contains("BYN2022", diags); // unsupported
        Assert.Contains("BYN3202", diags); // duplicateErrorCase
    }
}
