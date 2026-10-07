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
    // "Form Builder": select, move and resize existing form fields in the live view.
    EditFields,
    // Form Builder field types added alongside the originals above
    AddListBox,
    AddSignatureField,
    AddDateField,
    // Standard drawing tools: straight line, revision cloud, polygon and polyline (click the points,
    // double-click or Enter to finish)
    DrawLine, DrawCloud, DrawPolygon, DrawPolyline,
    // "Measure" tools
    MeasureDistance, MeasurePerimeter, MeasureArea,
    // Standard comment tools: caret to insert text, strike + caret to replace text
    InsertText, ReplaceText,
    // Drag the box for a certificate (digital) signature
    DigitalSignature,
    // Drag over text (or any area) to copy it, highlight it or ask the AI about it
    SelectText,
    // Wavy underline under text (squiggly)
    Squiggly,
    // Select the pictures already in the PDF to move, resize, replace, save or delete them
    EditImages
}
