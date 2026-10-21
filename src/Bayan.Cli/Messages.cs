using System.Globalization;

namespace Bayan.Cli;

/// <summary>A human-facing CLI string; codes, paths, symbols and counts are <c>{n}</c> arguments.</summary>
public sealed record Message(string Id, string Format)
{
    // Current culture, as the interpolated strings these replaced used.
    public string Text(params object?[] args) => string.Format(CultureInfo.CurrentCulture, Format, args);
}

/// <summary>
/// The CLI's message table, keyed by dotted id. Diagnostic messages stay in
/// <c>DiagnosticCodes</c>; <c>MessagesTests</c> keeps prose out of the CLI sources.
/// </summary>
public static class Messages
{
    private static readonly List<Message> Registry = new();

    private static Message Register(string id, string format)
    {
        var m = new Message(id, format);
        Registry.Add(m);
        return m;
    }

    public static IReadOnlyList<Message> All => Registry;

    public static Message Get(string id) => Registry.Find(m => m.Id == id) ?? throw new KeyNotFoundException(id);

    // ── usage ────────────────────────────────────────────────────────────────────────
    public static readonly Message Usage = Register("usage.help", """
        usage: bayan <command> [args]

        commands:
          new <name>           create an app in a new directory [--lib|--script] [--vcs git|none] [--ci github|none]
          init [<dir>]         add a project's missing files to a directory (same options as new)
          lex [--json] <file>  print the token stream [--json: as one JSON object] (`-` reads stdin)
          parse [--ast] <file> check syntax; with --ast print the syntax tree
          types <path>         print each function's parameter and local types as JSON
          check <path>         report diagnostics for a program (a file or directory)
          build <path>         compile to an executable [-o <file>] [--mode debug|safe|fast] [--target <t>] [--emit-c]
                               [-v: print each phase's time and the output path]
          run <path>           compile and run [--mode debug|safe|fast] [-- <program args>]
          test <path>          compile and run the tests, each in its own process [--mode debug|safe|fast]
                               [--json: print the results as JSON instead]
                               [--changed [--base <ref>]: only modules with files changed since HEAD or <ref>]
          dist [<dir>]         build release binaries into dist/<version>/ [--target <t>|all] [--mode m]
                               [--force: rebuild an existing version] [--allow-dirty: build from a dirty git tree]
          clean [<dir>]        remove out/ [--dist: also dist/] [--cache: also the global build cache]
          cache stats          show the shared build cache's location and size
          explain <code>       explain a diagnostic, e.g. `bayan explain BYN0003`
          <script>.byn         run a script, so `#!/usr/bin/env bayan` works
          doctor               check the C compiler, zig, git and the build cache, with fixes
          --version            print the compiler version and the commit it was built from
          --help               print this message

        check, types, build, run and test take a project directory (one with `package.byn`; the default
        inside a project), a script (a file starting with `#!`), or a file or directory of `.byn` files.

        options for every command:
          --color auto|always|never
                               color for terminals (auto: on unless piped or NO_COLOR is set;
                               FORCE_COLOR turns it on, TERM=dumb off)
          --ascii              use ASCII symbols instead of ◆ ✗ │ (also with TERM=dumb or a non-UTF-8 locale)

        options for check, types, build, run and test:
          --all                show every error instead of the first 20 (JSON is never capped)
          --diagnostics json   print each diagnostic to stderr as one JSON object per line
                               (code, severity, file, span, message, label, help, fixes)

        exit codes:
          0  success
          1  compile errors
          2  usage errors (unknown command or option, missing path)
          3  test failures
          4  toolchain problems (no C compiler, unwritable cache, C compiler failure)

        `bayan run` exits 1 when compilation fails and otherwise passes the program's own exit code
        through unchanged, so it cannot tell a build failure from a program that exits 1. When that
        matters, run `bayan build` first or read `--diagnostics json`.
        """);
    public static readonly Message UnknownCommand = Register("usage.unknown-command", "bayan: unknown command `{0}`");
    public static readonly Message UnknownMode = Register("usage.unknown-mode", "bayan: unknown mode `{0}` (expected debug, safe or fast)");
    public static readonly Message UnknownDiagnosticsFormat = Register("usage.unknown-diagnostics-format", "bayan: unknown diagnostics format `{0}` (expected json or text)");
    public static readonly Message UnknownTarget = Register("usage.unknown-target", "bayan: unknown target `{0}` (expected host, {1})");
    public static readonly Message UnknownDistTarget = Register("usage.unknown-dist-target", "bayan: unknown target `{0}` (expected host, all, {1})");
    public static readonly Message UnknownColor = Register("usage.unknown-color", "bayan: unknown color setting `{0}` (expected auto, always or never)");
    public static readonly Message UnexpectedArgument = Register("usage.unexpected-argument", "bayan: unexpected argument `{0}`");
    public static readonly Message ExpectedPath = Register("usage.expected-path", "bayan: expected a file or directory (or run inside a project with a `{0}`)");
    public static readonly Message NotFound = Register("usage.not-found", "bayan: not found: {0}");
    public static readonly Message ExpectedOneFile = Register("usage.expected-one-file", "bayan: expected exactly one file");
    public static readonly Message FileNotFound = Register("usage.file-not-found", "bayan: file not found: {0}");
    public static readonly Message ExplainExpected = Register("usage.explain-expected", "bayan: expected one diagnostic code, e.g. `{0}`");
    public static readonly Message ExplainUnknown = Register("usage.explain-unknown", "bayan: no explanation for `{0}`");
    public static readonly Message CacheUsage = Register("usage.cache", "usage: {0}   (`{1}` empties it)");
    public static readonly Message CommandUsage = Register("usage.command", "usage: {0}");

    // ── version ──────────────────────────────────────────────────────────────────────
    public static readonly Message VersionLine = Register("version.line", "{0} {1} (commit {2})");

    // ── build ────────────────────────────────────────────────────────────────────────
    public static readonly Message NoEntryApp = Register("build.no-entry.app", "bayan: `{0}` is an app, so its code goes in `{1}`, which doesn't exist");
    public static readonly Message NoEntryLib = Register("build.no-entry.lib", "bayan: `{0}` is a library, so its code goes in `{1}`, which doesn't exist");
    public static readonly Message LibraryNoProgram = Register("build.library", "bayan: `{0}` is a library, so there is no program to {1}; `{2}` runs its tests");
    public static readonly Message ToolchainError = Register("build.toolchain", "bayan: {0}");
    public static readonly Message DiagCapped = Register("diag.capped", "{0} and {1} more errors (use --all to show them)");
    public static readonly Message CompileProgress = Register("build.progress.compiling", "compiling");
    public static readonly Message BuildProgress = Register("build.progress.building", "building");
    public static readonly Message BuildSummary = Register("build.summary", "{0} built {1} ({2}, {3}) in {4}");
    public static readonly Message BuildFromCache = Register("build.summary.cached", " {0} from cache");
    public static readonly Message CheckSummary = Register("check.summary", "{0} checked {1} in {2}");
    public static readonly Message BuildFailed = Register("build.failed", "{0} build failed: {1}, {2}");
    public static readonly Message ErrorsOne = Register("build.failed.errors.one", "{0} error");
    public static readonly Message ErrorsMany = Register("build.failed.errors.many", "{0} errors");
    public static readonly Message WarningsOne = Register("build.failed.warnings.one", "{0} warning");
    public static readonly Message WarningsMany = Register("build.failed.warnings.many", "{0} warnings");
    public static readonly Message BuildExplain = Register("build.failed.explain", "  explain: bayan explain {0}");
    public static readonly Message PhaseParse = Register("build.phase.parse", "parse");
    public static readonly Message PhaseCheck = Register("build.phase.check", "check");
    public static readonly Message PhaseEmit = Register("build.phase.emit", "emit");
    public static readonly Message PhaseCompile = Register("build.phase.compile", "compile");
    public static readonly Message PhaseLine = Register("build.phase.line", "  {0,-11}{1}");
    public static readonly Message PhaseFromCache = Register("build.phase.cached", "from cache");
    public static readonly Message PhaseOutput = Register("build.phase.output", "  {0}");
    public static readonly Message Seconds = Register("build.seconds", "{0}s");

    // ── cache ────────────────────────────────────────────────────────────────────────
    public static readonly Message CacheDirectory = Register("cache.directory", "cache:    {0}");
    public static readonly Message CachePrograms = Register("cache.programs", "programs: {0} ({1:0.0} MB)");
    public static readonly Message CacheScripts = Register("cache.scripts", "scripts:  {0}");

    // ── test ─────────────────────────────────────────────────────────────────────────
    public static readonly Message TestRunningOne = Register("test.running.one", "running {0} test{1}");
    public static readonly Message TestRunningMany = Register("test.running.many", "running {0} tests{1}");
    public static readonly Message TestSkipped = Register("test.skipped", " ({0} in unchanged modules skipped)");
    public static readonly Message TestProgress = Register("test.progress", "testing {0}");
    public static readonly Message TestTimedOut = Register("test.timed-out", "timed out after {0:0} s");
    public static readonly Message TestLabelPass = Register("test.label.pass", "ok   ");
    public static readonly Message TestLabelFail = Register("test.label.fail", "FAIL ");
    public static readonly Message TestLabelPanic = Register("test.label.panic", "PANIC");
    public static readonly Message TestLabelPending = Register("test.label.pending", "PEND ");
    public static readonly Message TestLabelTimeout = Register("test.label.timeout", "TIME ");
    public static readonly Message TestResult = Register("test.result", "  {0}  {1}  ({2})");
    public static readonly Message TestDetail = Register("test.detail", "         {0}");
    public static readonly Message TestPassed = Register("test.passed", "{0} passed");
    public static readonly Message TestFailed = Register("test.failed", ", {0} failed");
    public static readonly Message TestPanicked = Register("test.panicked", ", {0} panicked");
    public static readonly Message TestTimedOutCount = Register("test.timed-out-count", ", {0} timed out");
    public static readonly Message TestPending = Register("test.pending", ", {0} pending");
    public static readonly Message TestSummaryOne = Register("test.summary.one", "{0} test: {1}  ({2})");
    public static readonly Message TestSummaryMany = Register("test.summary.many", "{0} tests: {1}  ({2})");
    public static readonly Message TestMs = Register("test.ms", "{0} ms");
    public static readonly Message ChangedNoGit = Register("test.changed.no-git", "bayan: `{0}` needs git, which isn't installed");
    public static readonly Message ChangedNoRepository = Register("test.changed.no-repository", "bayan: `{0}` needs a git repository, and `{1}` isn't in one");
    public static readonly Message ChangedNoCompare = Register("test.changed.no-compare", "bayan: git can't compare the working tree with `{0}`");

    // ── doctor ───────────────────────────────────────────────────────────────────────
    public static readonly Message DoctorLine = Register("doctor.line", "{0} {1}  {2}");
    public static readonly Message DoctorFailLine = Register("doctor.fail", "{0} {1}: {2}");
    public static readonly Message DoctorHelp = Register("doctor.help", "  help: {0}");
    public static readonly Message DoctorBayan = Register("doctor.bayan", "bayan");
    public static readonly Message DoctorVersion = Register("doctor.bayan.version", "{0} (commit {1})");
    public static readonly Message DoctorHost = Register("doctor.host", "host");
    public static readonly Message DoctorNoHost = Register("doctor.host.none", "this machine has no Bayan target");
    public static readonly Message DoctorNoHostFix = Register("doctor.host.none.fix", "build on linux, macos or windows, x64 or arm64");
    public static readonly Message DoctorCCompiler = Register("doctor.cc", "C compiler");
    public static readonly Message DoctorNoCCompiler = Register("doctor.cc.none", "none found");
    public static readonly Message DoctorNoCCompilerFix = Register("doctor.cc.none.fix", "install zig ({0}), or set {1} to a C compiler");
    public static readonly Message DoctorCCompilerEnvBroken = Register("doctor.cc.env-broken", "{0} is `{1}`, which can't be run");
    public static readonly Message DoctorCCompilerEnvBrokenFix = Register("doctor.cc.env-broken.fix", "point {0} at a C compiler, or unset it to use zig");
    public static readonly Message DoctorCCompilerBroken = Register("doctor.cc.broken", "`{0}` can't be run");
    public static readonly Message DoctorCCompilerBrokenFix = Register("doctor.cc.broken.fix", "reinstall it, or set {0} to a working C compiler");
    public static readonly Message DoctorTool = Register("doctor.tool", "{0} ({1})");
    public static readonly Message DoctorToolBroken = Register("doctor.tool.broken", "{0} can't be run");
    public static readonly Message DoctorZig = Register("doctor.zig", "zig");
    public static readonly Message DoctorNoZig = Register("doctor.zig.none", "not found; building for other targets needs it");
    public static readonly Message DoctorInstallZig = Register("doctor.zig.install", "install zig ({0})");
    public static readonly Message DoctorReinstallZig = Register("doctor.zig.reinstall", "reinstall zig ({0})");
    public static readonly Message DoctorGit = Register("doctor.git", "git");
    public static readonly Message DoctorNoGit = Register("doctor.git.none", "not found; `{0}` skips `{1}`, `{2}` records no commit, `{3}` is unavailable");
    public static readonly Message DoctorInstallGit = Register("doctor.git.install", "install git");
    public static readonly Message DoctorReinstallGit = Register("doctor.git.reinstall", "reinstall git");
    public static readonly Message DoctorCache = Register("doctor.cache", "cache");
    public static readonly Message DoctorCacheOk = Register("doctor.cache.ok", "{0} (writable, {1} programs, {2:0.0} MB)");
    public static readonly Message DoctorCacheBroken = Register("doctor.cache.broken", "{0} isn't writable: {1}");
    public static readonly Message DoctorCacheBrokenFix = Register("doctor.cache.broken.fix", "set {0} to a writable directory");

    // ── dist and clean ───────────────────────────────────────────────────────────────
    public static readonly Message DistNotProject = Register("dist.not-project", "bayan: `{0}` isn't a project (no `{1}`); scripts and loose files have no dist");
    public static readonly Message DistLibrary = Register("dist.library", "bayan: `{0}` is a library; library distribution arrives later, with FFI export");
    public static readonly Message DistDirty = Register("dist.dirty", "bayan: the git tree has uncommitted changes, so this build couldn't be traced to a commit");
    public static readonly Message DistDirtyHelp = Register("dist.dirty.help", "  help: commit first, or pass `{0}` (recorded in {1})");
    public static readonly Message DistExists = Register("dist.exists", "bayan: `{0}` already exists, and a released version never changes");
    public static readonly Message DistExistsHelp = Register("dist.exists.help", "  help: bump `{0}` in {1}, or pass `{2}` to rebuild this one");
    public static readonly Message DistBuilt = Register("dist.built", "built {0} {1} ({2}) for {3} in {4}");
    public static readonly Message DistLatest = Register("dist.latest", "{0} is {1}");
    public static readonly Message CleanNotProject = Register("clean.not-project", "bayan: `{0}` isn't a project (no `{1}`), so it has no {2} to clean");
    public static readonly Message CleanRemoved = Register("clean.removed", "removed {0}");
}
