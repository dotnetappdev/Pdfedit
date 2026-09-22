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
    public ObservableCollection<SearchResult> SearchResults { get; } = new();
    public ObservableCollection<RecentFileEntry> RecentFileEntries { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? PageChanged;
    public event Action? DocumentLoaded;
    public event Action? AnnotationFormattingChanged;
    public event Action<double>? UiScaleChanged;
    public event Action<bool>? SearchOverlayToggled;

    // ── Available options ────────────────────────────────────────────────────

    public IList<string> AvailableFonts { get; } = new List<string>
    {
        "Arial", "Times New Roman", "Courier New", "Georgia", "Verdana",
        "Tahoma", "Calibri", "Segoe UI", "Helvetica Neue", "Palatino Linotype"
    };

    public IList<string> AvailableStamps { get; } = new List<string>
    {
        "APPROVED", "CONFIDENTIAL", "DRAFT", "FINAL", "FOR REVIEW",
        "NOT APPROVED", "RECEIVED", "REJECTED", "REVISED", "VOID"
    };

    private string _selectedStamp = "APPROVED";
    public string SelectedStamp
    {
        get => _selectedStamp;
        set { _selectedStamp = value; OnPropertyChanged(); }
    }

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
        if (_currentFilePath != null)
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
        set { _selectedField = value; OnPropertyChanged(); }
    }

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
                CurrentTextAlignment = value.TextAlignment;
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
                _selectedAnnotation.TextAlignment = value;
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
    private string _aiModel = "claude-haiku-4-5-20251001";
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
            AppSettings.Current.Save();
        }
    }

    public IList<string> AiProviders { get; } = Services.AiProviderService.Providers.Keys.ToList();
    public ObservableCollection<string> AiModels { get; } = new();
    public ObservableCollection<AiChatMessage> AiChatHistory { get; } = new();

    // ── Connection state ─────────────────────────────────────────────────────

    public bool IsClaudeConnected => !string.IsNullOrEmpty(AppSettings.Current.ClaudeApiKey);
    public bool IsOpenAiConnected => !string.IsNullOrEmpty(AppSettings.Current.OpenAiApiKey);

    public string ProviderIcon => _aiProvider == "OpenAI" ? "☁" : "✦";

    public string ModelDisplayLabel
    {
        get
        {
            return _aiModel switch
            {
                "claude-haiku-4-5-20251001" => "Haiku",
                "claude-sonnet-5"           => "Sonnet 5",
                "claude-opus-5"             => "Opus 5",
                "gpt-4o-mini"               => "4o mini",
                "gpt-4o"                    => "4o",
                "gpt-3.5-turbo"             => "3.5 Turbo",
                _                           => _aiModel
            };
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

    public ObservableCollection<Models.BookmarkItem> Bookmarks { get; } = new();
    public ObservableCollection<Models.PdfAttachmentInfo> Attachments { get; } = new();

    public ICommand NavigateToBookmarkCommand { get; }
    public ICommand NavigateToPageCommand { get; }
    public ICommand UndoAnnotationCommand { get; }
    public ICommand RedoAnnotationCommand { get; }
    public ICommand AddAttachmentCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand ExtractAttachmentCommand { get; }

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
    public ICommand DeleteSelectedFieldCommand { get; }
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
    public ICommand ExportDesignCommand { get; }
    public ICommand OpenDesignInPdfViewCommand { get; }
    public ICommand ExportXfdfCommand { get; }
    public ICommand ImportXfdfCommand { get; }
    public ICommand ExportAnnotationSummaryCommand { get; }
    public ICommand FindAndHighlightCommand { get; }

    // ── Design Canvas ─────────────────────────────────────────────────────────
    private bool _isDesignMode;
    public bool IsDesignMode
    {
        get => _isDesignMode;
        set { _isDesignMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPdfMode)); }
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
        _forceUpperCase = AppSettings.Current.ForceUpperCaseDefault;
        _aiProvider = AppSettings.Current.AiProvider;
        _aiModel = AppSettings.Current.AiModel;
        SyncAiModels();

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
        DeleteSelectedFieldCommand = new RelayCommand(() => DeleteField(SelectedField), () => SelectedField != null);
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
        IncreaseUiScaleCommand = new RelayCommand(() => UiScale += 0.1);
        DecreaseUiScaleCommand = new RelayCommand(() => UiScale -= 0.1);
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
        MovePageUpCommand   = new AsyncRelayCommand(MovePageUpAsync,
            () => HasDocument && _currentPageIndex > 0);
        MovePageDownCommand = new AsyncRelayCommand(MovePageDownAsync,
            () => HasDocument && _currentPageIndex < (_document?.PageCount ?? 1) - 1);
        InsertPageBeforeCommand = new AsyncRelayCommand(InsertPageBeforeAsync, () => HasDocument);

        CancelAiCommand = new RelayCommand(() => { _aiCts?.Cancel(); }, () => _isAiRunning);
        SummarizeDocumentCommand    = new AsyncRelayCommand(() => RunAnalysisPresetAsync("summarize"),  () => HasDocument && !_isAiRunning);
        SmartFillFromDocCommand     = new AsyncRelayCommand(() => RunAnalysisPresetAsync("smartfill"),  () => HasDocument && !_isAiRunning);
        AnalyzeContractCommand      = new AsyncRelayCommand(() => RunAnalysisPresetAsync("contract"),   () => HasDocument && !_isAiRunning);
        ExtractKeyDataCommand       = new AsyncRelayCommand(() => RunAnalysisPresetAsync("extract"),    () => HasDocument && !_isAiRunning);
        FindPiiCommand              = new AsyncRelayCommand(() => RunAnalysisPresetAsync("pii"),        () => HasDocument && !_isAiRunning);

        NewDesignCommand = new RelayCommand(() =>
        {
            DesignCanvas.Elements.Clear();
            IsDesignMode = true;
            StatusText = "Design Canvas — draw shapes, text, and images to create a PDF from scratch.";
        });

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

        // Pre-select first profile if any exist
        if (Services.PersonalProfileStore.All.Count > 0)
            _selectedProfile = Services.PersonalProfileStore.All[0];

        SyncRecentFileEntries();
    }

    // ── Public Methods ───────────────────────────────────────────────────────

    public async Task OpenFileAsync(string path) => await LoadDocumentAsync(path);

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
            TextAlignment = _currentTextAlignment,
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
            FieldExportValues.Clear();
            AllFields.Clear();
            DeletedFieldNames.Clear();
            _pageRotations.Clear();
            FreeTextAnnotations.Clear();
            PlacedSignatures.Clear();
            HighlightAnnotations.Clear();
            RedactionRegions.Clear();
            StickyNotes.Clear();
            ShapeAnnotations.Clear();
            _undoService.Clear();
            Bookmarks.Clear();
            Attachments.Clear();

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

            await _renderService.LoadAsync(path);

            _currentPageIndex = 0;
            _zoom = 1.0;

            // Restore per-document state (last page + zoom)
            var docState = DocumentStateStore.Get(path);
            if (docState != null)
            {
                _currentPageIndex = Math.Clamp(docState.LastPageIndex, 0, Document.PageCount - 1);
                _zoom = Math.Clamp(docState.LastZoom, 0.1, 5.0);
                OnPropertyChanged(nameof(Zoom));
                OnPropertyChanged(nameof(ZoomPercent));
            }

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
            Dialogs.AppDialog.ShowError("Failed to open PDF", ex.Message, ex);
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
        if (_currentFilePath == null) { await SaveAsAsync(); return; }

        var tmp = _currentFilePath + ".tmp";
        try
        {
            var errors = _formService.SaveFull(_currentFilePath, tmp, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: false,
                deletedFieldNames: DeletedFieldNames, fieldExportValues: BuildExportValuesForSave(),
                highlightAnnotations: HighlightAnnotations, stickyNotes: StickyNotes,
                shapeAnnotations: ShapeAnnotations);
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            System.IO.File.Delete(tmp);
            StatusText = "Saved successfully.";
            if (errors.Count > 0)
            {
                ToastService.Instance.Warning($"Saved with {errors.Count} issue(s) — see details.");
                Dialogs.AppDialog.ShowError("Saved with warnings",
                    $"The file was saved but {errors.Count} field(s) could not be written:\n\n"
                    + string.Join("\n", errors.Take(10)));
            }
            else
            {
                ToastService.Instance.Success("Saved successfully.");
            }
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Save failed", ex.Message, ex);
            ToastService.Instance.Error("Save failed.");
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
            var errors = _formService.SaveFull(_currentFilePath!, dlg.FileName, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: false,
                deletedFieldNames: DeletedFieldNames, fieldExportValues: BuildExportValuesForSave(),
                highlightAnnotations: HighlightAnnotations, stickyNotes: StickyNotes,
                shapeAnnotations: ShapeAnnotations);
            _currentFilePath = dlg.FileName;
            StatusText = $"Saved as: {System.IO.Path.GetFileName(dlg.FileName)}";
            if (errors.Count > 0)
            {
                ToastService.Instance.Warning($"Saved with {errors.Count} issue(s).");
                Dialogs.AppDialog.ShowError("Saved with warnings",
                    $"The file was saved but {errors.Count} field(s) could not be written:\n\n"
                    + string.Join("\n", errors.Take(10)));
            }
            else
            {
                ToastService.Instance.Success($"Saved as {System.IO.Path.GetFileName(dlg.FileName)}");
            }
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Save failed", ex.Message, ex);
            ToastService.Instance.Error("Save failed.");
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
            var errors = _formService.SaveFull(_currentFilePath!, dlg.FileName, FieldValues,
                _pageRotations, FreeTextAnnotations, PlacedSignatures, flatten: true,
                deletedFieldNames: DeletedFieldNames, fieldExportValues: BuildExportValuesForSave(),
                highlightAnnotations: HighlightAnnotations, stickyNotes: StickyNotes,
                shapeAnnotations: ShapeAnnotations);
            StatusText = $"Flattened PDF saved: {System.IO.Path.GetFileName(dlg.FileName)}";
            if (errors.Count > 0)
            {
                ToastService.Instance.Warning($"Flattened with {errors.Count} issue(s).");
                Dialogs.AppDialog.ShowError("Flattened with warnings",
                    $"The file was saved but {errors.Count} field(s) could not be written:\n\n"
                    + string.Join("\n", errors.Take(10)));
            }
            else
            {
                ToastService.Instance.Success("Flattened PDF saved.");
            }
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Flatten & Save failed", ex.Message, ex);
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
        _currentFilePath = null;
        Document = null;
        _documentText = string.Empty;
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
        _undoService.Clear();
        _pageRotations.Clear();
        Attachments.Clear();
        SelectedField = null;
        SelectedAnnotation = null;
        StatusText = "Document closed.";
        ToastService.Instance.Info("Document closed.");
    }

    private void Print()
    {
        if (_document == null) return;

        var dlg = new System.Windows.Controls.PrintDialog();
        if (dlg.ShowDialog() == true)
        {
            // Print the current rendered page image via the WPF print dialog
            var doc = new System.Windows.Documents.FlowDocument();
            doc.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run($"Printing: {System.IO.Path.GetFileName(_currentFilePath)}\n\nFor best results, save the PDF and print from your system's PDF viewer.")));
            dlg.PrintDocument(
                ((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator,
                "PdfEdit Print");
            ToastService.Instance.Info("Sent to printer.");
        }
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
        AllFields.Remove(field);
        CurrentPageFields.Remove(field);
        FieldValues.Remove(field.Name);
        if (ReferenceEquals(SelectedField, field)) SelectedField = null;

        PageChanged?.Invoke();
        StatusText = $"Deleted field \"{field.Name}\". Save to make it permanent.";
        ToastService.Instance.Info($"Deleted field \"{field.Name}\".");
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
            Dialogs.AppDialog.ShowError("Could not compress PDF.", ex);
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

    private async Task WatermarkAsync()
    {
        if (_currentFilePath == null || _document == null) return;

        // Simple watermark dialog — collect text, opacity, angle, font size
        var dlg = new Dialogs.WatermarkDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var opt = dlg.Options;
        var dlgSave = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Watermarked PDF",
            Filter = "PDF files|*.pdf",
            FileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) + "_watermark.pdf",
            InitialDirectory = System.IO.Path.GetDirectoryName(_currentFilePath)
        };
        if (dlgSave.ShowDialog() != true) return;

        try
        {
            StatusText = "Applying watermark…";
            string outPath = dlgSave.FileName;
            await Task.Run(() => Services.WatermarkService.Apply(_currentFilePath, outPath, opt));
            StatusText = $"Watermarked PDF saved: {System.IO.Path.GetFileName(outPath)}";
            ToastService.Instance.Success("Watermark applied.");
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Could not apply watermark.", ex);
            StatusText = "Watermark failed.";
        }
    }

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
            var renderer = new Services.PdfRenderService();
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
        if (!Dialogs.AppDialog.Confirm($"Delete pages {dlg.FirstPage}–{dlg.LastPage}?\n\nThis operation cannot be undone.", "Delete Pages"))
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
        var key = _aiProvider == "OpenAI"
            ? AppSettings.Current.OpenAiApiKey
            : AppSettings.Current.ClaudeApiKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            ToastService.Instance.Warning($"No {_aiProvider} API key — set it in Settings → AI.");
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

        var key = _aiProvider == "OpenAI"
            ? AppSettings.Current.OpenAiApiKey
            : AppSettings.Current.ClaudeApiKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            ToastService.Instance.Warning($"No {_aiProvider} API key — add it in Settings → AI.");
            return;
        }

        AiChatHistory.Add(new AiChatMessage { Role = "user", Content = input });
        AiChatInput = string.Empty;

        var reply = new AiChatMessage { Role = "assistant", Content = "" };
        AiChatHistory.Add(reply);

        IsAiRunning = true;
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();

        try
        {
            var history = AiChatHistory.Take(AiChatHistory.Count - 1).ToList();

            // Build system prompt: profile context + document context
            var sysParts = new System.Text.StringBuilder();
            sysParts.Append("You are a helpful assistant for PDF documents. ");
            if (_selectedProfile != null)
                sysParts.Append(Services.PersonalProfileStore.BuildSystemPrompt(_selectedProfile)).Append("\n\n");
            if (!string.IsNullOrEmpty(_documentText))
                sysParts.Append("The user has the following PDF document open:\n\n").Append(_documentText);
            var sysPrompt = sysParts.Length > 35 ? sysParts.ToString() : null;

            await Services.AiProviderService.SendStreamingAsync(
                history, _aiProvider, _aiModel, key,
                chunk => Application.Current.Dispatcher.Invoke(() => reply.Content += chunk),
                _aiCts.Token,
                systemPrompt: sysPrompt);

            if (string.IsNullOrEmpty(reply.Content))
                reply.Content = "(No response — check your API key and model selection.)";
        }
        catch (OperationCanceledException)
        {
            reply.Content = "(Cancelled)";
        }
        catch (Exception ex)
        {
            reply.Content = $"Error: {ex.Message}";
            ToastService.Instance.Error("AI error — check your API key.");
        }
        finally
        {
            IsAiRunning = false;
        }
    }

    // Called by AiChatPanel preset chips and by the new AI commands
    public async Task RunAnalysisPresetAsync(string analysisType, string? overridePrompt = null)
    {
        var key = _aiProvider == "OpenAI"
            ? AppSettings.Current.OpenAiApiKey
            : AppSettings.Current.ClaudeApiKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            ToastService.Instance.Warning($"No {_aiProvider} API key — add it via the ⚙ icon in the AI panel.");
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
            await Services.AiProviderService.AnalyzeDocumentAsync(
                _documentText, analysisType, _aiProvider, _aiModel, key,
                chunk => Application.Current.Dispatcher.Invoke(() => reply.Content += chunk),
                _aiCts.Token,
                fieldNames: fieldNames);

            if (string.IsNullOrEmpty(reply.Content))
                reply.Content = "(No response — check your API key and model selection.)";
        }
        catch (OperationCanceledException)
        {
            reply.Content = "(Cancelled)";
        }
        catch (Exception ex)
        {
            reply.Content = $"Error: {ex.Message}";
            ToastService.Instance.Error("AI analysis failed.");
        }
        finally
        {
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

        var dlg = new Dialogs.InsertPdfDialog(_currentPageIndex + 1, TotalPages);
        dlg.Owner = System.Windows.Application.Current.MainWindow;
        if (dlg.ShowDialog() != true || dlg.SelectedFilePath == null) return;

        // Determine 0-based index after which pages are inserted (-1 = before page 0)
        int insertAfterIndex = dlg.InsertPosition switch
        {
            Dialogs.InsertPdfPosition.Beginning     => -1,
            Dialogs.InsertPdfPosition.BeforeCurrent => _currentPageIndex - 1,
            Dialogs.InsertPdfPosition.AfterCurrent  => _currentPageIndex,
            Dialogs.InsertPdfPosition.End           => TotalPages - 1,
            _                                       => _currentPageIndex
        };

        var tmp = System.IO.Path.GetTempFileName() + ".pdf";
        try
        {
            await Task.Run(() => _formService.InsertPdfAt(_currentFilePath, dlg.SelectedFilePath, tmp, insertAfterIndex));
            System.IO.File.Copy(tmp, _currentFilePath, overwrite: true);
            var savedPage = _currentPageIndex;
            await LoadDocumentAsync(_currentFilePath);
            CurrentPageIndex = Math.Min(savedPage, TotalPages - 1);
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

        // Search page text content
        if (_currentFilePath != null && _document != null)
        {
            for (int pg = 1; pg <= _document.PageCount; pg++)
            {
                var pageText = Services.PdfTextExtractorService.GetPageText(_currentFilePath, pg);
                if (pageText.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    // Find a snippet around the match
                    int idx = pageText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    int start = Math.Max(0, idx - 20);
                    int len = Math.Min(60, pageText.Length - start);
                    string snippet = "…" + pageText.Substring(start, len).Replace('\n', ' ') + "…";
                    SearchResults.Add(new SearchResult
                    {
                        Label = $"Page {pg}",
                        Detail = snippet,
                        Kind = "Page Text",
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
                    Opacity = 0.4f,
                    Kind   = Models.HighlightKind.Highlight,
                };
                // Add directly — AddHighlightAnnotation would overwrite PageNumber
                HighlightAnnotations.Add(hl);
            }
            _undoService.Push(new Services.AnnotationAction
            {
                Description = $"Find & highlight \"{query}\" ({matches.Count})",
                Execute     = () => { foreach (var m in matches) HighlightAnnotations.Add(new Models.HighlightAnnotation { PageNumber = m.PageNumber, Left = m.Left, Bottom = m.Bottom, Width = m.Width, Height = Math.Max(m.Height, 6), Color = CurrentHighlightColor, Opacity = 0.4f }); },
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
            LastZoom = _zoom
        });
    }

    private void SyncRecentFileEntries()
    {
        RecentFileEntries.Clear();
        foreach (var p in AppSettings.Current.RecentFiles)
            RecentFileEntries.Add(new RecentFileEntry(p));
        OnPropertyChanged(nameof(HasNoRecentFiles));
    }

    private void SyncAiModels()
    {
        AiModels.Clear();
        foreach (var m in Services.AiProviderService.GetModels(_aiProvider))
            AiModels.Add(m);
        if (AiModels.Count > 0 && !AiModels.Contains(_aiModel))
            _aiModel = AiModels[0];
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
