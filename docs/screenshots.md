# Screenshots

[Back to README](../README.md)

The Windows pictures below are rendered mock-ups of the interface rather than captures of the running app, so small details may differ. The ribbon and form-filling images are drawn from the real ribbon layout and can be regenerated with `node docs/screenshots/src/render.mjs`.

## PdfEdit for Mac and Linux

These are captures of the real app. PdfEdit for Mac and Linux shows the same screens as the web version, in its own window.

**Mac**

![PdfEdit for Mac: the start page](screenshots/desktop/mac-start.png)

![PdfEdit for Mac: filling in a form](screenshots/desktop/mac-form.png)

**Linux**

![PdfEdit for Linux: the start page](screenshots/desktop/linux-start.png)

![PdfEdit for Linux: filling in a form](screenshots/desktop/linux-form.png)

## The ribbon

**Fill & Sign**: everything for filling in and signing a form in one place.

![The Fill & Sign ribbon tab](screenshots/ribbon-fill-sign.png)

**Home**: files, navigation and page tools.

![The Home ribbon tab](screenshots/ribbon-home.png)

**Tools**: mark-up, drawing, measuring and stamps.

![The Tools ribbon tab](screenshots/ribbon-tools.png)

**Edit**: form data, adding form fields and lining them up.

![The Edit ribbon tab](screenshots/ribbon-edit.png)

## Filling in forms

Filling in, ticking and signing a flat form in Live View, start to finish:

![Filling in and signing a flat PDF form in Live View](screenshots/fill-and-sign.gif)

Text that doesn't fit its box: the **Fit** menu wraps it and grows the box instead of cutting it off.

![The text toolbar with the Fit menu open](screenshots/fill-text-fit.png)

Ticks and crosses in a flat form's checkboxes. Click inside a square and the mark fills it.

![Ticks and crosses on a printed checklist](screenshots/fill-marks.png)

Dates can be switched to another format, or have the day, month and year changed, from the Properties panel.

![The date format list in the Properties panel](screenshots/fill-date-format.png)

Stamps from Acrobat's standard set and more, including dynamic stamps that add your name and the time.

![The stamp list open with stamps placed on the page](screenshots/fill-stamps.png)

Signing: pick a saved signature or initials and click where they go.

![Placing a signature from the saved signatures list](screenshots/fill-signature.png)

## Around the app

![A tour of the app](screenshots/tour-v4.gif)

| Dark theme | Light theme |
|---|---|
| ![Adding text with the mini toolbar in the dark theme](screenshots/dark-theme.png) | ![Preparing a form in the light theme](screenshots/light-theme.png) |

| Filling a form | Comments and stamps |
|---|---|
| ![Filling a form with the date picker open](screenshots/form-filling.png) | ![Highlights, a sticky note, a stamp and a signature](screenshots/live-view.png) |

| Field properties | Editing fields |
|---|---|
| ![The field properties dialog](screenshots/field-properties.png) | ![Several fields selected with the Align menu open](screenshots/edit-fields.png) |

| Design canvas | AI assistant |
|---|---|
| ![An invoice on the design canvas](screenshots/design-canvas-view.png) | ![The AI assistant after Smart Fill](screenshots/ai-features.png) |

| All tools | High contrast |
|---|---|
| ![The All tools panel](screenshots/all-tools.png) | ![The high contrast theme](screenshots/high-contrast-theme.png) |

![PdfEdit in each of its themes](screenshots/themes.png)

| Settings > Accessibility |
|---|
| <img src="screenshots/settings-accessibility.png" width="480" alt="Sizes, screen reader and narration settings"> |

Sample PDFs (a two-page invoice, a landscape report, a certificate and a mixed-orientation document) are in the `Samples` folder:

![Browsing the sample PDFs](screenshots/samples-v1.gif)

## Web version (Blazor)

PdfEdit in the browser ([PdfEdit.Blazor](../PdfEdit.Blazor/README.md)): the same ribbon, panels and themes.

| | |
|---|---|
| <img src="screenshots/blazor/templates-gallery.png" width="480" alt="The template gallery (File > New)"> | <img src="screenshots/blazor/templates-preview.png" width="480" alt="A template in the preview"> |
| Templates (File > New) | Template preview |
| <img src="screenshots/blazor/start-page.png" width="480" alt="Start page with recent files and templates"> | <img src="screenshots/blazor/item-toolbar.png" width="480" alt="The floating toolbar over selected text"> |
| Recent files and featured templates | Floating toolbar on anything you add |
| <img src="screenshots/blazor/right-click-menu.png" width="480" alt="Right-click menu on a stamp"> | <img src="screenshots/blazor/properties-panel.png" width="480" alt="Properties of the selected item"> |
| Right-click menus | Properties and Lock |
| <img src="screenshots/blazor/comments-panel.png" width="480" alt="Comments panel with review status and replies"> | <img src="screenshots/blazor/field-menu.png" width="480" alt="Field menu in Edit Fields"> |
| Comments: status, checkmarks, replies | Edit Fields: align, space, size |
| <img src="screenshots/blazor/thumbnail-menu.png" width="480" alt="Thumbnail menu"> | <img src="screenshots/blazor/date-format.png" width="480" alt="Date format popover"> |
| Thumbnail menu, rotate, drag to reorder | Date format, day, month, year |
| <img src="screenshots/blazor/signature-after-save.png" width="480" alt="A signature applied to the PDF, selected again"> | |
| Signatures and pictures stay editable after saving | |
| <img src="screenshots/blazor/theme-light.png" width="480" alt="Filling a form in the web version, Light theme"> | <img src="screenshots/blazor/theme-dark.png" width="480" alt="Dark theme"> |
| Light | Dark |
| <img src="screenshots/blazor/theme-high-contrast.png" width="480" alt="High contrast theme"> | <img src="screenshots/blazor/draw-measure.png" width="480" alt="A stamp, freehand drawing, an arrow and a distance measurement"> |
| High contrast | Stamps, drawing and measuring |
| <img src="screenshots/blazor/design.png" width="480" alt="Designing a form"> | <img src="screenshots/blazor/rotated-page.png" width="480" alt="Text and a stamp upright on a rotated page, with document tabs"> |
| Design a form | Rotated pages and document tabs |
| <img src="screenshots/blazor/batch.png" width="480" alt="Batch Process"> | <img src="screenshots/blazor/bulk-fill.png" width="480" alt="Bulk Fill from a spreadsheet"> |
| Batch process | Bulk fill from a spreadsheet |
| <img src="screenshots/blazor/cloud.png" width="480" alt="Opening a PDF from Google Drive"> | <img src="screenshots/blazor/translate-after.png" width="480" alt="A translated page that keeps its layout and colours"> |
| Google Drive and OneDrive | Translate PDF, keeping layout and colours |
