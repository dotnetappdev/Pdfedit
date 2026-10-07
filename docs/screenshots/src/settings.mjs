// Themes gallery and the Settings → Accessibility tab.
// Colours come from PdfEdit/Themes/*.xaml and PdfEdit/Services/ThemeCatalog.cs.
import { icon } from './icons.mjs';

// name, app, panel, sidebar, content (canvas), statusbar bg, statusbar fg, fg, dim, border, accent, input
const THEMES = [
  ['Light', '#F5F5F5', '#FAFAFA', '#EBEBEB', '#D8D8D8', '#EAEAEA', '#444444', '#1A1A1A', '#666666', '#D0D0D0', '#0A84FF', '#FFFFFF'],
  ['Dark', '#121212', '#1D1D1D', '#252525', '#3C3C3C', '#0A84FF', '#FFFFFF', '#E8E8E8', '#8A8A8A', '#2C2C2C', '#0A84FF', '#2A2A2A'],
  ['High contrast', '#000000', '#000000', '#000000', '#0A0A0A', '#1AEBFF', '#000000', '#FFFFFF', '#FFFF00', '#FFFFFF', '#1AEBFF', '#000000'],
  ['Office', '#F3F2F1', '#FFFFFF', '#F3F2F1', '#E1DFDD', '#185ABD', '#FFFFFF', '#323130', '#605E5C', '#E1DFDD', '#185ABD', '#FFFFFF'],
  ['Office Black', '#1F1F1F', '#292929', '#262626', '#3B3B3B', '#1F1F1F', '#C8C8C8', '#F3F3F3', '#ABABAB', '#3F3F3F', '#4FA3F7', '#333333'],
  ['Dracula', '#21222C', '#282A36', '#21222C', '#191A21', '#191A21', '#F8F8F2', '#F8F8F2', '#9AA5CE', '#44475A', '#BD93F9', '#21222C'],
  ['Nord', '#2E3440', '#3B4252', '#2E3440', '#434C5E', '#3B4252', '#D8DEE9', '#ECEFF4', '#A3ABB9', '#4C566A', '#5E81AC', '#2E3440'],
  ['One Dark', '#21252B', '#282C34', '#21252B', '#3A3F4B', '#21252B', '#9DA5B4', '#D7DAE0', '#7F848E', '#181A1F', '#4D8FE0', '#1D1F23'],
  ['Monokai', '#1E1F1C', '#272822', '#1E1F1C', '#3E3D32', '#1E1F1C', '#CFCFC2', '#F8F8F2', '#A59F85', '#3E3D32', '#F92672', '#1E1F1C'],
  ['Solarized Light', '#EEE8D5', '#FDF6E3', '#EEE8D5', '#E4DCC5', '#EEE8D5', '#586E75', '#073642', '#657B83', '#DDD6C1', '#268BD2', '#FDF6E3'],
  ['Solarized Dark', '#002B36', '#073642', '#002B36', '#0A4252', '#00212B', '#93A1A1', '#EEE8D5', '#93A1A1', '#0E4553', '#268BD2', '#002B36'],
  ['GitHub Light', '#F6F8FA', '#FFFFFF', '#F6F8FA', '#E7EBEF', '#F6F8FA', '#57606A', '#1F2328', '#656D76', '#D0D7DE', '#0969DA', '#FFFFFF'],
];

const miniPage = (w, h) => `<div style="width:${w}px;height:${h}px;background:#fff;box-shadow:0 1px 6px rgba(0,0,0,.35);padding:12px 12px;font:7px Arial,sans-serif;color:#222">
  <div style="font-weight:700;font-size:9px;margin-bottom:6px">Car Rental Checklist</div>
  ${['Name', 'Pick-up date', 'Licence no.', 'Return'].map((l, i) => `<div style="display:flex;gap:6px;align-items:center;margin:5px 0"><span style="width:44px">${l}</span>
    <span style="flex:1;height:10px;background:${i === 1 ? '#e1e9ff' : 'transparent'};border-bottom:1px solid #999"></span></div>`).join('')}
  <div style="display:flex;gap:10px;margin-top:8px">${['Fuel', 'Clean', 'Keys'].map((l, i) => `<span style="display:flex;gap:3px;align-items:center"><span style="width:8px;height:8px;border:1px solid #333;display:grid;place-items:center;font-size:7px;color:#2E7D32">${i < 2 ? '✓' : ''}</span>${l}</span>`).join('')}</div>
  <div style="margin-top:12px;height:1px;background:#bbb"></div><div style="font-size:6px;color:#777;margin-top:2px">Signature</div>
</div>`;

function miniWindow([name, app, panel, side, content, sbBg, sbFg, fg, dim, border, accent, input]) {
  const ic = n => icon(n, 15, fg, 1.6);
  return `<div style="display:flex;flex-direction:column;gap:7px">
  <div style="width:300px;height:206px;display:flex;flex-direction:column;background:${panel};border:1px solid ${border};border-radius:6px;overflow:hidden;color:${fg};font-size:8px">
    <div style="height:18px;display:flex;align-items:center;gap:5px;padding:0 6px;background:${app}">
      <span style="width:10px;height:10px;border-radius:2px;background:${accent}"></span><b style="font-size:8px">PdfEdit</b>
      <span style="margin-left:auto;color:${dim}">— ☐ ✕</span></div>
    <div style="display:flex;gap:7px;padding:2px 6px 0;background:${app};font-size:7.5px">
      ${['Home', 'Complete &amp; Sign', 'Edit', 'View'].map((t, i) => `<span style="padding:2px 1px;${i === 1 ? `border-bottom:2px solid ${accent};font-weight:700` : `color:${dim}`}">${t}</span>`).join('')}</div>
    <div style="height:34px;display:flex;align-items:center;gap:9px;padding:0 8px;background:${panel};border-bottom:1px solid ${border}">
      ${ic('fill')}${ic('calendar')}${ic('sign')}${ic('stamp')}<span style="width:1px;height:22px;background:${border}"></span>${ic('highlight')}${ic('note')}${ic('draw')}
      <span style="margin-left:auto;padding:3px 7px;border-radius:3px;background:${accent};color:${sbBg === accent ? '#fff' : (name === 'High contrast' ? '#000' : '#fff')};font-size:7px">Save</span></div>
    <div style="flex:1;display:flex;min-height:0">
      <div style="width:44px;background:${side};border-right:1px solid ${border};display:flex;flex-direction:column;align-items:center;gap:6px;padding-top:6px">
        <div style="width:26px;height:34px;background:#fff;outline:2px solid ${accent}"></div><div style="width:26px;height:34px;background:#fff;opacity:.85"></div></div>
      <div style="flex:1;background:${content};display:grid;place-items:center;overflow:hidden">${miniPage(130, 118)}</div>
      <div style="width:66px;background:${side};border-left:1px solid ${border};padding:6px 5px;font-size:7px">
        <div style="color:${dim};font-weight:700;margin-bottom:4px">PROPERTIES</div>
        ${['Font', 'Size', 'Colour'].map(l => `<div style="color:${dim};margin-top:4px">${l}</div><div style="height:11px;background:${input};border:1px solid ${border};border-radius:2px;margin-top:1px"></div>`).join('')}</div>
    </div>
    <div style="height:13px;display:flex;align-items:center;justify-content:space-between;padding:0 6px;background:${sbBg};color:${sbFg};font-size:7px"><span>Ready</span><span>Page 1 of 2 · 100%</span></div>
  </div>
  <div style="text-align:center;font-size:13px;color:#e8e8e8">${name}</div></div>`;
}

/** One theme, full size: the gallery window drawn at 3× for a crisp README image. */
export function sceneTheme(name) {
  const t = THEMES.find(x => x[0] === name);
  const win = miniWindow(t).replace(/<div style="text-align:center;font-size:13px;color:#e8e8e8">[^<]*<\/div><\/div>$/, '</div>');
  return `<div class="win" style="display:inline-block;background:transparent"><div style="zoom:3">${win}</div></div>`;
}

export function sceneThemes() {
  return `<div class="win" style="display:inline-grid;grid-template-columns:repeat(4,300px);gap:18px 20px;padding:20px 22px;background:#161616">
    ${THEMES.map(miniWindow).join('')}</div>`;
}

// ── Settings → Accessibility (dark theme) ────────────────────────────────────
const cb = (label, on = true) => `<div style="display:flex;align-items:center;gap:8px;margin:5px 0">
  <span style="width:15px;height:15px;border-radius:3px;border:1px solid ${on ? '#0A84FF' : '#666'};background:${on ? '#0A84FF' : 'transparent'};display:grid;place-items:center;color:#fff;font-size:11px">${on ? '✓' : ''}</span>${label}</div>`;
const sh = t => `<div style="font-size:11px;font-weight:600;color:#8a8a8a;letter-spacing:.6px;margin:18px 0 8px">${t}</div>`;
const slider = (pct, label) => `<div style="display:flex;align-items:center;gap:10px;flex:1">
  <div style="flex:1;height:4px;background:#3a3a3a;border-radius:2px;position:relative">
    <div style="width:${pct}%;height:100%;background:#0A84FF;border-radius:2px"></div>
    <span style="position:absolute;left:calc(${pct}% - 7px);top:-5px;width:14px;height:14px;border-radius:50%;background:#0A84FF;border:2px solid #1D1D1D"></span></div>
  <span style="width:44px">${label}</span></div>`;
const row = (label, control) => `<div style="display:flex;align-items:center;gap:12px;margin:8px 0"><span style="width:120px">${label}</span>${control}</div>`;
const combo = (text, w = 180) => `<div style="width:${w}px;height:28px;border:1px solid #444;background:#2A2A2A;border-radius:3px;display:flex;align-items:center;justify-content:space-between;padding:0 8px">${text}<span style="color:#999">▾</span></div>`;
const dim = t => `<div style="color:#8a8a8a;font-size:11.5px;line-height:1.45;margin:4px 0">${t}</div>`;
const link = t => `<span style="color:#4DA3FF;text-decoration:underline">${t}</span>`;

export function sceneAccessibility() {
  const areas = ['Ribbon &amp; Toolbar', 'Menus', 'Side Panels', 'AI Helper', 'Status Bar', 'Dialogs &amp; Settings'];
  return `<div class="win" style="width:600px;border:1px solid #333">
  <div class="title"><div class="logo">P</div><b>Settings — PdfEdit</b><div class="caps"><span>&#10005;</span></div></div>
  <div style="display:flex;gap:2px;padding:6px 10px 0;border-bottom:1px solid #333;font-size:12.5px">
    ${['Appearance', 'Editor', 'AI Helper', 'Keyboard', 'Cloud', 'OCR', 'Accessibility'].map(t => `<span style="padding:6px 10px;${t === 'Accessibility' ? 'background:#2A2A2A;border:1px solid #3a3a3a;border-bottom:none;border-radius:4px 4px 0 0;color:#fff;font-weight:600' : 'color:#bbb'}">${t}</span>`).join('')}</div>
  <div style="padding:4px 22px 18px;font-size:13px">
    ${sh('SIZE')}
    ${row('Interface scale:', slider(33, '100%'))}
    ${row('Text size (all):', slider(33, '120%'))}
    ${row('Icon size:', combo('Large (125%)'))}
    ${dim('The interface scale applies when you click Save. Ctrl+Plus, Ctrl+Minus and Ctrl+0 change it at any time. Text and icon sizes change straight away.')}
    ${sh('TEXT SIZE FOR EACH PART OF THE WINDOW')}
    <div style="display:flex;gap:16px">
      <div style="width:190px;border:1px solid #444;background:#2A2A2A">${areas.map((a, i) => `<div style="padding:4px 8px;${i === 1 ? 'background:#1E2D3D;outline:1px solid #0A84FF' : ''}">${a}</div>`).join('')}</div>
      <div style="flex:1">
        <div style="font-size:14px;font-weight:600">Menus</div>
        ${dim('Right-click menus and the ribbon\'s drop-down menus.')}
        <div style="display:flex;align-items:center;gap:10px;margin:6px 0 8px">Size: ${combo('16', 70)}</div>
        <div style="border:1px solid #444;background:#2A2A2A;border-radius:3px;padding:8px;font-size:16px">AaBbCc 0123 — The quick brown fox.</div>
      </div></div>
    <div style="display:inline-block;margin-top:8px;padding:4px 12px;border:1px solid #444;background:#2A2A2A;border-radius:3px">Reset text sizes</div>
    ${sh('SCREEN READER')}
    ${dim('A screen reader is running. PdfEdit\'s buttons, boxes and panels all have names it can read.')}
    ${cb('Tell my screen reader about status messages, page and tool changes')}
    <div style="margin:6px 0 2px">Announce:</div>
    <div style="display:flex;gap:18px;margin-left:12px">${cb('Status messages')}${cb('Page changes')}${cb('Tool changes')}</div>
    ${cb('Show high-contrast focus indicators', false)}
    ${sh('NARRATION')}
    ${dim('PdfEdit can read things out in a Windows voice, without a screen reader. The voice and speed are also used by View → Read Page.')}
    ${cb('Speak the announcements ticked above')}
    ${cb('Speak buttons, boxes and menu items as I move to them with the keyboard')}
    ${cb('Speak tooltips when I hover over a button', false)}
    ${cb('Stay quiet while a screen reader is running')}
    <div style="display:flex;align-items:center;gap:12px;margin:8px 0"><span style="width:120px">Voice:</span>${combo('Microsoft Libby (Natural)', 300)}
      <span style="padding:4px 12px;border:1px solid #444;background:#2A2A2A;border-radius:3px">Test</span></div>
    ${row('Speed:', slider(25, '1×'))}
    ${row('Volume:', slider(100, '100%'))}
    ${sh('KEYBOARD')}
    ${dim(`Everything in PdfEdit can be reached from the keyboard: Alt shows the ribbon's key tips, F1 lists the shortcuts, and you can change them on the ${link('Keyboard')} tab.`)}
  </div>
  <div style="display:flex;justify-content:flex-end;gap:8px;padding:10px 16px;background:#252525;border-top:1px solid #333">
    <span style="width:80px;height:30px;display:grid;place-items:center;border:1px solid #444;background:#2A2A2A;border-radius:3px">Cancel</span>
    <span style="width:80px;height:30px;display:grid;place-items:center;background:#0A84FF;color:#fff;border-radius:3px">Save</span></div>
</div>`;
}
