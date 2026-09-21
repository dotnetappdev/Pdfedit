# PdfEdit — WPF PDF Form Filler

A native WPF application for filling PDF forms visually, styled after Adobe Acrobat with a Fluent ribbon toolbar, toolbox panel, and properties pane.

## Features

- **Fluent Ribbon Toolbar** — Home, View, Forms, and Tools tabs with all key operations
- **Toolbox Panel** — Hand, Select, Text Fill, Checkbox, Signature, Highlight, and Zoom tools
- **Visual PDF Viewer** — Renders PDF pages via Windows.Data.Pdf (built-in Windows PDF engine)
- **Native Form Filling** — Reads and writes PDF AcroForm fields using iText7
  - Text fields, checkboxes, radio buttons, combo boxes, list boxes, signature fields
  - Field highlight overlay (blue = optional, red = required)
- **Properties Panel** — Shows selected field details; lists all fields in the document
- **Drag & Drop** — Drag a PDF file onto the window to open it
- **Import / Export** — Save and reload form data as TSV files
- **Flatten & Save** — Bake form values into a non-editable PDF

## Requirements

- Windows 10 (build 19041+) or Windows 11
- .NET 8 SDK (or Visual Studio 2022 17.8+)

## Building

```
cd PdfEdit
dotnet restore
dotnet build -c Release
dotnet run
```

Or open `PdfEdit.sln` in Visual Studio 2022 and press F5.

## Usage

1. **Open a PDF** — `File → Open` (Ctrl+O) or drag a PDF onto the window
2. **Fill fields** — Click any form field on the page and type / select a value
3. **Navigate pages** — Use the ribbon navigation buttons or Ctrl+Left / Ctrl+Right
4. **Zoom** — Ctrl+Mouse Wheel, or use the ribbon zoom controls
5. **Save** — `Ctrl+S` saves with form fields still editable
6. **Flatten & Save** — Saves a flat (non-editable) version of the filled PDF
7. **Export data** — Forms → Export Data to save all field values to a TSV file
8. **Import data** — Forms → Import Data to bulk-fill fields from a TSV file

## Architecture

| Layer | Description |
|-------|-------------|
| `Services/PdfRenderService` | Renders pages to `BitmapSource` via Windows.Data.Pdf |
| `Services/PdfFormService` | Reads/writes AcroForm fields via iText7 |
| `ViewModels/MainViewModel` | MVVM view model; all commands and state |
| `Controls/PdfViewerControl` | Renders the page image and overlays live form controls |
| `Controls/ToolboxPanel` | Left-side tool selector |
| `Models/` | `FormFieldInfo`, `PdfDocumentInfo`, `FieldType`, `ActiveTool` |

## Key Packages

| Package | Use |
|---------|-----|
| `itext7` | PDF AcroForm reading and writing |
| `Fluent.Ribbon` | Office-style ribbon toolbar |
| `Windows.Data.Pdf` (built-in) | High-quality PDF page rendering |
