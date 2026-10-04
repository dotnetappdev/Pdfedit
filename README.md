# PdfEdit

[![CI](https://img.shields.io/github/actions/workflow/status/dotnetappdev/Pdfedit/ci.yml?branch=devmain&label=CI)](https://github.com/dotnetappdev/Pdfedit/actions/workflows/ci.yml?query=branch%3Adevmain)
[![Latest release](https://img.shields.io/github/v/release/dotnetappdev/Pdfedit?display_name=tag&label=release)](https://github.com/dotnetappdev/Pdfedit/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Free PDF editor for Windows: fill in and sign forms, mark up documents, and move pages around. See the [docs](docs/) for everything it does.

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
