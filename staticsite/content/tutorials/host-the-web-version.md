---
title: Host PdfEdit for the web for your team
summary: Run PdfEdit on a server so everyone can use it in their browser, with OCR, Office conversion and shared AI keys.
group: Setting up
level: Advanced
time: 30 minutes
apps: Web
order: 1
---

PdfEdit for the web is a .NET 10 app that you run on your own server. The PDFs people work on stay on that server, in temporary folders that are cleared after two hours.

## What you'll need

- A computer or server running Linux, Windows or macOS
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build (or just the ASP.NET Core 10 runtime to run a published copy)
- Optional: Tesseract for OCR and LibreOffice for Office files

## Try it

```bash
git clone https://github.com/dotnetappdev/pdfedit.git
cd pdfedit
dotnet run --project PdfEdit.Blazor
```

Open the address it prints. With the repository checked out, the sample forms appear on the start page.

## Publish a release copy

```bash
dotnet publish PdfEdit.Blazor -c Release -o /opt/pdfedit
cd /opt/pdfedit
ASPNETCORE_URLS=http://0.0.0.0:5080 dotnet PdfEdit.Blazor.dll
```

Run it as a service (systemd on Linux, a Windows service or IIS on Windows) so it starts with the server.

## Put it behind HTTPS

Put a reverse proxy such as nginx, Caddy or IIS in front, with an HTTPS certificate. PdfEdit for the web uses **WebSockets** (it's a Blazor Server app), so make sure the proxy passes them through. With nginx, that means:

```nginx
location / {
    proxy_pass http://127.0.0.1:5080;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    client_max_body_size 200m;
}
```

HTTPS is also needed for installing PdfEdit as an app, Share, Dictate and location on stamps.

> **Important:** PdfEdit for the web has no sign-in of its own. If it's on the internet, put it behind your organisation's sign-in (for example your proxy's or your cloud's authentication), or keep it on your internal network.

## Add OCR and Office conversion

- **OCR**: install Tesseract, for example `sudo apt install tesseract-ocr` plus `tesseract-ocr-deu`, `tesseract-ocr-fra` and so on for more languages.
- **Office files**: install LibreOffice (`sudo apt install libreoffice-core libreoffice-writer libreoffice-calc libreoffice-impress`). Without it, Word files are still converted by PdfEdit's own converter.

## Share an AI key (optional)

Each person can paste their own key in **AI Assistant → API Keys**. To give everyone one, set it in the server's environment, never in a committed file:

```bash
export PdfEdit__Ai__Provider=Claude          # Claude, OpenAI, Copilot or Local
export PdfEdit__Ai__ClaudeApiKey=...
```

Google Drive and OneDrive need an OAuth app each. The [hosting guide](web-version.md) has the details.

## Tell people about it

Send the address round. In Chrome or Edge people can **install** PdfEdit as an app from the address bar, and it then opens PDFs from their computer's **Open with** menu. On a phone, **Add to Home Screen** does the same.

## What's next

- [Hosting PdfEdit for the web](web-version.md): every setting
- [Building from source](building.md)
