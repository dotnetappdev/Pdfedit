# How the code is organised

[← Back to README](../README.md)

PdfEdit is a WPF app on .NET 10 using MVVM. The main folders under `PdfEdit/` are:

| Folder | What's in it |
|--------|--------------|
| `Engine/` | A PDF renderer written in C#: parser, stream filters, fonts and the content-stream interpreter |
| `Services/` | Everything that reads or writes PDFs (iText 7), plus rendering, OCR, scanning, AI providers and settings |
| `ViewModels/` | `MainViewModel` (split into partial files by feature) and the design canvas view model |
| `Controls/` | The page viewer and its overlays, the design canvas, thumbnails, the toolbox and the side panels |
| `Dialogs/` | Settings, scan, watermark, links, field properties and the other windows |
| `Models/` | Annotations, form fields, design elements, the stamp catalogue and options classes |
| `Themes/`, `Resources/` | Colour tokens for each theme and the shared styles that use them |

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
| iText 7 (8.x) | Reading and writing PDFs: forms, annotations, pages, encryption |
| Fluent.Ribbon | The ribbon |
| AvalonDock | Dockable panels |
| Docnet.Core | The optional Pdfium renderer |
| NTwain | TWAIN scanners |
| Tesseract | Bundled OCR engine |
| xUnit | Tests |

## Privacy

There's no telemetry. The only network requests are to an AI provider you've configured, OCR language downloads you start yourself, and links you choose to open.
