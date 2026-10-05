using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using PdfEdit.Models;
using PdfEdit.Services;
using PdfEdit.ViewModels;

namespace PdfEdit;

/// <summary>
/// Settings → Accessibility in the main window: status, page and tool announcements for screen
/// readers and PdfEdit's own voice, and a ribbon tall enough for the chosen icon size.
/// </summary>
public partial class MainWindow
{
    private double _ribbonContentHeight = double.NaN;

    private void InitAccessibility()
    {
        if (VM is { } vm) vm.PropertyChanged += OnVmPropertyChangedAnnounce;
        InterfaceStyleService.IconSizeChanged += FitRibbonToIcons;
        FitRibbonToIcons(InterfaceStyleService.IconFactor(AppSettings.Current.IconSize));
    }

    private void OnVmPropertyChangedAnnounce(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;
        var s = AppSettings.Current;
        string? text = e.PropertyName switch
        {
            nameof(MainViewModel.StatusText) when s.AnnounceStatus => vm.StatusText,
            nameof(MainViewModel.CurrentPageIndex) when s.AnnouncePageChanges && vm.HasDocument => $"Page {vm.CurrentPageIndex + 1} of {vm.PageCount}",
            nameof(MainViewModel.ActiveTool) when s.AnnounceToolChanges => ToolName(vm.ActiveTool) + " tool",
            _ => null,
        };
        Announce(text);
    }

    /// <summary>Tells the screen reader and/or speaks, as set in Settings → Accessibility.</summary>
    public void Announce(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var s = AppSettings.Current;
        if (s.AnnounceToScreenReader) ScreenReader.Announce(StatusMessageText, text);
        if (s.NarrateAnnouncements) NarrationService.Speak(text);
    }

    /// <summary>"AddText" → "Add text", "DrawFreehand" → "Draw freehand".</summary>
    private static string ToolName(ActiveTool tool)
    {
        string words = Regex.Replace(tool.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
        return words.Length > 1 ? words[0] + words[1..].ToLowerInvariant() : words;
    }

    /// <summary>Bigger icons need a taller ribbon, or the button labels get cut off.</summary>
    private void FitRibbonToIcons(double factor)
    {
        var dp = typeof(Fluent.Ribbon).GetField("ContentHeightProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as DependencyProperty;
        if (dp == null || dp.PropertyType != typeof(double)) return;
        if (double.IsNaN(_ribbonContentHeight)) _ribbonContentHeight = (double)MainRibbon.GetValue(dp);
        MainRibbon.SetValue(dp, _ribbonContentHeight + Math.Max(0, 32 * factor - 32));
    }
}
