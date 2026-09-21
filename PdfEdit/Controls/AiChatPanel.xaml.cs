using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfEdit.Models;
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
    }

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
            // Inject field names for the fill preset
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
