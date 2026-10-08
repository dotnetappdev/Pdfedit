# Installing PdfEdit

PdfEdit is free and comes three ways. They share the same tools and templates, so pick the one that suits your computer. You can [download them all](../../download.html) from the download page.

| | PdfEdit for Windows | PdfEdit for Mac | PdfEdit for the web |
|---|---|---|---|
| Runs on | Windows 10 (version 2004) or 11, 64-bit | macOS 12 Monterey or later, Apple silicon or Intel | Any modern browser, on a server you run |
| Best for | Everyday use on a PC, scanners, everything | Everyday use on a Mac | A team, phones and tablets |
| Files stay | On your PC | On your Mac | On your server |

> **Tip:** on Windows, choose **PdfEdit for Windows**. It's the full app, with scanner support, Windows voices and every tool.

## Windows

1. Download **PdfEditSetup-*version*.exe** from the [download page](../../download.html#windows).
2. Open it. If Windows SmartScreen says it protected your PC, click **More info**, then **Run anyway**. (The installer isn't signed with a paid certificate yet.)
3. Follow the steps. PdfEdit is added to the Start menu and can open PDFs when you double-click them.

Other downloads for Windows:

- **Portable ZIP (no install)**: unzip anywhere, even a USB stick, and run `PdfEdit.exe`. Everything it needs is inside.
- **ZIP (needs .NET 10 Desktop Runtime)**: a much smaller download if the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) is already installed.
- **MSIX package**: for people who prefer Windows' own installer. Install the test certificate first (double-click the `.cer`, choose **Install Certificate → Local Machine → Trusted People**), then open the `.msix`.

### Updating

**Help → Check for Updates** finds the newest version, downloads the right file for how you installed PdfEdit, checks it and installs it. PdfEdit also checks each time it starts and asks before doing anything. Your settings, signatures, stamps and recent files are kept.

## Mac

1. Download the disk image for your Mac from the [download page](../../download.html#mac):
   - **Apple silicon** for Macs with an M1 chip or newer
   - **Intel** for older Macs

   Not sure? Open the Apple menu → **About This Mac**. It says *Chip: Apple M…* or *Processor: Intel*.
2. Open the `.dmg` and drag **PdfEdit** into **Applications**.
3. The first time, macOS may say it can't check PdfEdit for malicious software. Right-click (or Control-click) PdfEdit in Applications and choose **Open**, then **Open** again. On macOS 15 and later, open **System Settings → Privacy & Security**, scroll down and click **Open Anyway** next to PdfEdit.

After that it opens like any other app, and you can choose it under **Open With** for PDFs in Finder.

> **Mac:** PdfEdit for Mac uses the same interface as the web version, inside its own window. It works without an internet connection, saves with the normal Mac Save dialog and keeps your files on your Mac.

## The web version

PdfEdit for the web runs on a server you look after: your own computer, a server at work or a cloud machine. Everyone then uses it in their browser, including on phones and tablets, and it can be installed as an app from the browser.

The quickest start, on any computer with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```bash
git clone https://github.com/dotnetappdev/pdfedit.git
cd pdfedit
dotnet run --project PdfEdit.Blazor
```

Then open the address it prints. [Hosting PdfEdit for the web](web-version.md) covers AI keys, OCR, cloud storage and Office conversion, and the [hosting tutorial](host-the-web-version.md) walks through it step by step.

## Removing PdfEdit

- **Windows**: Settings → Apps → Installed apps → PdfEdit → Uninstall. For the portable ZIP, delete the folder.
- **Mac**: drag PdfEdit from Applications to the Bin.

Your settings live in `%AppData%\PdfEdit` on Windows and in your user Application Support folder on a Mac, if you want to remove those too.
