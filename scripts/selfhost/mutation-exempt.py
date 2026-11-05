#!/usr/bin/env python3
# mutation-exempt.py <file.byn> <handoff.md> — the lines guard.sh's mutation step skips in <file.byn>.
# `// mutation-exempt: Qnnn` exempts the next line; `// mutation-exempt: Qnnn through N` exempts the next line to N.
# Each marker's Question must cite <file>:L (or a range) with L inside what the marker exempts, so every exempted
# span is named in the handoff with the stub that blocks it. Prints `skip <line>` and `note <message>` lines.
import re, sys, os

path, handoff = sys.argv[1], sys.argv[2]
lines = open(path, encoding='utf-8').read().split('\n')
questions = {}
for q in open(handoff, encoding='utf-8').read().split('\n'):
    m = re.match(r'(\d{3,4})\. ', q)
    if m: questions[m.group(1)] = q
base = os.path.basename(path)
for i, text in enumerate(lines, 1):
    m = re.search(r'// mutation-exempt: Q(\d{3,4})(?: through (\d+))?', text)
    if not m: continue
    q, last = m.group(1), int(m.group(2) or i + 1)
    if last <= i or last > len(lines):
        print(f'note mutation: {path}:{i} exempts through {last}, which is not after the marker and inside the file')
        continue
    cited = [(int(a), int(b or a)) for a, b in re.findall(re.escape(base) + r':(\d+)(?:-(\d+))?', questions.get(q, ''))]
    if not any(a <= last and b >= i + 1 for a, b in cited):
        print(f'note mutation: {path}:{i} exempts {i + 1}-{last} for Q{q}, but Q{q} cites no {base} line in that span')
        continue
    for n in range(i + 1, last + 1): print(f'skip {n}')
