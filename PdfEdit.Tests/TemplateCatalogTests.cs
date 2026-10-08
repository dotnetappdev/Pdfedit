using System.IO;
using iText.Forms;
using iText.Kernel.Pdf;
using PdfEdit.Services;
using PdfEdit.Templates;
using Xunit;

namespace PdfEdit.Tests;

/// <summary>The shared template catalog: every template builds, exports and (when fillable) has fields.</summary>
public class TemplateCatalogTests
{
    public static IEnumerable<object[]> Ids => TemplateCatalog.All.Select(t => new object[] { t.Id });

    [Fact]
    public void Ids_AreUnique_AndEveryCategoryIsUsed()
    {
        var ids = TemplateCatalog.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(TemplateCatalog.Categories, c => Assert.Contains(TemplateCatalog.All, t => t.Category == c));
        Assert.All(TemplateCatalog.All, t => Assert.Contains(t.Category, TemplateCatalog.Categories));
    }

    [Fact]
    public void ClassicTemplates_KeepTheirIds()
    {
        // The Design tab's buttons (desktop and web) load these by id.
        foreach (var id in new[] { "Invoice", "Letter", "Form", "Certificate", "BusinessCard", "Resume", "Flyer" })
            Assert.NotNull(TemplateCatalog.Find(id));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Template_ExportsToPdf_WithFieldsWhenFillable(string id)
    {
        var template = TemplateCatalog.Find(id)!;
        var design = template.Create();
        Assert.NotEmpty(design.Elements!);
        var path = Path.Combine(Path.GetTempPath(), $"template-test-{Guid.NewGuid():N}.pdf");
        try
        {
            DesignPdfExporter.Export(design, path);
            using var pdf = new PdfDocument(new PdfReader(path));
            Assert.Equal(1, pdf.GetNumberOfPages());
            int fields = PdfAcroForm.GetAcroForm(pdf, false)?.GetAllFormFields().Count ?? 0;
            if (template.Fillable) Assert.True(fields > 0, $"{id} is marked fillable but has no fields");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Search_MatchesTitlesTagsAndCategories()
    {
        Assert.Contains(TemplateCatalog.Search(null, "invoice"), t => t.Id == "InvoiceModern");
        Assert.Contains(TemplateCatalog.Search(null, "cv"), t => t.Category == TemplateCatalog.Resumes);
        Assert.All(TemplateCatalog.Search(TemplateCatalog.Forms, null), t => Assert.Equal(TemplateCatalog.Forms, t.Category));
        Assert.Empty(TemplateCatalog.Search(null, "zzzz-no-such-template"));
    }
}
