/* Bayan runtime: the C support every compiled program links against.
 *
 * Panics, printing, formatting, checked arithmetic, bounds checks, allocators, lists.
 * Overflow, narrowing casts, bounds, division by zero and invalid chars are checked
 * in every mode; only +% -% *% wrap and +| -| *| saturate. */
#ifndef BAYAN_RT_H
#define BAYAN_RT_H

/* Bumped on every change to the runtime source. */
#define BYN_RUNTIME_VERSION 1

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#if defined(__GNUC__) || defined(__clang__)
#define BYN_NORETURN __attribute__((noreturn))
#define BYN_COLD __attribute__((cold))
#define BYN_UNLIKELY(x) __builtin_expect(!!(x), 0)
#else
#define BYN_NORETURN _Noreturn
#define BYN_COLD
#define BYN_UNLIKELY(x) (x)
#endif

/* ── Strings ─────────────────────────────────────────────────────────────── */

/* Valid UTF-8, not NUL-terminated. Literals point into static storage. */
typedef struct byn_str {
    const uint8_t* ptr;
    int64_t len;
} byn_str;

#define BYN_STR(lit, n) ((byn_str){(const uint8_t*)(lit), (n)})

/* ── Panics ──────────────────────────────────────────────────────────────── */

BYN_NORETURN void byn_panic(const char* msg, const char* file, int line);
BYN_NORETURN void byn_panic_str(byn_str msg, const char* file, int line);
/* `todo(msg)`: prints `pending: msg` and exits with 4, which the test runner reports as pending. */
BYN_NORETURN void byn_todo_str(byn_str msg, const char* file, int line);
BYN_NORETURN void byn_unreachable(const char* file, int line);

/* One cold trap per kind (J89), called after a single compare, so the hot path is only the test. */
static inline BYN_NORETURN BYN_COLD void bayan_trap_bounds(const char* file, int line) { byn_panic("index out of range", file, line); }
static inline BYN_NORETURN BYN_COLD void bayan_trap_slice(const char* file, int line) { byn_panic("slice out of range", file, line); }
static inline BYN_NORETURN BYN_COLD void bayan_trap_overflow(const char* file, int line) { byn_panic("arithmetic overflow", file, line); }
static inline BYN_NORETURN BYN_COLD void bayan_trap_range(const char* file, int line) { byn_panic("value out of range for the target type", file, line); }
static inline BYN_NORETURN BYN_COLD void bayan_trap_divide(const char* file, int line) { byn_panic("division by zero", file, line); }
static inline BYN_NORETURN BYN_COLD void bayan_trap_scalar(const char* file, int line) { byn_panic("value is not a Unicode scalar", file, line); }

/* ── Panic handlers ──────────────────────────────────────────────────────── */

/* What a `defer panicking` handler and `Panic.onPanic` receive. The message stays valid until the process aborts. */
typedef struct byn_panic_info {
    byn_str message;
} byn_panic_info;

/* A `defer panicking` handler: a record on the thread's list, pushed where its scope declares it and popped as the scope
 * ends. On a panic the runtime unlinks the innermost record and longjmps into its frame, where the handler runs and calls
 * byn_panic_resume for the next one. Plain `defer` and `defer failing` never run on a panic. wasm32-wasi has no
 * setjmp, so there handlers are registered but never run. */
#ifdef __wasi__
typedef struct byn_panic_handler {
    struct byn_panic_handler* prev;
} byn_panic_handler;
#define BYN_PANIC_CATCH(h) 0
#else
#include <setjmp.h>
typedef struct byn_panic_handler {
    struct byn_panic_handler* prev;
    jmp_buf jump;
} byn_panic_handler;
#define BYN_PANIC_CATCH(h) setjmp((h)->jump)
#endif

extern _Thread_local byn_panic_handler* byn_panic_handlers;
/* Set by `Panic.onPanic = f;`, the first statement of `main`; runs after the handlers and before the trace. */
extern void (*byn_on_panic)(byn_panic_info);
/* Null unless an instrumented build sets it; runs once, just before a panic aborts. */
extern void (*byn_cov_flush)(void);

static inline void byn_panic_push(byn_panic_handler* h) {
    h->prev = byn_panic_handlers;
    byn_panic_handlers = h;
}

static inline void byn_panic_pop(byn_panic_handler* h) { byn_panic_handlers = h->prev; }

/* The panic in progress, for a handler's `(p)`. */
byn_panic_info byn_panic_current(void);
/* Ends a handler: runs the next one, then `Panic.onPanic`, then prints the trace and aborts. */
BYN_NORETURN void byn_panic_resume(void);

/* ── Ownership checker (debug builds) ────────────────────────────────────── */

/* Debug programs define BYN_DEBUG before including this header; safe and fast ones never see these. bayan-rt.c
 * compiles the checker unless built with -DBYN_NDEBUG (the toolchain passes it for safe and fast builds). */

#ifdef BYN_DEBUG
/* Where a local was moved out; line 0 while it holds a value. */
typedef struct byn_own_slot {
    const char* file;
    int line;
} byn_own_slot;

/* Records `place` as the owner of `storage` (a List's elements, a Map's keys, a growable StrBuf's bytes, an
 * Owned box); panics when another place already owns it. NULL storage is never recorded. */
void byn_own_claim(const void* storage, const void* place, const char* type, const char* file, int line);
/* `storage` leaves its owner (a move or a return); the next claim takes it. */
void byn_own_release(const void* storage);
/* As byn_own_release, but only if `place` owns it (the place is overwritten). */
void byn_own_drop(const void* storage, const void* place);
/* Panics when the local behind `slot` was moved out. */
void byn_own_read(const byn_own_slot* slot, const char* type, const char* file, int line);
/* Storage grew from `from` into `to`. */
void byn_own_rekey(const void* from, const void* to);
/* Storage in [lo, hi) was freed. */
void byn_own_forget(const void* lo, const void* hi);
#endif

/* ── Output and formatting ───────────────────────────────────────────────── */

typedef enum byn_writer_mode {
    BYN_WRITE_STDOUT,   /* flush to standard output when full */
    BYN_WRITE_STDERR,   /* flush to standard error when full (print(…, to: .stderr)) */
    BYN_WRITE_TRUNCATE, /* drop what does not fit (panic messages) */
    BYN_WRITE_STRICT,   /* panic when full (StrBuf(buf)) */
    BYN_WRITE_GROW,     /* grow through alloc when full (StrBuf()) */
} byn_writer_mode;

typedef struct byn_alloc byn_alloc;

/* Formats text into caller-provided bytes; only BYN_WRITE_GROW allocates. */
typedef struct byn_writer {
    uint8_t* buf;
    int64_t cap;
    int64_t len;
    byn_writer_mode mode;
    const char* file;
    int line;
    byn_alloc* alloc;
} byn_writer;

void byn_w_bytes(byn_writer* w, const uint8_t* p, int64_t n);
void byn_w_str(byn_writer* w, byn_str s);
void byn_w_i64(byn_writer* w, int64_t v);
void byn_w_u64(byn_writer* w, uint64_t v);
void byn_w_f64(byn_writer* w, double v);
void byn_w_f32(byn_writer* w, float v);
void byn_w_bool(byn_writer* w, bool v);
void byn_w_char(byn_writer* w, uint32_t c);
/* Ends a print: appends a newline and flushes the buffer to standard output, or to standard error. */
void byn_w_println(byn_writer* w);

/* The built-in Stream: stdout, stderr, in that order. */
typedef int32_t byn_builtin_Stream;
/* `print`: the text and a newline, buffered on standard output; on standard error after flushing standard output. */
void byn_print(byn_builtin_Stream stream, byn_str s);
void byn_stdout_flush(void);
/* Reports a failed `main` or test: flushes output and writes `error: <summary>` to standard error. */
void byn_report_error(byn_str summary);
/* The built-in LogLevel: debug, info, warn, error, in that order. */
typedef int32_t byn_builtin_LogLevel;
/* `log`: flushes output and writes `<level>: <text>` to standard error, control characters escaped. */
void byn_log(byn_builtin_LogLevel level, byn_str text);

/* ── Strings ─────────────────────────────────────────────────────────────── */

bool byn_str_eq(byn_str a, byn_str b);
int byn_str_cmp(byn_str a, byn_str b);
bool byn_str_starts_with(byn_str s, byn_str prefix);
bool byn_str_ends_with(byn_str s, byn_str suffix);
bool byn_str_contains(byn_str s, byn_str text);
/* `s.runeCount`: the number of code points, O(n). */
int64_t byn_str_rune_count(byn_str s);
/* `s.isAscii`: every byte is below 0x80. */
bool byn_str_is_ascii(byn_str s);
/* The next code point at *pos (valid UTF-8 assumed), advancing *pos. */
uint32_t byn_str_next(byn_str s, int64_t* pos);
/* s[start..end] by byte offset; panics out of range or mid-code-point. */
byn_str byn_str_slice(byn_str s, int64_t start, int64_t end, const char* file, int line);
/* `s.position(x, from: i)`: the byte position of the first x at or after i, or -1; an empty x is found at i. */
int64_t byn_str_position(byn_str s, byn_str x, int64_t from, const char* file, int line);
/* `s.lastPosition(x)`: the byte position of the last x, or -1; an empty x is found at s.len. */
int64_t byn_str_last_position(byn_str s, byn_str x);
/* `s.count(x)`: non-overlapping occurrences of x; 0 for an empty x. */
int64_t byn_str_count(byn_str s, byn_str x);
/* `s.trimmed(from:)` without ASCII whitespace at the start (edge 0), the end (edge 1) or both (edge 2). A view of s. */
byn_str byn_str_trimmed(byn_str s, int32_t edge);
/* `s.trimmed(by: runes)` without the given code points at both ends. A view of s. */
byn_str byn_str_trimmed_by(byn_str s, const uint32_t* runes, int64_t n);
/* `s.lowercased()` / `s.uppercased()`: each code point's Unicode simple case mapping (revision 0015), into a new string. */
byn_str byn_str_case(byn_str s, bool upper, byn_alloc* to);
/* `s.equals(t, as: .caseless)`: equal after ASCII case folding. */
bool byn_str_eq_caseless(byn_str a, byn_str b);
/* `s.replaced(x, by: y)`: every non-overlapping x replaced; an empty x panics. */
byn_str byn_str_replaced(byn_str s, byn_str x, byn_str y, byn_alloc* to, const char* file, int line);
/* `s.repeated(n)`: n copies of s; a negative n panics. */
byn_str byn_str_repeated(byn_str s, int64_t n, byn_alloc* to, const char* file, int line);
/* `xs.joined(by: sep)` on a span of strings. */
byn_str byn_str_joined(const byn_str* xs, int64_t n, byn_str sep, byn_alloc* to);
/* `s.runes.take(n)`: the first n code points of s (all of s when it has fewer). A view of s. */
byn_str byn_str_take_runes(byn_str s, int64_t n, const char* file, int line);
/* `s.runes.skip(n)` / `s.runes.takeLast(n)`: s after its first n code points, or its last n. Views of s. */
byn_str byn_str_skip_runes(byn_str s, int64_t n, const char* file, int line);
byn_str byn_str_take_last_runes(byn_str s, int64_t n, const char* file, int line);
/* `s.padded(to: n, by: c)`: s with c appended until it has n code points; s copied when it already has. */
byn_str byn_str_padded(byn_str s, int64_t n, uint32_t c, byn_alloc* to, const char* file, int line);
/* `string(bytes)`: the bytes formatted as a list, `[104, 105]`. */
byn_str byn_bytes_string(const uint8_t* p, int64_t n, byn_alloc* to);

/* The views `s.runes`, `s.utf16`, `s.lines` and `s.parts(by:, limit:)`, walked by `for` with a byte cursor.
   `pending` holds a UTF-16 low surrogate not yet returned; `limit` counts down the parts still allowed (0: no limit).
   A `rune` separator is kept encoded in `rune`, with `sep.ptr` NULL and `sep.len` its length. */
typedef struct byn_text_view {
    byn_str s;
    byn_str sep;
    int64_t limit;
    uint16_t pending;
    uint8_t rune[4];
} byn_text_view;
/* Whether the view has another item at pos; `parts` marks its end with pos -1. */
bool byn_view_more(byn_text_view v, int64_t pos, int32_t kind);
/* `s.parts(by: sep, limit: n)`; an empty sep or a negative limit panics. */
byn_text_view byn_parts(byn_str s, byn_str sep, int64_t limit, const char* file, int line);
/* `s.parts(by: c, limit: n)` with a `rune` separator; a negative limit panics. */
byn_text_view byn_parts_rune(byn_str s, uint32_t c, int64_t limit, const char* file, int line);
uint16_t byn_utf16_next(byn_text_view* v, int64_t* pos);
byn_str byn_lines_next(byn_text_view* v, int64_t* pos);
byn_str byn_parts_next(byn_text_view* v, int64_t* pos);
/* `s.utf16.copy()`: the UTF-16 code units of s; ptr from `to`, *len set. */
uint16_t* byn_utf16_copy(byn_str s, int64_t* len, byn_alloc* to);

/* ── Bounds ──────────────────────────────────────────────────────────────── */

static inline int64_t byn_index(int64_t i, int64_t len, const char* file, int line) {
    if (BYN_UNLIKELY((uint64_t)i >= (uint64_t)len)) bayan_trap_bounds(file, line);
    return i;
}

/* Validates a slice [start, end) of a sequence of length len. */
static inline void byn_slice_check(int64_t start, int64_t end, int64_t len, const char* file, int line) {
    if (BYN_UNLIKELY(start < 0 || end < start || end > len)) bayan_trap_slice(file, line);
}

/* Resolves slice bounds: a mode of 0 is an offset from the start, 1 from the end, 2 absent. */
static inline void byn_slice_range(int64_t len, int64_t start, int start_mode, int64_t end, int end_mode, bool inclusive,
                                   const char* file, int line, int64_t* from, int64_t* to) {
    int64_t s = start_mode == 2 ? 0 : start_mode == 1 ? len - start : start;
    int64_t e = end_mode == 2 ? len : end_mode == 1 ? len - end : end;
    if (inclusive && end_mode != 2) e += 1;
    byn_slice_check(s, e, len, file, line);
    *from = s;
    *to = e;
}

/* `xs.sort()`: a stable sort of n elements of size bytes in place, without allocating; less(a, b) orders them. */
void byn_sort(void* p, int64_t n, int64_t size, bool (*less)(const void* a, const void* b));

/* ── Allocators ──────────────────────────────────────────────────────────── */

struct byn_alloc {
    /* Grows (or allocates, when old is NULL) a block; the old block's bytes are kept. */
    void* (*resize)(byn_alloc* self, void* old, int64_t old_size, int64_t new_size);
};

/* The default allocator for `main` (malloc/realloc; never freed in v0.1). */
extern byn_alloc byn_general;
/* `context.allocator`; set by `context (allocator: …)`. */
extern byn_alloc* byn_context;
/* Frees a scratch block taken from `from` where it can: General's; an arena's stays until reset. */
void byn_release(byn_alloc* from, void* p);

typedef struct byn_arena byn_arena;
byn_arena* byn_arena_new(void);
void byn_arena_reset(byn_arena* a);
void byn_arena_free(byn_arena* a);
byn_alloc* byn_arena_alloc(byn_arena* a);

byn_str byn_str_copy(byn_str s, byn_alloc* to);
void* byn_copy_bytes(const void* p, int64_t n, byn_alloc* to);
/* The builtin `enum Radix` of `string(n, as:)`: 0 binary, 1 octal, 2 decimal, 3 hex. */
typedef int32_t byn_builtin_Radix;
/* The builtin `enum Edge` of `s.trimmed(from:)`: 0 start, 1 end. */
typedef int32_t byn_builtin_Edge;
/* The builtin `enum Align` of a `FormatSpec`: 0 start, 1 end, 2 center. */
typedef int32_t byn_builtin_Align;
/* `FormatSpec`: a hole's specs as `formatInto(ref StrBuf, as FormatSpec)` receives them; `digits` is -1 for none. */
typedef struct byn_format_spec {
    byn_builtin_Radix radix;
    bool debug;
    int64_t width;
    uint32_t fill;
    byn_builtin_Align align;
    int64_t digits;
} byn_format_spec;
/* The builtin `enum Comparison` of `s.equals(t, as:)`: 0 ordinal, 1 caseless. */
typedef int32_t byn_builtin_Comparison;
/* v in radix, lowercase, no prefix or leading zeros. Decimal prints a signed v with its sign;
   the others print v's two's-complement bits at the type's width. */
byn_str byn_int_string(int64_t v, int32_t bits, bool is_signed, byn_builtin_Radix radix, byn_alloc* to);
/* `T.tryParse(text, as: radix)`: digits only, no prefix, sign or spaces, either letter case. Decimal takes a leading `-` on a
   signed type and checks the range; the other radixes read the type's bits, as `string(n, as:)` prints them. False when invalid. */
bool byn_int_parse(byn_str s, int32_t bits, bool is_signed, byn_builtin_Radix radix, int64_t* out);
/* `T.parse(text, as: radix)`: as `byn_int_parse`; -1 when it parses, -2 on overflow, otherwise the offset of the first bad
 * digit (the length when a digit is missing). */
int64_t byn_int_parse_at(byn_str s, int32_t bits, bool is_signed, byn_builtin_Radix radix, int64_t* out);
/* `f64.tryParse(text)` / `f32.tryParse(text)` (single): the float literal form without `_`, a leading `-`, digits, then an
   optional `.` and digits and an optional `e`/`E`, sign and digits; no spaces. Correctly rounded; ±∞ on overflow. False when invalid. */
bool byn_float_parse(byn_str s, bool single, double* out);
/* Two lowercase hex digits per byte (`Hex.encode`). */
byn_str byn_hex_encode(const uint8_t* p, int64_t n, byn_alloc* to);
/* `Hex.decode`: -1 and the bytes in `*out`, or the offset of the first non-hex digit (the length for an odd count). */
int64_t byn_hex_decode(byn_str s, uint8_t** out, byn_alloc* to);
/* `Path.isRooted`: .NET `Path.IsPathRooted` on this OS (on Windows a leading `\` or `/`, or a drive letter and `:`). */
bool byn_path_is_rooted(byn_str p);
/* `Path.fileName`: .NET `Path.GetFileName` on this OS, copied into `to`. */
byn_str byn_path_file_name(byn_str p, byn_alloc* to);
/* `Path.directoryName`: .NET `Path.GetDirectoryName` on this OS, copied into `*out` (runs of separators collapsed, and on Windows
   each one `\`). False, where .NET returns null, for an empty path (on Windows a blank one) or a root. */
bool byn_path_directory_name(byn_str p, byn_str* out, byn_alloc* to);
/* `Path.combine(a, b)`: .NET `Path.Combine(a, b)` on this OS, copied into `to`: `b` when `a` is empty or `b` rooted, else `a`
   and `b` joined by the OS separator unless `a` ends in a separator (any of them, on Windows) or `b` is empty. */
byn_str byn_path_combine(byn_str a, byn_str b, byn_alloc* to);
/* `Path.combine(paths)`: .NET `Path.Combine(params string[])` on this OS, copied into `to`: empty parts are skipped and the
   last rooted part restarts the result. */
byn_str byn_path_combine_span(const byn_str* p, int64_t n, byn_alloc* to);
/* `Path.relative(from, to)`: .NET `Path.GetRelativePath(from, to)` on this OS, copied into `to`; both paths are made full against
   the working directory first. Panics on an empty path or one with a NUL byte, where .NET throws `ArgumentException`. */
byn_str byn_path_relative(byn_str from, byn_str path, byn_alloc* to, const char* file, int line);

/* Makes room for `need` elements: grows *ptr (capacity *cap) through alloc. */
void byn_list_reserve(void** ptr, int64_t* cap, int64_t need, int64_t elem_size, byn_alloc* alloc);

/* ── Maps ────────────────────────────────────────────────────────────────── */

/* Seeds every key hash; `main` sets it (0 under `--test`, else time xor pid). */
extern uint64_t byn_hash_seed;
void byn_hash_seed_init(bool fixed);
/* FNV-1a 64 over n bytes, starting from the seed. */
uint64_t byn_hash_bytes(const void* p, int64_t n);

/* The untyped part of `Map<K, V>`: entries are dense arrays in insertion order (keys and values live in the
 * typed struct), removed entries are tombstones until a rehash compacts them, and `index` is an open-addressed
 * table of entry numbers (-1 empty) with `icap` slots, a power of two at least twice `cap`. */
typedef struct byn_map_hdr {
    int32_t* index;
    uint64_t* hashes;
    bool* dead;
    int64_t icap;
    int64_t cap;
    int64_t len;
    int64_t live;
    byn_alloc* alloc;
} byn_map_hdr;

/* Compares a stored key with a probe key (both point at a K). */
typedef bool (*byn_key_eq)(const void* a, const void* b);

/* The entry holding `key`, or -1. */
int64_t byn_map_find(const byn_map_hdr* h, const void* keys, int64_t ksize, const void* key, uint64_t hash, byn_key_eq eq);
/* The entry for `key`; a new one is appended (*added set) and the caller stores the key. */
int64_t byn_map_insert(byn_map_hdr* h, void** keys, int64_t ksize, void** vals, int64_t vsize, const void* key,
                       uint64_t hash, byn_key_eq eq, bool* added);
bool byn_map_remove(byn_map_hdr* h, const void* keys, int64_t ksize, const void* key, uint64_t hash, byn_key_eq eq);
void byn_map_clear(byn_map_hdr* h);
/* Copies the live entries of `src` in insertion order into fresh storage from `to`; `dst` gets the new header. */
void byn_map_copy(const byn_map_hdr* src, const void* keys, int64_t ksize, void** dkeys, const void* vals, int64_t vsize,
                  void** dvals, byn_map_hdr* dst, byn_alloc* to);

/* Folds `part` into the hash `h` (FNV-1a over its 8 bytes), for keys hashed part by part. */
uint64_t byn_hash_mix(uint64_t h, uint64_t part);

/* ── Symbols ─────────────────────────────────────────────────────────────── */

/* `Symbol`: an index into the program's one symbol table. */
typedef struct byn_sym {
    uint32_t id;
} byn_sym;

/* Starts the table with the program's literal texts (sorted, `""` first), so literal i is ID i; `main` calls it once. */
void byn_sym_init(const byn_str* lits, uint32_t n);
/* `Symbol.intern`: the ID of `s`, adding a copy of a new text; the table and its text come from the general allocator and
 * are never freed. */
byn_sym byn_sym_intern(byn_str s);
/* `sym.text`: valid until the program exits. */
byn_str byn_sym_text(byn_sym s);

/* ── Program entry and Io ────────────────────────────────────────────────── */

/* True if the bytes are well-formed UTF-8 (no overlongs, surrogates or values above U+10FFFF). */
bool byn_utf8_valid(const uint8_t* p, int64_t n);
/* Byte offset of the first invalid sequence (its first byte), or -1 when the bytes are well-formed UTF-8. */
int64_t byn_utf8_invalid_at(const uint8_t* p, int64_t n);
/* `Utf8.decode(bytes, invalid: .replace)`: a copy with each invalid sequence as U+FFFD. */
byn_str byn_utf8_decode_replace(const uint8_t* p, int64_t n, byn_alloc* to);

/* argv[1..argc-1] as strings. Writes `error: argument N is not valid UTF-8` (N from 1) and exits 1 on bad input.
 * On Windows they come from the UTF-16 command line instead of `argv`, and `*argc` is set to their count. */
byn_str* byn_args(int* argc, char** argv);

/* `IoError` codes: the compiler numbers its variants first. */
#define BYN_IO_NOT_FOUND 1u
#define BYN_IO_PERMISSION_DENIED 2u
#define BYN_IO_INVALID_UTF8 3u
#define BYN_IO_NOT_A_DIRECTORY 4u
#define BYN_IO_OTHER 5u
#define BYN_IO_INVALID_INPUT 6u
#define BYN_IO_UNSUPPORTED 7u

/* Reads a whole file into memory from `alloc`. Returns 0 or an `IoError` code. */
uint32_t byn_io_read(byn_str path, uint8_t** ptr, int64_t* len, byn_alloc* alloc);
/* As byn_io_read, and the bytes must be UTF-8. `out` may be NULL. */
uint32_t byn_io_read_text(byn_str path, byn_str* out, byn_alloc* alloc);
/* `Io.stdin.readAll`: the rest of standard input, bytes as-is (not checked for UTF-8), from `alloc`. */
uint32_t byn_io_read_stdin(byn_str* out, byn_alloc* alloc);
uint32_t byn_io_write_file(byn_str path, const uint8_t* ptr, int64_t len);
/* `Io.listDir`: entry names without `.` and `..`, sorted by byte order, allocated from `alloc`. */
uint32_t byn_io_list_dir(byn_str path, byn_str** ptr, int64_t* len, byn_alloc* alloc);

/* `ProcessResult`: a child killed by a signal reports 128 + the signal number. */
typedef struct byn_process {
    int32_t exit_code;
    byn_str out;
    byn_str err;
} byn_process;

/* `Io.run`: spawns `program` (looked up on PATH when it has no path separator) with `args`,
 * never through a shell, and waits for it. Both streams are captured into `alloc`. `out` may be NULL.
 * On Windows a batch file (`.bat`, `.cmd`) is refused with `permissionDenied`, since cmd.exe would re-parse the arguments. */
uint32_t byn_io_run(byn_str program, const byn_str* args, int64_t nargs, byn_process* out, byn_alloc* alloc);
/* `Io.runBatch`'s command line: `cmd.exe /d /e:ON /v:OFF /s /c ""<path>" "<a1>" … "<an>""`, each argument
 * in double quotes as-is. False, with nothing built, when the path or an argument holds NUL, CR, LF, `"` or `%`,
 * or the path does not end in `.bat` or `.cmd` (any case). Otherwise `out` is from malloc. Compiled on every host. */
bool byn_batch_cmdline(byn_str path, const byn_str* args, int64_t nargs, byn_str* out);
/* `Io.runBatch`: runs the batch file through the system directory's cmd.exe with that command line and
 * captures it like byn_io_run. A refused argument is `invalidInput` (nothing starts); off Windows, `unsupported`. */
uint32_t byn_io_run_batch(byn_str path, const byn_str* args, int64_t nargs, byn_process* out, byn_alloc* alloc);
/* `Io.stdout.write` (fd 1, buffered with print) and `Io.stderr.write` (fd 2). No newline. */
void byn_io_write(int32_t fd, byn_str s);
int64_t byn_io_monotonic_nanos(void);

/* ── StrBuf ──────────────────────────────────────────────────────────────── */

/* `StrBuf(buf)` is a strict writer over caller-provided bytes; `StrBuf()` grows from `alloc`. */
static inline byn_str byn_strbuf_str(byn_writer w) { return (byn_str){w.buf, w.len}; }
/* A growable StrBuf holding the same text in fresh storage from `to`. */
byn_writer byn_strbuf_copy(byn_writer w, byn_alloc* to);
static inline byn_writer byn_strbuf_grow(byn_alloc* alloc) {
    return (byn_writer){NULL, 0, 0, BYN_WRITE_GROW, NULL, 0, alloc};
}

/* ── Chars ───────────────────────────────────────────────────────────────── */

static inline uint32_t byn_to_char(int64_t v, const char* file, int line) {
    if (BYN_UNLIKELY(v < 0 || v > 0x10FFFF || (v >= 0xD800 && v <= 0xDFFF)))
        bayan_trap_scalar(file, line);
    return (uint32_t)v;
}

static inline uint32_t byn_to_char_u(uint64_t v, const char* file, int line) {
    return byn_to_char(v > 0x10FFFF ? -1 : (int64_t)v, file, line);
}

/* ── Checked arithmetic ──────────────────────────────────────────────────── */

#define BYN_INT_TYPES(X) \
    X(i8, int8_t, 1) X(i16, int16_t, 1) X(i32, int32_t, 1) X(i64, int64_t, 1) \
    X(u8, uint8_t, 0) X(u16, uint16_t, 0) X(u32, uint32_t, 0) X(u64, uint64_t, 0)

#define BYN_BITS(T) ((int)(sizeof(T) * 8))
#define BYN_MIN(T, S) ((S) ? (T)((uint64_t)1 << (BYN_BITS(T) - 1)) : (T)0)
#define BYN_MAX(T, S) ((S) ? (T)(((uint64_t)1 << (BYN_BITS(T) - 1)) - 1) : (T)~(T)0)

#define BYN_DEF_TRAPPING(N, T, S) \
    static inline T byn_add_##N(T a, T b, const char* f, int l) { \
        T r; if (BYN_UNLIKELY(__builtin_add_overflow(a, b, &r))) bayan_trap_overflow(f, l); return r; } \
    static inline T byn_sub_##N(T a, T b, const char* f, int l) { \
        T r; if (BYN_UNLIKELY(__builtin_sub_overflow(a, b, &r))) bayan_trap_overflow(f, l); return r; } \
    static inline T byn_mul_##N(T a, T b, const char* f, int l) { \
        T r; if (BYN_UNLIKELY(__builtin_mul_overflow(a, b, &r))) bayan_trap_overflow(f, l); return r; } \
    static inline T byn_neg_##N(T a, const char* f, int l) { \
        T r; if (BYN_UNLIKELY(__builtin_sub_overflow((T)0, a, &r))) bayan_trap_overflow(f, l); return r; } \
    static inline T byn_inc_##N(T a, const char* f, int l) { return byn_add_##N(a, (T)1, f, l); } \
    static inline T byn_dec_##N(T a, const char* f, int l) { return byn_sub_##N(a, (T)1, f, l); } \
    static inline T byn_cast_##N##_i(int64_t v, const char* f, int l) { \
        T r = (T)v; if (BYN_UNLIKELY((int64_t)r != v || (r < (T)1) != (v < 1))) bayan_trap_range(f, l); return r; } \
    static inline T byn_cast_##N##_u(uint64_t v, const char* f, int l) { \
        T r = (T)v; if (BYN_UNLIKELY((uint64_t)r != v || r < (T)0)) bayan_trap_range(f, l); return r; }

#define BYN_DEF_ARITH(N, T, S) \
    BYN_DEF_TRAPPING(N, T, S) \
    static inline T byn_div_##N(T a, T b, const char* f, int l) { \
        if (BYN_UNLIKELY(b == 0)) bayan_trap_divide(f, l); \
        if ((S) && BYN_UNLIKELY(a == BYN_MIN(T, S) && b == (T)-1)) return byn_neg_##N(a, f, l); \
        return (T)(a / b); } \
    static inline T byn_rem_##N(T a, T b, const char* f, int l) { \
        if (BYN_UNLIKELY(b == 0)) bayan_trap_divide(f, l); \
        if ((S) && b == (T)-1) return 0; \
        return (T)(a % b); } \
    static inline T byn_addw_##N(T a, T b) { return (T)((uint64_t)a + (uint64_t)b); } \
    static inline T byn_subw_##N(T a, T b) { return (T)((uint64_t)a - (uint64_t)b); } \
    static inline T byn_mulw_##N(T a, T b) { return (T)((uint64_t)a * (uint64_t)b); } \
    static inline T byn_adds_##N(T a, T b) { \
        T r; if (!__builtin_add_overflow(a, b, &r)) return r; return (S) && b < (T)1 ? BYN_MIN(T, S) : BYN_MAX(T, S); } \
    static inline T byn_subs_##N(T a, T b) { \
        T r; if (!__builtin_sub_overflow(a, b, &r)) return r; return (S) ? (b < (T)1 ? BYN_MAX(T, S) : BYN_MIN(T, S)) : (T)0; } \
    static inline T byn_muls_##N(T a, T b) { \
        T r; if (!__builtin_mul_overflow(a, b, &r)) return r; return (S) && ((a < (T)1) != (b < (T)1)) ? BYN_MIN(T, S) : BYN_MAX(T, S); } \
    static inline T byn_shl_##N(T a, int64_t b) { return (T)((uint64_t)a << (b & (BYN_BITS(T) - 1))); } \
    static inline T byn_shr_##N(T a, int64_t b) { return (T)(a >> (b & (BYN_BITS(T) - 1))); } \
    static inline T byn_f2i_##N(double v) { \
        if (v != v) return 0; \
        if (v <= (double)BYN_MIN(T, S)) return BYN_MIN(T, S); \
        if (v >= (double)BYN_MAX(T, S)) return BYN_MAX(T, S); \
        return (T)v; }

BYN_INT_TYPES(BYN_DEF_ARITH)

#endif
