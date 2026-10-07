using PdfEdit.Drawing.Wpf;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.Services.Cloud;

namespace PdfEdit.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly PdfFormService _formService = new();
    private readonly IPdfRenderer _renderService = RendererFactory.Create();
    private readonly Services.AnnotationUndoService _undoService = new();

    private PdfDocumentInfo? _document;
    private int _currentPageIndex;
    private double _zoom = 1.0;
    private double _uiScale = 1.0;
    private string _statusText = "Ready — Open a PDF to begin.";
    private FormFieldInfo? _selectedField;
    private FreeTextAnnotation? _selectedAnnotation;
    private ActiveTool _activeTool = ActiveTool.Hand;
    private bool _isLoading;
    private bool _highlightFields = true;
    private string? _currentFilePath;
    private bool _showAiPanel;
    private bool _showSearchOverlay;
    private string _searchQuery = string.Empty;
    private bool _isAiRunning;

    // Font/style state for new and selected annotations
    private double _currentFontSize = 12;
    private string _currentFontFamily = "Arial";
    private bool _currentFontBold;
    private bool _currentFontItalic;
    private bool _currentFontUnderline;
    private string _currentFontColor = "#000000";
    private TextAlignment _currentTextAlignment = TextAlignment.Left;
    private bool _forceUpperCase;
    private bool _updatingFromAnnotation;

    // Page rotation: pageIndex → cumulative degrees
    private readonly Dictionary<int, int> _pageRotations = new();

    // Active highlight color used when dragging with the Highlight tool
    private string _activeHighlightColor = "#80FFFF00";
    public string ActiveHighlightColor
    {
        get => _activeHighlightColor;
        set { _activeHighlightColor = value ?? "#80FFFF00"; OnPropertyChanged(); }
    }

    // Library signature pending placement
    private byte[]? _pendingLibrarySignature;
    public byte[]? PendingLibrarySignature
    {
        get => _pendingLibrarySignature;
        set { _pendingLibrarySignature = value; OnPropertyChanged(); }
    }

    public ObservableCollection<FreeTextAnnotation> FreeTextAnnotations { get; } = new();
    public ObservableCollection<PlacedSignature> PlacedSignatures { get; } = new();
    public ObservableCollection<Models.HighlightAnnotation> HighlightAnnotations { get; } = new();
    public ObservableCollection<Models.RedactRegion> RedactionRegions { get; } = new();
    public ObservableCollection<Models.StickyNoteAnnotation> StickyNotes { get; } = new();
    public ObservableCollection<Models.ShapeAnnotation> ShapeAnnotations { get; } = new();
    // Insert text / Replace text marks
    public ObservableCollection<Models.TextEditMark> TextEditMarks { get; } = new();
    // Temp copy the page renderer reads when PdfEdit's own saved annotations are left out of it
    private string? _renderCopyPath;
    public ObservableCollection<SearchResult> SearchResults { get; } = new();
    public ObservableCollection<RecentFileEntry> RecentFileEntries { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? PageChanged;
    public event Action? DocumentLoaded;
    public event Action? AnnotationFormattingChanged;
    public event Action<double>? UiScaleChanged;
    public event Action<bool>? SearchOverlayToggled;
    public event Action? GoToPageRequested;

    // ── Available options ────────────────────────────────────────────────────

    public IList<string> AvailableFonts { get; } = new List<string>
    {
        "Arial", "Times New Roman", "Courier New", "Georgia", "Verdana",
        "Tahoma", "Calibri", "Segoe UI", "Helvetica Neue", "Palatino Linotype"
    };

    // ── Bindable Properties ──────────────────────────────────────────────────

    public PdfDocumentInfo? Document
    {
        get => _document;
        private set
        {
            _document = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(PageCountDisplay));
            OnPropertyChanged(nameof(PageCount));
        }
    }

    public bool HasDocument => _document != null;
    public string? CurrentFilePath => _currentFilePath;

    public async Task ReloadCurrentFileAsync()
    {
        if (_currentFilePath == null) return;
        // Keep in-memory work (placed text, values, moved / edited fields) across the reload —
        // it is written to the per-document state first and restored by LoadDocumentAsync.
        SaveDocumentState();
        await LoadDocumentAsync(_currentFilePath);
    }

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        set
        {
            if (_document == null) return;
            value = Math.Clamp(value, 0, _document.PageCount - 1);
            if (_currentPageIndex == value) return;
            _currentPageIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPageDisplay));
            OnPropertyChanged(nameof(CurrentPageRotation));
            PageChanged?.Invoke();
        }
    }

    public string CurrentPageDisplay => HasDocument ? $"Page {_currentPageIndex + 1} of {_document!.PageCount}" : "—";
    public string PageCountDisplay => HasDocument ? _document!.PageCount.ToString() : "0";
    public int PageCount => _document?.PageCount ?? 1;
    // Absolute rotation = PDF-stored rotation + session delta, shown in status bar
    public int CurrentPageRotation
    {
        get
        {
            int pdfRot = (_document != null && _currentPageIndex < _document.PageRotations.Count)
                ? _document.PageRotations[_currentPageIndex] : 0;
            return (pdfRot + GetPageRotation(_currentPageIndex) + 360) % 360;
        }
    }

    public double Zoom
    {
        get => _zoom;
        set
        {
            value = Math.Clamp(value, 0.1, 5.0);
            if (Math.Abs(_zoom - value) < 0.001) return;
            _zoom = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ZoomPercent));
            PageChanged?.Invoke();
        }
    }

    public string ZoomPercent => $"{(int)Math.Round(_zoom * 100)}%";

    public double UiScale
    {
        get => _uiScale;
        set
        {
            value = Math.Clamp(value, 0.5, 2.0);
            if (Math.Abs(_uiScale - value) < 0.01) return;
            _uiScale = value;
            OnPropertyChanged();
            UiScaleChanged?.Invoke(_uiScale);
            AppSettings.Current.UiScale = _uiScale;
            AppSettings.Current.Save();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public FormFieldInfo? SelectedField
    {
        get => _selectedField;
        set
        {
            _selectedField = value;
            OnPropertyChanged();
            NotifySelectedFieldProperties();
            // The Properties panel edits one thing at a time: a field or a placed text / mark.
            if (value != null && _selectedAnnotation != null) SelectedAnnotation = null;
        }
    }

    /// <summary>
    /// Value of the selected field for the properties panel. Writes go through UpdateFieldValue so
    /// they are saved with the form and pushed to the live-view control.
    /// </summary>
    public string SelectedFieldValue
    {
        get => _selectedField == null ? string.Empty
             : FieldValues.TryGetValue(_selectedField.Name, out var v) ? v : _selectedField.Value;
        set
        {
            if (_selectedField == null || value == SelectedFieldValue) return;
            UpdateFieldValue(_selectedField.Name, value ?? string.Empty);
            FieldValueChangedExternally?.Invoke(_selectedField.Name, value ?? string.Empty);
        }
    }

    /// <summary>Raised when a field value is changed somewhere other than its live-view control.</summary>
    public event Action<string, string>? FieldValueChangedExternally;

    // ── Selected placed text / mark (Properties panel) ──────────────────────────
    // Font family / size / style / colour / alignment use the Current* properties above, which
    // already write through to the selected annotation. These cover the rest.

    /// <summary>Raised when a property of <see cref="SelectedAnnotation"/> changes, so the live view redraws it.</summary>
    public event Action<FreeTextAnnotation>? AnnotationChanged;

    private void NotifySelectedAnnotationProperties()
    {
        foreach (var n in new[] { nameof(HasSelectedAnnotation), nameof(SelectedAnnotationText), nameof(SelectedAnnotationRotation),
                                  nameof(SelectedAnnotationCharSpacing), nameof(SelectedAnnotationX), nameof(SelectedAnnotationY),
                                  nameof(SelectedAnnotationWidth), nameof(SelectedAnnotationHeight), nameof(SelectedAnnotationLocked),
                                  nameof(SelectedAnnotationAutoSize), nameof(SelectedAnnotationGrowToFit) })
            OnPropertyChanged(n);
        NotifySelectedDateProperties();
    }

    /// <summary>Called by the live view after it changes the selected annotation (drag, resize, typing).</summary>
    public void NotifyAnnotationEdited(FreeTextAnnotation ann)
    {
        if (ReferenceEquals(ann, _selectedAnnotation)) NotifySelectedAnnotationProperties();
    }

    private void EditSelectedAnnotation(Action<FreeTextAnnotation> apply)
    {
        if (_selectedAnnotation == null) return;
        apply(_selectedAnnotation);
        NotifySelectedAnnotationProperties();
        AnnotationChanged?.Invoke(_selectedAnnotation);
    }

    public bool HasSelectedAnnotation => _selectedAnnotation != null;

    public string SelectedAnnotationText
    {
        get => _selectedAnnotation?.Text ?? string.Empty;
        set { if (value != SelectedAnnotationText) EditSelectedAnnotation(a => a.Text = value ?? string.Empty); }
    }

    public IReadOnlyList<double> RotationAngles { get; } = new double[] { 0, 90, 180, 270 };

    /// <summary>Clockwise rotation in degrees (0 / 90 / 180 / 270).</summary>
    public double SelectedAnnotationRotation
    {
        get => _selectedAnnotation == null ? 0 : ((_selectedAnnotation.RotationAngle % 360) + 360) % 360;
        set { if (value != SelectedAnnotationRotation) EditSelectedAnnotation(a => a.RotationAngle = ((value % 360) + 360) % 360); }
    }

    /// <summary>Extra space between characters in points (0 = normal).</summary>
    public double SelectedAnnotationCharSpacing
    {
        get => _selectedAnnotation?.CharacterSpacing ?? 0;
        set
        {
            value = Math.Clamp(value, 0, 72);
            if (Math.Abs(value - SelectedAnnotationCharSpacing) > 0.001) EditSelectedAnnotation(a => a.CharacterSpacing = value);
        }
    }

    public double SelectedAnnotationX
    {
        get => _selectedAnnotation?.Left ?? 0;
        set => EditSelectedAnnotation(a => a.Left = value);
    }

    public double SelectedAnnotationY
    {
        get => _selectedAnnotation?.Bottom ?? 0;
        set => EditSelectedAnnotation(a => a.Bottom = value);
    }

    public double SelectedAnnotationWidth
    {
        get => _selectedAnnotation?.Width ?? 0;
        set => EditSelectedAnnotation(a => { a.Width = Math.Max(4, value); a.AutoSize = false; });
    }

    public double SelectedAnnotationHeight
    {
        get => _selectedAnnotation?.Height ?? 0;
        set => EditSelectedAnnotation(a => { a.Height = Math.Max(4, value); a.AutoSize = false; });
    }

    public bool SelectedAnnotationGrowToFit
    {
        get => _selectedAnnotation?.GrowToFit ?? true;
        set => EditSelectedAnnotation(a => a.GrowToFit = value);
    }

    public bool SelectedAnnotationAutoSize
    {
        get => _selectedAnnotation?.AutoSize ?? false;
        set => EditSelectedAnnotation(a => a.AutoSize = value);
    }

    public bool SelectedAnnotationLocked
    {
        get => _selectedAnnotation?.IsLocked ?? false;
        set => EditSelectedAnnotation(a => a.IsLocked = value);
    }

    public IReadOnlyList<TextAlignment> TextAlignmentChoices { get; } =
        new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right };

    // ── Editable field properties (Properties panel) ─────────────────────────────

    private IEnumerable<FormFieldInfo> WidgetsOf(FormFieldInfo f) => AllFields.Where(x => x.Name == f.Name);

    /// <summary>Applies a property change to every widget of the selected field, with undo.</summary>
    private void EditSelectedField(string description, Action<FormFieldInfo> apply, Action<FormFieldInfo> revert)
    {
        if (_selectedField == null) return;
        var widgets = WidgetsOf(_selectedField).ToList();
        if (widgets.Count == 0) widgets.Add(_selectedField);
        void Do()   { foreach (var w in widgets) apply(w);  ModifiedFieldNames.Add(widgets[0].Name); NotifySelectedFieldProperties(); PageChanged?.Invoke(); }
        void Undo() { foreach (var w in widgets) revert(w); NotifySelectedFieldProperties(); PageChanged?.Invoke(); }
        Do();
        PushUndo(Undo, Do);
        StatusText = $"{description}. Save to write it to the PDF.";
    }

    private void NotifySelectedFieldProperties()
    {
        foreach (var n in new[] { nameof(SelectedFieldName), nameof(SelectedFieldTooltip), nameof(SelectedFieldRequired),
                                  nameof(SelectedFieldReadOnly), nameof(SelectedFieldMultiline), nameof(SelectedFieldAlignment),
                                  nameof(SelectedFieldFontSize), nameof(SelectedFieldX), nameof(SelectedFieldY),
                                  nameof(SelectedFieldWidth), nameof(SelectedFieldHeight), nameof(SelectedFieldIsTextLike),
                                  nameof(SelectedFieldValue) })
            OnPropertyChanged(n);
    }

    /// <summary>Field name. A rename is shown immediately and written to the PDF on save.</summary>
    public string SelectedFieldName
    {
        get => _selectedField?.DisplayName ?? string.Empty;
        set
        {
            if (_selectedField == null) return;
            var newName = (value ?? string.Empty).Trim();
            var oldDisplay = _selectedField.DisplayName;
            if (newName == oldDisplay) return;
            var error = ValidateFieldName(_selectedField, newName);
            if (error != null)
            {
                ToastService.Instance.Warning(error);
                OnPropertyChanged(); // snap the text box back
                return;
            }
            var oldPending = _selectedField.PendingName;
            string? target = newName == _selectedField.Name ? null : newName;
            EditSelectedField($"Field renamed to \"{newName}\"", w => w.PendingName = target, w => w.PendingName = oldPending);
        }
    }

    /// <summary>Returns an error message, or null when <paramref name="newName"/> is a valid new name.</summary>
    public string? ValidateFieldName(FormFieldInfo field, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return "A field name cannot be empty.";
        // Hierarchical names ("parent.child"): only the last part can be changed.
        int dot = field.Name.LastIndexOf('.');
        string prefix = dot >= 0 ? field.Name[..(dot + 1)] : string.Empty;
        if (!newName.StartsWith(prefix, StringComparison.Ordinal) || newName[prefix.Length..].Contains('.'))
            return prefix.Length > 0
                ? $"Only the last part of \"{field.Name}\" can be renamed (keep \"{prefix}\")."
                : "Field names cannot contain '.'.";
        bool taken = AllFields.Any(f => f.Name != field.Name && (f.DisplayName == newName || f.Name == newName));
        return taken ? $"Another field is already called \"{newName}\"." : null;
    }

    public string SelectedFieldTooltip
    {
        get => _selectedField?.Tooltip ?? string.Empty;
        set
        {
            if (_selectedField == null || value == SelectedFieldTooltip) return;
            var old = _selectedField.Tooltip;
            EditSelectedField("Tooltip changed", w => w.Tooltip = value, w => w.Tooltip = old);
        }
    }

    public bool SelectedFieldRequired
    {
        get => _selectedField?.IsRequired ?? false;
        set
        {
            if (_selectedField == null || value == SelectedFieldRequired) return;
            EditSelectedField(value ? "Field marked required" : "Field no longer required",
                w => w.IsRequired = value, w => w.IsRequired = !value);
        }
    }

    public bool SelectedFieldReadOnly
    {
        get => _selectedField?.IsReadOnly ?? false;
        set
        {
            if (_selectedField == null || value == SelectedFieldReadOnly) return;
            EditSelectedField(value ? "Field made read-only" : "Field made editable",
                w => w.IsReadOnly = value, w => w.IsReadOnly = !value);
        }
    }

    public bool SelectedFieldMultiline
    {
        get => _selectedField?.IsMultiline ?? false;
        set
        {
            if (_selectedField == null || value == SelectedFieldMultiline) return;
            EditSelectedField(value ? "Field set to multi-line" : "Field set to single-line",
                w => w.IsMultiline = value, w => w.IsMultiline = !value);
        }
    }

    public bool SelectedFieldIsTextLike => _selectedField?.FieldType is FieldType.Text or FieldType.ComboBox or FieldType.ListBox;

    public FieldAlignment SelectedFieldAlignment
    {
        get => _selectedField?.Alignment ?? FieldAlignment.Left;
        set
        {
            if (_selectedField == null || value == SelectedFieldAlignment) return;
            var old = _selectedField.Alignment;
            EditSelectedField($"Text alignment: {value}", w => w.Alignment = value, w => w.Alignment = old);
        }
    }

    public IReadOnlyList<FieldAlignment> FieldAlignments { get; } = Enum.GetValues<FieldAlignment>();

    /// <summary>Font size in points; 0 = auto-size to fit the field.</summary>
    public double SelectedFieldFontSize
    {
        get => _selectedField?.FontSize ?? 0;
        set
        {
            value = Math.Clamp(value, 0, 144);
            if (_selectedField == null || Math.Abs(value - SelectedFieldFontSize) < 0.01) return;
            var old = _selectedField.FontSize;
            EditSelectedField(value == 0 ? "Font size: auto" : $"Font size: {value:0.#} pt",
                w => w.FontSize = value, w => w.FontSize = old);
        }
    }

    // Position / size of the selected widget, in PDF points (Y measured from the page bottom).
    public double SelectedFieldX      { get => _selectedField?.Left ?? 0;   set => SetSelectedFieldBounds(x: value); }
    public double SelectedFieldY      { get => _selectedField?.Bottom ?? 0; set => SetSelectedFieldBounds(y: value); }
    public double SelectedFieldWidth  { get => _selectedField?.Width ?? 0;  set => SetSelectedFieldBounds(w: value); }
    public double SelectedFieldHeight { get => _selectedField?.Height ?? 0; set => SetSelectedFieldBounds(h: value); }

    private void SetSelectedFieldBounds(double? x = null, double? y = null, double? w = null, double? h = null)
    {
        var f = _selectedField;
        if (f == null) return;
        var b = new FieldBounds(x ?? f.Left, y ?? f.Bottom, Math.Max(2, w ?? f.Width), Math.Max(2, h ?? f.Height));
        SetFieldBounds(f, b);
        NotifySelectedFieldProperties();
        PageChanged?.Invoke();
    }

    /// <summary>One entry per edited field (first widget), for PdfFormService.SaveFull.</summary>
    private List<FormFieldInfo> GetFieldEditsForSave() => AllFields
        .Where(f => ModifiedFieldNames.Contains(f.Name))
        .GroupBy(f => f.Name).Select(g => g.First()).ToList();

    /// <summary>
    /// After a successful save the PDF matches the model: apply pending renames to the in-memory
    /// keys (so the next save finds the fields under their new names) and clear the change sets.
    /// </summary>
    private void CommitFieldEditsAfterSave()
    {
        foreach (var group in AllFields.Where(f => !string.IsNullOrEmpty(f.PendingName) && f.PendingName != f.Name)
                                       .GroupBy(f => f.Name).ToList())
        {
            string oldName = group.Key, newName = group.First().PendingName!;
            foreach (var f in AllFields.Where(f => f.Name == oldName))
            {
                f.Name = newName;
                if (f.RadioGroup == oldName) f.RadioGroup = newName;
            }
            if (FieldValues.Remove(oldName, out var v)) FieldValues[newName] = v;
            if (FieldExportValues.Remove(oldName, out var ev)) FieldExportValues[newName] = ev;
        }
        foreach (var f in AllFields) f.PendingName = null;
        ModifiedFieldNames.Clear();
        ModifiedFieldBounds.Clear();
        DeletedFieldNames.Clear();
        SaveDocumentState();
        NotifySelectedFieldProperties();
        PageChanged?.Invoke();
    }

    public FreeTextAnnotation? SelectedAnnotation
    {
        get => _selectedAnnotation;
        set
        {
            _selectedAnnotation = value;
            OnPropertyChanged();
            NotifySelectedAnnotationProperties();
            if (value != null && _selectedField != null) SelectedField = null;
            if (value != null && _selectedGraphic != null) SelectedGraphic = null;
            if (value != null)
            {
                _updatingFromAnnotation = true;
                CurrentFontSize = value.FontSize;
                CurrentFontFamily = value.FontFamily;
                CurrentFontBold = value.IsBold;
                CurrentFontItalic = value.IsItalic;
                CurrentFontUnderline = value.IsUnderline;
                CurrentFontColor = value.FontColor;
                CurrentTextAlignment = value.TextAlignment.ToWpf();
                ForceUpperCase = value.ForceUpperCase;
                _updatingFromAnnotation = false;
            }
        }
    }

    public ActiveTool ActiveTool
    {
        get => _activeTool;
        set { _activeTool = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool HighlightFields
    {
        get => _highlightFields;
        set { _highlightFields = value; OnPropertyChanged(); PageChanged?.Invoke(); }
    }

    private string _currentHighlightColor = "#FFFF00";
    public string CurrentHighlightColor
    {
        get => _currentHighlightColor;
        set { _currentHighlightColor = value; OnPropertyChanged(); }
    }

    private string _currentDrawingColor = "#C62828";
    public string CurrentDrawingColor
    {
        get => _currentDrawingColor;
        set { _currentDrawingColor = value; OnPropertyChanged(); }
    }

    private string _currentFillColor = "";
    public string CurrentFillColor
    {
        get => _currentFillColor;
        set { _currentFillColor = value; OnPropertyChanged(); }
    }

    private float _currentHighlightOpacity = 0.4f;
    public float CurrentHighlightOpacity
    {
        get => _currentHighlightOpacity;
        set { _currentHighlightOpacity = Math.Max(0.1f, Math.Min(1.0f, value)); OnPropertyChanged(); }
    }

    private double _currentStrokeWidth = 2.0;
    public double CurrentStrokeWidth
    {
        get => _currentStrokeWidth;
        set { _currentStrokeWidth = Math.Max(0.5, Math.Min(20.0, value)); OnPropertyChanged(); }
    }

    public bool ShowAiPanel
    {
        get => _showAiPanel;
        set { _showAiPanel = value; OnPropertyChanged(); }
    }

    public bool ShowSearchOverlay
    {
        get => _showSearchOverlay;
        set
        {
            _showSearchOverlay = value;
            OnPropertyChanged();
            SearchOverlayToggled?.Invoke(value);
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            _searchQuery = value;
            OnPropertyChanged();
            PerformSearch(value);
        }
    }

    public bool IsAiRunning
    {
        get => _isAiRunning;
        set { _isAiRunning = value; OnPropertyChanged(); }
    }

    // ── Font/style properties ────────────────────────────────────────────────

    public double CurrentFontSize
    {
        get => _currentFontSize;
        set
        {
            value = Math.Clamp(value, 6, 144);
            if (Math.Abs(_currentFontSize - value) < 0.5) return;
            _currentFontSize = value;
            OnPropertyChanged();
            if (!_updatingFromAnnotation && _selectedAnnotation != null)
            {
                _selectedAnnotation.FontSize = value;
                AnnotationFormattingChanged?.Invoke();
            }
        }
    }

    public string CurrentFontFamily
    {
        get => _currentFontFamily;
        set
        {
            if (_currentFontFamily == value) return;
            _currentFontFamily = value ?? "Arial";
            OnPropertyChanged();
            if (!_updatingFromAnnotation && _selectedAnnotation != null)
            {
                _selectedAnnotation.FontFamily = _currentFontFamily;
                AnnotationFormattingChanged?.Invoke();
            }
        }
    }

    public bool CurrentFontBold
    {
        get => _currentFontBold;
        set
        {
            if (_currentFontBold == value) return;
            _currentFontBold = value;
            OnPropertyChanged();
            if (!_updatingFromAnnotation && _selectedAnnotation != null)
            {
                _selectedAnnotation.IsBold = value;
                AnnotationFormattingChanged?.Invoke();
            }
        }
    }

    public bool CurrentFontItalic
    {
        get => _currentFontItalic;
        set
        {
            if (_currentFontItalic == value) return;
            _currentFontItalic = value;
            OnPropertyChanged();
            if (!_updatingFromAnnotation && _selectedAnnotation != null)
            {
                _selectedAnnotation.IsItalic = value;
                AnnotationFormattingChanged?.Invoke();
            }
        }
    }

    public bool CurrentFontUnderline
    {
        get => _currentFontUnderline;
        set
        {
            if (_currentFontUnderline == value) return;
            _currentFontUnderline = value;
            OnPropertyChanged();
            if (!_updatingFromAnnotation && _selectedAnnotation != null)
            {
                _selectedAnnotation.IsUnderline = value;
                AnnotationFormattingChanged?.Invoke();
            }
        }
    }

    // Unit for the Measure tools (distance / perimeter / area labels): "in", "mm", "cm" or "pt".
    private string _measureUnit = "in";
    public string MeasureUnit
    {
        get => _measureUnit;
        set { _measureUnit = value is "in" or "mm" or "cm" or "pt" ? value : "in"; OnPropertyChanged(); }
    }

    // Colour for Complete & Sign marks picked from the toolbox; null = standard defaults
    // (green ✓, black ✕ ● ○ —).
    private string? _markColor;
    public string? MarkColor
    {
        get => _markColor;
        set { _markColor = string.IsNullOrEmpty(value) ? null : value; OnPropertyChanged(); }
    }

    /// <summary>The colour a ✓ / ✕ / ● / ○ / — mark is placed in.</summary>
    public string MarkColorFor(ActiveTool mark) =>
        _markColor ?? (mark == ActiveTool.Checkmark ? "#2E7D32" : "#000000");

    public string CurrentFontColor
    {
        get => _currentFontColor;
        set
        {
            if (_currentFontColor == value) return;
            _currentFontColor = value ?? "#000000";
            OnPropertyChanged();
            if (!_updatingFromAnnotation && _selectedAnnotation != null)
            {
                _selectedAnnotation.FontColor = _currentFontColor;
                AnnotationFormattingChanged?.Invoke();
            }
        }
    }

    public TextAlignment CurrentTextAlignment
    {
        get => _currentTextAlignment;
        set
        {
            if (_currentTextAlignment == value) return;
            _currentTextAlignment = value;
            OnPropertyChanged();
            if (!_updatingFromAnnotation && _selectedAnnotation != null)
            {
                _selectedAnnotation.TextAlignment = value.ToCore();
                AnnotationFormattingChanged?.Invoke();
            }
        }
    }

    public bool ForceUpperCase
    {
        get => _forceUpperCase;
        set
        {
            if (_forceUpperCase == value) return;
            _forceUpperCase = value;
            OnPropertyChanged();
            if (!_updatingFromAnnotation && _selectedAnnotation != null)
            {
                _selectedAnnotation.ForceUpperCase = value;
                AnnotationFormattingChanged?.Invoke();
            }
        }
    }

    // ── AI chat ───────────────────────────────────────────────────────────────

    private string _aiChatInput = string.Empty;
    private string _aiProvider = "Claude";
    private string _aiModel = "claude-opus-5-5";
    private CancellationTokenSource? _aiCts;
    private PersonalProfile? _selectedProfile;
    private string _documentText = string.Empty;  // extracted text for AI context

    public bool DocumentContextReady => !string.IsNullOrEmpty(_documentText);

    public string AiChatInput
    {
        get => _aiChatInput;
        set { _aiChatInput = value; OnPropertyChanged(); }
    }

    public string AiProvider
    {
        get => _aiProvider;
        set
        {
            if (_aiProvider == value) return;
            _aiProvider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProviderIcon));
            OnPropertyChanged(nameof(ModelDisplayLabel));
            SyncAiModels();
            AppSettings.Current.AiProvider = value;
            AppSettings.Current.Save();
        }
    }

    public string AiModel
    {
        get => _aiModel;
        set
        {
            if (_aiModel == value) return;
            _aiModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModelDisplayLabel));
            AppSettings.Current.AiModel = value;
            if (_aiProvider == Services.AiProviderService.LocalProvider) AppSettings.Current.LocalAiModel = value;
            AppSettings.Current.Save();
        }
    }

    public IList<string> AiProviders { get; } = Services.AiProviderService.Providers.Keys.ToList();
    public ObservableCollection<string> AiModels { get; } = new();
    public ObservableCollection<AiChatMessage> AiChatHistory { get; } = new();

    // ── Connection state ─────────────────────────────────────────────────────

    public bool IsClaudeConnected => !string.IsNullOrEmpty(AppSettings.Current.ClaudeApiKey);
    public bool IsOpenAiConnected => !string.IsNullOrEmpty(AppSettings.Current.OpenAiApiKey);
    public bool IsCopilotConnected => !string.IsNullOrEmpty(AppSettings.Current.GitHubToken);

    public bool IsLocalAiConnected => !string.IsNullOrWhiteSpace(AppSettings.Current.LocalAiEndpoint);

    /// <summary>The key for the selected provider (local servers usually need none).</summary>
    public string CurrentAiKey => _aiProvider switch
    {
        "OpenAI" => AppSettings.Current.OpenAiApiKey,
        Services.AiProviderService.CopilotProvider => AppSettings.Current.GitHubToken,
        Services.AiProviderService.LocalProvider => AppSettings.Current.LocalAiApiKey,
        _ => AppSettings.Current.ClaudeApiKey,
    };

    /// <summary>Ready to send: a cloud provider has its key; Local AI just needs a server address.</summary>
    public bool IsAiConfigured => _aiProvider == Services.AiProviderService.LocalProvider
        ? IsLocalAiConnected
        : !string.IsNullOrWhiteSpace(CurrentAiKey);

    public string ProviderIcon => _aiProvider switch
    {
        "OpenAI" => "☁",
        Services.AiProviderService.CopilotProvider => "⌬",
        Services.AiProviderService.LocalProvider => "🖥",
        _ => "✦",
    };

    public string ModelDisplayLabel
    {
        get
        {
            return Services.AiProviderService.ModelDisplayNames.TryGetValue(_aiModel, out var name) ? name : _aiModel;
        }
    }

    public void ConnectClaude(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        AppSettings.Current.ClaudeApiKey = apiKey.Trim();
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(IsClaudeConnected));
    }

    public void DisconnectClaude()
    {
        AppSettings.Current.ClaudeApiKey = string.Empty;
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(IsClaudeConnected));
    }

    public void ConnectOpenAi(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        AppSettings.Current.OpenAiApiKey = apiKey.Trim();
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(IsOpenAiConnected));
    }

    public void DisconnectOpenAi()
    {
        AppSettings.Current.OpenAiApiKey = string.Empty;
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(IsOpenAiConnected));
    }

    public void ConnectCopilot(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        AppSettings.Current.GitHubToken = token.Trim();
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(IsCopilotConnected));
    }

    public void DisconnectCopilot()
    {
        AppSettings.Current.GitHubToken = string.Empty;
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(IsCopilotConnected));
    }

    public ObservableCollection<PersonalProfile> Profiles => Services.PersonalProfileStore.All;

    public PersonalProfile? SelectedProfile
    {
        get => _selectedProfile;
        set { _selectedProfile = value; OnPropertyChanged(); }
    }

    // Thumbnails toggle
    private bool _showThumbnails;
    public bool ShowThumbnails
    {
        get => _showThumbnails;
        set { _showThumbnails = value; OnPropertyChanged(); }
    }

    // Legacy prompt/response for RunAiFillCommand ribbon button
    private string _aiPrompt = string.Empty;
    private string _aiResponse = string.Empty;

    public string AiPrompt
    {
        get => _aiPrompt;
        set { _aiPrompt = value; OnPropertyChanged(); }
    }

    public string AiResponse
    {
        get => _aiResponse;
        set { _aiResponse = value; OnPropertyChanged(); }
    }

    // ── Collections ──────────────────────────────────────────────────────────

    public Dictionary<string, string> FieldValues { get; } = new();
    // Maps field name → export/on-value (for checkboxes and radio buttons)
    public Dictionary<string, string> FieldExportValues { get; } = new();
    public ObservableCollection<FormFieldInfo> CurrentPageFields { get; } = new();
    public ObservableCollection<FormFieldInfo> AllFields { get; } = new();

    // Names of existing fields the user has deleted; stripped from the PDF on save.
    public HashSet<string> DeletedFieldNames { get; } = new();

    // Widget rectangles moved/resized in the live view (PDF points); written to the PDF on save.
    public Dictionary<(string Name, int WidgetIndex), FieldBounds> ModifiedFieldBounds { get; } = new();

    // Fields whose properties (name, tooltip, required, alignment …) were edited; written on save.
    public HashSet<string> ModifiedFieldNames { get; } = new();

    // Fields selected in the live view's Edit Fields mode (last = primary / reference for alignment).
    public ObservableCollection<FormFieldInfo> SelectedLayoutFields { get; } = new();

    public ObservableCollection<Models.BookmarkItem> Bookmarks { get; } = new();
    public ObservableCollection<Models.PdfAttachmentInfo> Attachments { get; } = new();

    public ICommand NavigateToBookmarkCommand { get; }
    public ICommand NavigateToPageCommand { get; }
    public ICommand UndoAnnotationCommand { get; }
    public ICommand RedoAnnotationCommand { get; }
    public ICommand AddAttachmentCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand ExtractAttachmentCommand { get; }

    // ── Undo / Redo ───────────────────────────────────────────────────────────

    private readonly Stack<(Action Undo, Action Redo)> _undoStack = new();
    private readonly Stack<(Action Undo, Action Redo)> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void PushUndo(Action undo, Action redo)
    {
        _undoStack.Push((undo, redo));
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        var entry = _undoStack.Pop();
        _redoStack.Push(entry);
        entry.Undo();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var entry = _redoStack.Pop();
        _undoStack.Push(entry);
        entry.Redo();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ── Viewer size (updated by PdfViewerControl on resize) ───────────────────

    private double _viewerWidth  = 800;
    private double _viewerHeight = 600;

    public void UpdateViewerSize(double w, double h)
    {
        _viewerWidth  = Math.Max(100, w);
        _viewerHeight = Math.Max(100, h);
    }

    private void FitPage()
    {
        if (_document == null || _currentPageIndex >= _document.PageSizes.Count) return;
        var ps = _document.PageSizes[_currentPageIndex];
        double dipW = ps.Width  * RendererFactory.PointsToDips;
        double dipH = ps.Height * RendererFactory.PointsToDips;
        double zoomW = (_viewerWidth  - 24) / dipW;
        double zoomH = (_viewerHeight - 24) / dipH;
        Zoom = Math.Max(0.1, Math.Min(zoomW, zoomH));
    }

    private void FitWidth()
    {
        if (_document == null || _currentPageIndex >= _document.PageSizes.Count) return;
        var ps = _document.PageSizes[_currentPageIndex];
        double dipW = ps.Width * RendererFactory.PointsToDips;
        Zoom = Math.Max(0.1, (_viewerWidth - 24) / dipW);
    }

    // Fired by PdfViewerControl after loading a document so zoom auto-fits.
    public void AutoFitOnLoad() => FitPage();

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand GoToPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomFitCommand { get; }
    public ICommand ZoomWidthCommand { get; }
    public ICommand ZoomActualCommand { get; }
    public ICommand ClearAllFieldsCommand { get; }
    public ICommand DeleteSelectedFieldCommand { get; }
    public ICommand ArrangeFieldsCommand { get; }
    public ICommand ExportDataCommand { get; }
    public ICommand ImportDataCommand { get; }
    public ICommand SetToolCommand { get; }
    public ICommand FlattenAndSaveCommand { get; }
    public ICommand RotatePageCWCommand { get; }
    public ICommand RotatePageCCWCommand { get; }
    public ICommand ResetPageRotationCommand { get; }
    public ICommand IncreaseFontSizeCommand { get; }
    public ICommand DecreaseFontSizeCommand { get; }
    public ICommand ToggleBoldCommand { get; }
    public ICommand ToggleItalicCommand { get; }
    public ICommand ToggleUnderlineCommand { get; }
    public ICommand DeleteAnnotationCommand { get; }
    public ICommand SetFontColorCommand { get; }
    public ICommand SetAlignmentCommand { get; }
    public ICommand ToggleForceUpperCaseCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ToggleAiPanelCommand { get; }
    public ICommand RunAiFillCommand { get; }
    public ICommand ToggleSearchCommand { get; }
    public ICommand IncreaseUiScaleCommand { get; }
    public ICommand DecreaseUiScaleCommand { get; }
    public ICommand NavigateToResultCommand { get; }
    public ICommand OpenRecentCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand ShowShortcutsCommand { get; }
    public ICommand SendAiChatCommand { get; }
    public ICommand ClearAiChatCommand { get; }
    public ICommand DeleteCurrentPageCommand { get; }
    public ICommand InsertBlankPageCommand { get; }
    public ICommand MergePdfCommand { get; }
    public ICommand InsertPdfCommand { get; }
    public ICommand ExtractCurrentPageCommand { get; }
    public ICommand ToggleThumbnailsCommand { get; }
    public ICommand ManageProfilesCommand { get; }
    public ICommand QuickFillWithProfileCommand { get; }
    public ICommand RotateAllPagesCWCommand { get; }
    public ICommand RotateAllPagesCCWCommand { get; }
    public ICommand SplitPdfCommand { get; }
    public ICommand MovePageUpCommand { get; }
    public ICommand MovePageDownCommand { get; }
    public ICommand InsertPageBeforeCommand { get; }
    public ICommand DuplicateCurrentPageCommand { get; }
    public ICommand CancelAiCommand { get; }
    public ICommand SummarizeDocumentCommand { get; }
    public ICommand SmartFillFromDocCommand { get; }
    public ICommand AnalyzeContractCommand { get; }
    public ICommand ExtractKeyDataCommand { get; }
    public ICommand FindPiiCommand { get; }
    public ICommand CompressPdfCommand { get; }
    public ICommand AddPageNumbersCommand { get; }
    public ICommand WatermarkCommand { get; }
    public ICommand DocumentPropertiesCommand { get; }
    public ICommand ExportPagesAsImagesCommand { get; }
    public ICommand FindReplaceFieldsCommand { get; }
    public ICommand ExportPdfACommand { get; }
    public ICommand ValidateRequiredFieldsCommand { get; }
    public ICommand ApplyRedactionsCommand { get; }
    public ICommand DuplicatePageCommand { get; }
    public ICommand AddHeaderFooterCommand { get; }
    public ICommand PasswordProtectCommand { get; }
    public ICommand RemovePasswordCommand { get; }
    public ICommand BatesNumberCommand { get; }
    public ICommand AddBookmarkCommand { get; }
    public ICommand CropPagesCommand { get; }
    public ICommand ExportTextCommand { get; }
    public ICommand AddTextFieldCommand { get; }
    public ICommand AddCheckboxFieldCommand { get; }
    public ICommand DeletePageRangeCommand { get; }
    public ICommand ExtractPageRangeCommand { get; }
    public ICommand ComparePdfsCommand { get; }
    public ICommand StickyNoteCommand { get; }
    public ICommand DrawRectangleCommand { get; }
    public ICommand DrawEllipseCommand { get; }
    public ICommand DrawArrowCommand { get; }
    public ICommand NewDesignCommand { get; }
    public ICommand CloseDesignCommand { get; }
    public ICommand ImportPdfPageCommand { get; }
    public ICommand ExportDesignCommand { get; }
    public ICommand OpenDesignInPdfViewCommand { get; }
    public ICommand ExportPageAsImageCommand { get; }
    public ICommand ExportXfdfCommand { get; }
    public ICommand ImportXfdfCommand { get; }
    public ICommand ExportAnnotationSummaryCommand { get; }
    public ICommand FindAndHighlightCommand { get; }
    public ICommand SetDrawingColorCommand { get; }
    public ICommand SetFillColorCommand { get; }
    public ICommand AddCustomStampCommand { get; }
    public ICommand DocumentStatisticsCommand { get; }
    public ICommand ImportFormDataFromJsonCommand { get; }

    // ── Design Canvas ─────────────────────────────────────────────────────────
    private bool _isDesignMode;
    public bool IsDesignMode
    {
        get => _isDesignMode;
        set
        {
            bool entering = !_isDesignMode && value;
            bool leaving = _isDesignMode && !value;
            _isDesignMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPdfMode));
            // Keep the two views in step: form fields moved / resized / renamed in one appear
            // the same in the other, and switching views never throws away work.
            if (leaving) SyncDesignFieldsToLive();
            if (entering) EnsureDesignImported();
        }
    }
    public bool IsPdfMode => !_isDesignMode;

    public DesignCanvasViewModel DesignCanvas { get; } = new();

    public MainViewModel()
    {
        // Load settings
        _uiScale = AppSettings.Current.UiScale;
        _currentFontFamily = AppSettings.Current.DefaultFontFamily;
        _currentFontSize = AppSettings.Current.DefaultFontSize;
        _currentFontColor = AppSettings.Current.DefaultFontColor;
        _currentDrawingColor = AppSettings.Current.DefaultDrawingColor;
        _forceUpperCase = AppSettings.Current.ForceUpperCaseDefault;
        _aiProvider = AppSettings.Current.AiProvider;
        _aiModel = Services.AiProviderService.UpgradeModelId(AppSettings.Current.AiModel);
        SyncAiModels();
        SyncStamps();

        NavigateToBookmarkCommand = new RelayCommand(p =>
        {
            if (p is Models.BookmarkItem bm && bm.PageNumber > 0)
                CurrentPageIndex = bm.PageNumber - 1;
        });

        NavigateToPageCommand = new RelayCommand(p =>
        {
            if (p is int pageNum && pageNum > 0)
                CurrentPageIndex = pageNum - 1;
        });

        UndoAnnotationCommand = new RelayCommand(() =>
        {
            _undoService.Undo();
            PageChanged?.Invoke();
            OnPropertyChanged(nameof(UndoAnnotationCommand));
            OnPropertyChanged(nameof(RedoAnnotationCommand));
        }, () => _undoService.CanUndo);
        RedoAnnotationCommand = new RelayCommand(() =>
        {
            _undoService.Redo();
            PageChanged?.Invoke();
            OnPropertyChanged(nameof(UndoAnnotationCommand));
            OnPropertyChanged(nameof(RedoAnnotationCommand));
        }, () => _undoService.CanRedo);

        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        OpenCommand = new AsyncRelayCommand(OpenAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasDocument);
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync, () => HasDocument);
        CloseCommand = new RelayCommand(CloseDocument, () => HasDocument);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => HasDocument);
        GoToPageCommand = new RelayCommand(() => GoToPageRequested?.Invoke(), () => HasDocument);
        NextPageCommand = new RelayCommand(() => CurrentPageIndex++,
            () => HasDocument && _currentPageIndex < (_document?.PageCount ?? 1) - 1);
        PreviousPageCommand = new RelayCommand(() => CurrentPageIndex--,
            () => HasDocument && _currentPageIndex > 0);
        FirstPageCommand = new RelayCommand(() => CurrentPageIndex = 0, () => HasDocument);
        LastPageCommand = new RelayCommand(() => CurrentPageIndex = (_document?.PageCount ?? 1) - 1, () => HasDocument);
        ZoomInCommand = new RelayCommand(() => Zoom += 0.1, () => HasDocument);
        ZoomOutCommand = new RelayCommand(() => Zoom -= 0.1, () => HasDocument);
        ZoomFitCommand  = new RelayCommand(FitPage,  () => HasDocument);
        ZoomWidthCommand = new RelayCommand(FitWidth, () => HasDocument);
        ZoomActualCommand = new RelayCommand(() => Zoom = 1.0, () => HasDocument);
        ClearAllFieldsCommand = new RelayCommand(ClearAllFields, () => HasDocument);
        DeleteSelectedFieldCommand = new RelayCommand(() => DeleteField(SelectedField), () => SelectedField != null);
        ArrangeFieldsCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<ArrangeOperation>(s, out var op)) ArrangeFields(op);
        }, _ => HasDocument);
        ExportDataCommand = new AsyncRelayCommand(ExportDataAsync, () => HasDocument);
        ImportDataCommand = new AsyncRelayCommand(ImportDataAsync, () => HasDocument);
        SetToolCommand = new RelayCommand(p =>
        {
            ActiveTool? tool = p is ActiveTool t ? t
                             : p is string s && Enum.TryParse<ActiveTool>(s, out var st) ? st : null;
            if (tool is not { } chosen) return;
            // Complete & Sign tools work on the Design canvas too; anything else needs Live View.
            if (IsDesignMode && SelectFillSignToolInDesign(chosen)) return;
            if (IsDesignMode) IsDesignMode = false;
            ActiveTool = chosen;
        });
        FlattenAndSaveCommand = new AsyncRelayCommand(FlattenAndSaveAsync, () => HasDocument);
        RotatePageCWCommand = new RelayCommand(() => RotatePage(+90), () => HasDocument);
        RotatePageCCWCommand = new RelayCommand(() => RotatePage(-90), () => HasDocument);
        ResetPageRotationCommand = new RelayCommand(() =>
        {
            _pageRotations.Remove(_currentPageIndex);
            OnPropertyChanged(nameof(CurrentPageRotation));
            PageChanged?.Invoke();
            StatusText = "Page rotation reset.";
        }, () => HasDocument);

        IncreaseFontSizeCommand = new RelayCommand(() => CurrentFontSize += 2, () => HasDocument);
        DecreaseFontSizeCommand = new RelayCommand(() => CurrentFontSize -= 2, () => HasDocument);
        ToggleBoldCommand = new RelayCommand(() => CurrentFontBold = !CurrentFontBold, () => HasDocument);
        ToggleItalicCommand = new RelayCommand(() => CurrentFontItalic = !CurrentFontItalic, () => HasDocument);
        ToggleUnderlineCommand = new RelayCommand(() => CurrentFontUnderline = !CurrentFontUnderline, () => HasDocument);
        DeleteAnnotationCommand = new RelayCommand(DeleteSelectedAnnotation, () => _selectedAnnotation != null);
        SetFontColorCommand = new RelayCommand(p =>
        {
            if (p is string color) CurrentFontColor = color;
        });
        SetAlignmentCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<TextAlignment>(s, out var align))
                CurrentTextAlignment = align;
        });
        ToggleForceUpperCaseCommand = new RelayCommand(() => ForceUpperCase = !ForceUpperCase);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ToggleAiPanelCommand = new RelayCommand(() => ShowAiPanel = !ShowAiPanel);
        RunAiFillCommand = new AsyncRelayCommand(RunAiFillAsync, () => HasDocument && !IsAiRunning);
        ToggleSearchCommand = new RelayCommand(() => ShowSearchOverlay = !ShowSearchOverlay);
        IncreaseUiScaleCommand = new RelayCommand(() => { UiScale = Math.Round(UiScale + 0.1, 1); StatusText = $"Interface size {UiScale:P0}."; });
        DecreaseUiScaleCommand = new RelayCommand(() => { UiScale = Math.Round(UiScale - 0.1, 1); StatusText = $"Interface size {UiScale:P0}."; });
        NavigateToResultCommand = new RelayCommand(p =>
        {
            if (p is SearchResult r) NavigateToSearchResult(r);
        });
        OpenRecentCommand = new RelayCommand(p =>
        {
            if (p is string path && !string.IsNullOrEmpty(path))
                _ = LoadDocumentAsync(path);
        });
        ShowAboutCommand = new RelayCommand(() =>
        {
            var dlg = new Dialogs.AboutDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
        });
        ShowShortcutsCommand = new RelayCommand(() =>
        {
            var dlg = new Dialogs.ShortcutsDialog { Owner = Application.Current.MainWindow };
            dlg.ShowDialog();
            if (dlg.ChangeRequested)
            {
                var settings = new Dialogs.SettingsWindow { Owner = Application.Current.MainWindow };
                settings.ShowTab("Keyboard");
                settings.ShowDialog();
            }
        });
        SendAiChatCommand = new AsyncRelayCommand(SendAiChatAsync, () => !_isAiRunning);
        ClearAiChatCommand = new RelayCommand(() =>
        {
            AiChatHistory.Clear();
            AiChatInput = string.Empty;
        });
        DeleteCurrentPageCommand = new AsyncRelayCommand(DeleteCurrentPageAsync, () => HasDocument && (_document?.PageCount ?? 1) > 1);
        InsertBlankPageCommand = new AsyncRelayCommand(InsertBlankPageAsync, () => HasDocument);
        MergePdfCommand   = new AsyncRelayCommand(MergePdfAsync,      () => HasDocument);
        InsertPdfCommand  = new AsyncRelayCommand(InsertPdfAsync,      () => HasDocument);
        ExtractCurrentPageCommand = new AsyncRelayCommand(ExtractCurrentPageAsync, () => HasDocument);
        ToggleThumbnailsCommand = new RelayCommand(() => ShowThumbnails = !ShowThumbnails);
        ManageProfilesCommand = new RelayCommand(OpenManageProfiles);
        QuickFillWithProfileCommand = new RelayCommand(QuickFillWithProfile,
            () => HasDocument && _selectedProfile != null);

        RotateAllPagesCWCommand  = new RelayCommand(() => RotateAllPages(+90), () => HasDocument);
        RotateAllPagesCCWCommand = new RelayCommand(() => RotateAllPages(-90), () => HasDocument);
        SplitPdfCommand          = new AsyncRelayCommand(SplitPdfAsync, () => HasDocument);
        WatermarkCommand            = new AsyncRelayCommand(WatermarkAsync, () => HasDocument);
        AddPageNumbersCommand       = new AsyncRelayCommand(AddPageNumbersAsync, () => HasDocument);
        CompressPdfCommand          = new AsyncRelayCommand(CompressPdfAsync, () => HasDocument);
        DocumentPropertiesCommand   = new AsyncRelayCommand(DocumentPropertiesAsync, () => HasDocument);
        ExportPagesAsImagesCommand  = new AsyncRelayCommand(ExportPagesAsImagesAsync, () => HasDocument);
        FindReplaceFieldsCommand    = new RelayCommand(FindReplaceFields, () => HasDocument && AllFields.Count > 0);
        ExportPdfACommand           = new AsyncRelayCommand(ExportPdfAAsync, () => HasDocument);
        ValidateRequiredFieldsCommand = new RelayCommand(ValidateRequiredFields, () => HasDocument);
        ApplyRedactionsCommand        = new AsyncRelayCommand(ApplyRedactionsAsync,
            () => HasDocument && RedactionRegions.Count > 0);
        DuplicatePageCommand          = new AsyncRelayCommand(DuplicatePageAsync, () => HasDocument);
        AddHeaderFooterCommand        = new AsyncRelayCommand(AddHeaderFooterAsync, () => HasDocument);
        PasswordProtectCommand        = new AsyncRelayCommand(PasswordProtectAsync, () => HasDocument);
        RemovePasswordCommand         = new AsyncRelayCommand(RemovePasswordAsync,  () => HasDocument);
        BatesNumberCommand            = new AsyncRelayCommand(BatesNumberAsync,     () => HasDocument);
        AddBookmarkCommand            = new AsyncRelayCommand(AddBookmarkAsync,      () => HasDocument);
        AddAttachmentCommand          = new AsyncRelayCommand(AddAttachmentAsync,    () => HasDocument);
        RemoveAttachmentCommand       = new AsyncRelayCommand(p => RemoveAttachmentAsync(p as Models.PdfAttachmentInfo), p => HasDocument && p is Models.PdfAttachmentInfo);
        ExtractAttachmentCommand      = new AsyncRelayCommand(p => ExtractAttachmentAsync(p as Models.PdfAttachmentInfo), p => HasDocument && p is Models.PdfAttachmentInfo);
        CropPagesCommand              = new AsyncRelayCommand(CropPagesAsync,        () => HasDocument);
        ExportTextCommand             = new AsyncRelayCommand(ExportTextAsync,       () => HasDocument);
        AddTextFieldCommand   = new RelayCommand(() => ActiveTool = ActiveTool.AddTextField,  () => HasDocument);
        AddCheckboxFieldCommand = new RelayCommand(() => ActiveTool = ActiveTool.AddCheckbox, () => HasDocument);
        DeletePageRangeCommand    = new AsyncRelayCommand(DeletePageRangeAsync,
            () => HasDocument && (_document?.PageCount ?? 1) > 1);
        ExtractPageRangeCommand   = new AsyncRelayCommand(ExtractPageRangeAsync, () => HasDocument);
        ComparePdfsCommand        = new AsyncRelayCommand(ComparePdfsAsync,       () => HasDocument);
        StickyNoteCommand         = new RelayCommand(() => ActiveTool = ActiveTool.StickyNote, () => HasDocument);
        DrawRectangleCommand      = new RelayCommand(() => ActiveTool = ActiveTool.DrawRectangle, () => HasDocument);
        DrawEllipseCommand        = new RelayCommand(() => ActiveTool = ActiveTool.DrawEllipse, () => HasDocument);
        DrawArrowCommand          = new RelayCommand(() => ActiveTool = ActiveTool.DrawArrow, () => HasDocument);
        ExportXfdfCommand         = new AsyncRelayCommand(ExportXfdfAsync,         () => HasDocument);
        ImportXfdfCommand         = new AsyncRelayCommand(ImportXfdfAsync,         () => HasDocument);
        ExportAnnotationSummaryCommand = new AsyncRelayCommand(ExportAnnotationSummaryAsync, () => HasDocument);
        FindAndHighlightCommand   = new AsyncRelayCommand(FindAndHighlightAsync,   () => HasDocument);
        SetDrawingColorCommand    = new RelayCommand(p => { if (p is string c) { CurrentDrawingColor = c; AppSettings.Current.DefaultDrawingColor = c; AppSettings.Current.Save(); } });
        SetFillColorCommand       = new RelayCommand(p => { if (p is string c) CurrentFillColor = c; });
        AddCustomStampCommand     = new RelayCommand(AddCustomStamp);
        DocumentStatisticsCommand = new AsyncRelayCommand(ShowDocumentStatisticsAsync, () => HasDocument);
        ImportFormDataFromJsonCommand = new AsyncRelayCommand(ImportFormDataFromJsonAsync, () => HasDocument);
        MovePageUpCommand   = new AsyncRelayCommand(MovePageUpAsync,
            () => HasDocument && _currentPageIndex > 0);
        MovePageDownCommand = new AsyncRelayCommand(MovePageDownAsync,
            () => HasDocument && _currentPageIndex < (_document?.PageCount ?? 1) - 1);
        InsertPageBeforeCommand = new AsyncRelayCommand(InsertPageBeforeAsync, () => HasDocument);
        DuplicateCurrentPageCommand = new AsyncRelayCommand(DuplicateCurrentPageAsync, () => HasDocument);

        CancelAiCommand = new RelayCommand(() => { _aiCts?.Cancel(); }, () => _isAiRunning);
        SummarizeDocumentCommand    = new AsyncRelayCommand(() => RunAnalysisPresetAsync("summarize"),  () => HasDocument && !_isAiRunning);
        SmartFillFromDocCommand     = new AsyncRelayCommand(() => RunAnalysisPresetAsync("smartfill"),  () => HasDocument && !_isAiRunning);
        AnalyzeContractCommand      = new AsyncRelayCommand(() => RunAnalysisPresetAsync("contract"),   () => HasDocument && !_isAiRunning);
        ExtractKeyDataCommand       = new AsyncRelayCommand(() => RunAnalysisPresetAsync("extract"),    () => HasDocument && !_isAiRunning);
        FindPiiCommand              = new AsyncRelayCommand(() => RunAnalysisPresetAsync("pii"),        () => HasDocument && !_isAiRunning);

        NewDesignCommand = new RelayCommand(() =>
        {
            // Switching to the Design tab keeps whatever is on the canvas; the current PDF page is
            // imported only the first time (or when a different page / document is open).
            IsDesignMode = true;
            if (!HasDocument && DesignCanvas.Elements.Count == 0)
                StatusText = "Design Canvas — draw shapes, text, and images to create a PDF from scratch.";
        });

        CloseDesignCommand = new RelayCommand(() =>
        {
            IsDesignMode = false;
            StatusText = HasDocument ? "PDF view." : "Ready.";
        });

        ImportPdfPageCommand = new RelayCommand(ImportCurrentPdfPageIntoDesign, () => HasDocument);

        ExportDesignCommand = new AsyncRelayCommand(ExportDesignAsync, () => IsDesignMode && DesignCanvas.Elements.Count > 0);

        OpenDesignInPdfViewCommand = new AsyncRelayCommand(async () =>
        {
            var tmp = System.IO.Path.GetTempFileName() + ".pdf";
            try
            {
                await Task.Run(() => Services.DesignExportService.ExportToPdf(
                    DesignCanvas.Elements, DesignCanvas.PageWidth, DesignCanvas.PageHeight, tmp, DesignCanvas.PageBackground));
                IsDesignMode = false;
                await LoadDocumentAsync(tmp);
                StatusText = "Design exported and opened as PDF.";
            }
            catch (Exception ex) { Dialogs.AppDialog.ShowError("Export failed.", ex); }
        }, () => IsDesignMode && DesignCanvas.Elements.Count > 0);

        ExportPageAsImageCommand = new AsyncRelayCommand(ExportPageAsImageAsync, () => HasDocument);
        InitAllToolsCommands();

        // Pre-select first profile if any exist
        if (Services.PersonalProfileStore.All.Count > 0)
            _selectedProfile = Services.PersonalProfileStore.All[0];

        SyncRecentFileEntries();
    }

    // ── Public Methods ───────────────────────────────────────────────────────

    /// <summary>Opens a PDF; Word, Excel and PowerPoint files are turned into a PDF first.</summary>
    public async Task OpenFileAsync(string path)
    {
        if (OfficeConversionService.IsOfficeFile(path)) await ConvertOfficeFilesAsync(new[] { path });
        else await LoadDocumentAsync(path);
    }

    public async Task ReorderPagesAsync(IEnumerable<int> newOrder, int navigateToIndex = 0)
    {
        if (_currentFilePath == null) return;
        var tmp = _currentFilePath + ".ptmp";
        try
        {
            var orderList = newOrder.ToList();
            await Task.Run(() => _formService.ReorderPages(_currentFilePath, tmp, orderList));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            await LoadDocumentAsync(_currentFilePath);
            CurrentPageIndex = Math.Clamp(navigateToIndex, 0, (_document?.PageCount ?? 1) - 1);
            ToastService.Instance.Success("Page moved.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Reorder failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    public IPdfRenderer RenderService => _renderService;

    public int GetPageRotation(int pageIndex)
        => _pageRotations.TryGetValue(pageIndex, out var r) ? r : 0;

    public void UpdateFieldValue(string fieldName, string value)
    {
        FieldValues[fieldName] = value;
        // Every widget of the field shares the value (radio groups, fields repeated across pages).
        foreach (var field in AllFields.Where(f => f.Name == fieldName))
            field.Value = value;
        if (_selectedField?.Name == fieldName) OnPropertyChanged(nameof(SelectedFieldValue));
        StatusText = $"Field '{fieldName}' updated.";
        RecalculateFields();
    }

    private bool _recalculating;

    /// <summary>
    /// Re-runs every calculated field (the usual AFSimple_Calculate: sum, product, average, min,
    /// max) after a value changes, repeating so totals of totals settle.
    /// </summary>
    public void RecalculateFields()
    {
        if (_recalculating) return;
        var calcs = AllFields.Where(f => !string.IsNullOrEmpty(f.CalcOp) && f.CalcFields.Count > 0)
                             .GroupBy(f => f.Name).Select(g => g.First()).ToList();
        if (calcs.Count == 0) return;
        _recalculating = true;
        try
        {
            for (int pass = 0; pass < 5; pass++)
            {
                bool changed = false;
                foreach (var c in calcs)
                {
                    double result = Services.FieldFormatting.Calculate(c.CalcOp!, c.CalcFields.Select(n => FieldValues.TryGetValue(n, out var v) ? v : null));
                    string stored = Math.Round(result, 10).ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
                    if (FieldValues.TryGetValue(c.Name, out var old) && old == stored) continue;
                    FieldValues[c.Name] = stored;
                    foreach (var w in AllFields.Where(f => f.Name == c.Name)) w.Value = stored;
                    FieldValueChangedExternally?.Invoke(c.Name, stored);
                    changed = true;
                }
                if (!changed) break;
            }
        }
        finally { _recalculating = false; }
    }

    public void ApplyValueToAllMatchingFields(string fieldName, string value)
    {
        int count = 0;
        foreach (var f in AllFields.Where(f => f.Name == fieldName))
        {
            f.Value = value;
            count++;
        }
        FieldValues[fieldName] = value;
        ToastService.Instance.Success($"Applied \"{value}\" to {count} field(s) named '{fieldName}'.");
        StatusText = $"Value applied to {count} matching field(s).";
    }

    public FreeTextAnnotation AddFreeTextAnnotation(double pdfX, double pdfY, double pdfW, double pdfH,
                                                     string text, bool isVertical,
                                                     double? fontSize = null)
    {
        var ann = new FreeTextAnnotation
        {
            PageNumber = _currentPageIndex + 1,
            Left = pdfX,
            Bottom = pdfY,
            Width = pdfW,
            Height = pdfH,
            Text = text,
            RotationAngle = isVertical ? -90.0 : 0.0,
            FontSize = fontSize ?? _currentFontSize,
            FontFamily = _currentFontFamily,
            IsBold = _currentFontBold,
            IsItalic = _currentFontItalic,
            IsUnderline = _currentFontUnderline,
            FontColor = _currentFontColor,
            TextAlignment = _currentTextAlignment.ToCore(),
            ForceUpperCase = _forceUpperCase,
        };
        FreeTextAnnotations.Add(ann);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = "Add text",
            Execute     = () => FreeTextAnnotations.Add(ann),
            Undo        = () => { FreeTextAnnotations.Remove(ann); if (_selectedAnnotation == ann) SelectedAnnotation = null; },
        });
        RefreshUndoCanExecute();
        return ann;
    }

    public void RemoveFreeTextAnnotation(FreeTextAnnotation ann)
    {
        FreeTextAnnotations.Remove(ann);
        if (_selectedAnnotation == ann) SelectedAnnotation = null;
        _undoService.Push(new Services.AnnotationAction
        {
            Description = "Delete text",
            Execute     = () => { FreeTextAnnotations.Remove(ann); if (_selectedAnnotation == ann) SelectedAnnotation = null; },
            Undo        = () => FreeTextAnnotations.Add(ann),
        });
        RefreshUndoCanExecute();
    }

    public IEnumerable<FreeTextAnnotation> GetAnnotationsForCurrentPage()
        => FreeTextAnnotations.Where(a => a.PageNumber == _currentPageIndex + 1);

    public void AddPlacedSignature(PlacedSignature sig) => PlacedSignatures.Add(sig);
    public void RemovePlacedSignature(PlacedSignature sig) => PlacedSignatures.Remove(sig);

    public IEnumerable<PlacedSignature> GetSignaturesForCurrentPage()
        => PlacedSignatures.Where(s => s.PageNumber == _currentPageIndex + 1);

    public void AddHighlightAnnotation(Models.HighlightAnnotation hl)
    {
        hl.PageNumber = _currentPageIndex + 1;
        HighlightAnnotations.Add(hl);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = $"Add {hl.Kind}",
            Execute     = () => HighlightAnnotations.Add(hl),
            Undo        = () => HighlightAnnotations.Remove(hl),
        });
        RefreshUndoCanExecute();
        PageChanged?.Invoke();
    }

    public void RemoveHighlightAnnotation(Models.HighlightAnnotation hl)
    {
        HighlightAnnotations.Remove(hl);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = "Delete highlight",
            Execute     = () => HighlightAnnotations.Remove(hl),
            Undo        = () => HighlightAnnotations.Add(hl),
        });
        RefreshUndoCanExecute();
        PageChanged?.Invoke();
    }

    public IEnumerable<Models.HighlightAnnotation> GetHighlightAnnotationsForCurrentPage()
        => HighlightAnnotations.Where(h => h.PageNumber == _currentPageIndex + 1);

    public void AddRedactRegion(Models.RedactRegion r)
    {
        r.PageNumber = _currentPageIndex + 1;
        RedactionRegions.Add(r);
        OnPropertyChanged(nameof(ApplyRedactionsCommand));
        PageChanged?.Invoke();
    }

    public void RemoveRedactRegion(Models.RedactRegion r)
    {
        RedactionRegions.Remove(r);
        OnPropertyChanged(nameof(ApplyRedactionsCommand));
        PageChanged?.Invoke();
    }

    public IEnumerable<Models.RedactRegion> GetRedactRegionsForCurrentPage()
        => RedactionRegions.Where(r => r.PageNumber == _currentPageIndex + 1);

    public void AddStickyNote(Models.StickyNoteAnnotation note)
    {
        note.PageNumber = _currentPageIndex + 1;
        StickyNotes.Add(note);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = "Add sticky note",
            Execute     = () => StickyNotes.Add(note),
            Undo        = () => StickyNotes.Remove(note),
        });
        RefreshUndoCanExecute();
    }

    public void RemoveStickyNote(Models.StickyNoteAnnotation note)
    {
        StickyNotes.Remove(note);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = "Delete sticky note",
            Execute     = () => StickyNotes.Remove(note),
            Undo        = () => StickyNotes.Add(note),
        });
        RefreshUndoCanExecute();
    }

    public void AddTextEditMark(Models.TextEditMark mark)
    {
        mark.PageNumber = _currentPageIndex + 1;
        TextEditMarks.Add(mark);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = mark.Kind == Models.TextEditKind.Insert ? "Insert text" : "Replace text",
            Execute     = () => TextEditMarks.Add(mark),
            Undo        = () => TextEditMarks.Remove(mark),
        });
        RefreshUndoCanExecute();
        NotifyCommentsChanged();
    }

    public void RemoveTextEditMark(Models.TextEditMark mark)
    {
        TextEditMarks.Remove(mark);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = "Delete text edit",
            Execute     = () => TextEditMarks.Remove(mark),
            Undo        = () => TextEditMarks.Add(mark),
        });
        RefreshUndoCanExecute();
        NotifyCommentsChanged();
    }

    public IEnumerable<Models.TextEditMark> GetTextEditMarksForCurrentPage()
        => TextEditMarks.Where(m => m.PageNumber == _currentPageIndex + 1);

    // ── Comments panel ────────────────────────────────────────────────────────

    /// <summary>A comment's note, replies, status or checkmark changed (not its position).</summary>
    public event Action? CommentsChanged;
    public void NotifyCommentsChanged() => CommentsChanged?.Invoke();

    /// <summary>Asks the window to bring the Comments panel to the front.</summary>
    public event Action? CommentsPanelRequested;
    public void ShowCommentsPanel() => CommentsPanelRequested?.Invoke();

    /// <summary>Goes to a comment's page and re-renders so it is on screen.</summary>
    public void GoToComment(int pageNumber)
    {
        if (IsDesignMode) IsDesignMode = false;
        if (Document == null) return;
        int index = Math.Clamp(pageNumber - 1, 0, Document.PageCount - 1);
        if (index != CurrentPageIndex) CurrentPageIndex = index;
        else PageChanged?.Invoke();
    }

    /// <summary>Re-draws the page after a comment was deleted from the panel.</summary>
    public void RefreshAfterCommentEdit() => PageChanged?.Invoke();

    public IEnumerable<Models.StickyNoteAnnotation> GetStickyNotesForCurrentPage()
        => StickyNotes.Where(n => n.PageNumber == _currentPageIndex + 1);

    public void AddShapeAnnotation(Models.ShapeAnnotation shape)
    {
        shape.PageNumber = _currentPageIndex + 1;
        ShapeAnnotations.Add(shape);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = $"Add {shape.Kind}",
            Execute     = () => ShapeAnnotations.Add(shape),
            Undo        = () => ShapeAnnotations.Remove(shape),
        });
        RefreshUndoCanExecute();
    }
    public void RemoveShapeAnnotation(Models.ShapeAnnotation shape)
    {
        ShapeAnnotations.Remove(shape);
        _undoService.Push(new Services.AnnotationAction
        {
            Description = $"Delete {shape.Kind}",
            Execute     = () => ShapeAnnotations.Remove(shape),
            Undo        = () => ShapeAnnotations.Add(shape),
        });
        RefreshUndoCanExecute();
    }
    public IEnumerable<Models.ShapeAnnotation> GetShapeAnnotationsForCurrentPage()
        => ShapeAnnotations.Where(s => s.PageNumber == _currentPageIndex + 1);

    private void RefreshUndoCanExecute()
    {
        OnPropertyChanged(nameof(UndoAnnotationCommand));
        OnPropertyChanged(nameof(RedoAnnotationCommand));
    }

    // ── Private Commands ─────────────────────────────────────────────────────

    private void DeleteSelectedAnnotation()
    {
        if (_selectedAnnotation == null) return;
        RemoveFreeTextAnnotation(_selectedAnnotation);
        PageChanged?.Invoke();
        StatusText = "Annotation deleted.";
        ToastService.Instance.Info("Annotation deleted.");
    }

    private async Task OpenAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open",
            Filter = "PDF and documents|*.pdf;*.doc;*.docx;*.docm;*.rtf;*.odt;*.xls;*.xlsx;*.ods;*.csv;*.ppt;*.pptx;*.odp;*.txt;*.md;*.markdown;*.html;*.htm" +
                     "|PDF Files (*.pdf)|*.pdf|Word documents|*.doc;*.docx;*.docm;*.rtf;*.odt|Text, Markdown and web pages|*.txt;*.md;*.markdown;*.html;*.htm|All Files (*.*)|*.*",
            DefaultExt = ".pdf"
        };
        if (dlg.ShowDialog() != true) return;
        await OpenFileAsync(dlg.FileName);
    }

    private async Task LoadDocumentAsync(string path)
    {
        IsLoading = true;
        IsDesignMode = false;
        StatusText = $"Loading {System.IO.Path.GetFileName(path)}…";

        try
        {
            _currentFilePath = path;
            TrackTab(path);
            ShowCachedSummary();
            Document = _formService.LoadDocument(path);

            FieldValues.Clear();
            FieldExportValues.Clear();
            AllFields.Clear();
            DeletedFieldNames.Clear();
            ModifiedFieldBounds.Clear();
            ModifiedFieldNames.Clear();
            SelectedLayoutFields.Clear();
            _pageRotations.Clear();
            FreeTextAnnotations.Clear();
            PlacedSignatures.Clear();
            HighlightAnnotations.Clear();
            RedactionRegions.Clear();
            StickyNotes.Clear();
            ShapeAnnotations.Clear();
            TextEditMarks.Clear();
            SelectedGraphic = null;
            _undoService.Clear();
            Bookmarks.Clear();
            Attachments.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));

            foreach (var f in Document.FormFields)
            {
                AllFields.Add(f);
                // For radio groups, all widgets share the same Name; Value is the group's current selection.
                // Only set FieldValues once per name (all widgets have the same group value).
                if (!FieldValues.ContainsKey(f.Name))
                    FieldValues[f.Name] = f.Value;
                // Track export values for checkboxes and radio buttons
                if (f.FieldType is Models.FieldType.Checkbox or Models.FieldType.RadioButton)
                    FieldExportValues[f.Name + "|" + f.ExportValue] = f.ExportValue;
            }

            _currentPageIndex = 0;
            _zoom = 1.0;

            // Restore per-document state (last page, zoom, annotations, signatures, field values)
            var docState = DocumentStateStore.Get(path);
            if (docState != null)
            {
                _currentPageIndex = Math.Clamp(docState.LastPageIndex, 0, Document.PageCount - 1);
                _zoom = Math.Clamp(docState.LastZoom, 0.1, 5.0);
                OnPropertyChanged(nameof(Zoom));
                OnPropertyChanged(nameof(ZoomPercent));

                foreach (var ann in docState.Annotations)
                    FreeTextAnnotations.Add(ann);
                foreach (var sig in docState.Signatures)
                    PlacedSignatures.Add(sig);
                // Comments added since the last save (they used to be lost on reopen)
                foreach (var hl in docState.Highlights ?? new()) HighlightAnnotations.Add(hl);
                foreach (var note in docState.StickyNotes ?? new()) StickyNotes.Add(note);
                foreach (var shape in docState.Shapes ?? new()) ShapeAnnotations.Add(shape);
                foreach (var mark in docState.TextEdits ?? new()) TextEditMarks.Add(mark);

                // Saved field values override the ones loaded from the PDF
                foreach (var kv in docState.FieldValues)
                    FieldValues[kv.Key] = kv.Value;

                RestoreFieldLayoutState(docState);
            }

            // PdfEdit draws its own annotations (editable). If an earlier save wrote them into the
            // file, render from a copy without them — otherwise they showed twice after reopening.
            var ownIds = new HashSet<string>(FreeTextAnnotations.Select(a => a.Comment.Id)
                .Concat(PlacedSignatures.Select(sg => sg.Id))
                .Concat(HighlightAnnotations.Select(h => h.Comment.Id))
                .Concat(StickyNotes.Select(n => n.Comment.Id))
                .Concat(ShapeAnnotations.Select(sh => sh.Comment.Id))
                .Concat(TextEditMarks.Select(t => t.Comment.Id)));
            if (_renderCopyPath != null) { try { System.IO.File.Delete(_renderCopyPath); } catch { } }
            _renderCopyPath = await Task.Run(() => _formService.CreateRenderCopyWithout(path, ownIds));
            await _renderService.LoadAsync(_renderCopyPath ?? path);

            OnPropertyChanged(nameof(CurrentPageIndex));
            OnPropertyChanged(nameof(CurrentPageRotation));
            RefreshCurrentPageFields();

            // Load bookmarks and attachments in background
            _ = Task.Run(() =>
            {
                try
                {
                    var bms = _formService.GetBookmarks(path);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var bm in bms) Bookmarks.Add(bm);
                    });
                }
                catch { /* non-critical */ }
            });
            _ = Task.Run(() =>
            {
                try
                {
                    var atts = _formService.GetAttachments(path);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var a in atts) Attachments.Add(a);
                    });
                }
                catch { /* non-critical */ }
            });

            // Record in recent files
            AppSettings.Current.AddRecentFile(path);
            SyncRecentFileEntries();

            DocumentLoaded?.Invoke();
            string msg = $"Opened: {System.IO.Path.GetFileName(path)} — " +
                         $"{Document.PageCount} page(s), {Document.FormFields.Count} field(s).";
            StatusText = msg;
            ToastService.Instance.Success($"Opened {System.IO.Path.GetFileName(path)}");

            // Extract text in background so AI has document context
            _documentText = string.Empty;
            OnPropertyChanged(nameof(DocumentContextReady));
            var extractPath = path;
            _ = Task.Run(() =>
            {
                var text = Services.PdfTextExtractorService.GetDocumentText(extractPath);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _documentText = text;
                    OnPropertyChanged(nameof(DocumentContextReady));
                });
            });
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Failed to open PDF", ex);
            if (OpenTabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) is { } bad)
            {
                OpenTabs.Remove(bad);
                OnPropertyChanged(nameof(HasTabs));
            }
            StatusText = "Error loading document.";
            ToastService.Instance.Error("Failed to open PDF.");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SaveAsync()
    {
        if (IsDesignMode) SyncDesignFieldsToLive(); // fields filled / signed on the Design canvas
        if (_currentFilePath == null) { await SaveAsAsync(); return; }

        var tmp = _currentFilePath + ".tmp";
        try
        {
            var errors = _formService.SaveFull(_currentFilePath, tmp, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: false,
                deletedFieldNames: DeletedFieldNames, fieldExportValues: BuildExportValuesForSave(),
                highlightAnnotations: HighlightAnnotations, stickyNotes: StickyNotes,
                shapeAnnotations: ShapeAnnotations, fieldBounds: ModifiedFieldBounds,
                fieldEdits: GetFieldEditsForSave(), textEdits: TextEditMarks);
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            System.IO.File.Delete(tmp);
            CommitFieldEditsAfterSave();
            StatusText = "Saved successfully.";
            if (errors.Count > 0)
            {
                ToastService.Instance.Warning($"Saved with {errors.Count} issue(s) — see details.");
                Dialogs.AppDialog.ShowError(
                    $"The file was saved but {errors.Count} field(s) could not be written:\n\n"
                    + string.Join("\n", errors.Take(10)),
                    title: "Saved with warnings");
            }
            else if (CloudStorage.LinkFor(_currentFilePath) == null)
            {
                ToastService.Instance.Success("Saved successfully.");
            }
            await UploadIfCloudAsync();
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Save failed", ex);
            ToastService.Instance.Error("Save failed.");
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task SaveAsAsync()
    {
        if (IsDesignMode) SyncDesignFieldsToLive(); // fields filled / signed on the Design canvas
        var dlg = new SaveFileDialog
        {
            Title = "Save PDF As",
            Filter = "PDF Files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = _currentFilePath != null
                ? System.IO.Path.GetFileName(_currentFilePath)
                : "document.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var errors = _formService.SaveFull(_currentFilePath!, dlg.FileName, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: false,
                deletedFieldNames: DeletedFieldNames, fieldExportValues: BuildExportValuesForSave(),
                highlightAnnotations: HighlightAnnotations, stickyNotes: StickyNotes,
                shapeAnnotations: ShapeAnnotations, fieldBounds: ModifiedFieldBounds,
                fieldEdits: GetFieldEditsForSave(), textEdits: TextEditMarks);
            _currentFilePath = dlg.FileName;
            RenameActiveTab(dlg.FileName);
            CommitFieldEditsAfterSave();
            StatusText = $"Saved as: {System.IO.Path.GetFileName(dlg.FileName)}";
            if (errors.Count > 0)
            {
                ToastService.Instance.Warning($"Saved with {errors.Count} issue(s).");
                Dialogs.AppDialog.ShowError(
                    $"The file was saved but {errors.Count} field(s) could not be written:\n\n"
                    + string.Join("\n", errors.Take(10)),
                    title: "Saved with warnings");
            }
            else
            {
                ToastService.Instance.Success($"Saved as {System.IO.Path.GetFileName(dlg.FileName)}");
            }
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Save failed", ex);
            ToastService.Instance.Error("Save failed.");
        }
    }

    private async Task FlattenAndSaveAsync()
    {
        if (IsDesignMode) SyncDesignFieldsToLive(); // fields filled / signed on the Design canvas
        var dlg = new SaveFileDialog
        {
            Title = "Save Flattened PDF",
            Filter = "PDF Files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = "flattened.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var errors = _formService.SaveFull(_currentFilePath!, dlg.FileName, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: true,
                deletedFieldNames: DeletedFieldNames, fieldExportValues: BuildExportValuesForSave(),
                highlightAnnotations: HighlightAnnotations, stickyNotes: StickyNotes,
                shapeAnnotations: ShapeAnnotations, fieldBounds: ModifiedFieldBounds,
                fieldEdits: GetFieldEditsForSave(), textEdits: TextEditMarks);
            StatusText = $"Flattened PDF saved: {System.IO.Path.GetFileName(dlg.FileName)}";
            if (errors.Count > 0)
            {
                ToastService.Instance.Warning($"Flattened with {errors.Count} issue(s).");
                Dialogs.AppDialog.ShowError(
                    $"The file was saved but {errors.Count} field(s) could not be written:\n\n"
                    + string.Join("\n", errors.Take(10)),
                    title: "Flattened with warnings");
            }
            else
            {
                ToastService.Instance.Success("Flattened PDF saved.");
            }
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Flatten & Save failed", ex);
            ToastService.Instance.Error("Flatten & Save failed.");
        }
    }

    private Dictionary<string, string> BuildExportValuesForSave()
    {
        var result = new Dictionary<string, string>();
        foreach (var kv in FieldExportValues)
        {
            // Key format: "FieldName|ExportValue"
            var sep = kv.Key.LastIndexOf('|');
            if (sep > 0)
                result[kv.Key[..sep]] = kv.Value;
        }
        return result;
    }

    private void CloseDocument()
    {
        SaveDocumentState();
        if (!_switchingTab && OpenTabs.FirstOrDefault(t => t.IsActive) is { } closing)
        {
            OpenTabs.Remove(closing);
            OnPropertyChanged(nameof(HasTabs));
        }
        _currentFilePath = null;
        Document = null;
        _documentText = string.Empty;
        ShowCachedSummary();
        OnPropertyChanged(nameof(DocumentContextReady));
        FieldValues.Clear();
        AllFields.Clear();
        CurrentPageFields.Clear();
        FreeTextAnnotations.Clear();
        PlacedSignatures.Clear();
        HighlightAnnotations.Clear();
        RedactionRegions.Clear();
        StickyNotes.Clear();
        ShapeAnnotations.Clear();
        TextEditMarks.Clear();
        _undoService.Clear();
        _pageRotations.Clear();
        Attachments.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        SelectedField = null;
        SelectedAnnotation = null;
        StatusText = "Document closed.";
        ToastService.Instance.Info("Document closed.");
    }

    private void ClearAllFields()
    {
        foreach (var f in AllFields)
        {
            f.Value = string.Empty;
            FieldValues[f.Name] = string.Empty;
        }
        PageChanged?.Invoke();
        StatusText = "All fields cleared.";
        ToastService.Instance.Info("All fields cleared.");
    }

    /// <summary>
    /// Removes an existing AcroForm field from the document. The field disappears
    /// from the UI immediately and is stripped from the PDF when it is next saved.
    /// </summary>
    public void DeleteField(FormFieldInfo? field)
    {
        if (field == null) return;

        DeletedFieldNames.Add(field.Name);
        foreach (var key in ModifiedFieldBounds.Keys.Where(k => k.Name == field.Name).ToList())
            ModifiedFieldBounds.Remove(key);
        AllFields.Remove(field);
        CurrentPageFields.Remove(field);
        FieldValues.Remove(field.Name);
        if (ReferenceEquals(SelectedField, field)) SelectedField = null;

        PageChanged?.Invoke();
        StatusText = $"Deleted field \"{field.Name}\". Save to make it permanent.";
        ToastService.Instance.Info($"Deleted field \"{field.Name}\".");
    }

    /// <summary>
    /// Moves/resizes a form field widget ("Form Builder" style). The new rectangle is in
    /// PDF points and is written to the PDF on the next save. Undoable with Ctrl+Z.
    /// </summary>
    public void SetFieldBounds(FormFieldInfo field, FieldBounds bounds, bool recordUndo = true)
    {
        var previous = new FieldBounds(field.Left, field.Bottom, field.Width, field.Height);
        if (previous == bounds) return;

        field.Left = bounds.Left;
        field.Bottom = bounds.Bottom;
        field.Width = bounds.Width;
        field.Height = bounds.Height;
        ModifiedFieldBounds[(field.Name, field.WidgetIndex)] = bounds;

        if (recordUndo)
        {
            PushUndo(
                () => { SetFieldBounds(field, previous, recordUndo: false); PageChanged?.Invoke(); },
                () => { SetFieldBounds(field, bounds, recordUndo: false); PageChanged?.Invoke(); });
        }
        StatusText = $"Field \"{field.Name}\" — X {bounds.Left:F0}, Y {bounds.Bottom:F0}, " +
                     $"W {bounds.Width:F0}, H {bounds.Height:F0} pt. Save to make it permanent.";
    }

    /// <summary>Moves / resizes several widgets as one undoable step.</summary>
    /// <param name="refresh">False when the caller (the live view) already shows the new layout.</param>
    public void SetFieldBoundsBatch(IReadOnlyList<(FormFieldInfo Field, FieldBounds Bounds)> changes, string description,
        bool refresh = true)
    {
        var before = changes.Select(c => (c.Field, Bounds: new FieldBounds(c.Field.Left, c.Field.Bottom, c.Field.Width, c.Field.Height))).ToList();
        if (changes.All(c => before.First(b => ReferenceEquals(b.Field, c.Field)).Bounds == c.Bounds)) return;

        void Apply(IEnumerable<(FormFieldInfo Field, FieldBounds Bounds)> set)
        {
            foreach (var (f, b) in set) SetFieldBounds(f, b, recordUndo: false);
            NotifySelectedFieldProperties();
            PageChanged?.Invoke();
        }
        if (refresh) Apply(changes);
        else
        {
            foreach (var (f, b) in changes) SetFieldBounds(f, b, recordUndo: false);
            NotifySelectedFieldProperties();
        }
        PushUndo(() => Apply(before), () => Apply(changes));
        StatusText = $"{description} ({changes.Count} field{(changes.Count == 1 ? "" : "s")}). Save to make it permanent.";
    }

    /// <summary>
    /// Align / distribute / size the fields selected in Edit Fields mode (reference = last
    /// selected). With one field, alignment is to the page.
    /// </summary>
    public void ArrangeFields(ArrangeOperation op)
    {
        var fields = SelectedLayoutFields.ToList();
        if (fields.Count == 0 && _selectedField != null) fields.Add(_selectedField);
        if (fields.Count == 0 || _document == null)
        {
            ToastService.Instance.Info("Select form fields with the Edit Fields tool first (Ctrl+click to add more).");
            return;
        }
        if (ArrangeHelper.NeedsThree(op) && fields.Count < 3) { ToastService.Instance.Info("Select at least three fields to distribute."); return; }
        if (ArrangeHelper.NeedsTwo(op) && fields.Count < 2) { ToastService.Instance.Info("Select at least two fields to match sizes."); return; }

        int pageIdx = fields[^1].PageNumber - 1;
        if (pageIdx < 0 || pageIdx >= _document.PageSizes.Count) return;
        var page = _document.PageSizes[pageIdx];
        double pageH = page.Height;

        // PDF (y-up) → top-left (y-down) and back.
        var rects = fields.Select(f => new Rect(f.Left, pageH - f.Bottom - f.Height, f.Width, f.Height)).ToList();
        var arranged = ArrangeHelper.Arrange(rects, fields.Count - 1, op, new Size(page.Width, page.Height));
        var changes = fields.Select((f, i) => (f, new FieldBounds(
            Math.Round(arranged[i].Left, 2), Math.Round(pageH - arranged[i].Bottom, 2),
            Math.Round(arranged[i].Width, 2), Math.Round(arranged[i].Height, 2)))).ToList();
        SetFieldBoundsBatch(changes, DescribeArrange(op));
    }

    public static string DescribeArrange(ArrangeOperation op) => op switch
    {
        ArrangeOperation.AlignLefts   => "Aligned lefts",
        ArrangeOperation.AlignCenters => "Aligned centres",
        ArrangeOperation.AlignRights  => "Aligned rights",
        ArrangeOperation.AlignTops    => "Aligned tops",
        ArrangeOperation.AlignMiddles => "Aligned middles",
        ArrangeOperation.AlignBottoms => "Aligned bottoms",
        ArrangeOperation.DistributeHorizontally => "Distributed horizontally",
        ArrangeOperation.DistributeVertically   => "Distributed vertically",
        ArrangeOperation.MakeSameWidth  => "Made same width",
        ArrangeOperation.MakeSameHeight => "Made same height",
        ArrangeOperation.MakeSameSize   => "Made same size",
        ArrangeOperation.CenterOnPageHorizontally => "Centred horizontally on page",
        ArrangeOperation.CenterOnPageVertically   => "Centred vertically on page",
        _ => op.ToString(),
    };

    private async Task ExportDataAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export Form Data",
            Filter = "Tab-Separated Values (*.tsv)|*.tsv|Text Files (*.txt)|*.txt",
            DefaultExt = ".tsv"
        };
        if (dlg.ShowDialog() != true) return;
        _formService.ExportFormData(_currentFilePath!, dlg.FileName, FieldValues);
        StatusText = $"Data exported to: {System.IO.Path.GetFileName(dlg.FileName)}";
        ToastService.Instance.Success($"Exported to {System.IO.Path.GetFileName(dlg.FileName)}");
    }

    private async Task ImportDataAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import Form Data",
            Filter = "Tab-Separated Values (*.tsv)|*.tsv|Text Files (*.txt)|*.txt"
        };
        if (dlg.ShowDialog() != true) return;

        var imported = _formService.ImportFormData(dlg.FileName);
        foreach (var (k, v) in imported)
        {
            FieldValues[k] = v;
            var f = AllFields.FirstOrDefault(f => f.Name == k);
            if (f != null) f.Value = v;
        }
        PageChanged?.Invoke();
        StatusText = $"Imported {imported.Count} field values.";
        ToastService.Instance.Success($"Imported {imported.Count} field values.");
    }

    private void RotatePage(int degrees)
    {
        int current = GetPageRotation(_currentPageIndex);
        int next = (current + degrees + 360) % 360;
        if (next == 0) _pageRotations.Remove(_currentPageIndex);
        else _pageRotations[_currentPageIndex] = next;

        OnPropertyChanged(nameof(CurrentPageRotation));
        PageChanged?.Invoke();
        StatusText = $"Page {_currentPageIndex + 1} rotated — total {CurrentPageRotation}°.";
    }

    private void RotateAllPages(int degrees)
    {
        if (_document == null) return;
        for (int i = 0; i < _document.PageCount; i++)
        {
            int current = GetPageRotation(i);
            int next = (current + degrees + 360) % 360;
            if (next == 0) _pageRotations.Remove(i);
            else _pageRotations[i] = next;
        }
        OnPropertyChanged(nameof(CurrentPageRotation));
        PageChanged?.Invoke();
        string dir = degrees > 0 ? "clockwise" : "counter-clockwise";
        StatusText = $"All {_document.PageCount} pages rotated 90° {dir}.";
        ToastService.Instance.Success($"All {_document.PageCount} pages rotated 90° {dir}.");
    }

    private async Task CompressPdfAsync()
    {
        if (_currentFilePath == null || _document == null) return;

        var dlgSave = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Compressed PDF",
            Filter = "PDF files|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) + "_compressed.pdf",
            InitialDirectory = System.IO.Path.GetDirectoryName(_currentFilePath)
        };
        if (dlgSave.ShowDialog() != true) return;

        try
        {
            StatusText = "Compressing PDF…";
            string outPath = dlgSave.FileName;
            var (orig, comp) = await Task.Run(() => _formService.CompressPdf(_currentFilePath, outPath));
            double savings = orig > 0 ? (1.0 - (double)comp / orig) * 100 : 0;
            string msg = $"Compressed {FormatBytes(orig)} → {FormatBytes(comp)} ({savings:F0}% saved)";
            StatusText = msg;
            ToastService.Instance.Success(msg);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not shrink PDF.", ex);
            StatusText = "Compression failed.";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F1} MB";
        if (bytes >= 1_000)     return $"{bytes / 1_000.0:F0} KB";
        return $"{bytes} B";
    }

    private async Task AddPageNumbersAsync()
    {
        if (_currentFilePath == null || _document == null) return;

        var dlgSave = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save PDF with Page Numbers",
            Filter = "PDF files|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) + "_numbered.pdf",
            InitialDirectory = System.IO.Path.GetDirectoryName(_currentFilePath)
        };
        if (dlgSave.ShowDialog() != true) return;

        try
        {
            StatusText = "Adding page numbers…";
            string outPath = dlgSave.FileName;
            await Task.Run(() => _formService.AddPageNumbers(_currentFilePath, outPath));
            StatusText = $"Page numbers added: {System.IO.Path.GetFileName(outPath)}";
            ToastService.Instance.Success("Page numbers added.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not add page numbers.", ex);
            StatusText = "Page numbering failed.";
        }
    }

    private Task WatermarkAsync() => WatermarkWithAsync(null, null);

    /// <summary>Watermark dialog, optionally starting with a stamp's text and colour (Stamps… → Use as background).</summary>
    private async Task WatermarkWithAsync(string? text, string? color)
    {
        if (_currentFilePath == null || _document == null) return;

        // Preview on the current page, as it is shown.
        System.Windows.Media.Imaging.BitmapSource? preview = null;
        try { preview = await _renderService.RenderPageAsync(_currentPageIndex, 1.0, 1.0); } catch { }
        var size = _document.PageSizes[Math.Clamp(_currentPageIndex, 0, _document.PageSizes.Count - 1)];
        string path = _currentFilePath;
        bool has = await Task.Run(() => Services.WatermarkService.HasWatermark(path));

        var dlg = new Dialogs.WatermarkDialog(preview, size.Width, size.Height, has) { Owner = Application.Current.MainWindow };
        if (text != null) dlg.UseText(text, color);
        if (dlg.ShowDialog() != true) return;

        if (dlg.RemoveRequested) { await RemoveWatermarkAsync(); return; }

        var opt = dlg.Options;
        int current = _currentPageIndex + 1;
        string what = string.IsNullOrEmpty(opt.ImagePath) ? $"'{opt.Text}'" : "Image";
        if (await ModifyCurrentFileAsync((i, o) => Services.WatermarkService.Apply(i, o, opt, current),
                $"{what} watermark added ({(opt.Behind ? "background" : "on top")})"))
            ToastService.Instance.Success("Watermark added. Ctrl+Z to undo, or Watermark → Remove.");
    }

    private async Task RemoveWatermarkAsync()
    {
        if (_currentFilePath == null) return;
        string path = _currentFilePath;
        if (!await Task.Run(() => Services.WatermarkService.HasWatermark(path)))
        {
            ToastService.Instance.Info("This PDF has no watermark added by PdfEdit.");
            return;
        }
        if (await ModifyCurrentFileAsync((i, o) => Services.WatermarkService.Remove(i, o), "Watermark removed"))
            ToastService.Instance.Success("Watermark removed.");
    }

    private ICommand? _removeWatermarkCommand;
    public ICommand RemoveWatermarkCommand => _removeWatermarkCommand ??= new AsyncRelayCommand(RemoveWatermarkAsync, () => HasDocument);

    private async Task DocumentPropertiesAsync()
    {
        if (_currentFilePath == null) return;
        try
        {
            var meta = await Task.Run(() => _formService.GetMetadata(_currentFilePath));
            var dlg  = new Dialogs.DocumentPropertiesDialog(meta)
            {
                Owner = Application.Current.MainWindow
            };
            if (dlg.ShowDialog() != true) return;

            var updated = dlg.Result!;
            var tmp = _currentFilePath + ".ptmp";
            try
            {
                await Task.Run(() => _formService.SetMetadata(_currentFilePath, tmp, updated));
                System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
                // Refresh the document info so the title bar / status reflects the change
                if (_document != null)
                {
                    _document.Title   = updated.Title;
                    _document.Author  = updated.Author;
                    _document.Subject = updated.Subject;
                }
                ToastService.Instance.Success("Document properties saved.");
            }
            finally
            {
                if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
            }
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not update document properties.", ex);
        }
    }

    private async Task ExportPagesAsImagesAsync()
    {
        if (_currentFilePath == null || _document == null) return;

        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose folder to save page images",
        };
        if (dlg.ShowDialog() != true) return;

        string folder   = dlg.FolderName;
        string baseName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);
        int total       = _document.PageCount;

        StatusText = $"Exporting {total} page(s) as images…";
        try
        {
            var renderer = RendererFactory.Create();
            await renderer.LoadAsync(_currentFilePath);

            for (int i = 0; i < total; i++)
            {
                StatusText = $"Exporting page {i + 1} of {total}…";
                var bmp = await renderer.RenderPageAsync(i, zoom: 2.0); // 192 DPI

                string outPath = System.IO.Path.Combine(folder, $"{baseName}_p{i + 1:D3}.png");
                await Task.Run(() =>
                {
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using var stream = System.IO.File.Create(outPath);
                    enc.Save(stream);
                });
            }

            StatusText = $"Exported {total} image(s) to {System.IO.Path.GetFileName(folder)}";
            ToastService.Instance.Success($"Exported {total} PNG image(s).");
            Dialogs.AppDialog.ShowInfo(
                $"Exported {total} page image(s) to:\n{folder}",
                "Export Complete");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Export failed.", ex);
            StatusText = "Export failed.";
        }
    }

    private void FindReplaceFields()
    {
        var dlg = new Dialogs.FindReplaceFieldsDialog(AllFields.ToList())
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() != true) return;

        string find    = dlg.FindText;
        string replace = dlg.ReplaceText;
        bool caseSens  = dlg.CaseSensitive;

        int count = 0;
        var comparison = caseSens
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        foreach (var field in AllFields)
        {
            if (field.FieldType == Models.FieldType.Text &&
                FieldValues.TryGetValue(field.Name, out var current) &&
                current.Contains(find, comparison))
            {
                string newVal = caseSens
                    ? current.Replace(find, replace, StringComparison.Ordinal)
                    : ReplaceIgnoreCase(current, find, replace);
                UpdateFieldValue(field.Name, newVal);
                count++;
            }
        }

        PageChanged?.Invoke();
        if (count > 0)
            ToastService.Instance.Success($"Replaced {count} field value(s).");
        else
            ToastService.Instance.Info("No matching field values found.");
    }

    private static string ReplaceIgnoreCase(string source, string find, string replace)
    {
        int idx = source.IndexOf(find, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return source;
        var sb = new System.Text.StringBuilder();
        int prev = 0;
        while (idx >= 0)
        {
            sb.Append(source, prev, idx - prev);
            sb.Append(replace);
            prev = idx + find.Length;
            idx = source.IndexOf(find, prev, StringComparison.OrdinalIgnoreCase);
        }
        sb.Append(source, prev, source.Length - prev);
        return sb.ToString();
    }

    private async Task ExportPdfAAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save as PDF/A-1b",
            Filter = "PDF/A files (*.pdf)|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) + "_pdfa.pdf",
            InitialDirectory = System.IO.Path.GetDirectoryName(_currentFilePath)
        };
        if (dlg.ShowDialog() != true) return;

        StatusText = "Converting to PDF/A-1b…";
        try
        {
            await Task.Run(() => _formService.ConvertToPdfA(_currentFilePath, dlg.FileName));
            StatusText = $"PDF/A-1b saved: {System.IO.Path.GetFileName(dlg.FileName)}";
            ToastService.Instance.Success("PDF/A conversion complete.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("PDF/A conversion failed.", ex);
            StatusText = "PDF/A conversion failed.";
        }
    }

    private async Task ApplyRedactionsAsync()
    {
        if (_currentFilePath == null || RedactionRegions.Count == 0) return;

        bool confirm = Dialogs.AppDialog.ShowConfirm(
            $"Apply {RedactionRegions.Count} redaction(s) to the document?\n\n" +
            "This permanently burns black boxes over the selected areas and saves the file. This action cannot be undone.",
            "Apply Redactions", isDanger: true);
        if (!confirm) return;

        StatusText = "Applying redactions…";
        try
        {
            var regions = RedactionRegions
                .Select(r => (r.PageNumber, (float)r.Left, (float)r.Bottom, (float)r.Width, (float)r.Height))
                .ToList();

            string tmp = _currentFilePath + ".tmp";
            await Task.Run(() => _formService.ApplyRedactions(_currentFilePath, tmp, regions));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            System.IO.File.Delete(tmp);

            RedactionRegions.Clear();
            StatusText = "Redactions applied. Reloading document…";
            ToastService.Instance.Success($"Redactions applied successfully.");
            await OpenFileAsync(_currentFilePath);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Redaction failed.", ex);
            StatusText = "Redaction failed.";
        }
    }

    private async Task DuplicatePageAsync()
    {
        if (_currentFilePath == null || _document == null) return;
        string tmp = _currentFilePath + ".tmp";
        try
        {
            int idx = _currentPageIndex;
            await Task.Run(() => _formService.DuplicatePage(_currentFilePath, tmp, idx));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            System.IO.File.Delete(tmp);
            StatusText = $"Page {idx + 1} duplicated.";
            ToastService.Instance.Success($"Page {idx + 1} duplicated.");
            await OpenFileAsync(_currentFilePath);
            CurrentPageIndex = idx + 1;
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Duplicate page failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task AddHeaderFooterAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Dialogs.HeaderFooterDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        if (string.IsNullOrWhiteSpace(dlg.HeaderText) && string.IsNullOrWhiteSpace(dlg.FooterText))
        {
            ToastService.Instance.Info("No header or footer text entered.");
            return;
        }

        string tmp = _currentFilePath + ".tmp";
        try
        {
            await Task.Run(() => _formService.AddHeaderFooter(
                _currentFilePath, tmp,
                string.IsNullOrWhiteSpace(dlg.HeaderText) ? null : dlg.HeaderText,
                string.IsNullOrWhiteSpace(dlg.FooterText) ? null : dlg.FooterText,
                dlg.FontSize, 18f, dlg.Alignment));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            System.IO.File.Delete(tmp);
            StatusText = "Header/footer added.";
            ToastService.Instance.Success("Header/footer added to all pages.");
            await OpenFileAsync(_currentFilePath);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Add header/footer failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task PasswordProtectAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Dialogs.PasswordProtectDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        string tmp = _currentFilePath + ".tmp";
        try
        {
            string userPwd  = dlg.UserPassword;
            string ownerPwd = dlg.OwnerPassword;
            bool print = dlg.AllowPrinting;
            bool copy  = dlg.AllowCopying;
            await Task.Run(() => _formService.EncryptPdf(_currentFilePath, tmp, userPwd, ownerPwd, print, copy));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            ToastService.Instance.Success("PDF password-protected successfully.");
            StatusText = "PDF protected with password.";
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Password protection failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task RemovePasswordAsync()
    {
        if (_currentFilePath == null) return;

        string tmp = _currentFilePath + ".tmp";
        try
        {
            await Task.Run(() => _formService.RemoveEncryption(_currentFilePath, tmp));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            ToastService.Instance.Success("PDF password removed.");
            StatusText = "PDF password removed.";
            await OpenFileAsync(_currentFilePath);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Remove password failed. If the PDF is encrypted, open it with the password first.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task AddBookmarkAsync()
    {
        if (_currentFilePath == null) return;
        int pageNum = _currentPageIndex + 1;
        string defaultTitle = $"Page {pageNum}";
        var dlg = new Dialogs.InputDialog("Add Bookmark", $"Enter bookmark title for page {pageNum}:", defaultTitle)
        { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;

        string title = dlg.InputText.Trim();
        string tmp = _currentFilePath + ".tmp";
        try
        {
            await Task.Run(() => _formService.AddBookmark(_currentFilePath, tmp, title, pageNum));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);

            var bms = _formService.GetBookmarks(_currentFilePath);
            Application.Current.Dispatcher.Invoke(() =>
            {
                Bookmarks.Clear();
                foreach (var bm in bms) Bookmarks.Add(bm);
            });
            StatusText = $"Bookmark '{title}' added at page {pageNum}.";
            ToastService.Instance.Success($"Bookmark added: {title}");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Add bookmark failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task AddAttachmentAsync()
    {
        if (_currentFilePath == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose file to attach",
            Filter = "All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        string filePath = dlg.FileName;
        string tmp = _currentFilePath + ".tmp";
        try
        {
            await Task.Run(() => _formService.AddAttachment(_currentFilePath, tmp, filePath));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            await RefreshAttachmentsAsync();
            ToastService.Instance.Success($"Attached: {System.IO.Path.GetFileName(filePath)}");
            StatusText = $"File attached: {System.IO.Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not attach file.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task RemoveAttachmentAsync(Models.PdfAttachmentInfo? att)
    {
        if (_currentFilePath == null || att == null) return;
        bool confirm = Dialogs.AppDialog.ShowConfirm($"Remove attachment '{att.Name}'?", "Remove Attachment");
        if (!confirm) return;

        string tmp = _currentFilePath + ".tmp";
        try
        {
            await Task.Run(() => _formService.RemoveAttachment(_currentFilePath, tmp, att.Name));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            await RefreshAttachmentsAsync();
            ToastService.Instance.Success($"Attachment removed: {att.Name}");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not remove attachment.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task ExtractAttachmentAsync(Models.PdfAttachmentInfo? att)
    {
        if (_currentFilePath == null || att == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save attachment as",
            FileName = att.Name,
            Filter = "All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            byte[] data = await Task.Run(() => _formService.ExtractAttachment(_currentFilePath, att.Name));
            await System.IO.File.WriteAllBytesAsync(dlg.FileName, data);
            ToastService.Instance.Success($"Saved: {System.IO.Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not extract attachment.", ex);
        }
    }

    private async Task RefreshAttachmentsAsync()
    {
        if (_currentFilePath == null) return;
        try
        {
            var list = await Task.Run(() => _formService.GetAttachments(_currentFilePath));
            Application.Current.Dispatcher.Invoke(() =>
            {
                Attachments.Clear();
                foreach (var a in list) Attachments.Add(a);
            });
        }
        catch { /* non-critical */ }
    }

    private async Task BatesNumberAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Dialogs.BatesNumberDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        string tmp = _currentFilePath + ".tmp";
        try
        {
            int    start    = dlg.StartNumber;
            int    padding  = dlg.Padding;
            string prefix   = dlg.Prefix;
            string suffix   = dlg.Suffix;
            float  fontSize = dlg.FontSize;
            string position = dlg.Position;
            await Task.Run(() => _formService.AddBatesNumbers(
                _currentFilePath, tmp, start, padding, prefix, suffix, fontSize, 18f, position));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            StatusText = "Bates numbers added.";
            ToastService.Instance.Success("Bates numbers added to all pages.");
            await OpenFileAsync(_currentFilePath);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Add Bates numbers failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task CropPagesAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Dialogs.CropPageDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        if (dlg.LeftMargin == 0 && dlg.RightMargin == 0 && dlg.TopMargin == 0 && dlg.BottomMargin == 0)
        {
            ToastService.Instance.Info("No crop margins specified.");
            return;
        }

        string tmp = _currentFilePath + ".tmp";
        try
        {
            float l = dlg.LeftMargin, r = -dlg.RightMargin, t = -dlg.TopMargin, b = dlg.BottomMargin;
            await Task.Run(() => _formService.CropAllPages(_currentFilePath, tmp, l, b, r, t));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            StatusText = "Pages cropped.";
            ToastService.Instance.Success("Crop applied to all pages.");
            await OpenFileAsync(_currentFilePath);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Crop failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task ExportTextAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export PDF Text",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) + "_text.txt",
        };
        if (dlg.ShowDialog() != true) return;

        string outPath = dlg.FileName;
        try
        {
            await Task.Run(() => _formService.ExportTextToFile(_currentFilePath, outPath));
            StatusText = $"Text exported to {System.IO.Path.GetFileName(outPath)}.";
            ToastService.Instance.Success("PDF text exported successfully.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Text export failed.", ex);
        }
    }

    private async Task DeletePageRangeAsync()
    {
        if (_currentFilePath == null || _document == null) return;

        int currentPage = _currentPageIndex + 1;
        var dlg = new Dialogs.PageRangeDialog(currentPage, _document.PageCount,
            "Delete", $"Delete pages from this PDF (total: {_document.PageCount} pages). This cannot be undone.");
        dlg.Owner = Application.Current.MainWindow;
        if (dlg.ShowDialog() != true) return;

        int totalAfter = _document.PageCount - (dlg.LastPage - dlg.FirstPage + 1);
        if (totalAfter < 1)
        {
            Dialogs.AppDialog.ShowInfo("Cannot delete all pages — at least one page must remain.", "Delete Pages");
            return;
        }
        if (!Dialogs.AppDialog.ShowConfirm($"Delete pages {dlg.FirstPage}–{dlg.LastPage}?\n\nThis operation cannot be undone.", "Delete Pages"))
            return;

        string tmp = _currentFilePath + ".tmp";
        try
        {
            int fp = dlg.FirstPage, lp = dlg.LastPage;
            await Task.Run(() => _formService.DeletePageRange(_currentFilePath, tmp, fp, lp));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            StatusText = $"Pages {fp}–{lp} deleted.";
            ToastService.Instance.Success($"Deleted pages {fp}–{lp}.");
            await OpenFileAsync(_currentFilePath);
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Delete page range failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task ExtractPageRangeAsync()
    {
        if (_currentFilePath == null || _document == null) return;

        int currentPage = _currentPageIndex + 1;
        var dlg = new Dialogs.PageRangeDialog(currentPage, _document.PageCount,
            "Extract", $"Extract a range of pages to a new PDF (total: {_document.PageCount} pages).");
        dlg.Owner = Application.Current.MainWindow;
        if (dlg.ShowDialog() != true) return;

        var saveDlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Extracted Pages",
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = $"{System.IO.Path.GetFileNameWithoutExtension(_currentFilePath)}_p{dlg.FirstPage}-{dlg.LastPage}.pdf",
        };
        if (saveDlg.ShowDialog() != true) return;

        string outPath = saveDlg.FileName;
        try
        {
            int fp = dlg.FirstPage, lp = dlg.LastPage;
            await Task.Run(() => _formService.ExtractPageRange(_currentFilePath, outPath, fp, lp));
            StatusText = $"Pages {fp}–{lp} extracted.";
            ToastService.Instance.Success($"Pages {fp}–{lp} extracted to {System.IO.Path.GetFileName(outPath)}.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Extract page range failed.", ex);
        }
    }

    private async Task ComparePdfsAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Compare with…",
            Filter = "PDF files (*.pdf)|*.pdf",
        };
        if (dlg.ShowDialog() != true) return;

        string fileB = dlg.FileName;
        StatusText = "Comparing PDFs…";
        try
        {
            var diffs = await Task.Run(() => _formService.ComparePdfs(_currentFilePath, fileB));
            var resultDlg = new Dialogs.ComparePdfsDialog(_currentFilePath, fileB, diffs)
            {
                Owner = Application.Current.MainWindow
            };
            resultDlg.ShowDialog();
            StatusText = $"Comparison complete — {diffs.Count(d => d.HasDifferences)} page(s) differ.";
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("PDF comparison failed.", ex);
            StatusText = "Comparison failed.";
        }
    }

    private void ValidateRequiredFields()
    {
        var empty = AllFields
            .Where(f => f.IsRequired &&
                        (!FieldValues.TryGetValue(f.Name, out var v) || string.IsNullOrWhiteSpace(v)))
            .Select(f => f.Name)
            .ToList();

        if (empty.Count == 0)
        {
            ToastService.Instance.Success("All required fields are filled.");
        }
        else
        {
            string list = string.Join("\n• ", empty.Take(15));
            Dialogs.AppDialog.ShowInfo(
                $"The following {empty.Count} required field(s) are empty:\n\n• {list}" +
                (empty.Count > 15 ? $"\n…and {empty.Count - 15} more." : ""),
                "Required Fields");
            // Navigate to the page containing the first empty required field
            var first = AllFields.FirstOrDefault(f => f.Name == empty[0]);
            if (first != null && first.PageNumber > 0)
                CurrentPageIndex = first.PageNumber - 1;
        }
    }

    private async Task SplitPdfAsync()
    {
        if (_currentFilePath == null || _document == null) return;

        string folder = System.IO.Path.GetDirectoryName(_currentFilePath)!;
        string baseName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);
        string splitFolder = System.IO.Path.Combine(folder, baseName + "_split");

        bool confirmed = Dialogs.AppDialog.ShowConfirm(
            $"Split \"{baseName}.pdf\" ({_document.PageCount} pages) into separate files?\n\nFiles will be saved to:\n{splitFolder}",
            title: "Split PDF",
            confirmText: "Split",
            cancelText: "Cancel");
        if (!confirmed) return;

        try
        {
            int count = await Task.Run(() => _formService.SplitPdf(_currentFilePath, splitFolder));
            ToastService.Instance.Success($"Split into {count} files.");
            Dialogs.AppDialog.ShowInfo(
                $"Split complete. {count} files saved to:\n{splitFolder}",
                "Split PDF");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("PDF split failed.", ex);
        }
    }

    private async Task MovePageUpAsync()
    {
        if (_currentFilePath == null || _document == null || _currentPageIndex <= 0) return;
        var tmp = _currentFilePath + ".ptmp";
        int fromIdx = _currentPageIndex;
        int toIdx = _currentPageIndex - 1;
        try
        {
            var newOrder = Enumerable.Range(0, _document.PageCount).ToList();
            newOrder.RemoveAt(fromIdx);
            newOrder.Insert(toIdx, fromIdx);
            await Task.Run(() => _formService.ReorderPages(_currentFilePath, tmp, newOrder));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            int savedPage = toIdx;
            await LoadDocumentAsync(_currentFilePath);
            CurrentPageIndex = savedPage;
            ToastService.Instance.Success("Page moved up.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Move page failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task MovePageDownAsync()
    {
        if (_currentFilePath == null || _document == null ||
            _currentPageIndex >= _document.PageCount - 1) return;
        var tmp = _currentFilePath + ".ptmp";
        int fromIdx = _currentPageIndex;
        int toIdx = _currentPageIndex + 1;
        try
        {
            var newOrder = Enumerable.Range(0, _document.PageCount).ToList();
            newOrder.RemoveAt(fromIdx);
            newOrder.Insert(toIdx, fromIdx);
            await Task.Run(() => _formService.ReorderPages(_currentFilePath, tmp, newOrder));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            int savedPage = toIdx;
            await LoadDocumentAsync(_currentFilePath);
            CurrentPageIndex = savedPage;
            ToastService.Instance.Success("Page moved down.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Move page failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task DuplicateCurrentPageAsync()
    {
        if (_currentFilePath == null) return;
        var tmp = _currentFilePath + ".ptmp";
        try
        {
            int savedPage = _currentPageIndex;
            await Task.Run(() => _formService.DuplicatePage(_currentFilePath, tmp, savedPage));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            await LoadDocumentAsync(_currentFilePath);
            CurrentPageIndex = savedPage + 1; // navigate to the new duplicate
            ToastService.Instance.Success($"Page {savedPage + 1} duplicated.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Duplicate page failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task InsertPageBeforeAsync()
    {
        if (_currentFilePath == null) return;
        var tmp = _currentFilePath + ".ptmp";
        try
        {
            int insertAt = _currentPageIndex;
            await Task.Run(() => _formService.InsertPageBefore(_currentFilePath, tmp, insertAt));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            int savedPage = insertAt; // navigate to the newly inserted blank page
            await LoadDocumentAsync(_currentFilePath);
            CurrentPageIndex = savedPage;
            ToastService.Instance.Success("Blank page inserted before current page.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Insert page failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private void OpenSettings()
    {
        var dlg = new Dialogs.SettingsWindow
        {
            Owner = Application.Current.MainWindow
        };
        if (dlg.ShowDialog() == true)
        {
            UiScale = AppSettings.Current.UiScale;
            CurrentFontFamily = AppSettings.Current.DefaultFontFamily;
            CurrentFontSize = AppSettings.Current.DefaultFontSize;
            CurrentFontColor = AppSettings.Current.DefaultFontColor;
            ForceUpperCase = AppSettings.Current.ForceUpperCaseDefault;
            RefreshAiModels();   // local AI server / model may have changed
            SyncStamps();        // custom / default stamps may have been restored or reset
            OnPropertyChanged(nameof(IsAiConfigured));
            ToastService.Instance.Success("Settings saved.");
        }
    }

    private void OpenManageProfiles()
    {
        var dlg = new Dialogs.ManageProfilesDialog
        {
            Owner = Application.Current.MainWindow
        };
        dlg.ShowDialog();
        OnPropertyChanged(nameof(Profiles));
        // Keep selection valid
        if (_selectedProfile != null &&
            !Services.PersonalProfileStore.All.Contains(_selectedProfile))
        {
            SelectedProfile = Services.PersonalProfileStore.All.Count > 0
                ? Services.PersonalProfileStore.All[0] : null;
        }
    }

    private void QuickFillWithProfile()
    {
        if (_selectedProfile == null || !HasDocument) return;
        var matches = Services.PersonalProfileStore.MatchFields(
            AllFields.Select(f => f.Name), _selectedProfile);

        int count = 0;
        foreach (var (k, v) in matches)
        {
            UpdateFieldValue(k, v);
            count++;
        }
        PageChanged?.Invoke();
        if (count > 0)
            ToastService.Instance.Success($"Quick Fill: {count} field(s) filled from profile.");
        else
            ToastService.Instance.Warning("No matching fields found for this profile.");
    }

    private async Task RunAiFillAsync()
    {
        var key = CurrentAiKey;
        if (!IsAiConfigured)
        {
            ToastService.Instance.Warning(_aiProvider == Services.AiProviderService.LocalProvider
                ? "Local AI has no server address — set it in Settings → AI Helper → Local AI."
                : $"No {_aiProvider} API key — set it in Settings → AI.");
            return;
        }
        if (string.IsNullOrWhiteSpace(AiPrompt))
        {
            ToastService.Instance.Warning("Enter a prompt describing how to fill the form.");
            return;
        }

        IsAiRunning = true;
        AiResponse = "Running AI…";
        try
        {
            var fieldNames = AllFields.Select(f => f.Name).ToList();
            var sysPrompt = _selectedProfile != null
                ? Services.PersonalProfileStore.BuildSystemPrompt(_selectedProfile) : null;
            var result = await Services.AiProviderService.FillFormFieldsAsync(
                AiPrompt, fieldNames, _aiProvider, _aiModel, key,
                systemPrompt: sysPrompt, documentText: _documentText);
            int count = 0;
            foreach (var (k, v) in result)
            {
                if (FieldValues.ContainsKey(k))
                {
                    UpdateFieldValue(k, v);
                    count++;
                }
            }
            PageChanged?.Invoke();
            AiResponse = $"Filled {count} of {result.Count} suggested fields.";
            ToastService.Instance.Success($"AI filled {count} fields.");
        }
        catch (Exception ex)
        {
            AiResponse = $"Error: {ex.Message}";
            ToastService.Instance.Error("AI fill failed.");
        }
        finally
        {
            IsAiRunning = false;
        }
    }

    private async Task SendAiChatAsync()
    {
        var input = AiChatInput.Trim();
        if (string.IsNullOrEmpty(input)) return;
        await SendChatMessageAsync(input, null, clearInput: true);
    }

    /// <summary>Sends a message (optionally with a picture) to the assistant and streams the reply.</summary>
    public async Task SendChatMessageAsync(string input, byte[]? imagePng, bool clearInput = false)
    {
        if (_isAiRunning) return;
        var key = CurrentAiKey;
        if (!IsAiConfigured)
        {
            ToastService.Instance.Warning(_aiProvider == Services.AiProviderService.LocalProvider
                ? "Local AI has no server address — set it in Settings → AI Helper → Local AI."
                : $"No {_aiProvider} API key — add it in Settings → AI.");
            return;
        }

        AiChatHistory.Add(new AiChatMessage { Role = "user", Content = input, ImagePng = imagePng });
        if (clearInput) AiChatInput = string.Empty;
        ShowAiPanel = true;

        var reply = new AiChatMessage { Role = "assistant", Content = "" };
        AiChatHistory.Add(reply);

        IsAiRunning = true;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();

        try
        {
            var history = AiChatHistory.Take(AiChatHistory.Count - 1).ToList();

            // Build system prompt: how to answer, profile context, document context
            var sysParts = new System.Text.StringBuilder();
            sysParts.Append(AiAssistantInstructions()).Append("\n\n");
            if (_selectedProfile != null)
                sysParts.Append(Services.PersonalProfileStore.BuildSystemPrompt(_selectedProfile)).Append("\n\n");
            if (!string.IsNullOrEmpty(_documentText))
                sysParts.Append("The user has the following PDF document open:\n\n").Append(_documentText);
            var sysPrompt = sysParts.ToString();

            reply.IsStreaming = true;
            await Services.AiProviderService.SendStreamingAsync(
                history, _aiProvider, _aiModel, key,
                chunk => Application.Current.Dispatcher.Invoke(() => AppendReplyChunk(reply, chunk)),
                _aiCts.Token,
                systemPrompt: sysPrompt);

            FinishReply(reply);
            if (string.IsNullOrEmpty(reply.Content) && reply.Actions.Count == 0)
                reply.Content = "(No response — check your API key and model selection.)";
        }
        catch (OperationCanceledException)
        {
            FinishReply(reply);
            reply.Content = reply.Content.Length > 0 ? reply.Content + "\n\n*(Stopped)*" : "(Cancelled)";
        }
        catch (Exception ex)
        {
            reply.IsStreaming = false;
            reply.Content = $"Error: {ex.Message}";
            ToastService.Instance.Error("AI error — check your API key.");
        }
        finally
        {
            reply.IsStreaming = false;
            IsAiRunning = false;
        }
    }

    // Called by AiChatPanel preset chips and by the new AI commands
    public async Task RunAnalysisPresetAsync(string analysisType, string? overridePrompt = null)
    {
        var key = CurrentAiKey;
        if (!IsAiConfigured)
        {
            ToastService.Instance.Warning(_aiProvider == Services.AiProviderService.LocalProvider
                ? "Local AI has no server address — set it in Settings → AI Helper → Local AI."
                : $"No {_aiProvider} API key — add it via the ⚙ icon in the AI panel.");
            return;
        }

        if (analysisType is "summarize" or "extract" or "contract" or "pii" or "translate" or "smartfill")
        {
            if (string.IsNullOrEmpty(_documentText))
            {
                ToastService.Instance.Warning("Waiting for document text to load — try again in a moment.");
                return;
            }
        }

        // For smart fill, use the special JSON-extract path
        if (analysisType == "smartfill")
        {
            await SmartFillFromDocAsync(key);
            return;
        }

        // For all other analysis types: stream into chat
        var labelMap = new Dictionary<string, string>
        {
            ["summarize"] = "📋 Summarize this document",
            ["extract"]   = "🔍 Extract key data from this document",
            ["contract"]  = "🔎 Analyze this as a contract",
            ["pii"]       = "🔒 Find PII that should be redacted",
            ["translate"] = "🌐 Translate this document",
            ["qa"]        = overridePrompt ?? "💬 What is this document about?",
        };

        var userMsg = overridePrompt ?? (labelMap.TryGetValue(analysisType, out var lbl) ? lbl : analysisType);
        AiChatHistory.Add(new AiChatMessage { Role = "user", Content = userMsg });

        var reply = new AiChatMessage { Role = "assistant", Content = "" };
        AiChatHistory.Add(reply);

        ShowAiPanel = true;
        IsAiRunning = true;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();

        try
        {
            var fieldNames = AllFields.Select(f => f.Name);
            reply.IsStreaming = true;
            await Services.AiProviderService.AnalyzeDocumentAsync(
                _documentText, analysisType, _aiProvider, _aiModel, key,
                chunk => Application.Current.Dispatcher.Invoke(() => AppendReplyChunk(reply, chunk)),
                _aiCts.Token,
                fieldNames: fieldNames,
                extraInstructions: AiAssistantInstructions());

            FinishReply(reply);
            if (string.IsNullOrEmpty(reply.Content))
                reply.Content = "(No response — check your API key and model selection.)";
        }
        catch (OperationCanceledException)
        {
            FinishReply(reply);
            reply.Content = reply.Content.Length > 0 ? reply.Content + "\n\n*(Stopped)*" : "(Cancelled)";
        }
        catch (Exception ex)
        {
            reply.Content = $"Error: {ex.Message}";
            ToastService.Instance.Error("AI analysis failed.");
        }
        finally
        {
            reply.IsStreaming = false;
            IsAiRunning = false;
        }
    }

    private async Task SmartFillFromDocAsync(string apiKey)
    {
        if (string.IsNullOrEmpty(_documentText)) return;

        var userMsg = "📄 Smart Fill — fill all fields from document content";
        AiChatHistory.Add(new AiChatMessage { Role = "user", Content = userMsg });

        var reply = new AiChatMessage { Role = "assistant", Content = "Analysing document and filling fields…" };
        AiChatHistory.Add(reply);

        ShowAiPanel = true;
        IsAiRunning = true;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();

        try
        {
            var fieldNames = AllFields.Select(f => f.Name).ToList();
            var result = await Services.AiProviderService.FillFormFieldsAsync(
                "Fill these form fields using the information found in the document.",
                fieldNames, _aiProvider, _aiModel, apiKey,
                _aiCts.Token,
                documentText: _documentText);

            int filled = 0;
            var filledList = new System.Text.StringBuilder();
            foreach (var (k, v) in result)
            {
                if (FieldValues.ContainsKey(k) && !string.IsNullOrEmpty(v))
                {
                    UpdateFieldValue(k, v);
                    filledList.AppendLine($"• **{k}**: {v}");
                    filled++;
                }
            }
            PageChanged?.Invoke();

            reply.Content = filled > 0
                ? $"✓ Smart Fill complete — filled {filled} field(s) from document content:\n\n{filledList}"
                : "No fields could be confidently filled from this document's content. Try using a profile or the AI chat.";

            if (filled > 0)
                ToastService.Instance.Success($"Smart Fill: {filled} field(s) filled from document.");
            else
                ToastService.Instance.Info("Smart Fill: no matching fields found.");
        }
        catch (OperationCanceledException)
        {
            reply.Content = "(Cancelled)";
        }
        catch (Exception ex)
        {
            reply.Content = $"Error: {ex.Message}";
            ToastService.Instance.Error("Smart Fill failed.");
        }
        finally
        {
            IsAiRunning = false;
        }
    }

    private async Task DeleteCurrentPageAsync()
    {
        if (_currentFilePath == null || _document == null) return;
        var tmp = _currentFilePath + ".ptmp";
        try
        {
            _formService.DeletePages(_currentFilePath, tmp, new[] { _currentPageIndex });
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            await LoadDocumentAsync(_currentFilePath);
            ToastService.Instance.Success("Page deleted.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Delete page failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task InsertBlankPageAsync()
    {
        if (_currentFilePath == null) return;
        var tmp = _currentFilePath + ".ptmp";
        try
        {
            _formService.InsertBlankPage(_currentFilePath, tmp, _currentPageIndex);
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            await LoadDocumentAsync(_currentFilePath);
            ToastService.Instance.Success("Blank page inserted.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Insert page failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task MergePdfAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select PDFs to Merge",
            Filter = "PDF Files (*.pdf)|*.pdf",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0) return;

        var dlgSave = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Merged PDF",
            Filter = "PDF Files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = "merged.pdf"
        };
        if (dlgSave.ShowDialog() != true) return;

        try
        {
            var sources = new[] { _currentFilePath }.Concat(dlg.FileNames);
            _formService.MergePdfs(sources, dlgSave.FileName);
            await LoadDocumentAsync(dlgSave.FileName);
            ToastService.Instance.Success($"Merged {dlg.FileNames.Length + 1} PDFs.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("PDF merge failed.", ex);
        }
    }

    private async Task InsertPdfAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Dialogs.InsertPdfDialog(_currentPageIndex + 1, PageCount);
        dlg.Owner = System.Windows.Application.Current.MainWindow;
        if (dlg.ShowDialog() != true || dlg.SelectedFilePath == null) return;

        // Determine 0-based index after which pages are inserted (-1 = before page 0)
        int insertAfterIndex = dlg.InsertPosition switch
        {
            Dialogs.InsertPdfPosition.Beginning     => -1,
            Dialogs.InsertPdfPosition.BeforeCurrent => _currentPageIndex - 1,
            Dialogs.InsertPdfPosition.AfterCurrent  => _currentPageIndex,
            Dialogs.InsertPdfPosition.End           => PageCount - 1,
            _                                       => _currentPageIndex
        };

        var tmp = System.IO.Path.GetTempFileName() + ".pdf";
        try
        {
            await Task.Run(() => _formService.InsertPdfAt(_currentFilePath, dlg.SelectedFilePath, tmp, insertAfterIndex));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            var savedPage = _currentPageIndex;
            await LoadDocumentAsync(_currentFilePath);
            CurrentPageIndex = Math.Min(savedPage, PageCount - 1);
            ToastService.Instance.Success($"PDF inserted successfully.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Insert PDF failed.", ex);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task ExtractCurrentPageAsync()
    {
        if (_currentFilePath == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Extracted Page",
            Filter = "PDF Files (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"page_{_currentPageIndex + 1}.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _formService.ExtractPages(_currentFilePath, dlg.FileName, new[] { _currentPageIndex });
            ToastService.Instance.Success($"Page {_currentPageIndex + 1} extracted.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Page extraction failed.", ex);
        }
    }

    private void PerformSearch(string query)
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(query)) return;

        var q = query.ToLowerInvariant();

        // Search form field names and values
        foreach (var f in AllFields)
        {
            if (f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                f.Value.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                SearchResults.Add(new SearchResult
                {
                    Label = f.Name,
                    Detail = f.Value,
                    Kind = "Field",
                    PageNumber = f.PageNumber,
                    Field = f
                });
            }
        }

        // Search free-text annotation content
        foreach (var ann in FreeTextAnnotations)
        {
            if (ann.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                SearchResults.Add(new SearchResult
                {
                    Label = ann.Text.Length > 40 ? ann.Text[..40] + "…" : ann.Text,
                    Detail = $"Page {ann.PageNumber}",
                    Kind = "Annotation",
                    PageNumber = ann.PageNumber,
                    Annotation = ann
                });
            }
        }

        // Search the PDF text layer page-by-page (uses cached per-page text)
        if (_currentFilePath != null && _document != null)
        {
            var addedPages = new HashSet<int>();
            for (int pg = 1; pg <= _document.PageCount; pg++)
            {
                // Avoid searching too many pages to keep search responsive
                if (addedPages.Count >= 50) break;
                var pageText = Services.PdfTextExtractorService.GetPageText(_currentFilePath, pg);
                if (!string.IsNullOrEmpty(pageText)
                    && pageText.Contains(q, StringComparison.OrdinalIgnoreCase)
                    && addedPages.Add(pg))
                {
                    // Find context snippet around the first match
                    int matchIdx = pageText.IndexOf(q, StringComparison.OrdinalIgnoreCase);
                    int start = Math.Max(0, matchIdx - 20);
                    int len = Math.Min(60, pageText.Length - start);
                    string snippet = pageText.Substring(start, len).Replace('\n', ' ').Trim();
                    if (start > 0) snippet = "…" + snippet;
                    SearchResults.Add(new SearchResult
                    {
                        Label = $"Page {pg}",
                        Detail = snippet,
                        Kind = "Text",
                        PageNumber = pg,
                    });
                }
            }
        }
    }

    private async Task ExportXfdfAsync()
    {
        if (_currentFilePath == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Annotations as XFDF",
            Filter = "XFDF annotation files (*.xfdf)|*.xfdf",
            DefaultExt = ".xfdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) + ".xfdf",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            string path = dlg.FileName;
            await Task.Run(() => Services.XfdfService.Export(
                path, _currentFilePath,
                HighlightAnnotations, StickyNotes, FreeTextAnnotations, ShapeAnnotations));
            int total = HighlightAnnotations.Count + StickyNotes.Count
                      + FreeTextAnnotations.Count + ShapeAnnotations.Count;
            ToastService.Instance.Success($"Exported {total} annotation(s) to XFDF.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("XFDF export failed.", ex); }
    }

    private async Task ImportXfdfAsync()
    {
        if (_currentFilePath == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Annotations from XFDF",
            Filter = "XFDF annotation files (*.xfdf)|*.xfdf",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var result = await Task.Run(() => Services.XfdfService.Import(dlg.FileName));

            int added = 0;
            foreach (var hl in result.Highlights)   { HighlightAnnotations.Add(hl); added++; }
            foreach (var sn in result.StickyNotes)  { StickyNotes.Add(sn);          added++; }
            foreach (var ft in result.FreeTexts)    { FreeTextAnnotations.Add(ft);  added++; }
            foreach (var sh in result.Shapes)       { ShapeAnnotations.Add(sh);     added++; }

            PageChanged?.Invoke();
            ToastService.Instance.Success($"Imported {added} annotation(s) from XFDF.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("XFDF import failed.", ex); }
    }

    private async Task ImportFormDataFromJsonAsync()
    {
        if (_currentFilePath == null || _document == null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Form Data from JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            string json = await Task.Run(() => System.IO.File.ReadAllText(dlg.FileName));
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
            if (dict == null || dict.Count == 0)
            {
                ToastService.Instance.Info("No data found in the JSON file.");
                return;
            }

            int filled = 0;
            foreach (var kvp in dict)
            {
                var field = AllFields.FirstOrDefault(f =>
                    string.Equals(f.Name, kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.Name.Replace(" ", "_"), kvp.Key.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase));

                if (field == null) continue;

                string val = kvp.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String  => kvp.Value.GetString() ?? "",
                    System.Text.Json.JsonValueKind.Number  => kvp.Value.GetRawText(),
                    System.Text.Json.JsonValueKind.True    => "true",
                    System.Text.Json.JsonValueKind.False   => "false",
                    _                                      => kvp.Value.GetRawText(),
                };

                field.Value = val;
                filled++;
            }

            OnPropertyChanged(nameof(AllFields));
            PageChanged?.Invoke();
            ToastService.Instance.Success($"Filled {filled} field(s) from {System.IO.Path.GetFileName(dlg.FileName)}.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Import form data failed.", ex); }
    }

    private async Task ExportAnnotationSummaryAsync()
    {
        if (_currentFilePath == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Annotation Summary",
            Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt",
            DefaultExt = ".csv",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) + "_annotations.csv",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await Task.Run(() =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Type,Page,Left,Bottom,Width,Height,Color,Text/Value");

                foreach (var hl in HighlightAnnotations)
                    sb.AppendLine($"{hl.Kind},{ hl.PageNumber},{hl.Left:F1},{hl.Bottom:F1},{hl.Width:F1},{hl.Height:F1},{hl.Color},");

                foreach (var sn in StickyNotes)
                    sb.AppendLine($"StickyNote,{sn.PageNumber},{sn.Left:F1},{sn.Bottom:F1},,,{sn.Color},{CsvEscape(sn.Text)}");

                foreach (var ft in FreeTextAnnotations)
                    sb.AppendLine($"FreeText,{ft.PageNumber},{ft.Left:F1},{ft.Bottom:F1},{ft.Width:F1},{ft.Height:F1},,{CsvEscape(ft.Text)}");

                foreach (var sh in ShapeAnnotations)
                {
                    double w = Math.Abs(sh.X2 - sh.X1);
                    double h = Math.Abs(sh.Y2 - sh.Y1);
                    sb.AppendLine($"{sh.Kind},{sh.PageNumber},{Math.Min(sh.X1, sh.X2):F1},{Math.Min(sh.Y1, sh.Y2):F1},{w:F1},{h:F1},{sh.StrokeColor},");
                }

                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            });

            int total = HighlightAnnotations.Count + StickyNotes.Count
                      + FreeTextAnnotations.Count + ShapeAnnotations.Count;
            ToastService.Instance.Success($"Annotation summary: {total} item(s) exported.");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Annotation summary export failed.", ex); }
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private async Task FindAndHighlightAsync()
    {
        if (_currentFilePath == null || _document == null) return;

        var dlg = new Dialogs.InputDialog(
            "Find and Highlight",
            "Enter text to search and create highlight annotations on all matches:");
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;

        string query = dlg.InputText.Trim();
        StatusText = $"Searching for \"{query}\"…";

        try
        {
            var matches = await Task.Run(() =>
                Services.PdfTextExtractorService.FindTextPositions(_currentFilePath, query));

            if (matches.Count == 0)
            {
                ToastService.Instance.Info($"No matches found for \"{query}\".");
                StatusText = "Ready";
                return;
            }

            foreach (var m in matches)
            {
                var hl = new Models.HighlightAnnotation
                {
                    PageNumber = m.PageNumber,
                    Left   = m.Left,
                    Bottom = m.Bottom,
                    Width  = m.Width,
                    Height = Math.Max(m.Height, 6),
                    Color  = CurrentHighlightColor,
                    Opacity = CurrentHighlightOpacity,
                    Kind   = Models.HighlightKind.Highlight,
                };
                // Add directly — AddHighlightAnnotation would overwrite PageNumber
                HighlightAnnotations.Add(hl);
            }
            float capturedOpacity = CurrentHighlightOpacity;
            _undoService.Push(new Services.AnnotationAction
            {
                Description = $"Find & highlight \"{query}\" ({matches.Count})",
                Execute     = () => { foreach (var m in matches) HighlightAnnotations.Add(new Models.HighlightAnnotation { PageNumber = m.PageNumber, Left = m.Left, Bottom = m.Bottom, Width = m.Width, Height = Math.Max(m.Height, 6), Color = CurrentHighlightColor, Opacity = capturedOpacity }); },
                Undo        = () => { for (int i = 0; i < matches.Count; i++) { if (HighlightAnnotations.Count > 0) HighlightAnnotations.RemoveAt(HighlightAnnotations.Count - 1); } },
            });
            RefreshUndoCanExecute();

            PageChanged?.Invoke();
            ToastService.Instance.Success($"Highlighted {matches.Count} occurrence(s) of \"{query}\".");
            StatusText = $"Found and highlighted {matches.Count} matches.";
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Find and highlight failed.", ex);
            StatusText = "Ready";
        }
    }

    /// <summary>
    /// Replaces the Design canvas with a best-effort editable reconstruction of the current
    /// PDF page: text lines, images, and existing form fields as movable elements, rather than
    /// a flat background image. Content-stream reconstruction is inherently approximate — see
    /// PdfToDesignImportService for the specific tradeoffs (line grouping, font substitution).
    /// </summary>
    // The PDF page the Design canvas was imported from (null = a design made from scratch / template).
    private (string Path, int Page)? _designSourceKey;

    /// <summary>On entering Design: import the current page once, otherwise refresh field positions from Live.</summary>
    private void EnsureDesignImported()
    {
        if (!HasDocument || _currentFilePath == null) return;
        var key = (_currentFilePath, _currentPageIndex);
        bool isScratchDesign = _designSourceKey == null && DesignCanvas.Elements.Count > 0;
        if (isScratchDesign) return; // never overwrite a design the user started from scratch

        if (DesignCanvas.Elements.Count == 0 || _designSourceKey != key)
            ImportCurrentPdfPageIntoDesign();
        else
            SyncLiveFieldsToDesign();
    }

    private FormFieldInfo? FindSourceField(FormFieldDesignElement el) =>
        el.SourceFieldName == null ? null
        : AllFields.FirstOrDefault(f => f.Name == el.SourceFieldName && f.WidgetIndex == el.SourceWidgetIndex && f.PageNumber == el.SourcePageNumber);

    private string LiveFieldValue(FormFieldInfo f) =>
        FieldValues.TryGetValue(f.Name, out var v) ? v : f.Value;

    // Signatures the Design canvas shows for its source page, so ones deleted in Design are
    // removed from Live View too (and ones placed in Live View appear in Design).
    private readonly HashSet<PlacedSignature> _designSignatures = new();

    /// <summary>Live → Design: signatures placed, moved or removed in Live View since the last sync.</summary>
    private void SyncLiveSignaturesToDesign(int pageIndex, double pageH)
    {
        var onPage = PlacedSignatures.Where(s => s.PageNumber == pageIndex + 1).ToList();
        var elements = DesignCanvas.Elements.OfType<ImageDesignElement>().Where(e => e.SignatureBytes != null).ToList();

        foreach (var el in elements)
        {
            if (el.LinkedSignature is not { } sig) continue;
            if (!onPage.Contains(sig) && _designSignatures.Contains(sig))
            {
                DesignCanvas.Elements.Remove(el);   // removed in Live View
                continue;
            }
            el.X = sig.Left; el.Y = pageH - sig.Bottom - sig.Height;
            el.Width = sig.Width; el.Height = sig.Height;
        }
        foreach (var sig in onPage.Where(s => elements.All(e => e.LinkedSignature != s)))
        {
            var el = DesignCanvas.CreateSignatureElement(sig.ImageBytes,
                new Rect(sig.Left, pageH - sig.Bottom - sig.Height, sig.Width, sig.Height));
            if (el == null) continue;
            el.X = sig.Left; el.Y = pageH - sig.Bottom - sig.Height;
            el.Width = sig.Width; el.Height = sig.Height;
            el.LinkedSignature = sig;
            el.ZOrder = DesignCanvas.Elements.Count;
            DesignCanvas.Elements.Add(el);
        }
        _designSignatures.Clear();
        _designSignatures.UnionWith(onPage);
    }

    /// <summary>Design → Live: signatures placed, moved or deleted on the Design canvas.</summary>
    private void SyncDesignSignaturesToLive(int pageIndex, double pageH)
    {
        var current = new HashSet<PlacedSignature>();
        foreach (var el in DesignCanvas.Elements.OfType<ImageDesignElement>().Where(e => e.SignatureBytes != null))
        {
            // A copy / paste of a signature shares its link: give the copy its own Live signature.
            if (el.LinkedSignature is not { } sig || current.Contains(sig) || !PlacedSignatures.Contains(sig)
                && !_designSignatures.Contains(sig))
            {
                sig = new PlacedSignature { PageNumber = pageIndex + 1, ImageBytes = el.SignatureBytes! };
                el.LinkedSignature = sig;
            }
            sig.Left = Math.Round(el.X, 2);
            sig.Bottom = Math.Round(pageH - el.Y - el.Height, 2);
            sig.Width = Math.Round(el.Width, 2);
            sig.Height = Math.Round(el.Height, 2);
            if (!PlacedSignatures.Contains(sig)) PlacedSignatures.Add(sig);
            current.Add(sig);
        }
        foreach (var gone in _designSignatures.Where(s => !current.Contains(s)).ToList())
            PlacedSignatures.Remove(gone);   // deleted in Design
        _designSignatures.Clear();
        _designSignatures.UnionWith(current);
    }

    /// <summary>
    /// Picks the Design-canvas equivalent of a Live View Complete &amp; Sign tool. Returns false when
    /// the tool has none (the caller then switches to Live View).
    /// </summary>
    public bool SelectFillSignToolInDesign(ActiveTool tool)
    {
        DesignTool? designTool = tool switch
        {
            ActiveTool.Select or ActiveTool.TextFill or ActiveTool.CheckboxToggle => DesignTool.Fill,
            ActiveTool.AddText   => DesignTool.Text,
            ActiveTool.Checkmark => DesignTool.Checkmark,
            ActiveTool.XMark     => DesignTool.XMark,
            ActiveTool.Signature => DesignTool.Sign,
            ActiveTool.DrawFreehand => DesignTool.Pen,
            _ => null,
        };
        if (designTool is not { } dt) return false;
        if (dt == DesignTool.Sign && _pendingLibrarySignature != null)
            DesignCanvas.PendingSignature = _pendingLibrarySignature;
        DesignCanvas.ActiveTool = dt;
        return true;
    }

    /// <summary>Live → Design: fields moved or edited in Live View since the import.</summary>
    private void SyncLiveFieldsToDesign()
    {
        if (_document == null || _designSourceKey is not { } key) return;
        double pageH = _document.PageSizes[key.Page].Height;
        SyncLiveSignaturesToDesign(key.Page, pageH);
        SyncLiveAnnotationsToDesign(key.Page, pageH);
        foreach (var el in DesignCanvas.Elements.OfType<FormFieldDesignElement>())
        {
            if (FindSourceField(el) is not { } f) continue;
            string value = LiveFieldValue(f);
            if (el.ExportValue != f.ExportValue) el.ExportValue = f.ExportValue;
            if (el.Value != value) el.Value = value;
            double y = pageH - f.Bottom - f.Height;
            if (Math.Abs(el.X - f.Left) > 0.01) el.X = f.Left;
            if (Math.Abs(el.Y - y) > 0.01) el.Y = y;
            if (Math.Abs(el.Width - f.Width) > 0.01) el.Width = f.Width;
            if (Math.Abs(el.Height - f.Height) > 0.01) el.Height = f.Height;
            if (el.FieldName != f.DisplayName) el.FieldName = f.DisplayName;
            if (el.Required != f.IsRequired) el.Required = f.IsRequired;
        }
    }

    /// <summary>Design → Live: fields moved / resized / renamed on the Design canvas (one undo step).</summary>
    private void SyncDesignFieldsToLive()
    {
        if (_document == null || _currentFilePath == null || _designSourceKey is not { } key || key.Path != _currentFilePath) return;
        if (key.Page < 0 || key.Page >= _document.PageSizes.Count) return;
        double pageH = _document.PageSizes[key.Page].Height;

        // Fields drawn in Design become real form fields (the document reloads afterwards).
        CreateDesignFieldsInPdf(key.Page, pageH);

        var moves = new List<(FormFieldInfo, FieldBounds)>();
        foreach (var el in DesignCanvas.Elements.OfType<FormFieldDesignElement>())
        {
            if (FindSourceField(el) is not { } f) continue;
            var b = new FieldBounds(Math.Round(el.X, 2), Math.Round(pageH - el.Y - el.Height, 2),
                                    Math.Round(el.Width, 2), Math.Round(el.Height, 2));
            if (Math.Abs(b.Left - f.Left) > 0.01 || Math.Abs(b.Bottom - f.Bottom) > 0.01 ||
                Math.Abs(b.Width - f.Width) > 0.01 || Math.Abs(b.Height - f.Height) > 0.01)
                moves.Add((f, b));

            string newName = (el.FieldName ?? string.Empty).Trim();
            if (newName.Length > 0 && newName != f.DisplayName && ValidateFieldName(f, newName) == null)
            {
                foreach (var w in AllFields.Where(x => x.Name == f.Name))
                    w.PendingName = newName == f.Name ? null : newName;
                ModifiedFieldNames.Add(f.Name);
            }
            if (el.Required != f.IsRequired)
            {
                foreach (var w in AllFields.Where(x => x.Name == f.Name)) w.IsRequired = el.Required;
                ModifiedFieldNames.Add(f.Name);
            }
            // Filled in on the Design canvas (Complete & Sign).
            if (el.Value != LiveFieldValue(f)
                && !(f.FieldType is FieldType.Checkbox or FieldType.RadioButton && !el.HasValue && LiveFieldValue(f) is "" or "Off"))
                UpdateFieldValue(f.Name, el.Value);
        }
        // Shapes, text, marks, drawings and pictures added in Design (before signatures: pictures
        // are placed like signature images).
        SyncDesignAnnotationsToLive(key.Page, pageH);
        SyncDesignSignaturesToLive(key.Page, pageH);
        if (moves.Count > 0) SetFieldBoundsBatch(moves, "Updated from Design");
        else PageChanged?.Invoke();
    }

    private void ImportCurrentPdfPageIntoDesign()
    {
        if (_currentFilePath == null || _document == null) return;
        try
        {
            RefreshCurrentPageFields();
            double pageWidth = _document.PageSizes[_currentPageIndex].Width;
            double pageHeight = _document.PageSizes[_currentPageIndex].Height;

            var elements = Services.PdfToDesignImportService.ExtractPageAsElements(
                _currentFilePath, _currentPageIndex, pageHeight, CurrentPageFields);

            DesignCanvas.Elements.Clear();

            // Vector artwork (fills, rules, boxes, drawings) has no editable equivalent, so
            // it comes in as a locked full-page backdrop beneath the editable elements.
            try
            {
                var artwork = PdfEdit.Render.Engine.CustomPdfEngine.RenderPageArtwork(PdfEdit.Drawing.Wpf.WpfDrawingBackend.Instance, _currentFilePath, _currentPageIndex)?.ToBitmapSource();
                if (artwork != null)
                {
                    DesignCanvas.Elements.Add(new Models.ImageDesignElement
                    {
                        X = 0, Y = 0, Width = pageWidth, Height = pageHeight,
                        Bitmap = artwork, IsLocked = true, IsFromPage = true, ZOrder = -1
                    });
                }
            }
            catch { /* backdrop is best-effort; the editable elements still import */ }

            foreach (var e in elements) DesignCanvas.Elements.Add(e);

            // Carry the filled-in values and placed signatures over, so the page can be
            // filled and signed in Design as well as in Live View.
            foreach (var el in DesignCanvas.Elements.OfType<FormFieldDesignElement>())
            {
                if (FindSourceField(el) is not { } f) continue;
                el.ExportValue = f.ExportValue;
                el.Value = LiveFieldValue(f);
            }
            _designSignatures.Clear();
            SyncLiveSignaturesToDesign(_currentPageIndex, pageHeight);
            _designLiveIds.Clear();
            SyncLiveAnnotationsToDesign(_currentPageIndex, pageHeight);

            _designSourceKey = (_currentFilePath, _currentPageIndex);
            DesignCanvas.SelectedElement = null;
            DesignCanvas.PageSize = Models.DesignPageSize.Custom;
            DesignCanvas.CustomPageWidth = pageWidth;
            DesignCanvas.CustomPageHeight = pageHeight;

            StatusText = elements.Count > 0
                ? $"Imported page {_currentPageIndex + 1} into Design ({elements.Count} elements) — positions and fonts are approximate."
                : $"Page {_currentPageIndex + 1} had no extractable text, images, or fields — Design canvas is blank.";
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not import the PDF page into Design.", ex);
        }
    }

    private async Task ExportDesignAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Design as PDF",
            Filter = "PDF files|*.pdf",
            DefaultExt = ".pdf",
            FileName = "design.pdf"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            await Task.Run(() => Services.DesignExportService.ExportToPdf(
                DesignCanvas.Elements, DesignCanvas.PageWidth, DesignCanvas.PageHeight, dlg.FileName, DesignCanvas.PageBackground));
            ToastService.Instance.Success($"Design exported to {System.IO.Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Export failed.", ex); }
    }

    private async Task ExportPageAsImageAsync()
    {
        if (_document == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Page as Image",
            Filter = "PNG image|*.png|JPEG image|*.jpg",
            DefaultExt = ".png",
            FileName = $"page{_currentPageIndex + 1}.png"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            StatusText = "Rendering page for export…";
            // High-res export at 2× zoom (192 DPI equivalent)
            var bmp = await _renderService.RenderPageAsync(_currentPageIndex, 2.0);

            bool isPng = dlg.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            await Task.Run(() =>
            {
                System.Windows.Media.Imaging.BitmapEncoder enc = isPng
                    ? new System.Windows.Media.Imaging.PngBitmapEncoder()
                    : new System.Windows.Media.Imaging.JpegBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var fs = System.IO.File.OpenWrite(dlg.FileName);
                enc.Save(fs);
            });

            StatusText = $"Page exported to {System.IO.Path.GetFileName(dlg.FileName)}";
            ToastService.Instance.Success($"Page {_currentPageIndex + 1} exported as image.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Export failed.", ex);
        }
    }

    private async Task PrintAsync()
    {
        if (_document == null || _currentFilePath == null) return;

        var dlg = new System.Windows.Controls.PrintDialog();
        if (dlg.ShowDialog() != true) return;

        StatusText = "Preparing print…";
        IsLoading = true;
        try
        {
            int pageCount = _document.PageCount;

            // Render all pages at 150 DPI (zoom = 150/96)
            const double printZoom = 150.0 / 96.0;
            var frames = new System.Windows.Media.Imaging.BitmapSource[pageCount];
            for (int i = 0; i < pageCount; i++)
            {
                frames[i] = await _renderService.RenderPageAsync(i, printZoom);
                StatusText = $"Rendering page {i + 1} of {pageCount}…";
            }

            // Build a FixedDocument with one FixedPage per rendered frame
            var fixedDoc = new System.Windows.Documents.FixedDocument();
            fixedDoc.DocumentPaginator.PageSize = new Size(
                dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);

            for (int i = 0; i < pageCount; i++)
            {
                var bmp = frames[i];
                double pageW = bmp.PixelWidth;
                double pageH = bmp.PixelHeight;

                // Scale to fit printable area while preserving aspect ratio
                double scaleX = dlg.PrintableAreaWidth / pageW;
                double scaleY = dlg.PrintableAreaHeight / pageH;
                double scale = Math.Min(scaleX, scaleY);

                var img = new System.Windows.Controls.Image
                {
                    Source = bmp,
                    Width = pageW * scale,
                    Height = pageH * scale,
                };
                System.Windows.Controls.Canvas.SetLeft(img, 0);
                System.Windows.Controls.Canvas.SetTop(img, 0);

                var canvas = new System.Windows.Controls.Canvas
                {
                    Width = dlg.PrintableAreaWidth,
                    Height = dlg.PrintableAreaHeight,
                };
                canvas.Children.Add(img);
                canvas.Measure(new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight));
                canvas.Arrange(new Rect(new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight)));

                var fixedPage = new System.Windows.Documents.FixedPage
                {
                    Width = dlg.PrintableAreaWidth,
                    Height = dlg.PrintableAreaHeight,
                };
                fixedPage.Children.Add(canvas);

                var pageContent = new System.Windows.Documents.PageContent();
                ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
                fixedDoc.Pages.Add(pageContent);
            }

            dlg.PrintDocument(fixedDoc.DocumentPaginator,
                System.IO.Path.GetFileNameWithoutExtension(_currentFilePath));

            StatusText = $"Printed {pageCount} page(s).";
            ToastService.Instance.Success($"Sent {pageCount} page(s) to printer.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Print failed.", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void NavigateToSearchResult(SearchResult result)
    {
        if (result.PageNumber > 0)
            CurrentPageIndex = result.PageNumber - 1;
        if (result.Field != null)
            SelectedField = result.Field;
        if (result.Annotation != null)
            SelectedAnnotation = result.Annotation;
        ShowSearchOverlay = false;
    }

    public void RefreshCurrentPageFields()
    {
        CurrentPageFields.Clear();
        if (_document == null) return;
        int page = _currentPageIndex + 1;
        foreach (var f in AllFields.Where(f => f.PageNumber == page))
            CurrentPageFields.Add(f);
    }

    public bool HasNoRecentFiles => RecentFileEntries.Count == 0;

    public void SaveDocumentState()
    {
        if (_currentFilePath == null) return;
        DocumentStateStore.Set(_currentFilePath, new DocumentState
        {
            LastPageIndex = _currentPageIndex,
            LastZoom = _zoom,
            Annotations = FreeTextAnnotations.ToList(),
            Signatures = PlacedSignatures.ToList(),
            Highlights = HighlightAnnotations.ToList(),
            StickyNotes = StickyNotes.ToList(),
            Shapes = ShapeAnnotations.ToList(),
            TextEdits = TextEditMarks.ToList(),
            FieldValues = FieldValues.ToDictionary(kv => kv.Key, kv => kv.Value),
            FieldLayouts = ModifiedFieldBounds.Select(kv => new FieldLayoutState
            {
                Name = kv.Key.Name, WidgetIndex = kv.Key.WidgetIndex,
                Left = kv.Value.Left, Bottom = kv.Value.Bottom, Width = kv.Value.Width, Height = kv.Value.Height,
            }).ToList(),
            FieldEdits = GetFieldEditsForSave().Select(f => new FieldEditState
            {
                Name = f.Name, PendingName = f.PendingName, Tooltip = f.Tooltip,
                IsRequired = f.IsRequired, IsReadOnly = f.IsReadOnly, IsMultiline = f.IsMultiline,
                Alignment = f.Alignment, FontSize = f.FontSize,
                BorderColor = f.BorderColor, FillColor = f.FillColor, TextColor = f.TextColor,
                MaxLength = f.MaxLength, IsComb = f.IsComb, IsEditable = f.IsEditable,
                DateFormat = f.DateFormat, DefaultValue = f.DefaultValue, Options = f.Options.ToList(),
            }).ToList(),
            DeletedFields = DeletedFieldNames.ToList(),
        });
    }

    /// <summary>Re-applies unsaved form-layout work (positions, properties, deletions) after a (re)load.</summary>
    private void RestoreFieldLayoutState(DocumentState state)
    {
        foreach (var l in state.FieldLayouts)
        {
            var f = AllFields.FirstOrDefault(x => x.Name == l.Name && x.WidgetIndex == l.WidgetIndex);
            if (f == null) continue;
            SetFieldBounds(f, new FieldBounds(l.Left, l.Bottom, l.Width, l.Height), recordUndo: false);
        }
        foreach (var e in state.FieldEdits)
        {
            var widgets = AllFields.Where(x => x.Name == e.Name).ToList();
            if (widgets.Count == 0) continue;
            foreach (var w in widgets)
            {
                w.PendingName = e.PendingName;
                w.Tooltip = e.Tooltip;
                w.IsRequired = e.IsRequired;
                w.IsReadOnly = e.IsReadOnly;
                w.IsMultiline = e.IsMultiline;
                w.Alignment = e.Alignment;
                w.FontSize = e.FontSize;
                w.BorderColor = e.BorderColor;
                w.FillColor = e.FillColor;
                w.TextColor = e.TextColor;
                w.MaxLength = e.MaxLength;
                w.IsComb = e.IsComb;
                w.IsEditable = e.IsEditable;
                w.DateFormat = e.DateFormat;
                w.DefaultValue = e.DefaultValue;
                if (e.Options.Count > 0) w.Options = e.Options.ToList();
            }
            ModifiedFieldNames.Add(e.Name);
        }
        foreach (var name in state.DeletedFields)
        {
            if (!AllFields.Any(f => f.Name == name)) continue;
            DeletedFieldNames.Add(name);
            foreach (var f in AllFields.Where(f => f.Name == name).ToList()) AllFields.Remove(f);
            FieldValues.Remove(name);
        }
    }

    private void SyncRecentFileEntries()
    {
        RecentFileEntries.Clear();
        foreach (var p in AppSettings.Current.RecentFiles)
            RecentFileEntries.Add(new RecentFileEntry(p));
        OnPropertyChanged(nameof(HasNoRecentFiles));
    }

    /// <summary>Re-reads the model list (e.g. after detecting the local server's models).</summary>
    public void RefreshAiModels()
    {
        SyncAiModels();
        OnPropertyChanged(nameof(AiModel));
        OnPropertyChanged(nameof(ModelDisplayLabel));
        OnPropertyChanged(nameof(IsLocalAiConnected));
    }

    private void SyncAiModels()
    {
        AiModels.Clear();
        foreach (var m in Services.AiProviderService.GetModels(_aiProvider))
            AiModels.Add(m);
        if (AiModels.Count > 0 && !AiModels.Contains(_aiModel))
            _aiModel = AiModels[0];
    }

    private async Task ShowDocumentStatisticsAsync()
    {
        if (_currentFilePath == null || _document == null) return;
        StatusText = "Computing document statistics…";
        try
        {
            var fileInfo = new System.IO.FileInfo(_currentFilePath);
            int pageCount = _document.PageCount;
            int fieldCount = AllFields.Count;
            int highlightCount = HighlightAnnotations.Count;
            int stickyCount = StickyNotes.Count;
            int freeTextCount = FreeTextAnnotations.Count(f => !f.Text.StartsWith("__INK__:"));
            int inkCount = FreeTextAnnotations.Count(f => f.Text.StartsWith("__INK__:"));
            int shapeCount = ShapeAnnotations.Count;
            int totalAnnotations = highlightCount + stickyCount + freeTextCount + inkCount + shapeCount;
            int bookmarkCount = Bookmarks.Count;
            string docText = await Task.Run(() => Services.PdfTextExtractorService.GetDocumentText(_currentFilePath, 200000));
            int wordCount = string.IsNullOrWhiteSpace(docText) ? 0
                : docText.Split(new[] {' ', '\t', '\r', '\n'}, StringSplitOptions.RemoveEmptyEntries).Length;
            int charCount = docText.Replace("\n", "").Replace("\r", "").Length;

            string sizeStr = fileInfo.Length switch
            {
                < 1024 => $"{fileInfo.Length} B",
                < 1024 * 1024 => $"{fileInfo.Length / 1024.0:F1} KB",
                _ => $"{fileInfo.Length / (1024.0 * 1024):F2} MB"
            };

            string msg = $"Pages:               {pageCount}\n" +
                         $"File size:           {sizeStr}\n" +
                         $"Bookmarks:           {bookmarkCount}\n" +
                         $"\n" +
                         $"Form fields:         {fieldCount}\n" +
                         $"\n" +
                         $"Total annotations:   {totalAnnotations}\n" +
                         $"  Highlights/marks:  {highlightCount}\n" +
                         $"  Sticky notes:      {stickyCount}\n" +
                         $"  Free text:         {freeTextCount}\n" +
                         $"  Ink strokes:       {inkCount}\n" +
                         $"  Shapes:            {shapeCount}\n" +
                         $"\n" +
                         $"Word count:          {wordCount:N0}\n" +
                         $"Character count:     {charCount:N0}";

            System.Windows.MessageBox.Show(msg, "Document Statistics",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("Could not compute statistics.", ex); }
        finally { StatusText = "Ready"; }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A result item from global search.</summary>
public class SearchResult
{
    public string Label { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public FormFieldInfo? Field { get; set; }
    public FreeTextAnnotation? Annotation { get; set; }
}
