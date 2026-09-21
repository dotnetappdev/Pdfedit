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
    private static readonly HttpClient _http = new();

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

    public static string[] GetModels(string provider) =>
        Providers.TryGetValue(provider, out var m) ? m : Array.Empty<string>();

    public static async Task SendStreamingAsync(
        IList<AiChatMessage> history,
        string provider,
        string model,
        string apiKey,
        Action<string> onChunk,
        CancellationToken ct = default)
    {
        if (provider == "OpenAI")
            await SendOpenAiStreamAsync(history, model, apiKey, onChunk, ct);
        else
            await SendClaudeStreamAsync(history, model, apiKey, onChunk, ct);
    }

    // Non-streaming form fill (keeps backward compat with RunAiFillCommand)
    public static async Task<Dictionary<string, string>> FillFormFieldsAsync(
        string userPrompt,
        IEnumerable<string> fieldNames,
        string model,
        string apiKey,
        CancellationToken ct = default)
    {
        var fieldList = string.Join(", ", fieldNames);
        var fullPrompt =
            $"{userPrompt}\n\nThe PDF form has these fields: {fieldList}\n\n" +
            "Return ONLY a valid JSON object where keys are field names and values are strings to fill in. " +
            "Do not include any explanation, just the JSON object.";

        var messages = new[] { new AiChatMessage { Role = "user", Content = fullPrompt } };
        var sb = new StringBuilder();
        await SendClaudeStreamAsync(messages, model, apiKey, chunk => sb.Append(chunk), ct);
        var text = sb.ToString();

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text[start..(end + 1)]);
            return parsed ?? new();
        }
        return new();
    }

    private static async Task SendClaudeStreamAsync(
        IEnumerable<AiChatMessage> history,
        string model,
        string apiKey,
        Action<string> onChunk,
        CancellationToken ct)
    {
        var messages = history
            .Where(m => m.Role != "system")
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        var body = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 2048,
            stream = true,
            messages
        });

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

    private static async Task SendOpenAiStreamAsync(
        IEnumerable<AiChatMessage> history,
        string model,
        string apiKey,
        Action<string> onChunk,
        CancellationToken ct)
    {
        var messages = history.Select(m => new { role = m.Role, content = m.Content }).ToArray();

        var body = JsonSerializer.Serialize(new { model, stream = true, messages });

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
