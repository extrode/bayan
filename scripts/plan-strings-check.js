// Checks that diagnostic messages and help strings are copied verbatim from the
// round-3 plan's code entries into src/ and docs/diagnostics/BYN####.md.
//
//   deno run -A scripts/plan-strings-check.js [--base <ref>] [--plan <file>] [BYN#### ...]
//
// A string counts as found in src/ when a C# string literal holds it or a tests/cases
// `.diag` golden renders it; on the page, when the page holds it or renders it.
// Codes: those named on the command line, else every code whose descriptor line in
// DiagnosticCodes.cs differs from <base> (default dev). Placeholders compare equal:
// `{0}` in the plan matches `{anything}` in an interpolated C# string.
// Each checked help must also start with a verb from the grammar checker's list (R15), so the
// plan can be copied verbatim; --all-help checks every help in the plan and nothing else.
// Exit 0 when every checked string is found, 1 when one is missing, 2 on bad usage.

const args = [...Deno.args];
const take = (flag, fallback) => {
  const i = args.indexOf(flag);
  if (i < 0) return fallback;
  const value = args[i + 1];
  args.splice(i, 2);
  return value;
};
const base = take("--base", "dev");
const planPath = take("--plan", "docs/plans/2026-10-22-002-design-round-3.md");
const allHelp = args.includes("--all-help");
if (allHelp) args.splice(args.indexOf("--all-help"), 1);
const descriptorsPath = "src/Bayan.Syntax/Diagnostics/DiagnosticCodes.cs";
const helpTestPath = "tests/Bayan.Tests/HelpLanguageTests.cs";
const lexiconPath = "src/Bayan.Semantics/Lexicon.cs";

const run = async (...cmd) => {
  const out = await new Deno.Command(cmd[0], { args: cmd.slice(1), stdout: "piped", stderr: "null" }).output();
  return out.success ? new TextDecoder().decode(out.stdout) : "";
};

// Plan text: `{0}` holes become `{}`; plan and C# text: `{{`/`}}` and `\"` are undone, any C# hole becomes `{}`.
const normPlan = (s) => s.replace(/\{\{/g, "\u0001").replace(/\}\}/g, "\u0002")
  .replace(/\{\d+\}/g, "{}").replace(/\u0001/g, "{").replace(/\u0002/g, "}").replace(/\\"/g, '"');
const norm = (s) => s.replace(/\{\{/g, "\u0001").replace(/\}\}/g, "\u0002")
  .replace(/\{[^{}]*\}/g, "{}").replace(/\u0001/g, "{").replace(/\u0002/g, "}")
  .replace(/\\"/g, '"');
// A plan string as a pattern over rendered output: each hole matches any text on one line.
const pattern = (s) => new RegExp(normPlan(s).split("{}").map((p) => p.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("[^\\n]+?"));

// Plan entries: BYN#### [(plan[, warning|revision NNNN])] `Name` ... "message" (label ...; help "..."; fix ...), one or
// more message forms per entry. An entry runs to the next entry start or the end of its line.
const parsePlan = (text) => {
  const entries = new Map();
  const start = /BYN(\d{4})(?: \(plan(?:, [\w ]+)?\))? `\w+`/g;
  const form = /"((?:[^"\\]|\\.)*)" \(((?:[^;()"]*; )?label(?:[^()"]|"(?:[^"\\]|\\.)*"|\([^()]*\))*)\)/g;
  const helpIn = /help "((?:[^"\\]|\\.)*)"/;
  const starts = [...text.matchAll(start)];
  starts.forEach((m, i) => {
    const lineEnd = text.indexOf("\n", m.index);
    const end = Math.min(starts[i + 1]?.index ?? text.length, lineEnd < 0 ? text.length : lineEnd);
    const chunk = text.slice(m.index + m[0].length, end);
    const forms = [...chunk.matchAll(form)].map((f) => ({ message: f[1], help: helpIn.exec(f[2])?.[1] ?? null }));
    if (!forms.length) return;
    const code = `BYN${m[1]}`;
    entries.set(code, [...(entries.get(code) ?? []), ...forms]);
  });
  return entries;
};

const changedCodes = async () => {
  const diff = await run("git", "diff", `${base}...HEAD`, "--", descriptorsPath);
  return [...new Set([...diff.matchAll(/^\+.*"(BYN\d{4})"/gm)].map((m) => m[1]))];
};

const srcText = async () => {
  const files = (await run("git", "ls-files", "src")).split("\n").filter((f) => f.endsWith(".cs"));
  const text = (await Promise.all(files.map((f) => Deno.readTextFile(f)))).join("\n");
  return [...text.matchAll(/"((?:[^"\\\n]|\\.)*)"/g)].map((m) => norm(m[1])).join("\n");
};

// R15's verbs, the grammar checker's verb list (BYN5014), and the allowed openings from the test that enforces R15 on src/.
const helpRule = async () => {
  const lexicon = await Deno.readTextFile(lexiconPath);
  const verbs = new Set([...(lexicon.match(/Verbs =\s*\[([\s\S]*?)\];/)?.[1] ?? "").matchAll(/"(\w+)"/g)].map((m) => m[1]));
  const text = await Deno.readTextFile(helpTestPath);
  const allowed = [...(text.match(/Allowed = new\(\)\s*\{([\s\S]*?)\};/)?.[1] ?? "").matchAll(/\["((?:[^"\\]|\\.)*)"\]/g)].map((m) => m[1]);
  return (help) => verbs.has(help.split(/[ ,;:]/)[0]) || allowed.some((a) => help.startsWith(a));
};
const startsWithVerb = await helpRule();

const plan = parsePlan(await Deno.readTextFile(planPath));
if (allHelp) {
  const bad = [...plan].flatMap(([code, forms]) => forms.filter((f) => f.help && !startsWithVerb(f.help)).map((f) => `${code} help does not start with a verb: "${f.help}"`));
  bad.forEach((line) => console.log(line));
  console.log(bad.length ? `${bad.length} plan help(s) without a leading verb` : "every plan help starts with a verb");
  Deno.exit(bad.length ? 1 : 0);
}
const codes = args.length ? args : await changedCodes();
if (!codes.length) {
  console.log(`no changed descriptors against ${base}`);
  Deno.exit(0);
}
const src = await srcText();
const goldenFiles = (await run("git", "ls-files", "tests/cases")).split("\n").filter((f) => f.endsWith(".diag"));
const goldens = (await Promise.all(goldenFiles.map((f) => Deno.readTextFile(f).catch(() => "")))).join("\n");
let missing = 0;
for (const code of codes) {
  const entries = plan.get(code);
  if (!entries) {
    console.log(`${code}: no plan entry (skipped)`);
    continue;
  }
  let page = "";
  try { page = await Deno.readTextFile(`docs/diagnostics/${code}.md`); } catch { /* reported below */ }
  for (const { message, help } of entries) {
    if (help && !startsWithVerb(help)) {
      missing++;
      console.log(`${code} help in the plan does not start with a verb from ${lexiconPath}: "${help}"`);
    }
    for (const [kind, text] of [["message", message], ["help", help]]) {
      if (!text) continue;
      const want = normPlan(text);
      const rendered = pattern(text);
      for (const [where, hay] of [["src", src], ["page", page]]) {
        if (hay.includes(want) || rendered.test(where === "src" ? goldens : hay)) continue;
        missing++;
        console.log(`${code} ${kind} not in ${where === "src" ? "src/" : `docs/diagnostics/${code}.md`}: "${text}"`);
      }
    }
  }
}
console.log(missing ? `${missing} string(s) differ from the plan` : `all plan strings found for ${codes.length} code(s)`);
Deno.exit(missing ? 1 : 0);
