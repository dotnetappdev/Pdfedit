# AI assistant

[Back to README](../README.md)

The AI features are entirely optional. PdfEdit sends nothing anywhere until you connect a provider in **Settings > AI**.

## Providers

- **Claude** (Anthropic), using your own API key from console.anthropic.com. Pick Opus 5.5 (the default), Sonnet 5.5, Haiku 4.5 (quickest and cheapest) or Fable 5.1 (most capable). If Claude's safety checks decline a request, a suitable fallback model answers it instead of stopping.
- **GitHub Copilot**: the models behind Copilot (GPT-4.1, GPT-4o) through GitHub Models, signed in with a GitHub personal access token that has the *Models: Read-only* permission. It's free to try within GitHub's rate limits.
- **OpenAI** (ChatGPT), using your own API key
- **A local model** through Ollama, LM Studio, llama.cpp or another OpenAI-compatible server. It's free, and the document never leaves your PC.

API keys are stored in `%AppData%\PdfEdit\settings.json` on your machine and are only sent to the provider you chose.

## What it can do

- **Chat** answers questions about the open document, citing pages you can click to jump to. Replies can be copied or added to the page as a note.
- **Select Text** (press Shift+S): drag over a passage and choose Explain, Summarise, Rewrite, Translate or Ask. Drag over a chart, table or scan and choose **Ask about this area** to send a picture of it.
- **Changes on request**: ask it to fill fields, highlight or redact text, add notes or stamps, rotate or delete pages, or run PdfEdit commands ("compress this", "export to Word"). Each change appears as a card you Apply or Undo.
- **Outline** writes a summary with section key points and page links in its own panel.
- **Write** turns the document into an email, study notes, flashcards, a quiz, an FAQ, a list of actions and deadlines, or a plain-English version.
- **Translate PDF** makes a translated copy that keeps the layout.
- **Ask Across PDFs** answers from several files or a whole folder, citing file and page.
- **Mind Map** shows the document as topics you can click.
- **Smart Fill**, **Summarise**, **Extract key data**, **Contract analysis** and **Find personal information** run in one click.
- **Ask by voice** with the microphone button (needs Windows online speech recognition).

You can stop a response at any time.

## Without AI

**Profiles** keep your own details (name, address, phone and so on) and fill matching fields with no AI involved.
