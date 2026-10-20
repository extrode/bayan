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
#else
#include <time.h>
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

byn_str* byn_args(int argc, char** argv) {
    int n = argc > 1 ? argc - 1 : 0;
    byn_str* args = (byn_str*)malloc(sizeof(byn_str) * (size_t)(n > 0 ? n : 1));
    if (!args) byn_panic("out of memory", NULL, 0);
    for (int i = 0; i < n; i++) {
        const uint8_t* p = (const uint8_t*)argv[i + 1];
        int64_t len = (int64_t)strlen(argv[i + 1]);
        if (!byn_utf8_valid(p, len)) {
            fprintf(stderr, "error: argument %d is not valid UTF-8\n", i + 1);
            fflush(stderr);
            exit(1);
        }
        args[i] = (byn_str){p, len};
    }
    return args;
}

/* ── Io ──────────────────────────────────────────────────────────────────── */

static uint32_t byn_io_error(int e) {
    switch (e) {
    case ENOENT: return BYN_IO_NOT_FOUND;
    case EACCES:
    case EPERM: return BYN_IO_PERMISSION_DENIED;
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
    int failed = ferror(f);
    int e = errno;
    fclose(f);
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

uint32_t byn_io_write_file(byn_str path, const uint8_t* ptr, int64_t len) {
    FILE* f = byn_io_open(path, "wb");
    if (!f) return byn_io_error(errno);
    size_t put = len > 0 ? fwrite(ptr, 1, (size_t)len, f) : 0;
    int e = errno;
    int closed = fclose(f);
    if ((int64_t)put != len || closed != 0) return byn_io_error(e);
    return 0;
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
