using System.Windows.Input;
using PdfEdit.Models;

namespace PdfEdit.ViewModels;

/// <summary>
/// The shape or drawing selected in Live View (click it with Select), edited from the Properties
/// panel like Acrobat's properties bar: line colour, fill, line width, opacity. Text and ✓ ✕ marks
/// use the "Selected text" section (their colour is the text colour). Every change is undoable.
/// </summary>
public partial class MainViewModel
{
    private object? _selectedGraphic;   // ShapeAnnotation, or an ink FreeTextAnnotation (drawing)

    public object? SelectedGraphic
    {
        get => _selectedGraphic;
        set
        {
            if (ReferenceEquals(_selectedGraphic, value)) return;
            _selectedGraphic = value;
            if (value != null && _selectedAnnotation != null) SelectedAnnotation = null;
            RaiseGraphicProperties();
        }
    }

    private void RaiseGraphicProperties()
    {
        foreach (var n in new[] { nameof(SelectedGraphic), nameof(HasSelectedGraphic), nameof(SelectedGraphicLabel),
                     nameof(GraphicStrokeColor), nameof(GraphicFillColor), nameof(GraphicHasFill),
                     nameof(GraphicLineWidth), nameof(GraphicOpacityPercent), nameof(GraphicHasOpacity) })
            OnPropertyChanged(n);
    }

    public bool HasSelectedGraphic => _selectedGraphic != null;

    /// <summary>Asks the window to bring the Properties panel to the front.</summary>
    public event Action? PropertiesPanelRequested;
    public void ShowPropertiesPanel() => PropertiesPanelRequested?.Invoke();

    public string SelectedGraphicLabel => _selectedGraphic switch
    {
        ShapeAnnotation s => s.Kind.ToString(),
        FreeTextAnnotation => "Drawing",
        _ => string.Empty,
    };

    private InkLike? Ink => _selectedGraphic is FreeTextAnnotation a && Controls.PdfViewerControl.ParseInk(a.Text) is { } d
        ? new InkLike(a, d) : null;
    private readonly record struct InkLike(FreeTextAnnotation Ann, Controls.PdfViewerControl.InkData Data);

    public string GraphicStrokeColor
    {
        get => _selectedGraphic switch
        {
            ShapeAnnotation s => s.StrokeColor,
            _ => Ink?.Data.Color ?? "#000000",
        };
        set => EditGraphic("colour", () =>
        {
            string hex = NormaliseHex(value);
            if (_selectedGraphic is ShapeAnnotation s) s.StrokeColor = hex;
            else if (Ink is { } ink)
            {
                ink.Ann.Text = Controls.PdfViewerControl.EncodeInk(hex, ink.Data.Width, ink.Data.Points);
                ink.Ann.FontColor = hex;
            }
        });
    }

    public bool GraphicHasFill => _selectedGraphic is ShapeAnnotation s && s.IsClosedShape;

    /// <summary>"" = no fill, "tint" = light tint of the line colour, or #RRGGBB.</summary>
    public string GraphicFillColor
    {
        get => _selectedGraphic is ShapeAnnotation s ? (s.FillColor ?? "tint") : "";
        set => EditGraphic("fill", () =>
        {
            if (_selectedGraphic is not ShapeAnnotation s) return;
            s.FillColor = value switch
            {
                null or "tint" => null,
                "" or "none" => "",
                _ => NormaliseHex(value),
            };
        });
    }

    public double GraphicLineWidth
    {
        get => _selectedGraphic switch
        {
            ShapeAnnotation s => s.LineWidth,
            _ => Ink?.Data.Width ?? 2,
        };
        set => EditGraphic("line width", () =>
        {
            double w = Math.Clamp(value, 0.25, 72);
            if (_selectedGraphic is ShapeAnnotation s) s.LineWidth = w;
            else if (Ink is { } ink) ink.Ann.Text = Controls.PdfViewerControl.EncodeInk(ink.Data.Color, w, ink.Data.Points);
        });
    }

    public bool GraphicHasOpacity => _selectedGraphic is ShapeAnnotation;

    public double GraphicOpacityPercent
    {
        get => _selectedGraphic is ShapeAnnotation s ? Math.Round(s.Opacity * 100) : 100;
        set => EditGraphic("opacity", () =>
        {
            if (_selectedGraphic is ShapeAnnotation s) s.Opacity = Math.Clamp(value, 5, 100) / 100.0;
        });
    }

    private static string NormaliseHex(string? hex)
    {
        hex = (hex ?? "").Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        catch { return "#000000"; }
    }

    /// <summary>Applies a property change as one undo step and redraws the page.</summary>
    private void EditGraphic(string what, Action change)
    {
        var target = _selectedGraphic;
        if (target == null) return;
        var before = Snapshot(target);
        change();
        var after = Snapshot(target);
        if (before.SequenceEqual(after)) return;
        PushUndo(
            () => { Restore(target, before); RaiseGraphicProperties(); PageChanged?.Invoke(); },
            () => { Restore(target, after); RaiseGraphicProperties(); PageChanged?.Invoke(); });
        if (target is ShapeAnnotation s) s.Comment.Modified = DateTime.Now;
        StatusText = $"{SelectedGraphicLabel}: {what} changed.";
        RaiseGraphicProperties();
        PageChanged?.Invoke();
    }

    private static object?[] Snapshot(object t) => t switch
    {
        ShapeAnnotation s => new object?[] { s.StrokeColor, s.FillColor, s.LineWidth, s.Opacity },
        FreeTextAnnotation a => new object?[] { a.Text, a.FontColor },
        _ => Array.Empty<object?>(),
    };

    private static void Restore(object t, object?[] v)
    {
        if (t is ShapeAnnotation s)
        {
            s.StrokeColor = (string)v[0]!; s.FillColor = (string?)v[1]; s.LineWidth = (double)v[2]!; s.Opacity = (double)v[3]!;
        }
        else if (t is FreeTextAnnotation a)
        {
            a.Text = (string)v[0]!; a.FontColor = (string)v[1]!;
        }
    }

    private ICommand? _setGraphicColorCommand, _setGraphicFillCommand, _deleteSelectedGraphicCommand, _setSelectionColorCommand;

    /// <summary>Line colour of the selected shape / drawing (parameter: "#RRGGBB").</summary>
    public ICommand SetGraphicColorCommand => _setGraphicColorCommand ??=
        new RelayCommand(p => { if (p is string hex) GraphicStrokeColor = hex; });

    /// <summary>Fill of the selected shape (parameter: "#RRGGBB", "none" or "tint").</summary>
    public ICommand SetGraphicFillCommand => _setGraphicFillCommand ??=
        new RelayCommand(p => { if (p is string v) GraphicFillColor = v; });

    /// <summary>Colour swatch for selected text or a ✓ ✕ mark (parameter: "#RRGGBB").</summary>
    public ICommand SetSelectionColorCommand => _setSelectionColorCommand ??=
        new RelayCommand(p => { if (p is string hex) CurrentFontColor = hex; });

    public ICommand DeleteSelectedGraphicCommand => _deleteSelectedGraphicCommand ??= new RelayCommand(() =>
    {
        switch (_selectedGraphic)
        {
            case ShapeAnnotation s: RemoveShapeAnnotation(s); break;
            case FreeTextAnnotation a: RemoveFreeTextAnnotation(a); break;
        }
        SelectedGraphic = null;
        PageChanged?.Invoke();
    });
}
