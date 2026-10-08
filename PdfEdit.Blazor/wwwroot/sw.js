// PdfEdit's service worker: makes PdfEdit installable as an app. The editor runs on the server, so
// nothing is cached for offline use — when the connection is down, a page saying so is shown
// instead of the browser's error, and your drafts stay safe in this browser.
const OFFLINE = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>PdfEdit — offline</title><style>body{font:16px system-ui,sans-serif;margin:0;display:grid;place-items:center;min-height:100vh;background:#F5F5F5;color:#1A1A1A}
@media (prefers-color-scheme:dark){body{background:#1E1E1E;color:#EEE}}main{max-width:420px;padding:24px;text-align:center}button{font:inherit;padding:8px 18px;border-radius:6px;border:0;background:#4F46E5;color:#fff;cursor:pointer}</style></head>
<body><main><h1>PdfEdit is offline</h1><p>PdfEdit needs a connection to its server. Your drafts are kept in this browser and come back when you reconnect.</p>
<button onclick="location.reload()">Try again</button></main></body></html>`;

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));
self.addEventListener('fetch', e => {
    if (e.request.mode !== 'navigate') return;   // everything else goes to the network as usual
    e.respondWith(fetch(e.request).catch(() => new Response(OFFLINE, { headers: { 'Content-Type': 'text/html; charset=utf-8' } })));
});
