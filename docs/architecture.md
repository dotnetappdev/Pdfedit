# How the code is organised

[← Back to README](../README.md)

PdfEdit is a .NET 10 app in two projects, so the logic can be reused under another UI (Avalonia, MAUI, a command-line tool or a web API):

| Project | Target | What's in it |
|---------|--------|--------------|
| `PdfEdit.Core` | `net10.0` (no WPF, no Windows-only types) | `Services/`: reading and writing PDFs with iText 7 (forms, annotations, pages, watermarks, redaction, signatures, sanitising, accessibility checks, XFDF), Word and Office import, Excel and PowerPoint export, field detection, bulk fill, AI providers, cloud storage, measurement, settings and stores. `Models/`: annotations, form fields, profiles, signatures and options, using its own `PointD` and `TextAlign` types. |
| `PdfEdit` | `net10.0-windows` (WPF) | The UI and the Windows-only parts, in the folders below |

`PdfEdit.Core` builds and runs on Windows, macOS and Linux. Office conversion through Microsoft Office and the DPAPI-encrypted cloud tokens only work on Windows. The WPF app converts Core's `PointD` and `TextAlign` to WPF's types with `ToWpf()` / `ToCore()` (`CoreInterop.cs`).

Folders in `PdfEdit/`:

| Folder | What's in it |
|--------|--------------|
| `Engine/` | A PDF renderer written in C#: parser, stream filters, fonts and the content-stream interpreter (draws with WPF) |
| `Services/` | The Windows and WPF parts: page rendering, OCR (Windows OCR and Tesseract), scanning (TWAIN and WIA), read aloud and narration, voice input, themes, fonts and icon sizes, keyboard shortcuts, batch processing, scan tools, visual compare, and the design canvas's import, export and file format |
| `ViewModels/` | `MainViewModel` (split into partial files by feature) and the design canvas view model |
| `Controls/` | The page viewer and its overlays, the design canvas, thumbnails, the toolbox and the side panels |
| `Dialogs/` | Settings, scan, watermark, links, field properties and the other windows |
| `Models/` | The design canvas elements (they use WPF colours and bitmaps) and page thumbnails |
| `Themes/`, `Resources/` | Colour tokens for each theme and the shared styles that use them |

### Moving to another UI

A new front end references `PdfEdit.Core` and gets all of the above. What it still needs from the WPF app, in rough order of effort:

- **Page rendering**: the built-in `Engine/` and the Pdfium path draw into WPF bitmaps. Pdfium (Docnet.Core) is cross-platform, so a renderer that returns raw pixels belongs in Core next.
- **The view models**: `MainViewModel` uses `ICommand`, dialogs and WPF types directly. Splitting UI-free state and commands into Core (with dialogs behind interfaces) is the biggest remaining step.
- **Design canvas models**: `DesignElement` uses WPF `Color` and `BitmapSource`; swapping those for Core types would let the designer, its file format and its PDF export move too.
- **Platform services** (OCR, scanning, speech, themes): define interfaces in Core and implement them per platform.

## Rendering

You can choose between three page renderers in **Settings → Render Engine**:

| Engine | Notes |
|--------|-------|
| Built-in (default) | Pure C#, no native DLLs. Handles xref streams, object streams, the common filters, standard and embedded fonts, and images. |
| Pdfium | Google's PDF engine through Docnet.Core. The closest match to Chrome and Acrobat. |
| Windows | The `Windows.Data.Pdf` API built into Windows 10 and 11. |

Annotations, fields and signatures are drawn as WPF elements on top of the rendered page. They're only written into the PDF when you save.

## Main libraries

| Library | Used for |
|---------|----------|
| iText 7 (8.x) | Reading and writing PDFs: forms, annotations, pages, encryption (in `PdfEdit.Core`) |
| Fluent.Ribbon | The ribbon |
| AvalonDock | Dockable panels |
| Docnet.Core | The optional Pdfium renderer |
| NTwain | TWAIN scanners |
| Tesseract | Bundled OCR engine |
| xUnit | Tests |

## Privacy

There's no telemetry. The only network requests are to an AI provider you've configured, OCR language downloads you start yourself, and links you choose to open.
