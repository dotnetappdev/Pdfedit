import { icon } from './icons.mjs';
export { icon };

// Dark theme tokens (PdfEdit/Themes/DarkTheme.xaml)
export const CSS = `
:root{--accent:#0A84FF;--app:#121212;--panel:#1D1D1D;--side:#252525;--content:#3C3C3C;--fg:#E8E8E8;--dim:#8a8a8a;
--border:#2C2C2C;--input:#2A2A2A;--hover:#303030;--active:#1E2D3D;--ribbon:#262626;--tabs:#1b1b1b}
*{box-sizing:border-box;margin:0;padding:0}
body{background:var(--app);color:var(--fg);font:13px "Segoe UI","Liberation Sans","DejaVu Sans",sans-serif;-webkit-font-smoothing:antialiased}
.win{display:flex;flex-direction:column;background:var(--panel);overflow:hidden}
.title{height:34px;display:flex;align-items:center;gap:10px;padding:0 0 0 10px;background:#1b1b1b}
.logo{width:20px;height:20px;border-radius:4px;background:var(--accent);color:#fff;font-weight:700;font-size:12px;display:grid;place-items:center}
.title b{font-weight:600}.title .doc{color:var(--dim)}
.caps{margin-left:auto;display:flex}.caps span{width:46px;height:34px;display:grid;place-items:center;color:#ccc}
.tabs{display:flex;gap:2px;padding:0 8px;background:var(--tabs);border-bottom:1px solid #333}
.tab{padding:7px 12px 6px;color:#c8c8c8;border-bottom:2px solid transparent}
.tab.on{color:#fff;font-weight:600;border-color:var(--accent);background:var(--ribbon)}
.ribbon{display:flex;background:var(--ribbon);border-bottom:1px solid #333;height:104px;padding:4px 4px 0}
.grp{display:flex;flex-direction:column;border-right:1px solid #3a3a3a;padding:0 6px}
.grp .items{display:flex;gap:2px;flex:1;align-items:flex-start}
.grp .gl{font-size:11px;color:#a8a8a8;text-align:center;padding:3px 0 4px;white-space:nowrap}
.lg{display:flex;flex-direction:column;align-items:center;width:58px;padding:4px 2px;border-radius:4px;color:#e6e6e6;font-size:11.5px;text-align:center;line-height:1.15}
.lg svg{margin-bottom:5px}
.lg.on,.md.on{background:#2f4f73;outline:1px solid #3d6fa3}
.col{display:flex;flex-direction:column;gap:1px}
.md{display:flex;align-items:center;gap:6px;height:24px;padding:0 6px;border-radius:3px;font-size:11.5px;white-space:nowrap;color:#e6e6e6}
.md.dis{color:#777}
.sm{width:24px;height:22px;display:grid;place-items:center;border-radius:3px}
.combo{height:22px;border:1px solid #444;background:#2b2b2b;border-radius:3px;display:flex;align-items:center;justify-content:space-between;padding:0 6px;font-size:11px;color:#ddd;gap:8px}
.sw{width:15px;height:15px;border-radius:3px;border:1px solid #555;display:inline-block}
.dot{width:10px;height:10px;border-radius:2px;display:inline-block}
.statusbar{height:24px;background:var(--accent);color:#fff;display:flex;align-items:center;justify-content:space-between;padding:0 10px;font-size:12px}
.pane-h{display:flex;gap:14px;padding:8px 10px 0;border-bottom:1px solid var(--border);font-size:12px;color:#bbb}
.pane-h span{padding-bottom:6px}.pane-h .on{color:#fff;font-weight:600;border-bottom:2px solid var(--accent)}
.page{background:#fff;color:#111;box-shadow:0 2px 14px rgba(0,0,0,.55);position:relative;font-family:"Liberation Sans",Arial,sans-serif}
.fld{position:absolute;background:#e1e9ff;border:1px solid #7a9ee0;font-size:12px;padding:2px 5px;color:#111}
.prop{display:grid;grid-template-columns:96px 1fr;font-size:12px}
.prop div{padding:4px 8px;border-bottom:1px solid #2a2a2a}.prop div:nth-child(odd){color:#aaa;border-right:1px solid #2a2a2a}
.cat{background:#2a2a2a;font-weight:600;font-size:12px;padding:4px 8px}
.menu{position:absolute;background:#2b2b2b;border:1px solid #474747;border-radius:6px;box-shadow:0 8px 24px rgba(0,0,0,.5);padding:4px 0;font-size:12.5px;z-index:20}
.menu .mi{padding:6px 14px 6px 30px;position:relative;white-space:nowrap}
.menu .mi.hot{background:#3a3a3a}.menu .mi.chk::before{content:"✓";position:absolute;left:11px;color:#fff}
.menu .sep{height:1px;background:#444;margin:4px 0}
.menu .gh{padding:7px 12px 3px;font-size:10.5px;font-weight:600;color:#999;letter-spacing:.3px}
.mini{position:absolute;display:flex;height:26px;background:#232323;border:1px solid #464646;border-radius:4px 4px 0 0;color:#c8c8c8;font-size:12px;z-index:10}
.mini span{display:grid;place-items:center;padding:0 8px;height:100%}.mini .g{background:#373737;border-right:1px solid #5a5a5a;padding:0 6px}
.mini .hot{background:#3c3c3c}
.sel{outline:1px solid #2f7de1}
.thumb{position:absolute;width:10px;height:10px;background:#0078d7;border:1px solid #fff;border-radius:2px}
`;

export function titlebar(doc) {
  return `<div class="title"><div class="logo">P</div><b>PdfEdit</b><span class="doc">— ${doc}</span>
  <div class="caps"><span>&#8212;</span><span>&#9744;</span><span>&#10005;</span></div></div>`;
}
const TABS = ['File', 'Home', 'Fill &amp; Sign', 'Edit', 'View', 'Tools', 'Forms', 'AI Assistant', 'Design'];
export function tabs(on) {
  return `<div class="tabs">${TABS.map(t => `<div class="tab${t === on ? ' on' : ''}">${t}</div>`).join('')}</div>`;
}
// Ribbon spec: groups [{h, items:[ ['L',icon,label,{on}] | ['M',[[icon,label,opts],...]] | ['H',html] ]}]
export function ribbon(groups) {
  const it = x => {
    if (x[0] === 'L') return `<div class="lg${x[3]?.on ? ' on' : ''}">${x[1].startsWith('<') ? x[1] : icon(x[1], 30, '#e6e6e6', 1.4)}<div>${x[2]}</div></div>`;
    if (x[0] === 'M') return `<div class="col">${x[1].map(m => `<div class="md${m[2]?.on ? ' on' : ''}${m[2]?.dis ? ' dis' : ''}">${m[0].startsWith('<') ? m[0] : icon(m[0], 16, m[2]?.color || (m[2]?.dis ? '#777' : '#e6e6e6'))}${m[1]}</div>`).join('')}</div>`;
    return x[1];
  };
  return `<div class="ribbon">${groups.map(g => `<div class="grp"><div class="items">${g.items.map(it).join('')}</div><div class="gl">${g.h}</div></div>`).join('')}</div>`;
}
export const glyph = (t, color = '#e6e6e6', size = 15, extra = '') =>
  `<span style="display:inline-grid;place-items:center;width:16px;height:16px;color:${color};font-weight:700;font-size:${size}px;${extra}">${t}</span>`;
