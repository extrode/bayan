/* Reads 1 MiB that is not UTF-8 from a file and from stdin with the general allocator, and prints whether the heap in use
 * grew by the input: a failed read releases its buffer. Then reads a directory (fread fails with EISDIR) as a file and as
 * stdin, many times over, so a leaked 4 KiB buffer shows. The heap is measured on macOS and glibc only; elsewhere it prints
 * "unsupported". Argument 1 is the file to write, argument 2 a directory. */
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
static long long in_use(void) { return -1; }
#endif

enum { SIZE = 1 << 20 };

int main(int argc, char** argv) {
    (void)argc;
    const char* path = argv[1];
    const char* dir = argv[2];
    if (in_use() < 0) {
        printf("unsupported\n");
        return 0;
    }
    FILE* f = fopen(path, "wb");
    if (!f) return 1;
    uint8_t* bytes = malloc(SIZE);
    if (!bytes) return 1;
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
    enum { ROUNDS = 1000 };
    uint32_t dir_err = 0;
    before = in_use();
    for (int i = 0; i < ROUNDS; i++) dir_err = byn_io_read_text((byn_str){(const uint8_t*)dir, (int64_t)strlen(dir)}, &text, &byn_general);
    long long dir_grew = in_use() - before;
    uint8_t* raw;
    int64_t raw_len;
    uint32_t dir_stdin_err = 0;
    before = in_use();
    for (int i = 0; i < ROUNDS; i++) {
        if (!freopen(dir, "rb", stdin)) return 1;
        dir_stdin_err = byn_io_read_stdin_bytes(&raw, &raw_len, &byn_general);
    }
    long long dir_stdin_grew = in_use() - before;
    printf("file %u leaked %d, stdin %u leaked %d, dir failed %d leaked %d, dir stdin failed %d leaked %d\n", file_err, file_grew >= SIZE / 2, stdin_err,
           stdin_grew >= SIZE / 2, dir_err != 0, dir_grew >= ROUNDS * 2048, dir_stdin_err != 0, dir_stdin_grew >= ROUNDS * 2048);
    return 0;
}
