using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PdfEdit.Models;
using PdfEdit.Services;

namespace PdfEdit.Dialogs;

/// <summary>
/// Batch processing (Acrobat's Action Wizard): choose files, build a list of steps, run them over
/// every file. Step lists can be saved as named actions for next time.
/// </summary>
public partial class BatchDialog : Window
{
    private readonly ObservableCollection<string> _files = new();
    private readonly ObservableCollection<BatchStep> _steps = new();
    private readonly string? _openFile;
    private CancellationTokenSource? _cts;
    private bool _loadingAction;

    public BatchDialog(string? openFile = null)
    {
        InitializeComponent();
        _openFile = openFile;
        AddOpenBtn.IsEnabled = openFile != null;
        FileList.ItemsSource = _files;
        StepList.ItemsSource = _steps;
        _files.CollectionChanged += (_, _) => FilesHead.Text = $"FILES ({_files.Count})";

        AddStepBox.Items.Add(new ComboBoxItem { Content = "+ Add a step…", IsEnabled = false });
        foreach (BatchStepKind k in Enum.GetValues(typeof(BatchStepKind)))
            AddStepBox.Items.Add(new ComboBoxItem { Content = BatchStep.Title(k), Tag = k });
        AddStepBox.SelectedIndex = 0;

        RefreshActions();
        RefreshStepTexts();
    }

    // ── Files ─────────────────────────────────────────────────────────────────
    private void AddFile(string f)
    {
        if (File.Exists(f) && f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !_files.Contains(f, StringComparer.OrdinalIgnoreCase))
            _files.Add(f);
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF files|*.pdf", Multiselect = true, Title = "Add PDFs" };
        if (dlg.ShowDialog(this) == true) foreach (var f in dlg.FileNames) AddFile(f);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Add every PDF in a folder" };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in BatchService.PdfsIn(dlg.FolderName, SubfoldersBox.IsChecked == true)) AddFile(f);
    }

    private void AddOpen_Click(object sender, RoutedEventArgs e) { if (_openFile != null) AddFile(_openFile); }

    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in FileList.SelectedItems.Cast<string>().ToList()) _files.Remove(f);
    }

    private void ClearFiles_Click(object sender, RoutedEventArgs e) => _files.Clear();

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) foreach (var f in BatchService.PdfsIn(p, SubfoldersBox.IsChecked == true)) AddFile(f);
            else AddFile(p);
        }
    }

    // ── Steps ─────────────────────────────────────────────────────────────────
    private void RefreshStepTexts()
    {
        // ListBox shows ToString(); keep a parallel list of summaries via a converter-free refresh.
        int sel = StepList.SelectedIndex;
        StepList.ItemsSource = null;
        StepList.ItemsSource = _steps.Select((s, i) => $"{i + 1}.  {s.Summary()}").ToList();
        StepList.SelectedIndex = Math.Min(sel, _steps.Count - 1);
    }

    private void AddStepBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AddStepBox.SelectedItem is ComboBoxItem { Tag: BatchStepKind k })
        {
            _steps.Add(BatchStep.Create(k));
            RefreshStepTexts();
            StepList.SelectedIndex = _steps.Count - 1;
        }
        AddStepBox.SelectedIndex = 0;
    }

    private void Move(int delta)
    {
        int i = StepList.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _steps.Count) return;
        _steps.Move(i, j);
        RefreshStepTexts();
        StepList.SelectedIndex = j;
    }

    private void StepUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void StepDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        int i = StepList.SelectedIndex;
        if (i < 0) return;
        _steps.RemoveAt(i);
        RefreshStepTexts();
    }

    private void StepList_SelectionChanged(object sender, SelectionChangedEventArgs e) => BuildSettings();

    /// <summary>Settings fields for the selected step, generated from its schema.</summary>
    private void BuildSettings()
    {
        SettingsPanel.Children.Clear();
        int i = StepList.SelectedIndex;
        if (i < 0 || i >= _steps.Count)
        {
            SettingsHead.Text = "STEP SETTINGS";
            SettingsPanel.Children.Add(new TextBlock { Text = "Add steps with the list below the steps, then select one to change its settings.", TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)FindResource("DimForegroundBrush") });
            return;
        }
        var step = _steps[i];
        SettingsHead.Text = BatchStep.Title(step.Kind).ToUpperInvariant();
        var schema = BatchStep.Schema(step.Kind);
        if (schema.Count == 0)
        {
            SettingsPanel.Children.Add(new TextBlock
            {
                Text = step.Kind switch
                {
                    BatchStepKind.Ocr => "Pages that already have text are skipped. Uses the OCR engine chosen in Settings → OCR.",
                    BatchStepKind.Compress => "Re-saves with maximum compression.",
                    BatchStepKind.Flatten => "Filled-in values become part of the page and can no longer be edited.",
                    BatchStepKind.PdfA => "Saves an archival PDF/A copy.",
                    BatchStepKind.ExportText => "Writes the text of each PDF to a .txt file next to the result.",
                    _ => "No settings.",
                },
                TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)FindResource("DimForegroundBrush"),
            });
            return;
        }
        foreach (var p in schema)
        {
            if (p.IsBool)
            {
                var cb = new CheckBox { Content = p.Label, IsChecked = step.GetBool(p.Key), Margin = new Thickness(0, 4, 0, 4) };
                cb.Checked += (_, _) => Set(step, p.Key, "true");
                cb.Unchecked += (_, _) => Set(step, p.Key, "false");
                SettingsPanel.Children.Add(cb);
                continue;
            }
            SettingsPanel.Children.Add(new TextBlock { Text = p.Label, FontSize = 11, Margin = new Thickness(0, 6, 0, 3), Foreground = (System.Windows.Media.Brush)FindResource("DimForegroundBrush") });
            if (p.Choices != null)
            {
                var combo = new ComboBox { ItemsSource = p.Choices, SelectedItem = step.Get(p.Key), Height = 26 };
                combo.SelectionChanged += (_, _) => Set(step, p.Key, combo.SelectedItem as string ?? p.Default);
                SettingsPanel.Children.Add(combo);
            }
            else if (p.Secret)
            {
                var pb = new PasswordBox { Password = step.Get(p.Key), Height = 26, Background = (System.Windows.Media.Brush)FindResource("InputBgBrush"),
                                           Foreground = (System.Windows.Media.Brush)FindResource("InputForegroundBrush"), VerticalContentAlignment = VerticalAlignment.Center };
                pb.PasswordChanged += (_, _) => step.Settings[p.Key] = pb.Password;
                SettingsPanel.Children.Add(pb);
            }
            else
            {
                var tb = new TextBox { Text = step.Get(p.Key) };
                tb.TextChanged += (_, _) => Set(step, p.Key, tb.Text, refresh: false);
                tb.LostFocus += (_, _) => RefreshStepTexts();
                SettingsPanel.Children.Add(tb);
            }
        }
        if (step.Kind is BatchStepKind.HeaderFooter)
            SettingsPanel.Children.Add(new TextBlock { Text = "{file} = file name, {date} = today.", FontSize = 11, Margin = new Thickness(0, 8, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("DimForegroundBrush") });
        if (step.Kind is BatchStepKind.Password)
            SettingsPanel.Children.Add(new TextBlock { Text = "Passwords are never saved with an action.", FontSize = 11, Margin = new Thickness(0, 8, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("DimForegroundBrush") });
    }

    private void Set(BatchStep step, string key, string value, bool refresh = true)
    {
        step.Settings[key] = value;
        if (refresh)
        {
            int sel = StepList.SelectedIndex;
            var list = _steps.Select((s, i) => $"{i + 1}.  {s.Summary()}").ToList();
            if (StepList.ItemsSource is List<string> cur && sel >= 0 && sel < cur.Count) { cur[sel] = list[sel]; StepList.Items.Refresh(); }
        }
    }

    // ── Saved actions ────────────────────────────────────────────────────────
    private void RefreshActions(string? select = null)
    {
        _loadingAction = true;
        var actions = BatchService.LoadActions();
        ActionBox.ItemsSource = actions;
        ActionBox.SelectedItem = actions.FirstOrDefault(a => a.Name == select);
        _loadingAction = false;
    }

    private void ActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingAction || ActionBox.SelectedItem is not BatchAction a) return;
        _steps.Clear();
        foreach (var s in a.Steps) _steps.Add(new BatchStep { Kind = s.Kind, Settings = new(s.Settings) });
        RefreshStepTexts();
        StepList.SelectedIndex = _steps.Count > 0 ? 0 : -1;
    }

    private void SaveAction_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) { AppDialog.ShowInfo("Add some steps first.", "Save action"); return; }
        var dlg = new InputDialog("Save action", "Name for this list of steps:", (ActionBox.SelectedItem as BatchAction)?.Name ?? "") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;
        string name = dlg.InputText.Trim();
        BatchService.SaveAction(new BatchAction { Name = name, Steps = _steps.ToList() });
        RefreshActions(name);
    }

    private void DeleteAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionBox.SelectedItem is not BatchAction a) return;
        if (!AppDialog.ShowConfirm($"Delete the saved action \"{a.Name}\"?", "Delete action", "Delete", "Cancel", isDanger: true)) return;
        BatchService.DeleteAction(a.Name);
        RefreshActions();
    }

    // ── Output ───────────────────────────────────────────────────────────────
    private void Out_Changed(object sender, RoutedEventArgs e)
    {
        if (OverwriteBox == null || SuffixBox == null) return;
        if (OtherFolder.IsChecked == true) OverwriteBox.IsChecked = false;
        OverwriteBox.IsEnabled = SameFolder.IsChecked == true;
        SuffixBox.IsEnabled = OverwriteBox.IsChecked != true;
    }

    private void BrowseOut_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Save results in" };
        if (dlg.ShowDialog(this) == true) FolderBox.Text = dlg.FolderName;
    }

    // ── Run ──────────────────────────────────────────────────────────────────
    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        if (_files.Count == 0) { AppDialog.ShowInfo("Add the PDFs to process first.", "Batch"); return; }
        if (_steps.Count == 0) { AppDialog.ShowInfo("Add at least one step.", "Batch"); return; }
        bool otherFolder = OtherFolder.IsChecked == true;
        if (otherFolder && string.IsNullOrWhiteSpace(FolderBox.Text)) { AppDialog.ShowInfo("Choose the folder to save the results in.", "Batch"); return; }
        bool overwrite = OverwriteBox.IsChecked == true && !otherFolder;
        if (overwrite && !AppDialog.ShowConfirm($"Replace the original {_files.Count} file(s) with the results?\nThis can't be undone.", "Replace originals", "Replace", "Cancel", isDanger: true))
            return;

        var output = new BatchOutput(otherFolder ? FolderBox.Text.Trim() : null, overwrite ? "" : SuffixBox.Text, overwrite);
        LogList.Items.Clear();
        Progress.Value = 0;
        Progress.Maximum = _files.Count;
        RunBtn.Content = "Stop";
        _cts = new CancellationTokenSource();
        var progress = new Progress<BatchProgress>(p =>
        {
            Progress.Value = p.Step is "Done" or "Failed" ? p.FileIndex + 1 : p.FileIndex + 0.5;
            if (p.Step is "Done" or "Failed" || p.Message != null)
                LogList.Items.Add($"{(p.IsError ? "✕" : "✓")} {p.File}: {(p.Step is "Done" or "Failed" ? "" : p.Step + " — ")}{p.Message}");
            else SummaryText.Text = $"{p.File} — {p.Step}…";
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        });
        try
        {
            var (ok, failed) = await BatchService.RunAsync(_files.ToList(), _steps.ToList(), output, progress, _cts.Token);
            SummaryText.Text = failed == 0 ? $"Finished: {ok} file(s) processed." : $"Finished: {ok} processed, {failed} failed — see the list above.";
            if (failed == 0) ToastService.Instance.Success($"Batch finished — {ok} file(s).");
        }
        catch (OperationCanceledException) { SummaryText.Text = "Stopped."; }
        catch (Exception ex) { AppDialog.ShowError("The batch could not run.", ex); }
        finally
        {
            _cts = null;
            RunBtn.Content = "Run";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }
}
