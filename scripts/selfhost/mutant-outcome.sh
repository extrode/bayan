# Sourced by guard.sh. mutant_outcome <test exit code> <test log> prints `killed`, `survived`, `uncompiled` or `timeout`.
# Exit code 124 is the guard's own mutant timeout: it counts as killed but is reported apart.
# A mutant the Bayan compiler rejects makes the harness fail every test with "… did not build"; that proves nothing
# about the tests, so it must not count as killed. The log needs `--logger 'console;verbosity=normal'`.
mutant_outcome() {
  if [ "$1" = 124 ]; then echo timeout
  elif [ "$1" = 0 ]; then echo survived
  elif grep -q 'did not build' "$2"; then echo uncompiled
  else echo killed; fi
}
