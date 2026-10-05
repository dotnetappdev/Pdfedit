using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public partial class SettingsWindow : Window
{
    // Snapshot of interface font sizes taken when the dialog opens, so Cancel
    // can revert any live changes the user previewed.
    private readonly Dictionary<string, double> _fontSnapshot;
    private bool _fontsInitialized;

    private readonly List<string> _fonts = new()
    {
        "Arial", "Times New Roman", "Courier New", "Georgia", "Verdana",
        "Tahoma", "Calibri", "Segoe UI", "Helvetica Neue", "Palatino Linotype"
    };

    // (label, format string) pairs shown with a live preview
    private static readonly (string Label, string Format)[] DateFormats =
    {
        ("Long date",        "MMMM d, yyyy"),
        ("US date",          "MM/dd/yyyy"),
        ("EU date",          "dd/MM/yyyy"),
        ("Dashed (EU)",      "dd-MM-yyyy"),
        ("ISO 8601",         "yyyy-MM-dd"),
        ("Short date",       "d MMM yyyy"),
        ("Full weekday",     "dddd, MMMM d, yyyy"),
        ("Short US",         "M/d/yy"),
    };

    public SettingsWindow()
    {
        InitializeComponent();
        _fontSnapshot = new Dictionary<string, double>(AppSettings.Current.InterfaceFontSizes);
        LoadCurrentSettings();
        InitFontSettings();
    }

    // ── Fonts tab (Visual Studio "Fonts and Colors" style) ───────────────────
    private void InitFontSettings()
    {
        for (double s = InterfaceFonts.MinSize; s <= InterfaceFonts.MaxSize; s++)
            FontSizeCombo.Items.Add(s.ToString("0"));

        // Apply custom sizes typed directly into the editable combo box.
        FontSizeCombo.LostFocus += (_, _) => ApplySelectedFontSize();
        FontSizeCombo.KeyUp += (_, ev) =>
        {
            if (ev.Key == System.Windows.Input.Key.Enter) ApplySelectedFontSize();
        };

        FontAreaList.ItemsSource = InterfaceFonts.Areas;
        _fontsInitialized = true;
        FontAreaList.SelectedIndex = 0; // triggers SelectionChanged → loads editor
    }

    private void FontAreaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_fontsInitialized || FontAreaList.SelectedItem is not InterfaceFontArea area)
            return;

        FontAreaName.Text = area.DisplayName;
        FontAreaDesc.Text = area.Description;

        double size = FontService.GetSize(area);
        FontSizeCombo.Text = size.ToString("0");
        FontPreview.FontSize = size;
    }

    private void FontSizeCombo_Changed(object sender, SelectionChangedEventArgs e)
        => ApplySelectedFontSize();

    private void ApplySelectedFontSize()
    {
        if (!_fontsInitialized || FontAreaList.SelectedItem is not InterfaceFontArea area)
            return;
        if (!double.TryParse(FontSizeCombo.Text, out var size))
            return;

        size = Math.Clamp(size, InterfaceFonts.MinSize, InterfaceFonts.MaxSize);
        FontService.SetSize(area, size);   // applies live to the whole app
        FontPreview.FontSize = size;
    }

    private void ResetFonts_Click(object sender, RoutedEventArgs e)
    {
        FontService.ResetAll();
        // Refresh the editor for the currently selected area.
        FontAreaList_SelectionChanged(FontAreaList, null!);
    }

    private void LoadCurrentSettings()
    {
        var s = AppSettings.Current;

        // Theme
        RbDark.IsChecked = s.Theme == "Dark";
        RbLight.IsChecked = s.Theme == "Light";
        RbHighContrast.IsChecked = s.Theme == "HighContrast";

        // Render engine
        RbEngineCustom.IsChecked = s.RenderEngine == "Custom" || (s.RenderEngine != "Pdfium" && s.RenderEngine != "WinRT");
        RbEnginePdfium.IsChecked = s.RenderEngine == "Pdfium";
        RbEngineWinRT.IsChecked  = s.RenderEngine == "WinRT";

        // UI Scale
        UiScaleSlider.Value = s.UiScale;
        UiScaleLabel.Text = $"{(int)(s.UiScale * 100)}%";

        // Editor
        DefaultFontCombo.ItemsSource = _fonts;
        DefaultFontCombo.SelectedItem = _fonts.Contains(s.DefaultFontFamily)
            ? s.DefaultFontFamily : "Arial";
        DefaultFontSizeTb.Text = s.DefaultFontSize.ToString("0");
        DefaultColorTb.Text = s.DefaultFontColor;
        ForceUpperCaseCb.IsChecked = s.ForceUpperCaseDefault;
        SpellCheckCb.IsChecked = s.SpellCheck;
        GoogleClientIdBox.Text = s.GoogleClientId;
        GoogleSecretBox.Password = s.GoogleClientSecret;
        OneDriveClientIdBox.Text = s.OneDriveClientId;
        OneDriveTenantBox.Text = string.IsNullOrWhiteSpace(s.OneDriveTenant) ? "common" : s.OneDriveTenant;
        CloudAutoUploadCb.IsChecked = s.CloudAutoUpload;
        UpdateCloudStatus();
        TipsAtStartupCb.IsChecked = s.ShowTipsAtStartup;
        TourNextStartCb.IsChecked = !s.TourCompleted;

        // Date formats
        BuildDateFormatPanel(s.DateFormat);

        // AI
        ApiKeyBox.Password = s.ClaudeApiKey;
        OpenAiKeyBox.Password = s.OpenAiApiKey;
        LocalEndpointBox.Text = s.LocalAiEndpoint;
        LocalModelBox.ItemsSource = AiProviderService.LocalModels;
        LocalModelBox.Text = s.LocalAiModel;
        LocalKeyBox.Password = s.LocalAiApiKey;

        // OCR
        OcrAuto.IsChecked = s.OcrEngine is not ("Tesseract" or "Windows");
        OcrTesseract.IsChecked = s.OcrEngine == "Tesseract";
        OcrWindows.IsChecked = s.OcrEngine == "Windows";
        BuildOcrLanguages(s.OcrLanguages);
        RefreshOcrStatus();

        // Accessibility
        HighContrastFocusCb.IsChecked = s.HighContrastFocusIndicators;
    }

    private void BuildDateFormatPanel(string currentFormat)
    {
        DateFormatPanel.Children.Clear();
        var today = DateTime.Today;
        var fg = (Brush)(TryFindResource("ForegroundBrush") ?? Brushes.White);
        var dim = (Brush)(TryFindResource("DimForegroundBrush") ?? Brushes.Gray);

        foreach (var (label, fmt) in DateFormats)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rb = new RadioButton
            {
                GroupName = "DateFormat",
                Tag = fmt,
                IsChecked = fmt == currentFormat,
                Foreground = fg,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, $"Date format: {label}");

            var labelSp = new StackPanel { Orientation = Orientation.Horizontal };
            labelSp.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = fg,
                FontSize = 13,
                Width = 120,
                VerticalAlignment = VerticalAlignment.Center
            });
            labelSp.Children.Add(new TextBlock
            {
                Text = $"({fmt})",
                Foreground = dim,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            });
            rb.Content = labelSp;
            Grid.SetColumn(rb, 0);

            var preview = new TextBlock
            {
                Text = today.ToString(fmt),
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 120)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
                MinWidth = 180
            };
            System.Windows.Automation.AutomationProperties.SetHelpText(preview, $"Preview: {today.ToString(fmt)}");
            Grid.SetColumn(preview, 1);

            row.Children.Add(rb);
            row.Children.Add(preview);
            DateFormatPanel.Children.Add(row);
        }
    }

    private string GetSelectedDateFormat()
    {
        foreach (Grid row in DateFormatPanel.Children)
        {
            if (row.Children[0] is RadioButton rb && rb.IsChecked == true && rb.Tag is string fmt)
                return fmt;
        }
        return "MMMM d, yyyy";
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
            App.SwitchTheme(tag);
    }

    private void RenderEngine_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            AppSettings.Current.RenderEngine = tag;
            AppSettings.Current.Save();
        }
    }

    private void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (UiScaleLabel != null)
            UiScaleLabel.Text = $"{(int)(e.NewValue * 100)}%";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;

        // Theme already applied by radio button handler
        s.UiScale = UiScaleSlider.Value;

        s.DefaultFontFamily = DefaultFontCombo.SelectedItem as string ?? "Arial";
        if (double.TryParse(DefaultFontSizeTb.Text, out var fs))
            s.DefaultFontSize = Math.Clamp(fs, 6, 144);
        s.DefaultFontColor = DefaultColorTb.Text;
        s.ForceUpperCaseDefault = ForceUpperCaseCb.IsChecked == true;
        s.SpellCheck = SpellCheckCb.IsChecked == true;
        StoreCloudCredentials();
        s.CloudAutoUpload = CloudAutoUploadCb.IsChecked == true;
        s.ShowTipsAtStartup = TipsAtStartupCb.IsChecked == true;
        s.TourCompleted = TourNextStartCb.IsChecked != true;

        s.ClaudeApiKey = ApiKeyBox.Password;
        s.OpenAiApiKey = OpenAiKeyBox.Password;
        s.LocalAiEndpoint = LocalEndpointBox.Text.Trim();
        s.LocalAiModel = LocalModelBox.Text.Trim();
        s.LocalAiApiKey = LocalKeyBox.Password;
        s.OcrEngine = OcrTesseract.IsChecked == true ? "Tesseract" : OcrWindows.IsChecked == true ? "Windows" : "Auto";
        var ticked = TickedOcrLanguages().Where(OcrService.InstalledTesseractLanguages().Contains).ToList();
        s.OcrLanguages = ticked.Count > 0 ? string.Join("+", ticked) : "eng";
        s.HighContrastFocusIndicators = HighContrastFocusCb.IsChecked == true;
        s.DateFormat = GetSelectedDateFormat();

        ShortcutsEditor.Commit();
        s.Save();
        ShortcutService.RaiseChanged();
        DialogResult = true;
        Close();
    }

    // ── OCR ─────────────────────────────────────────────────────────────────

    private void BuildOcrLanguages(string selected)
    {
        var chosen = selected.Split('+', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var installed = OcrService.InstalledTesseractLanguages().ToHashSet();
        OcrLanguagePanel.Children.Clear();
        foreach (var (code, name) in OcrService.CommonLanguages)
        {
            var cb = new CheckBox
            {
                Content = installed.Contains(code) ? name : $"{name} ↓",
                Tag = code,
                IsChecked = chosen.Contains(code),
                Width = 170, Margin = new Thickness(0, 2, 0, 2),
                ToolTip = installed.Contains(code) ? $"{code} — installed" : $"{code} — downloads when you click Download",
            };
            OcrLanguagePanel.Children.Add(cb);
        }
    }

    private IEnumerable<string> TickedOcrLanguages() =>
        OcrLanguagePanel.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag);

    private void RefreshOcrStatus()
    {
        bool win = OcrService.WindowsOcrAvailable;
        var langs = OcrService.InstalledTesseractLanguages();
        OcrStatusText.Text = $"Tesseract languages installed: {(langs.Count > 0 ? string.Join(", ", langs) : "none")}. " +
                             $"Windows OCR: {(win ? "available" : "no language installed")}.";
        WinOcrStatusText.Text = win
            ? "✓ Windows OCR is available for your language."
            : "Windows has no OCR language installed. That's fine — Tesseract is used instead. Install it if you prefer Windows' engine.";
        InstallWinOcrBtn.IsEnabled = !win;
    }

    private async void DownloadOcrLanguages_Click(object sender, RoutedEventArgs e)
    {
        var installed = OcrService.InstalledTesseractLanguages();
        var missing = TickedOcrLanguages().Where(c => !installed.Contains(c)).ToList();
        if (missing.Count == 0) { OcrStatusText.Text = "All ticked languages are already installed."; return; }
        OcrDownloadProgress.Visibility = Visibility.Visible;
        try
        {
            for (int i = 0; i < missing.Count; i++)
            {
                int index = i;
                OcrStatusText.Text = $"Downloading {missing[i]} ({i + 1} of {missing.Count})…";
                await OcrService.DownloadTesseractLanguageAsync(missing[i],
                    new Progress<double>(p => OcrDownloadProgress.Value = (index + p) / missing.Count * 100));
            }
            string selected = string.Join("+", TickedOcrLanguages());
            BuildOcrLanguages(selected);
            RefreshOcrStatus();
        }
        catch (Exception ex)
        {
            OcrStatusText.Text = "Download failed: " + ex.Message;
        }
        finally { OcrDownloadProgress.Visibility = Visibility.Collapsed; }
    }

    private async void InstallWindowsOcr_Click(object sender, RoutedEventArgs e)
    {
        WinOcrStatusText.Text = "Installing Windows OCR… (approve the Windows prompt; this can take a minute)";
        InstallWinOcrBtn.IsEnabled = false;
        bool ok = await OcrService.InstallWindowsOcrLanguageAsync();
        RefreshOcrStatus();
        if (!ok && !OcrService.WindowsOcrAvailable)
            WinOcrStatusText.Text = "Windows OCR wasn't installed (cancelled or not available for this language). Tesseract is still used, so OCR works.";
    }

    // ── Local AI ────────────────────────────────────────────────────────────

    private void LocalPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }) LocalEndpointBox.Text = url;
    }

    private async void DetectLocalModels_Click(object sender, RoutedEventArgs e)
    {
        LocalStatusText.Text = "Looking for models…";
        var models = await AiProviderService.DiscoverLocalModelsAsync(LocalEndpointBox.Text.Trim(), LocalKeyBox.Password);
        LocalModelBox.ItemsSource = models;
        if (models.Count == 0)
        {
            LocalStatusText.Text = "No models found — is the server running? (Ollama: run  ollama pull llama3.2)";
            return;
        }
        if (!models.Contains(LocalModelBox.Text)) LocalModelBox.Text = models[0];
        LocalStatusText.Text = $"Found {models.Count} model(s).";
    }

    private async void TestLocalAi_Click(object sender, RoutedEventArgs e)
    {
        string endpoint = LocalEndpointBox.Text.Trim(), model = LocalModelBox.Text.Trim();
        LocalStatusText.Text = $"Asking {model}…";
        var saved = (AppSettings.Current.LocalAiEndpoint, AppSettings.Current.LocalAiModel, AppSettings.Current.LocalAiApiKey);
        try
        {
            // Test with the values typed here (restored afterwards; Save keeps them).
            AppSettings.Current.LocalAiEndpoint = endpoint;
            AppSettings.Current.LocalAiModel = model;
            AppSettings.Current.LocalAiApiKey = LocalKeyBox.Password;
            var reply = new System.Text.StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await AiProviderService.SendStreamingAsync(
                new[] { new Models.AiChatMessage { Role = "user", Content = "Reply with just the word: ready" } },
                AiProviderService.LocalProvider, model, LocalKeyBox.Password, chunk => reply.Append(chunk), cts.Token);
            LocalStatusText.Text = reply.Length > 0
                ? $"✓ Working — {model} replied \"{reply.ToString().Trim()[..Math.Min(40, reply.ToString().Trim().Length)]}\""
                : "Connected, but the model returned nothing.";
        }
        catch (Exception ex)
        {
            LocalStatusText.Text = "✕ " + ex.Message;
        }
        finally
        {
            (AppSettings.Current.LocalAiEndpoint, AppSettings.Current.LocalAiModel, AppSettings.Current.LocalAiApiKey) = saved;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Restore original theme if user changed it without saving
        App.SwitchTheme(AppSettings.Current.Theme);

        // Revert any live font-size previews to the snapshot taken on open.
        AppSettings.Current.InterfaceFontSizes = new Dictionary<string, double>(_fontSnapshot);
        FontService.ApplyAll();

        DialogResult = false;
        Close();
    }

    // ── Cloud tab ─────────────────────────────────────────────────────────────

    /// <summary>Opens Settings on a given tab (e.g. "Cloud").</summary>
    public void ShowTab(string name)
    {
        if (name == "Cloud") SettingsTabs.SelectedItem = CloudTab;
        if (name == "Keyboard") SettingsTabs.SelectedItem = KeyboardTab;
    }

    private void StoreCloudCredentials()
    {
        var s = AppSettings.Current;
        string gId = GoogleClientIdBox.Text.Trim(), gSecret = GoogleSecretBox.Password.Trim();
        string mId = OneDriveClientIdBox.Text.Trim(), tenant = string.IsNullOrWhiteSpace(OneDriveTenantBox.Text) ? "common" : OneDriveTenantBox.Text.Trim();
        // Different app details: the old sign-in no longer applies.
        if (gId != s.GoogleClientId || gSecret != s.GoogleClientSecret) PdfEdit.Services.Cloud.GoogleDriveProvider.Instance.Disconnect();
        if (mId != s.OneDriveClientId || tenant != s.OneDriveTenant) PdfEdit.Services.Cloud.OneDriveProvider.Instance.Disconnect();
        s.GoogleClientId = gId;
        s.GoogleClientSecret = gSecret;
        s.OneDriveClientId = mId;
        s.OneDriveTenant = tenant;
    }

    private void UpdateCloudStatus()
    {
        var g = PdfEdit.Services.Cloud.GoogleDriveProvider.Instance;
        var m = PdfEdit.Services.Cloud.OneDriveProvider.Instance;
        GoogleConnectBtn.Content = g.IsConnected ? "Sign out" : "Connect";
        GoogleStatus.Text = g.IsConnected ? $"Connected as {g.AccountName}" : "Not connected";
        OneDriveConnectBtn.Content = m.IsConnected ? "Sign out" : "Connect";
        OneDriveStatus.Text = m.IsConnected ? $"Connected as {m.AccountName}" : "Not connected";
    }

    private async void GoogleConnect_Click(object sender, RoutedEventArgs e) =>
        await ConnectCloudAsync(PdfEdit.Services.Cloud.GoogleDriveProvider.Instance, GoogleConnectBtn, GoogleStatus);

    private async void OneDriveConnect_Click(object sender, RoutedEventArgs e) =>
        await ConnectCloudAsync(PdfEdit.Services.Cloud.OneDriveProvider.Instance, OneDriveConnectBtn, OneDriveStatus);

    private async Task ConnectCloudAsync(PdfEdit.Services.Cloud.CloudProvider p, System.Windows.Controls.Button btn, System.Windows.Controls.TextBlock status)
    {
        if (p.IsConnected) { p.Disconnect(); UpdateCloudStatus(); return; }
        StoreCloudCredentials();
        AppSettings.Current.Save();
        if (!p.IsConfigured)
        {
            status.Text = p is PdfEdit.Services.Cloud.GoogleDriveProvider ? "Enter the client ID and secret first." : "Enter the application ID first.";
            return;
        }
        btn.IsEnabled = false;
        status.Text = "Finish signing in in your browser…";
        try
        {
            await p.ConnectAsync();
            UpdateCloudStatus();
        }
        catch (Exception ex) { status.Text = "Couldn't sign in: " + ex.Message; }
        finally { btn.IsEnabled = true; Activate(); }
    }
}
