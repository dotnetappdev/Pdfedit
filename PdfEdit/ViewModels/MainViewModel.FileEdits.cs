using System.IO;
using System.Windows;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>
/// Changes written straight into the open PDF (links, watermarks …): run an iText edit from the
/// current file to a temp file, put it in place, reload, and make it undoable (Ctrl+Z restores the
/// file as it was; reloading clears the in-memory undo history, so the step is re-added after it).
/// </summary>
public partial class MainViewModel
{
    /// <summary>Runs <paramref name="edit"/>(input, output) on the open PDF. Returns false if it failed.</summary>
    public async Task<bool> ModifyCurrentFileAsync(Action<string, string> edit, string description)
    {
        if (_currentFilePath == null) return false;
        string path = _currentFilePath;
        string tmp = path + ".pdfedit-tmp";
        try
        {
            byte[] before = await File.ReadAllBytesAsync(path);
            await Task.Run(() => edit(path, tmp));
            byte[] after = await File.ReadAllBytesAsync(tmp);
            await WriteOpenFileAsync(path, after);
            _linkCache = null;
            await ReloadCurrentFileAsync();
            PushFileUndo(path, before, after, description);
            StatusText = $"{description}.";
            return true;
        }
        catch (IOException ex) when (IsFileLocked(ex))
        {
            Dialogs.AppDialog.ShowError(LockedMessage(path, description), ex);
            return false;
        }
        catch (Exception ex)
        {
            Dialogs.AppDialog.ShowError($"{description} failed.", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// Overwrites the open PDF. Virus scanners, Explorer's preview pane and search indexing hold a
    /// file for a moment (often right after a download), so a sharing violation is retried briefly
    /// before giving up; a file kept open in another program still fails.
    /// </summary>
    private static async Task WriteOpenFileAsync(string path, byte[] bytes)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await File.WriteAllBytesAsync(path, bytes);
                return;
            }
            catch (IOException ex) when (IsFileLocked(ex) && attempt < 6)
            {
                await Task.Delay(150 * attempt);
            }
        }
    }

    /// <summary>ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33).</summary>
    private static bool IsFileLocked(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    private static string LockedMessage(string path, string description) =>
        $"{description} couldn't be saved: {Path.GetFileName(path)} is open in another program " +
        "(another PDF reader, a browser tab, or Explorer's preview pane).\n\n" +
        "Close it there and try again, or use Save As to keep a copy under another name.";

    private void PushFileUndo(string path, byte[] before, byte[] after, string description)
    {
        (Action Undo, Action Redo) entry = default;
        entry = (
            () => _ = Restore(before, toRedo: true),
            () => _ = Restore(after, toRedo: false));

        async Task Restore(byte[] bytes, bool toRedo)
        {
            if (!string.Equals(_currentFilePath, path, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                await WriteOpenFileAsync(path, bytes);
                _linkCache = null;
                await ReloadCurrentFileAsync();
                // The reload emptied both stacks: put this step back where Undo / Redo expects it.
                (toRedo ? _redoStack : _undoStack).Push(entry);
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanRedo));
                StatusText = toRedo ? $"Undone: {description}." : $"Redone: {description}.";
            }
            catch (IOException ex) when (IsFileLocked(ex))
            {
                Dialogs.AppDialog.ShowError(LockedMessage(path, toRedo ? $"Undo \"{description}\"" : $"Redo \"{description}\""), ex);
            }
            catch (Exception ex)
            {
                Dialogs.AppDialog.ShowError($"Could not undo \"{description}\".", ex);
            }
        }

        _undoStack.Push(entry);
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
