import path from 'path';
import { fileURLToPath } from 'url';
// Page artwork, window shell and the screenshot scenes (see render.mjs / gif.mjs).
import { CSS, titlebar, tabs, ribbon, icon, glyph } from './ui.mjs';
import { FILL_SIGN, HOME, TOOLS, EDIT } from './ribbons.mjs';

export const OUT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// ── Page artwork ────────────────────────────────────────────────────────────
export const check = (x, y, s = 18, c = '#2E7D32') => `<svg style="position:absolute;left:${x}px;top:${y}px" width="${s}" height="${s}" viewBox="0 0 1 1"><path d="M.16 .55 L.4 .78 L.86 .2" fill="none" stroke="${c}" stroke-width=".16" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
export const cross = (x, y, s = 18, c = '#111') => `<svg style="position:absolute;left:${x}px;top:${y}px" width="${s}" height="${s}" viewBox="0 0 1 1"><path d="M.2 .2 L.8 .8 M.8 .2 L.2 .8" fill="none" stroke="${c}" stroke-width=".14" stroke-linecap="round"/></svg>`;
export const dot = (x, y, s = 18, c = '#111') => `<svg style="position:absolute;left:${x}px;top:${y}px" width="${s}" height="${s}" viewBox="0 0 1 1"><circle cx=".5" cy=".5" r=".22" fill="${c}"/></svg>`;
export const box = (x, y, s = 16) => `<div style="position:absolute;left:${x}px;top:${y}px;width:${s}px;height:${s}px;border:1.4px solid #333"></div>`;
export const line = (x, y, w) => `<div style="position:absolute;left:${x}px;top:${y}px;width:${w}px;border-bottom:1px solid #888"></div>`;
export const rect = (x, y, w, h) => `<div style="position:absolute;left:${x}px;top:${y}px;width:${w}px;height:${h}px;border:1px solid #888"></div>`;
export const lbl = (x, y, t, st = '') => `<div style="position:absolute;left:${x}px;top:${y}px;font-size:12.5px;color:#222;${st}">${t}</div>`;
export const typed = (x, y, t, st = '') => `<div style="position:absolute;left:${x}px;top:${y}px;font-size:13px;color:#111;${st}">${t}</div>`;
export const signature = (x, y, w = 150, c = '#1A237E') => `<svg style="position:absolute;left:${x}px;top:${y}px" width="${w}" height="${w * .36}" viewBox="0 0 150 54"><path d="M6 40 C14 10 22 6 24 18 S18 44 30 36 S44 14 50 22 S48 40 58 34 S70 18 76 26 S76 40 86 34 C94 28 98 20 104 24 S106 38 116 32 S132 22 144 26" fill="none" stroke="${c}" stroke-width="2.2" stroke-linecap="round"/><path d="M20 46 L120 44" stroke="${c}" stroke-width="1.2" opacity=".6"/></svg>`;
export const initials = (x, y, c = '#1A237E') => `<svg style="position:absolute;left:${x}px;top:${y}px" width="54" height="30" viewBox="0 0 54 30"><path d="M6 24 C10 8 14 4 14 14 S10 26 18 20 M24 6 L22 24 M22 14 C30 8 34 10 32 18 S28 26 36 22 S44 12 48 16" fill="none" stroke="${c}" stroke-width="2" stroke-linecap="round"/></svg>`;
export const stamp = (x, y, w, h, title, color, sub = '', rot = 0) => {
  const o = Math.max(2, Math.min(w, h) * .07), r = Math.min(w, h) * .18;
  return `<div style="position:absolute;left:${x}px;top:${y}px;width:${w}px;height:${h}px;transform:rotate(${rot}deg);border:${o}px solid ${color};border-radius:${r}px;background:${color}16;color:${color};display:flex;flex-direction:column;justify-content:center;align-items:center;font-family:'Liberation Sans',Arial">
    <div style="position:absolute;inset:${o * 1.1}px;border:${Math.max(1, o * .4)}px solid ${color};border-radius:${r * .6}px"></div>
    <div style="font-weight:700;font-size:${sub ? h * .36 : h * .5}px;letter-spacing:.5px;line-height:1">${title}</div>
    ${sub ? `<div style="font-style:italic;font-size:${h * .17}px;margin-top:${h * .06}px">${sub}</div>` : ''}</div>`;
};

export function carRental(extra = '', w = 560, h = 740) {
  const orange = '#E2642B';
  let s = `<div style="position:absolute;left:0;top:0;right:0;height:58px;background:#3a3a3a;color:#fff;padding:12px 24px;display:flex;justify-content:space-between">
    <div style="font-size:24px;font-weight:700">Car Rental Checklist</div><div style="font-size:10.5px;opacity:.8;margin-top:8px">Pick-up &amp; return inspection</div></div>
    <div style="position:absolute;left:0;right:0;top:58px;height:4px;background:${orange}"></div>`;
  s += lbl(24, 80, "Renter's details", `color:${orange};font-size:16px;font-weight:600`);
  const rows = [['Full name', 108], ['Address', 140], ['City', 172], ['Email', 204], ['Phone', 236]];
  for (const [t, y] of rows) { s += lbl(24, y + 4, t) + rect(120, y, 410, 24); }
  s += lbl(24, 280, 'Rental', `color:${orange};font-size:16px;font-weight:600`);
  s += lbl(24, 312, 'Pick-up date') + rect(120, 308, 150, 24) + lbl(290, 312, 'Return date') + rect(380, 308, 150, 24);
  s += lbl(24, 360, 'Vehicle condition at pick-up', `color:${orange};font-size:16px;font-weight:600`);
  const items = ['Bodywork free of dents', 'Windscreen undamaged', 'Tyres in good condition', 'Interior clean', 'Spare wheel and jack present', 'Fuel tank full'];
  items.forEach((t, i) => { const y = 392 + i * 28; s += lbl(48, y, t) + lbl(330, y, 'Yes', 'color:#555') + box(360, y) + lbl(400, y, 'No', 'color:#555') + box(425, y); });
  s += lbl(24, 572, 'Fuel level', 'color:#222');
  ['E', '¼', '½', '¾', 'F'].forEach((t, i) => { s += box(120 + i * 70, 572) + lbl(142 + i * 70, 572, t, 'color:#555'); });
  s += lbl(24, 616, 'Declaration', `color:${orange};font-size:16px;font-weight:600`);
  s += lbl(24, 646, 'Renter signature') + line(130, 668, 210) + lbl(360, 646, 'Date') + line(395, 668, 135);
  s += lbl(24, 700, 'Initials') + line(80, 714, 70);
  return `<div class="page" style="width:${w}px;height:${h}px;overflow:hidden">${s}${extra}</div>`;
}

export const ribbonStrip = (tab, groups) => `<div class="win" style="display:inline-flex;min-width:900px">${titlebar('car_rental_checklist.pdf')}${tabs(tab)}${ribbon(groups)}</div>`;

export function windowShell({ tab = 'Complete &amp; Sign', groups = FILL_SIGN, page, right, status, overlay = '', pageTop = 18 }) {
  return `<div class="win" style="width:1560px;height:900px">${titlebar('car_rental_checklist.pdf')}${tabs(tab)}${ribbon(groups)}
  <div style="flex:1;display:flex;min-height:0">
    <div style="width:96px;background:var(--side);border-right:1px solid var(--border)"><div class="pane-h"><span class="on">Pages</span></div>
      <div style="padding:12px 18px;display:flex;flex-direction:column;gap:12px">${[1, 2].map(i => `<div style="width:58px;height:76px;background:#fff;outline:${i === 1 ? '2px solid var(--accent)' : '1px solid #444'};outline-offset:2px;padding:6px">${'<div style="height:3px;background:#ccc;margin:4px 0"></div>'.repeat(7)}</div><div style="text-align:center;font-size:11px;color:#aaa;margin-top:-8px">${i}</div>`).join('')}</div></div>
    <div style="flex:1;display:flex;flex-direction:column;min-width:0">
      <div class="pane-h" style="background:var(--panel)"><span class="on">Live View</span><span>Design</span></div>
      <div style="flex:1;background:#171717;position:relative;overflow:hidden;display:flex;justify-content:center;align-items:flex-start"><div style="margin-top:${pageTop}px">${page}</div>${overlay}</div>
    </div>
    <div style="width:300px;background:var(--panel);border-left:1px solid var(--border);overflow:hidden">
      <div class="pane-h"><span class="on">Properties</span><span>Fields</span><span>Comments</span></div>${right}</div>
  </div>
  <div class="statusbar"><span>${status}</span><span>Page 1 of 2 · 100%</span></div></div>`;
}

export const head = t => `<div style="padding:12px 10px 6px;font-size:10.5px;font-weight:600;color:#999;letter-spacing:.4px">${t}</div>`;
export const props = rows => `<div class="prop">${rows.map(([a, b]) => `<div>${a}</div><div>${b}</div>`).join('')}</div>`;
export const swatches = `<div style="display:flex;gap:4px;padding:8px">${['#000', '#C62828', '#1565C0', '#2E7D32', '#F9A825', '#6A1B9A', '#757575', '#fff'].map(c => `<span class="sw" style="width:18px;height:18px;background:${c}"></span>`).join('')}</div>`;
const textProps = (text, extra = '') => head('SELECTED TEXT') + `<div class="cat">Text</div>` + props([['Text', text], ['Font', 'Arial ▾'], ['Size', '12'], ['Colour', '#000000']]) + swatches + extra +
  props([['Bold', '☐'], ['Alignment', 'Left ▾'], ['Rotation', '0 ▾'], ['Auto-size', '☐'], ['Wrap &amp; grow', '☑']]);

// ── Scenes ──────────────────────────────────────────────────────────────────
export function sceneTextFit() {
  // Address typed with a big font in a narrow box; the Fit menu is open.
  const page = carRental(`
    ${typed(126, 113, 'Jordan Taylor')}
    <div class="sel" style="position:absolute;left:124px;top:136px;width:250px;height:58px;font-size:20px;line-height:1.35;padding:1px 3px;color:#111;background:rgba(255,255,255,.6)">221B Baker Street,<br>London NW1 6XE</div>
    <div class="thumb" style="left:369px;top:189px"></div>
    <div class="mini" style="left:124px;top:110px"><span class="g">⠿</span><span style="font-size:10px">A</span><span style="font-size:15px">A</span><span>${icon('trash', 14, '#c8c8c8')}</span><span>${icon('undo', 14, '#c8c8c8')}</span><span style="font-size:11px">VA</span><span class="hot">${icon('compress', 13, '#fff')}</span>
      <span style="gap:3px;display:flex;align-items:center;border-left:1px solid #555">${['#000', '#1A237E', '#B71C1C', '#1B5E20', '#757575'].map(c => `<i class="sw" style="width:11px;height:11px;background:${c};border-color:#ddd"></i>`).join('')}</span></div>`);
  const overlay = `<div class="menu" style="left:${(1560 - 96 - 300 - 560) / 2 + 318}px;top:150px;width:250px">
    <div class="mi">Auto-size box to text</div><div class="mi chk hot">Wrap text, grow box to fit</div><div class="mi">Fixed box size</div><div class="sep"></div><div class="mi">Fit box to text now</div></div>`;
  return windowShell({ page, overlay, right: textProps('221B Baker Street, London NW1 6XE').replace('<div>Size</div><div>12</div>','<div>Size</div><div>16</div>') + `<div class="cat">Layout (pt)</div>` + props([['X', '93.0'], ['Y', '612.5'], ['Width', '187.5'], ['Height', '43.5']]),
    status: 'Fit: wrap text and grow the box — nothing is cut off.' });
}

export function sceneMarks() {
  const rowsY = [392, 420, 448, 476, 504, 532];
  const marks = [true, true, false, true, true, null];
  let extra = typed(126, 113, 'Jordan Taylor') + typed(126, 145, '221B Baker Street') + typed(126, 177, 'London') + typed(126, 209, 'jordan.taylor@example.com') + typed(126, 241, '+44 7700 900123') +
    typed(126, 313, '04/10/2026') + typed(386, 313, '11/10/2026');
  marks.forEach((m, i) => { if (m === true) extra += check(359, rowsY[i] - 1); else if (m === false) extra += cross(424, rowsY[i] - 1); });
  extra += `<div class="sel" style="position:absolute;left:358px;top:${rowsY[5] - 2}px;width:20px;height:20px"></div>`;
  extra += check(359, rowsY[5] - 1) + `<div class="thumb" style="left:373px;top:${rowsY[5] + 13}px"></div>`;
  extra += `<div class="mini" style="left:358px;top:${rowsY[5] - 30}px"><span class="g">⠿</span><span style="font-size:10px">A</span><span style="font-size:15px">A</span><span>${icon('trash', 14, '#c8c8c8')}</span><span>${icon('undo', 14, '#c8c8c8')}</span><span class="hot">${icon('replace', 14, '#fff')}</span></div>`;
  extra += dot(330, 571, 18) + `<div style="position:absolute;left:330px;top:571px;width:16px;height:16px"></div>`;
  const groups = FILL_SIGN.map(g => g.h !== 'Marks' ? { ...g, items: g.items.map(x => x[0] === 'L' && x[2] === 'Select' ? ['L', x[1], x[2]] : x) } :
    { ...g, items: [['M', [[glyph('✓', '#4CAF50'), 'Check', { on: true }], [glyph('✕', '#F44336'), 'Cross'], [glyph('●', '#e6e6e6', 11), 'Dot']]], g.items[1]] });
  return windowShell({ groups, page: carRental(extra), pageTop: -200,
    right: head('SELECTED MARK') + `<div class="cat">Mark ✓</div>` + props([['Colour', '#2E7D32'], ['Rotation', '0 ▾'], ['Locked', '☐']]) + swatches +
      `<div class="cat">Layout (pt)</div>` + props([['X', '270.0'], ['Y', '146.0'], ['Width', '12.0'], ['Height', '12.0']]) +
      `<div style="padding:12px 10px;color:#aaa;font-size:12px;line-height:1.5">Click inside a drawn square to tick it. A / A on the toolbar resizes the mark; the swap button cycles ✓ ✕ ○ — ●.</div>`,
    status: "Mark '✓' placed — use the toolbar to resize, swap or delete." });
}

export function sceneDate() {
  let extra = typed(126, 113, 'Jordan Taylor') + typed(126, 145, '221B Baker Street') + typed(126, 177, 'London');
  extra += `<div class="sel" style="position:absolute;left:123px;top:310px;width:118px;height:20px;font-size:13px;padding:1px 3px;color:#111">October 4, 2026</div><div class="thumb" style="left:236px;top:325px"></div>`;
  extra += typed(386, 313, '11/10/2026');
  const formats = [['04/10/2026', 'dd/MM/yyyy'], ['10/04/2026', 'MM/dd/yyyy'], ['2026-10-04', 'yyyy-MM-dd'], ['04.10.2026', 'dd.MM.yyyy'], ['4 Oct 2026', 'd MMM yyyy'], ['4 October 2026', 'd MMMM yyyy'], ['October 4, 2026', 'MMMM d, yyyy'], ['Sun, 4 Oct 2026', 'ddd, d MMM yyyy']];
  const right = textProps('October 4, 2026', `<div class="cat">Date</div>` + props([['Format', 'MMMM d, yyyy ▾'], ['Day', '4 ▾'], ['Month', 'October ▾'], ['Year', '2026']]) +
    `<div style="padding:6px 8px"><span style="display:inline-block;padding:3px 10px;border:1px solid #3a3a3a;background:#2b2b2b;border-radius:6px;font-size:11px">Today</span></div>`);
  const overlay = `<div class="menu" style="left:1266px;top:426px;width:290px;font-size:12px">${formats.map(([a, b]) => `<div class="mi${b === 'MMMM d, yyyy' ? ' chk hot' : ''}" style="display:flex;justify-content:space-between;gap:12px"><span>${a}</span><span style="color:#999">${b}</span></div>`).join('')}</div>`;
  return windowShell({ page: carRental(extra), right: right, status: 'Pick a format, or change the day, month and year — the date on the page is rewritten to match.' })
    .replace('</div>\n  <div class="statusbar">', overlay + '</div>\n  <div class="statusbar">');
}

export function sceneStamps() {
  let extra = typed(126, 113, 'Jordan Taylor') + typed(126, 145, '221B Baker Street') + typed(126, 177, 'London');
  extra += stamp(240, 168, 200, 50, 'RECEIVED', '#1F4FB5', 'By jtaylor at 10:42, 04/10/2026', -4);
  extra += stamp(135, 640, 170, 40, 'SIGN HERE', '#C62828');
  extra += stamp(372, 552, 150, 42, 'APPROVED', '#1B7A2E', '', -6);
  const groups = [['Business', [['APPROVED', '#1B7A2E'], ['AS IS', '#1F4FB5'], ['COMPLETED', '#1B7A2E'], ['CONFIDENTIAL', '#C62828'], ['DRAFT', '#1F4FB5'], ['FINAL', '#1B7A2E']]],
    ['Signing', [['SIGN HERE', '#C62828'], ['INITIAL HERE', '#C62828'], ['WITNESS', '#C62828']]],
    ['Dynamic', [['RECEIVED', '#1F4FB5'], ['REVIEWED', '#1F4FB5'], ['PAID', '#1B7A2E']]],
    ['More', [['URGENT', '#C62828'], ['COPY', '#555']]]];
  // ribbon Sign group: Stamp on, picker open
  const g2 = FILL_SIGN.map(g => g.h !== 'Sign' ? g : { ...g, items: [g.items[0], ['M', [['pen', 'New Signature…'], ['initials', 'New Initials…'], ['stamp', 'Stamp', { on: true }]]], g.items[2]] });
  const overlay = `<div class="menu" style="left:874px;top:94px;width:200px;max-height:520px;overflow:hidden">${groups.map(([h, items]) => `<div class="gh">${h}</div>` + items.map(([t, c]) => `<div class="mi${t === 'RECEIVED' && h === 'Dynamic' ? ' hot' : ''}" style="padding-left:14px"><span class="dot" style="background:${c}"></span>&nbsp; ${t}</div>`).join('')).join('')}</div>`;
  const right = head('SELECTED STAMP') + `<div class="cat">Stamp</div>` + props([['Title', 'RECEIVED'], ['Line 2', 'By jtaylor at 10:42, 04/10/2026'], ['Colour', '#1F4FB5'], ['Rotation', '-4']]) + swatches +
    `<div style="padding:12px 10px;color:#aaa;font-size:12px;line-height:1.5">Dynamic stamps add your name and the time. Stamps are saved as real PDF stamp annotations, so other PDF readers show them too.</div>`;
  return windowShell({ groups: g2, page: carRental(extra), right, pageTop: -140, status: "'RECEIVED' stamp placed — drag to move, corner to resize, Del to delete." })
    .replace('</div>\n  <div class="statusbar">', overlay + '</div>\n  <div class="statusbar">');
}

export function sceneSign() {
  let extra = typed(126, 113, 'Jordan Taylor') + typed(126, 145, '221B Baker Street') + typed(126, 177, 'London') + typed(126, 209, 'jordan.taylor@example.com') + typed(126, 241, '+44 7700 900123') +
    typed(126, 313, '04/10/2026') + typed(386, 313, '11/10/2026');
  [true, true, false, true, true, true].forEach((m, i) => { extra += m ? check(359, 391 + i * 28) : cross(424, 391 + i * 28); });
  extra += check(399, 571);
  extra += signature(150, 622, 160) + typed(400, 650, '04/10/2026') + initials(86, 690);
  extra += `<div class="sel" style="position:absolute;left:148px;top:620px;width:164px;height:60px"></div><div class="thumb" style="left:307px;top:675px"></div>`;
  const g2 = FILL_SIGN.map(g => g.h !== 'Sign' ? g : { ...g, items: [['L', 'sign', 'Sign', { on: true }], ...g.items.slice(1)] });
  const overlay = `<div class="menu" style="left:700px;top:132px;width:236px;padding:8px">
    <div style="font-size:11px;color:#999;padding:2px 4px 6px">YOUR SIGNATURES</div>
    <div style="background:#fff;border-radius:4px;height:58px;position:relative;margin-bottom:6px;outline:2px solid var(--accent)">${signature(40, 4, 140)}</div>
    <div style="background:#fff;border-radius:4px;height:40px;position:relative;margin-bottom:8px">${initials(90, 5)}</div>
    <div class="mi" style="padding-left:10px">${icon('plus', 13)} &nbsp;Add Signature</div><div class="mi" style="padding-left:10px">${icon('plus', 13)} &nbsp;Add Initials</div></div>`;
  return windowShell({ groups: g2, page: carRental(extra), pageTop: -150, right: head('SELECTED SIGNATURE') + `<div class="cat">Signature</div>` + props([['Page', '1'], ['X', '96.0'], ['Y', '58.0'], ['Width', '120.0'], ['Height', '43.2']]) +
      `<div style="padding:12px 10px;color:#aaa;font-size:12px;line-height:1.5">Draw, type or import a signature once; it's kept for next time. When you're done, <b style="color:#ddd">Flatten &amp; Save</b> makes everything part of the page.</div>`,
    status: 'Signature placed — drag to move, drag the corner to resize, right-click to delete.' })
    .replace('</div>\n  <div class="statusbar">', overlay + '</div>\n  <div class="statusbar">');
}

