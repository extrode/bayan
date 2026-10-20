/* Prints `byn_batch_cmdline` for each case, one line each: the command line, or `refused`. */
#include "bayan-rt.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static byn_str s(const char* p) {
    return (byn_str){(const uint8_t*)p, (int64_t)strlen(p)};
}

static void show(const char* path, const char* arg) {
    byn_str a = s(arg);
    byn_str line;
    if (!byn_batch_cmdline(s(path), &a, 1, &line)) {
        printf("refused\n");
        return;
    }
    fwrite(line.ptr, 1, (size_t)line.len, stdout);
    printf("\n");
    free((void*)line.ptr);
}

int main(void) {
    show(".\\echo.cmd", "a b");
    show(".\\echo.cmd", "");
    show(".\\echo.cmd", "a\"b");
    show(".\\echo.cmd", "%PATH%");
    show(".\\echo.cmd", "^&|<>()!");
    show(".\\echo.cmd", "a\\");
    show(".\\echo.cmd", "a\rb");
    show(".\\echo.cmd", "a\nb");
    show("x.exe", "a");
    show("C:\\dir\\X.CMD", "a");
    show("./run.Bat", "a");
    show("100%.bat", "a");
    show("echo.cmd.", "a");
    show("echo.cmd", "a");
    show(".\\x.bat", "a");
    show("C:\\x\\y.cmd", "a");
    byn_str two[2] = {s("x"), s("y z")};
    byn_str line;
    if (byn_batch_cmdline(s(".\\echo.cmd"), two, 2, &line)) printf("%.*s\n", (int)line.len, (const char*)line.ptr);
    if (byn_batch_cmdline(s(".\\echo.cmd"), NULL, 0, &line)) printf("%.*s\n", (int)line.len, (const char*)line.ptr);
    byn_str nul = {(const uint8_t*)"a\0b", 3};
    printf("%s\n", byn_batch_cmdline(s(".\\echo.cmd"), &nul, 1, &line) ? "accepted" : "refused");
    return 0;
}
