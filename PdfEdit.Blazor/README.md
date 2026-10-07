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
| Fill & Sign | Fill every kind of form field, Add Text, Date, ticks, crosses and dots, draw or type a signature and place it, sticky notes, highlights, Apply Changes, Flatten & Download |
| Pages | Rotate, delete, insert blank before/after, duplicate, move up/down, extract or delete a range, split, merge PDFs and pictures, insert a PDF, export a page as an image |
| Document | Properties, watermark (add/remove), page numbers, header/footer, Bates numbers, compress, resize pages, pages per sheet, booklet, greyscale |
| Security | Password protect, remove hidden information, redaction (mark, then apply) |
| Edit | Undo/Redo for everything, export/import form data, reset form, check required fields, rectangles and ellipses |
| View | Zoom, fit width/page, thumbnails, side panel, bookmarks, comments, search with highlights, light/dark theme, statistics |
| Export | Word, Excel, PowerPoint, HTML, Markdown, ePub, text, page images, pictures |
| Prepare Form | Detect Fields on flat forms (named from their printed labels), add text, checkbox, radio, dropdown, list, date and signature fields by drawing them, select, drag to move, drag the corner to resize, Delete key, field properties (name, tooltip, required, read only, multi-line, alignment, font size, max characters, date and number formats) |
| Design | Live View / Design switch like the Windows app: design a form page from text, rectangles, ellipses, lines, arrows, tables, pictures, ticks and crosses, and text, multi-line, checkbox, radio, dropdown and signature fields; drag to move, drag the corner to resize, properties for each element, front/back, duplicate, delete, undo/redo; save and open .pdfdesign files (the same format as the Windows app), export a fillable PDF or open it straight in Live View |
| Scan & OCR | Recognise Text (OCR) adds an invisible text layer so scanned pages can be searched and copied (Tesseract on the server, any installed language), Scan with Camera turns phone or webcam photos — or pictures — into a PDF, optionally made searchable |
| Compare & Read Aloud | Compare PDFs: page-by-page difference pictures (red only in the old version, green only in the new, orange changed), the old and new pages, and the text lines that changed; Read Page / Read to End with the browser's voices, pause, stop, speed and voice |
| AI Assistant | Chat about the open PDF with page links, Summarize, Extract data, Review contract, Find personal info, Translate, Fill form with AI, and the changes the AI proposes (fill fields, highlight, redact, notes, rotate, delete, watermark, bookmarks, commands) applied one by one or all at once |

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

## How it works

- Each upload gets a temporary folder on the server; every change writes a new version of the
  file, so Undo and Redo step between versions. Uploads unused for two hours are deleted.
- Pages are drawn by Pdfium and served as PNGs (`/documents/{id}/pages/{n}.png`). Form fields are
  HTML inputs laid over the page; things you add stay editable until Apply Changes, Save or a
  page tool writes them into the PDF.

## Not in the web version yet

Scanning straight from a scanner (browsers can't reach TWAIN scanners — use Scan with Camera or
pictures instead), certificate signing, stamps, ink drawing and measuring,
moving or resizing things after placing them, adding things to rotated pages, cloud storage,
batch and bulk fill, translation, Office-to-PDF conversion, and several documents open at once.
