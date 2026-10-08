// Builds the website into staticsite/_site: puts the shared head, header and footer into each page
// (<!-- @include name --> → partials/name.html) and copies the assets, data and the screenshots the
// pages use (img/shots/x.png ← docs/screenshots/blazor/x.png, img/shots/win-x.png ← docs/screenshots/x.png).
//   node staticsite/tools/build.mjs        then serve staticsite/_site (e.g. npx serve staticsite/_site)
import { readFile, writeFile, mkdir, cp, readdir, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const site = join(dirname(fileURLToPath(import.meta.url)), '..');
const repo = join(site, '..');
const out = join(site, '_site');
await rm(out, { recursive: true, force: true });
await mkdir(join(out, 'img/shots'), { recursive: true });

const partials = {};
for (const f of await readdir(join(site, 'partials'))) partials[f.replace(/\.html$/, '')] = await readFile(join(site, 'partials', f), 'utf8');

const shots = new Set();
for (const f of (await readdir(site)).filter(f => f.endsWith('.html'))) {
  let html = await readFile(join(site, f), 'utf8');
  html = html.replace(/<!--\s*@include\s+(\w+)\s*-->/g, (_, name) => {
    if (!(name in partials)) throw new Error(`${f}: no partial "${name}"`);
    return partials[name];
  });
  for (const m of html.matchAll(/img\/shots\/([\w.-]+\.png)/g)) shots.add(m[1]);
  await writeFile(join(out, f), html);
}

for (const name of shots) {
  const src = name.startsWith('win-')
    ? join(repo, 'docs/screenshots', name.slice(4))
    : join(repo, 'docs/screenshots/blazor', name);
  if (!existsSync(src)) throw new Error(`screenshot not found: ${src}`);
  await cp(src, join(out, 'img/shots', name));
}
await cp(join(site, 'assets'), join(out, 'assets'), { recursive: true });
await cp(join(site, 'data'), join(out, 'data'), { recursive: true });
await writeFile(join(out, '.nojekyll'), '');
console.log(`_site: ${(await readdir(out)).filter(f => f.endsWith('.html')).length} pages, ${shots.size} screenshots`);
