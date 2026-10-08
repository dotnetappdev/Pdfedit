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

Fill in and sign any form, even a flat scan with no fields, and send it back. Highlight, comment, stamp and redact, with your own stamps alongside Acrobat's. Move, resize, replace or delete the pictures already in a PDF, or snapshot any area as an image. Rotate, reorder, merge, split and resize pages, convert to greyscale, and print several pages per sheet or as a folded booklet. Scan straight to a searchable PDF and turn Word documents into fillable forms.

Open Word, Excel, PowerPoint, text, Markdown and HTML files as PDFs, and save PDFs as Word, Excel, PowerPoint, HTML, Markdown or ePub. Batch-process a folder, fill a form from every row of a spreadsheet, and save to Google Drive or OneDrive. Read hands-free with read aloud and auto-scroll, and set it up the way you need: text sizes, a high-contrast focus ring, reduced motion and a voice that reads things out. There's an optional AI assistant if you want one, and it can run on a free local model.

<p align="center"><a href="docs/tour.md"><img src="docs/tour/tour-poster.png" width="640" alt="Watch the PdfEdit tour"></a></p>

**[Download](https://github.com/dotnetappdev/Pdfedit/releases/latest)** for Windows 10 (2004+) and 11, 64-bit: installer or portable zip.

**[Features](docs/features.md)** · [Video tour](docs/tour.md) · [Screenshots](docs/screenshots.md) · [Documentation](docs/) · [Themes](docs/themes.md) · [Web version (Blazor)](#blazor-web-version) · [Accessibility](docs/accessibility.md) · [Keyboard shortcuts](docs/keyboard-shortcuts.md) · [Building](docs/building.md)

## Screenshots

### Blazor web version

**PdfEdit.Blazor** is PdfEdit in the browser: a Blazor Web App (.NET 10) with the same ribbon
(the same nine tabs, groups and commands — everything that can work in a browser), panels, All tools
pane and themes as the Windows app, running PdfEdit.Core and the Pdfium renderer on the
server. Fill and sign forms, prepare and design forms, stamps, drawing and measuring, OCR,
compare, certificate signing, the AI assistant and Translate PDF, batch processing and bulk fill,
Google Drive and OneDrive, Office files to PDF, several documents open at once, and the
floating toolbox beside the pages.
How to run it and what it needs on the server: [PdfEdit.Blazor/README.md](PdfEdit.Blazor/README.md).

**The ribbon, tab by tab, as in the Windows app**

![Home tab](docs/screenshots/blazor/ribbon-home.png)
![Fill & Sign tab](docs/screenshots/blazor/ribbon-fill-sign.png)
![Edit tab](docs/screenshots/blazor/ribbon-edit.png)
![View tab](docs/screenshots/blazor/ribbon-view.png)
![Tools tab](docs/screenshots/blazor/ribbon-tools.png)
![AI Assistant tab](docs/screenshots/blazor/ribbon-ai.png)
![Design tab](docs/screenshots/blazor/ribbon-design.png)

**A narrower window: groups fold into drop-downs, like the Fluent ribbon**

![A collapsed ribbon group dropped down](docs/screenshots/blazor/ribbon-collapsed.png)

**All tools pane, in the Dracula theme**

![All tools pane in the Dracula theme](docs/screenshots/blazor/all-tools-dracula.png)

**Office theme**

![Office theme](docs/screenshots/blazor/theme-office.png)

**Select Text: copy, highlight or ask the AI about it**

![Select Text](docs/screenshots/blazor/select-text.png)

**Edit Images: move, resize, replace, save or delete a picture in the PDF**

![Edit Images](docs/screenshots/blazor/edit-images.png)

**Mind Map of the document (AI Assistant)**

![Mind map](docs/screenshots/blazor/mind-map.png)

**Design templates (Invoice)**

![Invoice template on the design canvas](docs/screenshots/blazor/design-invoice.png)

**Take the Tour**

![The tour](docs/screenshots/blazor/tour.png)

**Floating toolbox: Draw tools with ink colours**

![Floating toolbox with the Draw flyout open](docs/screenshots/blazor/toolbox-draw.png)

**Toolbox tools on a page: callout, insert and replace text, highlights, clouds, polygons, marks, vertical text and initials**

![Callouts, text edits, markup, clouds, polygons, marks and initials placed with the toolbox](docs/screenshots/blazor/toolbox-tools.png)

**Saved signatures and initials**

![The Sign flyout with a saved signature and initials](docs/screenshots/blazor/toolbox-sign.png)

**More tools: measure, navigate, fill, shapes and form fields**

![The More flyout](docs/screenshots/blazor/toolbox-more.png)

**Design toolbox with grid and snap**

![The design toolbox with the grid turned on](docs/screenshots/blazor/toolbox-design.png)

**Fill in and sign forms — Light theme**

![Fill in and sign forms — Light theme](docs/screenshots/blazor/theme-light.png)

**Dark theme**

![Dark theme](docs/screenshots/blazor/theme-dark.png)

**High Contrast theme**

![High Contrast theme](docs/screenshots/blazor/theme-high-contrast.png)

**Stamps, freehand drawing, arrows and measuring**

![Stamps, freehand drawing, arrows and measuring](docs/screenshots/blazor/draw-measure.png)

**Design a fillable form on the design canvas**

![Design a fillable form on the design canvas](docs/screenshots/blazor/design.png)

**Text and stamps placed upright on a rotated page, with several documents open as tabs**

![Text and stamps placed upright on a rotated page, with several documents open as tabs](docs/screenshots/blazor/rotated-page.png)

**Batch Process: OCR, watermark, Bates numbers and compress over many PDFs**

![Batch Process: OCR, watermark, Bates numbers and compress over many PDFs](docs/screenshots/blazor/batch.png)

**Bulk Fill: one filled form per spreadsheet row**

![Bulk Fill: one filled form per spreadsheet row](docs/screenshots/blazor/bulk-fill.png)

**Open from and save to Google Drive and OneDrive**

![Open from and save to Google Drive and OneDrive](docs/screenshots/blazor/cloud.png)

**Translate PDF — before**

![Translate PDF — before](docs/screenshots/blazor/translate-before.png)

**Translate PDF — after: the layout and colours are kept**

![Translate PDF — after: the layout and colours are kept](docs/screenshots/blazor/translate-after.png)

### Desktop (Windows) version

![Filling in and signing a flat PDF form](docs/screenshots/fill-and-sign.gif)

<table>
  <tr>
    <td width="50%"><img src="docs/screenshots/live-view.png" alt="Highlights, a sticky note, a stamp and a signature"></td>
    <td width="50%"><img src="docs/screenshots/ai-features.png" alt="The AI assistant after Smart Fill"></td>
  </tr>
</table>

#### Desktop themes

PdfEdit follows your Windows light, dark or contrast theme, or you can pick one of twelve. [More about themes](docs/themes.md).

<table>
  <tr>
    <td align="center"><img src="docs/screenshots/theme-light.png" alt="PdfEdit in the Light theme"><br>Light</td>
    <td align="center"><img src="docs/screenshots/theme-dark.png" alt="PdfEdit in the Dark theme"><br>Dark</td>
    <td align="center"><img src="docs/screenshots/theme-high-contrast.png" alt="PdfEdit in the High contrast theme"><br>High contrast</td>
  </tr>
  <tr>
    <td align="center"><img src="docs/screenshots/theme-office.png" alt="PdfEdit in the Office theme"><br>Office</td>
    <td align="center"><img src="docs/screenshots/theme-dracula.png" alt="PdfEdit in the Dracula theme"><br>Dracula</td>
    <td align="center"><img src="docs/screenshots/theme-nord.png" alt="PdfEdit in the Nord theme"><br>Nord</td>
  </tr>
</table>

<table>
  <tr>
    <td width="66%"><img src="docs/screenshots/themes.png" alt="All twelve themes"><br><p align="center">All twelve themes</p></td>
    <td width="34%"><img src="docs/screenshots/settings-accessibility.png" alt="Settings, Accessibility tab"><br><p align="center"><a href="docs/accessibility.md">Accessibility settings</a></p></td>
  </tr>
</table>

More in the [screenshot gallery](docs/screenshots.md).

## Updates

**Help → Check for Updates** (on the Help tab, or File → Help) looks at the [GitHub releases](https://github.com/dotnetappdev/pdfedit/releases) for a newer version. It picks the download that matches how PdfEdit was installed (setup EXE, portable ZIP or MSIX), lets you choose where to save it, shows a progress bar while it downloads, and checks the file against its SHA-256. Then it installs the update. By default it closes PdfEdit (and any other PdfEdit windows) first and starts it again afterwards. Your unsaved work is kept, as it is on any normal close. Each time PdfEdit starts it also checks for a new version. If there is one, it shows the version and a link to it on GitHub, with **See release notes**, and asks **Yes, update** / **No** (skip this version) / **Cancel** (ask again next time). **Help → About PdfEdit** has a **Check for Updates…** button too. You can turn the startup check off, or include pre-releases, in the update window. The setup program checks for a version that's already installed and uninstalls it first, then installs the new one in the same folder. Your settings, signatures, stamps and recent files are kept.

## Build from source

```bash
git clone https://github.com/dotnetappdev/Pdfedit.git
cd Pdfedit
dotnet run --project PdfEdit
```

Needs the .NET 10 SDK. Bugs and ideas go in [issues](https://github.com/dotnetappdev/Pdfedit/issues); pull requests go to the `devmain` branch.

## License

[MIT](LICENSE). PDF processing uses [iText 7](https://itextpdf.com/), which is licensed under the AGPL.
