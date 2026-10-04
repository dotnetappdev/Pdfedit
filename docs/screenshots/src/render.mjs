// Renders the README / docs mock-up screenshots: node docs/screenshots/src/render.mjs [file.png …]
// They are drawn from the real ribbon layout in MainWindow.xaml, not captured from the app.
import path from 'path';
import { chromium } from './browser.mjs';
import { CSS } from './ui.mjs';
import { FILL_SIGN, HOME, TOOLS, EDIT } from './ribbons.mjs';
import { OUT, ribbonStrip, sceneTextFit, sceneMarks, sceneDate, sceneStamps, sceneSign } from './scenes.mjs';

// ── Render ──────────────────────────────────────────────────────────────────
const shots = [
  ['ribbon-fill-sign.png', ribbonStrip('Fill &amp; Sign', FILL_SIGN)],
  ['ribbon-home.png', ribbonStrip('Home', HOME)],
  ['ribbon-tools.png', ribbonStrip('Tools', TOOLS)],
  ['ribbon-edit.png', ribbonStrip('Edit', EDIT)],
  ['fill-text-fit.png', sceneTextFit()],
  ['fill-marks.png', sceneMarks()],
  ['fill-date-format.png', sceneDate()],
  ['fill-stamps.png', sceneStamps()],
  ['fill-signature.png', sceneSign()],
];
const only = process.argv.slice(2);
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 2400, height: 1000 }, deviceScaleFactor: 1 });
for (const [file, html] of shots) {
  if (only.length && !only.includes(file)) continue;
  await page.setContent(`<!doctype html><html><head><meta charset="utf-8"><style>${CSS}body{display:inline-block}</style></head><body>${html}</body></html>`);
  await page.locator('.win').first().screenshot({ path: path.join(OUT, file) });
  console.log('wrote', file);
}
await browser.close();
