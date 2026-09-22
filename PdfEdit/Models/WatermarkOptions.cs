namespace PdfEdit.Models;

public class WatermarkOptions
{
    public string Text       { get; set; } = "DRAFT";
    public float  FontSize   { get; set; } = 72;
    public float  Opacity    { get; set; } = 0.25f;
    public float  AngleDeg   { get; set; } = -45;
    public string Color      { get; set; } = "#FF808080"; // ARGB hex
    public bool   AllPages   { get; set; } = true;
}
