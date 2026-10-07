using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Controls;

/// <summary>
/// "Form Builder" behaviour for the live view:
/// <list type="bullet">
/// <item>A field toolbar across the top of the page (Select, Text, Check Box, Radio, List Box,
/// Dropdown, Signature, Date · Preview · Close) and, while filling, the usual
/// "This document contains interactive form fields" bar.</item>
/// <item>Click to drop a default-size field or drag to size it; fields are named automatically
/// (Text1, Check Box1, Group1/Choice1, Dropdown1, List Box1, Signature1, Date1) and a small
/// popup offers the name, Required and "All Properties" — no dialog in the way.</item>
/// <item>Double-click a field for the tabbed Field Properties dialog.</item>
/// </list>
/// </summary>
public partial class PdfViewerControl
{
    // Radio buttons placed one after another (via "Add another button") join the same group.
    private string? _lastRadioGroup;
    private bool _continueRadioGroup;

    // The field that was just created — its name popup opens once the reloaded page shows it.
    private (string Name, int WidgetIndex)? _namePopupKey;
    private Popup? _namePopup;

    private static (double W, double H) DefaultFieldSize(ActiveTool tool) => tool switch
    {
        ActiveTool.AddCheckbox or ActiveTool.AddRadioButton => (14, 14),
        ActiveTool.AddListBox => (140, 60),
        ActiveTool.AddSignatureField => (180, 40),
        ActiveTool.AddDateField => (100, 22),
        ActiveTool.AddComboBox => (140, 22),
        _ => (160, 22),
    };

    /// <summary>Creates the field in the PDF (the document then reloads) and selects it.</summary>
    private void CreateFormField(ActiveTool tool, Rect canvasRect)
    {
        if (_vm?.Document == null || _vm.CurrentFilePath == null) return;
        int pageNum = _vm.CurrentPageIndex + 1;
        if (pageNum < 1 || pageNum > _vm.Document.PageSizes.Count) return;
        double pageH = _vm.Document.PageSizes[pageNum - 1].Height;

        // Canvas → PDF points; a click (tiny rect) gets the usual default size for the type.
        double left = canvasRect.X / Scale, top = canvasRect.Y / Scale;
        double width = canvasRect.Width / Scale, height = canvasRect.Height / Scale;
        if (canvasRect.Width < 8 || canvasRect.Height < 8)
            (width, height) = DefaultFieldSize(tool);
        if (tool is ActiveTool.AddCheckbox or ActiveTool.AddRadioButton)
            width = height = Math.Min(width, height);
        double bottom = pageH - top - height;

        string name;
        int widgetIndex = 0;
        string? radioChoice = null;
        if (tool == ActiveTool.AddRadioButton)
        {
            name = _continueRadioGroup && _lastRadioGroup != null && _vm.AllFields.Any(f => f.Name == _lastRadioGroup)
                ? _lastRadioGroup
                : _vm.NextFieldName("Group");
            widgetIndex = _vm.AllFields.Count(f => f.Name == name);
            radioChoice = $"Choice{widgetIndex + 1}";
            _lastRadioGroup = name;
        }
        else
        {
            name = _vm.NextFieldName(tool switch
            {
                ActiveTool.AddCheckbox => "Check Box",
                ActiveTool.AddComboBox => "Dropdown",
                ActiveTool.AddListBox => "List Box",
                ActiveTool.AddSignatureField => "Signature",
                ActiveTool.AddDateField => "Date",
                _ => "Text",
            });
            _lastRadioGroup = null;
        }
        _continueRadioGroup = false;

        string src = _vm.CurrentFilePath;
        string tmp = src + ".tmp";
        try
        {
            var svc = new PdfFormService();
            // Adding a field reloads the document, so first bake in fields moved but not saved yet.
            if (_vm.ModifiedFieldBounds.Count > 0)
            {
                svc.ApplyFieldBounds(src, tmp, _vm.ModifiedFieldBounds);
                File.Copy(tmp, src, overwrite: true);
            }
            float l = (float)left, b = (float)bottom, w = (float)width, h = (float)height;
            switch (tool)
            {
                case ActiveTool.AddCheckbox:       svc.AddCheckboxField(src, tmp, pageNum, l, b, w, name); break;
                case ActiveTool.AddRadioButton:    svc.AddRadioButtonField(src, tmp, pageNum, l, b, w, name, radioChoice!); break;
                case ActiveTool.AddComboBox:       svc.AddComboBoxField(src, tmp, pageNum, l, b, w, h, name, new[] { "Item 1", "Item 2", "Item 3" }); break;
                case ActiveTool.AddListBox:        svc.AddListBoxField(src, tmp, pageNum, l, b, w, h, name, new[] { "Item 1", "Item 2", "Item 3" }); break;
                case ActiveTool.AddSignatureField: svc.AddSignatureField(src, tmp, pageNum, l, b, w, h, name); break;
                case ActiveTool.AddDateField:      svc.AddDateField(src, tmp, pageNum, l, b, w, h, name); break;
                default:                           svc.AddTextFormField(src, tmp, pageNum, l, b, w, h, name); break;
            }
            File.Copy(tmp, src, overwrite: true);

            _vm.StatusText = tool == ActiveTool.AddRadioButton
                ? $"Radio button '{radioChoice}' added to group '{name}'. Use \"Add another button\" to add more choices."
                : $"Field '{name}' added. Drag to move, double-click for properties.";
            // Like PDF readers: back to the selection tool with the new field selected and its name popup open.
            _layoutSelectedKey = (name, widgetIndex);
            _namePopupKey = (name, widgetIndex);
            _vm.ActiveTool = ActiveTool.EditFields;
            _ = _vm.ReloadCurrentFileAsync();
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError("Add form field failed.", ex);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>Called when a layout box is (re)built: opens the name popup for a just-created field.</summary>
    private void MaybeShowNamePopup(Border box, FormFieldInfo field)
    {
        if (_namePopupKey is not { } key || key.Name != field.Name || key.WidgetIndex != field.WidgetIndex) return;
        _namePopupKey = null;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => ShowFieldNamePopup(box, field));
    }

    /// <summary>the usual popup under a new field: Field Name, Required, All Properties.</summary>
    private void ShowFieldNamePopup(Border box, FormFieldInfo field)
    {
        if (_vm == null) return;
        if (_namePopup != null) _namePopup.IsOpen = false;

        var fg = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        var nameBox = new TextBox { Text = field.DisplayName, Width = 170, Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(3, 2, 3, 2) };
        var required = new CheckBox { Content = "Required field", IsChecked = field.IsRequired, Foreground = fg, Margin = new Thickness(0, 0, 0, 6) };
        var allProps = new TextBlock
        {
            Text = "All Properties", Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x73, 0xE6)),
            Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline,
        };

        void Commit()
        {
            if (_vm.SelectedField != field) _vm.SelectedField = field;
            if (nameBox.Text.Trim() != field.DisplayName) _vm.SelectedFieldName = nameBox.Text.Trim();
            if (required.IsChecked == true != field.IsRequired) _vm.SelectedFieldRequired = required.IsChecked == true;
        }

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = field.FieldType == FieldType.RadioButton ? "Group Name:" : "Field Name:", Foreground = fg, FontSize = 11 });
        stack.Children.Add(nameBox);
        stack.Children.Add(required);
        stack.Children.Add(allProps);
        if (field.FieldType == FieldType.RadioButton)
        {
            var another = new TextBlock
            {
                Text = "Add another button", Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x73, 0xE6)),
                Cursor = Cursors.Hand, TextDecorations = TextDecorations.Underline, Margin = new Thickness(0, 4, 0, 0),
            };
            another.MouseLeftButtonDown += (_, _) =>
            {
                Commit();
                _namePopup!.IsOpen = false;
                _lastRadioGroup = field.Name;
                _continueRadioGroup = true;
                _vm.ActiveTool = ActiveTool.AddRadioButton;
                _vm.StatusText = $"Click on the page to add another choice to '{field.DisplayName}'.";
            };
            stack.Children.Add(another);
        }

        _namePopup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3 },
                Child = stack,
            },
        };
        allProps.MouseLeftButtonDown += (_, _) =>
        {
            Commit();
            _namePopup.IsOpen = false;
            _vm.OpenFieldProperties(field);
        };
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); _namePopup.IsOpen = false; e.Handled = true; }
            if (e.Key == Key.Escape) { _namePopup.IsOpen = false; e.Handled = true; }
        };
        _namePopup.Closed += (_, _) => Commit();
        _namePopup.IsOpen = true;
        nameBox.Focus();
        nameBox.SelectAll();
    }

    // ── Form Builder toolbar / fill-mode notice bar ───────────────────────────

    private void InitFormBars()
    {
        foreach (var btn in PrepareFormToolbar.Children.OfType<ToggleButton>())
        {
            btn.Click += (_, _) =>
            {
                if (_vm == null || btn.Tag is not string t || !Enum.TryParse<ActiveTool>(t, out var tool)) return;
                _vm.IsDesignMode = false;
                _vm.ActiveTool = tool;
                SyncFormBars();
            };
        }
        PreparePreviewBtn.Click += (_, _) => { if (_vm != null) _vm.ActiveTool = ActiveTool.Select; };
        PrepareCloseBtn.Click += (_, _) => { if (_vm != null) _vm.ActiveTool = ActiveTool.Hand; };
        PrepareFieldsBtn.Click += (_, _) => _vm?.ShowFieldsPanel();
        NoticePrepareBtn.Click += (_, _) => { if (_vm != null) _vm.ActiveTool = ActiveTool.EditFields; };
        NoticeCloseBtn.Click += (_, _) => { _noticeDismissedFor = _vm?.CurrentFilePath; SyncFormBars(); };
    }

    private string? _noticeDismissedFor;

    /// <summary>Shows the Form Builder toolbar while editing fields, or the fill notice while filling.</summary>
    private void SyncFormBars()
    {
        if (_vm?.Document == null)
        {
            PrepareFormBar.Visibility = FormNoticeBar.Visibility = Visibility.Collapsed;
            return;
        }
        bool prepare = IsFieldLayoutMode;
        PrepareFormBar.Visibility = prepare ? Visibility.Visible : Visibility.Collapsed;
        foreach (var btn in PrepareFormToolbar.Children.OfType<ToggleButton>())
            btn.IsChecked = btn.Tag as string == _vm.ActiveTool.ToString();

        bool hasFields = _vm.AllFields.Count > 0;
        FormNoticeBar.Visibility = !prepare && hasFields && _noticeDismissedFor != _vm.CurrentFilePath
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Fields panel → page ───────────────────────────────────────────────────

    private void OnFieldSelectionRequested(FormFieldInfo field)
    {
        _layoutSelectedKey = (field.Name, field.WidgetIndex);
        if (_vm?.ActiveTool != ActiveTool.EditFields && _vm != null) _vm.ActiveTool = ActiveTool.EditFields;
        else RebuildFieldOverlay();
    }

    // ── Date fields (shows a calendar) ────────────────────────────────

    private void AddDatePicker(FormFieldInfo field, TextBox tb, double x, double y, double w, double h)
    {
        double size = Math.Max(14, Math.Min(h, 22));
        var btn = new Button
        {
            Width = size, Height = size, Padding = new Thickness(0), Focusable = false,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            ToolTip = "Pick a date",
            Content = new TextBlock { Text = "\uE787", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size * 0.6, Foreground = Brushes.DimGray },
        };
        System.Windows.Automation.AutomationProperties.SetName(btn, $"Pick a date for {field.DisplayName}");

        var calendar = new Calendar { DisplayDate = DateTime.Today };
        if (TryParseAfDate(tb.Text, field.DateFormat!, out var current)) { calendar.SelectedDate = current; calendar.DisplayDate = current; }
        var popup = new Popup { PlacementTarget = tb, Placement = PlacementMode.Bottom, StaysOpen = false, Child = calendar };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is { } d)
            {
                tb.Text = FormatAfDate(d, field.DateFormat!);
                popup.IsOpen = false;
            }
        };
        btn.Click += (_, _) => popup.IsOpen = !popup.IsOpen;

        Canvas.SetLeft(btn, x + w - size - 1);
        Canvas.SetTop(btn, y + (h - size) / 2);
        Panel.SetZIndex(btn, 50);
        FieldOverlayCanvas.Children.Add(btn);
        tb.Padding = new Thickness(tb.Padding.Left, 0, size + 2, 0);
    }

    private static readonly System.Text.RegularExpressions.Regex AfDateToken =
        new("yyyy|yy|mmmm|mmm|mm|m|dddd|ddd|dd|d|HH|H|hh|h|MM|ss|tt");

    /// <summary>Formats a date with an AFDate pattern: m = month, M = minutes.</summary>
    public static string FormatAfDate(DateTime d, string format) =>
        AfDateToken.Replace(format, t => t.Value switch
        {
            "yyyy" => d.ToString("yyyy"), "yy" => d.ToString("yy"),
            "mmmm" => d.ToString("MMMM"), "mmm" => d.ToString("MMM"), "mm" => d.ToString("MM"), "m" => d.Month.ToString(),
            "dddd" => d.ToString("dddd"), "ddd" => d.ToString("ddd"), "dd" => d.ToString("dd"), "d" => d.Day.ToString(),
            "HH" => d.ToString("HH"), "H" => d.Hour.ToString(), "hh" => d.ToString("hh"), "h" => (d.Hour % 12 == 0 ? 12 : d.Hour % 12).ToString(),
            "MM" => d.ToString("mm"), "ss" => d.ToString("ss"), "tt" => d.ToString("tt"),
            _ => t.Value,
        });

    private static bool TryParseAfDate(string text, string format, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string net = AfDateToken.Replace(format, t => t.Value switch
        {
            "mmmm" => "MMMM", "mmm" => "MMM", "mm" => "MM", "m" => "M", "MM" => "mm", _ => t.Value,
        });
        return DateTime.TryParseExact(text.Trim(), net, System.Globalization.CultureInfo.CurrentCulture,
                                      System.Globalization.DateTimeStyles.None, out date)
            || DateTime.TryParse(text, out date);
    }
}
