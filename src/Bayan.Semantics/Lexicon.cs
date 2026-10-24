namespace Bayan.Semantics;

/// <summary>
/// The grammar checker's built-in word list (docs/bayan-grammar.md §7, §13): the words of a member name it knows
/// as verbs, nouns and adjectives (BYN5014). <see cref="Verbs"/> is also R15's list of the verbs a help may start
/// with (<c>HelpLanguageTests</c>, <c>scripts/plan-strings-check.js</c>), so both agree on what a verb is.
/// Inflected forms (<c>-s</c>, <c>-ed</c>, <c>-ing</c>, <c>-er</c>) are derived, not listed.
/// </summary>
public static class Lexicon
{
    public static readonly HashSet<string> Verbs =
    [
        "accept", "access", "acquire", "add", "adjust", "advance", "align", "allocate", "allow", "annotate", "append",
        "apply", "approve", "archive", "ask", "assert", "assign", "attach", "authenticate", "authorize", "await", "begin",
        "bind", "block", "borrow", "break", "bring", "buffer", "build", "bump", "buy", "cache", "calculate", "call",
        "cancel", "capture", "cast", "change", "charge", "check", "choose", "claim", "clamp", "classify", "clean", "clear",
        "clip", "clone", "close", "collect", "combine", "come", "comment", "commit", "compare", "compile", "complete",
        "compose", "compress", "compute", "concat", "configure", "confirm", "connect", "consume", "contain", "convert",
        "copy", "correct", "count", "create", "credit", "cut", "debit", "decide", "declare", "decode", "decompress",
        "decrement", "decrypt", "deduct", "define", "delete", "deliver", "deposit", "dequeue", "derive", "describe",
        "destroy", "detach", "detect", "determine", "disable", "discard", "dispatch", "display", "dispose", "divide", "do",
        "double", "drain", "draw", "drop", "dump", "echo", "edit", "emit", "empty", "enable", "encode", "encrypt", "end",
        "enqueue", "ensure", "enter", "equal", "erase", "escape", "evaluate", "exclude", "execute", "exit", "expand",
        "expect", "explain", "export", "extend", "extract", "fail", "fetch", "fill", "filter", "find", "finish", "fix",
        "flag", "flatten", "flip", "flush", "fold", "follow", "force", "format", "free", "freeze", "fulfil", "fulfill",
        "gather", "generate", "get", "give", "go", "grant", "group", "grow", "guard", "hand", "handle", "hash", "hide",
        "hold", "ignore", "import", "include", "increment", "indent", "index", "init", "initialize", "insert", "inspect",
        "install", "interpolate", "invert", "invoke", "issue", "join", "jump", "keep", "kill", "label", "launch", "lay",
        "leave", "let", "lex", "limit", "link", "list", "listen", "load", "lock", "log", "look", "loop", "lower", "make",
        "map", "mark", "match", "measure", "merge", "migrate", "mix", "move", "multiply", "mutate", "name", "need",
        "negate", "normalize", "note", "notify", "observe", "obtain", "offset", "open", "order", "output", "overwrite",
        "own", "pack", "pad", "page", "paint", "parse", "pass", "paste", "patch", "pause", "pay", "peek", "pick", "place",
        "play", "plot", "poll", "pop", "post", "prefer", "prepare", "prepend", "print", "process", "produce", "promote",
        "propagate", "prune", "publish", "pull", "purchase", "push", "put", "query", "queue", "quit", "quote", "raise",
        "rank", "reach", "read", "rebuild", "receive", "record", "recover", "reduce", "refresh", "refund", "register",
        "reject", "release", "reload", "remove", "rename", "render", "reorder", "repeat", "replace", "reply", "report",
        "request", "require", "reserve", "reset", "resize", "resolve", "restart", "restock", "restore", "resume", "retry",
        "return", "reuse", "reverse", "revert", "rewind", "rewrite", "rotate", "round", "route", "run", "sample", "save",
        "scale", "scan", "schedule", "score", "search", "seed", "seek", "select", "sell", "send", "serialize", "serve",
        "set", "settle", "setup", "shift", "ship", "show", "shrink", "shuffle", "shut", "sign", "simplify", "skip", "sleep",
        "slice", "sort", "spawn", "split", "spread", "square", "stack", "stage", "start", "step", "stop", "store", "stream",
        "strip", "submit", "subscribe", "subtract", "sum", "swap", "switch", "sync", "tag", "take", "tally", "tell", "test",
        "throw", "tick", "toggle", "touch", "trace", "track", "transfer", "transform", "translate", "traverse", "trigger",
        "trim", "truncate", "trust", "tune", "turn", "unlock", "unpack", "unregister", "unsubscribe", "unwrap", "update",
        "upgrade", "upload", "use", "validate", "verify", "visit", "wait", "walk", "warn", "watch", "widen", "withdraw",
        "work", "wrap", "write", "yield", "zip",
    ];

    public static readonly HashSet<string> Nouns =
    [
        "account", "action", "address", "age", "alloc", "amount", "angle", "answer", "app", "area", "arg", "args",
        "argument", "array", "ascii", "average", "back", "balance", "bank", "base", "basket", "batch", "bed", "bidi", "bit",
        "bits", "body", "book", "bound", "bounds", "box", "branch", "brand", "bucket", "budget", "buf", "builder", "bus",
        "byte", "bytes", "capacity", "card", "cart", "case", "ceiling", "cell", "center", "centre", "chain", "channel",
        "char", "chars", "check", "checksum", "child", "children", "circle", "city", "class", "client", "clock", "code",
        "color", "colour", "column", "command", "comment", "config", "connection", "console", "content", "contents",
        "context", "cost", "counter", "country", "cursor", "customer", "data", "date", "day", "db", "deadline", "default",
        "delay", "delta", "depth", "detail", "device", "diff", "digit", "dir", "direction", "discount", "distance", "doc",
        "document", "dollar", "domain", "door", "dot", "draft", "duration", "edge", "effect", "element", "elements",
        "email", "end", "engine", "entries", "entry", "env", "error", "event", "exit", "expr", "expression", "factor",
        "fee", "field", "file", "first", "flag", "floor", "font", "form", "frame", "front", "fund", "game", "gap", "goal",
        "grade", "graph", "grid", "group", "guest", "half", "handler", "hash", "head", "header", "health", "heap", "height",
        "hello", "hex", "history", "host", "hour", "html", "http", "hundred", "icon", "id", "identifier", "image", "inbox",
        "info", "input", "instance", "interest", "inventory", "invoice", "io", "item", "items", "json", "kb", "key", "keys",
        "keyword", "kind", "label", "language", "last", "layer", "layout", "leaf", "left", "length", "letter", "level",
        "limit", "line", "link", "list", "literal", "load", "loan", "local", "location", "lock", "log", "lookup", "loop",
        "magnitude", "mail", "main", "map", "margin", "mask", "match", "max", "member", "memory", "menu", "message", "meta",
        "method", "midpoint", "min", "minus", "minute", "mode", "model", "module", "money", "month", "name", "newline",
        "nfc", "node", "nodes", "note", "null", "number", "object", "offset", "op", "option", "order", "origin", "os",
        "outcome", "output", "owner", "page", "pair", "parent", "part", "party", "password", "path", "pattern", "payload",
        "payment", "peak", "percent", "percentage", "period", "person", "phone", "piece", "pixel", "place", "plan",
        "player", "plus", "point", "pointer", "pool", "port", "position", "post", "power", "prefix", "price", "product",
        "profile", "program", "progress", "project", "property", "quantity", "queue", "range", "rate", "ratio", "reader",
        "reason", "receipt", "record", "rect", "rectangle", "ref", "region", "remainder", "report", "request", "response",
        "rest", "result", "results", "right", "role", "room", "root", "row", "rule", "rune", "runes", "salary", "scale",
        "schema", "scope", "score", "scratch", "screen", "second", "secret", "section", "seed", "segment", "sender",
        "sequence", "server", "service", "session", "set", "shape", "share", "shelf", "side", "sign", "signal", "size",
        "slot", "socket", "source", "space", "span", "speed", "sql", "stack", "stage", "start", "state", "status", "step",
        "stock", "store", "stream", "string", "struct", "style", "subtotal", "suffix", "sum", "summary", "supply", "symbol",
        "system", "table", "tag", "target", "task", "tax", "team", "temp", "term", "test", "text", "thing", "thousand",
        "thread", "threshold", "ticket", "time", "timeout", "timer", "title", "token", "tool", "top", "total", "trace",
        "track", "tree", "trivia", "type", "unit", "url", "user", "utf", "value", "values", "var", "vec", "vector",
        "version", "view", "volume", "wallet", "week", "weight", "width", "window", "word", "work", "world", "year", "zone",
    ];

    public static readonly HashSet<string> Adjectives =
    [
        "active", "actual", "ajar", "all", "any", "available", "average", "big", "blank", "busy", "current", "dark", "dead",
        "deep", "default", "dirty", "due", "early", "empty", "even", "exact", "external", "fast", "final", "fresh", "full",
        "global", "good", "high", "idle", "inner", "internal", "invalid", "large", "late", "lazy", "light", "live", "long",
        "low", "main", "many", "maximum", "minimum", "mutable", "native", "negative", "net", "new", "next", "odd", "old",
        "open", "other", "outer", "own", "paid", "pending", "positive", "previous", "prime", "private", "public", "raw",
        "ready", "real", "red", "safe", "same", "short", "simple", "single", "slow", "small", "stale", "static", "strict",
        "unique", "upper", "valid", "visible", "whole", "zero",
    ];

    /// <summary>Particles (spec §10), the prefixes of derived names, and function words a name may join with.</summary>
    public static readonly HashSet<string> Particles = ["from", "to", "until", "through", "at", "by", "for", "as"];

    public static readonly HashSet<string> FunctionWords =
    [
        "is", "has", "can", "try", "into", "with", "and", "or", "of", "in", "on", "off", "out", "up", "down", "over", "per",
        "no", "not", "each", "every", "some", "none", "ok", "if", "else", "then", "also", "only", "again", "once", "self",
        "twice", "before", "after",
    ];

    /// <summary>Math vocabulary: a naming exemption, not a synonym (HANDOFF round 3 §12, J70). There is no <c>log</c> member.</summary>
    public static readonly HashSet<string> Math =
    [
        "abs", "sign", "sqrt", "cbrt", "pow", "exp", "ln", "log2", "log10", "sin", "cos", "tan", "asin", "acos", "atan2",
        "radians", "degrees",
    ];
}
