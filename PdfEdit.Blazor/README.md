# PdfEdit for the web (Blazor)

The PdfEdit Windows app's window in the browser: a ribbon (File, Home, Fill & Sign, Edit, View,
Tools, Help), page thumbnails on the left, the pages in the middle, a side panel (Properties,
Fields, Comments, Bookmarks, Search) on the right and a blue status bar — in the same light and
dark colours. The PDF work runs on the server with the same libraries as the Windows app:
**PdfEdit.Core** (forms, annotations, page tools, export) and **PdfEdit.Render** (Pdfium).

```bash
dotnet run --project PdfEdit.Blazor
```

Then open the address it prints. With a checkout of the repository, the files in `Samples/` are
offered on the start page.

## What it can do

| Area | Features |
|---|---|
| File | Open (upload or samples), password-protected PDFs, New blank, Create from images/files, Save (download), Save As flattened / PDF/A / password-protected, Print, Export, Close |
| Fill & Sign | Fill every kind of form field, Add Text, Date, ticks, crosses and dots, draw or type a signature and place it, sticky notes, highlights, drag anything you've added to move it or its corner to resize it, works on rotated pages too (text, stamps and signatures are written upright), Apply Changes, Flatten & Download |
| Pages | Rotate, delete, insert blank before/after, duplicate, move up/down, extract or delete a range, split, merge PDFs and pictures, insert a PDF, export a page as an image |
| Document | Properties, watermark (add/remove), page numbers, header/footer, Bates numbers, compress, resize pages, pages per sheet, booklet, greyscale |
| Security | Password protect, remove hidden information, redaction (mark, then apply) |
| Edit | Undo/Redo for everything, export/import form data, reset form, check required fields, rectangles and ellipses |
| View | Zoom, fit width/page, thumbnails, side panel, bookmarks, comments, search with highlights, light/dark theme, statistics |
| Export | Word, Excel, PowerPoint, HTML, Markdown, ePub, text, page images, pictures |
| Prepare Form | Detect Fields on flat forms (named from their printed labels), add text, checkbox, radio, dropdown, list, date and signature fields by drawing them, select, drag to move, drag the corner to resize, Delete key, field properties (name, tooltip, required, read only, multi-line, alignment, font size, max characters, date and number formats) |
| Design | Live View / Design switch like the Windows app: design a form page from text, rectangles, ellipses, lines, arrows, tables, pictures, ticks and crosses, and text, multi-line, checkbox, radio, dropdown and signature fields; drag to move, drag the corner to resize, properties for each element, front/back, duplicate, delete, undo/redo; save and open .pdfdesign files (the same format as the Windows app), export a fillable PDF or open it straight in Live View |
| Scan & OCR | Recognise Text (OCR) adds an invisible text layer so scanned pages can be searched and copied (Tesseract on the server, any installed language), Scan with Camera turns phone or webcam photos — or pictures — into a PDF, optionally made searchable |
| Stamps, drawing & measuring | Stamps (Standard Business, Sign Here, Dynamic with your name and the time, and more) saved as real PDF stamps; freehand Draw with pen colour and width; lines and arrows; Measure distance, perimeter and area in inches, cm, mm or points with a live reading while you draw; stamps and drawings can be dragged to move or resized from the corner |
| Batch & bulk fill | Batch Process (Tools): run steps — OCR, compress, watermark, flatten, rotate, page numbers, header/footer, Bates numbers (continuing across files), remove hidden information, password, PDF/A, export text — over the open document and uploaded PDFs, download the results as a ZIP; Bulk Fill: one filled copy of the open form per row of a CSV or Excel sheet, fields matched to columns by name, file names from columns, optionally flattened, as a ZIP and/or one combined PDF |
| Cloud storage | Google Drive and OneDrive: sign in, browse folders and Shared with me, search, open PDFs (and Google Docs / Sheets / Slides as PDFs), save into a folder, Save Back over the opened file |
| Compare & Read Aloud | Compare PDFs: page-by-page difference pictures (red only in the old version, green only in the new, orange changed), the old and new pages, and the text lines that changed; Read Page / Read to End with the browser's voices, pause, stop, speed and voice |
| Certificate signing | Sign with Certificate using your .pfx / .p12 Digital ID or a new self-signed one (downloaded so you can reuse it): in an empty signature field, a box on the page or invisibly, with reason, location, contact, optional timestamp server, certify, and your drawn signature in the box; Check Signatures shows whether each signature is intact, trusted and what it covers. The Digital ID is only held in memory for the session |
| AI Assistant | Chat about the open PDF with page links, Summarize, Extract data, Review contract, Find personal info, Translate, Fill form with AI, Translate PDF (every paragraph translated and written back in place, keeping the layout; Undo brings back the original), and the changes the AI proposes (fill fields, highlight, redact, notes, stamps, rotate, delete, watermark, bookmarks, commands) applied one by one or all at once |

Keyboard: Ctrl+O, Ctrl+S, Ctrl+P, Ctrl+Z, Ctrl+Y, Ctrl+F, Ctrl+plus/minus, Ctrl+0, Esc.

## AI keys

The AI Assistant works with Claude, OpenAI, GitHub Copilot models or a local AI server (Ollama,
LM Studio …), through the same code as the Windows app. Each user can paste their own key in
**AI Assistant → API Keys**; it's kept only in memory for their session. To give everyone a key,
set it in the server's configuration — environment variables or `dotnet user-secrets`, never in a
committed file:

```bash
export PdfEdit__Ai__Provider=Claude          # Claude, OpenAI, Copilot or Local
export PdfEdit__Ai__ClaudeApiKey=...
export PdfEdit__Ai__OpenAiApiKey=...
export PdfEdit__Ai__GitHubToken=...
export PdfEdit__Ai__LocalEndpoint=http://localhost:11434/v1
export PdfEdit__Ai__LocalModel=llama3.2
```

## OCR

OCR uses the Tesseract program on the server (the same engine the Windows app bundles). Install
it with your package manager — for example `apt install tesseract-ocr`, plus
`tesseract-ocr-deu`, `tesseract-ocr-fra` … for more languages — or set
`PdfEdit__Ocr__TesseractPath` (and optionally `PdfEdit__Ocr__TessdataDir`). Without it the rest of
the site works and OCR says it isn't set up.

## Cloud storage

Home → **Open from Cloud** / **Save to Cloud** (and File → Open / Save As) work with Google Drive
and OneDrive: browse folders, search, open PDFs (Google Docs, Sheets and Slides, and Office files on
OneDrive, open as PDF copies), save a new file into a folder, and **Save Back** over the file you
opened. Each user signs in with their own account in a pop-up window; the sign-in is only kept in
memory for their session.

The server needs an OAuth app for each service you want to offer. Register
`https://your-site/cloud/callback` as the redirect address and put the details in the server's
configuration (environment variables or `dotnet user-secrets`, never a committed file):

```bash
# Google Cloud Console → APIs & Services → Credentials → OAuth client ID, type "Web application",
# with the Google Drive API enabled
export PdfEdit__Cloud__Google__ClientId=...apps.googleusercontent.com
export PdfEdit__Cloud__Google__ClientSecret=...
# Microsoft Entra → App registrations → platform "Web", a client secret, and the delegated
# permissions Files.ReadWrite, User.Read and offline_access
export PdfEdit__Cloud__OneDrive__ClientId=...
export PdfEdit__Cloud__OneDrive__ClientSecret=...
export PdfEdit__Cloud__OneDrive__Tenant=common     # or organizations, consumers, a tenant ID
```

A service with no client ID isn't offered. `PdfEdit__Cloud__TestServer` sends every cloud request
to a stand-in server instead, for testing.

## How it works

- Each upload gets a temporary folder on the server; every change writes a new version of the
  file, so Undo and Redo step between versions. Uploads unused for two hours are deleted.
- Pages are drawn by Pdfium and served as PNGs (`/documents/{id}/pages/{n}.png`). Form fields are
  HTML inputs laid over the page; things you add stay editable until Apply Changes, Save or a
  page tool writes them into the PDF.

## Not in the web version yet

Scanning straight from a scanner (browsers can't reach TWAIN scanners — use Scan with Camera or
pictures instead), Office-to-PDF conversion, and several documents open at once.
