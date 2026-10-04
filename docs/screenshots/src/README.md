# Screenshot generator

The ribbon and form-filling images in `docs/screenshots` are HTML mock-ups rendered with Playwright. They aren't captures of the running app, so they can be made on any OS, including CI.

```bash
npm i -g playwright            # once, if you don't have it
node docs/screenshots/src/render.mjs                 # all images
node docs/screenshots/src/render.mjs fill-stamps.png # just one
```

- `ribbons.mjs` lists each ribbon tab's groups and buttons. Keep it in step with `PdfEdit/MainWindow.xaml` when the ribbon changes.
- `ui.mjs` holds the dark-theme colours (from `Themes/DarkTheme.xaml`) and the window, ribbon and panel pieces.
- `icons.mjs` has the line icons.
- `render.mjs` builds each scene and writes the PNGs.
