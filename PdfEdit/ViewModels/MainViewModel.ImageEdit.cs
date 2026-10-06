using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PdfEdit.Services;

namespace PdfEdit.ViewModels;

/// <summary>Edit Images: move, resize, replace, save, copy and delete the pictures already in the PDF.</summary>
public partial class MainViewModel
{
    private (string Path, DateTime Stamp, int Page, List<PageImageInfo> Images)? _imageCache;
    private ICommand? _editImagesCommand;

    /// <summary>Turns on the Edit Images tool.</summary>
    public ICommand EditImagesCommand => _editImagesCommand ??= new RelayCommand(() =>
    {
        IsDesignMode = false;
        ActiveTool = Models.ActiveTool.EditImages;
        StatusText = "Edit Images: click a picture to select it, then drag to move, drag a corner to resize, or Replace / Save / Delete.";
    }, () => HasDocument);

    /// <summary>The pictures on the current page (cached until the file changes).</summary>
    public IReadOnlyList<PageImageInfo> GetImagesForCurrentPage()
    {
        if (_currentFilePath == null || _document == null) return Array.Empty<PageImageInfo>();
        try
        {
            var stamp = File.GetLastWriteTimeUtc(_currentFilePath);
            int page = _currentPageIndex + 1;
            if (_imageCache is not { } c || c.Path != _currentFilePath || c.Stamp != stamp || c.Page != page)
                _imageCache = (_currentFilePath, stamp, page, ImageEditService.GetImages(_currentFilePath, page));
            return _imageCache.Value.Images;
        }
        catch { return Array.Empty<PageImageInfo>(); }
    }

    public async Task MoveResizeImageAsync(PageImageInfo img, double left, double bottom, double width, double height)
    {
        _imageCache = null;
        await ModifyCurrentFileAsync((i, o) => ImageEditService.MoveResize(i, o, img.PageNumber, img.Index, left, bottom, width, height),
            width == img.Width && height == img.Height ? "Picture moved" : "Picture resized");
    }

    public async Task DeleteImageAsync(PageImageInfo img)
    {
        _imageCache = null;
        if (await ModifyCurrentFileAsync((i, o) => ImageEditService.Delete(i, o, img.PageNumber, img.Index), "Picture deleted"))
            ToastService.Instance.Success("Picture deleted. Ctrl+Z to undo.");
    }

    public async Task ReplaceImageAsync(PageImageInfo img)
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Replace picture with…",
            Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*",
        };
        if (open.ShowDialog() != true) return;
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(open.FileName); }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("That picture couldn't be read.", ex); return; }
        _imageCache = null;
        if (await ModifyCurrentFileAsync((i, o) => ImageEditService.Replace(i, o, img.PageNumber, img.Index, bytes), "Picture replaced"))
            ToastService.Instance.Success("Picture replaced. Drag it or its corners to adjust; Ctrl+Z to undo.");
    }

    public async Task SaveImageAsync(PageImageInfo img)
    {
        if (_currentFilePath == null) return;
        string src = _currentFilePath;
        try
        {
            var (bytes, ext) = await Task.Run(() => ImageEditService.GetImageFile(src, img.PageNumber, img.Index));
            var save = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save picture",
                Filter = $"Picture (*{ext})|*{ext}|All files (*.*)|*.*",
                FileName = $"{Path.GetFileNameWithoutExtension(src)} page {img.PageNumber} picture {img.Index + 1}{ext}",
            };
            if (save.ShowDialog() != true) return;
            await File.WriteAllBytesAsync(save.FileName, bytes);
            StatusText = $"Picture saved to {Path.GetFileName(save.FileName)}.";
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("The picture couldn't be saved.", ex); }
    }

    public async Task CopyImageAsync(PageImageInfo img)
    {
        if (_currentFilePath == null) return;
        string src = _currentFilePath;
        try
        {
            var (bytes, _) = await Task.Run(() => ImageEditService.GetImageFile(src, img.PageNumber, img.Index));
            using var ms = new MemoryStream(bytes);
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Clipboard.SetImage(frame);
            StatusText = "Picture copied.";
        }
        catch (Exception ex) { Dialogs.AppDialog.ShowError("The picture couldn't be copied (its format may not be supported by Windows).", ex); }
    }
}
