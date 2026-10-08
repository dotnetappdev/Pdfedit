// "Ask the guide": a chat that answers questions about PdfEdit from the user guide and tutorials.
// Each question picks the few best-matching passages from data/chunks.json (made by the build) and an
// LLM answers from those alone, citing them. The LLM can be:
//   • the site's own assistant — data/site.json "chatEndpoint", a small proxy (worker/ask-worker.js)
//     that holds the key; visitors need nothing;
//   • the visitor's own Claude or OpenAI key, sent straight from their browser to that provider and
//     kept only in this browser;
//   • a local model (Ollama, LM Studio…) on their own computer;
//   • none: the matching passages are shown instead.
(() => {
  'use strict';
  const ROOT = document.querySelector('meta[name="site-root"]')?.content ?? '';
  const KEY = 'pdfedit-ask';
  const esc = s => String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  const PROVIDERS = {
    site: { name: 'PdfEdit assistant', needs: [] },
    claude: { name: 'Claude (your key)', needs: ['key', 'model'], models: ['claude-sonnet-5-5', 'claude-haiku-5-5', 'claude-opus-5-5'], keyHint: 'sk-ant-…  from console.anthropic.com' },
    openai: { name: 'OpenAI (your key)', needs: ['key', 'model'], models: ['gpt-4o-mini', 'gpt-4.1-mini', 'gpt-4o'], keyHint: 'sk-…  from platform.openai.com' },
    local: { name: 'Local model (Ollama, LM Studio…)', needs: ['endpoint', 'model'], models: [], endpoint: 'http://localhost:11434/v1', model: 'llama3.2' },
    none: { name: 'No AI — show the matching sections', needs: [] },
  };

  // ── Settings (this browser only) ───────────────────────────────────────────
  let site = {};
  const load = () => { try { return JSON.parse(localStorage.getItem(KEY)) ?? {}; } catch { return {}; } };
  const save = s => { try { localStorage.setItem(KEY, JSON.stringify(s)); } catch { /* private window */ } };
  let prefs = load();
  const provider = () => {
    const p = prefs.provider;
    if (p && PROVIDERS[p] && (p !== 'site' || site.chatEndpoint)) return p;
    return site.chatEndpoint ? 'site' : 'none';
  };
  const configured = p => p === 'claude' || p === 'openai' ? !!prefs[p + 'Key'] : true;

  // ── Finding the passages a question needs ──────────────────────────────────
  const STOP = new Set('a an and are as at be but by can do does for from how i if in into is it its me my of on or so that the then there these this to was what when where which who why will with you your pdfedit please want need get'.split(' '));
  const SAME = { signature: 'sign', signing: 'sign', signed: 'sign', fillable: 'fill', filling: 'fill', filled: 'fill', merging: 'merge', combine: 'merge', join: 'merge', scanner: 'scan', scanning: 'scan', scanned: 'scan', ocr: 'ocr', searchable: 'ocr', password: 'protect', encrypt: 'protect', redaction: 'redact', redacting: 'redact', blackout: 'redact', comments: 'comment', annotations: 'comment', annotate: 'comment', pages: 'page', forms: 'form', fields: 'field', mac: 'mac', macos: 'mac', install: 'install', installing: 'install', setup: 'install', download: 'install', word: 'word', docx: 'word', excel: 'excel', xlsx: 'excel', watermarks: 'watermark', stamps: 'stamp', shortcuts: 'shortcut', keys: 'shortcut', themes: 'theme', dark: 'theme', translate: 'translate', translation: 'translate' };
  const terms = text => (text.toLowerCase().match(/[a-z0-9]+/g) ?? [])
    .filter(w => w.length > 1 && !STOP.has(w))
    .map(w => SAME[w] ?? (w.replace(/(ing|ed|es|s)$/, '') || w));

  let index = null;
  const loadIndex = () => index ??= fetch(ROOT + 'data/chunks.json').then(r => r.json()).then(chunks => {
    const docs = chunks.map(c => ({ c, body: terms(c.x), head: new Set(terms(`${c.t} ${c.h}`)) }));
    const df = new Map();
    docs.forEach(d => new Set([...d.body, ...d.head]).forEach(w => df.set(w, (df.get(w) ?? 0) + 1)));
    const avg = docs.reduce((n, d) => n + d.body.length, 0) / docs.length;
    docs.forEach(d => { d.tf = new Map(); d.body.forEach(w => d.tf.set(w, (d.tf.get(w) ?? 0) + 1)); });
    return { docs, df, avg, n: docs.length };
  });

  /** The best passages for a question (BM25, with a boost when the words are in the heading). */
  async function retrieve(question, limit = 6) {
    const { docs, df, avg, n } = await loadIndex();
    const q = [...new Set(terms(question))];
    if (!q.length) return [];
    const scored = docs.map(d => {
      let score = 0;
      for (const w of q) {
        const idf = Math.log(1 + (n - (df.get(w) ?? 0) + .5) / ((df.get(w) ?? 0) + .5));
        const tf = d.tf.get(w) ?? 0;
        score += idf * (tf * 2.2) / (tf + 1.2 * (.25 + .75 * d.body.length / avg));
        if (d.head.has(w)) score += idf * 1.6;
      }
      return { c: d.c, score };
    }).filter(s => s.score > 0).sort((a, b) => b.score - a.score);
    // At most two passages from one page, so the answer can draw on more than one place.
    const perPage = new Map(), out = [];
    for (const s of scored) {
      const page = s.c.u.split('#')[0];
      if ((perPage.get(page) ?? 0) >= 2) continue;
      perPage.set(page, (perPage.get(page) ?? 0) + 1);
      out.push(s.c);
      if (out.length >= limit) break;
    }
    return out;
  }

  const SYSTEM = `You are the help assistant on the PdfEdit website. PdfEdit is a free, open-source PDF editor in three versions: PdfEdit for Windows (the full Windows app), PdfEdit for Mac (the web interface in a Mac app) and PdfEdit for the web (self-hosted, used in a browser).
Answer the user's question using ONLY the numbered excerpts from the PdfEdit user guide and tutorials that are provided. Rules:
- Be brief and practical. For "how do I" questions, give numbered steps with the exact button and tab names in bold.
- Cite the excerpts you used with their numbers in square brackets, like [1] or [2][4], right after the sentence they support.
- If the Windows app and the Mac/web versions differ, say how for each.
- If the excerpts don't cover the question, say you couldn't find it in the guide and suggest searching the guide or reporting it on GitHub. Never invent buttons, menus or features.
- Only help with PdfEdit and PDFs. Politely decline anything unrelated.`;

  const contextBlock = passages => passages.map((p, i) => `[${i + 1}] ${p.t}${p.h ? ' › ' + p.h : ''} (${p.s})\n${p.x}`).join('\n\n');

  // ── Talking to the LLM ─────────────────────────────────────────────────────
  /** Reads a server-sent-events stream and hands each "data:" payload to onData. */
  async function readSse(response, onData) {
    const reader = response.body.getReader(), decoder = new TextDecoder();
    let buffer = '';
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      let nl;
      while ((nl = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, nl).trim();
        buffer = buffer.slice(nl + 1);
        if (line.startsWith('data:')) onData(line.slice(5).trim());
      }
    }
  }

  async function failure(response, p) {
    let detail = '';
    try { const j = await response.json(); detail = j.error?.message ?? j.error ?? j.message ?? ''; } catch { /* not JSON */ }
    if (response.status === 401 || response.status === 403) return `The ${PROVIDERS[p].name.replace(' (your key)', '')} key was refused. Check it in ⚙ Settings.`;
    if (response.status === 429) return 'Too many questions at once — wait a moment and try again.';
    return `The AI service answered ${response.status}${detail ? ': ' + detail : ''}.`;
  }

  /** Streams an answer; calls onText with each new piece. */
  async function ask(p, question, history, passages, onText, signal) {
    const messages = [...history, { role: 'user', content: `Excerpts from the PdfEdit guide:\n\n${contextBlock(passages)}\n\nQuestion: ${question}` }];
    if (p === 'site') {
      const r = await fetch(site.chatEndpoint, {
        method: 'POST', signal, headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ question, history, ids: passages.map(x => x.i) }),
      });
      if (!r.ok) throw new Error(await failure(r, p));
      await readSse(r, d => { if (d !== '[DONE]') { const j = JSON.parse(d); if (j.error) throw new Error(j.error); if (j.t) onText(j.t); } });
      return;
    }
    if (p === 'claude') {
      const r = await fetch('https://api.anthropic.com/v1/messages', {
        method: 'POST', signal,
        headers: {
          'content-type': 'application/json', 'x-api-key': prefs.claudeKey, 'anthropic-version': '2023-06-01',
          'anthropic-dangerous-direct-browser-access': 'true',   // the visitor's own key, from their own browser
        },
        body: JSON.stringify({ model: prefs.claudeModel || PROVIDERS.claude.models[0], max_tokens: 1200, system: SYSTEM, messages, stream: true }),
      });
      if (!r.ok) throw new Error(await failure(r, p));
      await readSse(r, d => {
        const j = JSON.parse(d);
        if (j.type === 'content_block_delta' && j.delta?.type === 'text_delta') onText(j.delta.text);
        else if (j.type === 'error') throw new Error(j.error?.message ?? 'The AI service stopped with an error.');
      });
      return;
    }
    // OpenAI and local servers speak the same chat-completions protocol.
    const base = p === 'openai' ? 'https://api.openai.com/v1' : (prefs.localEndpoint || PROVIDERS.local.endpoint).replace(/\/+$/, '');
    const headers = { 'content-type': 'application/json' };
    if (p === 'openai') headers.authorization = `Bearer ${prefs.openaiKey}`;
    let r;
    try {
      r = await fetch(`${base}/chat/completions`, {
        method: 'POST', signal, headers,
        body: JSON.stringify({ model: (p === 'openai' ? prefs.openaiModel || PROVIDERS.openai.models[0] : prefs.localModel || PROVIDERS.local.model), stream: true, messages: [{ role: 'system', content: SYSTEM }, ...messages] }),
      });
    } catch (e) {
      if (p === 'local' && e.name !== 'AbortError')
        throw new Error(`Couldn't reach ${base}. Is the model server running, and does it allow this site? For Ollama, start it with OLLAMA_ORIGINS=${location.origin} (LM Studio: turn on CORS in the server settings).`);
      throw e;
    }
    if (!r.ok) throw new Error(await failure(r, p));
    await readSse(r, d => { if (d !== '[DONE]') { const t = JSON.parse(d).choices?.[0]?.delta?.content; if (t) onText(t); } });
  }

  // ── Showing answers: a small, safe Markdown subset with [n] citations ──────
  function render(md, passages) {
    const inline = s => esc(s)
      .replace(/`([^`]+)`/g, '<code>$1</code>')
      .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
      .replace(/(^|[^*])\*([^*\s][^*]*)\*/g, '$1<em>$2</em>')
      .replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+|[\w./#-]+\.html[^)\s]*)\)/g, (_, text, url) => `<a href="${url.startsWith('http') ? url : ROOT + url}">${text}</a>`)
      .replace(/\[(\d{1,2})\]/g, (m, n) => passages[n - 1] ? `<a class="cite" href="${ROOT}${esc(passages[n - 1].u)}" title="${esc(passages[n - 1].t + (passages[n - 1].h ? ' › ' + passages[n - 1].h : ''))}">${n}</a>` : m);
    const out = [];
    let list = null;
    for (const raw of md.split('\n')) {
      const line = raw.trimEnd();
      const item = line.match(/^\s*(?:([-*•])|(\d+)[.)])\s+(.*)$/);
      if (item) {
        const kind = item[1] ? 'ul' : 'ol';
        if (list !== kind) { if (list) out.push(`</${list}>`); out.push(`<${kind}>`); list = kind; }
        out.push(`<li>${inline(item[3])}</li>`);
        continue;
      }
      if (list) { out.push(`</${list}>`); list = null; }
      const h = line.match(/^#{1,4}\s+(.*)$/);
      if (h) out.push(`<p><strong>${inline(h[1])}</strong></p>`);
      else if (line.trim()) out.push(`<p>${inline(line)}</p>`);
    }
    if (list) out.push(`</${list}>`);
    return out.join('');
  }

  const sourcesHtml = (passages, used) => {
    const show = passages.filter((_, i) => !used.size || used.has(i + 1));
    return show.length ? `<div class="ask-sources"><span>Sources</span>${show.map(p => {
      const n = passages.indexOf(p) + 1;
      return `<a href="${ROOT}${esc(p.u)}"><b>${n}</b>${esc(p.t)}${p.h ? ' › ' + esc(p.h) : ''}</a>`;
    }).join('')}</div>` : '';
  };

  // ── The panel ──────────────────────────────────────────────────────────────
  const ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a8 8 0 0 1-11.6 7.1L4 20l1-4.6A8 8 0 1 1 21 12z"/><path d="M9.5 9.5a2.5 2.5 0 1 1 3.3 2.4c-.5.2-.8.6-.8 1.1v.5M12 16h.01"/></svg>';
  const SUGGESTIONS = ['How do I sign a PDF?', 'How do I make a form fillable?', 'How do I merge two PDFs?', 'Can I use PdfEdit on a Mac?', 'Is my document sent anywhere?'];

  const launcher = document.createElement('button');
  launcher.className = 'ask-launcher';
  launcher.type = 'button';
  launcher.setAttribute('aria-haspopup', 'dialog');
  launcher.innerHTML = `${ICON}<span>Ask the guide</span>`;

  const panel = document.createElement('section');
  panel.className = 'ask-panel';
  panel.hidden = true;
  panel.setAttribute('role', 'dialog');
  panel.setAttribute('aria-label', 'Ask the PdfEdit guide');
  panel.innerHTML = `
    <header>
      <span class="ask-title">${ICON}<span><b>Ask the guide</b><small data-ask-via></small></span></span>
      <button type="button" data-ask-clear title="New conversation" aria-label="New conversation"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><path d="M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5"/></svg></button>
      <button type="button" data-ask-settings title="Settings" aria-label="Settings" aria-expanded="false"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg></button>
      <button type="button" data-ask-close title="Close" aria-label="Close">✕</button>
    </header>
    <div class="ask-settings" data-ask-settings-view hidden></div>
    <div class="ask-log" data-ask-log aria-live="polite"></div>
    <form class="ask-form" data-ask-form>
      <textarea rows="1" placeholder="Ask how to do something in PdfEdit…" aria-label="Your question" data-ask-input maxlength="1000"></textarea>
      <button type="submit" class="ask-send" data-ask-send aria-label="Send">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6"/></svg>
      </button>
    </form>
    <p class="ask-foot">Answers come from the PdfEdit guide and can be wrong — check the linked pages.</p>`;

  const $ = s => panel.querySelector(s);
  const log = $('[data-ask-log]'), input = $('[data-ask-input]'), form = $('[data-ask-form]'), sendBtn = $('[data-ask-send]');
  const settingsView = $('[data-ask-settings-view]'), settingsBtn = $('[data-ask-settings]');
  let history = [], busy = null;

  function showVia() {
    const p = provider();
    $('[data-ask-via]').textContent = p === 'none' ? 'Showing matching sections · ⚙ to add an AI'
      : configured(p) ? `Answered by ${PROVIDERS[p].name.replace(' (your key)', '')}` : `${PROVIDERS[p].name}: add your key in ⚙`;
  }

  function welcome() {
    log.innerHTML = `
      <div class="ask-msg bot"><p>Hi! Ask me how to do something in PdfEdit, and I'll answer from the user guide and tutorials with links to the pages.</p>
      ${provider() === 'none' ? '<p class="muted">No AI is connected, so I\'ll show the sections that match your question. Open ⚙ to use the site\'s assistant, your own Claude or OpenAI key, or a model on your computer.</p>' : ''}
      <div class="ask-suggest">${SUGGESTIONS.map(s => `<button type="button">${esc(s)}</button>`).join('')}</div></div>`;
    log.querySelectorAll('.ask-suggest button').forEach(b => b.addEventListener('click', () => { input.value = b.textContent; submit(); }));
  }

  function open() {
    panel.hidden = false;
    launcher.hidden = true;
    if (!log.children.length) welcome();
    showVia();
    loadIndex().catch(() => { });
    input.focus();
  }
  function close() { panel.hidden = true; launcher.hidden = false; busy?.abort(); launcher.focus(); }

  function settings(show) {
    settingsView.hidden = !show;
    log.hidden = form.hidden = show;
    settingsBtn.setAttribute('aria-expanded', String(show));
    if (!show) { showVia(); return; }
    const p = provider();
    const option = id => (id === 'site' && !site.chatEndpoint) ? '' :
      `<label class="ask-opt"><input type="radio" name="ask-provider" value="${id}"${p === id ? ' checked' : ''}> ${esc(PROVIDERS[id].name)}</label>`;
    settingsView.innerHTML = `
      <h3>Who answers</h3>
      ${Object.keys(PROVIDERS).map(option).join('')}
      <div data-ask-fields></div>
      <p class="ask-note">Keys and settings are kept only in this browser and sent only to the provider you choose — never to the PdfEdit site. With a local model, nothing leaves your computer.</p>
      <div class="ask-row"><button type="button" class="btn small" data-ask-forget>Forget my keys</button><button type="button" class="btn go small" data-ask-done>Done</button></div>`;
    const fields = settingsView.querySelector('[data-ask-fields]');
    const drawFields = () => {
      const id = settingsView.querySelector('input[name="ask-provider"]:checked')?.value ?? 'none';
      const def = PROVIDERS[id];
      fields.innerHTML = [
        def.needs.includes('endpoint') ? `<label>Server address<input data-f="localEndpoint" value="${esc(prefs.localEndpoint || def.endpoint)}" spellcheck="false"></label>
          <p class="ask-note">Ollama: start it with <code>OLLAMA_ORIGINS=${esc(location.origin)} ollama serve</code>. LM Studio: Developer → turn on CORS.</p>` : '',
        def.needs.includes('key') ? `<label>API key<input type="password" data-f="${id}Key" value="${esc(prefs[id + 'Key'] ?? '')}" placeholder="${esc(def.keyHint)}" autocomplete="off" spellcheck="false"></label>` : '',
        def.needs.includes('model') ? (def.models.length
          ? `<label>Model<select data-f="${id}Model">${def.models.map(m => `<option${(prefs[id + 'Model'] || def.models[0]) === m ? ' selected' : ''}>${m}</option>`).join('')}</select></label>`
          : `<label>Model<input data-f="localModel" value="${esc(prefs.localModel || def.model)}" spellcheck="false"></label>`) : '',
        id === 'site' ? '<p class="ask-note">The PdfEdit site\'s own assistant. Nothing to set up.</p>' : '',
      ].join('');
      fields.querySelectorAll('[data-f]').forEach(el => el.addEventListener('change', () => { prefs[el.dataset.f] = el.value.trim(); save(prefs); }));
    };
    settingsView.querySelectorAll('input[name="ask-provider"]').forEach(r => r.addEventListener('change', () => { prefs.provider = r.value; save(prefs); drawFields(); }));
    settingsView.querySelector('[data-ask-forget]').addEventListener('click', () => {
      delete prefs.claudeKey; delete prefs.openaiKey; save(prefs); drawFields();
    });
    settingsView.querySelector('[data-ask-done]').addEventListener('click', () => {
      fields.querySelectorAll('[data-f]').forEach(el => { prefs[el.dataset.f] = el.value.trim(); });
      save(prefs); settings(false); input.focus();
    });
    drawFields();
  }

  function bubble(cls, html) {
    const div = document.createElement('div');
    div.className = `ask-msg ${cls}`;
    div.innerHTML = html;
    log.append(div);
    log.scrollTop = log.scrollHeight;
    return div;
  }

  async function submit() {
    const question = input.value.trim();
    if (!question || busy) return;
    input.value = '';
    input.style.height = '';
    log.querySelector('.ask-suggest')?.remove();
    bubble('me', `<p>${esc(question)}</p>`);
    const answer = bubble('bot', '<p class="ask-typing"><span></span><span></span><span></span></p>');

    // A follow-up ("and on a Mac?") is looked up together with the question before it.
    const lastQuestion = history.filter(m => m.role === 'user').at(-1)?.q ?? '';
    const passages = await retrieve(`${question} ${question.split(/\s+/).length < 6 ? lastQuestion : ''}`).catch(() => []);
    const p = provider();

    if (p === 'none' || !configured(p)) {
      answer.innerHTML = passages.length
        ? `<p>${p === 'none' ? 'Here\'s what the guide says:' : 'Add your key in ⚙ for a written answer. Meanwhile, here\'s what the guide says:'}</p>` +
          passages.slice(0, 4).map((x, i) => `<div class="ask-passage"><a href="${ROOT}${esc(x.u)}"><b>${i + 1}</b>${esc(x.t)}${x.h ? ' › ' + esc(x.h) : ''}</a><p>${esc(x.x.length > 320 ? x.x.slice(0, 320) + '…' : x.x)}</p></div>`).join('')
        : '<p>I couldn\'t find that in the guide. Try other words, browse the <a href="' + ROOT + 'docs/index.html">user guide</a>, or <a href="https://github.com/dotnetappdev/pdfedit/issues/new">ask on GitHub</a>.</p>';
      log.scrollTop = log.scrollHeight;
      return;
    }

    busy = new AbortController();
    sendBtn.classList.add('stop');
    sendBtn.setAttribute('aria-label', 'Stop');
    let text = '';
    try {
      if (!passages.length) throw new Error('nothing');
      await ask(p, question, history.map(({ role, content }) => ({ role, content })), passages, piece => {
        text += piece;
        answer.innerHTML = render(text, passages);
        log.scrollTop = log.scrollHeight;
      }, busy.signal);
      const used = new Set([...text.matchAll(/\[(\d{1,2})\]/g)].map(m => +m[1]));
      answer.innerHTML = render(text, passages) + sourcesHtml(passages, used);
      history.push({ role: 'user', content: question, q: question }, { role: 'assistant', content: text });
      history = history.slice(-8);   // the last four questions and answers
    } catch (e) {
      if (e.name === 'AbortError') answer.innerHTML = render(text || '_Stopped._', passages);
      else if (e.message === 'nothing') answer.innerHTML = '<p>I couldn\'t find anything about that in the guide. Try other words, or <a href="https://github.com/dotnetappdev/pdfedit/issues/new">ask on GitHub</a>.</p>';
      else answer.innerHTML = `<p class="ask-error">${esc(e.message || 'Something went wrong.')}</p>` + sourcesHtml(passages, new Set());
    } finally {
      busy = null;
      sendBtn.classList.remove('stop');
      sendBtn.setAttribute('aria-label', 'Send');
      log.scrollTop = log.scrollHeight;
    }
  }

  form.addEventListener('submit', e => { e.preventDefault(); busy ? busy.abort() : submit(); });
  input.addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); submit(); } });
  input.addEventListener('input', () => { input.style.height = ''; input.style.height = Math.min(input.scrollHeight, 140) + 'px'; });
  launcher.addEventListener('click', open);
  $('[data-ask-close]').addEventListener('click', close);
  $('[data-ask-clear]').addEventListener('click', () => { busy?.abort(); history = []; log.innerHTML = ''; welcome(); input.focus(); });
  settingsBtn.addEventListener('click', () => settings(settingsView.hidden));
  panel.addEventListener('keydown', e => { if (e.key === 'Escape') { e.stopPropagation(); settingsView.hidden ? close() : settings(false); } });

  document.body.append(launcher, panel);
  document.querySelectorAll('[data-ask-open]').forEach(b => b.addEventListener('click', e => { e.preventDefault(); open(); }));
  // ?ask=… opens the chat with a question (the apps' Help can link straight to an answer).
  const asked = new URLSearchParams(location.search).get('ask');

  fetch(ROOT + 'data/site.json').then(r => r.ok ? r.json() : {}).catch(() => ({})).then(s => {
    site = s ?? {};
    showVia();
    if (asked) { open(); input.value = asked.slice(0, 1000); submit(); }
  });
})();
