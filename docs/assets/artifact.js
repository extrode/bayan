// Shared behaviour for generated artifacts. Deliberately tiny and dependency
// free: it is loaded from a sibling file with no network access, and anything
// that needs a framework does not belong in a generated document.
//
// Canonical copy: ~/.claude/tools/html/assets/artifact.js
// Synced into each repo at docs/assets/artifact.js by tools/html/build.js.
'use strict';

// Mark the section currently on screen in the contents rail. IntersectionObserver
// rather than a scroll handler, so this costs nothing while the page sits still.
(function markCurrentSection() {
  const links = Array.from(document.querySelectorAll('.toc a[href^="#"]'));
  if (!links.length || !('IntersectionObserver' in window)) return;

  const byId = new Map();
  for (const a of links) {
    const el = document.getElementById(decodeURIComponent(a.hash.slice(1)));
    if (el) byId.set(el, a);
  }
  if (!byId.size) return;

  let current = null;
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      const a = byId.get(e.target);
      if (!a || a === current) continue;
      if (current) current.classList.remove('here');
      a.classList.add('here');
      current = a;
    }
  }, { rootMargin: '0px 0px -70% 0px', threshold: 0 });

  for (const el of byId.keys()) io.observe(el);
})();

// Copy-as-markdown. The markdown lives in a <template data-markdown> so the
// round trip is exact rather than reconstructed from the DOM: an HTML-to-text
// guess is where "copy as markdown" usually stops being trustworthy.
(function copyAsMarkdown() {
  for (const btn of document.querySelectorAll('[data-copy]')) {
    btn.addEventListener('click', async () => {
      const tpl = document.querySelector(`template[data-markdown="${btn.dataset.copy}"]`);
      if (!tpl) return;
      const text = tpl.innerHTML
        .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
      const label = btn.querySelector('[data-label]');
      try {
        await navigator.clipboard.writeText(text);
        btn.classList.add('done');
        if (label) label.textContent = 'Copied';
      } catch {
        // Clipboard access is refused on file:// in some browsers. Select the
        // text instead of failing silently, so there is still a way to get it.
        if (label) label.textContent = 'Press Ctrl+C';
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.setAttribute('style', 'position:fixed;top:0;left:0;opacity:0');
        document.body.appendChild(ta);
        ta.select();
      }
      setTimeout(() => {
        btn.classList.remove('done');
        if (label) label.textContent = btn.dataset.label || 'Copy as markdown';
      }, 2400);
    });
  }
})();
