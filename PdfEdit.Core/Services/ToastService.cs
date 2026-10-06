namespace PdfEdit.Services;

public enum ToastType { Info, Success, Warning, Error }

public sealed class ToastService
{
    public static ToastService Instance { get; } = new();

    public event Action<string, ToastType>? ToastRequested;

    public void Show(string message, ToastType type = ToastType.Info)
        => ToastRequested?.Invoke(message, type);

    public void Info(string message) => Show(message, ToastType.Info);
    public void Success(string message) => Show(message, ToastType.Success);
    public void Warning(string message) => Show(message, ToastType.Warning);
    public void Error(string message) => Show(message, ToastType.Error);
}
