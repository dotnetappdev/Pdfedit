# PdfEdit website

The project's download site: home, downloads, what's new and screenshots, in the style of classic
software sites, with light and dark themes. Plain HTML, CSS and a little JavaScript — no framework.

```
index.html, download.html, whats-new.html, screenshots.html   the pages
partials/          head, header and footer, put into each page by the build
assets/            site.css, site.js, the logo
data/site.json     settings: the repository, and demoUrl (shows "Try it online" when set)
data/releases.json the releases, downloads and changes (made by tools/update-releases.mjs)
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

- **Settings → Pages → Source: GitHub Actions** — deployed directly (the workflow also asks GitHub to
  turn this on);
- otherwise the site is pushed to the **gh-pages** branch: choose **Source: Deploy from a branch →
  gh-pages / (root)**.

Every link is relative, so it works at `https://<owner>.github.io/<repo>/` or on its own domain.

New files are recognised by name (see `classify` in `tools/update-releases.mjs` and `RULES` in
`assets/js/site.js`); add a rule there when a release gains a new kind of file.

## Working on it

```bash
node staticsite/tools/update-releases.mjs   # refresh data/releases.json (optional)
node staticsite/tools/build.mjs             # → staticsite/_site
python3 -m http.server -d staticsite/_site  # then open http://localhost:8000
```

Screenshots come from `docs/screenshots`: `img/shots/name.png` is `docs/screenshots/blazor/name.png`
and `img/shots/win-name.png` is `docs/screenshots/name.png`; the build copies the ones the pages use.
