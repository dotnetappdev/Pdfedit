using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit.Controls;

public partial class AiChatPanel : UserControl
{
    private MainViewModel? _vm;

    public AiChatPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as MainViewModel;
        if (_vm == null) return;

        ChatList.ItemsSource = _vm.AiChatHistory;
        _vm.AiChatHistory.CollectionChanged += OnHistoryChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;

        RefreshModelList();
        RefreshConnectionStatus();
        UpdateDocContextBadge();
    }

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EmptyState.Visibility = _vm?.AiChatHistory.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.BeginInvoke(() => ChatScroll.ScrollToBottom());
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsAiRunning))
        {
            bool running = _vm?.IsAiRunning == true;
            ThinkingIndicator.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            CancelBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            SendBtn.Visibility   = running ? Visibility.Collapsed : Visibility.Visible;
            if (running)
                Dispatcher.BeginInvoke(() => ChatScroll.ScrollToBottom());
        }
        else if (e.PropertyName is nameof(MainViewModel.IsClaudeConnected) or nameof(MainViewModel.IsOpenAiConnected))
        {
            RefreshConnectionStatus();
        }
        else if (e.PropertyName == nameof(MainViewModel.AiProvider))
        {
            RefreshModelList();
            RefreshPopupStatus();
        }
        else if (e.PropertyName == nameof(MainViewModel.DocumentContextReady))
        {
            UpdateDocContextBadge();
        }
    }

    private void UpdateDocContextBadge()
    {
        bool ready = _vm?.DocumentContextReady == true;
        DocContextBadge.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Model picker ────────────────────────────────────────────────────────

    private void ModelPickerBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshModelList();
        RefreshPopupStatus();
        ModelPickerPopup.IsOpen = !ModelPickerPopup.IsOpen;
    }

    private void ProviderTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string provider && _vm != null)
        {
            _vm.AiProvider = provider;
            RefreshModelList();
            RefreshPopupStatus();
        }
    }

    private void RefreshModelList()
    {
        if (_vm == null) return;
        ModelListPanel.Children.Clear();

        var models = AiProviderService.GetModels(_vm.AiProvider);
        foreach (var model in models)
        {
            var rb = new RadioButton
            {
                Style = (Style)Resources["ModelRadioBtn"],
                IsChecked = model == _vm.AiModel,
                Tag = model,
                Content = ModelShortName(model),
                GroupName = "ModelGroup"
            };
            rb.Checked += ModelRadio_Checked;
            ModelListPanel.Children.Add(rb);
        }
    }

    private void ModelRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string model && _vm != null)
        {
            _vm.AiModel = model;
            ModelPickerPopup.IsOpen = false;
        }
    }

    private void RefreshPopupStatus()
    {
        if (_vm == null) return;
        bool connected = _vm.AiProvider == "OpenAI" ? _vm.IsOpenAiConnected : _vm.IsClaudeConnected;
        var green = new SolidColorBrush(Color.FromRgb(52, 199, 89));
        var red   = new SolidColorBrush(Color.FromRgb(204, 68, 68));
        PopupStatusDot.Fill  = connected ? green : red;
        PopupStatusText.Text = connected ? "Connected" : "Not connected — add key in ⚙";
    }

    private static string ModelShortName(string model) => model switch
    {
        "claude-haiku-4-5-20251001" => "Haiku  ·  fast & efficient",
        "claude-sonnet-5"           => "Sonnet 5  ·  balanced",
        "claude-opus-5"             => "Opus 5  ·  most capable",
        "gpt-4o-mini"               => "4o mini  ·  fast & efficient",
        "gpt-4o"                    => "4o  ·  balanced",
        "gpt-3.5-turbo"             => "3.5 Turbo  ·  legacy",
        _                           => model
    };

    // ── Connections popup ────────────────────────────────────────────────────

    private void ConnectionsBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshConnectionStatus();
        ConnectionsPopup.IsOpen = !ConnectionsPopup.IsOpen;
    }

    private void RefreshConnectionStatus()
    {
        var green = new SolidColorBrush(Color.FromRgb(52, 199, 89));
        var red   = new SolidColorBrush(Color.FromRgb(204, 68, 68));

        bool claudeOk = _vm?.IsClaudeConnected == true;
        ClaudeStatusDot.Fill   = claudeOk ? green : red;
        ClaudeStatusText.Text  = claudeOk ? "Connected" : "Not connected";
        ClaudeConnectedView.Visibility = claudeOk ? Visibility.Visible : Visibility.Collapsed;
        ClaudeConnectView.Visibility   = claudeOk ? Visibility.Collapsed : Visibility.Visible;
        if (claudeOk)
        {
            var key = AppSettings.Current.ClaudeApiKey;
            ClaudeKeyHint.Text = MaskKey(key);
        }

        bool openAiOk = _vm?.IsOpenAiConnected == true;
        OpenAiStatusDot.Fill   = openAiOk ? green : red;
        OpenAiStatusText.Text  = openAiOk ? "Connected" : "Not connected";
        OpenAiConnectedView.Visibility = openAiOk ? Visibility.Visible : Visibility.Collapsed;
        OpenAiConnectView.Visibility   = openAiOk ? Visibility.Collapsed : Visibility.Visible;
        if (openAiOk)
        {
            var key = AppSettings.Current.OpenAiApiKey;
            OpenAiKeyHint.Text = MaskKey(key);
        }
    }

    private static string MaskKey(string key)
    {
        if (key.Length <= 8) return "••••••••";
        return key[..4] + "••••" + key[^4..];
    }

    private void ClaudeConnect_Click(object sender, RoutedEventArgs e)
    {
        var key = ClaudeKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            ClaudeKeyBox.BorderBrush = new SolidColorBrush(Color.FromRgb(204, 68, 68));
            return;
        }
        _vm?.ConnectClaude(key);
        ClaudeKeyBox.Clear();
        RefreshConnectionStatus();
        RefreshPopupStatus();
    }

    private void ClaudeSignOut_Click(object sender, RoutedEventArgs e)
    {
        _vm?.DisconnectClaude();
        RefreshConnectionStatus();
        RefreshPopupStatus();
    }

    private void OpenAiConnect_Click(object sender, RoutedEventArgs e)
    {
        var key = OpenAiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            OpenAiKeyBox.BorderBrush = new SolidColorBrush(Color.FromRgb(204, 68, 68));
            return;
        }
        _vm?.ConnectOpenAi(key);
        OpenAiKeyBox.Clear();
        RefreshConnectionStatus();
        RefreshPopupStatus();
    }

    private void OpenAiSignOut_Click(object sender, RoutedEventArgs e)
    {
        _vm?.DisconnectOpenAi();
        RefreshConnectionStatus();
        RefreshPopupStatus();
    }

    // ── Presets ──────────────────────────────────────────────────────────────

    // Tags that go through AnalyzeDocumentAsync (document-context presets)
    private static readonly HashSet<string> AnalysisPresets = new()
        { "summarize", "extract", "contract", "pii", "translate", "smartfill" };

    // Tags that populate the chat input for the user to edit then send
    private static readonly Dictionary<string, string> InputPresets = new()
    {
        ["fill"] = "Please fill in the form fields based on the document.",
        ["qa"]   = "I have a question about this document: ",
    };

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag || _vm == null) return;

        if (AnalysisPresets.Contains(tag))
        {
            // Run analysis directly — no need to type in the chat box
            _ = _vm.RunAnalysisPresetAsync(tag);
        }
        else if (InputPresets.TryGetValue(tag, out var prompt))
        {
            if (tag == "fill" && _vm.AllFields.Count > 0)
            {
                var names = string.Join(", ", _vm.AllFields.Select(f => f.Name));
                prompt = $"Fill these form fields with appropriate values: {names}";
            }
            _vm.AiChatInput = prompt;
            InputBox.Focus();
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.CancelAiCommand?.CanExecute(null) == true)
            _vm.CancelAiCommand.Execute(null);
    }

    // ── Send / Clear / Keyboard ──────────────────────────────────────────────

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.SendAiChatCommand?.CanExecute(null) == true)
            _vm.SendAiChatCommand.Execute(null);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.ClearAiChatCommand?.CanExecute(null) == true)
            _vm.ClearAiChatCommand.Execute(null);
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            Send_Click(sender, e);
            e.Handled = true;
        }
    }
}
