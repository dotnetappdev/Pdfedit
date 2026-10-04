using System.Windows;
using System.Windows.Input;
using PdfEdit.Models;

namespace PdfEdit.ViewModels;

/// <summary>Fill &amp; Sign ribbon commands that the toolbox rail also offers.</summary>
public partial class MainViewModel
{
    private ICommand? _newSignatureCommand, _newInitialsCommand;

    /// <summary>Draw / type / import a new signature, then click the page to place it.</summary>
    public ICommand NewSignatureCommand => _newSignatureCommand ??= new RelayCommand(() => CreateSignature(false), () => HasDocument);

    /// <summary>Same as <see cref="NewSignatureCommand"/> for initials.</summary>
    public ICommand NewInitialsCommand => _newInitialsCommand ??= new RelayCommand(() => CreateSignature(true), () => HasDocument);

    private void CreateSignature(bool initials)
    {
        var dlg = new Dialogs.SignatureDialog { Owner = Application.Current.MainWindow, InitialsMode = initials };
        if (dlg.ShowDialog() != true || dlg.Result?.ImageBytes is not { } png) return;
        if (IsDesignMode)
        {
            DesignCanvas.PendingSignature = png;
            DesignCanvas.ActiveTool = DesignTool.Sign;
        }
        else
        {
            PendingLibrarySignature = png;
            ActiveTool = ActiveTool.Signature;
        }
        Services.ToastService.Instance.Info(initials ? "Initials created — click the page to place." : "Signature created — click the page to place.");
    }
}
