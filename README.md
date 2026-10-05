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

PdfEdit fills in and signs forms, marks up documents, rearranges pages and builds new PDFs, all in a native Windows app with an Office-style ribbon and a tab for every open file. Everything is included and nothing is locked behind an upgrade. It works offline and doesn't need an account.

![PdfEdit filling in a form](docs/screenshots/demo-v4.gif)

## Key features

- **Fill & Sign**: type into any form, even flat PDFs with no fields, and add signatures, dates, ticks and stamps. Sign with a certificate too.
- **Forms**: create fields by hand or let Detect Fields find them, with number formats and totals that add themselves up.
- **Review**: highlights, sticky notes, drawing, links, Acrobat's standard stamps, redaction and visual compare.
- **Pages**: rotate, reorder, merge, split, watermark, number, password-protect and clean up scans.
- **Import and export**: open Word files (with or without Office), Excel, PowerPoint and Google Docs; save to Word, Excel or PowerPoint.
- **Cloud**: import from and save to Google Drive or OneDrive with your own app details.
- **Automation**: batch-process a folder, fill a form from every row of a spreadsheet, search a folder of PDFs.
- **Themes**: follows your Windows light, dark or contrast theme, or pick Office, Dracula, Nord, One Dark, Monokai, Solarized or GitHub Light.
- **Accessibility**: adjustable text and icon sizes, screen reader announcements, and a built-in voice that reads out what you're doing.
- **Optional AI**: ask about the document or any passage, get an outline or a translated copy, and let it fill fields or run commands. Use your own Claude or OpenAI key, or a free local model.

![Filling in and signing a flat PDF form in Live View](docs/screenshots/fill-and-sign.gif)

See the [documentation](docs/) for the full list and [screenshots](docs/screenshots.md) of every tool.

## Quick start

Download the installer or the portable zip from [Releases](https://github.com/dotnetappdev/Pdfedit/releases/latest). Both include everything you need. It runs on Windows 10 (2004 or later) and 11, 64-bit.

To build from source with the .NET 10 SDK:

```bash
git clone https://github.com/dotnetappdev/Pdfedit.git
cd Pdfedit
dotnet run --project PdfEdit
```

## Resources

- [Documentation](docs/)
- [Building and releases](docs/building.md)
- [Import, export and cloud](docs/import-export-and-cloud.md)
- [Automation and tools](docs/automation.md)
- [Themes](docs/themes.md)
- [Accessibility](docs/accessibility.md)
- [Keyboard shortcuts](docs/keyboard-shortcuts.md)

## Support and contributing

Found a bug or have an idea? Open an [issue](https://github.com/dotnetappdev/Pdfedit/issues). Pull requests are welcome; development happens on the `devmain` branch.

## License

PdfEdit is [MIT licensed](LICENSE). It uses [iText 7](https://itextpdf.com/) for PDF processing, which is licensed under the AGPL.
