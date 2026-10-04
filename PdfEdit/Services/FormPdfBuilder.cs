using System.Globalization;
using System.IO;
using iText.IO.Font;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using PdfEdit.Services.Cloud;

namespace PdfEdit.Services;

/// <summary>
/// Lays a form (from Google Forms) out as PDF pages with real, fillable fields: each question with
/// its title, description and required mark, then the right field: a text line, a paragraph box,
/// option buttons, check boxes, a dropdown, a scale, a date or time, or a grid.
/// </summary>
public static class FormPdfBuilder
{
    private static readonly Color Accent = new DeviceRgb(0x67, 0x3A, 0xB7);
    private static readonly Color Dim = new DeviceRgb(0x5F, 0x63, 0x68);
    private static readonly Color Rule = new DeviceRgb(0xDA, 0xDC, 0xE0);

    public static int Build(FormSpec spec, string pdfPath)
    {
        string layoutPath = pdfPath + ".layout";
        var fields = new List<NewField>();
        try
        {
            Layout(spec, layoutPath, fields);
            if (fields.Count == 0) { File.Move(layoutPath, pdfPath, overwrite: true); return 0; }
            return DetectFieldsService.AddFields(layoutPath, pdfPath, fields);
        }
        finally { try { if (File.Exists(layoutPath)) File.Delete(layoutPath); } catch { } }
    }

    private static void Layout(FormSpec spec, string path, List<NewField> fields)
    {
        bool metric = true;
        try { metric = RegionInfo.CurrentRegion.IsMetric; } catch { }
        var pdf = new PdfDocument(new PdfWriter(path));
        pdf.GetDocumentInfo().SetTitle(spec.Title);
        var doc = new Document(pdf, metric ? PageSize.A4 : PageSize.LETTER);
        doc.SetMargins(54, 54, 54, 54);
        float width = (metric ? PageSize.A4 : PageSize.LETTER).GetWidth() - 108;
        var (regular, bold) = Fonts();
        doc.SetFont(regular).SetFontSize(10.5f);

        // Title block
        doc.Add(new Div().SetHeight(6).SetBackgroundColor(Accent).SetMarginBottom(10));
        doc.Add(new Paragraph(spec.Title).SetFont(bold).SetFontSize(20).SetMarginBottom(4));
        if (spec.Description.Length > 0) doc.Add(new Paragraph(spec.Description).SetFontColor(Dim).SetMarginBottom(4));
        if (spec.Questions.Any(q => q.Required))
            doc.Add(new Paragraph().Add(new Text("* ").SetFontColor(ColorConstants.RED)).Add(new Text("Required").SetFontColor(Dim)).SetFontSize(9));
        doc.Add(new Div().SetHeight(0.75f).SetBackgroundColor(Rule).SetMarginTop(6).SetMarginBottom(12));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Name(string s, string fallback)
        {
            string b = string.IsNullOrWhiteSpace(s) ? fallback : s.Replace(".", " ").Trim();
            if (b.Length > 60) b = b[..60].Trim();
            string n = b;
            for (int i = 2; !names.Add(n); i++) n = $"{b} {i}";
            return n;
        }

        int number = 0;
        foreach (var q in spec.Questions)
        {
            if (q.Type == FormQuestionType.Section)
            {
                if (q.Title.Length > 0) doc.Add(new Paragraph(q.Title).SetFont(bold).SetFontSize(14).SetFontColor(Accent).SetMarginTop(10).SetMarginBottom(2));
                if (q.Description.Length > 0) doc.Add(new Paragraph(q.Description).SetFontColor(Dim).SetMarginBottom(8));
                continue;
            }
            if (q.Type == FormQuestionType.Info)
            {
                if (q.Title.Length > 0) doc.Add(new Paragraph(q.Title).SetFont(bold).SetFontSize(12).SetMarginBottom(2));
                if (q.Description.Length > 0) doc.Add(new Paragraph(q.Description).SetFontColor(Dim).SetMarginBottom(10));
                continue;
            }

            number++;
            var block = new Div().SetKeepTogether(true).SetMarginBottom(14);
            var heading = new Paragraph().SetMarginBottom(2).Add(new Text($"{number}. ").SetFont(bold).SetFontColor(Dim)).Add(new Text(q.Title).SetFont(bold).SetFontSize(11.5f));
            if (q.Required) heading.Add(new Text(" *").SetFont(bold).SetFontColor(ColorConstants.RED));
            block.Add(heading);
            if (q.Description.Length > 0) block.Add(new Paragraph(q.Description).SetFontColor(Dim).SetFontSize(9.5f).SetMarginBottom(2));
            string fieldName = Name(q.Title, $"Question {number}");
            string tip = q.Title + (q.Required ? " (required)" : "");

            switch (q.Type)
            {
                case FormQuestionType.ShortText:
                case FormQuestionType.Time:
                case FormQuestionType.FileUpload:
                {
                    if (q.Type == FormQuestionType.FileUpload) block.Add(new Paragraph("Attach the file separately; write its name here:").SetFontColor(Dim).SetFontSize(9));
                    float w = q.Type == FormQuestionType.Time ? 90 : width;
                    var f = new NewField { Name = fieldName, Tooltip = q.Type == FormQuestionType.Time ? tip + " — hh:mm" : tip, Required = q.Required, FontSize = 11 };
                    block.Add(new Paragraph().SetMarginTop(4).Add(FieldPlaceholder.Create(f, w, 20, PlaceholderLook.Underline, fields)));
                    break;
                }
                case FormQuestionType.Paragraph:
                {
                    var f = new NewField { Name = fieldName, Multiline = true, Tooltip = tip, Required = q.Required, FontSize = 10.5f };
                    block.Add(new Paragraph().SetMarginTop(4).Add(FieldPlaceholder.Create(f, width, 72, PlaceholderLook.Box, fields)));
                    break;
                }
                case FormQuestionType.Date:
                {
                    string fmt = metric ? "dd/mm/yyyy" : "mm/dd/yyyy";
                    var f = new NewField { Name = fieldName, DateFormat = fmt, Tooltip = $"{tip} — {fmt}", Required = q.Required, FontSize = 11 };
                    block.Add(new Paragraph().SetMarginTop(4).Add(FieldPlaceholder.Create(f, 120, 20, PlaceholderLook.Underline, fields))
                        .Add(new Text("  " + fmt).SetFontColor(Dim).SetFontSize(9)));
                    break;
                }
                case FormQuestionType.Dropdown:
                {
                    var f = new NewField { Name = fieldName, Choices = q.Options.ToList(), Tooltip = tip, Required = q.Required, FontSize = 10.5f };
                    float w = Math.Clamp((q.Options.DefaultIfEmpty("").Max(o => o.Length) + 6) * 6f, 140, width);
                    block.Add(new Paragraph().SetMarginTop(4).Add(FieldPlaceholder.Create(f, w, 20, PlaceholderLook.Box, fields)));
                    break;
                }
                case FormQuestionType.Choice:
                case FormQuestionType.Checkboxes:
                {
                    bool radio = q.Type == FormQuestionType.Choice;
                    foreach (var option in q.Options)
                    {
                        var f = radio
                            ? new NewField { RadioGroup = fieldName, RadioValue = option, Tooltip = tip, Required = q.Required }
                            : new NewField { Name = Name($"{q.Title} - {option}", option), IsCheckBox = true, Tooltip = $"{q.Title}: {option}" };
                        block.Add(new Paragraph().SetMarginTop(3).SetMarginLeft(4)
                            .Add(FieldPlaceholder.Create(f, 11, 11, radio ? PlaceholderLook.Round : PlaceholderLook.Box, fields))
                            .Add(new Text("  " + option)));
                    }
                    if (q.HasOther)
                    {
                        var o = radio
                            ? new NewField { RadioGroup = fieldName, RadioValue = "Other", Tooltip = tip }
                            : new NewField { Name = Name($"{q.Title} - Other", "Other"), IsCheckBox = true, Tooltip = $"{q.Title}: Other" };
                        var other = new NewField { Name = Name($"{q.Title} - Other answer", "Other answer"), Tooltip = $"{q.Title}: other answer", FontSize = 10.5f };
                        block.Add(new Paragraph().SetMarginTop(3).SetMarginLeft(4)
                            .Add(FieldPlaceholder.Create(o, 11, 11, radio ? PlaceholderLook.Round : PlaceholderLook.Box, fields))
                            .Add(new Text("  Other: "))
                            .Add(FieldPlaceholder.Create(other, Math.Min(260, width - 80), 16, PlaceholderLook.Underline, fields)));
                    }
                    break;
                }
                case FormQuestionType.Scale:
                {
                    var table = new Table(UnitValue.CreatePercentArray(q.Options.Count + 2)).UseAllAvailableWidth().SetMarginTop(4);
                    table.AddCell(Plain(q.LowLabel, Dim, 9).SetTextAlignment(TextAlignment.RIGHT).SetVerticalAlignment(VerticalAlignment.BOTTOM));
                    foreach (var option in q.Options)
                    {
                        var f = new NewField { RadioGroup = fieldName, RadioValue = option, Tooltip = tip, Required = q.Required };
                        var cell = new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.CENTER).SetPadding(2);
                        cell.Add(new Paragraph(option).SetFontSize(9).SetMarginBottom(2));
                        cell.Add(new Paragraph().Add(FieldPlaceholder.Create(f, 12, 12, PlaceholderLook.Round, fields)));
                        table.AddCell(cell);
                    }
                    table.AddCell(Plain(q.HighLabel, Dim, 9).SetVerticalAlignment(VerticalAlignment.BOTTOM));
                    block.Add(table);
                    break;
                }
                case FormQuestionType.Grid:
                case FormQuestionType.CheckboxGrid:
                {
                    bool radio = q.Type == FormQuestionType.Grid;
                    var widths = new float[q.Options.Count + 1];
                    widths[0] = 3;
                    for (int i = 1; i < widths.Length; i++) widths[i] = 1;
                    var table = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth().SetMarginTop(4).SetFontSize(9.5f);
                    table.AddHeaderCell(Plain("", Dim, 9));
                    foreach (var col in q.Options) table.AddHeaderCell(Plain(col, Dim, 9).SetTextAlignment(TextAlignment.CENTER));
                    foreach (var row in q.Rows)
                    {
                        table.AddCell(Plain(row, null, 10).SetBorderBottom(new SolidBorder(Rule, 0.5f)));
                        string group = Name($"{q.Title} - {row}", row);
                        foreach (var col in q.Options)
                        {
                            var f = radio
                                ? new NewField { RadioGroup = group, RadioValue = col, Tooltip = $"{q.Title}: {row}", Required = q.Required }
                                : new NewField { Name = Name($"{q.Title} - {row} - {col}", col), IsCheckBox = true, Tooltip = $"{q.Title}: {row}, {col}" };
                            var cell = new Cell().SetBorder(Border.NO_BORDER).SetBorderBottom(new SolidBorder(Rule, 0.5f))
                                .SetTextAlignment(TextAlignment.CENTER).SetVerticalAlignment(VerticalAlignment.MIDDLE).SetPadding(4);
                            cell.Add(new Paragraph().Add(FieldPlaceholder.Create(f, 11, 11, radio ? PlaceholderLook.Round : PlaceholderLook.Box, fields)));
                            table.AddCell(cell);
                        }
                    }
                    block.Add(table);
                    break;
                }
            }
            doc.Add(block);
        }

        doc.Close();
    }

    private static Cell Plain(string text, Color? color, float size)
    {
        var p = new Paragraph(text).SetFontSize(size);
        if (color != null) p.SetFontColor(color);
        return new Cell().SetBorder(Border.NO_BORDER).SetPadding(3).Add(p);
    }

    private static (PdfFont Regular, PdfFont Bold) Fonts()
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var (r, b) in new[] { ("segoeui.ttf", "segoeuib.ttf"), ("arial.ttf", "arialbd.ttf") })
        {
            string rp = System.IO.Path.Combine(dir, r), bp = System.IO.Path.Combine(dir, b);
            if (!File.Exists(rp) || !File.Exists(bp)) continue;
            try
            {
                return (PdfFontFactory.CreateFont(rp, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED),
                        PdfFontFactory.CreateFont(bp, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED));
            }
            catch { }
        }
        return (PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA),
                PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA_BOLD));
    }
}
