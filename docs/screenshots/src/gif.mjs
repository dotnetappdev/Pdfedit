// Renders docs/screenshots/fill-and-sign.gif: filling in and signing a flat form in Live View.
//   node docs/screenshots/src/gif.mjs        (needs ffmpeg on PATH)
// A mock-up animation drawn from the real Fill & Sign ribbon, not a screen recording.
import fs from 'fs';
import os from 'os';
import path from 'path';
import { execFileSync } from 'child_process';
import { chromium } from './browser.mjs';
import { CSS, icon } from './ui.mjs';
import { FILL_SIGN } from './ribbons.mjs';
import { OUT, carRental, windowShell, typed, check, cross, signature, initials, head, props, swatches } from './scenes.mjs';

// Ribbon with one tool shown as active (by label).
const ribbonWith = on => FILL_SIGN.map(g => ({
  ...g, items: g.items.map(x => {
    if (x[0] === 'L') return ['L', x[1], x[2], { on: x[2].replace('<br>', ' ') === on }];
    if (x[0] === 'M') return ['M', x[1].map(m => [m[0], m[1], { ...(m[2] || {}), on: m[1] === on }])];
    return x;
  }),
}));

const pointer = (x, y) => `<svg style="position:absolute;left:${x}px;top:${y}px;z-index:50;filter:drop-shadow(0 1px 2px rgba(0,0,0,.5))" width="20" height="24" viewBox="0 0 20 24"><path d="M2 2 L2 19 L6.5 15 L9.5 22 L12.5 20.7 L9.6 14 L15.5 14 Z" fill="#fff" stroke="#000" stroke-width="1.3" stroke-linejoin="round"/></svg>`;
const ibeam = (x, y) => `<svg style="position:absolute;left:${x}px;top:${y}px;z-index:50" width="10" height="20" viewBox="0 0 10 20"><path d="M2 1h6 M5 1v18 M2 19h6" stroke="#111" stroke-width="1.6" fill="none"/></svg>`;
const caret = (x, y) => `<div style="position:absolute;left:${x}px;top:${y}px;width:1.5px;height:16px;background:#111"></div>`;
const mini = (x, y) => `<div class="mini" style="left:${x}px;top:${y}px"><span class="g">⠿</span><span style="font-size:10px">A</span><span style="font-size:15px">A</span><span>${icon('trash', 14, '#c8c8c8')}</span><span>${icon('undo', 14, '#c8c8c8')}</span><span style="font-size:11px">VA</span><span>${icon('compress', 13, '#c8c8c8')}</span></div>`;
const toast = t => `<div style="position:absolute;left:1000px;top:808px;width:240px;background:#2D2D2D;border:1px solid #3a3a3a;border-left:4px solid #2EA043;border-radius:6px;padding:10px 12px;font-size:12.5px;box-shadow:0 6px 18px rgba(0,0,0,.5);z-index:60"><b style="color:#7ee787">✓</b>&nbsp; ${t}</div>`;

// Form contents in page coordinates (see carRental in scenes.mjs).
const FIELDS = { name: [126, 113], addr: [126, 145], city: [126, 177], email: [126, 209], phone: [126, 241], pick: [126, 313], ret: [386, 313] };
const VALUES = { name: 'Jordan Taylor', addr: '221B Baker Street', city: 'London', email: 'jordan.taylor@example.com', phone: '+44 7700 900123', pick: '04/10/2026', ret: '11/10/2026' };
const TICKS = [[359, 391, 'y'], [359, 419, 'y'], [424, 447, 'n'], [359, 475, 'y'], [359, 503, 'y'], [359, 531, 'y'], [399, 571, 'y']];

function frame(st) {
  let extra = '';
  for (const [k, v] of Object.entries(st.text || {})) extra += typed(FIELDS[k][0], FIELDS[k][1], v);
  (st.ticks || []).forEach(([x, y, k]) => { extra += k === 'y' ? check(x, y) : cross(x, y); });
  if (st.sig) extra += signature(150, 622, 160);
  if (st.sigDate) extra += typed(400, 650, '04/10/2026');
  if (st.init) extra += initials(86, 690);
  if (st.editing) {
    const [k, len] = st.editing, [x, y] = FIELDS[k];
    const w = Math.max(60, len * 7.4 + 14);
    extra += `<div class="sel" style="position:absolute;left:${x - 3}px;top:${y - 3}px;width:${w}px;height:22px"></div>` + mini(x - 3, y - 30) + caret(x + len * 7.15 + 1, y);
  }
  if (st.selSig) extra += `<div class="sel" style="position:absolute;left:148px;top:620px;width:164px;height:60px"></div><div class="thumb" style="left:307px;top:675px"></div>`;
  if (st.ibeam) extra += ibeam(st.ibeam[0], st.ibeam[1]);
  if (st.ptr) extra += pointer(st.ptr[0], st.ptr[1]);

  const right = st.right || (head('PROPERTIES') + `<div style="padding:12px 10px;color:#aaa;font-size:12px;line-height:1.5">Click inside any box on the page to type in it — this PDF has no form fields, the boxes are only drawn.</div>`);
  let html = windowShell({ groups: ribbonWith(st.tool), page: carRental(extra), pageTop: st.pageTop ?? 18, right, status: st.status });
  let over = '';
  if (st.popup) over += `<div class="menu" style="left:700px;top:132px;width:236px;padding:8px">
    <div style="font-size:11px;color:#999;padding:2px 4px 6px">YOUR SIGNATURES</div>
    <div style="background:#fff;border-radius:4px;height:58px;position:relative;margin-bottom:6px;outline:2px solid var(--accent)">${signature(40, 4, 140)}</div>
    <div class="mi" style="padding-left:10px">${icon('plus', 13)} &nbsp;Add Signature</div></div>`;
  if (st.ribbonPtr) over += pointer(st.ribbonPtr[0], st.ribbonPtr[1]);
  if (st.toast) over += toast(st.toast);
  return html.replace(/<\/div>\s*<div class="statusbar">/, m => over + m);
}

// Page (x, y) → its position inside the page; pointer is drawn inside the page, so use page coords.
const textProps = (t) => head('SELECTED TEXT') + `<div class="cat">Text</div>` + props([['Text', t], ['Font', 'Arial ▾'], ['Size', '12'], ['Colour', '#000000']]) + swatches;

const frames = [];
const add = (st, ms) => frames.push([st, ms]);
let text = {};
const S = 'Select';

add({ tool: S, status: 'Ready — car_rental_checklist.pdf has no fillable fields.' }, 1400);
add({ tool: S, ptr: [300, 128], status: 'Click inside a box to type in it.' }, 500);
// Type each field
for (const k of ['name', 'addr', 'city', 'email', 'phone']) {
  const v = VALUES[k], [x, y] = FIELDS[k];
  add({ tool: S, text, ibeam: [x + 140, y], status: 'Click inside a box to type in it.' }, 250);
  const steps = k === 'name' ? [3, 6, 9, 13] : [Math.ceil(v.length / 2), v.length];
  for (const n of steps) add({ tool: S, text: { ...text, [k]: v.slice(0, n) }, editing: [k, n], right: textProps(v.slice(0, n)), status: 'Typing — the text lines up with the box.' }, k === 'name' ? 180 : 220);
  text = { ...text, [k]: v };
}
add({ tool: S, text, status: 'Renter details filled in.' }, 500);
// Dates
add({ tool: S, text, ribbonPtr: [228, 92], status: 'Date — click a box to stamp a date.' }, 600);
add({ tool: 'Date', text, ptr: [190, 310], status: 'Date — click a box to stamp a date.' }, 500);
text = { ...text, pick: VALUES.pick };
add({ tool: 'Date', text, ptr: [190, 310], status: 'Date placed — change its format in Properties.' }, 500);
text = { ...text, ret: VALUES.ret };
add({ tool: 'Date', text, ptr: [450, 310], status: 'Date placed — change its format in Properties.' }, 700);
// Ticks — scroll down
add({ tool: 'Date', text, ribbonPtr: [400, 76], status: 'Check — click a square to tick it.', pageTop: -40 }, 600);
const ticks = [];
for (const t of TICKS) {
  ticks.push(t);
  add({ tool: 'Check', text, ticks: [...ticks], ptr: [t[0] + 10, t[1] + 10], pageTop: -40, status: t[2] === 'y' ? "Mark '✓' placed." : "Mark '✕' placed — swapped on the toolbar." }, 330);
}
add({ tool: 'Check', text, ticks, pageTop: -40, status: 'Checklist done.' }, 600);
// Sign — scroll to the declaration
add({ tool: 'Check', text, ticks, ribbonPtr: [716, 92], pageTop: -150, status: 'Sign — choose a signature, then click the page.' }, 600);
add({ tool: 'Sign', text, ticks, popup: true, ribbonPtr: [800, 172], pageTop: -150, status: 'Sign — choose a signature, then click the page.' }, 1000);
add({ tool: 'Sign', text, ticks, ptr: [220, 650], pageTop: -150, status: 'Click where the signature goes.' }, 600);
add({ tool: 'Sign', text, ticks, sig: true, selSig: true, ptr: [235, 660], pageTop: -150, status: 'Signature placed — drag to move, corner to resize.',
  right: head('SELECTED SIGNATURE') + `<div class="cat">Signature</div>` + props([['Page', '1'], ['Width', '120.0'], ['Height', '43.2']]) }, 1100);
add({ tool: 'Date', text, ticks, sig: true, sigDate: true, ptr: [460, 650], pageTop: -150, status: 'Date placed.' }, 700);
add({ tool: 'Sign', text, ticks, sig: true, sigDate: true, init: true, ptr: [120, 700], pageTop: -150, status: 'Initials placed.' }, 900);
// Save
add({ tool: 'Sign', text, ticks, sig: true, sigDate: true, init: true, ribbonPtr: [1404, 92], pageTop: -150, status: 'Save — keeps everything editable; Flatten & Save locks it in.' }, 700);
add({ tool: 'Save', text, ticks, sig: true, sigDate: true, init: true, ribbonPtr: [1404, 92], pageTop: -150, toast: 'Saved car_rental_checklist.pdf', status: 'Saved.' }, 2600);

// ── Render frames and build the GIF ─────────────────────────────────────────
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'pdfedit-gif-'));
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1600, height: 950 } });
const list = [];
for (let i = 0; i < frames.length; i++) {
  const [st, ms] = frames[i];
  await page.setContent(`<!doctype html><html><head><meta charset="utf-8"><style>${CSS}body{display:inline-block}</style></head><body>${frame(st)}</body></html>`);
  const f = path.join(tmp, `f${String(i).padStart(3, '0')}.png`);
  await page.locator('.win').first().screenshot({ path: f });
  list.push(`file '${f}'\nduration ${(ms / 1000).toFixed(3)}`);
}
await browser.close();
list.push(`file '${path.join(tmp, `f${String(frames.length - 1).padStart(3, '0')}.png`)}'`); // concat needs the last frame twice
fs.writeFileSync(path.join(tmp, 'list.txt'), list.join('\n'));

const out = path.join(OUT, 'fill-and-sign.gif');
const vf = 'scale=1200:-1:flags=lanczos';
execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', path.join(tmp, 'list.txt'),
  '-vf', `${vf},palettegen=stats_mode=diff:max_colors=128`, path.join(tmp, 'pal.png')]);
execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', path.join(tmp, 'list.txt'), '-i', path.join(tmp, 'pal.png'),
  '-lavfi', `${vf}[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle`, '-loop', '0', out]);
fs.rmSync(tmp, { recursive: true, force: true });
console.log(`wrote ${out} (${frames.length} frames, ${(fs.statSync(out).size / 1e6).toFixed(1)} MB)`);
