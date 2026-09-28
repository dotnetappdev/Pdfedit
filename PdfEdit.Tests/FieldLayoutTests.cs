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
}
