// PdfEdit website: theme toggle, release data (versions, downloads, what's new) and the screenshot
// viewer. Release data comes from data/releases.json, which the Website workflow refreshes after
// every release; if it's missing, the GitHub API is asked directly.
(() => {
  'use strict';
  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => [...root.querySelectorAll(s)];

  // ── Current tab ──────────────────────────────────────────────────────────
  const page = document.body.dataset.page;
  $$('[data-tab]').forEach(a => { if (a.dataset.tab === page) a.setAttribute('aria-current', 'page'); });

  // ── Theme: Auto (follows the system) → Light → Dark ─────────────────────
  const KEY = 'pdfedit-site-theme';
  const label = { auto: 'Auto', light: 'Light', dark: 'Dark' };
  const current = () => document.documentElement.dataset.theme || 'auto';
  const showTheme = () => $$('[data-theme-label]').forEach(el => el.textContent = label[current()]);
  $$('[data-theme-toggle]').forEach(btn => btn.addEventListener('click', () => {
    const next = { auto: 'light', light: 'dark', dark: 'auto' }[current()];
    if (next === 'auto') delete document.documentElement.dataset.theme;
    else document.documentElement.dataset.theme = next;
    try { next === 'auto' ? localStorage.removeItem(KEY) : localStorage.setItem(KEY, next); } catch { /* private window */ }
    showTheme();
  }));
  showTheme();

  // ── Screenshot viewer ────────────────────────────────────────────────────
  const box = $('[data-lightbox]');
  if (box?.showModal) {
    $$('[data-zoom]').forEach(btn => btn.addEventListener('click', () => {
      const img = $('img', btn);
      $('[data-lightbox-img]', box).src = img.currentSrc || img.src;
      $('[data-lightbox-img]', box).alt = img.alt;
      $('[data-lightbox-caption]', box).textContent = btn.closest('figure')?.querySelector('figcaption')?.textContent ?? img.alt;
      box.showModal();
    }));
    box.addEventListener('click', e => { if (e.target === box) box.close(); });
  }

  // ── Release data ─────────────────────────────────────────────────────────
  const fmtDate = iso => new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'long', year: 'numeric' });
  const fmtSize = b => b >= 1048576 ? `${(b / 1048576).toFixed(b >= 104857600 ? 0 : 1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`;
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  // The same sorting of files as tools/update-releases.mjs, for when the JSON isn't there.
  const RULES = [
    [/^pdfedit-desktop-setup-.*\.exe$/i, 'desktop', 'windows', 'Installer', 'installer'],
    [/^pdfedit-desktop-.*-win-x64\.zip$/i, 'desktop', 'windows', 'Portable ZIP', 'portable'],
    [/^pdfedit-desktop-.*-mac-arm64\.dmg$/i, 'desktop', 'mac', 'Apple silicon (M1 and later)', 'arm64'],
    [/^pdfedit-desktop-.*-mac-x64\.dmg$/i, 'desktop', 'mac', 'Intel Mac', 'x64'],
    [/^pdfeditsetup-.*\.exe$/i, 'windows', 'windows', 'Installer', 'installer'],
    [/^pdfedit-.*-win-x64-portable\.zip$/i, 'windows', 'windows', 'Portable ZIP (no install)', 'portable'],
    [/^pdfedit-.*-win-x64\.zip$/i, 'windows', 'windows', 'ZIP (needs .NET 10 Desktop Runtime)', 'zip'],
    [/^pdfedit-.*\.msix$/i, 'windows', 'windows', 'MSIX package', 'msix'],
    [/\.cer$/i, 'windows', 'windows', 'Test certificate for the MSIX', 'cert'],
  ];
  const classify = name => { for (const [re, product, platform, label, kind] of RULES) if (re.test(name)) return { product, platform, label, kind }; return null; };
  const changesOf = body => {
    const m = (body || '').match(/###\s*What's new\s*\n([\s\S]*?)(\n---|\n#{1,3}\s|$)/i);
    return m ? m[1].split('\n').map(l => l.trim()).filter(l => l.startsWith('- ')).map(l => ({
      text: l.slice(2).replace(/\s*\([0-9a-f]{7,40}\)\s*$/, '').trim(),
      commit: l.match(/\(([0-9a-f]{7,40})\)\s*$/)?.[1] ?? null,
    })).filter(c => c.text && !/^version \d/i.test(c.text)) : [];
  };

  async function loadReleases() {
    try {
      const r = await fetch('data/releases.json', { cache: 'no-cache' });
      if (r.ok) return await r.json();
    } catch { /* fall through to GitHub */ }
    const repo = 'dotnetappdev/pdfedit';
    const r = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=30`, { headers: { Accept: 'application/vnd.github+json' } });
    if (!r.ok) throw new Error('GitHub ' + r.status);
    return {
      repo, updated: new Date().toISOString(),
      releases: (await r.json()).filter(x => !x.draft).map(x => ({
        version: x.tag_name.replace(/^v/, ''), tag: x.tag_name, date: x.published_at, prerelease: x.prerelease, url: x.html_url,
        changes: changesOf(x.body),
        assets: x.assets.map(a => ({ name: a.name, url: a.browser_download_url, size: a.size, ...classify(a.name) })).filter(a => a.product),
      })),
    };
  }

  // The best single download for this computer, for the big green buttons.
  function bestFor(release) {
    const ua = navigator.userAgent;
    const isMac = /Macintosh|Mac OS X/.test(ua) && !/iPhone|iPad/.test(ua);
    const isWin = /Windows/.test(ua);
    const pick = (product, kind) => release.assets.find(a => a.product === product && a.kind === kind);
    if (isMac) return pick('desktop', 'arm64') && { asset: pick('desktop', 'arm64'), text: `Mac · ${release.version}` };
    if (isWin) return pick('windows', 'installer') && { asset: pick('windows', 'installer'), text: `Windows · ${release.version} · ${fmtSize(pick('windows', 'installer').size)}` };
    return null;
  }

  function table(assets, repoUrl) {
    if (!assets.length) return `<p class="muted">No files for this yet — see <a href="${esc(repoUrl)}">the release on GitHub</a>.</p>`;
    const order = ['installer', 'arm64', 'x64', 'portable', 'zip', 'msix', 'cert'];
    const rows = [...assets].sort((a, b) => order.indexOf(a.kind) - order.indexOf(b.kind)).map(a => `
      <tr>
        <td><strong>${a.platform === 'mac' ? 'Mac — ' : 'Windows — '}${esc(a.label)}</strong><span class="file">${esc(a.name)}</span></td>
        <td class="size">${fmtSize(a.size)}</td>
        <td class="go"><a class="btn ${a.kind === 'cert' ? 'small' : 'go small'}" href="${esc(a.url)}">Download</a></td>
      </tr>`).join('');
    return `<table class="dl-table"><thead><tr><th>File</th><th class="size">Size</th><th></th></tr></thead><tbody>${rows}</tbody></table>`;
  }

  function changeList(release, repo, limit) {
    const items = release.changes.slice(0, limit ?? Infinity).map(c =>
      `<li>${esc(c.text)}${c.commit ? ` <a class="hash" href="https://github.com/${esc(repo)}/commit/${esc(c.commit)}">${esc(c.commit)}</a>` : ''}</li>`).join('');
    return items ? `<ul>${items}</ul>` : `<p class="muted">See <a href="${esc(release.url)}">the release notes</a>.</p>`;
  }

  function render(data) {
    const releases = data.releases;
    const latest = releases.find(r => !r.prerelease) ?? releases[0];
    if (!latest) return;
    $$('[data-latest-version]').forEach(el => el.textContent = latest.version);
    $$('[data-latest-date]').forEach(el => el.textContent = fmtDate(latest.date));
    $$('[data-strip]').forEach(el => el.innerHTML = `<strong>PdfEdit ${esc(latest.version)}</strong> is out (${esc(fmtDate(latest.date))})${latest.changes[0] ? ' — ' + esc(latest.changes[0].text) : ''}`);
    $$('[data-updated]').forEach(el => el.textContent = `Downloads updated ${fmtDate(data.updated)}`);

    const best = bestFor(latest);
    if (best) $$('[data-download-best]').forEach(a => {
      a.href = best.asset.url;
      const l = $('[data-download-best-label]', a); if (l) l.textContent = best.text;
    });
    $$('[data-asset]').forEach(a => {
      const [product, kind] = a.dataset.asset.split(':');
      const asset = latest.assets.find(x => x.product === product && x.kind === kind);
      if (asset) a.href = asset.url;
    });

    // Download page: the latest files for each product (the newest release that has them).
    $$('[data-downloads]').forEach(el => {
      const product = el.dataset.downloads;
      const rel = releases.find(r => !r.prerelease && r.assets.some(a => a.product === product));
      el.innerHTML = rel
        ? (rel !== latest ? `<p class="muted">From version ${esc(rel.version)} — the newest with these files.</p>` : '') + table(rel.assets.filter(a => a.product === product), rel.url)
        : '<p class="muted">Coming with the next release.</p>';
    });
    $$('[data-older]').forEach(el => {
      el.innerHTML = releases.filter(r => r !== latest).map(r => `
        <details class="older"><summary>PdfEdit ${esc(r.version)} <span class="muted">— ${esc(fmtDate(r.date))}</span>${r.prerelease ? '<span class="badge">Pre-release</span>' : ''}</summary>
          <div class="body">${table(r.assets, r.url)}</div></details>`).join('') || '<p class="muted">None yet.</p>';
    });

    // What's new: every release.
    $$('[data-changelog]').forEach(el => {
      el.innerHTML = releases.map((r, i) => `
        <article class="release" id="v${esc(r.version)}">
          <h2>Version ${esc(r.version)}${i === 0 ? '<span class="badge">Latest</span>' : ''}${r.prerelease ? '<span class="badge">Pre-release</span>' : ''}</h2>
          <div class="when">${esc(fmtDate(r.date))} · <a href="${esc(r.url)}">release notes</a> · <a href="download.html${i === 0 ? '' : '#older'}">download</a></div>
          ${changeList(r, data.repo)}
        </article>`).join('');
    });

    // Home: the news box.
    $$('[data-news]').forEach(el => {
      el.innerHTML = releases.slice(0, 3).map(r => `
        <div style="margin-bottom:14px">
          <strong><a href="whats-new.html#v${esc(r.version)}">PdfEdit ${esc(r.version)}</a></strong>
          <div class="muted" style="font-size:.85rem">${esc(fmtDate(r.date))}</div>
          ${changeList(r, data.repo, 3)}
        </div>`).join('');
    });
  }

  loadReleases().then(render).catch(() => {
    $$('[data-downloads], [data-older], [data-changelog], [data-news]').forEach(el =>
      el.innerHTML = '<p>Couldn\'t load the release list. Get PdfEdit from <a href="https://github.com/dotnetappdev/pdfedit/releases">GitHub Releases</a>.</p>');
  });

  // "Try it online" shows only when data/site.json names a demo.
  fetch('data/site.json').then(r => r.ok ? r.json() : null).then(site => {
    if (site?.demoUrl) $$('[data-demo]').forEach(a => { a.href = site.demoUrl; a.hidden = false; });
  }).catch(() => { });
})();
