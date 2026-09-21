using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly PdfFormService _formService = new();
    private readonly PdfRenderService _renderService = new();

    private PdfDocumentInfo? _document;
    private int _currentPageIndex;
    private double _zoom = 1.0;
    private string _statusText = "Ready — Open a PDF to begin.";
    private FormFieldInfo? _selectedField;
    private FreeTextAnnotation? _selectedAnnotation;
    private ActiveTool _activeTool = ActiveTool.Hand;
    private bool _isLoading;
    private bool _highlightFields = true;
    private string? _currentFilePath;

    // Font/style state for new and selected annotations
    private double _currentFontSize = 12;
    private string _currentFontFamily = "Arial";
    private bool _currentFontBold;
    private bool _currentFontItalic;
    private bool _currentFontUnderline;
    private string _currentFontColor = "#000000";
    private bool _updatingFromAnnotation;

    // Page rotation: pageIndex → cumulative degrees (0/90/180/270)
    private readonly Dictionary<int, int> _pageRotations = new();

    // Free-text annotations placed by the user
    public ObservableCollection<FreeTextAnnotation> FreeTextAnnotations { get; } = new();

    // Placed signature images
    public ObservableCollection<PlacedSignature> PlacedSignatures { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? PageChanged;
    public event Action? DocumentLoaded;
    public event Action? AnnotationFormattingChanged;

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

    public int CurrentPageRotation => GetPageRotation(_currentPageIndex);

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

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public FormFieldInfo? SelectedField
    {
        get => _selectedField;
        set { _selectedField = value; OnPropertyChanged(); }
    }

    /// <summary>The free-text annotation currently focused/selected in the viewer.</summary>
    public FreeTextAnnotation? SelectedAnnotation
    {
        get => _selectedAnnotation;
        set
        {
            _selectedAnnotation = value;
            OnPropertyChanged();
            if (value != null)
            {
                _updatingFromAnnotation = true;
                CurrentFontSize = value.FontSize;
                CurrentFontFamily = value.FontFamily;
                CurrentFontBold = value.IsBold;
                CurrentFontItalic = value.IsItalic;
                CurrentFontUnderline = value.IsUnderline;
                CurrentFontColor = value.FontColor;
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

    // ── Collections ──────────────────────────────────────────────────────────

    public Dictionary<string, string> FieldValues { get; } = new();
    public ObservableCollection<FormFieldInfo> CurrentPageFields { get; } = new();
    public ObservableCollection<FormFieldInfo> AllFields { get; } = new();

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand FirstPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomFitCommand { get; }
    public ICommand ZoomActualCommand { get; }
    public ICommand ClearAllFieldsCommand { get; }
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

    public MainViewModel()
    {
        OpenCommand = new AsyncRelayCommand(OpenAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasDocument);
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync, () => HasDocument);
        CloseCommand = new RelayCommand(CloseDocument, () => HasDocument);
        PrintCommand = new RelayCommand(Print, () => HasDocument);
        NextPageCommand = new RelayCommand(() => CurrentPageIndex++,
            () => HasDocument && _currentPageIndex < (_document?.PageCount ?? 1) - 1);
        PreviousPageCommand = new RelayCommand(() => CurrentPageIndex--,
            () => HasDocument && _currentPageIndex > 0);
        FirstPageCommand = new RelayCommand(() => CurrentPageIndex = 0, () => HasDocument);
        LastPageCommand = new RelayCommand(() => CurrentPageIndex = (_document?.PageCount ?? 1) - 1, () => HasDocument);
        ZoomInCommand = new RelayCommand(() => Zoom += 0.1, () => HasDocument);
        ZoomOutCommand = new RelayCommand(() => Zoom -= 0.1, () => HasDocument);
        ZoomFitCommand = new RelayCommand(() => Zoom = 1.0, () => HasDocument);
        ZoomActualCommand = new RelayCommand(() => Zoom = 1.0, () => HasDocument);
        ClearAllFieldsCommand = new RelayCommand(ClearAllFields, () => HasDocument);
        ExportDataCommand = new AsyncRelayCommand(ExportDataAsync, () => HasDocument);
        ImportDataCommand = new AsyncRelayCommand(ImportDataAsync, () => HasDocument);
        SetToolCommand = new RelayCommand(p =>
        {
            if (p is ActiveTool t) ActiveTool = t;
            else if (p is string s && Enum.TryParse<ActiveTool>(s, out var st)) ActiveTool = st;
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
    }

    // ── Public Methods ───────────────────────────────────────────────────────

    public async Task OpenFileAsync(string path) => await LoadDocumentAsync(path);

    public PdfRenderService RenderService => _renderService;

    public int GetPageRotation(int pageIndex)
        => _pageRotations.TryGetValue(pageIndex, out var r) ? r : 0;

    public void UpdateFieldValue(string fieldName, string value)
    {
        FieldValues[fieldName] = value;
        var field = AllFields.FirstOrDefault(f => f.Name == fieldName);
        if (field != null) field.Value = value;
        StatusText = $"Field '{fieldName}' updated.";
    }

    /// <summary>Adds a free-text annotation on the current page with current font settings applied.</summary>
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
            IsVertical = isVertical,
            FontSize = fontSize ?? _currentFontSize,
            FontFamily = _currentFontFamily,
            IsBold = _currentFontBold,
            IsItalic = _currentFontItalic,
            IsUnderline = _currentFontUnderline,
            FontColor = _currentFontColor,
        };
        FreeTextAnnotations.Add(ann);
        return ann;
    }

    public void RemoveFreeTextAnnotation(FreeTextAnnotation ann)
    {
        FreeTextAnnotations.Remove(ann);
        if (_selectedAnnotation == ann) SelectedAnnotation = null;
    }

    public IEnumerable<FreeTextAnnotation> GetAnnotationsForCurrentPage()
        => FreeTextAnnotations.Where(a => a.PageNumber == _currentPageIndex + 1);

    public void AddPlacedSignature(PlacedSignature sig) => PlacedSignatures.Add(sig);

    public void RemovePlacedSignature(PlacedSignature sig) => PlacedSignatures.Remove(sig);

    public IEnumerable<PlacedSignature> GetSignaturesForCurrentPage()
        => PlacedSignatures.Where(s => s.PageNumber == _currentPageIndex + 1);

    // ── Private Commands ─────────────────────────────────────────────────────

    private void DeleteSelectedAnnotation()
    {
        if (_selectedAnnotation == null) return;
        RemoveFreeTextAnnotation(_selectedAnnotation);
        PageChanged?.Invoke();
        StatusText = "Annotation deleted.";
    }

    private async Task OpenAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open PDF",
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
            DefaultExt = ".pdf"
        };
        if (dlg.ShowDialog() != true) return;
        await LoadDocumentAsync(dlg.FileName);
    }

    private async Task LoadDocumentAsync(string path)
    {
        IsLoading = true;
        StatusText = $"Loading {System.IO.Path.GetFileName(path)}…";

        try
        {
            _currentFilePath = path;
            Document = _formService.LoadDocument(path);

            FieldValues.Clear();
            AllFields.Clear();
            _pageRotations.Clear();
            FreeTextAnnotations.Clear();
            PlacedSignatures.Clear();

            foreach (var f in Document.FormFields)
            {
                AllFields.Add(f);
                FieldValues[f.Name] = f.Value;
            }

            await _renderService.LoadAsync(path);

            _currentPageIndex = 0;
            OnPropertyChanged(nameof(CurrentPageIndex));
            OnPropertyChanged(nameof(CurrentPageRotation));
            RefreshCurrentPageFields();

            DocumentLoaded?.Invoke();
            StatusText = $"Opened: {System.IO.Path.GetFileName(path)} — " +
                         $"{Document.PageCount} page(s), {Document.FormFields.Count} form field(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open PDF:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Error loading document.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SaveAsync()
    {
        if (_currentFilePath == null) { await SaveAsAsync(); return; }

        var tmp = _currentFilePath + ".tmp";
        try
        {
            _formService.SaveFull(_currentFilePath, tmp, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: false);
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            System.IO.File.Delete(tmp);
            StatusText = "Saved successfully.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }

    private async Task SaveAsAsync()
    {
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
            _formService.SaveFull(_currentFilePath!, dlg.FileName, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: false);
            _currentFilePath = dlg.FileName;
            StatusText = $"Saved as: {System.IO.Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task FlattenAndSaveAsync()
    {
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
            _formService.SaveFull(_currentFilePath!, dlg.FileName, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: true);
            StatusText = $"Flattened PDF saved: {System.IO.Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Flatten & Save failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseDocument()
    {
        _currentFilePath = null;
        Document = null;
        FieldValues.Clear();
        AllFields.Clear();
        CurrentPageFields.Clear();
        FreeTextAnnotations.Clear();
        PlacedSignatures.Clear();
        _pageRotations.Clear();
        SelectedField = null;
        SelectedAnnotation = null;
        StatusText = "Document closed.";
    }

    private void Print()
    {
        MessageBox.Show("Print is not yet implemented.\nSave the filled PDF and print from your PDF viewer.",
            "Print", MessageBoxButton.OK, MessageBoxImage.Information);
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
    }

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
    }

    private void RotatePage(int degrees)
    {
        int current = GetPageRotation(_currentPageIndex);
        int next = (current + degrees + 360) % 360;
        if (next == 0) _pageRotations.Remove(_currentPageIndex);
        else _pageRotations[_currentPageIndex] = next;

        OnPropertyChanged(nameof(CurrentPageRotation));
        PageChanged?.Invoke();
        StatusText = $"Page {_currentPageIndex + 1} rotated to {next}°.";
    }

    public void RefreshCurrentPageFields()
    {
        CurrentPageFields.Clear();
        if (_document == null) return;
        int page = _currentPageIndex + 1;
        foreach (var f in AllFields.Where(f => f.PageNumber == page))
            CurrentPageFields.Add(f);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
