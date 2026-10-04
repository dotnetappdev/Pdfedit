# PdfEdit

[![CI](https://img.shields.io/github/actions/workflow/status/dotnetappdev/Pdfedit/ci.yml?branch=devmain&label=CI)](https://github.com/dotnetappdev/Pdfedit/actions/workflows/ci.yml?query=branch%3Adevmain)
[![Latest release](https://img.shields.io/github/v/release/dotnetappdev/Pdfedit?display_name=tag&label=release)](https://github.com/dotnetappdev/Pdfedit/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A free PDF editor for Windows, built to rival Adobe Acrobat without the ads, subscriptions or paywalls. Everything is included and nothing is locked behind an upgrade.

Fill in and sign forms (even flat ones with no fields), build your own forms, add comments, stamps and links, rearrange pages, watermark, scan with OCR and design documents from scratch. If you want it, there's optional AI form filling with your own key or a local model. It runs offline and doesn't need an account. See the [docs](docs/) for the details.

![PdfEdit filling in a form](docs/screenshots/demo-v4.gif)

## Download

Get it from [Releases](https://github.com/dotnetappdev/Pdfedit/releases/latest). The setup exe and the `-portable` zip include everything; the plain zip needs the .NET 10 Desktop Runtime. Windows 10 (2004+) or 11, 64-bit.

## Building

```
dotnet run --project PdfEdit
```

Needs the .NET 10 SDK on Windows. See [docs/building.md](docs/building.md) for tests and installers.

## License

MIT. PDF processing uses [iText 7](https://itextpdf.com/), which is AGPL.
