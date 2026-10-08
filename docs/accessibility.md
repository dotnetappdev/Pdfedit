# Accessibility

[Back to README](../README.md)

The size, screen reader and narration options are in **Settings > Accessibility**.

<img src="screenshots/settings-accessibility.png" width="480" alt="Settings, Accessibility tab">

## Size

Every size setting is in one place, at the top of **Settings > Accessibility**:

- **Interface scale** makes the whole window bigger or smaller. **Ctrl+Plus** and **Ctrl+Minus** do the same at any time, and **Ctrl+0** puts it back to 100%.
- **Text size (all)** sets the text of every part of the window at once, from 80% to 200%.
- **Icon size** sets the ribbon and toolbar icons to Small, Normal, Large or Extra large. The ribbon grows to fit.
- **Text size for each part of the window** fine-tunes one part at a time: ribbon, menus (right-click and drop-down), side panels, AI assistant, status bar and dialogs. **Reset text sizes** puts them all back.

**Ctrl+mouse wheel** zooms the page itself.

## Screen readers

PdfEdit works with Narrator, NVDA and JAWS. Every button, box and panel has a name the screen reader can read. Press **Windows + Ctrl + Enter** to start Narrator.

PdfEdit also tells the screen reader when something happens, so you don't have to go looking:

- **Status messages**, such as "Saved" or "3 fields added"
- **Page changes**, such as "Page 4 of 12"
- **Tool changes**, such as "Add text tool"

Untick any of these, or untick **Tell my screen reader...** to turn them all off.

## Narration

You don't need a screen reader for PdfEdit to read things out. It can speak in a Windows voice:

- the announcements above
- the button, box or menu item you move to with the keyboard
- tooltips, as they appear

Pick the voice, speed and volume, then click **Test** to hear them. **View > Read Page** and **Read to End** use the same voice. By default PdfEdit's voice stays quiet while a screen reader is running, so nothing is said twice.

More voices can be added in Windows: *Settings > Time & language > Speech > Add voices*.

## Keyboard and colours

- Everything can be reached from the keyboard. **Alt** shows the ribbon's key tips, and **F1** lists the shortcuts. You can change any shortcut in **Settings > Keyboard**.
- **Show high-contrast focus indicators** makes it easier to see which control has focus.
- PdfEdit follows Windows contrast themes. See [Themes](themes.md).
- **View > Accessibility Check** tests whether a *PDF* works well with screen readers, and fixes the common problems. See [Automation and tools](automation.md).
