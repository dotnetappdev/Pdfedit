# How the code is organised

[← Back to README](../README.md)

PdfEdit is a .NET 10 app split into libraries, so the UI can be swapped (Avalonia, MAUI, a command-line tool or a web API) without touching the logic or the renderer:

| Project | Target | What's in it |
|---------|--------|--------------|
| `PdfEdit.Core` | `net10.0`, no UI | `Services/`: reading and writing PDFs with iText 7 (forms, annotations, pages, watermarks, redaction, signatures, sanitising, accessibility checks, XFDF), Word and Office import, Excel and PowerPoint export, field detection, bulk fill, AI providers, cloud storage, measurement, settings and stores. `Models/`: annotations, form fields, profiles, signatures and options, using its own `PointD` and `TextAlign` types. |
| `PdfEdit.Render` | `net10.0`, no UI | Page rendering. `Engine/`: the built-in PDF engine (parser, lexer, stream filters, colour spaces, fonts and the content-stream interpreter). `Drawing/`: the drawing interface the engine draws through (`IDrawingSurface`, `IDrawingBackend`, `IFontProvider`) and its types (paths, colours, matrices, images). Also the Pdfium engine (Docnet), which returns raw pixels. |
| `PdfEdit.Drawing.Wpf` | `net10.0-windows`, WPF | Everything that draws with WPF: the drawing backend (DrawingContext, glyph runs, system fonts, image decoding), `WpfPdfRenderer` (rendered pages to `BitmapSource`), and the Windows-only `Windows.Data.Pdf` renderer. |
| `PdfEdit` | `net10.0-windows`, WPF | The app: windows, controls, view models, and the Windows parts listed below |

`PdfEdit.Core` and `PdfEdit.Render` build and run on Windows, macOS and Linux. The WPF app converts Core's `PointD` and `TextAlign` to WPF's types with `ToWpf()` / `ToCore()` (`CoreInterop.cs`).

### How a page is drawn

`RendererFactory` picks an engine from Settings → Render Engine. The built-in engine (`CustomPdfEngine`) reads the page's drawing operators and calls an `IDrawingSurface`: fill or stroke a path, push a clip, transform or opacity, draw an image or a glyph. `PdfEdit.Drawing.Wpf` turns those calls into a WPF `DrawingContext` and a bitmap. The Pdfium engine renders to pixels by itself. `WpfPdfRenderer` gives the app a `BitmapSource` either way.

To render with another UI, implement `IDrawingBackend`, `IDrawingSurface` and `IFontProvider` for it (about 200 lines; Avalonia's `DrawingContext` and SkiaSharp both map almost one to one), and wrap pages for its image type, as `PdfEdit.Drawing.Wpf` does for WPF. The engines themselves don't change.

Folders in `PdfEdit/`:

| Folder | What's in it |
|--------|--------------|
| `Services/` | The Windows and WPF parts: choosing the renderer (`RendererFactory`), OCR (Windows OCR and Tesseract), scanning (TWAIN and WIA), read aloud and narration, voice input, themes, fonts and icon sizes, keyboard shortcuts, batch processing, scan tools, visual compare, and the design canvas's import, export and file format |
| `ViewModels/` | `MainViewModel` (split into partial files by feature) and the design canvas view model |
| `Controls/` | The page viewer and its overlays, the design canvas, thumbnails, the toolbox and the side panels |
| `Dialogs/` | Settings, scan, watermark, links, field properties and the other windows |
| `Models/` | The design canvas elements (they use WPF colours and bitmaps) and page thumbnails |
| `Themes/`, `Resources/` | Colour tokens for each theme and the shared styles that use them |

### Moving to another UI

A new front end references `PdfEdit.Core` and gets all of the above. What it still needs from the WPF app, in rough order of effort:

- **Drawing**: a backend for the new UI's graphics (see above). Rendering itself is already in `PdfEdit.Render`.
- **The view models**: `MainViewModel` uses `ICommand`, dialogs and WPF types directly. Splitting UI-free state and commands into Core (with dialogs behind interfaces) is the biggest remaining step.
- **Design canvas models**: `DesignElement` uses WPF `Color` and `BitmapSource`; swapping those for Core types would let the designer, its file format and its PDF export move too.
- **Platform services** (OCR, scanning, speech, themes): define interfaces in Core and implement them per platform.

## Rendering

You can choose between three page renderers in **Settings → Render Engine**:

| Engine | Notes |
|--------|-------|
| Built-in (default) | Pure C#, no native DLLs. Handles xref streams, object streams, the common filters, standard and embedded fonts, and images. |
| Pdfium | Google's PDF engine through Docnet.Core. The same engine Chrome uses. |
| Windows | The `Windows.Data.Pdf` API built into Windows 10 and 11. |

Annotations, fields and signatures are drawn as WPF elements on top of the rendered page. They're only written into the PDF when you save.

## Main libraries

| Library | Used for |
|---------|----------|
| iText 7 (8.x) | Reading and writing PDFs: forms, annotations, pages, encryption (in `PdfEdit.Core`) |
| Fluent.Ribbon | The ribbon |
| AvalonDock | Dockable panels |
| Docnet.Core | The optional Pdfium renderer (in `PdfEdit.Render`) |
| NTwain | TWAIN scanners |
| Tesseract | Bundled OCR engine |
| xUnit | Tests |

## Privacy

There's no telemetry. The only network requests are to an AI provider you've configured, OCR language downloads you start yourself, and links you choose to open.
