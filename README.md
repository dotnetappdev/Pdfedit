# PdfEdit

[![CI](https://github.com/dotnetappdev/Pdfedit/actions/workflows/ci.yml/badge.svg)](https://github.com/dotnetappdev/Pdfedit/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/dotnetappdev/Pdfedit?display_name=tag)](https://github.com/dotnetappdev/Pdfedit/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

PdfEdit is a free PDF editor for Windows. Open a PDF to fill in forms, sign them, add comments or rearrange pages. You can also design a new document from scratch. It's a native WPF app with an Office-style ribbon and dark, light and high-contrast themes, and it doesn't need an account or an internet connection.

![Filling in a job application with PdfEdit](docs/screenshots/demo-v4.gif)

## Download

Grab the latest version from the [Releases page](https://github.com/dotnetappdev/Pdfedit/releases/latest):

| File | Use it if |
|------|-----------|
| `PdfEditSetup-x.y.z.exe` | You want a normal installer with Start menu shortcuts. Everything is included. |
| `PdfEdit-x.y.z-win-x64-portable.zip` | You'd rather not install anything. Unzip and run `PdfEdit.exe`. |
| `PdfEdit-x.y.z-win-x64.zip` | You already have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) and want a smaller download. |
| `PdfEdit-x.y.z.msix` | You prefer MSIX packages. It's test-signed, so see the release notes first. |

PdfEdit runs on 64-bit Windows 10 (version 2004 or later) and Windows 11.

## What you can do with it

- **Fill and sign.** Type into form fields, or click anywhere on a flat PDF to add text, ticks, crosses and dates. Then add your signature or initials.
- **Prepare forms.** Add text fields, check boxes, radio buttons, dropdowns, signature and date fields. Move and resize them, and edit their properties.
- **Comment and mark up.** Highlight, underline, strike through, add sticky notes, shapes and freehand drawing. Place stamps such as APPROVED or SIGN HERE, and add links.
- **Organise pages.** Rotate, reorder, insert, delete, extract, merge and split pages. Add watermarks, headers and footers, page numbers and Bates numbers.
- **Scan and OCR.** Scan from any TWAIN or Windows scanner, then make the result searchable.
- **Design documents.** Lay out invoices, letters, certificates and forms on a blank canvas, then save them as PDF.
- **Use AI if you want to.** Bring your own Claude or OpenAI key, or run a local model, and let it fill forms or summarise documents. Nothing is sent anywhere unless you set this up.

## Documentation

Each topic has its own short page:

- [Filling and signing](docs/filling-and-signing.md)
- [Preparing forms](docs/forms.md)
- [Comments, stamps and links](docs/comments-and-markup.md)
- [Pages, watermarks and security](docs/pages-and-security.md)
- [Scanning and OCR](docs/scan-and-ocr.md)
- [Design canvas](docs/design-canvas.md)
- [AI assistant](docs/ai-assistant.md)
- [Keyboard shortcuts](docs/keyboard-shortcuts.md)
- [Screenshots](docs/screenshots.md)
- [Building from source and releases](docs/building.md)
- [How the code is organised](docs/architecture.md)

## Building it yourself

You'll need the .NET 10 SDK on Windows:

```powershell
git clone https://github.com/dotnetappdev/Pdfedit.git
cd Pdfedit
dotnet run --project PdfEdit
```

Or open `PdfEdit.sln` in Visual Studio 2022 and press F5. For tests, installers and how releases are published, see [docs/building.md](docs/building.md).

## Contributing

Bug reports and pull requests are welcome. If you're planning something big, please open an issue first so we can talk it through. The `devmain` branch is where development happens.

## Licence

PdfEdit is released under the [MIT licence](LICENSE). It uses [iText 7](https://itextpdf.com/) for PDF processing, which is licensed separately under the AGPL. Please check that it suits how you plan to use or distribute the app.
