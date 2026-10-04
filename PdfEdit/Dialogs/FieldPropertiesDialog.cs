using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.ViewModels;

namespace PdfEdit.Dialogs;

/// <summary>
/// Acrobat Pro's "Text Field Properties" style dialog: General (name, tooltip, read only,
/// required), Appearance (border, fill, font size, text colour) and Options (alignment, default,
/// multi-line, character limit, comb, date format, list items, custom text). Works on a copy;
/// <see cref="Result"/> is applied by the caller.
/// </summary>
public class FieldPropertiesDialog : Window
{
    public FormFieldInfo Result { get; } = new();

    private readonly MainViewModel _vm;
    private readonly FormFieldInfo _field;

    private sealed record ColorChoice(string Name, string? Hex)
    {
        public Brush Swatch => Hex == null ? Brushes.Transparent : (Brush)new BrushConverter().ConvertFrom(Hex)!;
        public override string ToString() => Name;
    }

    private static readonly ColorChoice[] Palette =
    {
        new("No colour", null), new("Black", "#000000"), new("Dark grey", "#404040"), new("Grey", "#808080"),
        new("Light grey", "#C0C0C0"), new("White", "#FFFFFF"), new("Red", "#FF0000"), new("Dark red", "#8B0000"),
        new("Orange", "#FF8C00"), new("Yellow", "#FFD700"), new("Green", "#008000"), new("Light blue", "#DDE7FF"),
        new("Blue", "#0000FF"), new("Dark blue", "#00008B"), new("Purple", "#800080"),
    };

    private static readonly string[] DateFormats = { "dd/mm/yyyy", "mm/dd/yyyy", "yyyy-mm-dd", "d mmm yyyy", "dd-mmm-yy", "mmmm d, yyyy" };

    // controls
    private readonly TextBox _name = new(), _tooltip = new(), _default = new(), _limit = new() { Width = 50 };
    private readonly CheckBox _readOnly = new() { Content = "Read Only" }, _required = new() { Content = "Required" };
    private readonly CheckBox _multiline = new() { Content = "Multi-line" }, _limitOn = new() { Content = "Limit of" };
    private readonly CheckBox _comb = new() { Content = "Comb of characters (spreads the characters evenly — needs a limit)" };
    private readonly CheckBox _isDate = new() { Content = "Date field (shows a calendar when filling)" };
    private readonly CheckBox _editable = new() { Content = "Allow user to enter custom text" };
    private readonly ComboBox _border = ColorCombo(), _fill = ColorCombo(), _text = ColorCombo();
    private readonly ComboBox _fontSize = new() { IsEditable = true, Width = 90 };
    private readonly ComboBox _align = new() { Width = 120 };
    private readonly ComboBox _dateFormat = new() { IsEditable = true, Width = 160 };
    private readonly ListBox _items = new() { Height = 110, Width = 220 };
    private readonly ComboBox _formatKind = new() { Width = 160 };
    private readonly ComboBox _decimals = new() { Width = 70 };
    private readonly TextBox _currency = new() { Width = 60 };
    private readonly RadioButton _noCalc = new() { Content = "Value is not calculated", GroupName = "calc" };
    private readonly RadioButton _calc = new() { Content = "Value is the", GroupName = "calc", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _calcOp = new() { Width = 130, Margin = new Thickness(6, 0, 6, 0) };
    private readonly StackPanel _calcList = new();
    private readonly TextBox _newItem = new() { Width = 160 };

    public FieldPropertiesDialog(MainViewModel vm, FormFieldInfo field)
    {
        _vm = vm;
        _field = field;
        MainViewModel.CopyFieldProperties(field, Result);
        Result.Name = field.Name;
        Result.FieldType = field.FieldType;
        Result.WidgetIndex = field.WidgetIndex;

        Title = $"{TypeLabel(field)} Properties";
        Width = 500; Height = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        Foreground = Brushes.Black;
        FontSize = 12;

        var tabs = new TabControl { Margin = new Thickness(10, 10, 10, 0) };
        tabs.Items.Add(new TabItem { Header = "General", Content = BuildGeneral() });
        tabs.Items.Add(new TabItem { Header = "Appearance", Content = BuildAppearance() });
        tabs.Items.Add(new TabItem { Header = "Options", Content = BuildOptions() });
        if (IsText)
        {
            tabs.Items.Add(new TabItem { Header = "Format", Content = BuildFormat() });
            tabs.Items.Add(new TabItem { Header = "Calculate", Content = BuildCalculate() });
        }

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(0, 3, 0, 3) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true, Padding = new Thickness(0, 3, 0, 3) };
        ok.Click += (_, _) => { if (Commit()) DialogResult = true; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabs);
        Content = root;
    }

    private static string TypeLabel(FormFieldInfo f) => f.FieldType switch
    {
        FieldType.Checkbox => "Check Box",
        FieldType.RadioButton => "Radio Button",
        FieldType.ComboBox => "Dropdown",
        FieldType.ListBox => "List Box",
        FieldType.Signature => "Digital Signature",
        _ => f.IsDateField ? "Date Field" : "Text Field",
    };

    private bool IsText => _field.FieldType == FieldType.Text;
    private bool IsChoice => _field.FieldType is FieldType.ComboBox or FieldType.ListBox;

    // ── Tabs ──────────────────────────────────────────────────────────────────

    private UIElement BuildGeneral()
    {
        var p = Panel();
        _name.Text = _field.DisplayName;
        _tooltip.Text = _field.Tooltip ?? string.Empty;
        _readOnly.IsChecked = _field.IsReadOnly;
        _required.IsChecked = _field.IsRequired;
        p.Children.Add(Row(_field.FieldType == FieldType.RadioButton ? "Group name" : "Name", _name));
        p.Children.Add(Row("Tooltip", _tooltip));
        p.Children.Add(Header("Common Properties"));
        p.Children.Add(_readOnly);
        p.Children.Add(Spaced(_required));
        return p;
    }

    private UIElement BuildAppearance()
    {
        var p = Panel();
        Select(_border, _field.BorderColor);
        Select(_fill, _field.FillColor);
        Select(_text, _field.TextColor);
        _fontSize.Items.Add("Auto");
        foreach (var n in new[] { 6, 7, 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36 }) _fontSize.Items.Add(n.ToString());
        _fontSize.Text = _field.FontSize > 0 ? _field.FontSize.ToString("0.#") : "Auto";
        p.Children.Add(Header("Borders and Colours"));
        p.Children.Add(Row("Border colour", _border));
        p.Children.Add(Row("Fill colour", _fill));
        p.Children.Add(Header("Text"));
        bool textLike = IsText || IsChoice;
        _fontSize.IsEnabled = _text.IsEnabled = textLike;
        p.Children.Add(Row("Font size", _fontSize));
        p.Children.Add(Row("Text colour", _text));
        return p;
    }

    private UIElement BuildOptions()
    {
        var p = Panel();
        if (IsText)
        {
            foreach (var a in Enum.GetValues<FieldAlignment>()) _align.Items.Add(a);
            _align.SelectedItem = _field.Alignment;
            _default.Text = _field.DefaultValue ?? string.Empty;
            _multiline.IsChecked = _field.IsMultiline;
            _limitOn.IsChecked = _field.MaxLength > 0;
            _limit.Text = _field.MaxLength > 0 ? _field.MaxLength.ToString() : "10";
            _comb.IsChecked = _field.IsComb;
            _isDate.IsChecked = _field.IsDateField;
            foreach (var f in DateFormats) _dateFormat.Items.Add(f);
            _dateFormat.Text = _field.DateFormat ?? DateFormats[0];

            p.Children.Add(Row("Alignment", _align));
            p.Children.Add(Row("Default value", _default));
            p.Children.Add(Spaced(_multiline));
            var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            limitRow.Children.Add(_limitOn);
            limitRow.Children.Add(_limit);
            limitRow.Children.Add(new TextBlock { Text = " characters", VerticalAlignment = VerticalAlignment.Center });
            _limitOn.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(limitRow);
            p.Children.Add(Spaced(_comb));
        }
        else if (IsChoice)
        {
            foreach (var o in _field.Options) _items.Items.Add(o);
            var add = new Button { Content = "Add", Width = 60, Margin = new Thickness(6, 0, 0, 0) };
            add.Click += (_, _) =>
            {
                var t = _newItem.Text.Trim();
                if (t.Length == 0) return;
                _items.Items.Add(t);
                _newItem.Clear();
                _newItem.Focus();
            };
            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            addRow.Children.Add(_newItem);
            addRow.Children.Add(add);

            Button Mini(string text, Action act)
            {
                var b = new Button { Content = text, Width = 70, Margin = new Thickness(0, 0, 0, 4) };
                b.Click += (_, _) => act();
                return b;
            }
            var side = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            side.Children.Add(Mini("Delete", () => { if (_items.SelectedIndex >= 0) _items.Items.RemoveAt(_items.SelectedIndex); }));
            side.Children.Add(Mini("Up", () => MoveItem(-1)));
            side.Children.Add(Mini("Down", () => MoveItem(+1)));
            var listRow = new StackPanel { Orientation = Orientation.Horizontal };
            listRow.Children.Add(_items);
            listRow.Children.Add(side);

            _default.Text = _field.DefaultValue ?? string.Empty;
            _editable.IsChecked = _field.IsEditable;

            p.Children.Add(new TextBlock { Text = "Item", Margin = new Thickness(0, 0, 0, 2) });
            p.Children.Add(addRow);
            p.Children.Add(new TextBlock { Text = "Item list", Margin = new Thickness(0, 0, 0, 2) });
            p.Children.Add(listRow);
            p.Children.Add(Row("Default value", _default));
            if (_field.FieldType == FieldType.ComboBox) p.Children.Add(Spaced(_editable));
        }
        else
        {
            p.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = _field.FieldType switch
                {
                    FieldType.Checkbox => $"Export value when checked: {_field.ExportValue}",
                    FieldType.RadioButton => $"Radio button choice (export value): {_field.ExportValue}\nButtons with the same group name are mutually exclusive.",
                    FieldType.Signature => "The signer adds their signature here with Fill & Sign → Sign.",
                    _ => "No options for this field type.",
                },
            });
        }
        return p;
    }

    // Acrobat's Format tab: None / Number / Currency / Percent / Date / Zip / Phone / SSN.
    private UIElement BuildFormat()
    {
        var p = Panel();
        foreach (var k in new[] { "None", "Number", "Currency", "Percent", "Date", "Zip", "Zip+4", "Phone", "SSN" }) _formatKind.Items.Add(k);
        foreach (var n in new[] { "0", "1", "2", "3", "4" }) _decimals.Items.Add(n);
        _formatKind.SelectedItem = _field.IsDateField ? "Date" : _field.NumberFormat ?? "None";
        _decimals.SelectedItem = Math.Clamp(_field.Decimals, 0, 4).ToString();
        _currency.Text = _field.CurrencySymbol;

        var decRow = Row("Decimal places", _decimals);
        var curRow = Row("Currency symbol", _currency);
        var dateRow = Row("Date format", _dateFormat);
        var example = new TextBlock { Margin = new Thickness(0, 10, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)) };
        void Update()
        {
            string k = _formatKind.SelectedItem as string ?? "None";
            decRow.Visibility = k is "Number" or "Currency" or "Percent" ? Visibility.Visible : Visibility.Collapsed;
            curRow.Visibility = k == "Currency" ? Visibility.Visible : Visibility.Collapsed;
            dateRow.Visibility = k == "Date" ? Visibility.Visible : Visibility.Collapsed;
            int dec = int.TryParse(_decimals.SelectedItem as string, out var d) ? d : 2;
            example.Text = k switch
            {
                "Number" or "Currency" or "Percent" => "Example: " + PdfEdit.Services.FieldFormatting.ToDisplay(k == "Percent" ? "0.1234" : "1234.5", k, dec, _currency.Text),
                "Zip" => "Example: 12345", "Zip+4" => "Example: 12345-6789", "Phone" => "Example: (555) 123-4567", "SSN" => "Example: 123-45-6789",
                "Date" => "Shows a calendar when filling.", _ => "The text is kept exactly as typed.",
            };
        }
        _formatKind.SelectionChanged += (_, _) => Update();
        _decimals.SelectionChanged += (_, _) => Update();
        _currency.TextChanged += (_, _) => Update();

        p.Children.Add(Row("Format category", _formatKind));
        p.Children.Add(decRow);
        p.Children.Add(curRow);
        p.Children.Add(dateRow);
        p.Children.Add(example);
        p.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Text = "Formatted fields store the plain number and show it formatted. Acrobat and other readers use the same rules.",
        });
        Update();
        return p;
    }

    // Acrobat's Calculate tab (simple calculations).
    private UIElement BuildCalculate()
    {
        var p = Panel();
        foreach (var (op, label) in PdfEdit.Services.FieldFormatting.CalcOps) _calcOp.Items.Add(new ComboBoxItem { Content = label, Tag = op });
        _calcOp.SelectedIndex = Math.Max(0, Array.FindIndex(PdfEdit.Services.FieldFormatting.CalcOps, c => c.Op == _field.CalcOp));
        bool calculates = !string.IsNullOrEmpty(_field.CalcOp);
        _noCalc.IsChecked = !calculates;
        _calc.IsChecked = calculates;

        var others = _vm.AllFields.Where(f => f.FieldType == FieldType.Text && f.Name != _field.Name)
                                  .Select(f => f.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var n in others)
            _calcList.Children.Add(new CheckBox { Content = n, Tag = n, IsChecked = _field.CalcFields.Contains(n), Margin = new Thickness(0, 2, 0, 2) });

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        row.Children.Add(_calc);
        row.Children.Add(_calcOp);
        row.Children.Add(new TextBlock { Text = "of these fields:", VerticalAlignment = VerticalAlignment.Center });
        var scroll = new ScrollViewer { Height = 200, Content = _calcList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                        BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(6) };
        _calcList.IsEnabled = _calcOp.IsEnabled = calculates;
        _calc.Checked += (_, _) => _calcList.IsEnabled = _calcOp.IsEnabled = true;
        _noCalc.Checked += (_, _) => _calcList.IsEnabled = _calcOp.IsEnabled = false;

        p.Children.Add(_noCalc);
        p.Children.Add(row);
        p.Children.Add(others.Count == 0 ? new TextBlock { Text = "There are no other text fields to calculate from.", Foreground = Brushes.Gray } : scroll);
        return p;
    }

    private void MoveItem(int delta)
    {
        int i = _items.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _items.Items.Count) return;
        var item = _items.Items[i];
        _items.Items.RemoveAt(i);
        _items.Items.Insert(j, item);
        _items.SelectedIndex = j;
    }

    // ── Commit ────────────────────────────────────────────────────────────────

    private bool Commit()
    {
        string name = _name.Text.Trim();
        if (name != _field.DisplayName)
        {
            var error = _vm.ValidateFieldName(_field, name);
            if (error != null) { MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
            Result.PendingName = name == _field.Name ? null : name;
        }
        Result.Tooltip = _tooltip.Text;
        Result.IsReadOnly = _readOnly.IsChecked == true;
        Result.IsRequired = _required.IsChecked == true;

        Result.BorderColor = (_border.SelectedItem as ColorChoice)?.Hex;
        Result.FillColor = (_fill.SelectedItem as ColorChoice)?.Hex;
        Result.TextColor = (_text.SelectedItem as ColorChoice)?.Hex ?? "#000000";
        Result.FontSize = double.TryParse(_fontSize.Text, out var fs) ? Math.Clamp(fs, 0, 144) : 0;

        if (IsText)
        {
            Result.Alignment = _align.SelectedItem is FieldAlignment a ? a : FieldAlignment.Left;
            Result.DefaultValue = _default.Text;
            Result.IsMultiline = _multiline.IsChecked == true;
            Result.MaxLength = _limitOn.IsChecked == true && int.TryParse(_limit.Text, out var n) && n > 0 ? n : 0;
            Result.IsComb = _comb.IsChecked == true && Result.MaxLength > 0;
            string kind = _formatKind.SelectedItem as string ?? "None";
            Result.DateFormat = kind == "Date" ? (string.IsNullOrWhiteSpace(_dateFormat.Text) ? DateFormats[0] : _dateFormat.Text.Trim()) : null;
            Result.NumberFormat = kind is "None" or "Date" ? null : kind;
            Result.Decimals = int.TryParse(_decimals.SelectedItem as string, out var dec) ? dec : 2;
            Result.CurrencySymbol = string.IsNullOrWhiteSpace(_currency.Text) ? "£" : _currency.Text.Trim();
            Result.CalcFields = _calcList.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
            Result.CalcOp = _calc.IsChecked == true && Result.CalcFields.Count > 0 ? (_calcOp.SelectedItem as ComboBoxItem)?.Tag as string : null;
            if (Result.CalcOp == null) Result.CalcFields.Clear();
        }
        else if (IsChoice)
        {
            Result.Options = _items.Items.Cast<object>().Select(o => o.ToString() ?? string.Empty).Where(o => o.Length > 0).ToList();
            Result.DefaultValue = _default.Text;
            Result.IsEditable = _editable.IsChecked == true;
        }
        return true;
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static ComboBox ColorCombo()
    {
        var cb = new ComboBox { Width = 170 };
        var template = new DataTemplate();
        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var sw = new FrameworkElementFactory(typeof(Border));
        sw.SetValue(WidthProperty, 16.0);
        sw.SetValue(HeightProperty, 12.0);
        sw.SetValue(Border.BorderBrushProperty, Brushes.Gray);
        sw.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        sw.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        sw.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(ColorChoice.Swatch)));
        var tx = new FrameworkElementFactory(typeof(TextBlock));
        tx.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ColorChoice.Name)));
        sp.AppendChild(sw);
        sp.AppendChild(tx);
        template.VisualTree = sp;
        cb.ItemTemplate = template;
        foreach (var c in Palette) cb.Items.Add(c);
        return cb;
    }

    private static void Select(ComboBox cb, string? hex)
    {
        var match = Palette.FirstOrDefault(c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase));
        if (match == null && hex != null)
        {
            match = new ColorChoice($"Custom ({hex})", hex);
            cb.Items.Add(match);
        }
        cb.SelectedItem = match ?? Palette[0];
    }

    private static StackPanel Panel() => new() { Margin = new Thickness(14, 12, 14, 12) };

    private static TextBlock Header(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6),
        Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
    };

    private static UIElement Spaced(FrameworkElement e) { e.Margin = new Thickness(0, 0, 0, 6); return e; }

    private static UIElement Row(string label, FrameworkElement editor)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        editor.HorizontalAlignment = editor is TextBox ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        Grid.SetColumn(editor, 1);
        g.Children.Add(l);
        g.Children.Add(editor);
        return g;
    }
}
