# "Ask the guide" assistant (optional)

The website's **Ask the guide** chat answers questions from the user guide and tutorials. It works
in three ways, and visitors can choose in its ⚙ settings:

| | Who pays | Set up |
|---|---|---|
| **The site's assistant** (this Worker) | the project's Anthropic key | once, below |
| **Free AI in the browser** (WebLLM) | free | nothing: a small open model runs on the visitor's GPU after a one-time download |
| **The visitor's own key** (Claude or OpenAI) | the visitor | nothing: they paste their key; it stays in their browser |
| **A local model** (Ollama, LM Studio) | free | the visitor runs it with `OLLAMA_ORIGINS=<site origin>` |
| **No AI** | free | the chat shows the matching guide sections |

Until a Worker address is set, the chat uses the free in-browser AI where the browser supports WebGPU, and the matching sections elsewhere.

## How it answers

For each question, the page picks the best passages from `data/chunks.json` (every heading of the
guide and tutorials, made by the build) and sends them to the model with the question. The model is
told to answer from those passages only and cite them as [1], [2]…; the chat turns the numbers into
links. The Worker receives passage *numbers*, not text, and reads the passages from the published
site, so it can only be used for questions about PdfEdit.

## Deploying the Worker

Needs a free [Cloudflare](https://dash.cloudflare.com) account and an
[Anthropic API key](https://console.anthropic.com).

```bash
cd staticsite/worker
npx wrangler login
npx wrangler secret put ANTHROPIC_API_KEY     # paste the key
npx wrangler deploy                           # prints https://pdfedit-ask.<you>.workers.dev
```

Check `SITE_URL` and `ALLOWED_ORIGINS` in `wrangler.toml` first (the published site and its origin).
Or deploy from GitHub: add the repository secrets `CLOUDFLARE_API_TOKEN` (a token with *Edit
Cloudflare Workers*) and `ANTHROPIC_API_KEY`, then run **Actions → Ask assistant → Run workflow**.

Then tell the site where it is, either:

- set the repository **variable** `ASK_ENDPOINT` (Settings → Secrets and variables → Actions →
  Variables) to the Worker's address; the Website workflow puts it in at build time; or
- put it in `staticsite/data/site.json` as `"chatEndpoint"`.

## Costs and limits

Each answer sends about 6 passages (2,000 to 3,000 tokens) and the last few turns. With the default
`claude-haiku-5-5` that's a fraction of a cent a question. The Worker allows 10 questions a minute
per visitor (the `LIMITER` binding), questions up to 1000 characters and answers up to 1000 tokens.
Set a monthly spend limit on the Anthropic key as well.
