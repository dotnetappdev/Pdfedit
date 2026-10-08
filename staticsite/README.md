# PdfEdit website

The project's website: home, downloads, what's new, screenshots, the **user guide** and
**tutorials**, in the style of classic software sites, with light and dark themes. Plain HTML, CSS
and a little JavaScript, with no framework. Help → User Guide / Tutorials in the Windows app, the web
version and the Mac app open it (the addresses are in `PdfEdit.Core/Services/HelpLinks.cs`).

```
index.html, download.html, whats-new.html, screenshots.html   the pages
partials/          head, header and footer, put into each page by the build
assets/            site.css, site.js, the logo
data/site.json     settings: the repository, and demoUrl (shows "Try it online" when set)
data/releases.json the releases, downloads and changes (made by tools/update-releases.mjs)
content/docs/      user guide pages that only the site has (install, getting started, troubleshooting)
content/tutorials/ the tutorials, one Markdown file each
tools/             update-releases.mjs and build.mjs
```

## Downloads and what's new update themselves

`tools/update-releases.mjs` reads the GitHub releases and writes `data/releases.json`: each version,
its date, the changes from the release's *What's new* list, and its files sorted by product
(PdfEdit for Windows, PdfEdit for Mac) and platform. The pages fill in the version, the download
tables, the news and the release history from it, and the big download button picks the file for
the visitor's computer. If the file is missing, the pages ask GitHub directly.

The **Website** workflow (`.github/workflows/website.yml`) fetches the releases, builds the site and
publishes it after every release, daily, and whenever the site changes. It works either way GitHub
Pages is set up:

- **Settings → Pages → Source: GitHub Actions**: deployed directly (the workflow also asks GitHub to
  turn this on);
- otherwise the site is pushed to the **gh-pages** branch: choose **Source: Deploy from a branch →
  gh-pages / (root)**.

Every link is relative, so it works at `https://<owner>.github.io/<repo>/` or on its own domain.

New files are recognised by name (see `classify` in `tools/update-releases.mjs` and `RULES` in
`assets/js/site.js`); add a rule there when a release gains a new kind of file.

## User guide and tutorials

The user guide is built from the Markdown in the repository's `docs/` folder (so the docs on GitHub
and on the site are the same pages), plus `content/docs/`. Its order and sections are the `GUIDE`
list at the top of `tools/build.mjs`; add a page there. Links between `.md` files become links
between pages, pictures are copied, and anything else in the repository links to GitHub.

Each tutorial is a Markdown file in `content/tutorials/` that starts with:

```
---
title: Fill in and sign a PDF form
summary: One sentence for the list and search results.
group: Forms and signing        # one of TUTORIAL_GROUPS in tools/build.mjs
level: Beginner                 # Beginner, Intermediate or Advanced
time: 5 minutes
apps: Windows, Mac, Web
order: 1                        # its place in the group
---
```

Each `##` heading is a numbered step, except *Before you start*, *What you'll need*, *If something
goes wrong* and *What's next*. A quote that starts with **Tip:**, **Note:**, **Important:**,
**Warning:** or **Mac:** becomes a call-out box. Link to a guide page or another tutorial by its file
name (`forms.md`, `make-a-form-fillable.md`). The build writes `data/search.json` for the search
box.

## Ask the guide (AI chat)

Every page has an **Ask the guide** button (`assets/js/ask.js`). It answers questions from the user
guide and tutorials: the build cuts them into passages (`data/chunks.json`, one per heading), the
page picks the best ones for each question and an LLM answers from those only, citing them as
links. Visitors choose who answers in its ⚙ settings: the site's own assistant, a **free AI that runs
in their browser** (the default when no site assistant is set up), their own Claude or OpenAI key
(kept in their browser), a local model (Ollama, LM Studio), or no AI (the matching sections).

The free AI is [WebLLM](https://github.com/mlc-ai/web-llm) running a small open model (Qwen 2.5 1.5B
by default; Llama 3.2 1B and Qwen 2.5 3B in settings) on the visitor's GPU through WebGPU, in a
worker (`assets/js/ask-llm-worker.js`). It asks before the one-time download (about 1 GB), then
loads from the browser's cache; nothing the visitor asks leaves their computer. It needs WebGPU
(Chrome or Edge 113+, Safari 26); elsewhere the chat falls back to the matching sections. The site's assistant is a small Cloudflare Worker that keeps the API key secret; see
[`worker/README.md`](worker/README.md) to deploy it and set `ASK_ENDPOINT`. `?ask=question` on any
page opens the chat with that question.

## Working on it

```bash
npm ci --prefix staticsite                  # once: the Markdown converter
node staticsite/tools/update-releases.mjs   # refresh data/releases.json (optional)
node staticsite/tools/build.mjs             # → staticsite/_site
python3 -m http.server -d staticsite/_site  # then open http://localhost:8000
```

Screenshots come from `docs/screenshots`: `img/shots/name.png` is `docs/screenshots/blazor/name.png`
and `img/shots/win-name.png` is `docs/screenshots/name.png`; the build copies the ones the pages use.
