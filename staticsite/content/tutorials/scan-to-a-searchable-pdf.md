---
title: Scan paper into a searchable PDF
summary: Scan pages from a scanner or your phone's camera, tidy them up, and add OCR so you can search and copy the text.
group: Scanning and converting
level: Beginner
time: 10 minutes
apps: Windows, Mac, Web
order: 1
---

## What you'll need

- **Windows**: a scanner that Windows' own Scan app can see, including network all-in-ones from HP, Canon, Epson and others.
- **Mac and web**: your phone or tablet's camera, or photos of the pages. Browsers can't talk to scanners directly.

## Scan the pages

**On Windows:**

1. Click **Scan** (Home tab) and pick your scanner.
2. Choose the **source** (flatbed, document feeder, or both sides), **colour mode** and **resolution**. Use 300 dpi if you'll run OCR.
3. Click **Preview** for a quick look. You can drag on the preview to scan just part of the glass.
4. Click **Scan**. The pages appear in a strip on the right. Scan more, or rotate, reorder, crop or delete pages.
5. Tick **Recognise text** to make it searchable straight away, then click **Done**.

**On Mac and the web:**

1. Click **Scan** (Home tab).
2. On a phone or tablet the camera opens: take a photo of each page. On a computer, choose pictures of the pages.
3. Tick **Make it searchable (OCR)** and pick the language.
4. Click **Make PDF**.

> **Tip:** take photos in good, even light, with the page flat and filling the frame.

## Clean up the scans

**Clean Up Scans** (Home tab) removes blank pages, straightens pages scanned at an angle and splits two-page spreads into separate pages. **Ctrl+Z** undoes it.

## Add OCR to a PDF you already have

If you skipped OCR, or have an old scanned PDF:

- **Windows**: All tools > **Scan & OCR > Recognise text**.
- **Mac and web**: Tools tab > **OCR**.

Pick the language and click **Recognise text**. PdfEdit adds an invisible layer of text. The pages look the same, but now you can search, select and copy.

## Check it worked

Search for a word you can see on the page. If it's found, the OCR worked.

## If something goes wrong

- **Scanner not listed (Windows)**: check that it works in the Windows Scan app, and install the maker's driver.
- **OCR says it isn't set up (Mac)**: PdfEdit uses the free Tesseract engine. Install it with [Homebrew](https://brew.sh): `brew install tesseract tesseract-lang`, then open PdfEdit again.
- **Other languages (Windows)**: download them in **Settings > OCR**.
- **Wrong text**: scan at 300 dpi in greyscale and straighten pages first.

## What's next

- [Scanning and OCR](scan-and-ocr.md) in the user guide
- [Convert a PDF to Word or Excel](convert-pdf-to-word-and-excel.md)
