/* Grows a StrBuf past several blocks and prints whether text taken before each growth still reads the same, and how often
 * the allocator was handed back an old block (to free or resize it). The checker relies on both (Q745). */
#include "bayan-rt.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int handed_back;

static void* counting_resize(byn_alloc* self, void* old, int64_t old_size, int64_t new_size) {
    (void)self;
    (void)old_size;
    if (old) handed_back++;
    return realloc(old, new_size > 0 ? (size_t)new_size : 1);
}

int main(void) {
    byn_alloc a = {counting_resize};
    byn_writer w = byn_strbuf_grow(&a);
    byn_str taken[8];
    int same = 1;
    for (int i = 0; i < 8; i++) {
        byn_w_str(&w, BYN_STR("0123456789abcdef", 16));
        taken[i] = byn_strbuf_str(w);
    }
    for (int i = 0; i < 8; i++) {
        if (taken[i].len != 16 * (i + 1)) same = 0;
        for (int j = 0; j <= i; j++)
            if (memcmp(taken[i].ptr + 16 * j, "0123456789abcdef", 16) != 0) same = 0;
    }
    printf("cap %lld, taken text unchanged %d, old blocks handed back %d\n", (long long)w.cap, same, handed_back);
    return 0;
}
