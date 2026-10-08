# Preparing forms

[Back to README](../README.md)

## Adding fields

Choose **Prepare a form** in All tools, **Prepare Form** on the form bar, or press E. A toolbar appears above the page with:

Select | Text | Check Box | Radio Button | List Box | Dropdown | Signature | Date

Click the page to drop a field at its default size, or drag to size it yourself. New fields are named the way Acrobat names them (`Text1`, `Check Box1`, `Group1`...). A small popup under the field lets you rename it straight away, mark it required, or add another button to a radio group.

Fields can go on any PDF, including scans that have no form at all.

## Moving and resizing

With **Edit Fields** active, each field shows as a named box:

- Drag a field to move it, or drag one of its eight handles to resize it. Hold Shift on a corner to keep the proportions.
- Use the arrow keys to nudge by 1 pt (10 pt with Shift), and Ctrl+arrow to resize.
- Ctrl/Shift+click or drag a selection box to select several fields, and Ctrl+A to select every field on the page.
- Right-click to **Align**, **Distribute**, **Make Same Size** or **Center on Page**. The last field you clicked is the one the others line up with.

You can also right-click a field while filling the form and choose **Move / resize field**.

## Field properties

Double-click a field to open its properties:

| Tab | What's on it |
|-----|--------------|
| General | Name, tooltip, read only, required |
| Appearance | Border and fill colour, font size, text colour |
| Options | Alignment, default value, multi-line, character limit, comb, date format, list items, export value |

The **Fields** panel lists every field page by page. Click one to jump to it.

The quick properties (name, value, tooltip, flags, alignment, font size, position) are also in the **Properties** panel whenever a field is selected.

## Saving

Everything is undoable until you save. On save, the fields are written into the PDF. Date fields use Acrobat's own `AFDate` scripts, so they behave the same in Adobe Reader.
