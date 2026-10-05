using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfEdit.Services;

namespace PdfEdit.Controls;

/// <summary>Settings → Keyboard: see and change every shortcut. Changes apply when Settings is saved.</summary>
public partial class ShortcutEditor : UserControl
{
    public sealed class Row : INotifyPropertyChanged
    {
        private List<KeyCombo> _combos;
        public ShortcutAction Action { get; }
        public string Category => Action.Category;
        public string Label => Action.Label;
        public List<KeyCombo> Combos { get => _combos; set { _combos = value; OnChanged(nameof(Keys)); OnChanged(nameof(IsCustom)); } }
        public string Keys => _combos.Count == 0 ? "—" : ShortcutService.Display(_combos);
        public bool IsCustom => !_combos.SequenceEqual(Action.DefaultCombos);

        public Row(ShortcutAction a, IEnumerable<KeyCombo> combos) { Action = a; _combos = combos.ToList(); }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // Keys that must keep their usual job when pressed on their own.
    private static readonly HashSet<Key> Reserved = new()
    {
        Key.Tab, Key.Enter, Key.Return, Key.Escape, Key.Space, Key.Back, Key.Delete,
        Key.Left, Key.Right, Key.Up, Key.Down, Key.Home, Key.End, Key.PageUp, Key.PageDown,
    };

    private readonly List<Row> _rows;
    private KeyCombo? _captured;

    public ShortcutEditor()
    {
        InitializeComponent();
        _rows = ShortcutService.Actions.Select(a => new Row(a, ShortcutService.Effective(a))).ToList();
        List.ItemsSource = _rows;
    }

    /// <summary>Saves the changes into the settings (called when Settings is saved).</summary>
    public void Commit()
    {
        var s = AppSettings.Current.Shortcuts;
        s.Clear();
        foreach (var r in _rows.Where(r => r.IsCustom))
            s[r.Action.Id] = string.Join(", ", r.Combos.Select(c => c.ToString()));
    }

    private Row? Selected => List.SelectedItem as Row;

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        string q = SearchBox.Text.Trim();
        List.ItemsSource = q.Length == 0 ? _rows
            : _rows.Where(r => r.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
                               || r.Keys.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool any = Selected != null;
        CaptureBox.IsEnabled = ClearBtn.IsEnabled = ResetBtn.IsEnabled = any;
        _captured = null;
        CaptureBox.Text = "";
        AssignBtn.IsEnabled = AddBtn.IsEnabled = false;
        Status.Text = any ? $"“{Selected!.Label}”: click in the box and press the new keys." : "";
    }

    private void Capture_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_captured == null) CaptureBox.Text = "Press the keys…";
    }

    private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;   // the box only records keys
        var combo = KeyCombo.FromEvent(e);
        if (KeyCombo.IsModifierKey(combo.Key)) return;
        if (combo.IsPlain && Reserved.Contains(combo.Key))
        {
            Status.Text = $"{KeyCombo.KeyName(combo.Key)} on its own is needed for moving around and typing. Add Ctrl or Alt.";
            return;
        }
        _captured = combo;
        CaptureBox.Text = combo.ToString();
        var other = _rows.FirstOrDefault(r => r != Selected && r.Combos.Contains(combo));
        Status.Text = other != null ? $"{combo} is used by “{other.Label}”. Assigning moves it here." : $"Click Assign to use {combo}.";
        AssignBtn.IsEnabled = AddBtn.IsEnabled = Selected != null;
    }

    private void TakeFromOthers(KeyCombo combo)
    {
        foreach (var r in _rows.Where(r => r != Selected && r.Combos.Contains(combo)))
            r.Combos = r.Combos.Where(c => c != combo).ToList();
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || _captured is not { } combo) return;
        TakeFromOthers(combo);
        row.Combos = new List<KeyCombo> { combo };
        Status.Text = $"“{row.Label}” is now {combo}.";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || _captured is not { } combo || row.Combos.Contains(combo)) return;
        TakeFromOthers(combo);
        row.Combos = row.Combos.Append(combo).ToList();
        Status.Text = $"“{row.Label}”: {row.Keys}.";
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        row.Combos = new List<KeyCombo>();
        Status.Text = $"“{row.Label}” has no shortcut.";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        foreach (var c in row.Action.DefaultCombos) TakeFromOthers(c);
        row.Combos = row.Action.DefaultCombos.ToList();
        Status.Text = $"“{row.Label}” is back to {row.Keys}.";
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rows) r.Combos = r.Action.DefaultCombos.ToList();
        Status.Text = "Every shortcut is back to the built-in one.";
    }
}
