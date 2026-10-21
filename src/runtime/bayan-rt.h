/* Bayan runtime: the C support every compiled program links against.
 *
 * Panics, printing, formatting, checked arithmetic, bounds checks, allocators, lists.
 * Define BYN_FAST to make + - * and narrowing casts wrap instead of trapping
 * (bounds, division by zero and invalid chars are checked in every mode). */
#ifndef BAYAN_RT_H
#define BAYAN_RT_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#if defined(__GNUC__) || defined(__clang__)
#define BYN_NORETURN __attribute__((noreturn))
#define BYN_UNLIKELY(x) __builtin_expect(!!(x), 0)
#else
#define BYN_NORETURN _Noreturn
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
BYN_NORETURN void byn_unreachable(const char* file, int line);

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
/* Ends a print: appends a newline and flushes the buffer to standard output. */
void byn_w_println(byn_writer* w);

void byn_print(byn_str s);
void byn_stdout_flush(void);
/* Reports a failed `main` or test: flushes output and writes `error: <summary>` to standard error. */
void byn_report_error(byn_str summary);

/* ── Strings ─────────────────────────────────────────────────────────────── */

bool byn_str_eq(byn_str a, byn_str b);
int byn_str_cmp(byn_str a, byn_str b);
bool byn_str_starts_with(byn_str s, byn_str prefix);
bool byn_str_ends_with(byn_str s, byn_str suffix);
bool byn_str_contains(byn_str s, byn_str text);
/* The next code point at *pos (valid UTF-8 assumed), advancing *pos. */
uint32_t byn_str_next(byn_str s, int64_t* pos);
/* s[start..end] by byte offset; panics out of range or mid-code-point. */
byn_str byn_str_slice(byn_str s, int64_t start, int64_t end, const char* file, int line);

/* ── Bounds ──────────────────────────────────────────────────────────────── */

static inline int64_t byn_index(int64_t i, int64_t len, const char* file, int line) {
    if (BYN_UNLIKELY((uint64_t)i >= (uint64_t)len)) byn_panic("index out of range", file, line);
    return i;
}

/* Validates a slice [start, end) of a sequence of length len. */
static inline void byn_slice_check(int64_t start, int64_t end, int64_t len, const char* file, int line) {
    if (BYN_UNLIKELY(start < 0 || end < start || end > len)) byn_panic("slice out of range", file, line);
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

/* ── Allocators ──────────────────────────────────────────────────────────── */

struct byn_alloc {
    /* Grows (or allocates, when old is NULL) a block; the old block's bytes are kept. */
    void* (*resize)(byn_alloc* self, void* old, int64_t old_size, int64_t new_size);
};

/* The default allocator for `main` (malloc/realloc; never freed in v0.1). */
extern byn_alloc byn_general;
/* `context.allocator`; set by `context (allocator: …)`. */
extern byn_alloc* byn_context;

typedef struct byn_arena byn_arena;
byn_arena* byn_arena_new(void);
void byn_arena_reset(byn_arena* a);
void byn_arena_free(byn_arena* a);
byn_alloc* byn_arena_alloc(byn_arena* a);

byn_str byn_str_copy(byn_str s, byn_alloc* to);
void* byn_copy_bytes(const void* p, int64_t n, byn_alloc* to);
/* Lowercase hexadecimal digits of v, no prefix or leading zeros. */
byn_str byn_to_hex(uint64_t v, byn_alloc* to);

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

/* ── Program entry and Io ────────────────────────────────────────────────── */

/* True if the bytes are well-formed UTF-8 (no overlongs, surrogates or values above U+10FFFF). */
bool byn_utf8_valid(const uint8_t* p, int64_t n);
/* Byte offset of the first invalid sequence (its first byte), or -1 when the bytes are well-formed UTF-8. */
int64_t byn_utf8_invalid_at(const uint8_t* p, int64_t n);

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
        byn_panic("value is not a Unicode scalar", file, line);
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

#ifdef BYN_FAST
#define BYN_DEF_TRAPPING(N, T, S) \
    static inline T byn_add_##N(T a, T b, const char* f, int l) { (void)f; (void)l; return (T)((uint64_t)a + (uint64_t)b); } \
    static inline T byn_sub_##N(T a, T b, const char* f, int l) { (void)f; (void)l; return (T)((uint64_t)a - (uint64_t)b); } \
    static inline T byn_mul_##N(T a, T b, const char* f, int l) { (void)f; (void)l; return (T)((uint64_t)a * (uint64_t)b); } \
    static inline T byn_neg_##N(T a, const char* f, int l) { (void)f; (void)l; return (T)(0 - (uint64_t)a); } \
    static inline T byn_inc_##N(T a, const char* f, int l) { (void)f; (void)l; return (T)((uint64_t)a + 1); } \
    static inline T byn_dec_##N(T a, const char* f, int l) { (void)f; (void)l; return (T)((uint64_t)a - 1); } \
    static inline T byn_cast_##N##_i(int64_t v, const char* f, int l) { (void)f; (void)l; return (T)v; } \
    static inline T byn_cast_##N##_u(uint64_t v, const char* f, int l) { (void)f; (void)l; return (T)v; }
#else
#define BYN_DEF_TRAPPING(N, T, S) \
    static inline T byn_add_##N(T a, T b, const char* f, int l) { \
        T r; if (BYN_UNLIKELY(__builtin_add_overflow(a, b, &r))) byn_panic("arithmetic overflow", f, l); return r; } \
    static inline T byn_sub_##N(T a, T b, const char* f, int l) { \
        T r; if (BYN_UNLIKELY(__builtin_sub_overflow(a, b, &r))) byn_panic("arithmetic overflow", f, l); return r; } \
    static inline T byn_mul_##N(T a, T b, const char* f, int l) { \
        T r; if (BYN_UNLIKELY(__builtin_mul_overflow(a, b, &r))) byn_panic("arithmetic overflow", f, l); return r; } \
    static inline T byn_neg_##N(T a, const char* f, int l) { \
        T r; if (BYN_UNLIKELY(__builtin_sub_overflow((T)0, a, &r))) byn_panic("arithmetic overflow", f, l); return r; } \
    static inline T byn_inc_##N(T a, const char* f, int l) { return byn_add_##N(a, (T)1, f, l); } \
    static inline T byn_dec_##N(T a, const char* f, int l) { return byn_sub_##N(a, (T)1, f, l); } \
    static inline T byn_cast_##N##_i(int64_t v, const char* f, int l) { \
        T r = (T)v; if (BYN_UNLIKELY((int64_t)r != v || (r < (T)1) != (v < 1))) byn_panic("value out of range for the target type", f, l); return r; } \
    static inline T byn_cast_##N##_u(uint64_t v, const char* f, int l) { \
        T r = (T)v; if (BYN_UNLIKELY((uint64_t)r != v || r < (T)0)) byn_panic("value out of range for the target type", f, l); return r; }
#endif

#define BYN_DEF_ARITH(N, T, S) \
    BYN_DEF_TRAPPING(N, T, S) \
    static inline T byn_div_##N(T a, T b, const char* f, int l) { \
        if (BYN_UNLIKELY(b == 0)) byn_panic("division by zero", f, l); \
        if ((S) && BYN_UNLIKELY(a == BYN_MIN(T, S) && b == (T)-1)) return byn_neg_##N(a, f, l); \
        return (T)(a / b); } \
    static inline T byn_rem_##N(T a, T b, const char* f, int l) { \
        if (BYN_UNLIKELY(b == 0)) byn_panic("division by zero", f, l); \
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
