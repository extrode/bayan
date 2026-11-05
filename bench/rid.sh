# Sourced by bench/run.sh, bench/baseline.sh and bench/friction.sh: the NativeAOT runtime identifier and the
# name of the published bayan executable for a platform.

# bench_rid <uname -s> <uname -m>: prints the runtime identifier, or returns 1 when there is none.
bench_rid() {
    case "$1-$2" in
        Darwin-arm64) echo osx-arm64 ;;
        Darwin-x86_64) echo osx-x64 ;;
        Linux-x86_64) echo linux-x64 ;;
        Linux-aarch64) echo linux-arm64 ;;
        MINGW*-x86_64|MSYS*-x86_64|CYGWIN*-x86_64) echo win-x64 ;;
        *) return 1 ;;
    esac
}

# bench_exe <uname -s>: file name of the published bayan.
bench_exe() {
    case "$1" in
        MINGW*|MSYS*|CYGWIN*) echo bayan.exe ;;
        *) echo bayan ;;
    esac
}
