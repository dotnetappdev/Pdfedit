# Troubleshooting and FAQ

Answers to the questions people ask most. If yours isn't here, [open an issue on GitHub](https://github.com/dotnetappdev/pdfedit/issues/new). Include what you did, what happened, and your version (Help > About PdfEdit).

## Installing and starting

### Windows says it protected my PC

That's SmartScreen. The installer isn't signed with a paid certificate yet. Click **More info > Run anyway**. Every file is built by GitHub Actions from the public source code.

### macOS says PdfEdit can't be opened or checked

Right-click PdfEdit in Applications and choose **Open**. On macOS 15 and later, go to **System Settings > Privacy & Security** and click **Open Anyway** next to PdfEdit. You only need to do this once.

### PdfEdit for Mac shows a blank window

PdfEdit for Mac shows its interface in the system's web view (WebKit). Make sure macOS is up to date, then quit and open it again. If it still stays blank, report it with your macOS version.

### PdfEdit Desktop on Windows needs WebView2

The desktop app shows its interface with Microsoft Edge WebView2, which is part of Windows 10 and 11. If it's been removed, install the [WebView2 runtime](https://developer.microsoft.com/microsoft-edge/webview2/) from Microsoft.

## Forms

### I can't type in the form

The PDF probably has no fillable fields, only lines and boxes printed on the page. Use **Add Text** on the Fill & Sign tab and click where the text should go. PdfEdit lines it up with a box when you click inside one. To make the form properly fillable, use **Detect Fields** ([tutorial](make-a-form-fillable.md)).

### My text is cut off in a field

Select it and use **Fit** on the small toolbar above it: grow the box, wrap the text, or keep the size. For a form field, make the font size Auto or smaller in its properties.

### Other people can't see what I filled in

Some viewers, especially on phones, don't draw form fields well. Use **Flatten & Save** for the copy you send. It fixes everything into the page.

### Validate says required fields are empty

**Forms > Validate** (Edit > Validate Required on the web) lists them and jumps to the first. Required fields have a red outline when **Highlight Fields** is on.

## Signing

### What's the difference between Sign and Certificate?

**Sign** puts a picture of your signature on the page, like signing paper. **Certificate...** (a Digital ID) adds a cryptographic signature that shows if the file is changed afterwards. Use a certificate when the other side asks for a digital signature.

### My signature has a grey background

Import the photo again with **New Signature > Image**. PdfEdit removes the paper behind the ink. A photo taken in good light on plain white paper works best.

## Scanning and OCR

### My scanner isn't listed

PdfEdit lists what Windows' own Scan app sees. Check that the scanner works there first, and install the manufacturer's driver if needed. Network scanners need to be on the same network. Scanning from a scanner is in the Windows app.

### OCR found no text, or the wrong text

Scan at 300 dpi in greyscale, and straighten the pages first with **Clean Up Scans**. For languages other than English, download the language in **Settings > OCR**.

## Saving and files

### Where did my file go? (web)

The web version saves by downloading. Look in your browser's downloads. Your work in progress is kept as a draft in the browser, so a reload doesn't lose it.

### The file got bigger after saving

Pictures, signatures and fonts add up. **Compress** (Home tab) saves a smaller copy and tells you how much it saved.

### I can't edit a protected PDF

If it asks for a password to open, you'll need that password. If it opens but won't let you change it, it has permission restrictions. Use **Remove Password** only on files you have the right to change.

## AI assistant

### The AI buttons don't do anything

The AI features need a provider. Add your Claude or OpenAI key, a GitHub token for Copilot, or a local model in **Settings > AI**. Nothing is sent anywhere until you do. See [AI assistant](ai-assistant.md).

### Is my document sent to the internet?

Only if you use the AI assistant or cloud storage, and only to the provider you chose. With a local model (Ollama, LM Studio), nothing leaves your computer.

## Still stuck?

- Search this guide with the box on the [user guide home](index.html).
- Look through the [tutorials](../tutorials/index.html).
- [Report a problem on GitHub](https://github.com/dotnetappdev/pdfedit/issues/new).
