using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PdfEdit.Services;

public class ClaudeAiService
{
    private static readonly HttpClient _http = new();
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    public async Task<Dictionary<string, string>> FillFormFieldsAsync(
        string userPrompt,
        IEnumerable<string> fieldNames,
        string apiKey,
        CancellationToken ct = default)
    {
        var fieldList = string.Join(", ", fieldNames);
        var fullPrompt =
            $"{userPrompt}\n\n" +
            $"The PDF form has these fields: {fieldList}\n\n" +
            "Return ONLY a valid JSON object where keys are field names and values are strings to fill in. " +
            "Do not include any explanation, just the JSON object.";

        var body = new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 1024,
            messages = new[] { new { role = "user", content = fullPrompt } }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", ApiVersion);
        req.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req, ct);
        var respBody = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude API error {resp.StatusCode}: {respBody}");

        var doc = JsonNode.Parse(respBody);
        var text = doc?["content"]?[0]?["text"]?.GetValue<string>() ?? string.Empty;

        // Extract JSON object from the response text
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var jsonSnippet = text[start..(end + 1)];
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonSnippet);
            return parsed ?? new Dictionary<string, string>();
        }

        return new Dictionary<string, string>();
    }
}
