using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PdfEdit.Blazor.Components.Editor;
using PdfEdit.Blazor.Services;

namespace PdfEdit.Blazor.Components.Pages;

// Signatures and initials kept for next time (SQLite on the server, one list per browser).
public partial class Editor
{
    [Inject] private SavedSignatures SignatureLibrary { get; set; } = default!;

    private string _clientId = "";

    public List<StoredSignature> MySignatures { get; private set; } = new();
    /// <summary>Keep a new signature or initials for next time on this browser.</summary>
    public bool KeepSignatures { get; set; } = true;

    private async Task LoadSavedSignaturesAsync()
    {
        try
        {
            _clientId = await JS.InvokeAsync<string>("pdfedit.clientId");
            MySignatures = SignatureLibrary.List(_clientId);
            SignaturePng ??= MySignatures.FirstOrDefault(s => s.Kind == "signature")?.Png;
            InitialsPng ??= MySignatures.FirstOrDefault(s => s.Kind == "initials")?.Png;
        }
        catch { /* storage blocked or no database: signatures last for this visit only */ }
    }

    /// <summary>A signature or initials from the dialog: use it now, and keep it if asked.</summary>
    public void UseNewSignature(byte[] png)
    {
        bool initials = MakingInitials;
        MakingInitials = false;
        if (initials) InitialsPng = png; else SignaturePng = png;
        if (KeepSignatures && _clientId.Length > 0)
        {
            try
            {
                SignatureLibrary.Add(_clientId, initials ? "initials" : "signature", png);
                MySignatures = SignatureLibrary.List(_clientId);
            }
            catch (Exception ex) { Toast("Couldn't keep it for next time: " + ex.Message, "error"); }
        }
        SetTool(initials ? Tool.Initials : Tool.Signature);
    }

    public void UseSavedSignature(StoredSignature s)
    {
        if (s.Kind == "initials") { InitialsPng = s.Png; SetTool(Tool.Initials); }
        else { SignaturePng = s.Png; SetTool(Tool.Signature); }
    }

    public void DeleteSavedSignature(StoredSignature s)
    {
        try { SignatureLibrary.Delete(_clientId, s.Id); } catch { }
        MySignatures.Remove(s);
        if (SignaturePng == s.Png) SignaturePng = MySignatures.FirstOrDefault(x => x.Kind == "signature")?.Png;
        if (InitialsPng == s.Png) InitialsPng = MySignatures.FirstOrDefault(x => x.Kind == "initials")?.Png;
        Status(s.Kind == "initials" ? "Saved initials deleted" : "Saved signature deleted");
    }

    public void NewSignature(bool initials)
    {
        MakingInitials = initials;
        _dialog = DialogKind.Signature;
    }
}
