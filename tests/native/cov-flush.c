/* Sets byn_cov_flush, then panics: the hook must run once before the abort. */
#include "bayan-rt.h"
#include <stdio.h>

static void flush(void) {
    puts("flushed");
    fflush(stdout);
    byn_panic("again", "cov-flush.c", 1); /* a panic inside the flush must not recurse */
}

int main(void) {
    byn_cov_flush = flush;
    byn_panic("boom", "cov-flush.c", 2);
}
