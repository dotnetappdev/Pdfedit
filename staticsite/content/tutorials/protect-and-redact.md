---
title: Redact private details and password-protect a PDF
summary: Black out names, numbers and other private details for good, strip hidden information, and lock the file with a password.
group: Pages and documents
level: Intermediate
time: 10 minutes
apps: Windows, Mac, Web
order: 3
---

Before you send a document outside your organisation, take out what the other side shouldn't see. A black box drawn on top isn't enough, because the text is still underneath. Redaction removes it.

## Mark what to remove

1. On the **Tools** tab, choose **Redact**.
2. Drag over each name, number, signature or picture you want gone. Each area is marked but nothing is removed yet.

> **Tip:** the AI assistant's **Find PII** (AI Assistant tab) lists names, addresses, phone numbers and other personal details in the document so you don't miss any. You'll need an AI provider set up.

## Apply the redactions

Click **Apply Redactions**. The text and pictures under the marked areas are deleted and replaced with black boxes. This can't be undone once you save, so check the pages first.

Search the document (**Search** on the Home tab) for a redacted word to be sure it's gone.

## Remove hidden information

PDFs can carry an author name, editing history, comments, attachments and scripts. Click **Remove Hidden Info** (Home tab, Security group) and choose what to strip: document metadata, JavaScript and automatic actions, attached files, comments and marks, and bookmarks.

## Add a password

1. Click **Password Protect** (Home tab, Security group).
2. Type a **password to open** the file, and type it again to confirm.
3. Optionally, set a separate **permissions password** and untick **Allow printing** or **Allow copying text**.
4. Click OK. On Mac and the web, PdfEdit saves a protected copy and leaves the copy you're working on unlocked.

The file is encrypted with AES-256. PdfEdit never keeps the passwords you type, so make sure you remember them.

> **Warning:** there's no way to get into the file if the password is lost.

## Send it

Send the protected file, and send the password a different way, for example by phone or text message.

## What's next

- [Remove a password](pages-and-security.md#passwords-and-redaction) from a file you've unlocked
- [Pages, watermarks and security](pages-and-security.md) in the user guide
