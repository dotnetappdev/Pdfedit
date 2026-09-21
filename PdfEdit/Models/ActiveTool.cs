namespace PdfEdit.Models;

public enum ActiveTool
{
    Hand,
    Select,
    TextFill,
    CheckboxToggle,
    Signature,
    Highlight,
    Zoom,
    Stamp,
    AddText,         // place free-text annotation anywhere on the page
    VerticalText     // place vertical free-text annotation (rotated 90°)
}
