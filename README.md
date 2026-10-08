<p align="center">
  <img src="docs/logo.svg" width="80" alt="PdfEdit logo">
</p>

<h1 align="center">PdfEdit</h1>

<p align="center">A free PDF editor for Windows, built to rival Adobe Acrobat. No ads, no subscriptions, no paywalls.</p>

<p align="center">
  <a href="https://github.com/dotnetappdev/Pdfedit/releases/latest"><img src="https://img.shields.io/github/v/release/dotnetappdev/Pdfedit?display_name=tag&label=release" alt="Latest release"></a>
  <a href="https://github.com/dotnetappdev/Pdfedit/actions/workflows/ci.yml?query=branch%3Adevmain"><img src="https://img.shields.io/github/actions/workflow/status/dotnetappdev/Pdfedit/ci.yml?branch=devmain&label=build" alt="Build status"></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Mac%20%7C%20web-0078D6" alt="Windows, Mac and web">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT licence"></a>
  <a href="https://dotnetappdev.github.io/Pdfedit/docs/index.html"><img src="https://img.shields.io/badge/docs-user%20guide-1565D8" alt="User guide"></a>
</p>

<p align="center">
  <a href="https://dotnetappdev.github.io/Pdfedit/"><b>Website</b></a> |
  <a href="https://dotnetappdev.github.io/Pdfedit/download.html"><b>Download</b></a> |
  <a href="https://dotnetappdev.github.io/Pdfedit/docs/index.html"><b>User guide</b></a> |
  <a href="https://dotnetappdev.github.io/Pdfedit/tutorials/index.html"><b>Tutorials</b></a> |
  <a href="https://dotnetappdev.github.io/Pdfedit/whats-new.html"><b>What's new</b></a>
</p>

## Why PdfEdit?

You open a form, click to type into it, and get asked to upgrade. You want to merge two files, and that's a paid feature. A "free" editor turns out to watermark every page, or cap you at three documents a day. Acrobat Reader lets you look at a PDF, but doing almost anything with it means a monthly subscription.

PdfEdit is the PDF editor I wanted instead: everything included, nothing locked, no account, no ads and no watermarks. It works offline and your files stay on your PC.

## What it can do

Fill in and sign any form, even a flat scan with no fields, and send it back. Highlight, comment, stamp and redact, with your own stamps alongside Acrobat's. Move, resize, replace or delete the pictures already in a PDF, or snapshot any area as an image. Rotate, reorder, merge, split and resize pages, convert to greyscale, and print several pages per sheet or as a folded booklet. Scan straight to a searchable PDF and turn Word documents into fillable forms. Start from one of 70 templates (invoices, quotes, fillable forms, agreements, letters, resumes, flyers, certificates, calendars and planners) in a gallery like Office's, then customise it or open it as a PDF to fill in.

Open Word, Excel, PowerPoint, text, Markdown and HTML files as PDFs, and save PDFs as Word, Excel, PowerPoint, HTML, Markdown or ePub. Batch-process a folder, fill a form from every row of a spreadsheet, and save to Google Drive or OneDrive. Read hands-free with read aloud and auto-scroll, and set it up the way you need: text sizes, a high-contrast focus ring, reduced motion and a voice that reads things out. There's an optional AI assistant if you want one, and it can run on a free local model.

<p align="center"><a href="docs/tour.md"><img src="docs/tour/tour-poster.png" width="640" alt="Watch the PdfEdit tour"></a></p>

**[Download](https://github.com/dotnetappdev/Pdfedit/releases/latest)** for Windows 10 (2004+) and 11, 64-bit: installer or portable zip.

**[Features](docs/features.md)** | [Video tour](docs/tour.md) | [Screenshots](docs/screenshots.md) | [Documentation](docs/) | [Themes](docs/themes.md) | [Web version (Blazor)](#blazor-web-version) | [Accessibility](docs/accessibility.md) | [Keyboard shortcuts](docs/keyboard-shortcuts.md) | [Building](docs/building.md)

## Screenshots

### Blazor web version

**PdfEdit.Blazor** is PdfEdit in the browser: a Blazor Web App (.NET 10) with the same ribbon
(the same nine tabs, groups and commands, everything that can work in a browser), panels, All tools
pane and themes as the Windows app, running PdfEdit.Core and the Pdfium renderer on the
server. Fill and sign forms, prepare and design forms, stamps, drawing and measuring, OCR,
compare, certificate signing, the AI assistant and Translate PDF, batch processing and bulk fill,
Google Drive and OneDrive, Office files to PDF, several documents open at once, and the
floating toolbox beside the pages.
How to run it and what it needs on the server: [PdfEdit.Blazor/README.md](PdfEdit.Blazor/README.md).

**Drafts: reloading the page doesn't lose your work.** Open documents, things placed on them but not applied yet, field values and the Design canvas are kept in the browser as you go (and at once with Save Draft), and come back after a reload; drafts from a closed window wait on the start page.

**Templates: 70 to start from, in a gallery like Office's (File > New)**

![The template gallery](docs/screenshots/blazor/templates-gallery.png)

![A template opened in the preview, ready to customise or open as a PDF](docs/screenshots/blazor/templates-preview.png)

**The start page: recent files and featured templates**

![The start page](docs/screenshots/blazor/start-page.png)

**Everything you add has the Windows app's floating toolbar: smaller, larger, delete, rotate, spacing, fit and colours**

![The floating toolbar over selected text, with character spacing open](docs/screenshots/blazor/item-toolbar.png)

**Dates change format, day, month and year in place**

![The date format popover](docs/screenshots/blazor/date-format.png)

**Right-click anything on the page**

![The right-click menu on a stamp](docs/screenshots/blazor/right-click-menu.png)

**Properties for the selected item, with Lock**

![Properties of the selected text](docs/screenshots/blazor/properties-panel.png)

**Comments: review status, checkmarks, notes and replies, saved in the PDF**

![The Comments panel with a review thread](docs/screenshots/blazor/comments-panel.png)

**Thumbnails: rotate and delete on hover, a right-click menu, and drag to reorder**

![The thumbnail menu](docs/screenshots/blazor/thumbnail-menu.png)

**Edit Fields: align, space and size fields from the right-click menu**

![The field menu in Edit Fields](docs/screenshots/blazor/field-menu.png)

**The File menu follows the dark theme**

![The File menu in the dark theme](docs/screenshots/blazor/file-menu-dark.png)

**The Tools tab and the floating toolbox: highlight, underline, strikethrough, rectangle, arrow, cloud, callout, sticky note, stamp and a measurement on a page**

![The Tools tab with comments and markup placed on the page](docs/screenshots/blazor/tools-tab.png)

**Floating toolbox: Draw tools with ink colours**

![Floating toolbox with the Draw flyout open](docs/screenshots/blazor/toolbox-draw.png)

**Comment tools: sticky note, callout, insert and replace text, stamps**

![The Comment flyout](docs/screenshots/blazor/toolbox-comment.png)

**Highlight tools: highlight, underline, squiggly, strikethrough, with colours**

![The Highlight flyout](docs/screenshots/blazor/toolbox-highlight.png)

**Sign: saved signatures and initials, or sign with a certificate**

![The Sign flyout with a saved signature and initials](docs/screenshots/blazor/toolbox-sign.png)

**Signatures and pictures stay editable after Apply changes, saving or reopening**

![A signature applied to the PDF, selected again to move, resize or delete](docs/screenshots/blazor/signature-after-save.png)

**More tools: measure, navigate, fill, shapes and form fields**

![The More flyout](docs/screenshots/blazor/toolbox-more.png)

**Toolbox tools on a page: callout, insert and replace text, highlights, clouds, polygons, marks, vertical text and initials**

![Callouts, text edits, markup, clouds, polygons, marks and initials placed with the toolbox](docs/screenshots/blazor/toolbox-tools.png)

**Design toolbox with the grid on, and an invoice template**

![The design toolbox with the grid turned on](docs/screenshots/blazor/toolbox-design.png)

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

**All tools pane, docked on the left beside the thumbnails as in the Windows app (Dracula theme)**

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

**Fill in and sign forms (Light theme)**

![Fill in and sign forms (Light theme)](docs/screenshots/blazor/theme-light.png)

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

**Translate PDF: before**

![Translate PDF: before](docs/screenshots/blazor/translate-before.png)

**Translate PDF: after, with the layout and colours kept**

![Translate PDF: after, with the layout and colours kept](docs/screenshots/blazor/translate-after.png)

### Cross-platform desktop (Avalonia)

**PdfEdit.Avalonia** runs the web version inside a desktop window on Windows, macOS and Linux, with the same ribbon,
tools and drafts, with the system's Save dialog, your PDF viewer for printing, and PDFs opened from the
command line or Open with. The program is called PdfEdit, like the Windows app. It hosts the web app in its own process on
a private local address that only its window can use, and shows it in WebView2, WebKit or WebKitGTK
([how to build and run it](docs/building.md#the-cross-platform-desktop-app-windows-macos-linux)).

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

**Help > Check for Updates** (on the Help tab, or File > Help) looks at the [GitHub releases](https://github.com/dotnetappdev/pdfedit/releases) for a newer version. It picks the download that matches how PdfEdit was installed (setup EXE, portable ZIP or MSIX), lets you choose where to save it, shows a progress bar while it downloads, and checks the file against its SHA-256. Then it installs the update. By default it closes PdfEdit (and any other PdfEdit windows) first and starts it again afterwards. Your unsaved work is kept, as it is on any normal close. Each time PdfEdit starts it also checks for a new version. If there is one, it shows the version and a link to it on GitHub, with **See release notes**, and asks **Yes, update** / **No** (skip this version) / **Cancel** (ask again next time). **Help > About PdfEdit** has a **Check for Updates...** button too. You can turn the startup check off, or include pre-releases, in the update window. The setup program checks for a version that's already installed and uninstalls it first, then installs the new one in the same folder. Your settings, signatures, stamps and recent files are kept.

## Build from source

```bash
git clone https://github.com/dotnetappdev/Pdfedit.git
cd Pdfedit
dotnet run --project PdfEdit
```

The cross-platform desktop app (Windows, macOS and Linux) is `dotnet run --project PdfEdit.Avalonia`: an
Avalonia window showing the web version in the system's web view. See [Building](docs/building.md).

Needs the .NET 10 SDK. Bugs and ideas go in [issues](https://github.com/dotnetappdev/Pdfedit/issues); pull requests go to the `devmain` branch.

## License

[MIT](LICENSE). PDF processing uses [iText 7](https://itextpdf.com/), which is licensed under the AGPL.
