# Import, export and cloud

[Back to README](../README.md)

## Bringing documents in

**Open** (Ctrl+O), drag and drop, or **File > Import Word or Office File** take Word, Excel and PowerPoint files as well as PDFs. Each one is turned into a PDF saved next to the original and opened in a new tab.

- **Word** works even without Microsoft Office. With Office or LibreOffice installed, PdfEdit uses them for the closest match.
- **Word forms stay fillable.** Content controls, form fields, ☐ boxes and `______` blanks become PDF fields in the same places, named after their labels.
- **Excel and PowerPoint** need Microsoft Office or the free [LibreOffice](https://www.libreoffice.org/).
- **Google Docs, Sheets and Slides**: Home > **From Google Docs** takes a link. It works for anything your connected Google account can see, or for files shared as "Anyone with the link".

If an imported document looks like a form but has no fields, PdfEdit offers to find the blanks and make them fillable.

Other ways to make a PDF: from images, from scans (Home > Scan), a blank page, or the Design canvas.

## Saving out

| To | Where |
|----|-------|
| Word (.docx) | Edit > Export to Word |
| Excel (.xlsx) | Edit > Export to Excel. Tables are rebuilt as rows and columns, with amounts as numbers. |
| PowerPoint (.pptx) | Edit > Export to PowerPoint. One slide per page. |
| Images | Home > Export as Images, or Edit > Extract Images for the pictures inside |
| Text | Edit > Export Text |
| PDF/A | Edit > Archive (PDF/A) |

## Google Drive and OneDrive

Cloud storage is for two things: importing documents into PdfEdit and saving PDFs back.

- **Import from Cloud** (Home, File menu) browses your folders and brings in PDFs, Word files and Google Docs. Documents come in as PDFs. Saving a PDF you imported uploads your changes back (you can turn this off).
- **Save to Cloud** saves the open PDF into a folder you pick.

PdfEdit uses your own app details, so there's no middleman. Set them up once in **Settings > Cloud**:

- **Google Drive**: in [Google Cloud Console](https://console.cloud.google.com/) create a project, enable the Google Drive API, set up the OAuth consent screen (add yourself as a test user), then create an OAuth client ID of type *Desktop app*. Enter the client ID and secret.
- **OneDrive**: in [Microsoft Entra](https://entra.microsoft.com/) add an app registration with the redirect URI `http://localhost` under *Mobile and desktop applications*, and turn on *Allow public client flows*. Enter the application (client) ID.

Click **Connect** and sign in once in your browser. The sign-in is stored encrypted for your Windows account.
