namespace PdfEdit.Models;

public enum ActiveTool
{
    Hand,
    Select,
    TextFill,
    CheckboxToggle,
    Signature,
    Highlight,
    Underline,
    Strikethrough,
    Redact,
    Zoom,
    Stamp,
    AddText,
    VerticalText,
    DateStamp,
    Checkmark,
    XMark,
    Dot,
    Line,
    Circle,
    Link,
    DrawFreehand,
    Eraser,
    AddTextField,
    AddCheckbox,
    AddComboBox,
    AddRadioButton,
    StickyNote,
    DrawRectangle,
    DrawEllipse,
    DrawArrow,
    DrawCallout,
    // Acrobat "Prepare Form": select, move and resize existing form fields in the live view.
    EditFields,
    // Acrobat Prepare Form field types added alongside the originals above
    AddListBox,
    AddSignatureField,
    AddDateField,
    // Acrobat drawing tools: straight line, revision cloud, polygon and polyline (click the points,
    // double-click or Enter to finish)
    DrawLine, DrawCloud, DrawPolygon, DrawPolyline,
    // Acrobat Pro "Measure" tools
    MeasureDistance, MeasurePerimeter, MeasureArea,
    // Acrobat comment tools: caret to insert text, strike + caret to replace text
    InsertText, ReplaceText,
    // Drag the box for a certificate (digital) signature
    DigitalSignature
}
