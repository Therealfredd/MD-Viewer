// Preview page logic. Receives rendered HTML from the host, sanitizes it, inserts it,
// highlights code lazily and reports link clicks back to the host. Content from the
// Markdown file never executes script: it is sanitized here and the page CSP only
// allows scripts served from https://app.mdviewer.
(function () {
  'use strict';

  const content = document.getElementById('content');
  const baseEl = document.getElementById('base');
  const LOCAL_ROOT = 'https://local.mdviewer/';
  const post = (m) => window.chrome.webview.postMessage(m);

  let blocks = [];          // top-level elements carrying data-line, in document order
  let totalLines = 0;       // line count of the source document
  let hljsState = 0;        // 0 = not loaded, 1 = loading, 2 = ready
  const pendingCode = new Set();

  // ---------------------------------------------------------------- sanitizing

  const DROP_TAGS = new Set([
    'script', 'style', 'link', 'meta', 'base', 'title', 'iframe', 'frame', 'frameset', 'object', 'embed',
    'applet', 'form', 'noscript', 'template', 'portal', 'slot', 'dialog',
    'animate', 'animatemotion', 'animatetransform', 'set', 'foreignobject', 'handler', 'listener'
  ]);
  const URL_ATTRS = new Set(['href', 'src', 'xlink:href', 'poster', 'action', 'formaction', 'background', 'cite', 'longdesc', 'data', 'manifest', 'ping']);
  const UNSAFE_URL = /^[\s\u0000-\u001f]*(javascript|vbscript|data|blob|filesystem)\s*:/i;
  const SAFE_DATA_IMAGE = /^data:image\/(png|jpe?g|gif|webp|bmp|avif|svg\+xml)[;,]/i;

  function sanitize(root) {
    const doomed = [];
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT);
    for (let el = walker.nextNode(); el; el = walker.nextNode()) {
      const tag = el.localName.toLowerCase();
      if (DROP_TAGS.has(tag)) { doomed.push(el); continue; }

      for (const attr of Array.from(el.attributes)) {
        const name = attr.name.toLowerCase();
        if (name.startsWith('on') || name === 'srcset' || name === 'autoplay' || name === 'formaction' ||
            name === 'ping' || name === 'srcdoc' || name === 'is' || name === 'http-equiv') {
          el.removeAttribute(attr.name);
          continue;
        }
        if (URL_ATTRS.has(name) || name.endsWith(':href')) {
          const v = attr.value;
          if (UNSAFE_URL.test(v) && !(name === 'src' && tag === 'img' && SAFE_DATA_IMAGE.test(v.trim()))) {
            el.removeAttribute(attr.name);
          }
        }
      }

      if (tag === 'img' || tag === 'video' || tag === 'audio' || tag === 'source') {
        fixLocalSrc(el, 'src');
        if (tag === 'video') fixLocalSrc(el, 'poster');
        if (tag === 'img') { el.loading = 'lazy'; el.decoding = 'async'; }
      } else if (tag === 'input') {
        el.disabled = true;
      } else if (tag === 'a') {
        el.removeAttribute('target');
      }
    }
    for (const el of doomed) el.remove();
  }

  // Encode a Windows path the same way the host's LocalUrl.FromPath does.
  function localUrl(path) {
    let p = path.replace(/\\/g, '/');
    let prefix = '';
    if (p.startsWith('//')) { prefix = 'UNC/'; p = p.slice(2); }
    return LOCAL_ROOT + prefix + p.split('/').map(encodeURIComponent).join('/');
  }

  // Absolute Windows paths, file:// URLs and backslash-relative paths -> local virtual host.
  function fixLocalSrc(el, attr) {
    const v = (el.getAttribute(attr) || '').trim();
    if (!v) return;
    let out = null;
    if (/^[a-zA-Z]:[\\/]/.test(v) || /^\\\\/.test(v)) {
      out = localUrl(v);
    } else if (/^file:/i.test(v)) {
      try {
        const u = new URL(v);
        const p = decodeURIComponent(u.pathname);
        out = u.host ? localUrl('//' + u.host + p) : localUrl(p.replace(/^\/+/, ''));
      } catch (_) { out = null; }
      if (!out) { el.removeAttribute(attr); return; }
    } else if (v.includes('\\') && !/^[a-z][a-z0-9+.-]*:/i.test(v)) {
      out = new URL(v.replace(/\\/g, '/'), baseEl.href).href;
    }
    if (out) el.setAttribute(attr, out);
  }

  // ---------------------------------------------------------------- syntax highlighting (lazy)

  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) {
        observer.unobserve(entry.target);
        highlight(entry.target);
      }
    }
  }, { rootMargin: '1200px 0px' });

  function loadHighlighter() {
    if (hljsState) return;
    hljsState = 1;
    const s = document.createElement('script');
    s.src = 'https://app.mdviewer/highlight.min.js';
    s.onload = () => {
      hljsState = 2;
      const els = Array.from(pendingCode);
      pendingCode.clear();
      for (const el of els) if (el.isConnected) highlight(el);
    };
    document.head.appendChild(s);
  }

  function highlight(code) {
    if (code.dataset.hl) return;
    const m = /(?:^|\s)language-(\S+)/.exec(code.className);
    if (!m) return;
    if (!window.hljs) { pendingCode.add(code); loadHighlighter(); return; }
    code.dataset.hl = '1';
    const lang = m[1].toLowerCase();
    if (!window.hljs.getLanguage(lang)) return;
    const text = code.textContent;
    if (text.length > 400000) return; // keep huge blocks responsive
    try {
      code.innerHTML = window.hljs.highlight(text, { language: lang, ignoreIllegals: true }).value;
      code.classList.add('hljs');
    } catch (_) { /* leave unhighlighted */ }
  }

  // ---------------------------------------------------------------- rendering & scrolling

  function render(m) {
    baseEl.href = m.base || LOCAL_ROOT;
    totalLines = m.lines || 0;
    const tpl = document.createElement('template');
    tpl.innerHTML = m.html;               // inert: nothing loads or runs inside a template
    sanitize(tpl.content);

    const y = window.scrollY;
    observer.disconnect();
    pendingCode.clear();
    content.replaceChildren(tpl.content);
    blocks = Array.from(content.children).filter((e) => e.hasAttribute('data-line'));
    for (const code of content.querySelectorAll('pre > code[class*="language-"]')) observer.observe(code);

    if (m.anchor && scrollToAnchor(m.anchor)) return;
    if (m.reset) window.scrollTo(0, 0);
    else if (typeof m.line === 'number') scrollToLine(m.line);
    else window.scrollTo(0, y);
  }

  function scrollToAnchor(id) {
    if (!id) return false;
    let target = document.getElementById(id) || document.getElementsByName(id)[0];
    if (!target) {
      try { target = document.getElementById(decodeURIComponent(id)); } catch (_) { /* bad escape */ }
    }
    if (!target) target = document.getElementById(id.toLowerCase());
    if (!target) return false;
    target.scrollIntoView({ block: 'start' });
    return true;
  }

  function topOf(el) { return el.getBoundingClientRect().top + window.scrollY; }

  // Align the preview with a 0-based source line, interpolating between tagged blocks.
  function scrollToLine(line) {
    if (!blocks.length) return;
    let lo = 0, hi = blocks.length - 1, idx = -1;
    while (lo <= hi) {
      const mid = (lo + hi) >> 1;
      if (+blocks[mid].dataset.line <= line) { idx = mid; lo = mid + 1; } else { hi = mid - 1; }
    }
    if (idx < 0) { window.scrollTo(0, 0); return; }
    const a = blocks[idx];
    const b = blocks[idx + 1];
    const la = +a.dataset.line;
    const ya = topOf(a);
    let y = ya;
    if (b) {
      const lb = +b.dataset.line;
      if (lb > la) y = ya + (topOf(b) - ya) * Math.min(1, (line - la) / (lb - la));
    } else if (totalLines > la) {
      // Last block: interpolate toward the end of the document.
      const end = content.offsetTop + content.offsetHeight - window.innerHeight + 16;
      if (end > ya) y = ya + (end - ya) * Math.min(1, (line - la) / (totalLines - la));
    }
    window.scrollTo(0, Math.max(0, y - 16));
  }

  // ---------------------------------------------------------------- links

  function onLinkClick(e) {
    const a = e.target.closest && e.target.closest('a[href]');
    if (!a) return;
    e.preventDefault();
    if (e.type === 'auxclick' && e.button !== 1) return;
    const href = a.getAttribute('href').trim();
    if (href.startsWith('#')) {
      let id = href.slice(1);
      try { id = decodeURIComponent(id); } catch (_) { /* keep raw */ }
      scrollToAnchor(id);
      return;
    }
    post({ type: 'link', href });
  }
  document.addEventListener('click', onLinkClick);
  document.addEventListener('auxclick', onLinkClick);
  // Esc is not a browser accelerator, so tell the host (closes the find bar).
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') post({ type: 'escape' }); });
  // Never let content be dragged out as a navigation of its own.
  document.addEventListener('dragstart', (e) => { if (e.target.closest && e.target.closest('a')) e.preventDefault(); });

  // ---------------------------------------------------------------- host messages

  window.chrome.webview.addEventListener('message', (e) => {
    const m = e.data;
    switch (m && m.type) {
      case 'render': render(m); break;
      case 'theme': document.documentElement.dataset.theme = m.dark ? 'dark' : 'light'; break;
      case 'line': scrollToLine(m.line); break;
      case 'anchor': scrollToAnchor(m.id); break;
    }
  });

  post({ type: 'ready' });
})();
