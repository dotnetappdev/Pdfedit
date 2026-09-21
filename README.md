# PdfEdit — WPF PDF Form Filler

A native WPF application for filling PDF forms visually, styled after Adobe Acrobat Pro with a modern Fluent ribbon toolbar, right-side Fill & Sign toolbox, and properties pane.

## Screenshots

### Dark Theme
![Dark Theme](docs/screenshots/dark-theme.png)

### Light Theme
![Light Theme](docs/screenshots/light-theme.png)

## Features

- **Modern Adobe-Style UI** — Deep dark palette, sharp `#0A84FF` accent, 2px left-bar active-tool indicator (Acrobat parity), slim 8px scrollbars, underline-only tab selector
- **Three Themes** — Dark, Light, and High Contrast; live-switchable from the ribbon with no restart
- **Fluent Ribbon Toolbar** — Home, View, Forms, and Tools tabs with all key operations
- **Fill & Sign Toolbox** — Right-side panel with Navigate, Fill In Form, Add Text, Sign, Markup, and Comment sections; accent-bar highlights the active tool
- **Visual PDF Viewer** — Renders PDF pages via `Windows.Data.Pdf` (built-in Windows PDF engine)
- **Native Form Filling** — Reads and writes PDF AcroForm fields using iText 7
  - Text fields, checkboxes, radio buttons, combo boxes, list boxes, signature fields
  - Field highlight overlay (blue = optional, red = required, blue border = focused)
- **Arbitrary Text Rotation** — Free-text annotations support any rotation angle
- **Properties Panel** — Shows selected field details; lists all fields in the document with tab views (Properties / Page Fields / AI Chat)
- **Drag & Drop** — Drag a PDF file onto the window to open it
- **Import / Export** — Save and reload form data as TSV files
- **Flatten & Save** — Bake form values into a non-editable PDF
- **Signature Thumbnails** — Visual signature picker with custom drawn previews
- **xUnit Test Coverage** — 30+ tests covering all field types, value round-trips, rotation, stamps, and settings

## Requirements

- Windows 10 (build 19041+) or Windows 11
- .NET 10 SDK (or Visual Studio 2022 17.12+)

## Building

```
cd PdfEdit
dotnet restore
dotnet build -c Release
dotnet run
```

Or open `PdfEdit.sln` in Visual Studio 2022 and press F5.

## Running Tests

```
cd PdfEdit.Tests
dotnet test
```

## Usage

1. **Open a PDF** — `File → Open` (Ctrl+O) or drag a PDF onto the window
2. **Select a tool** — Click a tool in the Fill & Sign panel on the right
3. **Fill fields** — Click any form field on the page and type / select a value
4. **Navigate pages** — Use the ribbon navigation buttons or the Navigate section in the toolbox
5. **Zoom** — Ctrl+Mouse Wheel, or use the ribbon zoom controls
6. **Switch theme** — View → Theme → Dark / Light / High Contrast
7. **Save** — `Ctrl+S` saves with form fields still editable
8. **Flatten & Save** — Saves a flat (non-editable) version of the filled PDF
9. **Export data** — Forms → Export Data to save all field values to a TSV file
10. **Import data** — Forms → Import Data to bulk-fill fields from a TSV file

## Architecture

| Layer | Description |
|-------|-------------|
| `Services/PdfRenderService` | Renders pages to `BitmapSource` via `Windows.Data.Pdf` |
| `Services/PdfFormService` | Reads/writes AcroForm fields via iText 7 |
| `ViewModels/MainViewModel` | MVVM view model; all commands and state |
| `Controls/PdfViewerControl` | Renders the page image and overlays live form controls |
| `Controls/ToolboxPanel` | Right-side Fill & Sign tool selector with accent-bar active state |
| `Resources/AppTheme.xaml` | Shared styles (ScrollBar, TabItem, ListBox, buttons) using `DynamicResource` tokens |
| `Themes/DarkTheme.xaml` | Dark palette tokens (colors, brushes, font sizes) |
| `Themes/LightTheme.xaml` | Light palette tokens |
| `Themes/HighContrastTheme.xaml` | High-contrast accessibility tokens |
| `Models/` | `FormFieldInfo`, `PdfDocumentInfo`, `FieldType`, `ActiveTool` |
| `PdfEdit.Tests/` | xUnit tests — field types, round-trips, rotation, stamps, settings |

## Key Packages

| Package | Use |
|---------|-----|
| `itext7` v8 | PDF AcroForm reading and writing |
| `Fluent.Ribbon` v10 | Office-style ribbon toolbar |
| `AvalonDock` (Dirkster) v5 | Dockable panels layout |
| `Windows.Data.Pdf` (built-in) | High-quality PDF page rendering |
| `xunit` v2 | Unit test framework |
