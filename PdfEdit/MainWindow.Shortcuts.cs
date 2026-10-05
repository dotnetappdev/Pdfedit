using System.Windows.Input;
using PdfEdit.Controls;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit;

/// <summary>
/// Keyboard shortcuts from <see cref="ShortcutService"/>: combinations with Ctrl / Alt and function
/// keys become window key bindings; plain keys (tool letters) are handled here only when nothing
/// that takes typing has focus, so letters typed into fields always reach them.
/// </summary>
public partial class MainWindow
{
    private readonly List<InputBinding> _shortcutBindings = new();
    private Dictionary<KeyCombo, ShortcutAction> _plainShortcuts = new();

    private void ApplyShortcuts()
    {
        if (DataContext is not MainViewModel vm) return;
        foreach (var b in _shortcutBindings) InputBindings.Remove(b);
        _shortcutBindings.Clear();
        var plain = new Dictionary<KeyCombo, ShortcutAction>();
        foreach (var (combo, action) in ShortcutService.Map())
        {
            if (combo.IsPlain) { plain[combo] = action; continue; }
            if (action.Command(vm) is not { } cmd) continue;
            try
            {
                var binding = new KeyBinding(cmd, combo.Key, combo.Modifiers) { CommandParameter = action.Parameter };
                InputBindings.Add(binding);
                _shortcutBindings.Add(binding);
            }
            catch (NotSupportedException) { /* a combination Windows doesn't allow as a shortcut */ }
        }
        _plainShortcuts = plain;
    }

    private void OnPlainShortcutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainViewModel vm || vm.IsDesignMode) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
        if (PdfViewerControl.IsTextInputFocused()) return;
        if (!_plainShortcuts.TryGetValue(KeyCombo.FromEvent(e), out var action)) return;
        if (action.Category == "Tools" && !vm.HasDocument) return;
        var cmd = action.Command(vm);
        if (cmd?.CanExecute(action.Parameter) != true) return;
        cmd.Execute(action.Parameter);
        e.Handled = true;
    }
}
