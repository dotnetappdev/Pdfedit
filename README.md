# PdfEdit — WPF PDF Form Filler & Editor

A native WPF application for viewing, editing, and filling PDF forms — styled after Adobe Acrobat Pro with a modern Fluent ribbon, AI-powered document analysis, full page management, and live theme switching.

## Screenshots

### Dark Theme
![Dark Theme](docs/screenshots/dark-theme.png)

### Light Theme (IRS W-4)
![Light Theme](docs/screenshots/light-theme.png)

### Document AI — Smart Fill, Summarize, Contract Analysis, Find PII
![AI Features](docs/screenshots/ai-features.png)

### AI Assistant & Model Picker
![AI Connections](docs/screenshots/ai-connections.png)

### Page Management & Thumbnails
![Page Management](docs/screenshots/page-management.png)

### Themed Dialogs & Toast Notifications
![Dialogs](docs/screenshots/dialogs.png)

---

## Features

### PDF Form Filling
- **Visual Form Overlay** — Click any AcroForm field directly on the rendered page to fill it in-place
- **All Field Types** — Text, checkboxes, radio buttons, combo boxes, list boxes, signature fields, and **password fields** (masked input via `PasswordBox`)
- **Existing AcroFields** — Open any PDF with pre-placed form fields and fill them immediately; field positions are read from the PDF
- **Field Highlights** — Blue = optional, red = required, blue border = focused
- **Clear All Fields** — Reset all field values in one click
- **Delete Field** — Remove individual AcroForm fields; stripped from the PDF on save
- **Import / Export** — Save and reload all field values as TSV files
- **Flatten & Save** — Bake filled values into a non-editable PDF

### AI-Powered Document Analysis
- **PDF Text Extraction** — Full document text extracted automatically on open (via iText7); used as AI context
- **Document Context Badge** — Green "📄 Doc context" indicator confirms the AI has read the PDF
- **Smart Fill** — AI reads the entire PDF and fills all form fields from the document content — no prompting needed
- **Summarize** — Structured summary: overview, key parties, key dates, key amounts, main points
- **Extract Key Data** — All names, dates, addresses, amounts, reference numbers, and contact info in a clean list
- **Contract Analysis** — Parties, obligations, payment terms, termination, risks, and missing standard clauses
- **Find PII** — Locate all personally identifiable information (names, IDs, financial data) for redaction review
- **Translate** — Translate document content to English or another target language
- **AI Chat with Document Context** — Ask any question; answers are grounded in the actual PDF text
- **Multi-Provider** — Connect **Anthropic (Claude)** or **OpenAI (ChatGPT)** with your own API key
- **Model Selection** — Claude Haiku 4.5 (fast) · Sonnet 5 (recommended) · Opus 5 (powerful) · GPT-4o mini · GPT-4o
- **Cancel** — Stop any in-progress AI response instantly
- **User Profiles** — Save named sets of personal data and fill matching fields with **Quick Fill** (no AI needed)
- **Secure Key Storage** — API keys stored locally in `%AppData%\PdfEdit\settings.json`; **passwords are never stored**

### Free-Text Annotations
- **Add Text Anywhere** — Place a free-text annotation at any position on any page
- **Rich Formatting** — Font family, size, bold, italic, underline, colour, and text alignment per annotation
- **Vertical Text** — Rotate annotations −90° for vertical labels
- **Force Uppercase** — Optional uppercase mode for all-caps fields
- **Delete Annotation** — Remove individual annotations before saving

### Signatures
- **Signature Library** — Draw, type, or load signature images; preview thumbnails in the picker
- **Place Signature** — Click to place a saved signature anywhere on a page
- **Remove Signature** — Delete placed signatures before saving

### Page Management
- **Rotate Page** — Clockwise or counter-clockwise; status bar shows absolute rotation (PDF-stored + session delta)
- **Rotate All Pages** — Apply a 90° rotation to every page at once
- **Move Page Up / Down** — Reorder pages via ribbon buttons
- **Insert Page Before** — Insert a blank page before the current page
- **Delete Page** — Remove the current page (disabled on single-page documents)
- **Extract Page** — Save the current page as a standalone PDF
- **Merge PDF** — Append one or more PDFs to the end of the current document
- **Insert PDF** — Insert another PDF at a chosen position: beginning, before/after the current page, or end of document
- **Split PDF** — Split every page into individual files (saved to `{name}_split/`)
- **Drag-and-Drop Reorder** — Drag thumbnails to reorder pages; PDF is rewritten automatically
- **Right-Click Thumbnails** — Context menu: Rotate CW/CCW, Move Up/Down, Insert Before/After, Delete, Extract

### Navigation & Viewing
- **Page Thumbnails** — Toggle the left thumbnail panel; click to jump to any page
- **Zoom** — Ctrl+Scroll, ribbon buttons, fit-to-window, and actual-size shortcuts
- **Page Navigation** — First, Previous, Next, Last buttons; type a page number directly
- **Recent Files** — File menu lists recently opened PDFs for quick re-open
- **Document State** — Last page and zoom level are remembered per file and restored on re-open
- **Search** — Global search across field names, field values, and annotation text; click a result to jump to it
- **Print** — Send the current page to the system print dialog

### UI & Themes
- **Three Live Themes** — Dark, Light, and High Contrast; switch from the ribbon with no restart
- **Fluent Ribbon** — Home, View, Forms, Tools, and AI Assistant tabs
- **Dockable Panels** — Properties, page thumbnails, and AI Chat via AvalonDock
- **Toast Notifications** — Success, info, warning, and error toasts with auto-dismiss
- **Themed Dialogs** — Error (expandable stack trace + Copy Details), Confirm (danger mode), and Info — all styled to match the active theme
- **UI Scale** — Increase/decrease the overall interface scale (independent of zoom)
- **Drag & Drop to Open** — Drag a PDF file onto the window to open it

### Sample PDFs
The `Samples/` folder includes:
- `all-field-types.pdf` — Exercises every AcroForm field type (text, checkbox, radio, combo, listbox)
- `irs-w4.pdf` — Real IRS W-4 form for realistic testing

---

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

Or open `PdfEdit.sln` in Visual Studio 2022 and press **F5**.

## Running Tests

```
cd PdfEdit.Tests
dotnet test
```

---

## Usage

1. **Open a PDF** — `File → Open` (Ctrl+O) or drag a PDF onto the window
2. **Fill fields** — Click any form field on the rendered page and type / select a value
3. **Smart Fill** — Go to the **AI Assistant** tab → click **Smart Fill** to let AI fill all fields from the document text
4. **AI Chat** — Open the AI panel, connect an API key via ⚙, then use presets (Summarize, Contract, Find PII…) or type a question
5. **Annotate** — Select the **Add Text** tool and click anywhere to place a free-text annotation
6. **Navigate pages** — Ribbon navigation buttons, click thumbnails, or type a page number
7. **Rotate pages** — Home → Rotate Page CW/CCW, or right-click a thumbnail
8. **Reorder pages** — Drag thumbnails, or use Move Up / Move Down in the ribbon
9. **Split PDF** — Home → Split PDF (saves each page as a separate file)
10. **Switch theme** — View → Theme → Dark / Light / High Contrast
11. **Save** — `Ctrl+S` keeps fields editable; **Flatten & Save** bakes them in permanently
12. **Export data** — Forms → Export Data (TSV)
13. **Import data** — Forms → Import Data (TSV bulk-fill)

---

## Architecture

| Layer | Description |
|-------|-------------|
| `Services/PdfRenderService` | Renders pages to `BitmapSource` via `Windows.Data.Pdf` |
| `Services/PdfFormService` | Reads/writes AcroForm fields, splits, merges, reorders, rotates, and inserts pages via iText 7 |
| `Services/PdfTextExtractorService` | Extracts text from PDF pages using iText7; cached per page for AI context |
| `Services/AiProviderService` | Streaming HTTP client for Claude and OpenAI; document analysis prompt builder |
| `Services/ToastService` | Singleton event-based toast notification bus |
| `Services/AppSettings` | Loads/saves `%AppData%\PdfEdit\settings.json` (API keys, theme, recent files, UI scale) |
| `Services/PersonalProfileStore` | Profile storage and keyword-based field matching for Quick Fill |
| `ViewModels/MainViewModel` | MVVM view model — all commands, page state, rotation, field values, AI orchestration |
| `Controls/PdfViewerControl` | Renders the page image and overlays live form controls (TextBox, PasswordBox, CheckBox, …) |
| `Controls/PageThumbnailsPanel` | Left thumbnail strip with drag-and-drop reorder and right-click context menu |
| `Controls/AiChatPanel` | AI chat UI — model picker, provider tabs, preset chips, streaming chat, doc-context badge |
| `Controls/ToolboxPanel` | Right-side Fill & Sign tool selector with accent-bar active state |
| `Dialogs/AppDialog` | Static service replacing `MessageBox.Show` — `ShowError`, `ShowConfirm`, `ShowInfo` |
| `Dialogs/ThemedErrorDialog` | Error dialog with expandable stack trace and Copy Details button |
| `Dialogs/ThemedConfirmDialog` | Confirm dialog with optional danger mode (red button) |
| `Dialogs/ThemedInfoDialog` | Info dialog with accent icon |
| `Resources/AppTheme.xaml` | Shared styles using `DynamicResource` tokens for live theme switching |
| `Themes/DarkTheme.xaml` | Dark palette tokens |
| `Themes/LightTheme.xaml` | Light palette tokens |
| `Themes/HighContrastTheme.xaml` | High-contrast accessibility tokens |
| `Models/` | `FormFieldInfo`, `PdfDocumentInfo`, `AiChatMessage`, `PersonalProfile`, `FieldType`, `ActiveTool` |
| `PdfEdit.Tests/` | xUnit tests — field types, round-trips, rotation, stamps, settings |

---

## Key Packages

| Package | Use |
|---------|-----|
| `itext7` v8 | PDF AcroForm reading/writing, text extraction, page manipulation |
| `Fluent.Ribbon` v10 | Office-style ribbon toolbar |
| `AvalonDock` (Dirkster) v5 | Dockable panels layout |
| `Windows.Data.Pdf` (built-in) | High-quality PDF page rendering |
| `System.Text.Json` (built-in) | Settings and AI API serialisation |
| `xunit` v2 | Unit test framework |

---

## Security

- API keys are stored in `%AppData%\PdfEdit\settings.json` on the local machine only
- **Passwords are never stored** — password-type PDF fields use a `PasswordBox` (masked input); values are written only to the PDF at save time
- Keys are sent only to the respective AI provider (Anthropic / OpenAI); they are never shared with third parties
- The app makes no network requests other than to the selected AI provider's public API endpoints
