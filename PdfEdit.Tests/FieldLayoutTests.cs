using System.IO;
using iText.Kernel.Pdf;
using PdfEdit.Models;
using PdfEdit.Services;
using Xunit;

namespace PdfEdit.Tests;

/// <summary>
/// Moving / resizing a form field in the live view (Acrobat "Prepare Form" style) must write the
/// new widget rectangle back to the PDF on save.
/// </summary>
public class FieldLayoutTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pdfedit-layout-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string MakePdfWithTextField(string fieldName)
    {
        string blank = Path.Combine(_dir, "blank.pdf");
        using (var doc = new PdfDocument(new PdfWriter(blank)))
            doc.AddNewPage();

        string withField = Path.Combine(_dir, "field.pdf");
        new PdfFormService().AddTextFormField(blank, withField, 1, 50, 600, 200, 20, fieldName);
        return withField;
    }

    [Fact]
    public void LoadDocument_AssignsWidgetIndex()
    {
        var info = new PdfFormService().LoadDocument(MakePdfWithTextField("name"));
        var field = Assert.Single(info.FormFields);
        Assert.Equal(0, field.WidgetIndex);
    }

    [Fact]
    public void SaveFull_WritesMovedAndResizedFieldBounds()
    {
        var svc = new PdfFormService();
        string src = MakePdfWithTextField("name");
        string dest = Path.Combine(_dir, "moved.pdf");

        var bounds = new Dictionary<(string Name, int WidgetIndex), FieldBounds>
        {
            [("name", 0)] = new FieldBounds(Left: 120, Bottom: 400, Width: 300, Height: 36),
        };

        var errors = svc.SaveFull(src, dest, new Dictionary<string, string> { ["name"] = "Alice" },
            new Dictionary<int, int>(), Array.Empty<FreeTextAnnotation>(), fieldBounds: bounds);
        Assert.Empty(errors);

        var field = Assert.Single(svc.LoadDocument(dest).FormFields);
        Assert.Equal(120, field.Left, 1);
        Assert.Equal(400, field.Bottom, 1);
        Assert.Equal(300, field.Width, 1);
        Assert.Equal(36, field.Height, 1);
        Assert.Equal("Alice", field.Value);
    }

    [Fact]
    public void SaveFull_IgnoresBoundsForUnknownOrDeletedFields()
    {
        var svc = new PdfFormService();
        string src = MakePdfWithTextField("name");
        string dest = Path.Combine(_dir, "ignored.pdf");

        var bounds = new Dictionary<(string Name, int WidgetIndex), FieldBounds>
        {
            [("missing", 0)] = new FieldBounds(1, 1, 10, 10),
            [("name", 5)]    = new FieldBounds(1, 1, 10, 10),
        };

        var errors = svc.SaveFull(src, dest, new Dictionary<string, string>(),
            new Dictionary<int, int>(), Array.Empty<FreeTextAnnotation>(), fieldBounds: bounds);
        Assert.Empty(errors);

        var field = Assert.Single(svc.LoadDocument(dest).FormFields);
        Assert.Equal(50, field.Left, 1);
        Assert.Equal(600, field.Bottom, 1);
    }

    [Fact]
    public void SaveFull_WritesEditedPropertiesAndRename()
    {
        var svc = new PdfFormService();
        string src = MakePdfWithTextField("name");
        string dest = Path.Combine(_dir, "props.pdf");

        var edit = svc.LoadDocument(src).FormFields.Single();
        edit.IsRequired = true;
        edit.IsMultiline = true;
        edit.Tooltip = "Your full name";
        edit.Alignment = FieldAlignment.Center;
        edit.FontSize = 14;
        edit.PendingName = "full_name";

        var errors = svc.SaveFull(src, dest, new Dictionary<string, string> { ["name"] = "Alice" },
            new Dictionary<int, int>(), Array.Empty<FreeTextAnnotation>(), fieldEdits: new[] { edit });
        Assert.Empty(errors);

        var saved = Assert.Single(svc.LoadDocument(dest).FormFields);
        Assert.Equal("full_name", saved.Name);
        Assert.Equal("Alice", saved.Value);
        Assert.True(saved.IsRequired);
        Assert.True(saved.IsMultiline);
        Assert.Equal("Your full name", saved.Tooltip);
        Assert.Equal(FieldAlignment.Center, saved.Alignment);
        Assert.Equal(14, saved.FontSize, 1);
    }

    [Fact]
    public void SaveFull_WritesCharacterSpacingIntoFreeTextAppearance()
    {
        var svc = new PdfFormService();
        string src = MakePdfWithTextField("name");
        string dest = Path.Combine(_dir, "spacing.pdf");

        var ann = new FreeTextAnnotation
        {
            PageNumber = 1, Left = 100, Bottom = 500, Width = 120, Height = 20,
            Text = "1234", FontSize = 12, CharacterSpacing = 6,
        };
        var errors = svc.SaveFull(src, dest, new Dictionary<string, string>(),
            new Dictionary<int, int>(), new[] { ann });
        Assert.Empty(errors);

        using var doc = new PdfDocument(new PdfReader(dest));
        var freeText = doc.GetPage(1).GetAnnotations()
            .OfType<iText.Kernel.Pdf.Annot.PdfFreeTextAnnotation>().Single();
        Assert.Contains("6.00 Tc", freeText.GetDefaultAppearance().ToUnicodeString());
    }

    [Fact]
    public void NewFieldTypes_LoadWithTheRightTypesAndDateFormat()
    {
        var svc = new PdfFormService();
        string a = MakePdfWithTextField("name");
        string b = Path.Combine(_dir, "b.pdf"), c = Path.Combine(_dir, "c.pdf"), d = Path.Combine(_dir, "d.pdf");
        svc.AddDateField(a, b, 1, 50, 500, 100, 20, "Date1", "yyyy-mm-dd");
        svc.AddListBoxField(b, c, 1, 50, 400, 120, 60, "List Box1", new[] { "One", "Two" });
        svc.AddSignatureField(c, d, 1, 50, 300, 180, 40, "Signature1");

        var fields = svc.LoadDocument(d).FormFields;
        Assert.Equal("yyyy-mm-dd", fields.Single(f => f.Name == "Date1").DateFormat);
        Assert.Equal(FieldType.ListBox, fields.Single(f => f.Name == "List Box1").FieldType);
        Assert.Equal(new[] { "One", "Two" }, fields.Single(f => f.Name == "List Box1").Options);
        Assert.Equal(FieldType.Signature, fields.Single(f => f.Name == "Signature1").FieldType);
    }

    [Fact]
    public void SaveFull_WritesAcrobatAppearanceAndOptions()
    {
        var svc = new PdfFormService();
        string src = MakePdfWithTextField("name");
        string dest = Path.Combine(_dir, "appearance.pdf");

        var edit = svc.LoadDocument(src).FormFields.Single();
        edit.BorderColor = "#FF0000";
        edit.FillColor = "#DDE7FF";
        edit.TextColor = "#0000FF";
        edit.MaxLength = 8;
        edit.IsComb = true;
        edit.DateFormat = "dd/mm/yyyy";

        Assert.Empty(svc.SaveFull(src, dest, new Dictionary<string, string>(), new Dictionary<int, int>(),
            Array.Empty<FreeTextAnnotation>(), fieldEdits: new[] { edit }));

        var saved = Assert.Single(svc.LoadDocument(dest).FormFields);
        Assert.Equal("#FF0000", saved.BorderColor);
        Assert.Equal("#DDE7FF", saved.FillColor);
        Assert.Equal("#0000FF", saved.TextColor);
        Assert.Equal(8, saved.MaxLength);
        Assert.True(saved.IsComb);
        Assert.Equal("dd/mm/yyyy", saved.DateFormat);
    }

    [Theory]
    [InlineData("dd/mm/yyyy", "05/03/2026")]
    [InlineData("mm/dd/yyyy", "03/05/2026")]
    [InlineData("yyyy-mm-dd", "2026-03-05")]
    [InlineData("d mmm yyyy", "5 Mar 2026")]
    public void FormatAcrobatDate_UsesAcrobatTokens(string format, string expected)
    {
        var date = new DateTime(2026, 3, 5);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try { Assert.Equal(expected, PdfEdit.Controls.PdfViewerControl.FormatAcrobatDate(date, format)); }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }
}
