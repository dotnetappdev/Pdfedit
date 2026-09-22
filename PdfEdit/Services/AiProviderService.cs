using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdfEdit.Models;

namespace PdfEdit.Services;

public static class AiProviderService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public static readonly Dictionary<string, string[]> Providers = new()
    {
        ["Claude"] = new[]
        {
            "claude-haiku-4-5-20251001",
            "claude-sonnet-5",
            "claude-opus-5"
        },
        ["OpenAI"] = new[]
        {
            "gpt-4o-mini",
            "gpt-4o",
            "gpt-3.5-turbo"
        }
    };

    public static readonly Dictionary<string, string> ModelDisplayNames = new()
    {
        ["claude-haiku-4-5-20251001"] = "Haiku 4.5",
        ["claude-sonnet-5"]           = "Sonnet 5",
        ["claude-opus-5"]             = "Opus 5",
        ["gpt-4o-mini"]               = "GPT-4o mini",
        ["gpt-4o"]                    = "GPT-4o",
        ["gpt-3.5-turbo"]             = "GPT-3.5",
    };

    public static string[] GetModels(string provider) =>
        Providers.TryGetValue(provider, out var m) ? m : Array.Empty<string>();

    // ── Streaming chat ───────────────────────────────────────────────────────

    public static async Task SendStreamingAsync(
        IList<AiChatMessage> history,
        string provider,
        string model,
        string apiKey,
        Action<string> onChunk,
        CancellationToken ct = default,
        string? systemPrompt = null)
    {
        if (provider == "OpenAI")
            await SendOpenAiStreamAsync(history, model, apiKey, onChunk, ct, systemPrompt);
        else
            await SendClaudeStreamAsync(history, model, apiKey, onChunk, ct, systemPrompt);
    }

    // ── Form fill — now routes to the right provider ─────────────────────────

    public static async Task<Dictionary<string, string>> FillFormFieldsAsync(
        string userPrompt,
        IEnumerable<string> fieldNames,
        string provider,
        string model,
        string apiKey,
        CancellationToken ct = default,
        string? systemPrompt = null,
        string? documentText = null)
    {
        var fieldList = string.Join(", ", fieldNames);
        var docSection = string.IsNullOrWhiteSpace(documentText)
            ? string.Empty
            : $"\n\nDocument content for context:\n{documentText}";

        var fullPrompt =
            $"{userPrompt}\n\nThe PDF form has these fields: {fieldList}{docSection}\n\n" +
            "Return ONLY a valid JSON object where keys are field names and values are the strings to fill in. " +
            "For checkboxes or radio buttons use \"true\" or \"false\". " +
            "Do not include any explanation, just the JSON object.";

        var messages = new[] { new AiChatMessage { Role = "user", Content = fullPrompt } };
        var sb = new StringBuilder();

        if (provider == "OpenAI")
            await SendOpenAiStreamAsync(messages, model, apiKey, chunk => sb.Append(chunk), ct, systemPrompt);
        else
            await SendClaudeStreamAsync(messages, model, apiKey, chunk => sb.Append(chunk), ct, systemPrompt);

        var text = sb.ToString();
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text[start..(end + 1)]);
                return parsed ?? new();
            }
            catch { }
        }
        return new();
    }

    // ── Document analysis (summarize, extract, contract, PII) ────────────────

    public static async Task AnalyzeDocumentAsync(
        string documentText,
        string analysisType,
        string provider,
        string model,
        string apiKey,
        Action<string> onChunk,
        CancellationToken ct = default,
        IEnumerable<string>? fieldNames = null)
    {
        var prompt = BuildAnalysisPrompt(analysisType, documentText, fieldNames);
        var messages = new[] { new AiChatMessage { Role = "user", Content = prompt } };

        const string systemPrompt =
            "You are an expert document analyst. Format your responses clearly with headers and bullet points where appropriate. " +
            "Be concise, accurate, and focus on actionable insights.";

        if (provider == "OpenAI")
            await SendOpenAiStreamAsync(messages, model, apiKey, onChunk, ct, systemPrompt, maxTokens: 4096);
        else
            await SendClaudeStreamAsync(messages, model, apiKey, onChunk, ct, systemPrompt, maxTokens: 4096);
    }

    private static string BuildAnalysisPrompt(string analysisType, string docText, IEnumerable<string>? fieldNames)
    {
        var fieldSection = fieldNames != null
            ? $"\n\nForm fields in this document: {string.Join(", ", fieldNames)}"
            : string.Empty;

        return analysisType switch
        {
            "summarize" =>
                $"Please provide a comprehensive summary of this document. Include:\n" +
                "**Overview** — what is this document and its purpose?\n" +
                "**Key Parties** — who is involved (names, roles, organizations)?\n" +
                "**Key Dates** — important dates, deadlines, or time periods.\n" +
                "**Key Amounts** — any monetary values, quantities, or measurements.\n" +
                "**Main Points** — the 3-5 most important facts or requirements.\n\n" +
                $"Document:\n{docText}",

            "extract" =>
                $"Extract and list all structured data from this document in a clear format:\n\n" +
                "- **Names**: all people and organizations mentioned\n" +
                "- **Dates**: all dates in ISO format where possible\n" +
                "- **Amounts**: all monetary values and quantities\n" +
                "- **Addresses**: any physical or email addresses\n" +
                "- **Reference numbers**: IDs, account numbers, case numbers, etc.\n" +
                "- **Contact info**: phone numbers, emails, websites\n\n" +
                $"Document:\n{docText}",

            "contract" =>
                $"Analyze this contract and provide:\n\n" +
                "**Parties**: Who are the contracting parties?\n" +
                "**Effective Date & Term**: When does it start and end?\n" +
                "**Key Obligations**: What must each party do?\n" +
                "**Payment Terms**: Any financial obligations or fees.\n" +
                "**Termination**: How can either party end this agreement?\n" +
                "**Risks & Concerns**: Any unusual clauses, liabilities, or items to review carefully.\n" +
                "**Missing Sections**: Any standard clauses that appear to be absent.\n\n" +
                $"Contract:\n{docText}",

            "pii" =>
                $"Identify all Personally Identifiable Information (PII) in this document that may need to be redacted before sharing. List each item by type:\n\n" +
                "- **Names** (full names, signatures)\n" +
                "- **ID numbers** (SSN, passport, driver's license, NI number)\n" +
                "- **Financial** (bank accounts, card numbers, tax IDs)\n" +
                "- **Contact** (addresses, phone numbers, email addresses)\n" +
                "- **Medical / biometric** (health information, biometric data)\n" +
                "- **Other sensitive** (passwords, security questions, etc.)\n\n" +
                "For each item, note approximately where it appears in the document.\n\n" +
                $"Document:\n{docText}",

            "translate" =>
                $"Translate the following document content to English (or if it is already in English, identify the language and summarize it). " +
                "Preserve the structure and formatting as much as possible.\n\n" +
                $"Document:\n{docText}",

            "qa" =>
                $"I have a question about this document. Please answer based only on the document content.\n\n" +
                $"Document:\n{docText}{fieldSection}",

            "smartfill" =>
                $"Based on the document content, intelligently fill in the form fields listed below. " +
                "Infer appropriate values from context — for example, if the document mentions a person's name, use it for name fields. " +
                "Return ONLY a valid JSON object where keys are field names and values are strings to fill in. " +
                "For checkboxes or radio buttons use \"true\" or \"false\". " +
                "Only fill fields where you can confidently determine the correct value from the document.\n\n" +
                $"Form fields: {string.Join(", ", fieldNames ?? Array.Empty<string>())}\n\n" +
                $"Document:\n{docText}",

            _ => $"Please help me with this document:\n\n{docText}{fieldSection}"
        };
    }

    // ── Claude streaming ─────────────────────────────────────────────────────

    private static async Task SendClaudeStreamAsync(
        IEnumerable<AiChatMessage> history,
        string model,
        string apiKey,
        Action<string> onChunk,
        CancellationToken ct,
        string? systemPrompt = null,
        int maxTokens = 4096)
    {
        var messages = history
            .Where(m => m.Role != "system")
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        object requestObj = string.IsNullOrEmpty(systemPrompt)
            ? new { model, max_tokens = maxTokens, stream = true, messages }
            : new { model, max_tokens = maxTokens, stream = true, system = systemPrompt, messages };

        var body = JsonSerializer.Serialize(requestObj);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Claude API error {(int)resp.StatusCode}: {err}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
            var data = line[6..];
            if (data == "[DONE]") break;
            try
            {
                var node = JsonNode.Parse(data);
                if (node?["type"]?.GetValue<string>() == "content_block_delta")
                {
                    var text = node["delta"]?["text"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(text)) onChunk(text);
                }
            }
            catch { }
        }
    }

    // ── OpenAI streaming ─────────────────────────────────────────────────────

    private static async Task SendOpenAiStreamAsync(
        IEnumerable<AiChatMessage> history,
        string model,
        string apiKey,
        Action<string> onChunk,
        CancellationToken ct,
        string? systemPrompt = null,
        int maxTokens = 4096)
    {
        var msgList = history
            .Select(m => (object)new { role = m.Role, content = m.Content })
            .ToList();
        if (!string.IsNullOrEmpty(systemPrompt))
            msgList.Insert(0, (object)new { role = "system", content = systemPrompt });
        var messages = msgList.ToArray();

        var body = JsonSerializer.Serialize(new { model, stream = true, max_tokens = maxTokens, messages });
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"OpenAI API error {(int)resp.StatusCode}: {err}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;
            var data = line[6..];
            if (data == "[DONE]") break;
            try
            {
                var node = JsonNode.Parse(data);
                var text = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(text)) onChunk(text);
            }
            catch { }
        }
    }
}
