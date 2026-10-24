// deno test -A scripts/plan-strings-check.test.js
import { assert, assertEquals, assertStringIncludes } from "jsr:@std/assert@1";

const script = new URL("./plan-strings-check.js", import.meta.url).pathname;

const plan = [
  '- (c) BYN1044 `KeywordOrder` "`{0}` is an input form; write `{1}`" (label at the keywords; help "put the kind first: `{1}`"; fix yes).',
  '- (c) BYN1043 `TypeNameOrder` (revised), two forms. A field: "`{0}: {1}` is an input form; write `{1} {0}`" (label "name before type"; help "put the type before the name; declarations are type-first"; fix yes).',
  '- (c) BYN1053 `OneLine` "`{0}` declares its cases on one line" (O1; label at the first `,`; help "write each case on its own line"; fix yes).',
  '- (c) BYN6006 `ContextInput` "`context (…) { }` is now `provide … { }`" (label at `context`; help "write `provide`"; fix yes).',
].join("\n");

const descriptors = `
    public static readonly DiagnosticDescriptor KeywordOrder = Error("BYN1044", "\`{0}\` is an input form; write \`{1}\`");
    public static readonly DiagnosticDescriptor TypeNameOrder = Error("BYN1043", "\`{0}\` is an input form; write \`{1}\`");
    public static readonly DiagnosticDescriptor OneLine = Error("BYN1053", "\`{0}\` declares its cases on one line");
    public static readonly DiagnosticDescriptor ContextInput = Error("BYN6006", "\`context (…) {{ }}\` is now \`provide … {{ }}\`");
`;

const parser = (help1044) => `
class Parser {
    void A() { Report(KeywordOrder, help: $"${help1044}"); }
    void B() { Report(TypeNameOrder, help: "put the type before the name; declarations are type-first"); }
    void C() { Report(OneLine, help: "write each case on its own line"); }
    void D() { Report(ContextInput, help: "write \`provide\`"); }
}
`;

const helpTest = `
    private static readonly Dictionary<string, string> Allowed = new()
    {
        ["did you mean \`"] = "question",
    };
`;

const lexicon = `
    public static readonly FrozenSet<string> Verbs = [
        "put", "write",
    ];
`;

const page = (code, message, help) => `# ${code}: title\n\n**Message.** ${message}\n\n**Help.** ${help}\n`;

async function git(dir, ...args) {
  const out = await new Deno.Command("git", { args, cwd: dir, stdout: "null", stderr: "null" }).output();
  assert(out.success, `git ${args.join(" ")}`);
}

async function fixture(help1044) {
  const dir = await Deno.makeTempDir();
  const write = async (path, text) => {
    await Deno.mkdir(`${dir}/${path.slice(0, path.lastIndexOf("/"))}`, { recursive: true });
    await Deno.writeTextFile(`${dir}/${path}`, text);
  };
  await write("docs/plans/2026-10-22-002-design-round-3.md", plan);
  await write("src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs", "class DiagnosticCodes {}\n");
  await write("tests/Bayan.Tests/HelpLanguageTests.cs", helpTest);
  await write("src/Bayan.Semantics/Lexicon.cs", lexicon);
  await git(dir, "init", "-q", "-b", "dev");
  await git(dir, "add", "-A");
  await git(dir, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "--no-verify", "-m", "base");
  await write("src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs", `class DiagnosticCodes {${descriptors}}\n`);
  await write("src/Bayan.Syntax/Parsing/Parser.cs", parser(help1044));
  await write("tests/cases/diag/field.diag", "error[BYN1043]: `x: i64` is an input form; write `i64 x`\n");
  await write("docs/diagnostics/BYN1044.md", page("BYN1044", "`{0}` is an input form; write `{1}`", "put the kind first: `{1}`"));
  await write("docs/diagnostics/BYN1043.md", page("BYN1043", "`x: i64` is an input form; write `i64 x`", "put the type before the name; declarations are type-first"));
  await write("docs/diagnostics/BYN1053.md", page("BYN1053", "`{0}` declares its cases on one line", "write each case on its own line"));
  await write("docs/diagnostics/BYN6006.md", page("BYN6006", "`context (…) { }` is now `provide … { }`", "write `provide`"));
  await git(dir, "checkout", "-q", "-b", "feat/x");
  await git(dir, "add", "-A");
  await git(dir, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "--no-verify", "-m", "step");
  return dir;
}

async function check(dir, ...args) {
  const out = await new Deno.Command("deno", { args: ["run", "-A", script, ...args], cwd: dir, stdout: "piped" }).output();
  return { code: out.code, text: new TextDecoder().decode(out.stdout) };
}

Deno.test("plan strings copied verbatim pass, codes found from the descriptor diff", async () => {
  const dir = await fixture("put the kind first: `{canonical}`");
  try {
    const { code, text } = await check(dir);
    assertEquals(code, 0, text);
    assertStringIncludes(text, "all plan strings found for 4 code(s)");
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

Deno.test("a help string that drifts from the plan fails and names it", async () => {
  const dir = await fixture("write the kind before the modifier: `{canonical}`");
  try {
    const { code, text } = await check(dir, "BYN1044");
    assertEquals(code, 1);
    assertStringIncludes(text, 'BYN1044 help not in src/: "put the kind first: `{1}`"');
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

Deno.test("a page without the plan's help fails", async () => {
  const dir = await fixture("put the kind first: `{canonical}`");
  try {
    await Deno.writeTextFile(`${dir}/docs/diagnostics/BYN1053.md`, "# BYN1053: title\n\n**Why.** prose only\n");
    const { code, text } = await check(dir, "BYN1053");
    assertEquals(code, 1);
    assertStringIncludes(text, "BYN1053 help not in docs/diagnostics/BYN1053.md");
    assertStringIncludes(text, "BYN1053 message not in docs/diagnostics/BYN1053.md");
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

Deno.test("a plan help without a leading verb fails, alone and in --all-help", async () => {
  const dir = await fixture("put the kind first: `{canonical}`");
  try {
    const planFile = `${dir}/docs/plans/2026-10-22-002-design-round-3.md`;
    const text = await Deno.readTextFile(planFile);
    await Deno.writeTextFile(planFile, text.replace('help "write each case on its own line"', 'help "each case goes on its own line"'));
    const one = await check(dir, "BYN1053");
    assertEquals(one.code, 1);
    assertStringIncludes(one.text, 'BYN1053 help in the plan does not start with a verb from src/Bayan.Semantics/Lexicon.cs: "each case goes on its own line"');
    const all = await check(dir, "--all-help");
    assertEquals(all.code, 1);
    assertStringIncludes(all.text, "1 plan help(s) without a leading verb");
    await Deno.writeTextFile(planFile, text);
    assertEquals((await check(dir, "--all-help")).code, 0);
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});

Deno.test("plan text with {{ }} escapes and a (plan, revision NNNN) tag is matched", async () => {
  const dir = await fixture("put the kind first: `{canonical}`");
  try {
    const planFile = `${dir}/docs/plans/2026-10-22-002-design-round-3.md`;
    await Deno.writeTextFile(planFile, (await Deno.readTextFile(planFile)) + "\n" + [
      '- (c) BYN7005 `StateLiteralNeedsState` "`{0}` has states; write `{0}.{1} {{ … }}`" (label at the type name; help "write the state"; fix no). BYN7008 (plan) `StateShadowsStatic` "state `.{0}` shadows `{1}.{0}`" (label at the state; help "write another name"; fix no). BYN7009 (plan, revision 0002) `MissingBecome` "`{0}` returns without `become`" (label at the return; help "write `become .{1}(…)`"; fix no).',
    ].join("\n"));
    const src = `${dir}/src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs`;
    await Deno.writeTextFile(src, (await Deno.readTextFile(src)).replace("}\n", `
    public static readonly DiagnosticDescriptor StateLiteralNeedsState = Error("BYN7005", "\`{0}\` has states; write \`{0}.{1} {{ … }}\`");
    public static readonly DiagnosticDescriptor StateShadowsStatic = Error("BYN7008", "state \`.{0}\` shadows \`{1}.{0}\`");
}
`));
    await Deno.writeTextFile(`${dir}/src/Bayan.Syntax/Parsing/States.cs`, 'class S { void A() { Help("write the state"); Help("write another name"); } }\n');
    await Deno.writeTextFile(`${dir}/docs/diagnostics/BYN7005.md`, page("BYN7005", "`{0}` has states; write `{0}.{1} { … }`", "write the state"));
    await Deno.writeTextFile(`${dir}/docs/diagnostics/BYN7008.md`, page("BYN7008", "state `.{0}` shadows `{1}.{0}`", "write another name"));
    await git(dir, "add", "-A");
    const { code, text } = await check(dir, "BYN7005", "BYN7008");
    assertEquals(code, 0, text);
  } finally {
    await Deno.remove(dir, { recursive: true });
  }
});
