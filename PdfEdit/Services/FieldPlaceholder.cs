using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Layout.Renderer;

namespace PdfEdit.Services;

public enum PlaceholderLook { Underline, Box, Round }

/// <summary>
/// A blank space in a laid-out document that becomes a fillable field: drawn as an underline or
/// a box, and when it's placed on the page its position is recorded so the field can be added
/// exactly there (see <see cref="DetectFieldsService.AddFields"/>).
/// </summary>
public static class FieldPlaceholder
{
    private static readonly ImageData Blank = ImageDataFactory.Create(1, 1, 1, 8, new byte[] { 255 }, null);
    private static readonly Color Line = new DeviceRgb(0x80, 0x80, 0x80);

    public static Image Create(NewField spec, float width, float height, PlaceholderLook look, List<NewField> sink)
    {
        var img = new Image(Blank).ScaleAbsolute(width, height);
        switch (look)
        {
            case PlaceholderLook.Underline: img.SetBorderBottom(new SolidBorder(Line, 0.75f)); break;
            case PlaceholderLook.Box: img.SetBorder(new SolidBorder(Line, 0.75f)); break;
            case PlaceholderLook.Round:
                img.SetBorder(new SolidBorder(Line, 0.75f));
                var radius = new BorderRadius(height / 2);
                img.SetProperty(Property.BORDER_TOP_LEFT_RADIUS, radius);
                img.SetProperty(Property.BORDER_TOP_RIGHT_RADIUS, radius);
                img.SetProperty(Property.BORDER_BOTTOM_LEFT_RADIUS, radius);
                img.SetProperty(Property.BORDER_BOTTOM_RIGHT_RADIUS, radius);
                break;
        }
        img.SetMarginLeft(1).SetMarginRight(1);
        img.SetNextRenderer(new Recorder(img, spec, sink));
        return img;
    }

    private sealed class Recorder : ImageRenderer
    {
        private readonly NewField _spec;
        private readonly List<NewField> _sink;

        public Recorder(Image image, NewField spec, List<NewField> sink) : base(image)
        {
            _spec = spec;
            _sink = sink;
        }

        public override void Draw(DrawContext drawContext)
        {
            base.Draw(drawContext);
            if (_sink.Contains(_spec)) return;
            var area = GetOccupiedArea();
            var r = area.GetBBox();
            _spec.Page = area.GetPageNumber();
            _spec.Left = r.GetX() + 1;
            _spec.Bottom = r.GetY();
            _spec.Width = Math.Max(4, r.GetWidth() - 2);
            _spec.Height = Math.Max(4, r.GetHeight());
            _sink.Add(_spec);
        }

        public override IRenderer GetNextRenderer() => new Recorder((Image)GetModelElement(), _spec, _sink);
    }
}
