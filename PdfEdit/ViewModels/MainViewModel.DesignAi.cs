using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Design → Design Form with AI: describe a form, and the AI lays it out on the canvas.</summary>
public partial class MainViewModel
{
    private ICommand? _designFormWithAiCommand;

    public ICommand DesignFormWithAiCommand => _designFormWithAiCommand ??= new RelayCommand(() => DesignFormWithAi());

    /// <summary>Opens the dialog (optionally pre-filled) and puts the designed form on the canvas.</summary>
    public bool DesignFormWithAi(string? description = null)
    {
        if (!RequireAi()) return false;
        string provider = _aiProvider, model = _aiModel, key = CurrentAiKey;
        var dlg = new Dialogs.DesignFormAiDialog(
            (prompt, ct) => AiProviderService.CompleteAsync(prompt, provider, model, key, ct, FormDesignAiService.SystemPrompt),
            description)
        { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Spec == null) return false;
        ApplyFormDesign(dlg.Spec, dlg.UseLetter ? DesignPageSize.Letter : DesignPageSize.A4, dlg.Accent);
        return true;
    }

    private void ApplyFormDesign(FormSpec spec, DesignPageSize size, Color accent)
    {
        double w = size == DesignPageSize.Letter ? 612 : 595, h = size == DesignPageSize.Letter ? 792 : 842;
        var layout = FormDesignAiService.Layout(spec, w, h, accent);

        // A new form stands on its own: it isn't tied to the open PDF's page, so switching views
        // never writes its fields into that PDF. Export PDF (or Open as PDF) makes the fillable file.
        _designSourceKey = null;
        if (layout.Grew) DesignCanvas.ReplaceAll(layout.Elements, DesignPageSize.Custom, w, layout.PageHeight);
        else DesignCanvas.ReplaceAll(layout.Elements, size);
        IsDesignMode = true;

        string title = string.IsNullOrWhiteSpace(spec.Title) ? "Your form" : spec.Title.Trim();
        StatusText = $"{title}: {spec.FieldCount} fields in {spec.Sections.Count} sections. Move or resize anything, then Export PDF to save it as a fillable form.";
        ToastService.Instance.Success(layout.Grew
            ? $"“{title}” is ready. It's longer than one page, so the page was made taller."
            : $"“{title}” is ready on the Design canvas.");
    }
}
