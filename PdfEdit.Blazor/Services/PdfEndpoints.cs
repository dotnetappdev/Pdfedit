namespace PdfEdit.Blazor.Services;

/// <summary>Plain HTTP endpoints the editor uses for page images and downloads.</summary>
public static class PdfEndpoints
{
    public static void MapPdfEndpoints(this WebApplication app)
    {
        // A rendered page: /documents/{id}/pages/0.png?scale=2&v=3 (v changes with every edit,
        // so the image can be cached for as long as the browser likes).
        app.MapGet("/documents/{id}/pages/{page:int}.png", async (string id, int page, double? scale, PdfDocumentStore store, HttpContext http) =>
        {
            var session = store.Get(id);
            if (session == null || page < 0 || page >= session.Info.PageCount) return Results.NotFound();
            var png = await store.RenderPageAsync(session, page, scale ?? 2);
            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(png, "image/png");
        });

        // The current version of the document, as a PDF.
        // ?inline=1 shows it in the browser (to print) instead of downloading it.
        app.MapGet("/documents/{id}/file", (string id, bool? inline, PdfDocumentStore store) =>
        {
            var session = store.Get(id);
            if (session == null || !File.Exists(session.CurrentPath)) return Results.NotFound();
            var bytes = File.ReadAllBytes(session.CurrentPath);
            return inline == true
                ? Results.File(bytes, "application/pdf")
                : Results.File(bytes, "application/pdf", session.FileName);
        });

        // Downloads that don't belong to a document (designs).
        app.MapGet("/downloads/{id}/{name}", (string id, string name, PdfDocumentStore store) =>
        {
            var path = store.DownloadPath(id, name);
            if (path == null) return Results.NotFound();
            var type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".pdfdesign" or ".json" => "application/json",
                ".png" => "image/png",
                _ => "application/octet-stream",
            };
            return Results.File(File.ReadAllBytes(path), type, Path.GetFileName(path));
        });

        // Files made by Save As, Export, Split, Extract and so on.
        app.MapGet("/documents/{id}/exports/{name}", (string id, string name, PdfDocumentStore store) =>
        {
            var session = store.Get(id);
            var path = session == null ? null : store.ExportPath(session, name);
            if (path == null) return Results.NotFound();
            var type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".png" => "image/png",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".html" => "text/html",
                ".md" => "text/markdown",
                ".epub" => "application/epub+zip",
                ".txt" or ".tsv" => "text/plain",
                ".xfdf" => "application/vnd.adobe.xfdf",
                _ => "application/octet-stream",
            };
            return Results.File(File.ReadAllBytes(path), type, Path.GetFileName(path));
        });
    }
}
