# Screenshots

[← Back to README](../README.md)

These are rendered mock-ups of the interface rather than captures of the running app, so small details may differ. The ribbon and form-filling images are drawn from the real ribbon layout and can be regenerated with `node docs/screenshots/src/render.mjs`.

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

| Settings → Accessibility |
|---|
| <img src="screenshots/settings-accessibility.png" width="480" alt="Sizes, screen reader and narration settings"> |

Sample PDFs (a two-page invoice, a landscape report, a certificate and a mixed-orientation document) are in the `Samples` folder:

![Browsing the sample PDFs](screenshots/samples-v1.gif)
