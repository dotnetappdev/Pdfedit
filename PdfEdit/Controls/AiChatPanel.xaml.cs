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

    private static readonly Dictionary<string, string> Presets = new()
    {
        ["fill"]      = "Please fill in the form fields appropriately based on the document context.",
        ["summarize"] = "Summarize the main content, purpose, and key points of this PDF document.",
        ["extract"]   = "Extract and list all key data: names, dates, addresses, numbers, and any important details from this PDF.",
        ["qa"]        = "I'll ask questions about this PDF. Please answer based on the document content.",
        ["redact"]    = "Identify all personally identifiable information (PII) — names, addresses, phone numbers, email addresses, SSNs, financial data — that should be redacted for privacy compliance.",
        ["translate"] = "Translate the main content of this PDF into English (or specify a target language)."
    };

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Presets.TryGetValue(tag, out var prompt))
        {
            if (tag == "fill" && _vm?.AllFields.Count > 0)
            {
                var names = string.Join(", ", _vm.AllFields.Select(f => f.Name));
                prompt = $"Please fill in the following form fields: {names}. Provide appropriate values based on the document context.";
            }

            if (_vm != null)
                _vm.AiChatInput = prompt;

            InputBox.Focus();
        }
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
