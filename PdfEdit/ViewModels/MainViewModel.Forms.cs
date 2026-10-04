using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Acrobat Pro "Prepare Form" support: automatic field names, Field Properties, Fields panel.</summary>
public partial class MainViewModel
{
    /// <summary>Asks the live view to select a field in Edit Fields mode (from the Fields panel).</summary>
    public event Action<FormFieldInfo>? FieldSelectionRequested;

    /// <summary>Asks the main window to bring the Fields panel to the front.</summary>
    public event Action? FieldsPanelRequested;

    /// <summary>Acrobat-style automatic name: "Text1", "Check Box2", "Group1" … (first free number).</summary>
    public string NextFieldName(string prefix)
    {
        var used = new HashSet<string>(AllFields.Select(f => f.Name).Concat(AllFields.Select(f => f.DisplayName)),
                                       StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
            if (!used.Contains(prefix + i)) return prefix + i;
    }

    public void ShowFieldsPanel() => FieldsPanelRequested?.Invoke();

    /// <summary>Goes to the field's page and selects it for editing (Fields panel click).</summary>
    public void SelectFieldForEditing(FormFieldInfo field)
    {
        if (IsDesignMode) IsDesignMode = false;
        if (field.PageNumber - 1 != CurrentPageIndex) CurrentPageIndex = field.PageNumber - 1;
        SelectedField = field;
        FieldSelectionRequested?.Invoke(field);
    }

    /// <summary>Opens Acrobat's tabbed Field Properties dialog for <paramref name="field"/>.</summary>
    public void OpenFieldProperties(FormFieldInfo? field)
    {
        field ??= SelectedField;
        if (field == null) return;
        SelectedField = field;
        var dlg = new Dialogs.FieldPropertiesDialog(this, field) { Owner = System.Windows.Application.Current?.MainWindow };
        if (dlg.ShowDialog() == true) ApplyFieldPropertiesEdit(field, dlg.Result);
    }

    /// <summary>Copies the properties the dialog edits (not position / value).</summary>
    public static void CopyFieldProperties(FormFieldInfo from, FormFieldInfo to)
    {
        to.PendingName = from.PendingName;
        to.Tooltip = from.Tooltip;
        to.IsRequired = from.IsRequired;
        to.IsReadOnly = from.IsReadOnly;
        to.IsMultiline = from.IsMultiline;
        to.Alignment = from.Alignment;
        to.FontSize = from.FontSize;
        to.BorderColor = from.BorderColor;
        to.FillColor = from.FillColor;
        to.TextColor = from.TextColor;
        to.MaxLength = from.MaxLength;
        to.IsComb = from.IsComb;
        to.IsEditable = from.IsEditable;
        to.DateFormat = from.DateFormat;
        to.NumberFormat = from.NumberFormat;
        to.Decimals = from.Decimals;
        to.CurrencySymbol = from.CurrencySymbol;
        to.CalcOp = from.CalcOp;
        to.CalcFields = from.CalcFields.ToList();
        to.DefaultValue = from.DefaultValue;
        to.Options = from.Options.ToList();
    }

    /// <summary>Applies the dialog's result to every widget of the field (one undo step; written on save).</summary>
    public void ApplyFieldPropertiesEdit(FormFieldInfo field, FormFieldInfo edited)
    {
        var widgets = AllFields.Where(f => f.Name == field.Name).ToList();
        if (widgets.Count == 0) widgets.Add(field);
        var before = widgets.Select(w => { var c = new FormFieldInfo(); CopyFieldProperties(w, c); return (w, c); }).ToList();

        void Apply(IEnumerable<(FormFieldInfo W, FormFieldInfo Src)> set)
        {
            foreach (var (w, src) in set) CopyFieldProperties(src, w);
            ModifiedFieldNames.Add(field.Name);
            NotifySelectedFieldProperties();
            RecalculateFields();
            PageChanged?.Invoke();
        }
        var after = widgets.Select(w => (w, edited)).ToList();
        Apply(after);
        PushUndo(() => Apply(before), () => Apply(after));
        StatusText = $"Properties of \"{edited.DisplayName}\" updated. Save to write them to the PDF.";
    }
}
