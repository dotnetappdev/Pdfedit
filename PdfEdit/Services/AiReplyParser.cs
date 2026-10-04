using System.Text.Json;
using System.Text.RegularExpressions;
using PdfEdit.Models;

namespace PdfEdit.Services;

/// <summary>
/// Pulls the structured parts out of an AI reply: the ```actions block (changes the assistant
/// proposes) and the FOLLOW-UPS line (suggested questions), leaving the readable text.
/// </summary>
public static class AiReplyParser
{
    private static readonly Regex ActionsRx = new(@"```actions\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
    private static readonly Regex FollowRx = new(@"^\s*FOLLOW-?UPS?\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>Text to show while streaming: hides a half-written actions block and the follow-ups line.</summary>
    public static string VisibleWhileStreaming(string text)
    {
        int i = text.IndexOf("```actions", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            int end = text.IndexOf("```", i + 10, StringComparison.Ordinal);
            text = end < 0 ? text[..i] : text[..i] + text[(end + 3)..];
        }
        text = FollowRx.Replace(text, "").TrimEnd();
        // Don't flash the start of an actions block or the follow-ups line before it's recognisable.
        int nl = text.LastIndexOf('\n');
        string last = text[(nl + 1)..].Trim();
        if (last.Length > 0 && ("```actions".StartsWith(last, StringComparison.OrdinalIgnoreCase)
                                || "FOLLOW-UPS".StartsWith(last, StringComparison.OrdinalIgnoreCase)))
            text = nl < 0 ? "" : text[..nl].TrimEnd();
        return text;
    }

    public static (string Text, List<AiActionItem> Actions, List<string> FollowUps) Parse(string reply)
    {
        var actions = new List<AiActionItem>();
        foreach (Match m in ActionsRx.Matches(reply))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value.Trim());
                var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new() { doc.RootElement };
                foreach (var el in items)
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var args = el.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString());
                    string action = (args.TryGetValue("action", out var a) ? a : "").Trim().ToLowerInvariant();
                    if (action.Length == 0) continue;
                    actions.Add(new AiActionItem { Action = action, Args = args, Description = Describe(action, args), Icon = IconFor(action) });
                }
            }
            catch { /* not valid JSON — leave it out */ }
        }
        string text = ActionsRx.Replace(reply, "");
        var follow = new List<string>();
        var fm = FollowRx.Match(text);
        if (fm.Success)
        {
            follow = fm.Groups[1].Value.Split('|').Select(s => s.Trim().Trim('"', '-', '*', ' ')).Where(s => s.Length > 3).Take(4).ToList();
            text = FollowRx.Replace(text, "");
        }
        return (text.Trim(), actions, follow);
    }

    private static string Get(Dictionary<string, string> a, string k) => a.TryGetValue(k, out var v) ? v : "";

    private static string Describe(string action, Dictionary<string, string> a) => action switch
    {
        "fill_field" => $"Fill “{Get(a, "field")}” with “{Get(a, "value")}”",
        "go_to_page" => $"Go to page {Get(a, "page")}",
        "highlight" => $"Highlight “{Get(a, "text")}”" + (Get(a, "page").Length > 0 ? $" on page {Get(a, "page")}" : ""),
        "redact" => $"Mark “{Get(a, "text")}” for redaction",
        "add_note" => $"Add a note on page {Get(a, "page")}: “{Get(a, "text")}”",
        "add_stamp" => $"Stamp {Get(a, "stamp").ToUpperInvariant()} on page {Get(a, "page")}",
        "rotate_page" => $"Rotate page {Get(a, "page")} by {Get(a, "degrees")}°",
        "delete_page" => $"Delete page {Get(a, "page")}",
        "add_watermark" => $"Add a “{Get(a, "text")}” watermark",
        "add_bookmark" => $"Add bookmark “{Get(a, "title")}” to page {Get(a, "page")}",
        "zoom" => Get(a, "mode").Length > 0 ? $"Zoom: {Get(a, "mode").Replace('_', ' ')}" : $"Zoom to {Get(a, "percent").TrimEnd('%')}%",
        "command" => $"Run: {Get(a, "name").Replace('_', ' ')}",
        _ => $"{action} {string.Join(", ", a.Where(kv => kv.Key != "action").Select(kv => $"{kv.Key}={kv.Value}"))}",
    };

    private static string IconFor(string action) => action switch
    {
        "fill_field" => "", "go_to_page" => "", "highlight" => "", "redact" => "",
        "add_note" => "", "add_stamp" => "", "rotate_page" => "", "delete_page" => "",
        "add_watermark" => "", "add_bookmark" => "", "zoom" => "\uE71E", "command" => "\uE768", _ => "",
    };
}
