using System.IO;

namespace PdfEdit.Services.Cloud;

/// <summary>The cloud providers and the local copies of files opened from them.</summary>
public static class CloudStorage
{
    public static IReadOnlyList<CloudProvider> Providers { get; } = new CloudProvider[] { GoogleDriveProvider.Instance, OneDriveProvider.Instance };

    public static CloudProvider? Get(string key) => Providers.FirstOrDefault(p => p.Key == key);

    /// <summary>Where a downloaded cloud file is kept on this PC.</summary>
    public static string CachePath(CloudProvider provider, string fileId, string name)
    {
        string safeId = string.Concat(fileId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string safeName = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (!safeName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) safeName += ".pdf";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PdfEdit", "Cloud", provider.Key, safeId.Length > 60 ? safeId[..60] : safeId, safeName);
    }

    public static CloudLink? LinkFor(string? localPath) =>
        localPath != null && AppSettings.Current.CloudLinks.TryGetValue(localPath, out var l) ? l : null;

    public static void Remember(string localPath, CloudProvider provider, string fileId, string name)
    {
        AppSettings.Current.CloudLinks[localPath] = new CloudLink { Provider = provider.Key, FileId = fileId, Name = name };
        AppSettings.Current.Save();
    }
}
