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
            });
        },

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
