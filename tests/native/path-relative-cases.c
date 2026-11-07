/* Runs `byn_path_relative` on every case read from stdin and prints one line per case: the result in hex. A case is
 * one line of two space-separated hex fields, from then to. Hex carries bytes that are not UTF-8, which no Bayan
 * string holds, so the runtime's U+FFFD handling is checked against .NET here. With `from` on a root `to` does not share, the
 * result is `to`'s full path, which is how StdPathTests.ShortNamesExpandAsDotNet shows each short-name expansion. */
#include "bayan-rt.h"
#include <stdio.h>
#include <string.h>

static int nibble(char c) {
    return c <= '9' ? c - '0' : c - 'a' + 10;
}

/* Decodes the field at `p` in place and returns its end. */
static char* field(char* p, byn_str* out) {
    char* end = p;
    while (*end && *end != ' ') end++;
    int64_t n = 0;
    for (char* q = p; q < end; q += 2) p[n++] = (char)(nibble(q[0]) * 16 + nibble(q[1]));
    *out = (byn_str){(const uint8_t*)p, n};
    return end;
}

int main(void) {
    static char buf[1 << 16];
    while (fgets(buf, sizeof buf, stdin)) {
        size_t len = strlen(buf);
        while (len > 0 && (buf[len - 1] == '\n' || buf[len - 1] == '\r')) buf[--len] = 0;
        if (len == 0) continue;
        byn_str from, to;
        char* p = field(buf, &from);
        field(p + 1, &to);
        byn_str r = byn_path_relative(from, to, &byn_general, __FILE__, __LINE__);
        for (int64_t i = 0; i < r.len; i++) printf("%02x", r.ptr[i]);
        printf("\n");
    }
    return 0;
}
