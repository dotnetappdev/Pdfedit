using System.IO;
using PdfEdit.Services;
using PdfEdit.Templates;

namespace PdfEdit.ViewModels;

// Templates (File → New from Template): put one on the Design canvas, or make it a PDF straight away.
public partial class MainViewModel
{
    /// <summary>Makes the template into a PDF (with its form fields) and opens it to fill in and save.</summary>
    public async Task OpenTemplateAsPdfAsync(string templateId)
    {
        var template = TemplateCatalog.Find(templateId);
        if (template == null) return;
        try
        {
            // Under its own name, so the tab and Save As show "Invoice.pdf" rather than a temp name.
            var folder = Path.Combine(Path.GetTempPath(), "PdfEdit Templates");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, template.Title + ".pdf");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{template.Title} ({n}).pdf");
            await Task.Run(() => DesignPdfExporter.Export(template.Create(), path));
            IsDesignMode = false;
            await OpenFileAsync(path);
            StatusText = template.Fillable
                ? $"{template.Title} — click a field to fill it in; Save As keeps your copy."
                : $"{template.Title} — Save As keeps your copy.";
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Couldn't make the template.", ex); }
    }
}
