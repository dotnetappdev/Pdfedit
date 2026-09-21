using System.Windows;
using System.Windows.Controls;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

public partial class SettingsWindow : Window
{
    private readonly List<string> _fonts = new()
    {
        "Arial", "Times New Roman", "Courier New", "Georgia", "Verdana",
        "Tahoma", "Calibri", "Segoe UI", "Helvetica Neue", "Palatino Linotype"
    };

    public SettingsWindow()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var s = AppSettings.Current;

        // Theme
        RbDark.IsChecked = s.Theme == "Dark";
        RbLight.IsChecked = s.Theme == "Light";
        RbHighContrast.IsChecked = s.Theme == "HighContrast";

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

        // AI
        ApiKeyBox.Password = s.ClaudeApiKey;

        // Accessibility
        HighContrastFocusCb.IsChecked = s.HighContrastFocusIndicators;
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
            App.SwitchTheme(tag);
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

        s.ClaudeApiKey = ApiKeyBox.Password;
        s.HighContrastFocusIndicators = HighContrastFocusCb.IsChecked == true;

        s.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Restore original theme if user changed it without saving
        App.SwitchTheme(AppSettings.Current.Theme);
        DialogResult = false;
        Close();
    }
}
