# Scanning and OCR

[← Back to README](../README.md)

## Scanning

Open **Scan** from the File menu, the Home ribbon, or Toolkit → Scan & text recognition.

The scanner list matches what the Windows Scan app shows. That includes USB scanners and network scanners (WSD and eSCL, such as most HP, Canon and Epson all-in-ones), plus TWAIN drivers. Once you pick a scanner, the other options only show what it supports:

- **Source**: flatbed, document feeder, or both sides (duplex)
- **Colour mode**: colour, greyscale, or black and white
- **Resolution**: 200 dpi suits documents, 300 dpi is best for OCR
- **Page size**: the whole glass, A4, Letter and so on, with optional auto-crop
- **File type**: PDF, or PNG / JPEG / TIFF / BMP images

**Preview** does a quick low-resolution pass. Drag on the preview to scan just part of the glass. **Scan** adds pages to the strip on the right, where you can rotate, reorder, crop or delete them before clicking **Done**.

No scanner? **Add image file…** builds the PDF from photos or image files instead.

## Making scans searchable (OCR)

Tick **Make searchable** and PdfEdit adds an invisible text layer to each scanned page. You can then search, select and copy the text. You'll find the same thing under **Toolkit → Scan & text recognition → Make scanned pages searchable** for PDFs you already have.

Two OCR engines are available:

- **Windows OCR** is used when your copy of Windows has an OCR language installed.
- **Tesseract** comes bundled with English, so OCR works on any PC. More languages can be downloaded in **Settings → OCR**.

**Settings → OCR** also lets you pick the engine yourself, and has a button to install a Windows OCR language.

Tesseract needs the Microsoft Visual C++ runtime. Most PCs already have it. If yours doesn't, PdfEdit tells you and links to the download.
