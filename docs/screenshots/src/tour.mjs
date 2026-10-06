// Renders the captioned video tour: docs/tour/pdfedit-tour.mp4, a GIF copy for the docs, and a
// poster image. Built from the screenshot mock-ups in docs/screenshots, not a screen recording.
//   node docs/screenshots/src/tour.mjs        (needs ffmpeg on PATH)
import fs from 'fs';
import os from 'os';
import path from 'path';
import { execFileSync } from 'child_process';
import { chromium } from './browser.mjs';
import { OUT as SHOTS } from './scenes.mjs';

const DEST = path.resolve(SHOTS, '..', 'tour');
const W = 1280, H = 720, FPS = 25, FADE = 0.6;
// Picture area inside each slide (the caption bar sits below it).
const BOX = { x: 40, y: 28, w: 1200, h: 548 };

// [image or animation, title, line, seconds]
const CHAPTERS = [
  [null, 'PdfEdit', 'A free PDF editor for Windows. No subscriptions, no paywalls, no watermarks.', 4.5],
  ['fill-and-sign.gif', 'Fill in and sign any form', 'Even a flat PDF with no form fields: click a box and type.', 0],
  ['fill-marks.png', 'Ticks, crosses and dots', 'For paper-style forms with boxes but no real checkboxes.', 4.5],
  ['fill-signature.png', 'Sign once, reuse everywhere', 'Draw, type or import your signature and initials.', 4.5],
  ['fill-stamps.png', 'Stamps like Acrobat\'s', 'Approved, Sign Here, dynamic stamps with your name and the time.', 4.5],
  ['live-view.png', 'Review and mark up', 'Highlights, sticky notes, strike-through, stamps and redaction.', 4.5],
  ['edit-fields.png', 'Build your own forms', 'Add, align and resize fields, or let Detect Fields find them.', 4.5],
  ['field-properties.png', 'Forms that do the maths', 'Number, currency and date formats, and totals that add up.', 4.5],
  ['page-management.png', 'Organise pages', 'Rotate, reorder, insert, extract, merge and split.', 4.5],
  ['design-canvas-view.png', 'Design from scratch', 'Invoices, letters, flyers and résumés from templates.', 4.5],
  ['ai-features.png', 'Optional AI assistant', 'Smart Fill, summaries and questions. Your own key or a free local model.', 4.5],
  ['themes.png', 'Twelve themes', 'Follows Windows light, dark and contrast themes, or pick your own.', 4.5],
  ['settings-accessibility.png', 'Built for everyone', 'Text and icon sizes, screen reader announcements and narration.', 4.5],
  [null, 'Free. Offline. No account.', 'github.com/dotnetappdev/Pdfedit', 4.5],
];

const font = "'Segoe UI','Liberation Sans','DejaVu Sans',sans-serif";
// Pages made with setContent can't load file:// URLs, so the screenshots go in as data URIs.
const dataUri = f => `data:image/png;base64,${fs.readFileSync(path.join(SHOTS, f)).toString('base64')}`;
function slideHtml(i, [img, title, line]) {
  const n = CHAPTERS.length, card = !img;
  const dots = CHAPTERS.map((_, k) => `<span style="width:${k === i ? 22 : 7}px;height:7px;border-radius:4px;background:${k <= i ? '#0A84FF' : '#3a3a3a'}"></span>`).join('');
  const picture = card ? `
    <div style="position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:22px">
      <div style="width:96px;height:96px;border-radius:22px;background:#0A84FF;color:#fff;font:700 54px ${font};display:grid;place-items:center">P</div>
      <div style="font:700 54px ${font};color:#fff">${title}</div>
      <div style="font:400 24px ${font};color:#bdbdbd;max-width:900px;text-align:center">${line}</div>
    </div>`
    : img.endsWith('.gif') ? '' // the animation is laid over this space by ffmpeg
    : (() => {
        // Fit the screenshot inside the picture box, never enlarging it past 1.15×.
        const buf = fs.readFileSync(path.join(SHOTS, img)), iw = buf.readUInt32BE(16), ih = buf.readUInt32BE(20);
        const k = Math.min(BOX.w / iw, BOX.h / ih, 1.15), w = Math.round(iw * k), h = Math.round(ih * k);
        return `<img src="${dataUri(img)}" style="position:absolute;left:${BOX.x + (BOX.w - w) / 2}px;top:${BOX.y + (BOX.h - h) / 2}px;width:${w}px;height:${h}px;border-radius:8px;box-shadow:0 10px 40px rgba(0,0,0,.6)">`;
      })();
  const caption = card ? '' : `
    <div style="position:absolute;left:40px;right:40px;top:${BOX.y + BOX.h + 26}px;display:flex;align-items:baseline;gap:16px">
      <span style="font:600 15px ${font};color:#0A84FF">${String(i).padStart(2, '0')}</span>
      <span style="font:700 27px ${font};color:#fff">${title}</span>
      <span style="font:400 18px ${font};color:#bdbdbd">${line}</span>
    </div>`;
  return `<!doctype html><html><head><meta charset="utf-8"><style>*{margin:0;padding:0}body{width:${W}px;height:${H}px;overflow:hidden;
    background:radial-gradient(ellipse at 50% 0%,#1d2633 0%,#101114 70%);position:relative}</style></head><body>
    ${picture}${caption}
    <div style="position:absolute;left:0;right:0;bottom:18px;display:flex;justify-content:center;gap:6px">${dots}</div>
    ${card ? '' : `<div style="position:absolute;right:40px;bottom:14px;font:400 12px ${font};color:#666">${i} / ${n - 2}</div>`}
  </body></html>`;
}

const run = (args) => execFileSync('ffmpeg', ['-v', 'error', '-y', ...args], { stdio: 'inherit' });
const probe = f => parseFloat(execFileSync('ffprobe', ['-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', f]).toString());

const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'pdfedit-tour-'));
fs.mkdirSync(DEST, { recursive: true });
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: W, height: H } });
const clips = [];
for (const [i, ch] of CHAPTERS.entries()) {
  const png = path.join(tmp, `s${i}.png`), clip = path.join(tmp, `c${i}.mp4`);
  await page.setContent(slideHtml(i, ch), { waitUntil: 'load' });
  await page.screenshot({ path: png });
  const [img] = ch;
  if (img && img.endsWith('.gif')) {
    // Animation chapter: the GIF, scaled into the picture box, over the slide.
    const gif = path.join(SHOTS, img), secs = probe(gif) + 0.8;
    run(['-loop', '1', '-t', `${secs}`, '-i', png, '-ignore_loop', '1', '-i', gif, '-filter_complex',
      `[1:v]scale=${BOX.w}:${BOX.h}:force_original_aspect_ratio=decrease,fps=${FPS}[a];` +
      `[0:v][a]overlay=x=${BOX.x}+(${BOX.w}-overlay_w)/2:y=${BOX.y}+(${BOX.h}-overlay_h)/2:eof_action=repeat,format=yuv420p`,
      '-r', `${FPS}`, '-t', `${secs}`, '-c:v', 'libx264', '-crf', '20', '-pix_fmt', 'yuv420p', clip]);
    clips.push([clip, secs]);
  } else {
    run(['-loop', '1', '-t', `${ch[3]}`, '-i', png, '-vf', `fps=${FPS},format=yuv420p`,
      '-c:v', 'libx264', '-crf', '20', '-tune', 'stillimage', '-pix_fmt', 'yuv420p', clip]);
    clips.push([clip, ch[3]]);
  }
}

// Poster for the README: the title card with a play button and the running time.
const total = clips.reduce((sum, [, secs]) => sum + secs, 0) - FADE * (clips.length - 1);
await page.setContent(slideHtml(0, CHAPTERS[0]).replace('</body>', `
  <div style="position:absolute;left:0;right:0;bottom:70px;display:flex;justify-content:center">
    <div style="display:flex;align-items:center;gap:14px;background:#0A84FF;color:#fff;border-radius:40px;padding:14px 28px 14px 20px;font:600 24px ${font};box-shadow:0 8px 30px rgba(10,132,255,.45)">
      <svg width="30" height="30" viewBox="0 0 30 30"><circle cx="15" cy="15" r="15" fill="#fff"/><path d="M12 9 L22 15 L12 21 Z" fill="#0A84FF"/></svg>
      Watch the tour (${Math.floor(total / 60)}:${String(Math.round(total % 60)).padStart(2, '0')})</div></div></body>`), { waitUntil: 'load' });
await page.screenshot({ path: path.join(DEST, 'tour-poster.png') });
console.log('wrote tour-poster.png');
await browser.close();

// Cross-fade the chapters together.
let filter = '', last = '[0:v]', t = clips[0][1];
for (let k = 1; k < clips.length; k++) {
  const out = k === clips.length - 1 ? '[v]' : `[x${k}]`;
  filter += `${last}[${k}:v]xfade=transition=fade:duration=${FADE}:offset=${(t - FADE).toFixed(2)}${out};`;
  last = out; t += clips[k][1] - FADE;
}
const mp4 = path.join(DEST, 'pdfedit-tour.mp4');
run([...clips.flatMap(([c]) => ['-i', c]), '-filter_complex', filter.slice(0, -1), '-map', '[v]',
  '-c:v', 'libx264', '-crf', '23', '-preset', 'slow', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', mp4]);
console.log('wrote', path.relative(process.cwd(), mp4), `(${t.toFixed(1)} s)`);

// GIF copy that plays inline on GitHub (smaller and slower than the MP4).
const gifOut = path.join(DEST, 'pdfedit-tour.gif'), pal = path.join(tmp, 'pal.png');
const scale = 'fps=6,scale=800:-1:flags=lanczos';
run(['-i', mp4, '-vf', `${scale},palettegen=stats_mode=diff:max_colors=128`, pal]);
run(['-i', mp4, '-i', pal, '-lavfi', `${scale}[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle`, gifOut]);
console.log('wrote', path.relative(process.cwd(), gifOut));

fs.rmSync(tmp, { recursive: true, force: true });
