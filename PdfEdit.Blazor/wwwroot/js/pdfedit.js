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

        viewerWidth(viewer) { return viewer ? viewer.clientWidth : 1000; },

        download(url) {
            const a = document.createElement('a');
            a.href = url;
            a.download = '';
            document.body.appendChild(a);
            a.click();
            a.remove();
        },

        openInNewTab(url) { window.open(url, '_blank', 'noopener'); },

        // Drag to move, or drag the corner handle to resize, anything marked data-drag (Prepare Form
        // field boxes, things placed on a page). Moves are shown live and reported to .NET at the end
        // as percentages of the page.
        listenDrag(dotnet) {
            document.addEventListener('pointerdown', e => {
                const box = e.target.closest?.('[data-drag]');
                if (!box || e.button !== 0) return;
                const page = box.closest('.pe-page');
                if (!page || /^(INPUT|TEXTAREA|SELECT|BUTTON)$/.test(e.target.tagName)) return;
                e.preventDefault();   // no text selection or native image drag (which cancels the pointer)
                const resize = !!e.target.closest('[data-resize]');
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
                    if (resize) { w = Math.max(6, start.w + dx); h = Math.max(6, start.h + dy); }
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
            document.addEventListener('dblclick', e => { if (poly && e.target.closest?.('.pe-tool-layer[data-sketch]')) finishPoly(); });
            document.addEventListener('keydown', e => {
                if (!poly) return;
                if (e.key === 'Enter') { e.preventDefault(); finishPoly(); }
                else if (e.key === 'Escape') { cancel(poly); poly = null; }
            });
        },

        timeZone() { return Intl.DateTimeFormat().resolvedOptions().timeZone; },

        // Keyboard shortcuts (Ctrl+O, Ctrl+S, Ctrl+Z, Ctrl+Y, Ctrl+F, Ctrl+P, +, -, Esc, Delete).
        listenKeys(dotnet) {
            document.addEventListener('keydown', e => {
                const inField = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName);
                const k = e.key.toLowerCase();
                if (e.ctrlKey || e.metaKey) {
                    if (['o', 's', 'z', 'y', 'f', 'p', '=', '+', '-', '0'].includes(k) && !(inField && ['z', 'y'].includes(k))) {
                        e.preventDefault();
                        dotnet.invokeMethodAsync('OnShortcut', (e.shiftKey ? 'Shift+' : '') + 'Ctrl+' + k);
                    }
                } else if (k === 'escape') {
                    dotnet.invokeMethodAsync('OnShortcut', 'Escape');
                } else if (k === 'delete' && !inField) {
                    dotnet.invokeMethodAsync('OnDeleteKey');
                }
            });
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
