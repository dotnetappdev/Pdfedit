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
- `scenes.mjs` has the page artwork, window layout and the screenshot scenes.
- `render.mjs` writes the PNGs.
- `gif.mjs` writes `fill-and-sign.gif` (needs `ffmpeg`): `node docs/screenshots/src/gif.mjs`
