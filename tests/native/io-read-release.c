/* Reads 1 MiB that is not UTF-8 from a file and from stdin with the general allocator, and prints whether the heap in use
 * grew by the input: a failed read releases its buffer. The heap is measured on macOS and glibc only; elsewhere it prints 0s. */
#include "bayan-rt.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#if defined(__APPLE__)
#include <malloc/malloc.h>
static long long in_use(void) {
    malloc_statistics_t s;
    malloc_zone_statistics(NULL, &s);
    return (long long)s.size_in_use;
}
#elif defined(__GLIBC__) && (__GLIBC__ > 2 || __GLIBC_MINOR__ >= 33)
#include <malloc.h>
static long long in_use(void) {
    struct mallinfo2 m = mallinfo2();
    return (long long)(m.uordblks + m.hblkhd);
}
#else
static long long in_use(void) { return 0; }
#endif

enum { SIZE = 1 << 20 };

int main(int argc, char** argv) {
    (void)argc;
    const char* path = argv[1];
    FILE* f = fopen(path, "wb");
    uint8_t* bytes = malloc(SIZE);
    memset(bytes, 0xFF, SIZE);
    fwrite(bytes, 1, SIZE, f);
    fclose(f);
    free(bytes);
    byn_str text;
    long long before = in_use();
    uint32_t file_err = byn_io_read_text((byn_str){(const uint8_t*)path, (int64_t)strlen(path)}, &text, &byn_general);
    long long file_grew = in_use() - before;
    if (!freopen(path, "rb", stdin)) return 1;
    before = in_use();
    uint32_t stdin_err = byn_io_read_stdin(&text, &byn_general);
    long long stdin_grew = in_use() - before;
    printf("file %u leaked %d, stdin %u leaked %d\n", file_err, file_grew >= SIZE / 2, stdin_err, stdin_grew >= SIZE / 2);
    return 0;
}
