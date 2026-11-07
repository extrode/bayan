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
    const int64_t target = 4194304, passes = 8;
    uint8_t *buf = malloc((size_t)target + 64);
    int64_t n = 0;
    uint64_t state = 777;
    while (n < target) {
        for (int field = 0; field < 3; field++) {
            state = state * 6364136223846793005ull + 1442695040888963407ull;
            uint64_t v = (state >> 33) % 1000000;
            uint8_t digits[8];
            int d = 0;
            if (v == 0) digits[d++] = 48;
            while (v > 0) { digits[d++] = (uint8_t)(48 + v % 10); v /= 10; }
            while (d > 0) buf[n++] = digits[--d];
            buf[n++] = field == 2 ? 10 : 44;
        }
    }
    uint64_t start = now();
    uint64_t sum = 0;
    for (int64_t p = 0; p < passes; p++) {
        uint64_t value = 0;
        for (int64_t i = 0; i < n; i++) {
            uint8_t c = buf[i];
            if (c >= 48 && c <= 57) value = value * 10 + (uint64_t)(c - 48);
            else { sum = sum * 31 + value; value = 0; }
        }
    }
    uint64_t elapsed = now() - start;
    printf("%llu %llu\n", (unsigned long long)elapsed, (unsigned long long)sum);
    return 0;
}
