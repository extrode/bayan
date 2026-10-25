/* Prints how many 0-byte blocks runtime functions ask an allocator for, given empty input. */
#include "bayan-rt.h"
#include <stdio.h>
#include <stdlib.h>

static int zero;

static void* counting_resize(byn_alloc* self, void* old, int64_t old_size, int64_t new_size) {
    (void)self;
    (void)old_size;
    if (new_size == 0) zero++;
    return realloc(old, new_size > 0 ? (size_t)new_size : 1);
}

int main(void) {
    byn_alloc a = {counting_resize};
    byn_str hex = byn_hex_encode(NULL, 0, &a);
    printf("hex %lld, zero-byte requests %d\n", (long long)hex.len, zero);
    return 0;
}
