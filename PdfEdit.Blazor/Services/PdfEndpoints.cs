namespace PdfEdit.Blazor.Services;

/// <summary>Plain HTTP endpoints the pages use for images and downloads.</summary>
public static class PdfEndpoints
{
    public static void MapPdfEndpoints(this WebApplication app)
    {
        // A rendered page: /documents/{id}/pages/0.png?scale=2
        app.MapGet("/documents/{id}/pages/{page:int}.png", async (string id, int page, double? scale, PdfDocumentStore store) =>
        {
            var session = store.Get(id);
            if (session == null || page < 0 || page >= session.Info.PageCount) return Results.NotFound();
            var png = await store.RenderPageAsync(session, page, scale ?? 2);
            return Results.File(png, "image/png");
        });

        // The last filled-in copy saved from the Fill Form page.
        app.MapGet("/documents/{id}/download", (string id, PdfDocumentStore store) =>
        {
            var session = store.Get(id);
            if (session?.FilledPath == null || !File.Exists(session.FilledPath)) return Results.NotFound();
            var name = Path.GetFileNameWithoutExtension(session.FileName) + " (filled).pdf";
            return Results.File(session.FilledPath, "application/pdf", name);
        });
    }
}
