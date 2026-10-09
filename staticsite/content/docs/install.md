# Installing PdfEdit

PdfEdit is free and comes four ways. They share the same tools and templates, so pick the one that suits your computer. You can [download them all](../../download.html) from the download page.

| | PdfEdit for Windows | PdfEdit for Mac | PdfEdit for Linux | PdfEdit for the web |
|---|---|---|---|---|
| Runs on | Windows 10 (version 2004) or 11, 64-bit | macOS 12 Monterey or later, Apple silicon or Intel | 64-bit Linux with WebKitGTK | Any modern browser, on a server you run |
| Best for | Everyday use on a PC, scanners, everything | Everyday use on a Mac | Everyday use on a Linux PC | A team, phones and tablets |
| Files stay | On your PC | On your Mac | On your PC | On your server |

> **Tip:** on Windows, choose **PdfEdit for Windows**. It's the full app, with scanner support, Windows voices and every tool.

## Windows

1. Download **PdfEditSetup-*version*.exe** from the [download page](../../download.html#windows).
2. Open it. If Windows SmartScreen says it protected your PC, click **More info**, then **Run anyway**. (The installer isn't signed with a paid certificate yet.)
3. Follow the steps. PdfEdit is added to the Start menu and can open PDFs when you double-click them.

No install? Download the **Portable ZIP** instead: unzip it anywhere, even a USB stick, and run `PdfEdit.exe`. Everything it needs is inside.

(Up to version 1.4.1 there was also a smaller ZIP that needed the .NET runtime, an MSIX package and a separate Windows build of PdfEdit Desktop. If you used one of those, Check for Updates moves the small ZIP to the portable one; for the others, install **PdfEditSetup** once and use that from then on.)

### Updating

**Help > Check for Updates** finds the newest version, downloads the right file for how you installed PdfEdit, checks it and installs it. PdfEdit also checks each time it starts and asks before doing anything. Your settings, signatures, stamps and recent files are kept.

## Mac

1. Download the disk image for your Mac from the [download page](../../download.html#mac):
   - **Apple silicon** for Macs with an M1 chip or newer
   - **Intel** for older Macs

   Not sure? Open the Apple menu > **About This Mac**. It says *Chip: Apple M...* or *Processor: Intel*.
2. Open the `.dmg` and drag **PdfEdit** into **Applications**.
3. The first time, macOS may say it can't check PdfEdit for malicious software. Right-click (or Control-click) PdfEdit in Applications and choose **Open**, then **Open** again. On macOS 15 and later, open **System Settings > Privacy & Security**, scroll down and click **Open Anyway** next to PdfEdit.

After that it opens like any other app, and you can choose it under **Open With** for PDFs in Finder.

![PdfEdit for Mac with a form open](../../../docs/screenshots/desktop/mac-form.png)

To keep it in the Dock, right-click its icon while it's open and choose **Options > Keep in Dock**. To make it the app for every PDF, select a PDF in Finder, choose **File > Get Info**, pick PdfEdit under **Open with** and click **Change All...**.

> **Mac:** PdfEdit for Mac uses the same interface as the web version, inside its own window. It works without an internet connection, saves with the normal Mac Save dialog and keeps your files on your Mac.

**Updating:** **Help > Check for Updates** (or **About PdfEdit**) says whether a newer version is out and downloads the disk image for your Mac; open it and drag PdfEdit into Applications again. It also checks quietly when it starts; turn that off in **Settings**.

## Linux

1. Download the **AppImage** from the [download page](../../download.html#linux).
2. Make it executable: right-click it, choose **Properties** and tick **Allow executing file as program** (or run `chmod +x PdfEdit-Desktop-*.AppImage`).
3. Double-click it to start PdfEdit.

![PdfEdit for Linux with a form open](../../../docs/screenshots/desktop/linux-form.png)

Or from a terminal, which also puts PdfEdit in your applications menu and makes it an **Open With** choice for PDFs:

```bash
mkdir -p ~/Applications ~/.local/share/applications ~/.local/share/icons/hicolor/256x256/apps
mv ~/Downloads/PdfEdit-Desktop-*-linux-x64.AppImage ~/Applications/PdfEdit.AppImage
chmod +x ~/Applications/PdfEdit.AppImage
cd /tmp && ~/Applications/PdfEdit.AppImage --appimage-extract pdfedit.png >/dev/null
cp /tmp/squashfs-root/pdfedit.png ~/.local/share/icons/hicolor/256x256/apps/pdfedit.png
cat > ~/.local/share/applications/pdfedit.desktop <<EOF
[Desktop Entry]
Type=Application
Name=PdfEdit
Comment=Fill, sign, edit and design PDFs
Exec=$HOME/Applications/PdfEdit.AppImage %F
Icon=pdfedit
Categories=Office;Viewer;
MimeType=application/pdf;
EOF
update-desktop-database ~/.local/share/applications 2>/dev/null || true
```

If the AppImage doesn't start at all and says something about **FUSE**, either install it (`sudo apt install libfuse2`, called `libfuse2t64` on Ubuntu 24.04 and later) or start PdfEdit with `--appimage-extract-and-run`.

PdfEdit shows its pages with **WebKitGTK**, which most Linux desktops already have. If the window stays blank, install it:

```bash
sudo apt install libwebkit2gtk-4.1-0     # Ubuntu, Debian, Mint
sudo dnf install webkit2gtk4.1           # Fedora
```

Prefer a plain folder? Download the **tarball** instead, unpack it anywhere and run `./PdfEdit` inside. It contains a `pdfedit.desktop` file and icon if you want to add it to your menu.

**Updating:** **Help > Check for Updates** downloads the new AppImage. Make it executable and use it in place of the old one; your settings and signatures are kept.

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

- **Windows**: Settings > Apps > Installed apps > PdfEdit > Uninstall. For the portable ZIP, delete the folder.
- **Mac**: drag PdfEdit from Applications to the Bin.
- **Linux**: delete the AppImage (or the unpacked folder), and `~/.local/share/applications/pdfedit.desktop` if you added it to the menu.

Your settings live in `%AppData%\PdfEdit` on Windows, in your user Application Support folder on a Mac and in `~/.local/share/PdfEdit` on Linux, if you want to remove those too.
