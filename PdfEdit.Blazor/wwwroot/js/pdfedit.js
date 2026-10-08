// Browser helpers for the PdfEdit editor: things Blazor Server can't do from .NET alone.
window.pdfedit = (() => {
    let pageObserver = null;

    return {
        // Tells .NET which page is in view as the user scrolls (for the status bar and thumbnails).
        observePages(dotnet, viewer) {
            pageObserver?.disconnect();
            const visible = new Map();
            pageObserver = new IntersectionObserver(entries => {
                for (const e of entries) visible.set(+e.target.dataset.page, e.intersectionRatio);
                let best = -1, ratio = 0;
                for (const [p, r] of visible) if (r > ratio) { ratio = r; best = p; }
                if (best >= 0) dotnet.invokeMethodAsync('OnPageInView', best);
            }, { root: viewer, threshold: [0, .25, .5, .75, 1] });
            viewer.querySelectorAll('.pe-page[data-page]').forEach(p => pageObserver.observe(p));
        },

        scrollToPage(index, smooth) {
            document.getElementById('page-' + index)?.scrollIntoView({ behavior: smooth ? 'smooth' : 'auto', block: 'start' });
        },

        // Scrolls so a rectangle (in % of its page) is in view.
        scrollToSpot(index, topPercent) {
            const page = document.getElementById('page-' + index);
            const viewer = page?.closest('.pe-viewer');
            if (!page || !viewer) return;
            const y = page.offsetTop + page.offsetHeight * topPercent / 100 - viewer.clientHeight / 3;
            viewer.scrollTo({ top: Math.max(0, y), behavior: 'smooth' });
        },

        focus(id) { document.getElementById(id)?.focus(); },

        // Fits the ribbon to the window like the Fluent ribbon: groups collapse from the right into a
        // button that drops the group down, and come back as the window widens.
        ribbonFit() {
            const ribbon = document.querySelector('.pe-ribbon');
            if (!ribbon || ribbon.__peFit) return;
            ribbon.__peFit = true;
            const fit = () => {
                const groups = [...ribbon.querySelectorAll(':scope > .pe-ribbon-row > .pe-group')];
                groups.forEach(g => g.classList.remove('collapsed', 'open'));
                for (let i = groups.length - 1; i >= 0 && ribbon.scrollWidth > ribbon.clientWidth + 1; i--) {
                    const g = groups[i];
                    const icon = g.querySelector('.pe-group-body i.bi')?.className.match(/bi-[\w-]+/g)?.find(c => c !== 'bi');
                    const ci = g.querySelector('.pe-group-collapsed > i');
                    if (icon && ci) ci.className = 'bi ' + icon;
                    g.classList.add('collapsed');
                }
            };
            let pending = 0;
            const later = () => { cancelAnimationFrame(pending); pending = requestAnimationFrame(fit); };
            new ResizeObserver(later).observe(ribbon);
            new MutationObserver(m => { if (m.some(r => r.type === 'childList' && (r.target === ribbon || r.target.classList?.contains('pe-ribbon-row')))) later(); })
                .observe(ribbon, { childList: true, subtree: true });
            ribbon.addEventListener('click', e => {
                const toggle = e.target.closest('.pe-group-collapsed');
                if (toggle) {
                    const g = toggle.parentElement, open = !g.classList.contains('open');
                    ribbon.querySelectorAll('.pe-group.open').forEach(x => x.classList.remove('open'));
                    if (open) {
                        g.classList.add('open');
                        const r = toggle.getBoundingClientRect(), body = g.querySelector('.pe-group-body');
                        body.style.top = r.bottom + 2 + 'px';
                        body.style.left = Math.max(4, Math.min(r.left, window.innerWidth - body.offsetWidth - 8)) + 'px';
                    }
                    return;
                }
                // A command in a dropped-down group closes it (not the inputs and pickers).
                if (e.target.closest('.pe-group.open .pe-group-body .pe-rbtn:not([aria-haspopup])')) {
                    setTimeout(() => ribbon.querySelectorAll('.pe-group.open').forEach(x => x.classList.remove('open')), 0);
                }
            });
            document.addEventListener('mousedown', e => {
                if (!e.target.closest('.pe-group.open, .pe-rmenu-list, .pe-flyout-backdrop'))
                    ribbon.querySelectorAll('.pe-group.open').forEach(x => x.classList.remove('open'));
            });
            fit();
        },

        // A ribbon drop-down's menu, placed under its button (the ribbon itself scrolls and clips).
        placeMenu(root) {
            const button = root?.querySelector('.pe-rbtn'), list = root?.querySelector('.pe-rmenu-list');
            if (!button || !list) return;
            const r = button.getBoundingClientRect();
            list.style.top = r.bottom + 2 + 'px';
            list.style.left = Math.max(4, Math.min(r.left, window.innerWidth - list.offsetWidth - 4)) + 'px';
        },

        viewerWidth(viewer) { return viewer ? viewer.clientWidth : 1000; },

        // Each document tab remembers where it was scrolled to.
        scrollTop(viewer) { return viewer ? viewer.scrollTop : 0; },
        setScrollTop(viewer, top) { if (viewer) requestAnimationFrame(() => { viewer.scrollTop = top; }); },

        download(url) {
            const a = document.createElement('a');
            a.href = url;
            a.download = '';
            document.body.appendChild(a);
            a.click();
            a.remove();
        },

        openInNewTab(url) { window.open(url, '_blank', 'noopener'); },

        // Cloud sign-in in a small window; false if the browser blocked it.
        openPopup(url) {
            const w = 520, h = 680;
            const left = window.screenX + Math.max(0, (window.outerWidth - w) / 2), top = window.screenY + Math.max(0, (window.outerHeight - h) / 2);
            return !!window.open(url, 'pdfedit-signin', `popup,width=${w},height=${h},left=${left},top=${top}`);
        },

        // Drag to move, or drag the corner handle to resize, anything marked data-drag (Prepare Form
        // field boxes, things placed on a page). Moves are shown live and reported to .NET at the end
        // as percentages of the page.
        listenDrag(dotnet) {
            // Right-click: the item under the pointer, or the page. Text being selected or edited and
            // form fields keep the browser's own menu (copy, paste, spelling).
            document.addEventListener('contextmenu', e => {
                const t = e.target;
                if (t.closest('input, select, [contenteditable="true"], .pe-ctxmenu')) return;
                if (t.tagName === 'TEXTAREA' && t.selectionStart !== t.selectionEnd) return;
                const item = t.closest('[data-drag^="i:"], [data-drag^="f:"]');
                const link = item ? null : t.closest('[data-link]');
                const page = t.closest('.pe-page[data-page]');
                if (!item && !link && !page) return;
                e.preventDefault();
                const r = page?.getBoundingClientRect();
                const key = item ? item.dataset.drag : link ? 'l:' + link.dataset.link : '';
                dotnet.invokeMethodAsync('OnContextMenu', key, page ? +page.dataset.page : -1,
                    r ? (e.clientX - r.left) / r.width * 100 : 0, r ? (e.clientY - r.top) / r.height * 100 : 0, e.clientX, e.clientY,
                    window.innerWidth, window.innerHeight);
            });
            document.addEventListener('pointerdown', e => {
                const box = e.target.closest?.('[data-drag]');
                if (!box || e.button !== 0 || box.dataset.locked) return;
                const page = box.closest('.pe-page');
                if (!page || e.target.closest('input, textarea, select, button, [data-nodrag]')) return;
                e.preventDefault();   // no text selection or native image drag (which cancels the pointer)
                // Which edges a handle drags: n, s, e, w or a corner (data-resize="1" is the bottom-right one).
                const handle = e.target.closest('[data-resize]')?.dataset.resize;
                const resize = handle ? (handle === '1' ? 'se' : handle) : '';
                const pr = page.getBoundingClientRect();
                const br = box.getBoundingClientRect();
                const start = { x: e.clientX, y: e.clientY, l: br.left - pr.left, t: br.top - pr.top, w: br.width, h: br.height };
                let moved = false;
                const move = ev => {
                    const dx = ev.clientX - start.x, dy = ev.clientY - start.y;
                    if (!moved && Math.abs(dx) + Math.abs(dy) < 4) return;
                    moved = true;
                    box.classList.add('pe-dragging');
                    let l = start.l, t = start.t, w = start.w, h = start.h;
                    if (resize) {
                        if (resize.includes('e')) w = Math.max(6, Math.min(start.w + dx, pr.width - start.l));
                        if (resize.includes('s')) h = Math.max(6, Math.min(start.h + dy, pr.height - start.t));
                        if (resize.includes('w')) { const nl = Math.min(Math.max(0, start.l + dx), start.l + start.w - 6); w = start.w + start.l - nl; l = nl; }
                        if (resize.includes('n')) { const nt = Math.min(Math.max(0, start.t + dy), start.t + start.h - 6); h = start.h + start.t - nt; t = nt; }
                    }
                    else { l = Math.min(Math.max(0, start.l + dx), pr.width - w); t = Math.min(Math.max(0, start.t + dy), pr.height - h); }
                    box.style.left = (l / pr.width * 100) + '%';
                    box.style.top = (t / pr.height * 100) + '%';
                    box.style.width = (w / pr.width * 100) + '%';
                    box.style.height = (h / pr.height * 100) + '%';
                };
                const up = () => {
                    document.removeEventListener('pointermove', move);
                    document.removeEventListener('pointerup', up);
                    document.removeEventListener('pointercancel', up);
                    box.classList.remove('pe-dragging');
                    if (!moved) return;
                    const r = box.getBoundingClientRect();
                    dotnet.invokeMethodAsync('OnBoxMoved', box.dataset.drag,
                        (r.left - pr.left) / pr.width * 100, (r.top - pr.top) / pr.height * 100,
                        r.width / pr.width * 100, r.height / pr.height * 100);
                    // Don't let the end of a drag count as a click.
                    box.addEventListener('click', ev => ev.stopPropagation(), { capture: true, once: true });
                };
                document.addEventListener('pointermove', move);
                document.addEventListener('pointerup', up);
                document.addEventListener('pointercancel', up);
            });
        },

        // Ink, lines and the Measure tools: the page's tool layer (data-sketch="ink|line|poly|closed")
        // draws the stroke live, then hands the points to .NET as percentages of the page.
        listenSketch(dotnet) {
            const NS = 'http://www.w3.org/2000/svg';
            let poly = null;   // perimeter / area being clicked out
            const at = (layer, e) => {
                const r = layer.getBoundingClientRect();
                return [(e.clientX - r.left) / r.width * 100, (e.clientY - r.top) / r.height * 100];
            };
            const perUnit = u => ({ mm: 72 / 25.4, cm: 72 / 2.54, pt: 1 }[u] ?? 72);
            const toPts = (layer, p) => [p[0] / 100 * +layer.dataset.ptw, p[1] / 100 * +layer.dataset.pth];
            const len = (layer, pts) => {
                let d = 0;
                for (let i = 1; i < pts.length; i++) {
                    const a = toPts(layer, pts[i - 1]), b = toPts(layer, pts[i]);
                    d += Math.hypot(b[0] - a[0], b[1] - a[1]);
                }
                return d;
            };
            const area = (layer, pts) => {
                let a = 0;
                for (let i = 0; i < pts.length; i++) {
                    const p = toPts(layer, pts[i]), q = toPts(layer, pts[(i + 1) % pts.length]);
                    a += p[0] * q[1] - q[0] * p[1];
                }
                return Math.abs(a) / 2;
            };
            const fmt = (v, u) => v.toFixed(u === 'pt' ? 0 : 2);
            const measureText = (layer, pts) => {
                const u = layer.dataset.unit, k = perUnit(u);
                if (layer.dataset.measure === 'area') return pts.length > 2 ? fmt(area(layer, pts) / (k * k), u) + ' sq ' + u : '';
                return layer.dataset.measure ? fmt(len(layer, pts) / k, u) + ' ' + u : '';
            };
            const start = (layer, closed) => {
                const svg = document.createElementNS(NS, 'svg');
                svg.setAttribute('class', 'pe-sketch-live');
                svg.setAttribute('viewBox', '0 0 100 100');
                svg.setAttribute('preserveAspectRatio', 'none');
                const line = document.createElementNS(NS, closed ? 'polygon' : 'polyline');
                const px = Math.max(1, +layer.dataset.width * layer.getBoundingClientRect().width / +layer.dataset.ptw);
                line.setAttribute('fill', closed ? layer.dataset.color + '22' : 'none');
                line.setAttribute('stroke', layer.dataset.color);
                line.setAttribute('stroke-width', px);
                line.setAttribute('stroke-linecap', 'round');
                line.setAttribute('stroke-linejoin', 'round');
                line.setAttribute('vector-effect', 'non-scaling-stroke');
                svg.appendChild(line);
                layer.appendChild(svg);
                const label = document.createElement('div');
                label.className = 'pe-sketch-label';
                if (layer.dataset.measure) layer.appendChild(label);
                return { layer, svg, line, label, pts: [] };
            };
            const draw = (s, extra) => {
                const pts = extra ? [...s.pts, extra] : s.pts;
                s.line.setAttribute('points', pts.map(p => p[0] + ',' + p[1]).join(' '));
                const text = measureText(s.layer, pts);
                s.label.textContent = text;
                s.label.style.display = text ? '' : 'none';
                const last = pts[pts.length - 1];
                if (last) { s.label.style.left = last[0] + '%'; s.label.style.top = last[1] + '%'; }
            };
            const finish = (s, pts) => {
                const send = dotnet.invokeMethodAsync('OnSketch', +s.layer.dataset.page, pts.flat());
                send.finally(() => { s.svg.remove(); s.label.remove(); });
            };
            const cancel = s => { s.svg.remove(); s.label.remove(); };
            // Drop the repeated point a double-click adds.
            const tidy = (layer, pts) => pts.filter((p, i) => i === 0 || len(layer, [pts[i - 1], p]) > 1.5);
            const finishPoly = () => {
                const s = poly; poly = null;
                if (!s || !s.layer.isConnected) return;
                const pts = tidy(s.layer, s.pts);
                if (pts.length < 2) { cancel(s); return; }
                finish(s, pts);
            };

            document.addEventListener('pointerdown', e => {
                const layer = e.target.closest?.('.pe-tool-layer[data-sketch]');
                if (!layer || e.button !== 0) return;
                e.preventDefault();
                const mode = layer.dataset.sketch;
                if (mode === 'pan') {
                    // Hand: drag the pages around (no .NET round trip).
                    const viewer = layer.closest('.pe-viewer');
                    const st = { x: e.clientX, y: e.clientY, l: viewer.scrollLeft, t: viewer.scrollTop };
                    layer.setPointerCapture(e.pointerId);
                    layer.classList.add('panning');
                    const mv = ev => { viewer.scrollLeft = st.l - (ev.clientX - st.x); viewer.scrollTop = st.t - (ev.clientY - st.y); };
                    const end = () => { layer.removeEventListener('pointermove', mv); layer.removeEventListener('pointerup', end); layer.classList.remove('panning'); };
                    layer.addEventListener('pointermove', mv);
                    layer.addEventListener('pointerup', end);
                    return;
                }
                if (mode === 'zoom') {
                    // Marquee zoom: a rectangle; .NET zooms so it fills the window (a click zooms in a step).
                    const a0 = at(layer, e);
                    const box = document.createElement('div');
                    box.className = 'pe-zoom-rect';
                    layer.appendChild(box);
                    layer.setPointerCapture(e.pointerId);
                    const place = p => {
                        box.style.left = Math.min(a0[0], p[0]) + '%'; box.style.top = Math.min(a0[1], p[1]) + '%';
                        box.style.width = Math.abs(p[0] - a0[0]) + '%'; box.style.height = Math.abs(p[1] - a0[1]) + '%';
                    };
                    const mv = ev => place(at(layer, ev));
                    const end = ev => {
                        layer.removeEventListener('pointermove', mv); layer.removeEventListener('pointerup', end);
                        box.remove();
                        const p = at(layer, ev);
                        dotnet.invokeMethodAsync('OnSketch', +layer.dataset.page, [a0[0], a0[1], p[0], p[1]]);
                    };
                    layer.addEventListener('pointermove', mv);
                    layer.addEventListener('pointerup', end);
                    return;
                }
                if (mode === 'poly' || mode === 'closed') {
                    if (poly && poly.layer !== layer) { cancel(poly); poly = null; }
                    poly ??= start(layer, mode === 'closed');
                    poly.pts.push(at(layer, e));
                    draw(poly);
                    return;
                }
                const s = start(layer, false);
                s.pts.push(at(layer, e));
                layer.setPointerCapture(e.pointerId);
                const move = ev => {
                    const p = at(layer, ev);
                    if (mode === 'ink') {
                        const last = s.pts[s.pts.length - 1];
                        if (Math.hypot((p[0] - last[0]) * layer.clientWidth, (p[1] - last[1]) * layer.clientHeight) < 150) return;   // < 1.5px
                        s.pts.push(p);
                        draw(s);
                    } else {
                        draw(s, p);
                    }
                };
                const up = ev => {
                    layer.removeEventListener('pointermove', move);
                    layer.removeEventListener('pointerup', up);
                    const p = at(layer, ev);
                    if (mode === 'ink') {
                        if (s.pts.length === 1) s.pts.push([p[0] + .1, p[1]]);   // a dot
                        finish(s, s.pts);
                    } else if (Math.hypot((p[0] - s.pts[0][0]) * layer.clientWidth, (p[1] - s.pts[0][1]) * layer.clientHeight) < 400) {
                        cancel(s);   // a click, not a drag
                    } else {
                        finish(s, [s.pts[0], p]);
                    }
                };
                layer.addEventListener('pointermove', move);
                layer.addEventListener('pointerup', up);
            });
            document.addEventListener('pointermove', e => {
                if (!poly) return;
                if (!poly.layer.isConnected) { poly = null; return; }
                draw(poly, at(poly.layer, e));
            });
            // A double-click (finishing an area) mustn't select text on the page or elsewhere.
            document.addEventListener('mousedown', e => { if (e.target.closest?.('.pe-tool-layer[data-sketch]')) e.preventDefault(); });
            document.addEventListener('dblclick', e => { if (poly && e.target.closest?.('.pe-tool-layer[data-sketch]')) finishPoly(); });
            document.addEventListener('keydown', e => {
                if (!poly) return;
                if (e.key === 'Enter') { e.preventDefault(); finishPoly(); }
                else if (e.key === 'Escape') { cancel(poly); poly = null; }
            });
        },

        // Theme: the saved choice and the device's dark / high-contrast settings (and their changes).
        theme: {
            init(dotnet) {
                const dark = matchMedia('(prefers-color-scheme: dark)');
                const contrast = matchMedia('(forced-colors: active), (prefers-contrast: more)');
                const tell = () => dotnet.invokeMethodAsync('OnSystemTheme', dark.matches, contrast.matches);
                dark.addEventListener('change', tell);
                contrast.addEventListener('change', tell);
                let saved = 'System';
                try { saved = localStorage.getItem('pdfedit-theme') || 'System'; } catch { }
                return { saved, dark: dark.matches, contrast: contrast.matches };
            },
            save(id) { try { localStorage.setItem('pdfedit-theme', id); } catch { } },
        },

        // A random ID for this browser, so it gets back the signatures it saved (the site has no accounts).
        clientId() {
            try {
                let id = localStorage.getItem('pdfedit-client');
                if (!id || !/^[0-9a-f]{32}$/.test(id)) {
                    id = crypto.randomUUID().replace(/-/g, '');
                    localStorage.setItem('pdfedit-client', id);
                }
                return id;
            } catch { return ''; }
        },

        timeZone() { return Intl.DateTimeFormat().resolvedOptions().timeZone; },

        // Keyboard shortcuts (Ctrl+O, Ctrl+S, Ctrl+Z, Ctrl+Y, Ctrl+F, Ctrl+P, +, -, Esc, Delete).
        listenKeys(dotnet) {
            window.__peDotnet = dotnet;
            document.addEventListener('keydown', e => {
                const inField = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName);
                const k = e.key.toLowerCase();
                const selecting = !!window.getSelection()?.toString();
                if (e.ctrlKey || e.metaKey) {
                    if (['o', 's', 'z', 'y', 'f', 'p', 'g', '=', '+', '-', '_', '0', '[', ']'].includes(k) && !(inField && ['z', 'y'].includes(k))) {
                        e.preventDefault();
                        dotnet.invokeMethodAsync('OnShortcut', (e.shiftKey ? 'Shift+' : '') + 'Ctrl+' + k);
                    } else if (['c', 'x'].includes(k) && !inField && !selecting) {
                        // Copy / cut what's selected on the page (text selected in the page keeps the browser's copy).
                        dotnet.invokeMethodAsync('OnShortcut', 'Ctrl+' + k);
                    } else if ((k === 'arrowleft' || k === 'arrowright') && !inField) {
                        e.preventDefault();
                        dotnet.invokeMethodAsync('OnShortcut', 'Ctrl+' + (k === 'arrowleft' ? 'ArrowLeft' : 'ArrowRight'));
                    }
                } else if (e.altKey && (k === 'pagedown' || k === 'pageup')) {
                    // Next / previous open document (the browser keeps Ctrl+Tab for its own tabs).
                    e.preventDefault();
                    dotnet.invokeMethodAsync('OnShortcut', k === 'pagedown' ? 'Alt+PageDown' : 'Alt+PageUp');
                } else if (k === 'f1') {
                    e.preventDefault();
                    dotnet.invokeMethodAsync('OnShortcut', 'F1');
                } else if (k === 'escape') {
                    dotnet.invokeMethodAsync('OnShortcut', 'Escape');
                } else if (k.startsWith('arrow') && !inField && document.querySelector('.pe-item-wrap.sel')
                           && !document.querySelector('.pe-modal, .pe-backstage')) {
                    // Nudge the selected item (Shift: ten points).
                    e.preventDefault();
                    const step = e.shiftKey ? 10 : 1;
                    dotnet.invokeMethodAsync('OnNudge', k === 'arrowleft' ? -step : k === 'arrowright' ? step : 0,
                        k === 'arrowup' ? -step : k === 'arrowdown' ? step : 0);
                } else if (k === 'delete' && !inField) {
                    dotnet.invokeMethodAsync('OnDeleteKey');
                } else if (!inField && !e.altKey && /^[a-z]$/.test(k) && !document.querySelector('.pe-modal, .pe-backstage')
                           && !document.activeElement?.isContentEditable) {
                    // The toolbox's single-letter shortcuts (V select, T text, S sign, H hand, Z zoom …).
                    dotnet.invokeMethodAsync('OnShortcut', 'Key:' + k);
                }
            });
            // Ctrl+V: a picture from another app is pasted onto the page; otherwise what was copied in PdfEdit.
            document.addEventListener('paste', e => {
                if (/^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName) || document.activeElement?.isContentEditable) return;
                if (document.querySelector('.pe-modal, .pe-backstage')) return;
                const file = [...(e.clipboardData?.items || [])].find(i => i.kind === 'file' && i.type.startsWith('image/'))?.getAsFile();
                e.preventDefault();
                if (!file) { dotnet.invokeMethodAsync('OnShortcut', 'Ctrl+v'); return; }
                const reader = new FileReader();
                reader.onload = () => dotnet.invokeMethodAsync('OnPastePicture', reader.result.split(',')[1]);
                reader.readAsDataURL(file);
            });
        },

        // ── Clipboard ──
        async copyText(text) { try { await navigator.clipboard.writeText(text); } catch { } },
        async copyImage(base64) {
            try {
                const blob = await (await fetch('data:image/png;base64,' + base64)).blob();
                await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
                return true;
            } catch { return false; }
        },
        // The Paste button: a picture (as PNG base64) or text from the computer's clipboard.
        async readClipboard() {
            try {
                for (const item of await navigator.clipboard.read()) {
                    const type = item.types.find(t => t.startsWith('image/'));
                    if (type) {
                        const blob = await item.getType(type);
                        const url = await new Promise(r => { const f = new FileReader(); f.onload = () => r(f.result); f.readAsDataURL(blob); });
                        return { image: url.split(',')[1], text: null };
                    }
                }
            } catch { }
            try { return { image: null, text: await navigator.clipboard.readText() }; } catch { return null; }
        },

        // ── Reading: auto scroll, full screen ──
        autoScroll(viewer, speed) {
            clearInterval(window.__peAutoScroll);
            viewer?.removeEventListener('wheel', window.__peAutoStop);
            if (!viewer || !speed) return;
            let last = performance.now();
            window.__peAutoStop = () => { clearInterval(window.__peAutoScroll); window.__peDotnet?.invokeMethodAsync('OnAutoScrollStopped'); };
            viewer.addEventListener('wheel', window.__peAutoStop, { once: true });
            window.__peAutoScroll = setInterval(() => {
                const now = performance.now();
                viewer.scrollTop += speed * (now - last) / 1000;
                last = now;
                if (viewer.scrollTop + viewer.clientHeight >= viewer.scrollHeight - 1) window.__peAutoStop();
            }, 30);
        },
        fullscreen(id) {
            const el = document.getElementById(id);
            el?.focus();
            el?.requestFullscreen?.().catch(() => { });
        },
        exitFullscreen() { if (document.fullscreenElement) document.exitFullscreen().catch(() => { }); },

        // ── A picture downloaded as JPEG (made from a PNG in the browser) ──
        downloadAsJpeg(base64, name) {
            const img = new Image();
            img.onload = () => {
                const c = document.createElement('canvas');
                c.width = img.naturalWidth; c.height = img.naturalHeight;
                const g = c.getContext('2d');
                g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height);
                g.drawImage(img, 0, 0);
                c.toBlob(b => {
                    const a = document.createElement('a');
                    a.href = URL.createObjectURL(b); a.download = name;
                    document.body.appendChild(a); a.click(); a.remove();
                    setTimeout(() => URL.revokeObjectURL(a.href), 4000);
                }, 'image/jpeg', 0.92);
            };
            img.src = 'data:image/png;base64,' + base64;
        },

        // ── Ask by voice (the browser's speech recognition: Chrome, Edge, Safari) ──
        voice: {
            listen(dotnet) {
                const R = window.SpeechRecognition || window.webkitSpeechRecognition;
                if (!R) return false;
                const r = new R();
                r.lang = navigator.language || 'en-GB';
                r.interimResults = false;
                r.maxAlternatives = 1;
                let done = false;
                const finish = (text, err) => { if (done) return; done = true; dotnet.invokeMethodAsync('OnVoiceResult', text, err); };
                r.onresult = e => finish(e.results[0][0].transcript, null);
                r.onerror = e => finish(null, e.error);
                r.onend = () => finish(null, null);
                window.__peVoice = r;
                r.start();
                return true;
            },
            stop() { window.__peVoice?.stop(); },
        },

        // ── Share: the device's share sheet (phones, tablets, Windows, macOS) with the PDF attached ──
        // Returns 'shared', 'cancelled', 'unsupported' or 'failed'. If too long has passed since the
        // click for the browser to allow it, a small "Share" prompt is shown to click again.
        async share(url, name) {
            if (!navigator.share || !navigator.canShare) return 'unsupported';
            let file;
            try {
                const r = await fetch(url);
                if (!r.ok) return 'failed';
                file = new File([await r.blob()], name, { type: 'application/pdf' });
            } catch { return 'failed'; }
            if (!navigator.canShare({ files: [file] })) return 'unsupported';
            const go = () => navigator.share({ files: [file], title: name });
            try { await go(); return 'shared'; }
            catch (e) {
                if (e?.name === 'AbortError') return 'cancelled';
                if (e?.name !== 'NotAllowedError') return 'failed';
            }
            // The click is too long ago (a big file, a slow connection): ask for one more.
            return await new Promise(done => {
                document.querySelector('.pe-share-prompt')?.remove();
                const box = document.createElement('div');
                box.className = 'pe-share-prompt';
                box.setAttribute('role', 'dialog');
                box.innerHTML = '<span></span><button class="pe-btn primary" type="button">Share</button><button class="pe-btn" type="button">Cancel</button>';
                box.querySelector('span').textContent = `${name} is ready to share`;
                const [ok, cancel] = box.querySelectorAll('button');
                const end = result => { box.remove(); done(result); };
                ok.onclick = async () => {
                    try { await go(); end('shared'); }
                    catch (e) { end(e?.name === 'AbortError' ? 'cancelled' : 'failed'); }
                };
                cancel.onclick = () => end('cancelled');
                document.body.appendChild(box);
                ok.focus();
            });
        },

        // ── Dictation: speech typed into the field, note or text box last clicked ──
        // Keeps listening (restarting after pauses) until stopped. Spoken punctuation is understood.
        dictation: (() => {
            let rec = null, on = false, target = null, net = null;
            const editable = el => el && (el.tagName === 'TEXTAREA' || el.isContentEditable ||
                (el.tagName === 'INPUT' && /^(text|search|email|url|tel|number|)$/i.test(el.type)));
            document.addEventListener('focusin', e => { if (editable(e.target)) target = e.target; }, true);
            const words = [
                [/\s*\b(new paragraph)\b\s*/gi, '\n\n'], [/\s*\b(new line|next line)\b\s*/gi, '\n'],
                [/\s*\b(full stop|period)\b/gi, '.'], [/\s*\bcomma\b/gi, ','], [/\s*\bquestion mark\b/gi, '?'],
                [/\s*\bexclamation (mark|point)\b/gi, '!'], [/\s*\bcolon\b/gi, ':'], [/\s*\bsemicolon\b/gi, ';'],
                [/\bopen bracket\s*/gi, '('], [/\s*\bclose bracket\b/gi, ')'], [/\s*\bhyphen\s*/gi, '-'],
            ];
            const tidy = text => words.reduce((t, [re, to]) => t.replace(re, to), text).trim();
            function insert(text) {
                const el = target;
                if (!el || !el.isConnected) { net?.invokeMethodAsync('OnDictation', 'nowhere', text); return; }
                if (el.tagName === 'INPUT' && el.type === 'number') text = text.replace(/[^\d.,-]/g, '');
                if (el.isContentEditable) {
                    el.focus();
                    document.execCommand('insertText', false, (el.textContent && !/\s$/.test(el.textContent) ? ' ' : '') + text);
                } else {
                    const start = el.selectionStart ?? el.value.length, end = el.selectionEnd ?? el.value.length;
                    const before = el.value.slice(0, start);
                    const glue = before && !/[\s(\n]$/.test(before) && !/^[.,?!:;)]/.test(text) ? ' ' : '';
                    if (el.tagName === 'INPUT') text = text.replace(/\n+/g, ' ');
                    try { el.setRangeText(glue + text, start, end, 'end'); }
                    catch { el.value = before + glue + text + el.value.slice(end); }
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                    el.focus();
                }
                net?.invokeMethodAsync('OnDictation', 'typed', text);
            }
            function start() {
                const R = window.SpeechRecognition || window.webkitSpeechRecognition;
                rec = new R();
                rec.lang = navigator.language || 'en-GB';
                rec.continuous = true;
                rec.interimResults = false;
                rec.onresult = e => {
                    for (let i = e.resultIndex; i < e.results.length; i++)
                        if (e.results[i].isFinal) { const t = tidy(e.results[i][0].transcript); if (t) insert(t); }
                };
                rec.onerror = e => {
                    if (e.error === 'no-speech' || e.error === 'aborted') return;
                    on = false;
                    net?.invokeMethodAsync('OnDictation', 'error', e.error);
                };
                // Browsers stop listening after a pause: carry on until Dictate is turned off.
                rec.onend = () => { if (on) { try { rec.start(); } catch { on = false; net?.invokeMethodAsync('OnDictation', 'stopped', ''); } } };
                rec.start();
            }
            return {
                supported: () => !!(window.SpeechRecognition || window.webkitSpeechRecognition),
                start(dotnet) {
                    if (!(window.SpeechRecognition || window.webkitSpeechRecognition)) return false;
                    net = dotnet; on = true;
                    try { start(); } catch { on = false; return false; }
                    target?.focus();
                    return true;
                },
                stop() { on = false; try { rec?.stop(); } catch { } },
            };
        })(),

        // ── Where the user is (for dynamic stamps): the browser asks permission the first time ──
        // Returns { lat, lon, accuracy } or { error }.
        location() {
            return new Promise(done => {
                if (!navigator.geolocation) { done({ error: 'unsupported' }); return; }
                navigator.geolocation.getCurrentPosition(
                    p => done({ lat: p.coords.latitude, lon: p.coords.longitude, accuracy: p.coords.accuracy }),
                    e => done({ error: e.code === 1 ? 'denied' : e.code === 3 ? 'timeout' : 'unavailable' }),
                    { enableHighAccuracy: false, timeout: 8000, maximumAge: 10 * 60 * 1000 });
            });
        },

        // Mind map topics that have a page: clicking one goes there.
        mindMapLinks(dotnet) {
            document.querySelector('.pe-mindmap')?.addEventListener('click', e => {
                const a = e.target.closest('[data-page]');
                if (!a) return;
                e.preventDefault();
                dotnet.invokeMethodAsync('MindMapPage', +a.dataset.page);
            });
        },

        // ── Take the Tour: the ring round the part being described, and the card beside it ──
        placeTour() {
            const tour = document.querySelector('.pe-tour');
            if (!tour) return;
            const ring = tour.querySelector('.pe-tour-ring'), card = tour.querySelector('.pe-tour-card');
            const target = tour.dataset.target ? document.querySelector(tour.dataset.target) : null;
            const W = window.innerWidth, H = window.innerHeight, cw = card.offsetWidth, ch = card.offsetHeight, gap = 14;
            if (!target) {
                ring.style.display = 'none';
                card.style.left = (W - cw) / 2 + 'px'; card.style.top = (H - ch) / 2 + 'px';
                return;
            }
            const t = target.getBoundingClientRect();
            ring.style.display = '';
            Object.assign(ring.style, { left: t.left - 4 + 'px', top: t.top - 4 + 'px', width: t.width + 8 + 'px', height: t.height + 8 + 'px' });
            let x, y;
            if (t.bottom + gap + ch <= H) { x = t.left + 24; y = t.bottom + gap; }
            else if (t.top - gap - ch >= 0) { x = t.left + 24; y = t.top - gap - ch; }
            else if (t.right + gap + cw <= W) { x = t.right + gap; y = t.top + 24; }
            else if (t.left - gap - cw >= 0) { x = t.left - gap - cw; y = t.top + 24; }
            else { x = t.left + (t.width - cw) / 2; y = t.top + (t.height - ch) / 2; }
            card.style.left = Math.max(8, Math.min(x, W - cw - 8)) + 'px';
            card.style.top = Math.max(8, Math.min(y, H - ch - 8)) + 'px';
        },

        // ── Read Aloud: the browser's speech synthesis ──────────────────────
        speech: {
            voices() {
                return new Promise(resolve => {
                    const list = () => speechSynthesis.getVoices().map(v => `${v.name} (${v.lang})`);
                    const now = list();
                    if (now.length) { resolve(now); return; }
                    speechSynthesis.onvoiceschanged = () => resolve(list());
                    setTimeout(() => resolve(list()), 1500);
                });
            },
            // items: [{ page, text }]; tells .NET which page is being read, and when it's done.
            speak(dotnet, items, voiceName, rate) {
                speechSynthesis.cancel();
                const voice = speechSynthesis.getVoices().find(v => `${v.name} (${v.lang})` === voiceName);
                items.forEach((item, i) => {
                    const u = new SpeechSynthesisUtterance(item.text);
                    if (voice) u.voice = voice;
                    u.rate = rate || 1;
                    u.onstart = () => dotnet.invokeMethodAsync('OnReadingPage', item.page);
                    if (i === items.length - 1) { u.onend = () => dotnet.invokeMethodAsync('OnReadingDone'); u.onerror = u.onend; }
                    speechSynthesis.speak(u);
                });
            },
            pause() { speechSynthesis.pause(); },
            resume() { speechSynthesis.resume(); },
            stop() { speechSynthesis.cancel(); },
        },

        // ── Signature pad ────────────────────────────────────────────────────
        sigPad: {
            init(canvas) {
                const ratio = window.devicePixelRatio || 1;
                canvas.width = canvas.clientWidth * ratio;
                canvas.height = canvas.clientHeight * ratio;
                const ctx = canvas.getContext('2d');
                ctx.scale(ratio, ratio);
                ctx.lineWidth = 2.4;
                ctx.lineCap = 'round';
                ctx.lineJoin = 'round';
                ctx.strokeStyle = canvas.dataset.color || '#0b2a6f';
                canvas._empty = true;
                let drawing = false;
                const pos = e => { const r = canvas.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
                canvas.onpointerdown = e => { drawing = true; canvas.setPointerCapture(e.pointerId); ctx.beginPath(); ctx.moveTo(...pos(e)); };
                canvas.onpointermove = e => { if (!drawing) return; ctx.lineTo(...pos(e)); ctx.stroke(); canvas._empty = false; };
                canvas.onpointerup = () => { drawing = false; };
            },
            setColor(canvas, color) { canvas.getContext('2d').strokeStyle = color; canvas.dataset.color = color; },
            clear(canvas) {
                const ctx = canvas.getContext('2d');
                ctx.save(); ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.clearRect(0, 0, canvas.width, canvas.height); ctx.restore();
                canvas._empty = true;
            },
            // The drawing cropped to its ink, as a PNG data URL ('' when nothing was drawn).
            toPng(canvas) {
                if (canvas._empty) return '';
                const ctx = canvas.getContext('2d');
                const { width, height } = canvas;
                const data = ctx.getImageData(0, 0, width, height).data;
                let minX = width, minY = height, maxX = -1, maxY = -1;
                for (let y = 0; y < height; y++)
                    for (let x = 0; x < width; x++)
                        if (data[(y * width + x) * 4 + 3] > 0) {
                            if (x < minX) minX = x; if (x > maxX) maxX = x;
                            if (y < minY) minY = y; if (y > maxY) maxY = y;
                        }
                if (maxX < 0) return '';
                const pad = 6;
                minX = Math.max(0, minX - pad); minY = Math.max(0, minY - pad);
                maxX = Math.min(width - 1, maxX + pad); maxY = Math.min(height - 1, maxY + pad);
                const out = document.createElement('canvas');
                out.width = maxX - minX + 1; out.height = maxY - minY + 1;
                out.getContext('2d').drawImage(canvas, minX, minY, out.width, out.height, 0, 0, out.width, out.height);
                return out.toDataURL('image/png');
            },
            // Typed signature: draws text in a script-like font and returns it as a PNG data URL.
            typed(text, font, color) {
                const c = document.createElement('canvas');
                const ctx = c.getContext('2d');
                const f = `64px ${font}`;
                ctx.font = f;
                const w = Math.ceil(ctx.measureText(text).width) + 24;
                c.width = Math.max(40, w); c.height = 96;
                ctx.font = f; ctx.fillStyle = color; ctx.textBaseline = 'middle';
                ctx.fillText(text, 12, 50);
                return c.toDataURL('image/png');
            },
        },
    };
})();

// ── Browser storage ──────────────────────────────────────────────────────────
// One IndexedDB database for the recent files and the drafts kept against a reload.
function pdfeditDb(name, mode, work) {
    const open = () => new Promise((ok, fail) => {
        const req = indexedDB.open('pdfedit', 2);
        req.onupgradeneeded = () => {
            for (const store of ['recent', 'drafts'])
                if (!req.result.objectStoreNames.contains(store)) req.result.createObjectStore(store, { keyPath: 'id' });
        };
        req.onsuccess = () => ok(req.result);
        req.onerror = () => fail(req.error);
    });
    return open().then(d => new Promise((ok, fail) => {
        const t = d.transaction(name, mode), store = t.objectStore(name);
        const result = work(store);
        t.oncomplete = () => { d.close(); ok(result?.result ?? result); };
        t.onerror = () => { d.close(); fail(t.error); };
    }));
}

// ── Recent files ─────────────────────────────────────────────────────────────
// Files opened from this computer are kept in this browser (IndexedDB) so File → Open can list them
// and open them again, like the Windows app's recent files. Nothing leaves the browser for this.
window.pdfeditRecent = (() => {
    const MAX_FILES = 10, MAX_SIZE = 50 * 1024 * 1024;
    const tx = (mode, work) => pdfeditDb('recent', mode, work);
    const all = async () => (await tx('readonly', s => s.getAll())) || [];
    const keyOf = f => `${f.name}|${f.size}`;
    // "today 14:05", "yesterday", "Monday" or "3 Oct 2026", in the viewer's own time.
    const when = ms => {
        const d = new Date(ms), today = new Date(); today.setHours(0, 0, 0, 0);
        const days = Math.round((today - new Date(d).setHours(0, 0, 0, 0)) / 864e5);
        if (days <= 0) return 'today ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        if (days === 1) return 'yesterday';
        if (days < 7) return d.toLocaleDateString([], { weekday: 'long' });
        return d.toLocaleDateString([], { day: 'numeric', month: 'short', year: 'numeric' });
    };

    async function add(file) {
        if (!file || file.size > MAX_SIZE) return;
        try {
            const items = await all();
            const id = keyOf(file);
            await tx('readwrite', s => {
                s.put({ id, name: file.name, size: file.size, type: file.type, opened: Date.now(), blob: file });
                items.filter(i => i.id !== id).sort((a, b) => b.opened - a.opened).slice(MAX_FILES - 1).forEach(i => s.delete(i.id));
            });
        } catch { /* private window or storage blocked: no recent list */ }
    }

    // Opening a recent file again only moves it to the top: storing it again would replace the very
    // record the file is being read from, and the browser then aborts the read.
    async function touch(id) {
        try {
            const item = (await all()).find(i => i.id === id);
            if (item) await tx('readwrite', s => s.put({ ...item, opened: Date.now() }));
        } catch { }
    }

    // Anything chosen or dropped on an Open area (.pe-drop) is remembered.
    document.addEventListener('change', e => {
        const input = e.target;
        if (input?.type !== 'file' || !input.files?.length) return;
        if (input.id === 'pe-recent-input') touch(keyOf(input.files[0]));
        else if (input.closest('.pe-drop')) add(input.files[0]);
    }, true);

    return {
        async list() {
            try {
                return (await all()).sort((a, b) => b.opened - a.opened)
                    .map(i => ({ id: i.id, name: i.name, size: i.size, when: when(i.opened) }));
            } catch { return []; }
        },
        async remove(id) { try { await tx('readwrite', s => s.delete(id)); } catch { } },
        async clear() { try { await tx('readwrite', s => s.clear()); } catch { } },
        // Hands the stored file to the page's hidden file input, as if it had just been chosen.
        async open(id, inputId) {
            try {
                const item = (await all()).find(i => i.id === id);
                const input = document.getElementById(inputId);
                if (!item || !input) return false;
                // A copy in memory, so the file doesn't depend on the stored record while it's read.
                const data = await item.blob.arrayBuffer();
                const dt = new DataTransfer();
                dt.items.add(new File([data], item.name, { type: item.type || 'application/pdf' }));
                input.files = dt.files;
                input.dispatchEvent(new Event('change', { bubbles: true }));
                return true;
            } catch { return false; }
        },
    };
})();

// ── Drafts ───────────────────────────────────────────────────────────────────
// Open documents (with the things placed on them but not applied yet) and the Design canvas are
// kept in this browser as drafts, so reloading the page, a dropped connection or closing the
// window doesn't lose them. Each window has its own id (sessionStorage survives a reload, not a
// new window); a reload brings back that window's drafts, and the start page lists the others.
window.pdfeditDrafts = (() => {
    const MAX_PDF = 50 * 1024 * 1024, MAX_AGE = 14 * 864e5;
    const tx = (mode, work) => pdfeditDb('drafts', mode, work);
    const get = id => tx('readonly', s => s.get(id));
    let unsaved = false;
    window.addEventListener('beforeunload', e => { if (unsaved) { e.preventDefault(); e.returnValue = ''; } });
    const when = ms => {
        const d = new Date(ms), today = new Date(); today.setHours(0, 0, 0, 0);
        const days = Math.round((today - new Date(d).setHours(0, 0, 0, 0)) / 864e5);
        const time = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        if (days <= 0) return 'today ' + time;
        if (days === 1) return 'yesterday ' + time;
        return d.toLocaleDateString([], { day: 'numeric', month: 'short' }) + ' ' + time;
    };
    const bytes = text => new TextEncoder().encode(text);

    return {
        windowId() {
            try {
                let id = sessionStorage.getItem('pdfedit-window');
                if (!id) { id = crypto.randomUUID?.() ?? String(Math.random()).slice(2); sessionStorage.setItem('pdfedit-window', id); }
                return id;
            } catch { return 'window'; }
        },
        // Everything kept, newest first, without the state or the PDF (those are read one at a time).
        async list() {
            try {
                const all = (await tx('readonly', s => s.getAll())) || [];
                const old = all.filter(d => Date.now() - d.saved > MAX_AGE);
                if (old.length) await tx('readwrite', s => old.forEach(d => s.delete(d.id)));
                return all.filter(d => !old.includes(d)).sort((a, b) => b.saved - a.saved).map(d => ({
                    id: d.id, window: d.window, kind: d.kind, name: d.name, sessionId: d.sessionId, order: d.order,
                    active: d.active, changes: d.changes, hasPdf: !!d.pdf, when: when(d.saved),
                }));
            } catch { return []; }
        },
        // Keeps a draft. The PDF itself is fetched from the server again only when it has changed
        // (pdfKey is the server copy and its version), so a draft can come back even after the
        // server has let the document go.
        async put(draft, state, pdfKey) {
            try {
                const old = await get(draft.id);
                let pdf = old?.pdf ?? null, key = old?.pdfKey ?? null;
                if (draft.kind === 'doc' && pdfKey !== key) {
                    pdf = null; key = null;
                    const r = await fetch(`documents/${encodeURIComponent(draft.sessionId)}/file`);
                    if (r.ok) {
                        const blob = await r.blob();
                        if (blob.size <= MAX_PDF) { pdf = blob; key = pdfKey; }
                    }
                }
                await tx('readwrite', s => s.put({ ...draft, state, pdf, pdfKey: key, saved: Date.now() }));
                return true;
            } catch { return false; }
        },
        // The state and the PDF go back as streams: they can be bigger than one message.
        async state(id) { const d = await get(id); return bytes(d?.state ?? ''); },
        async pdf(id) { const d = await get(id); return d?.pdf ? new Uint8Array(await d.pdf.arrayBuffer()) : new Uint8Array(0); },
        async claim(id, window) {
            try { const d = await get(id); if (d) await tx('readwrite', s => s.put({ ...d, window })); } catch { }
        },
        async remove(id) { try { await tx('readwrite', s => s.delete(id)); } catch { } },
        // Asks before the page is left while there's work not kept yet.
        setUnsaved(on) { unsaved = !!on; },
    };
})();
