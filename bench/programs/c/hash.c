#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>

static uint64_t now(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (uint64_t)ts.tv_sec * 1000000000u + (uint64_t)ts.tv_nsec;
}

int main(void) {
    const int64_t size = 8388608, passes = 16;
    uint8_t *buf = malloc((size_t)size);
    uint64_t state = 12345;
    for (int64_t i = 0; i < size; i++) {
        state = state * 6364136223846793005ull + 1442695040888963407ull;
        buf[i] = (uint8_t)(state >> 56);
    }
    uint64_t start = now();
    uint64_t h = 14695981039346656037ull;
    for (int64_t p = 0; p < passes; p++)
        for (int64_t i = 0; i < size; i++) h = (h ^ buf[i]) * 1099511628211ull;
    uint64_t elapsed = now() - start;
    printf("%llu %llu\n", (unsigned long long)elapsed, (unsigned long long)h);
    return 0;
}
