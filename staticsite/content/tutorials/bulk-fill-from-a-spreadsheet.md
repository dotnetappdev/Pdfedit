---
title: Fill a form for every row of a spreadsheet
summary: Bulk Fill makes one filled copy of a form for each row of a CSV or Excel file, like a mail merge: certificates, letters, badges and more.
group: Working faster
level: Intermediate
time: 10 minutes
apps: Windows, Mac, Web
order: 1
---

![Bulk Fill](../../../docs/screenshots/blazor/bulk-fill.png)

## Before you start

You need:

- a **fillable form**. Any PDF with fields works, or make one from a template marked *Fillable*, or with [Make a form fillable](make-a-form-fillable.md).
- a **spreadsheet** (`.csv` or Excel `.xlsx`) with a header row and one row per copy, for example:

| Name | Course | Date |
|------|--------|------|
| Ada Lovelace | Data Basics | 4 October 2026 |
| Alan Turing | Data Basics | 4 October 2026 |

> **Tip:** name the columns after the form's fields, and PdfEdit matches them up for you.

## Open the form and start Bulk Fill

Open the form, then click **Bulk Fill** (Home tab, Automate group).

## Choose the spreadsheet

Pick your CSV or Excel file. PdfEdit lists the form's fields, each with the column it'll be filled from and the value from the first row, so you can check the match.

Change any field's column if it picked the wrong one, or leave a field empty.

## Name the files

Choose how each copy is named, using one or more columns. For certificates, the person's name is a good choice. PdfEdit shows an example name.

## Choose what you get

- **Flatten**: values are fixed into the page and can't be changed. Good for certificates you send out.
- **One PDF per row**, **All in one PDF** (handy for printing), or **Both**.

## Fill

Click **Fill and download** (on the web and Mac), or choose the folder on Windows. One PDF per row is saved, zipped together on the web.

## What's next

- [Process a folder of PDFs in one go](batch-process-a-folder.md)
- [Automation and tools](automation.md) in the user guide
