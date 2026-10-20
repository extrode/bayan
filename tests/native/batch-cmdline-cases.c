/* Runs `byn_batch_cmdline` on every case read from stdin and prints one line per case: the command
 * line in hex, or `refused`. A case is one line of space-separated hex fields, the path then each
 * argument; `-` is the empty string. Hex keeps NUL, CR and LF out of the line structure. */
#include "bayan-rt.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int nibble(char c) {
    return c <= '9' ? c - '0' : c - 'a' + 10;
}

/* Decodes the field at `p` in place and returns its end. */
static char* field(char* p, byn_str* out) {
    char* end = p;
    while (*end && *end != ' ') end++;
    int64_t n = 0;
    if (!(end - p == 1 && *p == '-'))
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
        byn_str path;
        byn_str args[16];
        int64_t nargs = 0;
        char* p = field(buf, &path);
        while (*p == ' ' && nargs < 16) p = field(p + 1, &args[nargs++]);
        byn_str line;
        if (!byn_batch_cmdline(path, args, nargs, &line)) {
            printf("refused\n");
            continue;
        }
        for (int64_t i = 0; i < line.len; i++) printf("%02x", line.ptr[i]);
        printf("\n");
        free((void*)line.ptr);
    }
    return 0;
}
