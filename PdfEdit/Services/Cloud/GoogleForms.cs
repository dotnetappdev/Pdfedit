using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PdfEdit.Services.Cloud;

public enum FormQuestionType { ShortText, Paragraph, Choice, Checkboxes, Dropdown, Scale, Date, Time, Grid, CheckboxGrid, FileUpload, Section, Info }

/// <summary>A question (or heading) of a form.</summary>
public sealed class FormQuestion
{
    public FormQuestionType Type { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Required { get; set; }
    public List<string> Options { get; set; } = new();
    public bool HasOther { get; set; }
    public string LowLabel { get; set; } = "";
    public string HighLabel { get; set; } = "";
    /// <summary>Grid rows (the options are the columns).</summary>
    public List<string> Rows { get; set; } = new();
}

/// <summary>A form's title, description and questions.</summary>
public sealed class FormSpec
{
    public string Title { get; set; } = "Form";
    public string Description { get; set; } = "";
    public List<FormQuestion> Questions { get; } = new();
}

/// <summary>
/// Reads a Google Form: through the Forms API when Google Drive is connected (any form the account
/// can open), otherwise from the public form page (forms anyone can fill in).
/// </summary>
public static class GoogleForms
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly Regex EditIdRx = new(@"docs\.google\.com/forms/(?:u/\d+/)?d/(?!e/)([A-Za-z0-9_-]{20,})", RegexOptions.IgnoreCase);
    private static readonly Regex PublishedRx = new(@"docs\.google\.com/forms/(?:u/\d+/)?d/e/([A-Za-z0-9_-]{20,})", RegexOptions.IgnoreCase);

    public static bool IsFormLink(string link) =>
        link.Contains("docs.google.com/forms/", StringComparison.OrdinalIgnoreCase) || link.Contains("forms.gle/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the form at a link (edit, view or forms.gle link).</summary>
    public static async Task<FormSpec> FetchAsync(string link, CancellationToken ct = default)
    {
        var edit = EditIdRx.Match(link);
        if (edit.Success && GoogleDriveProvider.Instance.IsConnected)
        {
            try { return await FetchByIdAsync(edit.Groups[1].Value, ct); }
            catch (InvalidOperationException) when (!ct.IsCancellationRequested) { /* no Forms API access: try the public page */ }
        }
        string url = link.Trim();
        var pub = PublishedRx.Match(url);
        if (pub.Success) url = $"https://docs.google.com/forms/d/e/{pub.Groups[1].Value}/viewform";
        else if (edit.Success) url = $"https://docs.google.com/forms/d/{edit.Groups[1].Value}/viewform";
        else if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        string html = await Http.GetStringAsync(url, ct);
        return FromPublicPage(html) ?? throw new InvalidOperationException(
            "Couldn't read that form. Make sure it's open to responses and doesn't require signing in, or connect Google Drive (Settings → Cloud) with an account that can edit it.");
    }

    /// <summary>A form in the connected Google account, through the Forms API.</summary>
    public static async Task<FormSpec> FetchByIdAsync(string formId, CancellationToken ct = default)
    {
        string json = await GoogleDriveProvider.Instance.GetFormJsonAsync(formId, ct);
        return FromApi(json);
    }

    // ── Forms API (forms.get) ───────────────────────────────────────────────

    public static FormSpec FromApi(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var spec = new FormSpec();
        if (root.TryGetProperty("info", out var info))
        {
            spec.Title = Str(info, "title") is { Length: > 0 } t ? t : Str(info, "documentTitle");
            spec.Description = Str(info, "description");
        }
        if (!root.TryGetProperty("items", out var items)) return spec;
        foreach (var item in items.EnumerateArray())
        {
            string title = Str(item, "title"), desc = Str(item, "description");
            if (item.TryGetProperty("questionItem", out var qi) && qi.TryGetProperty("question", out var q))
            {
                var fq = new FormQuestion { Title = title, Description = desc, Required = q.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True };
                if (q.TryGetProperty("textQuestion", out var tq))
                    fq.Type = tq.TryGetProperty("paragraph", out var para) && para.ValueKind == JsonValueKind.True ? FormQuestionType.Paragraph : FormQuestionType.ShortText;
                else if (q.TryGetProperty("choiceQuestion", out var cq))
                {
                    fq.Type = Str(cq, "type") switch { "CHECKBOX" => FormQuestionType.Checkboxes, "DROP_DOWN" => FormQuestionType.Dropdown, _ => FormQuestionType.Choice };
                    if (cq.TryGetProperty("options", out var opts))
                        foreach (var o in opts.EnumerateArray())
                        {
                            if (o.TryGetProperty("isOther", out var other) && other.ValueKind == JsonValueKind.True) fq.HasOther = true;
                            else if (Str(o, "value") is { Length: > 0 } v) fq.Options.Add(v);
                        }
                }
                else if (q.TryGetProperty("scaleQuestion", out var sq))
                {
                    fq.Type = FormQuestionType.Scale;
                    int low = sq.TryGetProperty("low", out var l) && l.TryGetInt32(out var li) ? li : 1;
                    int high = sq.TryGetProperty("high", out var h) && h.TryGetInt32(out var hi) ? hi : 5;
                    for (int i = low; i <= high; i++) fq.Options.Add(i.ToString());
                    fq.LowLabel = Str(sq, "lowLabel");
                    fq.HighLabel = Str(sq, "highLabel");
                }
                else if (q.TryGetProperty("ratingQuestion", out var rq))
                {
                    fq.Type = FormQuestionType.Scale;
                    int n = rq.TryGetProperty("ratingScaleLevel", out var lv) && lv.TryGetInt32(out var ln) ? ln : 5;
                    for (int i = 1; i <= n; i++) fq.Options.Add(i.ToString());
                }
                else if (q.TryGetProperty("dateQuestion", out _)) fq.Type = FormQuestionType.Date;
                else if (q.TryGetProperty("timeQuestion", out _)) fq.Type = FormQuestionType.Time;
                else if (q.TryGetProperty("fileUploadQuestion", out _)) fq.Type = FormQuestionType.FileUpload;
                else fq.Type = FormQuestionType.ShortText;
                spec.Questions.Add(fq);
            }
            else if (item.TryGetProperty("questionGroupItem", out var gi))
            {
                var fq = new FormQuestion { Title = title, Description = desc, Type = FormQuestionType.Grid };
                if (gi.TryGetProperty("grid", out var grid) && grid.TryGetProperty("columns", out var cols))
                {
                    if (Str(cols, "type") == "CHECKBOX") fq.Type = FormQuestionType.CheckboxGrid;
                    if (cols.TryGetProperty("options", out var co)) foreach (var o in co.EnumerateArray()) fq.Options.Add(Str(o, "value"));
                }
                if (gi.TryGetProperty("questions", out var rows))
                    foreach (var row in rows.EnumerateArray())
                    {
                        if (row.TryGetProperty("rowQuestion", out var rq)) fq.Rows.Add(Str(rq, "title"));
                        if (row.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.True) fq.Required = true;
                    }
                spec.Questions.Add(fq);
            }
            else if (item.TryGetProperty("pageBreakItem", out _))
                spec.Questions.Add(new FormQuestion { Type = FormQuestionType.Section, Title = title, Description = desc });
            else if (item.TryGetProperty("textItem", out _))
                spec.Questions.Add(new FormQuestion { Type = FormQuestionType.Info, Title = title, Description = desc });
        }
        return spec;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // ── Public form page (FB_PUBLIC_LOAD_DATA_) ─────────────────────────────

    public static FormSpec? FromPublicPage(string html)
    {
        var m = Regex.Match(html, @"FB_PUBLIC_LOAD_DATA_\s*=\s*(\[.*?\]);\s*</script>", RegexOptions.Singleline);
        if (!m.Success) return null;
        try
        {
            using var doc = JsonDocument.Parse(m.Groups[1].Value);
            var data = doc.RootElement;
            var form = At(data, 1);
            var spec = new FormSpec
            {
                Description = S(At(form, 0)),
                Title = S(At(form, 8)) is { Length: > 0 } t ? t : S(At(data, 3)),
            };
            var items = At(form, 1);
            if (items.ValueKind != JsonValueKind.Array) return spec;
            foreach (var item in items.EnumerateArray())
            {
                string title = S(At(item, 1)), desc = S(At(item, 2));
                int type = At(item, 3).ValueKind == JsonValueKind.Number ? At(item, 3).GetInt32() : -1;
                var answers = At(item, 4);
                var first = At(answers, 0);
                var fq = new FormQuestion { Title = title, Description = desc, Required = At(first, 2).ValueKind == JsonValueKind.Number ? At(first, 2).GetInt32() == 1 : At(first, 2).ValueKind == JsonValueKind.True };
                void Options(JsonElement entry)
                {
                    var opts = At(entry, 1);
                    if (opts.ValueKind != JsonValueKind.Array) return;
                    foreach (var o in opts.EnumerateArray())
                    {
                        string v = S(At(o, 0));
                        bool isOther = At(o, 4).ValueKind == JsonValueKind.Number && At(o, 4).GetInt32() == 1;
                        if (isOther || v.Length == 0) fq.HasOther |= isOther;
                        else fq.Options.Add(v);
                    }
                }
                switch (type)
                {
                    case 0: fq.Type = FormQuestionType.ShortText; break;
                    case 1: fq.Type = FormQuestionType.Paragraph; break;
                    case 2: fq.Type = FormQuestionType.Choice; Options(first); break;
                    case 3: fq.Type = FormQuestionType.Dropdown; Options(first); break;
                    case 4: fq.Type = FormQuestionType.Checkboxes; Options(first); break;
                    case 5:
                    case 18:
                        fq.Type = FormQuestionType.Scale; Options(first);
                        var labels = At(first, 3);
                        fq.LowLabel = S(At(labels, 0)); fq.HighLabel = S(At(labels, 1));
                        if (fq.Options.Count == 0) for (int i = 1; i <= 5; i++) fq.Options.Add(i.ToString());
                        break;
                    case 7:
                        fq.Type = FormQuestionType.Grid;
                        if (answers.ValueKind == JsonValueKind.Array)
                            foreach (var row in answers.EnumerateArray())
                            {
                                fq.Rows.Add(S(At(At(row, 3), 0)));
                                if (fq.Options.Count == 0) Options(row);
                                if (At(At(row, 11), 0).ValueKind == JsonValueKind.Number && At(At(row, 11), 0).GetInt32() == 1) fq.Type = FormQuestionType.CheckboxGrid;
                            }
                        break;
                    case 8: fq.Type = FormQuestionType.Section; break;
                    case 9: fq.Type = FormQuestionType.Date; break;
                    case 10: fq.Type = FormQuestionType.Time; break;
                    case 13: fq.Type = FormQuestionType.FileUpload; break;
                    case 6: fq.Type = FormQuestionType.Info; break;
                    default: continue;   // images and videos
                }
                spec.Questions.Add(fq);
            }
            return spec;
        }
        catch { return null; }

        static JsonElement At(JsonElement e, int i) => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > i ? e[i] : default;
        static string S(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
    }

    /// <summary>Reads the form at <paramref name="link"/> and writes it as a fillable PDF into <paramref name="folder"/>.</summary>
    public static async Task<string> ImportAsync(string link, string folder, CancellationToken ct = default)
    {
        var spec = await FetchAsync(link, ct);
        return Save(spec, folder);
    }

    public static string Save(FormSpec spec, string folder)
    {
        Directory.CreateDirectory(folder);
        string safe = string.Concat(spec.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = "Google form";
        string dest = Path.Combine(folder, safe + ".pdf");
        for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(folder, $"{safe} ({i}).pdf");
        FormPdfBuilder.Build(spec, dest);
        return dest;
    }
}
