// Builds the website into staticsite/_site:
//  - the top-level pages, with the shared head, header and footer put in (<!-- @include name --> ->
//    partials/name.html) and the screenshots they use (img/shots/x.png <- docs/screenshots/blazor/x.png,
//    img/shots/win-x.png <- docs/screenshots/x.png, img/shots/mac-x.png and linux-x.png <-
//    docs/screenshots/desktop/);
//  - the user guide (docs/...), from the Markdown in the repository's docs folder plus the site's own
//    pages in content/docs, with a contents list, page outline, previous/next and search;
//  - the tutorials (tutorials/...), from content/tutorials;
//  - data/search.json for the search box, the assets and the release data.
// Partials write links as {{root}}page.html so they work from the top level and from a folder.
//   npm ci --prefix staticsite && node staticsite/tools/build.mjs     then serve staticsite/_site
import { readFile, writeFile, mkdir, cp, readdir, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, posix } from 'node:path';
import { marked } from 'marked';

const site = join(dirname(fileURLToPath(import.meta.url)), '..');
const repo = join(site, '..');
const out = join(site, '_site');
const GITHUB = 'https://github.com/dotnetappdev/Pdfedit';
const BRANCH = 'devmain';
await rm(out, { recursive: true, force: true });
await mkdir(join(out, 'img/shots'), { recursive: true });

const partials = {};
for (const f of await readdir(join(site, 'partials'))) partials[f.replace(/\.html$/, '')] = await readFile(join(site, 'partials', f), 'utf8');
const include = (html, where) => html.replace(/<!--\s*@include\s+(\w+)\s*-->/g, (_, name) => {
  if (!(name in partials)) throw new Error(`${where}: no partial "${name}"`);
  return partials[name];
});
const withRoot = (html, root) => html.replaceAll('{{root}}', root);
const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', '#39': "'", nbsp: ' ' };
const strip = html => html.replace(/<strong class="callout-title">(\w+)<\/strong>/g, '$1: ').replace(/<\/?(strong|em|b|i|code|a|kbd|span|mark|sup|sub)\b[^>]*>/gi, '').replace(/<[^>]+>/g, ' ').replace(/&([a-z]+|#\d+);/gi, (m, e) => ENTITIES[e.toLowerCase()] ?? ' ').replace(/\s+/g, ' ').trim();
const slugify = s => strip(s).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'section';

// Top-level pages
const shots = new Set();
let pageCount = 0;
for (const f of (await readdir(site)).filter(f => f.endsWith('.html'))) {
  const html = withRoot(include(await readFile(join(site, f), 'utf8'), f), '');
  for (const m of html.matchAll(/img\/shots\/([\w.-]+\.png)/g)) shots.add(m[1]);
  await writeFile(join(out, f), html);
  pageCount++;
}
for (const name of shots) {
  const src = name.startsWith('win-') ? join(repo, 'docs/screenshots', name.slice(4))
    : /^(mac|linux)-/.test(name) ? join(repo, 'docs/screenshots/desktop', name)
    : join(repo, 'docs/screenshots/blazor', name);
  if (!existsSync(src)) throw new Error(`screenshot not found: ${src}`);
  await cp(src, join(out, 'img/shots', name));
}

// The user guide: what's in it and in what order
// src is relative to the repository; title overrides the Markdown's own heading.
const GUIDE = [
  ['Getting started', [
    { slug: 'install', src: 'staticsite/content/docs/install.md' },
    { slug: 'getting-started', src: 'staticsite/content/docs/getting-started.md' },
    { slug: 'features', src: 'docs/features.md', title: 'Everything PdfEdit does' },
    { slug: 'tour', src: 'docs/tour.md' },
  ]],
  ['Using PdfEdit', [
    { slug: 'filling-and-signing', src: 'docs/filling-and-signing.md' },
    { slug: 'forms', src: 'docs/forms.md' },
    { slug: 'comments-and-markup', src: 'docs/comments-and-markup.md' },
    { slug: 'pages-and-security', src: 'docs/pages-and-security.md' },
    { slug: 'scan-and-ocr', src: 'docs/scan-and-ocr.md' },
    { slug: 'design-canvas', src: 'docs/design-canvas.md' },
    { slug: 'import-export-and-cloud', src: 'docs/import-export-and-cloud.md' },
    { slug: 'automation', src: 'docs/automation.md' },
    { slug: 'ai-assistant', src: 'docs/ai-assistant.md' },
  ]],
  ['Make it yours', [
    { slug: 'themes', src: 'docs/themes.md' },
    { slug: 'accessibility', src: 'docs/accessibility.md' },
    { slug: 'keyboard-shortcuts', src: 'docs/keyboard-shortcuts.md' },
  ]],
  ['Help', [
    { slug: 'troubleshooting', src: 'staticsite/content/docs/troubleshooting.md' },
  ]],
  ['Hosting and building', [
    { slug: 'web-version', src: 'PdfEdit.Blazor/README.md', title: 'Hosting PdfEdit for the web' },
    { slug: 'building', src: 'docs/building.md' },
    { slug: 'architecture', src: 'docs/architecture.md' },
  ]],
];
const guidePages = GUIDE.flatMap(([section, pages]) => pages.map(p => Object.assign(p, { section })));
const bySource = new Map(guidePages.map(p => [p.src, p]));

// Tutorials
const TUTORIAL_GROUPS = ['Forms and signing', 'Pages and documents', 'Review and markup', 'Scanning and converting', 'Working faster', 'Setting up'];
const tutorials = [];
for (const f of (await readdir(join(site, 'content/tutorials'))).filter(f => f.endsWith('.md')).sort()) {
  const raw = await readFile(join(site, 'content/tutorials', f), 'utf8');
  const fm = raw.match(/^---\n([\s\S]*?)\n---\n/);
  if (!fm) throw new Error(`content/tutorials/${f}: no front matter`);
  const meta = Object.fromEntries(fm[1].split('\n').filter(Boolean).map(l => [l.slice(0, l.indexOf(':')).trim(), l.slice(l.indexOf(':') + 1).trim()]));
  for (const k of ['title', 'summary', 'group', 'level', 'time', 'apps', 'order'])
    if (!meta[k]) throw new Error(`content/tutorials/${f}: front matter needs "${k}"`);
  if (!TUTORIAL_GROUPS.includes(meta.group)) throw new Error(`content/tutorials/${f}: unknown group "${meta.group}"`);
  tutorials.push({ ...meta, order: +meta.order, slug: f.replace(/\.md$/, ''), src: `staticsite/content/tutorials/${f}`, body: raw.slice(fm[0].length) });
}
tutorials.sort((a, b) => TUTORIAL_GROUPS.indexOf(a.group) - TUTORIAL_GROUPS.indexOf(b.group) || a.order - b.order);
const tutorialBySource = new Map(tutorials.map(t => [t.src, t]));

// Markdown -> page HTML
marked.setOptions({ gfm: true });
const media = new Set();   // repository files (pictures, video) the pages show

/** Where a link or picture in `src` (a repository path) should point from a page one folder down. */
function resolveLink(target, src) {
  if (/^([a-z]+:|#|\/\/)/i.test(target)) return target;
  const [path, hash = ''] = target.split('#');
  const anchor = hash ? '#' + hash : '';
  const p = posix.normalize(posix.join(posix.dirname(src), decodeURIComponent(path)));
  if (!path) return anchor;
  if (bySource.has(p)) return `../docs/${bySource.get(p).slug}.html${anchor}`;
  if (tutorialBySource.has(p)) return `../tutorials/${tutorialBySource.get(p).slug}.html${anchor}`;
  // The site's own pages can name a guide page or tutorial by its file name alone (forms.md).
  const name = posix.basename(p, '.md');
  if (p.endsWith('.md') && !path.includes('/')) {
    const g = guidePages.find(x => x.slug === name), t = tutorials.find(x => x.slug === name);
    if (g) return `../docs/${g.slug}.html${anchor}`;
    if (t) return `../tutorials/${t.slug}.html${anchor}`;
  }
  if (/\.(png|gif|jpe?g|svg|webp|mp4)$/i.test(p) && existsSync(join(repo, p))) { media.add(p); return `../img/repo/${p}`; }
  // The site's own pages: content/docs/x -> docs/x, content/tutorials/x -> tutorials/x, x.html -> x.html.
  if (p.startsWith('staticsite/')) return `../${p.slice('staticsite/'.length).replace(/^content\//, '')}${anchor}`;
  return `${GITHUB}/${existsSync(join(repo, p)) && !/\.\w+$/.test(p) ? 'tree' : 'blob'}/${BRANCH}/${p}${anchor}`;
}

function renderMarkdown(md, src) {
  md = md.replace(/^\[(\u2190\s*)?Back to README\]\([^)]*\)\s*$/gm, '');   // "Back to README" lines
  const title = md.match(/^#\s+(.+)$/m)?.[1].trim();
  md = md.replace(/^#\s+.+$/m, '');                                 // the page title is drawn separately
  let html = marked.parse(md);
  html = html.replace(/\b(href|src)="([^"]+)"/g, (_, attr, url) => `${attr}="${esc(resolveLink(url.replace(/&amp;/g, '&'), src))}"`);
  // Headings get ids for links and the outline.
  const used = new Set(), outline = [];
  html = html.replace(/<h([23])>([\s\S]*?)<\/h\1>/g, (_, level, inner) => {
    let id = slugify(inner), n = 2;
    while (used.has(id)) id = `${slugify(inner)}-${n++}`;
    used.add(id);
    outline.push({ level: +level, id, text: strip(inner) });
    return `<h${level} id="${id}">${inner}<a class="anchor" href="#${id}" aria-label="Link to this section">#</a></h${level}>`;
  });
  // Tip / Note / Important / Warning call-outs: a quote that starts with the word in bold.
  html = html.replace(/<blockquote>\s*<p><strong>(Tip|Note|Important|Warning|Mac|Windows|Web)(?::)?<\/strong>:?/g,
    (_, kind) => `<blockquote class="callout ${kind.toLowerCase()}"><p><strong class="callout-title">${kind}</strong>`)
    .replace(/(<strong class="callout-title">\w+<\/strong>)\s*([a-z])/g, (_, t, c) => t + c.toUpperCase());
  html = html.replace(/<table>/g, '<div class="table-wrap"><table>').replace(/<\/table>/g, '</table></div>');
  html = html.replace(/<img /g, '<img loading="lazy" ');
  return { title, html, outline };
}

// Page shells
const shell = ({ title, description, page, body }) => withRoot(include(`<!doctype html>
<html lang="en">
<head>
<title>${esc(title)} | PdfEdit</title>
<meta name="description" content="${esc(description)}">
<!-- @include head -->
</head>
<body data-page="${page}">
<!-- @include header -->
<main id="main">
${body}
</main>
<!-- @include footer -->
</body>
</html>
`, title), '../');

const searchBox = placeholder => `
  <div class="search" data-search>
    <label class="sr-only" for="q-${placeholder.length}">Search the docs</label>
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
    <input id="q-${placeholder.length}" type="search" placeholder="${esc(placeholder)}" autocomplete="off" data-search-input aria-controls="r-${placeholder.length}">
    <kbd>/</kbd>
    <div class="search-results" id="r-${placeholder.length}" data-search-results hidden></div>
  </div>`;

function sidebar(current) {
  const guide = GUIDE.map(([section, pages]) => `
      <h3>${esc(section)}</h3>
      <ul>${pages.map(p => `<li><a href="../docs/${p.slug}.html"${p.slug === current ? ' aria-current="page"' : ''}>${esc(p.navTitle)}</a></li>`).join('')}</ul>`).join('');
  return `
    <nav class="docs-nav" aria-label="User guide">
      <details open>
        <summary>Contents</summary>
        <a class="docs-home" href="../docs/index.html"${current === 'index' ? ' aria-current="page"' : ''}>User guide home</a>
        ${guide}
        <h3>Learn by doing</h3>
        <ul><li><a href="../tutorials/index.html"${current === 'tutorials' ? ' aria-current="page"' : ''}>All tutorials</a></li></ul>
      </details>
    </nav>`;
}

const outlineHtml = outline => outline.length < 2 ? '' : `
    <aside class="docs-toc" aria-label="On this page">
      <h3>On this page</h3>
      <ul>${outline.map(o => `<li class="l${o.level}"><a href="#${o.id}">${esc(o.text)}</a></li>`).join('')}</ul>
    </aside>`;

const pager = (prev, next) => `
      <nav class="pager" aria-label="More pages">
        ${prev ? `<a class="prev" href="${prev.href}"><small>Previous</small>${esc(prev.title)}</a>` : '<span></span>'}
        ${next ? `<a class="next" href="${next.href}"><small>Next</small>${esc(next.title)}</a>` : '<span></span>'}
      </nav>`;

const editLink = src => `<p class="edit"><a href="${GITHUB}/edit/${BRANCH}/${src}">Improve this page on GitHub</a> | <a href="${GITHUB}/issues/new">Report a problem</a></p>`;

const search = [];
const searchText = html => strip(html.replace(/<a class="anchor"[^>]*>#<\/a>/g, '')).slice(0, 6000);
// The guide cut into sections for "Ask the guide": each heading with its text, so the chat can
// send the few passages a question needs. Long sections are split.
const chunks = [];
function addChunks(html, url, title, where) {
  const parts = html.replace(/<a class="anchor"[^>]*>#<\/a>/g, '').split(/(?=<h[23] id=")/);
  for (const part of parts) {
    const m = part.match(/^<h[23] id="([^"]+)"[^>]*>([\s\S]*?)<\/h[23]>/);
    const text = strip(m ? part.slice(m[0].length) : part);
    if (text.length < 40) continue;
    const heading = m ? strip(m[2]) : '';
    for (let i = 0; i < text.length; i += 1600)
      chunks.push({ t: title, h: heading, s: where, u: url + (m ? '#' + m[1] : ''), x: text.slice(i, i + 1800) });
  }
}
const firstParagraph = html => strip(html.match(/<p>([\s\S]*?)<\/p>/)?.[1] ?? '').slice(0, 200);

// Build the user guide
await mkdir(join(out, 'docs'), { recursive: true });
await mkdir(join(out, 'tutorials'), { recursive: true });
for (const p of guidePages) {
  const r = renderMarkdown(await readFile(join(repo, p.src), 'utf8'), p.src);
  p.title = p.title ?? r.title ?? p.slug;
  p.navTitle = p.title.replace(/^PdfEdit for the web \(Blazor\)$/, 'Hosting the web version');
  p.rendered = r;
}
for (const [i, p] of guidePages.entries()) {
  const prev = guidePages[i - 1], next = guidePages[i + 1];
  const body = `
  <div class="wrap docs">
    ${sidebar(p.slug)}
    <article class="prose" data-search-scope>
      <p class="crumbs"><a href="index.html">User guide</a> > ${esc(p.section)}</p>
      <h1>${esc(p.title)}</h1>
      ${p.rendered.html}
      ${editLink(p.src)}
      ${pager(prev && { href: `${prev.slug}.html`, title: prev.title }, next && { href: `${next.slug}.html`, title: next.title })}
    </article>
    ${outlineHtml(p.rendered.outline)}
  </div>`;
  const description = firstParagraph(p.rendered.html) || `${p.title}: PdfEdit user guide.`;
  await writeFile(join(out, 'docs', `${p.slug}.html`), shell({ title: p.title, description, page: 'docs', body }));
  addChunks(p.rendered.html, `docs/${p.slug}.html`, p.title, `User guide | ${p.section}`);
  search.push({ t: p.title, u: `docs/${p.slug}.html`, s: `User guide | ${p.section}`, d: description,
    h: p.rendered.outline.map(o => [o.text, o.id]), x: searchText(p.rendered.html) });
}

// The user guide's front page.
const icons = {
  'Getting started': '<path d="M5 12h14M13 6l6 6-6 6"/>',
  'Using PdfEdit': '<path d="M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4"/>',
  'Make it yours': '<circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9 7 7M17 17l2.1 2.1M4.9 19.1 7 17M17 7l2.1-2.1"/>',
  'Help': '<circle cx="12" cy="12" r="9"/><path d="M9.5 9a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6V14M12 17h.01"/>',
  'Hosting and building': '<path d="M8 9l-4 3 4 3M16 9l4 3-4 3M13 6l-2 12"/>',
};
await writeFile(join(out, 'docs', 'index.html'), shell({
  title: 'User guide', page: 'docs',
  description: 'How to use PdfEdit on Windows, Mac and the web: filling and signing, forms, comments, pages, scanning, design, conversion and more.',
  body: `
  <div class="wrap docs">
    ${sidebar('index')}
    <div class="docs-main">
      <section class="docs-hero">
        <h1>PdfEdit user guide</h1>
        <p class="lead">How everything works in PdfEdit for Windows, PdfEdit for Mac and PdfEdit for the web. Search, or start with a section below.</p>
        ${searchBox('Search the guide and tutorials, e.g. signature, merge, OCR')}
        <p class="quick"><button type="button" class="btn small" data-ask-open>Ask a question</button> Popular: <a href="filling-and-signing.html">Fill and sign</a> | <a href="forms.html">Make a form fillable</a> | <a href="pages-and-security.html#passwords-and-redaction">Redact</a> | <a href="scan-and-ocr.html">Scan and OCR</a> | <a href="keyboard-shortcuts.html">Shortcuts</a></p>
      </section>
      <div class="doc-cards">
        ${GUIDE.map(([section, pages]) => `
        <section class="doc-card">
          <div class="ico"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${icons[section]}</svg></div>
          <h2>${esc(section)}</h2>
          <ul>${pages.map(p => `<li><a href="${p.slug}.html">${esc(p.navTitle)}</a></li>`).join('')}</ul>
        </section>`).join('')}
      </div>
      <section class="box tutorials-teaser">
        <h2>Learn by doing: tutorials</h2>
        <div class="body">
          <p>Step-by-step walkthroughs of the jobs people do most, from signing a form to scanning a stack of paper.</p>
          <ul class="tut-mini">${tutorials.slice(0, 6).map(t => `<li><a href="../tutorials/${t.slug}.html">${esc(t.title)}</a> <span class="muted">| ${esc(t.time)}</span></li>`).join('')}</ul>
          <a class="btn small" href="../tutorials/index.html">All ${tutorials.length} tutorials</a>
        </div>
      </section>
    </div>
  </div>`,
}));

// Build the tutorials
const appBadges = apps => apps.split(',').map(a => `<span class="app-badge">${esc(a.trim())}</span>`).join('');
const tutorialSidebar = current => `
    <nav class="docs-nav" aria-label="Tutorials">
      <details open>
        <summary>Tutorials</summary>
        <a class="docs-home" href="index.html"${current === 'index' ? ' aria-current="page"' : ''}>All tutorials</a>
        ${TUTORIAL_GROUPS.filter(g => tutorials.some(t => t.group === g)).map(g => `
        <h3>${esc(g)}</h3>
        <ul>${tutorials.filter(t => t.group === g).map(t => `<li><a href="${t.slug}.html"${t.slug === current ? ' aria-current="page"' : ''}>${esc(t.title)}</a></li>`).join('')}</ul>`).join('')}
        <h3>Reference</h3>
        <ul><li><a href="../docs/index.html">User guide</a></li></ul>
      </details>
    </nav>`;

for (const [i, t] of tutorials.entries()) {
  const r = renderMarkdown(t.body, t.src);
  // Steps are numbered; the sections round them (before you start, what's next...) aren't.
  r.html = r.html.replace(/<h2 id="((?:before-you-start|what-you-ll-need|what-s-next|next-steps|troubleshooting|if-something-goes-wrong)[\w-]*)">/g, '<h2 id="$1" class="plain">');
  const prev = tutorials[i - 1], next = tutorials[i + 1];
  const body = `
  <div class="wrap docs">
    ${tutorialSidebar(t.slug)}
    <article class="prose tutorial" data-search-scope>
      <p class="crumbs"><a href="index.html">Tutorials</a> > ${esc(t.group)}</p>
      <h1>${esc(t.title)}</h1>
      <p class="lead">${esc(t.summary)}</p>
      <div class="tut-meta">
        <span><b>Level</b> ${esc(t.level)}</span>
        <span><b>Time</b> ${esc(t.time)}</span>
        <span><b>Works in</b> ${appBadges(t.apps)}</span>
      </div>
      ${r.html}
      ${editLink(t.src)}
      ${pager(prev && { href: `${prev.slug}.html`, title: prev.title }, next && { href: `${next.slug}.html`, title: next.title })}
    </article>
    ${outlineHtml(r.outline)}
  </div>`;
  await writeFile(join(out, 'tutorials', `${t.slug}.html`), shell({ title: t.title, description: t.summary, page: 'tutorials', body }));
  addChunks(`<p>${esc(t.summary)}</p>` + r.html, `tutorials/${t.slug}.html`, t.title, `Tutorial | ${t.group}`);
  search.push({ t: t.title, u: `tutorials/${t.slug}.html`, s: `Tutorial | ${t.group}`, d: t.summary,
    h: r.outline.map(o => [o.text, o.id]), x: searchText(r.html) });
}

await writeFile(join(out, 'tutorials', 'index.html'), shell({
  title: 'Tutorials', page: 'tutorials',
  description: 'Step-by-step PdfEdit tutorials: fill and sign forms, make forms fillable, merge and split, redact, scan, convert and more.',
  body: `
  <div class="wrap docs">
    ${tutorialSidebar('index')}
    <div class="docs-main">
      <section class="docs-hero">
        <h1>Tutorials</h1>
        <p class="lead">Step-by-step walkthroughs, each a few minutes long. They work the same in PdfEdit for Windows, for Mac and on the web unless a step says otherwise.</p>
        ${searchBox('Search the tutorials and guide')}
        <p class="quick"><button type="button" class="btn small" data-ask-open>Ask a question</button> Not sure which tutorial you need? Ask, and get an answer with links.</p>
      </section>
      ${TUTORIAL_GROUPS.filter(g => tutorials.some(t => t.group === g)).map(g => `
      <section class="section">
        <div class="section-head"><h2>${esc(g)}</h2></div>
        <div class="tut-grid">
          ${tutorials.filter(t => t.group === g).map(t => `
          <a class="tut-card" href="${t.slug}.html">
            <span class="tut-level ${t.level.toLowerCase()}">${esc(t.level)}</span>
            <h3>${esc(t.title)}</h3>
            <p>${esc(t.summary)}</p>
            <span class="tut-foot"><span>${esc(t.time)}</span><span>${esc(t.apps)}</span></span>
          </a>`).join('')}
        </div>
      </section>`).join('')}
    </div>
  </div>`,
}));

// Pictures the docs use, assets, data
for (const p of media) await cp(join(repo, p), join(out, 'img/repo', p));
await cp(join(site, 'assets'), join(out, 'assets'), { recursive: true });
await cp(join(site, 'data'), join(out, 'data'), { recursive: true });
// The "Ask the guide" assistant's address can come from the build (the ASK_ENDPOINT repository variable).
if (process.env.ASK_ENDPOINT) {
  const settings = JSON.parse(await readFile(join(out, 'data', 'site.json'), 'utf8'));
  settings.chatEndpoint = process.env.ASK_ENDPOINT;
  await writeFile(join(out, 'data', 'site.json'), JSON.stringify(settings, null, 2));
}
await writeFile(join(out, 'data', 'search.json'), JSON.stringify(search));
await writeFile(join(out, 'data', 'chunks.json'), JSON.stringify(chunks.map((c, i) => ({ i, ...c }))));
await writeFile(join(out, '.nojekyll'), '');
console.log(`_site: ${chunks.length} chat passages, ${pageCount} pages, ${guidePages.length + 1} guide pages, ${tutorials.length + 1} tutorial pages, ${shots.size} screenshots, ${media.size} doc pictures`);
