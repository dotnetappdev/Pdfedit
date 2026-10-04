# AI assistant

[← Back to README](../README.md)

The AI features are entirely optional. PdfEdit sends nothing anywhere until you connect a provider in **Settings → AI**.

## Providers

- **Claude** (Anthropic) or **OpenAI**, using your own API key
- **A local model** through Ollama, LM Studio, llama.cpp or another OpenAI-compatible server. It's free, and the document never leaves your PC.

API keys are stored in `%AppData%\PdfEdit\settings.json` on your machine and are only sent to the provider you chose.

## What it can do

- **Smart Fill** reads the form and fills in every field it can, either from what you tell it or from another document.
- **Summarise** gives an overview with the key people, dates and amounts.
- **Extract key data** pulls out names, addresses, reference numbers and totals.
- **Contract analysis** covers the parties, obligations, payment terms, termination clauses and risks.
- **Find personal information** points out anything you might want to redact.
- **Chat** answers questions about the open document, based on its actual text.

You can stop a response at any time.

## Without AI

**Profiles** keep your own details (name, address, phone and so on) and fill matching fields with no AI involved.
