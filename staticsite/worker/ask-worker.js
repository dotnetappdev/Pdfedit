// The PdfEdit website's "Ask the guide" assistant: a Cloudflare Worker that answers questions about
// PdfEdit with Claude, using the passages of the user guide the page picked. The API key stays here
// (a Worker secret) and never reaches the browser.
//
// It only answers about the guide: the page sends passage numbers, not text, and the Worker reads the
// passages from the published site itself (data/chunks.json), so it can't be used as a free
// general-purpose AI. Questions and history are size-limited, and an optional rate limit applies per
// visitor. Set up: see README.md in this folder.
//
// Settings (wrangler.toml [vars] or the dashboard):
//   SITE_URL         the published site, e.g. https://dotnetappdev.github.io/Pdfedit/
//   ALLOWED_ORIGINS  comma-separated origins allowed to call it, e.g. https://dotnetappdev.github.io
//   MODEL            Claude model (default claude-haiku-5-5: quick and inexpensive)
// Secret:  ANTHROPIC_API_KEY  (npx wrangler secret put ANTHROPIC_API_KEY)
// Optional binding: LIMITER (a rate limit, see wrangler.toml)

const SYSTEM = `You are the help assistant on the PdfEdit website. PdfEdit is a free, open-source PDF editor in three versions: PdfEdit for Windows (the full Windows app), PdfEdit for Mac (the web interface in a Mac app) and PdfEdit for the web (self-hosted, used in a browser).
Answer the user's question using ONLY the numbered excerpts from the PdfEdit user guide and tutorials that are provided. Rules:
- Be brief and practical. For "how do I" questions, give numbered steps with the exact button and tab names in bold.
- Cite the excerpts you used with their numbers in square brackets, like [1] or [2][4], right after the sentence they support.
- If the Windows app and the Mac/web versions differ, say how for each.
- If the excerpts don't cover the question, say you couldn't find it in the guide and suggest searching the guide or reporting it on GitHub. Never invent buttons, menus or features.
- Only help with PdfEdit and PDFs. Politely decline anything unrelated.`;

const LIMITS = { body: 24_000, question: 1000, turns: 8, turn: 4000, passages: 8 };
let cache = { at: 0, chunks: null };

async function passages(env) {
  if (!cache.chunks || Date.now() - cache.at > 10 * 60_000) {
    const r = await fetch(new URL('data/chunks.json', env.SITE_URL), { cf: { cacheTtl: 600 } });
    if (!r.ok) throw new Error(`Couldn't read the guide (${r.status})`);
    cache = { at: Date.now(), chunks: await r.json() };
  }
  return cache.chunks;
}

function cors(request, env) {
  const origin = request.headers.get('origin') ?? '';
  const allowed = (env.ALLOWED_ORIGINS ?? '').split(',').map(s => s.trim()).filter(Boolean);
  return {
    ok: allowed.includes(origin),
    headers: {
      'access-control-allow-origin': allowed.includes(origin) ? origin : allowed[0] ?? '',
      'access-control-allow-methods': 'POST, OPTIONS',
      'access-control-allow-headers': 'content-type',
      'access-control-max-age': '86400',
      vary: 'origin',
    },
  };
}

const json = (status, body, headers) => new Response(JSON.stringify(body), { status, headers: { ...headers, 'content-type': 'application/json' } });

export default {
  async fetch(request, env) {
    const { ok, headers } = cors(request, env);
    if (request.method === 'OPTIONS') return new Response(null, { status: ok ? 204 : 403, headers });
    if (request.method !== 'POST') return json(405, { error: 'POST only' }, headers);
    if (!ok) return json(403, { error: 'This assistant only answers on the PdfEdit website.' }, headers);
    if (!env.ANTHROPIC_API_KEY) return json(503, { error: 'The assistant isn\'t set up yet.' }, headers);

    if (env.LIMITER) {
      const who = request.headers.get('cf-connecting-ip') ?? 'anyone';
      const { success } = await env.LIMITER.limit({ key: who });
      if (!success) return json(429, { error: 'Too many questions — wait a minute and try again.' }, headers);
    }

    // ── Check what was sent ──
    const raw = await request.text();
    if (raw.length > LIMITS.body) return json(413, { error: 'That question is too long.' }, headers);
    let body;
    try { body = JSON.parse(raw); } catch { return json(400, { error: 'Bad request' }, headers); }
    const question = typeof body.question === 'string' ? body.question.trim() : '';
    if (!question || question.length > LIMITS.question) return json(400, { error: 'Ask a question of up to 1000 characters.' }, headers);
    const history = (Array.isArray(body.history) ? body.history : []).slice(-LIMITS.turns)
      .filter(m => (m?.role === 'user' || m?.role === 'assistant') && typeof m.content === 'string')
      .map(m => ({ role: m.role, content: m.content.slice(0, LIMITS.turn) }));
    while (history.length && history[0].role !== 'user') history.shift();   // Claude wants user first, then turns
    if (history.length % 2) history.pop();
    const ids = (Array.isArray(body.ids) ? body.ids : []).filter(Number.isInteger).slice(0, LIMITS.passages);

    let chunks;
    try { chunks = await passages(env); } catch (e) { return json(502, { error: e.message }, headers); }
    const picked = ids.map(i => chunks[i]).filter(Boolean);
    if (!picked.length) return json(400, { error: 'Nothing in the guide matched that question.' }, headers);
    const excerpts = picked.map((p, i) => `[${i + 1}] ${p.t}${p.h ? ' › ' + p.h : ''} (${p.s})\n${p.x}`).join('\n\n');

    // ── Ask Claude, and pass the answer on as it's written ──
    const upstream = await fetch('https://api.anthropic.com/v1/messages', {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-api-key': env.ANTHROPIC_API_KEY, 'anthropic-version': '2023-06-01' },
      body: JSON.stringify({
        model: env.MODEL || 'claude-haiku-5-5', max_tokens: 1000, system: SYSTEM, stream: true,
        messages: [...history, { role: 'user', content: `Excerpts from the PdfEdit guide:\n\n${excerpts}\n\nQuestion: ${question}` }],
      }),
    });
    if (!upstream.ok) {
      console.log('Claude answered', upstream.status, await upstream.text());
      return json(502, { error: upstream.status === 429 ? 'The assistant is busy — try again in a minute.' : 'The assistant couldn\'t answer just now.' }, headers);
    }

    const encoder = new TextEncoder(), decoder = new TextDecoder();
    let buffer = '';
    const send = (controller, obj) => controller.enqueue(encoder.encode(`data: ${typeof obj === 'string' ? obj : JSON.stringify(obj)}\n\n`));
    const stream = upstream.body.pipeThrough(new TransformStream({
      transform(chunk, controller) {
        buffer += decoder.decode(chunk, { stream: true });
        let nl;
        while ((nl = buffer.indexOf('\n')) >= 0) {
          const line = buffer.slice(0, nl).trim();
          buffer = buffer.slice(nl + 1);
          if (!line.startsWith('data:')) continue;
          let event;
          try { event = JSON.parse(line.slice(5)); } catch { continue; }
          if (event.type === 'content_block_delta' && event.delta?.type === 'text_delta') send(controller, { t: event.delta.text });
          else if (event.type === 'error') send(controller, { error: 'The assistant stopped part way. Try again.' });
        }
      },
      flush(controller) { send(controller, '[DONE]'); },
    }));
    return new Response(stream, { headers: { ...headers, 'content-type': 'text/event-stream', 'cache-control': 'no-store' } });
  },
};
