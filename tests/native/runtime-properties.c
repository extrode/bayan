/* Randomized property checks of runtime functions against simple reference models. Seeded, so every run checks
 * the same cases. Prints each failure, then `ok` or the failure count. */
#include "bayan-rt.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>

static uint64_t state = UINT64_C(88172645463325252);
static uint64_t rnd(void) {
    state ^= state << 13;
    state ^= state >> 7;
    state ^= state << 17;
    return state;
}

static int fails;
#define CHECK(c, ...)                                       \
    do {                                                    \
        if (!(c)) {                                         \
            printf("FAIL line %d: ", __LINE__);             \
            printf(__VA_ARGS__);                            \
            printf("\n");                                   \
            if (++fails > 20) exit(1);                      \
        }                                                   \
    } while (0)

static uint8_t text[256];

static byn_str format_f64(double v) {
    byn_writer w = {text, sizeof text - 1, 0, BYN_WRITE_STRICT, "props", 0, NULL};
    byn_w_f64(&w, v);
    text[w.len] = 0;
    return (byn_str){text, w.len};
}

static byn_str format_f32(float v) {
    byn_writer w = {text, sizeof text - 1, 0, BYN_WRITE_STRICT, "props", 0, NULL};
    byn_w_f32(&w, v);
    text[w.len] = 0;
    return (byn_str){text, w.len};
}

/* Floats print as the shortest text that parses back to the same value. */
static void floats(void) {
    for (int it = 0; it < 30000; it++) {
        double v;
        if (it % 3 == 0) {
            v = (double)(rnd() % 2000000);
            for (int k = (int)(rnd() % 25); k > 0; k--) v /= 10;
            if (rnd() & 1) v = -v;
        } else {
            uint64_t bits = rnd();
            memcpy(&v, &bits, sizeof v);
        }
        if (isnan(v) || isinf(v)) continue;
        byn_str s = format_f64(v);
        double back = 0;
        CHECK(byn_float_parse(s, false, &back) && back == v, "f64 %.17g printed as %s", v, (const char*)text);

        float f;
        uint32_t bits32 = (uint32_t)rnd();
        memcpy(&f, &bits32, sizeof f);
        if (isnan(f) || isinf(f)) continue;
        s = format_f32(f);
        CHECK(byn_float_parse(s, true, &back) && (float)back == f, "f32 %.9g printed as %s", (double)f, (const char*)text);
    }
}

/* `string(n, as:)` reads back through `parse(as:)` at every width, signedness and radix. */
static void ints(void) {
    static const int widths[] = {8, 16, 32, 64};
    for (int it = 0; it < 40000; it++) {
        int bits = widths[rnd() % 4];
        bool is_signed = rnd() & 1;
        int radix = (int)(rnd() % 4);
        int64_t v = (int64_t)rnd();
        if (bits < 64) {
            uint64_t mask = (UINT64_C(1) << bits) - 1;
            uint64_t u = (uint64_t)v & mask;
            if (is_signed && (u >> (bits - 1)) != 0) u |= ~mask;
            v = (int64_t)u;
        }
        byn_str s = byn_int_string(v, bits, is_signed, radix, &byn_general);
        int64_t out = 0;
        int64_t r = byn_int_parse_at(s, bits, is_signed, radix, &out);
        CHECK(r == -1 && out == v, "%lld (bits %d, signed %d, radix %d) printed as %.*s, parsed %lld (%lld)", (long long)v, bits,
              is_signed, radix, (int)s.len, (const char*)s.ptr, (long long)out, (long long)r);
    }
}

/* Well-formed UTF-8 by the Unicode standard's table 3-7, written independently of the runtime's decoder. */
static bool reference_valid(const uint8_t* p, int64_t n) {
    for (int64_t i = 0; i < n;) {
        uint8_t c = p[i];
        int len = c < 0x80 ? 1 : c >= 0xC2 && c <= 0xDF ? 2 : c >= 0xE0 && c <= 0xEF ? 3 : c >= 0xF0 && c <= 0xF4 ? 4 : 0;
        if (len == 0 || n - i < len) return false;
        uint8_t lo = 0x80, hi = 0xBF;
        if (c == 0xE0) lo = 0xA0;
        else if (c == 0xED) hi = 0x9F;
        else if (c == 0xF0) lo = 0x90;
        else if (c == 0xF4) hi = 0x8F;
        if (len > 1 && (p[i + 1] < lo || p[i + 1] > hi)) return false;
        for (int k = 2; k < len; k++)
            if ((p[i + k] & 0xC0) != 0x80) return false;
        i += len;
    }
    return true;
}

/* Random bytes, leaning on the lead bytes at the edges of table 3-7: validation agrees with the reference, decoding
 * with replacement always yields valid UTF-8, valid input passes through, and the rune views stay on code-point
 * boundaries. */
static void utf8(void) {
    static const uint32_t trim[] = {'a', 0xE9};
    static const uint8_t edges[] = {0xC0, 0xC1, 0xC2, 0xDF, 0xE0, 0xED, 0xEF, 0xF0, 0xF4, 0xF5, 0xFF};
    for (int it = 0; it < 40000; it++) {
        uint8_t b[12];
        int n = (int)(rnd() % 12);
        for (int i = 0; i < n; i++) {
            uint64_t x = rnd();
            b[i] = x % 4 == 0   ? (uint8_t)(0x80 | (x >> 8) % 64)
                   : x % 4 == 1 ? edges[(x >> 8) % sizeof edges]
                                : (uint8_t)(x >> 16);
        }
        CHECK(byn_utf8_valid(b, n) == reference_valid(b, n), "validation disagrees with the reference");
        byn_str d = byn_utf8_decode_replace(b, n, &byn_general);
        CHECK(reference_valid(d.ptr, d.len), "decode with replacement is not valid UTF-8");
        int ascii_in = 0, ascii_out = 0;
        for (int i = 0; i < n; i++) ascii_in += b[i] < 0x80;
        for (int64_t i = 0; i < d.len; i++) ascii_out += d.ptr[i] < 0x80;
        CHECK(ascii_in == ascii_out, "decode with replacement swallowed an ASCII byte");
        if (!byn_utf8_valid(b, n)) continue;
        byn_str s = {b, n};
        CHECK(d.len == n && (n == 0 || memcmp(d.ptr, b, (size_t)n) == 0), "decode with replacement changed valid text");

        int64_t units = 0, len16 = 0;
        for (int64_t p = 0; p < n;) units += byn_str_next(s, &p) >= 0x10000 ? 2 : 1;
        byn_utf16_copy(s, &len16, &byn_general);
        CHECK(len16 == units, "utf16 count %lld, expected %lld", (long long)len16, (long long)units);

        int64_t k = (int64_t)(rnd() % 5);
        byn_str head = byn_str_take_runes(s, k, "props", 0), rest = byn_str_skip_runes(s, k, "props", 0);
        CHECK(head.len + rest.len == n, "take and skip do not add up");
        byn_str tail = byn_str_take_last_runes(s, k, "props", 0);
        CHECK(byn_utf8_valid(tail.ptr, tail.len), "takeLast splits a code point");
        byn_str trimmed = byn_str_trimmed_by(s, trim, 2);
        CHECK(byn_utf8_valid(trimmed.ptr, trimmed.len), "trimmed(by:) splits a code point");
        byn_str lower = byn_str_case(s, false, &byn_general), upper = byn_str_case(s, true, &byn_general);
        CHECK(byn_utf8_valid(lower.ptr, lower.len) && byn_utf8_valid(upper.ptr, upper.len), "case mapping is not valid UTF-8");
    }
}

/* replaced, count, lastPosition and parts against a naive scan over a two-letter alphabet. */
static void search(void) {
    for (int it = 0; it < 40000; it++) {
        char s[16], x[3], y[3], ref[64];
        int n = (int)(rnd() % 16), xn = 1 + (int)(rnd() % 3), yn = (int)(rnd() % 4);
        for (int i = 0; i < n; i++) s[i] = "ab"[rnd() % 2];
        for (int i = 0; i < xn; i++) x[i] = "ab"[rnd() % 2];
        for (int i = 0; i < yn; i++) y[i] = "xyz"[rnd() % 3];
        byn_str S = {(const uint8_t*)s, n}, X = {(const uint8_t*)x, xn}, Y = {(const uint8_t*)y, yn};

        int rn = 0, count = 0, last = -1;
        for (int i = 0; i < n;) {
            if (i + xn <= n && memcmp(s + i, x, (size_t)xn) == 0) {
                memcpy(ref + rn, y, (size_t)yn);
                rn += yn;
                i += xn;
                count++;
            } else {
                ref[rn++] = s[i++];
            }
        }
        for (int i = 0; i + xn <= n; i++)
            if (memcmp(s + i, x, (size_t)xn) == 0) last = i;

        byn_str r = byn_str_replaced(S, X, Y, &byn_general, "props", 0);
        CHECK(r.len == rn && (rn == 0 || memcmp(r.ptr, ref, (size_t)rn) == 0), "replaced %.*s", n, s);
        CHECK(byn_str_count(S, X) == count, "count in %.*s", n, s);
        CHECK(byn_str_last_position(S, X) == last, "lastPosition in %.*s", n, s);

        int64_t limit = (int64_t)(rnd() % 4), parts = 0, total = 0;
        byn_text_view v = byn_parts(S, X, limit, "props", 0);
        for (int64_t p = 0; byn_view_more(v, p, 3); parts++) total += byn_parts_next(&v, &p).len;
        CHECK(limit == 0 || parts <= limit, "parts(limit: %lld) gave %lld", (long long)limit, (long long)parts);
        CHECK(total + (parts - 1) * xn == n, "parts of %.*s do not add up", n, s);
    }
}

typedef struct {
    int key;
    int order;
} keyed;

static bool keyed_less(const void* a, const void* b) { return ((const keyed*)a)->key < ((const keyed*)b)->key; }

/* sort orders by key and keeps equal keys in their original order. */
static void sorting(void) {
    static keyed xs[300];
    for (int it = 0; it < 400; it++) {
        int n = (int)(rnd() % 300);
        for (int i = 0; i < n; i++) xs[i] = (keyed){(int)(rnd() % (uint64_t)(1 + it % 20)), i};
        byn_sort(xs, n, sizeof xs[0], keyed_less);
        for (int i = 1; i < n; i++)
            CHECK(xs[i - 1].key < xs[i].key || (xs[i - 1].key == xs[i].key && xs[i - 1].order < xs[i].order),
                  "sort of %d is out of order at %d", n, i);
    }
}

static bool i64_eq(const void* a, const void* b) { return *(const int64_t*)a == *(const int64_t*)b; }

static uint64_t key_hash(int64_t k) { return byn_hash_bytes(&k, sizeof k); }

/* Map insert, remove, find and copy against an array indexed by key. */
static void maps(void) {
    for (int it = 0; it < 60; it++) {
        byn_map_hdr h = {0};
        h.alloc = &byn_general;
        int64_t *keys = NULL, *vals = NULL, model[64];
        for (int i = 0; i < 64; i++) model[i] = -1;
        for (int op = 0; op < 2000; op++) {
            int64_t k = (int64_t)(rnd() % 64);
            switch (rnd() % 3) {
            case 0: {
                bool added;
                int64_t e = byn_map_insert(&h, (void**)&keys, sizeof k, (void**)&vals, sizeof k, &k, key_hash(k), i64_eq, &added);
                CHECK(added == (model[k] < 0), "insert of %lld", (long long)k);
                keys[e] = k;
                vals[e] = op;
                model[k] = op;
                break;
            }
            case 1:
                CHECK(byn_map_remove(&h, keys, sizeof k, &k, key_hash(k), i64_eq) == (model[k] >= 0), "remove of %lld", (long long)k);
                model[k] = -1;
                break;
            default: {
                int64_t e = byn_map_find(&h, keys, sizeof k, &k, key_hash(k), i64_eq);
                CHECK((e >= 0) == (model[k] >= 0) && (e < 0 || vals[e] == model[k]), "find of %lld", (long long)k);
            }
            }
            if (op % 500 == 499) {
                byn_map_hdr d;
                int64_t *dk, *dv;
                byn_map_copy(&h, keys, sizeof k, (void**)&dk, vals, sizeof k, (void**)&dv, &d, &byn_general);
                for (int64_t q = 0; q < 64; q++) {
                    int64_t e = byn_map_find(&d, dk, sizeof q, &q, key_hash(q), i64_eq);
                    CHECK((e >= 0) == (model[q] >= 0) && (e < 0 || dv[e] == model[q]), "copy lost %lld", (long long)q);
                }
            }
        }
    }
}

/* Hex.encode then Hex.decode gives the bytes back. */
static void hex(void) {
    for (int it = 0; it < 10000; it++) {
        uint8_t b[10], *out = NULL;
        int n = (int)(rnd() % 10);
        for (int i = 0; i < n; i++) b[i] = (uint8_t)rnd();
        byn_str s = byn_hex_encode(b, n, &byn_general);
        CHECK(byn_hex_decode(s, &out, &byn_general) == -1 && (n == 0 || memcmp(out, b, (size_t)n) == 0), "hex round trip");
    }
}

int main(void) {
    floats();
    ints();
    utf8();
    search();
    sorting();
    maps();
    hex();
    if (fails == 0) printf("ok\n");
    else printf("%d failures\n", fails);
    return fails != 0;
}
