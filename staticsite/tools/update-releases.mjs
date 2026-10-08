// Writes data/releases.json from the project's GitHub releases: each release's version, date, notes
// ("What's new") and downloads, sorted into products and platforms. Run by the Website workflow on
// every release (and daily); run it by hand with:  node staticsite/tools/update-releases.mjs
// GITHUB_TOKEN is used when set (higher rate limit); GITHUB_REPOSITORY overrides the repository.
import { writeFile, readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const site = JSON.parse(await readFile(join(here, '../data/site.json'), 'utf8'));
const repo = process.env.GITHUB_REPOSITORY || site.repo;
const headers = { 'Accept': 'application/vnd.github+json', 'User-Agent': 'pdfedit-website' };
if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;

const releases = [];
for (let page = 1; page <= 5; page++) {
  const r = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=50&page=${page}`, { headers });
  if (!r.ok) throw new Error(`GitHub answered ${r.status} ${r.statusText}`);
  const batch = await r.json();
  releases.push(...batch);
  if (batch.length < 50) break;
}

// What each file is, from its name.
function classify(name) {
  const n = name.toLowerCase();
  const rules = [
    [/^pdfedit-desktop-.*-mac-arm64\.dmg$/, 'desktop', 'mac', 'Apple silicon (M1 and later)', 'arm64'],
    [/^pdfedit-desktop-.*-mac-x64\.dmg$/, 'desktop', 'mac', 'Intel Mac', 'x64'],
    [/^pdfedit-desktop-/, null], // the desktop app's Windows builds aren't listed: Windows has PdfEdit for Windows
    [/^pdfeditsetup-.*\.exe$/, 'windows', 'windows', 'Installer', 'installer'],
    [/^pdfedit-.*-win-x64-portable\.zip$/, 'windows', 'windows', 'Portable ZIP (no install)', 'portable'],
    [/^pdfedit-.*-win-x64\.zip$/, 'windows', 'windows', 'ZIP (needs .NET 10 Desktop Runtime)', 'zip'],
    [/^pdfedit-.*\.msix$/, 'windows', 'windows', 'MSIX package', 'msix'],
    [/\.cer$/, 'windows', 'windows', 'Test certificate for the MSIX', 'cert'],
  ];
  for (const [re, product, platform, label, kind] of rules)
    if (re.test(n)) return product ? { product, platform, label, kind } : null;
  return null;
}

// Plain punctuation for the site: no long dashes or curly quotes.
const plain = t => t.replace(/\s+[\u2014\u2013]\s+/g, ', ').replace(/[\u2014\u2013]/g, '-').replace(/[\u201C\u201D]/g, '"').replace(/[\u2018\u2019]/g, "'").replace(/\s*\u2192\s*/g, ' > ').replace(/\u2026/g, '...').replace(/\s\u00B7\s/g, ', ');

// The "What's new" list from the release text: one line per change, without the commit hash.
function whatsNew(body = '') {
  const m = body.match(/###\s*What's new\s*\n([\s\S]*?)(\n---|\n#{1,3}\s|$)/i);
  if (!m) return [];
  return m[1].split('\n').map(l => l.trim()).filter(l => l.startsWith('- '))
    .map(l => {
      const hash = l.match(/\(([0-9a-f]{7,40})\)\s*$/)?.[1] ?? null;
      const text = plain(l.slice(2).replace(/\s*\([0-9a-f]{7,40}\)\s*$/, '').trim());
      return { text, commit: hash };
    })
    .filter(c => c.text && !/^version \d/i.test(c.text) && !/^merge /i.test(c.text));
}

const out = releases.filter(r => !r.draft).map(r => ({
  version: r.tag_name.replace(/^v/, ''),
  tag: r.tag_name,
  date: r.published_at,
  prerelease: r.prerelease,
  url: r.html_url,
  changes: whatsNew(r.body ?? ''),
  assets: r.assets.map(a => ({ name: a.name, url: a.browser_download_url, size: a.size, downloads: a.download_count, ...classify(a.name) }))
                  .filter(a => a.product),
}));

const data = { repo, updated: new Date().toISOString(), releases: out };
await writeFile(join(here, '../data/releases.json'), JSON.stringify(data, null, 1) + '\n');
console.log(`releases.json: ${out.length} releases, latest ${out.find(r => !r.prerelease)?.version ?? 'none'}`);
