/* Bayan runtime implementation. See bayan-rt.h. */
#if !defined(_WIN32) && !defined(_POSIX_C_SOURCE)
#define _POSIX_C_SOURCE 200809L /* clock_gettime under -std=c11 */
#endif
#include "bayan-rt.h"

#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#ifdef _WIN32
#include <windows.h>
#include <shellapi.h>
#include <fcntl.h>
#include <io.h>
#else
#include <dirent.h>
#include <fcntl.h>
#include <poll.h>
#include <spawn.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>
extern char** environ;
#endif

/* ── Panics ──────────────────────────────────────────────────────────────── */

static void byn_panic_begin(void) {
    byn_stdout_flush();
    fputs("panic: ", stderr);
}

static BYN_NORETURN void byn_panic_end(const char* file, int line) {
    if (file) fprintf(stderr, "\n  at %s:%d\n", file, line);
    else fputc('\n', stderr);
    fflush(stderr);
    abort();
}

void byn_panic(const char* msg, const char* file, int line) {
    byn_panic_begin();
    fputs(msg, stderr);
    byn_panic_end(file, line);
}

void byn_panic_str(byn_str msg, const char* file, int line) {
    byn_panic_begin();
    fwrite(msg.ptr, 1, (size_t)msg.len, stderr);
    byn_panic_end(file, line);
}

void byn_unreachable(const char* file, int line) {
    byn_panic("reached code the compiler proved unreachable", file, line);
}

/* ── Output ──────────────────────────────────────────────────────────────── */

static uint8_t byn_out_buf[1 << 14];
static int64_t byn_out_len;

static void byn_out(const uint8_t* p, int64_t n) {
    if (byn_out_len + n > (int64_t)sizeof byn_out_buf) {
        byn_stdout_flush();
        if (n > (int64_t)sizeof byn_out_buf) {
            fwrite(p, 1, (size_t)n, stdout);
            return;
        }
    }
    memcpy(byn_out_buf + byn_out_len, p, (size_t)n);
    byn_out_len += n;
}

void byn_stdout_flush(void) {
    if (byn_out_len > 0) fwrite(byn_out_buf, 1, (size_t)byn_out_len, stdout);
    byn_out_len = 0;
    fflush(stdout);
}

void byn_print(byn_str s) {
    byn_out(s.ptr, s.len);
    byn_out((const uint8_t*)"\n", 1);
}

void byn_report_error(byn_str summary) {
    byn_stdout_flush();
    fputs("error: ", stderr);
    fwrite(summary.ptr, 1, (size_t)summary.len, stderr);
    fputc('\n', stderr);
    fflush(stderr);
}

void byn_w_bytes(byn_writer* w, const uint8_t* p, int64_t n) {
    while (n > 0) {
        int64_t room = w->cap - w->len;
        if (room >= n) {
            memcpy(w->buf + w->len, p, (size_t)n);
            w->len += n;
            return;
        }
        switch (w->mode) {
        case BYN_WRITE_STDOUT:
            byn_out(w->buf, w->len);
            w->len = 0;
            if (n > w->cap) {
                byn_out(p, n);
                return;
            }
            break;
        case BYN_WRITE_TRUNCATE:
            memcpy(w->buf + w->len, p, (size_t)room);
            w->len = w->cap;
            return;
        case BYN_WRITE_STRICT:
            byn_panic("StrBuf is full", w->file, w->line);
        case BYN_WRITE_GROW: {
            int64_t grown = w->cap < 16 ? 16 : w->cap * 2;
            if (grown < w->len + n) grown = w->len + n;
            /* A fresh block, not a resize: text already taken with asString() keeps pointing at the old one. */
            uint8_t* p = (uint8_t*)w->alloc->resize(w->alloc, NULL, 0, grown);
            if (w->len > 0) memcpy(p, w->buf, (size_t)w->len);
            w->buf = p;
            w->cap = grown;
            break;
        }
        }
    }
}

void byn_w_println(byn_writer* w) {
    byn_out(w->buf, w->len);
    byn_out((const uint8_t*)"\n", 1);
    w->len = 0;
}

void byn_w_str(byn_writer* w, byn_str s) { byn_w_bytes(w, s.ptr, s.len); }

void byn_w_u64(byn_writer* w, uint64_t v) {
    uint8_t digits[20];
    int n = 0;
    do {
        digits[19 - n++] = (uint8_t)('0' + v % 10);
        v /= 10;
    } while (v != 0);
    byn_w_bytes(w, digits + 20 - n, n);
}

void byn_w_i64(byn_writer* w, int64_t v) {
    if (v < 0) {
        byn_w_bytes(w, (const uint8_t*)"-", 1);
        byn_w_u64(w, (uint64_t)0 - (uint64_t)v);
    } else {
        byn_w_u64(w, (uint64_t)v);
    }
}

/* Shortest text that reads back as the same value: fixed notation for 1e-5 <= |v| < 1e15
 * (.NET's thresholds), otherwise d.ddde<exp> with a lowercase `e`, no `+` and no leading
 * zeros, so the text is a valid Bayan literal and valid JSON. nan, inf, -inf, -0. */
static void byn_w_float(byn_writer* w, double v, int max_digits, bool single) {
    char sci[40], out[64];
    if (v != v) {
        byn_w_bytes(w, (const uint8_t*)"nan", 3);
        return;
    }
    if (v == 1.0 / 0.0 || v == -1.0 / 0.0) {
        if (v < 0) byn_w_bytes(w, (const uint8_t*)"-", 1);
        byn_w_bytes(w, (const uint8_t*)"inf", 3);
        return;
    }
    if (v == 0) {
        if (1.0 / v < 0) byn_w_bytes(w, (const uint8_t*)"-", 1);
        byn_w_bytes(w, (const uint8_t*)"0", 1);
        return;
    }
    for (int p = 1; p <= max_digits; p++) {
        snprintf(sci, sizeof sci, "%.*e", p - 1, v);
        double back = strtod(sci, NULL);
        if (single ? (float)back == (float)v : back == v) break;
    }
    /* sci is [-]d[.ddd]e[+-]xx: collect the digits and the exponent. */
    char digits[24];
    int n = 0, i = 0, len = 0;
    bool negative = sci[0] == '-';
    if (negative) i++;
    for (; sci[i] && sci[i] != 'e'; i++)
        if (sci[i] != '.') digits[n++] = sci[i];
    int exp = atoi(sci + i + 1);
    while (n > 1 && digits[n - 1] == '0') n--;
    if (negative) out[len++] = '-';
    if (exp >= 15 || exp < -5) {
        out[len++] = digits[0];
        if (n > 1) {
            out[len++] = '.';
            for (int k = 1; k < n; k++) out[len++] = digits[k];
        }
        len += snprintf(out + len, sizeof out - (size_t)len, "e%d", exp);
    } else if (exp < 0) {
        out[len++] = '0';
        out[len++] = '.';
        for (int k = 0; k < -exp - 1; k++) out[len++] = '0';
        for (int k = 0; k < n; k++) out[len++] = digits[k];
    } else {
        for (int k = 0; k <= exp || k < n; k++) {
            if (k == exp + 1) out[len++] = '.';
            out[len++] = k < n ? digits[k] : '0';
        }
    }
    byn_w_bytes(w, (const uint8_t*)out, len);
}

void byn_w_f64(byn_writer* w, double v) { byn_w_float(w, v, 17, false); }
void byn_w_f32(byn_writer* w, float v) { byn_w_float(w, v, 9, true); }

void byn_w_bool(byn_writer* w, bool v) {
    if (v) byn_w_bytes(w, (const uint8_t*)"true", 4);
    else byn_w_bytes(w, (const uint8_t*)"false", 5);
}

void byn_w_char(byn_writer* w, uint32_t c) {
    uint8_t b[4];
    int n;
    if (c < 0x80) { b[0] = (uint8_t)c; n = 1; }
    else if (c < 0x800) { b[0] = (uint8_t)(0xC0 | c >> 6); b[1] = (uint8_t)(0x80 | (c & 0x3F)); n = 2; }
    else if (c < 0x10000) {
        b[0] = (uint8_t)(0xE0 | c >> 12); b[1] = (uint8_t)(0x80 | (c >> 6 & 0x3F)); b[2] = (uint8_t)(0x80 | (c & 0x3F)); n = 3;
    } else {
        b[0] = (uint8_t)(0xF0 | c >> 18); b[1] = (uint8_t)(0x80 | (c >> 12 & 0x3F));
        b[2] = (uint8_t)(0x80 | (c >> 6 & 0x3F)); b[3] = (uint8_t)(0x80 | (c & 0x3F)); n = 4;
    }
    byn_w_bytes(w, b, n);
}

/* ── Strings ─────────────────────────────────────────────────────────────── */

bool byn_str_eq(byn_str a, byn_str b) {
    return a.len == b.len && (a.len == 0 || memcmp(a.ptr, b.ptr, (size_t)a.len) == 0);
}

int byn_str_cmp(byn_str a, byn_str b) {
    int64_t n = a.len < b.len ? a.len : b.len;
    int c = n == 0 ? 0 : memcmp(a.ptr, b.ptr, (size_t)n);
    if (c != 0) return c < 0 ? -1 : 1;
    return a.len < b.len ? -1 : a.len > b.len ? 1 : 0;
}

bool byn_str_starts_with(byn_str s, byn_str prefix) {
    return prefix.len <= s.len && (prefix.len == 0 || memcmp(s.ptr, prefix.ptr, (size_t)prefix.len) == 0);
}

bool byn_str_ends_with(byn_str s, byn_str suffix) {
    return suffix.len <= s.len && (suffix.len == 0 || memcmp(s.ptr + s.len - suffix.len, suffix.ptr, (size_t)suffix.len) == 0);
}

bool byn_str_contains(byn_str s, byn_str text) {
    if (text.len == 0) return true;
    for (int64_t i = 0; i + text.len <= s.len; i++)
        if (memcmp(s.ptr + i, text.ptr, (size_t)text.len) == 0) return true;
    return false;
}

uint32_t byn_str_next(byn_str s, int64_t* pos) {
    const uint8_t* p = s.ptr + *pos;
    uint32_t c = p[0];
    int n = c < 0x80 ? 1 : c < 0xE0 ? 2 : c < 0xF0 ? 3 : 4;
    if (n == 2) c = (c & 0x1F) << 6 | (p[1] & 0x3F);
    else if (n == 3) c = (c & 0x0F) << 12 | (uint32_t)(p[1] & 0x3F) << 6 | (p[2] & 0x3F);
    else if (n == 4) c = (c & 0x07) << 18 | (uint32_t)(p[1] & 0x3F) << 12 | (uint32_t)(p[2] & 0x3F) << 6 | (p[3] & 0x3F);
    *pos += n;
    return c;
}

static bool byn_is_boundary(byn_str s, int64_t i) {
    return i == 0 || i == s.len || (s.ptr[i] & 0xC0) != 0x80;
}

byn_str byn_str_slice(byn_str s, int64_t start, int64_t end, const char* file, int line) {
    byn_slice_check(start, end, s.len, file, line);
    if (!byn_is_boundary(s, start) || !byn_is_boundary(s, end)) byn_panic("string slice splits a code point", file, line);
    return (byn_str){s.ptr + start, end - start};
}

/* ── Allocators ──────────────────────────────────────────────────────────── */

static void* byn_general_resize(byn_alloc* self, void* old, int64_t old_size, int64_t new_size) {
    (void)self;
    (void)old_size;
    void* p = realloc(old, (size_t)new_size);
    if (!p) byn_panic("out of memory", NULL, 0);
    return p;
}

byn_alloc byn_general = {byn_general_resize};
byn_alloc* byn_context = &byn_general;

typedef struct byn_chunk {
    struct byn_chunk* next;
    int64_t size;
    int64_t used;
    _Alignas(16) uint8_t data[];
} byn_chunk;

struct byn_arena {
    byn_alloc base; /* first, so a byn_arena* is a byn_alloc* */
    byn_chunk* chunks;
};

static void* byn_arena_take(byn_arena* a, int64_t n) {
    n = (n + 15) & ~(int64_t)15;
    byn_chunk* c = a->chunks;
    if (!c || c->used + n > c->size) {
        int64_t size = n > 4096 ? n : 4096;
        if (c && c->size * 2 > size) size = c->size * 2;
        c = (byn_chunk*)malloc(sizeof(byn_chunk) + (size_t)size);
        if (!c) byn_panic("out of memory", NULL, 0);
        c->next = a->chunks;
        c->size = size;
        c->used = 0;
        a->chunks = c;
    }
    void* p = c->data + c->used;
    c->used += n;
    return p;
}

static void* byn_arena_resize(byn_alloc* self, void* old, int64_t old_size, int64_t new_size) {
    void* p = byn_arena_take((byn_arena*)self, new_size);
    if (old && old_size > 0) memcpy(p, old, (size_t)(old_size < new_size ? old_size : new_size));
    return p;
}

byn_arena* byn_arena_new(void) {
    byn_arena* a = (byn_arena*)malloc(sizeof *a);
    if (!a) byn_panic("out of memory", NULL, 0);
    a->base.resize = byn_arena_resize;
    a->chunks = NULL;
    return a;
}

void byn_arena_reset(byn_arena* a) {
    byn_chunk* c = a->chunks;
    while (c) {
        byn_chunk* next = c->next;
        free(c);
        c = next;
    }
    a->chunks = NULL;
}

void byn_arena_free(byn_arena* a) {
    byn_arena_reset(a);
    free(a);
}

byn_alloc* byn_arena_alloc(byn_arena* a) { return &a->base; }

void* byn_copy_bytes(const void* p, int64_t n, byn_alloc* to) {
    if (n == 0) return NULL;
    void* q = to->resize(to, NULL, 0, n);
    memcpy(q, p, (size_t)n);
    return q;
}

byn_str byn_str_copy(byn_str s, byn_alloc* to) {
    return (byn_str){(const uint8_t*)byn_copy_bytes(s.ptr, s.len, to), s.len};
}

void byn_list_reserve(void** ptr, int64_t* cap, int64_t need, int64_t elem_size, byn_alloc* alloc) {
    if (need <= *cap) return;
    int64_t grown = *cap < 4 ? 4 : *cap * 2;
    if (grown < need) grown = need;
    *ptr = alloc->resize(alloc, *ptr, *cap * elem_size, grown * elem_size);
    *cap = grown;
}

/* ── Maps ────────────────────────────────────────────────────────────────── */

uint64_t byn_hash_seed;

void byn_hash_seed_init(bool fixed) {
    if (fixed) {
        byn_hash_seed = 0;
        return;
    }
#ifdef _WIN32
    uint64_t pid = (uint64_t)GetCurrentProcessId();
#else
    uint64_t pid = (uint64_t)getpid();
#endif
    byn_hash_seed = (uint64_t)byn_io_monotonic_nanos() ^ (pid << 32 | pid);
}

uint64_t byn_hash_bytes(const void* p, int64_t n) {
    const uint8_t* b = (const uint8_t*)p;
    uint64_t h = UINT64_C(14695981039346656037) ^ byn_hash_seed;
    for (int64_t i = 0; i < n; i++) {
        h ^= b[i];
        h *= UINT64_C(1099511628211);
    }
    return h;
}

int64_t byn_map_find(const byn_map_hdr* h, const void* keys, int64_t ksize, const void* key, uint64_t hash, byn_key_eq eq) {
    if (h->icap == 0) return -1;
    uint64_t mask = (uint64_t)h->icap - 1;
    for (uint64_t s = hash & mask;; s = (s + 1) & mask) {
        int32_t e = h->index[s];
        if (e < 0) return -1;
        if (!h->dead[e] && h->hashes[e] == hash && eq((const uint8_t*)keys + e * ksize, key)) return e;
    }
}

static void byn_map_place(byn_map_hdr* h, int64_t e) {
    uint64_t mask = (uint64_t)h->icap - 1;
    uint64_t s = h->hashes[e] & mask;
    while (h->index[s] >= 0) s = (s + 1) & mask;
    h->index[s] = (int32_t)e;
}

/* Compacts away tombstones, grows the entries to `cap` and rebuilds the index. */
static void byn_map_rehash(byn_map_hdr* h, uint8_t** keys, int64_t ksize, uint8_t** vals, int64_t vsize, int64_t cap) {
    int64_t j = 0;
    for (int64_t i = 0; i < h->len; i++) {
        if (h->dead[i]) continue;
        if (i != j) {
            memcpy(*keys + j * ksize, *keys + i * ksize, (size_t)ksize);
            memcpy(*vals + j * vsize, *vals + i * vsize, (size_t)vsize);
            h->hashes[j] = h->hashes[i];
        }
        h->dead[j++] = false;
    }
    h->len = h->live = j;
    if (cap > h->cap) {
        byn_alloc* a = h->alloc;
        *keys = a->resize(a, *keys, h->cap * ksize, cap * ksize);
        *vals = a->resize(a, *vals, h->cap * vsize, cap * vsize);
        h->hashes = a->resize(a, h->hashes, h->cap * (int64_t)sizeof(uint64_t), cap * (int64_t)sizeof(uint64_t));
        h->dead = a->resize(a, h->dead, h->cap * (int64_t)sizeof(bool), cap * (int64_t)sizeof(bool));
        h->index = a->resize(a, h->index, h->icap * (int64_t)sizeof(int32_t), cap * 2 * (int64_t)sizeof(int32_t));
        h->cap = cap;
        h->icap = cap * 2;
    }
    memset(h->index, 0xFF, (size_t)h->icap * sizeof(int32_t));
    for (int64_t i = 0; i < h->len; i++) byn_map_place(h, i);
}

int64_t byn_map_insert(byn_map_hdr* h, void** keys, int64_t ksize, void** vals, int64_t vsize, const void* key,
                       uint64_t hash, byn_key_eq eq, bool* added) {
    int64_t e = byn_map_find(h, *keys, ksize, key, hash, eq);
    *added = e < 0;
    if (e >= 0) return e;
    if (h->len == h->cap) {
        int64_t cap = h->cap < 8 ? 8 : h->cap;
        if ((h->live + 1) * 2 > cap) cap *= 2;
        if (cap > INT32_MAX / 2) byn_panic("map too large", __FILE__, __LINE__);
        byn_map_rehash(h, (uint8_t**)keys, ksize, (uint8_t**)vals, vsize, cap);
    }
    e = h->len++;
    h->live++;
    h->hashes[e] = hash;
    h->dead[e] = false;
    byn_map_place(h, e);
    return e;
}

bool byn_map_remove(byn_map_hdr* h, const void* keys, int64_t ksize, const void* key, uint64_t hash, byn_key_eq eq) {
    int64_t e = byn_map_find(h, keys, ksize, key, hash, eq);
    if (e < 0) return false;
    h->dead[e] = true;
    h->live--;
    return true;
}

void byn_map_clear(byn_map_hdr* h) {
    h->len = h->live = 0;
    if (h->icap > 0) memset(h->index, 0xFF, (size_t)h->icap * sizeof(int32_t));
}

void byn_map_copy(const byn_map_hdr* src, const void* keys, int64_t ksize, void** dkeys, const void* vals, int64_t vsize,
                  void** dvals, byn_map_hdr* dst, byn_alloc* to) {
    *dst = (byn_map_hdr){0};
    dst->alloc = to;
    *dkeys = NULL;
    *dvals = NULL;
    if (src->live == 0) return;
    int64_t cap = 8;
    while (cap < src->live) cap *= 2;
    dst->cap = cap;
    dst->icap = cap * 2;
    *dkeys = to->resize(to, NULL, 0, cap * ksize);
    *dvals = to->resize(to, NULL, 0, cap * vsize);
    dst->hashes = to->resize(to, NULL, 0, cap * (int64_t)sizeof(uint64_t));
    dst->dead = to->resize(to, NULL, 0, cap * (int64_t)sizeof(bool));
    dst->index = to->resize(to, NULL, 0, dst->icap * (int64_t)sizeof(int32_t));
    int64_t j = 0;
    for (int64_t i = 0; i < src->len; i++) {
        if (src->dead[i]) continue;
        memcpy((uint8_t*)*dkeys + j * ksize, (const uint8_t*)keys + i * ksize, (size_t)ksize);
        memcpy((uint8_t*)*dvals + j * vsize, (const uint8_t*)vals + i * vsize, (size_t)vsize);
        dst->hashes[j] = src->hashes[i];
        dst->dead[j++] = false;
    }
    dst->len = dst->live = j;
    memset(dst->index, 0xFF, (size_t)dst->icap * sizeof(int32_t));
    for (int64_t i = 0; i < j; i++) byn_map_place(dst, i);
}

byn_writer byn_strbuf_copy(byn_writer w, byn_alloc* to) {
    byn_writer r = byn_strbuf_grow(to);
    r.buf = (uint8_t*)byn_copy_bytes(w.buf, w.len, to);
    r.cap = r.len = w.len;
    return r;
}

/* ── Program entry ───────────────────────────────────────────────────────── */

bool byn_utf8_valid(const uint8_t* p, int64_t n) {
    int64_t i = 0;
    while (i < n) {
        uint8_t c = p[i];
        if (c < 0x80) {
            i++;
            continue;
        }
        int len;
        uint32_t min;
        uint32_t v;
        if ((c & 0xE0) == 0xC0) len = 2, min = 0x80, v = c & 0x1F;
        else if ((c & 0xF0) == 0xE0) len = 3, min = 0x800, v = c & 0x0F;
        else if ((c & 0xF8) == 0xF0) len = 4, min = 0x10000, v = c & 0x07;
        else return false;
        if (n - i < len) return false;
        for (int k = 1; k < len; k++) {
            if ((p[i + k] & 0xC0) != 0x80) return false;
            v = v << 6 | (p[i + k] & 0x3F);
        }
        if (v < min || v > 0x10FFFF || (v >= 0xD800 && v <= 0xDFFF)) return false;
        i += len;
    }
    return true;
}

static BYN_NORETURN void byn_bad_arg(int i) {
    fprintf(stderr, "error: argument %d is not valid UTF-8\n", i);
    fflush(stderr);
    exit(1);
}

byn_str* byn_args(int* argc, char** argv) {
#ifdef _WIN32
    /* The CRT's argv is in the ANSI code page; the UTF-16 command line is what the caller passed. */
    (void)argv;
    int wargc = 0;
    wchar_t** wargv = CommandLineToArgvW(GetCommandLineW(), &wargc);
    if (!wargv) byn_panic("out of memory", NULL, 0);
    *argc = wargc;
    int n = wargc > 1 ? wargc - 1 : 0;
    byn_str* args = (byn_str*)malloc(sizeof(byn_str) * (size_t)(n > 0 ? n : 1));
    if (!args) byn_panic("out of memory", NULL, 0);
    for (int i = 0; i < n; i++) {
        const wchar_t* w = wargv[i + 1];
        int un = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, w, -1, NULL, 0, NULL, NULL);
        if (un <= 0) byn_bad_arg(i + 1);
        char* p = (char*)malloc((size_t)un);
        if (!p) byn_panic("out of memory", NULL, 0);
        WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, w, -1, p, un, NULL, NULL);
        args[i] = (byn_str){(const uint8_t*)p, un - 1};
    }
    LocalFree(wargv);
    return args;
#else
    int n = *argc > 1 ? *argc - 1 : 0;
    byn_str* args = (byn_str*)malloc(sizeof(byn_str) * (size_t)(n > 0 ? n : 1));
    if (!args) byn_panic("out of memory", NULL, 0);
    for (int i = 0; i < n; i++) {
        const uint8_t* p = (const uint8_t*)argv[i + 1];
        int64_t len = (int64_t)strlen(argv[i + 1]);
        if (!byn_utf8_valid(p, len)) byn_bad_arg(i + 1);
        args[i] = (byn_str){p, len};
    }
    return args;
#endif
}

/* ── Io ──────────────────────────────────────────────────────────────────── */

static uint32_t byn_io_error(int e) {
    switch (e) {
    case ENOENT: return BYN_IO_NOT_FOUND;
    case EACCES:
    case EPERM: return BYN_IO_PERMISSION_DENIED;
    case ENOTDIR: return BYN_IO_NOT_A_DIRECTORY;
    default: return BYN_IO_OTHER;
    }
}

/* Opens a UTF-8 path; NULL with errno set on failure. */
static FILE* byn_io_open(byn_str path, const char* mode) {
    if (memchr(path.ptr, 0, (size_t)path.len)) {
        errno = ENOENT;
        return NULL;
    }
#ifdef _WIN32
    int wn = MultiByteToWideChar(CP_UTF8, 0, (const char*)path.ptr, (int)path.len, NULL, 0);
    wchar_t* wpath = (wchar_t*)malloc(sizeof(wchar_t) * (size_t)(wn + 1));
    if (!wpath) byn_panic("out of memory", NULL, 0);
    MultiByteToWideChar(CP_UTF8, 0, (const char*)path.ptr, (int)path.len, wpath, wn);
    wpath[wn] = 0;
    wchar_t wmode[4] = {0};
    for (int i = 0; mode[i] && i < 3; i++) wmode[i] = (wchar_t)mode[i];
    FILE* f = _wfopen(wpath, wmode);
    free(wpath);
    return f;
#else
    char* cpath = (char*)malloc((size_t)path.len + 1);
    if (!cpath) byn_panic("out of memory", NULL, 0);
    memcpy(cpath, path.ptr, (size_t)path.len);
    cpath[path.len] = 0;
    FILE* f = fopen(cpath, mode);
    free(cpath);
    return f;
#endif
}

uint32_t byn_io_read(byn_str path, uint8_t** ptr, int64_t* len, byn_alloc* alloc) {
    FILE* f = byn_io_open(path, "rb");
    if (!f) return byn_io_error(errno);
    uint8_t* buf = NULL;
    int64_t cap = 0;
    int64_t n = 0;
    for (;;) {
        if (n == cap) {
            int64_t grown = cap < 4096 ? 4096 : cap * 2;
            buf = (uint8_t*)alloc->resize(alloc, buf, cap, grown);
            cap = grown;
        }
        size_t got = fread(buf + n, 1, (size_t)(cap - n), f);
        n += (int64_t)got;
        if (got == 0) break;
    }
    bool failed = ferror(f) != 0;
    int e = errno;
    if (fclose(f) != 0 && !failed) failed = true, e = errno;
    if (failed) return byn_io_error(e);
    *ptr = buf;
    *len = n;
    return 0;
}

uint32_t byn_io_read_text(byn_str path, byn_str* out, byn_alloc* alloc) {
    uint8_t* p;
    int64_t n;
    uint32_t err = byn_io_read(path, &p, &n, alloc);
    if (err != 0) return err;
    if (!byn_utf8_valid(p, n)) return BYN_IO_INVALID_UTF8;
    if (out) *out = (byn_str){p, n};
    return 0;
}

uint32_t byn_io_read_stdin(byn_str* out, byn_alloc* alloc) {
#ifdef _WIN32
    /* Text mode would turn CRLF into LF and stop at Ctrl-Z. */
    _setmode(_fileno(stdin), _O_BINARY);
#endif
    uint8_t* buf = NULL;
    int64_t cap = 0;
    int64_t n = 0;
    for (;;) {
        if (n == cap) {
            int64_t grown = cap < 4096 ? 4096 : cap * 2;
            buf = (uint8_t*)alloc->resize(alloc, buf, cap, grown);
            cap = grown;
        }
        size_t got = fread(buf + n, 1, (size_t)(cap - n), stdin);
        n += (int64_t)got;
        if (got == 0) break;
    }
    if (ferror(stdin)) return byn_io_error(errno);
    if (out) *out = (byn_str){buf, n};
    return 0;
}

uint32_t byn_io_write_file(byn_str path, const uint8_t* ptr, int64_t len) {
    FILE* f = byn_io_open(path, "wb");
    if (!f) return byn_io_error(errno);
    size_t put = len > 0 ? fwrite(ptr, 1, (size_t)len, f) : 0;
    bool failed = (int64_t)put != len;
    int e = errno;
    if (fclose(f) != 0 && !failed) failed = true, e = errno;
    if (failed) return byn_io_error(e);
    return 0;
}

/* Growable bytes from malloc: process output is drained before `alloc` sees it. */
typedef struct byn_bytes {
    uint8_t* ptr;
    int64_t len;
    int64_t cap;
} byn_bytes;

static void byn_bytes_reserve(byn_bytes* b, int64_t more) {
    if (b->len + more <= b->cap) return;
    int64_t grown = b->cap < 4096 ? 4096 : b->cap * 2;
    while (grown < b->len + more) grown *= 2;
    uint8_t* p = (uint8_t*)realloc(b->ptr, (size_t)grown);
    if (!p) byn_panic("out of memory", NULL, 0);
    b->ptr = p;
    b->cap = grown;
}

static int byn_str_order(const void* a, const void* b) {
    const byn_str* x = (const byn_str*)a;
    const byn_str* y = (const byn_str*)b;
    int64_t n = x->len < y->len ? x->len : y->len;
    int c = n > 0 ? memcmp(x->ptr, y->ptr, (size_t)n) : 0;
    if (c != 0) return c;
    return x->len < y->len ? -1 : x->len > y->len;
}

/* Copies an entry name into `alloc` and appends it to the list; false if it is not UTF-8. */
static bool byn_list_dir_add(byn_str** ptr, int64_t* len, int64_t* cap, const char* name, int64_t n, byn_alloc* alloc) {
    if (!byn_utf8_valid((const uint8_t*)name, n)) return false;
    byn_list_reserve((void**)ptr, cap, *len + 1, (int64_t)sizeof(byn_str), alloc);
    (*ptr)[(*len)++] = byn_str_copy((byn_str){(const uint8_t*)name, n}, alloc);
    return true;
}

#ifdef _WIN32
/* A UTF-8 string as a NUL-terminated wide string from malloc. */
static wchar_t* byn_wide(const uint8_t* p, int64_t n) {
    int wn = n > 0 ? MultiByteToWideChar(CP_UTF8, 0, (const char*)p, (int)n, NULL, 0) : 0;
    wchar_t* w = (wchar_t*)malloc(sizeof(wchar_t) * (size_t)(wn + 1));
    if (!w) byn_panic("out of memory", NULL, 0);
    if (wn > 0) MultiByteToWideChar(CP_UTF8, 0, (const char*)p, (int)n, w, wn);
    w[wn] = 0;
    return w;
}

static uint32_t byn_win_error(DWORD e) {
    switch (e) {
    case ERROR_FILE_NOT_FOUND:
    case ERROR_PATH_NOT_FOUND:
    case ERROR_INVALID_NAME:
    case ERROR_BAD_PATHNAME: return BYN_IO_NOT_FOUND;
    case ERROR_ACCESS_DENIED: return BYN_IO_PERMISSION_DENIED;
    case ERROR_DIRECTORY: return BYN_IO_NOT_A_DIRECTORY;
    default: return BYN_IO_OTHER;
    }
}
#else
/* A path as a NUL-terminated string from malloc, or NULL if it holds a NUL byte. */
static char* byn_cstr(byn_str s) {
    if (memchr(s.ptr, 0, (size_t)s.len)) return NULL;
    char* c = (char*)malloc((size_t)s.len + 1);
    if (!c) byn_panic("out of memory", NULL, 0);
    memcpy(c, s.ptr, (size_t)s.len);
    c[s.len] = 0;
    return c;
}
#endif

uint32_t byn_io_list_dir(byn_str path, byn_str** ptr, int64_t* len, byn_alloc* alloc) {
    byn_str* names = NULL;
    int64_t n = 0;
    int64_t cap = 0;
    if (memchr(path.ptr, 0, (size_t)path.len)) return BYN_IO_NOT_FOUND;
#ifdef _WIN32
    wchar_t* wpath = byn_wide(path.ptr, path.len);
    DWORD attrs = GetFileAttributesW(wpath);
    if (attrs == INVALID_FILE_ATTRIBUTES) {
        free(wpath);
        return byn_win_error(GetLastError());
    }
    if (!(attrs & FILE_ATTRIBUTE_DIRECTORY)) {
        free(wpath);
        return BYN_IO_NOT_A_DIRECTORY;
    }
    size_t wn = wcslen(wpath);
    wchar_t* pattern = (wchar_t*)malloc(sizeof(wchar_t) * (wn + 3));
    if (!pattern) byn_panic("out of memory", NULL, 0);
    memcpy(pattern, wpath, sizeof(wchar_t) * wn);
    if (wn > 0 && wpath[wn - 1] != L'\\' && wpath[wn - 1] != L'/') pattern[wn++] = L'\\';
    pattern[wn++] = L'*';
    pattern[wn] = 0;
    free(wpath);
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW(pattern, &fd);
    free(pattern);
    if (h == INVALID_HANDLE_VALUE) {
        DWORD e = GetLastError();
        if (e != ERROR_FILE_NOT_FOUND) return byn_win_error(e);
    } else {
        uint32_t err = 0;
        do {
            const wchar_t* w = fd.cFileName;
            if (wcscmp(w, L".") == 0 || wcscmp(w, L"..") == 0) continue;
            char name[MAX_PATH * 4];
            int un = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, w, -1, name, (int)sizeof name, NULL, NULL);
            if (un <= 0 || !byn_list_dir_add(&names, &n, &cap, name, un - 1, alloc)) {
                err = BYN_IO_INVALID_UTF8;
                break;
            }
        } while (FindNextFileW(h, &fd));
        DWORD e = GetLastError();
        FindClose(h);
        if (err != 0) return err;
        if (e != ERROR_NO_MORE_FILES) return byn_win_error(e);
    }
#else
    char* cpath = byn_cstr(path);
    DIR* d = opendir(cpath);
    int e = errno;
    free(cpath);
    if (!d) return byn_io_error(e);
    for (;;) {
        errno = 0;
        struct dirent* ent = readdir(d);
        if (!ent) {
            e = errno;
            break;
        }
        const char* name = ent->d_name;
        if (strcmp(name, ".") == 0 || strcmp(name, "..") == 0) continue;
        if (!byn_list_dir_add(&names, &n, &cap, name, (int64_t)strlen(name), alloc)) {
            closedir(d);
            return BYN_IO_INVALID_UTF8;
        }
    }
    closedir(d);
    if (e != 0) return byn_io_error(e);
#endif
    if (n > 1) qsort(names, (size_t)n, sizeof(byn_str), byn_str_order);
    *ptr = names;
    *len = n;
    return 0;
}

static void byn_bytes_add(byn_bytes* b, const void* p, int64_t n) {
    byn_bytes_reserve(b, n);
    memcpy(b->ptr + b->len, p, (size_t)n);
    b->len += n;
}

/* Whether `s` can be quoted for cmd.exe exactly: no NUL, CR or LF (they end the command), no `"`
 * (it would close the quotes) and no `%` (expanded even inside quotes). */
static bool byn_batch_safe(byn_str s) {
    for (int64_t i = 0; i < s.len; i++) {
        uint8_t c = s.ptr[i];
        if (c == 0 || c == '\r' || c == '\n' || c == '"' || c == '%') return false;
    }
    return true;
}

bool byn_batch_cmdline(byn_str path, const byn_str* args, int64_t nargs, byn_str* out) {
    if (path.len < 4 || !byn_batch_safe(path)) return false;
    char ext[4];
    for (int i = 0; i < 4; i++) {
        uint8_t c = path.ptr[path.len - 4 + i];
        ext[i] = (char)(c >= 'A' && c <= 'Z' ? c + 32 : c);
    }
    if (memcmp(ext, ".bat", 4) != 0 && memcmp(ext, ".cmd", 4) != 0) return false;
    for (int64_t i = 0; i < nargs; i++)
        if (!byn_batch_safe(args[i])) return false;
    byn_bytes b = {NULL, 0, 0};
    const char* head = "cmd.exe /d /e:ON /v:OFF /s /c \"\"";
    byn_bytes_add(&b, head, (int64_t)strlen(head));
    byn_bytes_add(&b, path.ptr, path.len);
    byn_bytes_add(&b, "\"", 1);
    for (int64_t i = 0; i < nargs; i++) {
        byn_bytes_add(&b, " \"", 2);
        byn_bytes_add(&b, args[i].ptr, args[i].len);
        byn_bytes_add(&b, "\"", 1);
    }
    byn_bytes_add(&b, "\"", 1);
    *out = (byn_str){b.ptr, b.len};
    return true;
}

/* Checks both captured streams are UTF-8 and copies them into `alloc`; frees them either way. */
static uint32_t byn_process_finish(byn_bytes* streams, int32_t code, byn_process* out, byn_alloc* alloc) {
    uint32_t result = 0;
    if (!byn_utf8_valid(streams[0].ptr, streams[0].len) || !byn_utf8_valid(streams[1].ptr, streams[1].len))
        result = BYN_IO_INVALID_UTF8;
    else if (out)
        *out = (byn_process){code, byn_str_copy((byn_str){streams[0].ptr, streams[0].len}, alloc),
                             byn_str_copy((byn_str){streams[1].ptr, streams[1].len}, alloc)};
    free(streams[0].ptr);
    free(streams[1].ptr);
    return result;
}

#ifdef _WIN32

/* Appends one argument quoted so that CommandLineToArgvW and the MSVC runtime read it back unchanged. */
static void byn_quote_arg(byn_bytes* b, byn_str s) {
    if (b->len > 0) byn_bytes_add(b, " ", 1);
    bool plain = s.len > 0;
    for (int64_t i = 0; i < s.len && plain; i++)
        if (s.ptr[i] == ' ' || s.ptr[i] == '\t' || s.ptr[i] == '\n' || s.ptr[i] == '\v' || s.ptr[i] == '"') plain = false;
    if (plain) {
        byn_bytes_add(b, s.ptr, s.len);
        return;
    }
    byn_bytes_add(b, "\"", 1);
    for (int64_t i = 0;; i++) {
        int64_t slashes = 0;
        while (i < s.len && s.ptr[i] == '\\') i++, slashes++;
        if (i == s.len) {
            for (int64_t k = 0; k < slashes * 2; k++) byn_bytes_add(b, "\\", 1);
            break;
        }
        int64_t count = s.ptr[i] == '"' ? slashes * 2 + 1 : slashes;
        for (int64_t k = 0; k < count; k++) byn_bytes_add(b, "\\", 1);
        byn_bytes_add(b, &s.ptr[i], 1);
    }
    byn_bytes_add(b, "\"", 1);
}

/* Whether a program name ends in `.bat` or `.cmd`; Windows ignores trailing dots and spaces. */
static bool byn_is_batch(const wchar_t* s, size_t n) {
    while (n > 0 && (s[n - 1] == L'.' || s[n - 1] == L' ')) n--;
    return n >= 4 && (_wcsnicmp(s + n - 4, L".bat", 4) == 0 || _wcsnicmp(s + n - 4, L".cmd", 4) == 0);
}

/* `dir` (`dn` units), a separator, `name` and `ext` as a file path from malloc if it is a file, else NULL. */
static wchar_t* byn_program_in(const wchar_t* dir, size_t dn, const wchar_t* name, size_t nn, const wchar_t* ext, size_t xn) {
    wchar_t* c = (wchar_t*)malloc(sizeof(wchar_t) * (dn + nn + xn + 2));
    if (!c) byn_panic("out of memory", NULL, 0);
    memcpy(c, dir, sizeof(wchar_t) * dn);
    size_t at = dn;
    if (c[at - 1] != L'\\' && c[at - 1] != L'/') c[at++] = L'\\';
    memcpy(c + at, name, sizeof(wchar_t) * nn);
    memcpy(c + at + nn, ext, sizeof(wchar_t) * xn);
    c[at + nn + xn] = 0;
    DWORD attrs = GetFileAttributesW(c);
    if (attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY)) return c;
    free(c);
    return NULL;
}

/* `program` itself if it names a path, else the first PATH entry holding it as given or with a
 * PATHEXT extension; `.BAT` and `.CMD` are never appended. */
static wchar_t* byn_find_program(byn_str program) {
    wchar_t* name = byn_wide(program.ptr, program.len);
    if (memchr(program.ptr, '/', (size_t)program.len) || memchr(program.ptr, '\\', (size_t)program.len)) return name;
    wchar_t path[32767];
    wchar_t exts[1024];
    DWORD pn = GetEnvironmentVariableW(L"PATH", path, 32767);
    if (pn == 0 || pn >= 32767) path[0] = 0;
    DWORD en = GetEnvironmentVariableW(L"PATHEXT", exts, 1024);
    if (en == 0 || en >= 1024) wcscpy(exts, L".COM;.EXE;.BAT;.CMD");
    size_t nn = wcslen(name);
    /* Like CreateProcess, the bare name is tried only when its final component already has an extension. */
    wchar_t* base = wcsrchr(name, L'.');
    int bare = base != NULL;
    wchar_t* found = NULL;
    for (wchar_t* dir = path; *dir && !found;) {
        wchar_t* dend = wcschr(dir, L';');
        size_t dn = dend ? (size_t)(dend - dir) : wcslen(dir);
        if (dn > 0 && bare) found = byn_program_in(dir, dn, name, nn, L"", 0);
        for (wchar_t* ext = exts; *ext && dn > 0 && !found;) {
            wchar_t* eend = wcschr(ext, L';');
            size_t xn = eend ? (size_t)(eend - ext) : wcslen(ext);
            if (xn > 0 && !byn_is_batch(ext, xn)) found = byn_program_in(dir, dn, name, nn, ext, xn);
            ext = eend ? eend + 1 : ext + xn;
        }
        dir = dend ? dend + 1 : dir + dn;
    }
    free(name);
    return found;
}

typedef struct byn_drain {
    HANDLE h;
    byn_bytes bytes;
} byn_drain;

static DWORD WINAPI byn_drain_pipe(LPVOID arg) {
    byn_drain* d = (byn_drain*)arg;
    for (;;) {
        byn_bytes_reserve(&d->bytes, 4096);
        DWORD got = 0;
        if (!ReadFile(d->h, d->bytes.ptr + d->bytes.len, 4096, &got, NULL) || got == 0) break;
        d->bytes.len += got;
    }
    return 0;
}

/* Runs `app` with the command line `wline` (both freed here), capturing both streams into `streams`. */
static uint32_t byn_win_spawn(wchar_t* app, wchar_t* wline, byn_bytes* streams, int32_t* code) {
    SECURITY_ATTRIBUTES sa = {sizeof sa, NULL, TRUE};
    HANDLE reads[2];
    HANDLE writes[2];
    if (!CreatePipe(&reads[0], &writes[0], &sa, 0)) {
        free(app);
        free(wline);
        return byn_win_error(GetLastError());
    }
    if (!CreatePipe(&reads[1], &writes[1], &sa, 0)) {
        DWORD e = GetLastError();
        CloseHandle(reads[0]);
        CloseHandle(writes[0]);
        free(app);
        free(wline);
        return byn_win_error(e);
    }
    SetHandleInformation(reads[0], HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(reads[1], HANDLE_FLAG_INHERIT, 0);
    STARTUPINFOW si;
    memset(&si, 0, sizeof si);
    si.cb = sizeof si;
    si.dwFlags = STARTF_USESTDHANDLES;
    si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
    si.hStdOutput = writes[0];
    si.hStdError = writes[1];
    PROCESS_INFORMATION pi;
    BOOL started = CreateProcessW(app, wline, NULL, NULL, TRUE, 0, NULL, NULL, &si, &pi);
    DWORD e = GetLastError();
    free(app);
    free(wline);
    CloseHandle(writes[0]);
    CloseHandle(writes[1]);
    if (!started) {
        CloseHandle(reads[0]);
        CloseHandle(reads[1]);
        return byn_win_error(e);
    }
    CloseHandle(pi.hThread);
    byn_drain drains[2] = {{reads[0], {NULL, 0, 0}}, {reads[1], {NULL, 0, 0}}};
    HANDLE t = CreateThread(NULL, 0, byn_drain_pipe, &drains[1], 0, NULL);
    if (!t) byn_drain_pipe(&drains[1]);
    byn_drain_pipe(&drains[0]);
    if (t) {
        WaitForSingleObject(t, INFINITE);
        CloseHandle(t);
    }
    CloseHandle(reads[0]);
    CloseHandle(reads[1]);
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD status = 0;
    GetExitCodeProcess(pi.hProcess, &status);
    CloseHandle(pi.hProcess);
    *code = (int32_t)status;
    streams[0] = drains[0].bytes;
    streams[1] = drains[1].bytes;
    return 0;
}
#endif

uint32_t byn_io_run(byn_str program, const byn_str* args, int64_t nargs, byn_process* out, byn_alloc* alloc) {
    if (program.len == 0 || memchr(program.ptr, 0, (size_t)program.len)) return BYN_IO_NOT_FOUND;
    for (int64_t i = 0; i < nargs; i++)
        if (memchr(args[i].ptr, 0, (size_t)args[i].len)) return BYN_IO_OTHER;
    byn_bytes streams[2] = {{NULL, 0, 0}, {NULL, 0, 0}};
    int32_t code;
#ifdef _WIN32
    wchar_t* app = byn_find_program(program);
    if (!app) return BYN_IO_NOT_FOUND;
    /* CreateProcessW runs a batch file through cmd.exe, which re-parses the arguments (BatBadBut). */
    if (byn_is_batch(app, wcslen(app))) {
        free(app);
        return BYN_IO_PERMISSION_DENIED;
    }
    byn_bytes line = {NULL, 0, 0};
    byn_quote_arg(&line, program);
    for (int64_t i = 0; i < nargs; i++) byn_quote_arg(&line, args[i]);
    wchar_t* wline = byn_wide(line.ptr, line.len);
    free(line.ptr);
    uint32_t err = byn_win_spawn(app, wline, streams, &code);
    if (err != 0) return err;
#else
    char** argv = (char**)malloc(sizeof(char*) * (size_t)(nargs + 2));
    if (!argv) byn_panic("out of memory", NULL, 0);
    argv[0] = byn_cstr(program);
    for (int64_t i = 0; i < nargs; i++) argv[i + 1] = byn_cstr(args[i]);
    argv[nargs + 1] = NULL;
    int pipes[2][2];
    uint32_t err = 0;
    if (pipe(pipes[0]) != 0) err = byn_io_error(errno);
    else if (pipe(pipes[1]) != 0) {
        err = byn_io_error(errno);
        close(pipes[0][0]);
        close(pipes[0][1]);
    }
    pid_t pid = 0;
    if (err == 0) {
        for (int i = 0; i < 2; i++) {
            fcntl(pipes[i][0], F_SETFD, FD_CLOEXEC);
            fcntl(pipes[i][1], F_SETFD, FD_CLOEXEC);
        }
        posix_spawn_file_actions_t actions;
        posix_spawn_file_actions_init(&actions);
        posix_spawn_file_actions_adddup2(&actions, pipes[0][1], 1);
        posix_spawn_file_actions_adddup2(&actions, pipes[1][1], 2);
        int rc = posix_spawnp(&pid, argv[0], &actions, NULL, argv, environ);
        posix_spawn_file_actions_destroy(&actions);
        close(pipes[0][1]);
        close(pipes[1][1]);
        if (rc != 0) {
            err = byn_io_error(rc);
            close(pipes[0][0]);
            close(pipes[1][0]);
        }
    }
    for (int64_t i = 0; i <= nargs; i++) free(argv[i]);
    free(argv);
    if (err != 0) return err;
    /* Drain both pipes together, so a child that fills one while we block on the other cannot deadlock. */
    struct pollfd fds[2] = {{pipes[0][0], POLLIN, 0}, {pipes[1][0], POLLIN, 0}};
    int open = 2;
    while (open > 0) {
        if (poll(fds, 2, -1) < 0) {
            if (errno == EINTR) continue;
            break;
        }
        for (int i = 0; i < 2; i++) {
            if (fds[i].fd < 0 || !(fds[i].revents & (POLLIN | POLLHUP | POLLERR))) continue;
            byn_bytes_reserve(&streams[i], 4096);
            ssize_t got = read(fds[i].fd, streams[i].ptr + streams[i].len, 4096);
            if (got > 0) streams[i].len += got;
            else if (got == 0 || errno != EINTR) {
                close(fds[i].fd);
                fds[i].fd = -1;
                open--;
            }
        }
    }
    for (int i = 0; i < 2; i++)
        if (fds[i].fd >= 0) close(fds[i].fd);
    int status = 0;
    while (waitpid(pid, &status, 0) < 0 && errno == EINTR) {}
    code = WIFSIGNALED(status) ? 128 + WTERMSIG(status) : WEXITSTATUS(status);
#endif
    return byn_process_finish(streams, code, out, alloc);
}

uint32_t byn_io_run_batch(byn_str path, const byn_str* args, int64_t nargs, byn_process* out, byn_alloc* alloc) {
#ifdef _WIN32
    byn_str line;
    if (!byn_batch_cmdline(path, args, nargs, &line)) return BYN_IO_INVALID_INPUT;
    /* cmd.exe from the system directory: neither PATH nor %ComSpec% can redirect it. */
    wchar_t sys[MAX_PATH];
    UINT sn = GetSystemDirectoryW(sys, MAX_PATH);
    if (sn == 0 || sn >= MAX_PATH - 9) {
        free((void*)line.ptr);
        return byn_win_error(GetLastError());
    }
    wchar_t* app = (wchar_t*)malloc(sizeof(wchar_t) * (sn + 9));
    if (!app) byn_panic("out of memory", NULL, 0);
    memcpy(app, sys, sizeof(wchar_t) * sn);
    wcscpy(app + sn, L"\\cmd.exe");
    wchar_t* wline = byn_wide(line.ptr, line.len);
    free((void*)line.ptr);
    byn_bytes streams[2] = {{NULL, 0, 0}, {NULL, 0, 0}};
    int32_t code;
    uint32_t err = byn_win_spawn(app, wline, streams, &code);
    if (err != 0) return err;
    return byn_process_finish(streams, code, out, alloc);
#else
    (void)path, (void)args, (void)nargs, (void)out, (void)alloc;
    return BYN_IO_UNSUPPORTED;
#endif
}

void byn_io_write(int32_t fd, byn_str s) {
    if (fd == 1) {
        byn_out(s.ptr, s.len);
        return;
    }
    byn_stdout_flush();
    fwrite(s.ptr, 1, (size_t)s.len, stderr);
    fflush(stderr);
}

int64_t byn_io_monotonic_nanos(void) {
#ifdef _WIN32
    LARGE_INTEGER count;
    LARGE_INTEGER freq;
    QueryPerformanceCounter(&count);
    QueryPerformanceFrequency(&freq);
    int64_t c = count.QuadPart;
    int64_t f = freq.QuadPart;
    return c / f * 1000000000 + c % f * 1000000000 / f;
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000000 + ts.tv_nsec;
#endif
}
