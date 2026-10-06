<p align="center">
  <img src="docs/logo.svg" width="80" alt="PdfEdit logo">
</p>

<h1 align="center">PdfEdit</h1>

<p align="center">A free PDF editor for Windows, built to rival Adobe Acrobat. No ads, no subscriptions, no paywalls.</p>

<p align="center">
  <a href="https://github.com/dotnetappdev/Pdfedit/releases/latest"><img src="https://img.shields.io/github/v/release/dotnetappdev/Pdfedit?display_name=tag&label=release" alt="Latest release"></a>
  <a href="https://github.com/dotnetappdev/Pdfedit/actions/workflows/ci.yml?query=branch%3Adevmain"><img src="https://img.shields.io/github/actions/workflow/status/dotnetappdev/Pdfedit/ci.yml?branch=devmain&label=build" alt="Build status"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6" alt="Windows 10 and 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT licence"></a>
</p>

## Why PdfEdit?

You open a form, click to type into it, and get asked to upgrade. You want to merge two files, and that's a paid feature. A "free" editor turns out to watermark every page, or cap you at three documents a day. Acrobat Reader lets you look at a PDF, but doing almost anything with it means a monthly subscription.

PdfEdit is the PDF editor I wanted instead: everything included, nothing locked, no account, no ads and no watermarks. It works offline and your files stay on your PC.

## What it can do

Fill in and sign any form, even a flat scan with no fields, and send it back. Highlight, comment, stamp and redact. Rotate, reorder, merge and split pages, scan straight to a searchable PDF, and turn Word documents into fillable forms. Batch-process a folder, fill a form from every row of a spreadsheet, and save to Google Drive or OneDrive. There's an optional AI assistant if you want one, and it can run on a free local model.

**[Download](https://github.com/dotnetappdev/Pdfedit/releases/latest)** for Windows 10 (2004+) and 11, 64-bit: installer or portable zip.

**[Features](docs/features.md)** · [Screenshots](docs/screenshots.md) · [Documentation](docs/) · [Themes](docs/themes.md) · [Accessibility](docs/accessibility.md) · [Keyboard shortcuts](docs/keyboard-shortcuts.md) · [Building](docs/building.md)

## Build from source

```bash
git clone https://github.com/dotnetappdev/Pdfedit.git
cd Pdfedit
dotnet run --project PdfEdit
```

Needs the .NET 10 SDK. Bugs and ideas go in [issues](https://github.com/dotnetappdev/Pdfedit/issues); pull requests go to the `devmain` branch.

## License

[MIT](LICENSE). PDF processing uses [iText 7](https://itextpdf.com/), which is licensed under the AGPL.
