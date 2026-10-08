using PdfEdit.Blazor.Components;
using PdfEdit.Blazor.Services;

namespace PdfEdit.Blazor;

/// <summary>
/// Builds the PdfEdit web app. The web server (Program.cs) runs it as a site; the cross-platform
/// desktop app (PdfEdit.Avalonia) runs the very same app inside its own process, on a private local
/// address, and shows it in a native web view.
/// </summary>
public static class PdfEditWebHost
{
    /// <summary>The cookie a desktop window carries, so only that window can use its private server.</summary>
    public const string DesktopCookie = "pdfedit-desktop";

    /// <summary>The desktop app hosting PdfEdit, or null when it runs as a website.</summary>
    public static Services.IDesktopShell? Desktop { get; set; }

    /// <summary>
    /// Builds the app. <paramref name="desktopToken"/> is set when the desktop app hosts it: the server
    /// then only listens on this computer and only answers its own window.
    /// </summary>
    public static WebApplication Build(string[] args, string? desktopToken = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // Hosted by the desktop app, the static files and settings are still this project's.
            ApplicationName = typeof(PdfEditWebHost).Assembly.GetName().Name,
            ContentRootPath = desktopToken != null ? AppContext.BaseDirectory : null,
        });
        if (desktopToken != null)
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            // Signatures, stamps and settings go in the user's own app data (the program's folder may be read-only).
            if (string.IsNullOrEmpty(builder.Configuration["PdfEdit:Database"]))
            {
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdfEdit");
                Directory.CreateDirectory(data);
                builder.Configuration["PdfEdit:Database"] = Path.Combine(data, "pdfedit-desktop.db");
            }
            // Run from a build (not a published copy), the page files are still in the web project:
            // its manifest says where.
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, builder.Environment.ApplicationName + ".staticwebassets.runtime.json")))
                builder.WebHost.UseStaticWebAssets();
        }

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            // Pictures pasted with Ctrl+V come over the connection; the default 32 KB limit drops it.
            .AddHubOptions(o => o.MaximumReceiveMessageSize = 16 * 1024 * 1024);

        // Uploaded PDFs, read and filled with PdfEdit.Core and drawn with PdfEdit.Render (Pdfium).
        builder.Services.AddSingleton<PdfDocumentStore>();
        // OCR with the Tesseract program on the server (see Services/OcrEngine.cs).
        builder.Services.AddSingleton<OcrEngine>();
        // Google Drive / OneDrive: OAuth apps from configuration, sign-ins kept per browser session.
        builder.Services.AddScoped<CloudConnections>();
        builder.Services.AddSingleton<CloudSignIns>();
        // Saved signatures and initials: SQLite in the app's folder (pdfedit.db, or PdfEdit:Database).
        builder.Services.AddSingleton<SavedSignatures>();
        builder.Services.AddSingleton<UserPrefs>();
        // Thumbnails for the template chooser (File → New).
        builder.Services.AddSingleton<TemplatePreviews>();

        // Office-to-PDF uses LibreOffice on the server; PdfEdit:Office:LibreOfficePath if it isn't on the PATH.
        if (builder.Configuration["PdfEdit:Office:LibreOfficePath"] is { Length: > 0 } soffice)
            PdfEdit.Services.OfficeConversionService.LibreOfficePath = soffice;

        var app = builder.Build();

        if (desktopToken != null)
        {
            if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
            // Only the desktop window may use this server: it opens /?desktop-token=… once, gets a
            // cookie, and every other request without that cookie is turned away.
            app.Use(async (context, next) =>
            {
                if (context.Request.Query.TryGetValue("desktop-token", out var given))
                {
                    if (given == desktopToken)
                    {
                        context.Response.Cookies.Append(DesktopCookie, desktopToken, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict });
                        // On to the app, keeping anything else asked for (a file to open).
                        var keep = context.Request.Query.Where(q => q.Key != "desktop-token")
                            .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}");
                        context.Response.Redirect("/" + (keep.Any() ? "?" + string.Join('&', keep) : ""));
                        return;
                    }
                }
                else if (context.Request.Cookies[DesktopCookie] == desktopToken)
                {
                    await next();
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            });
        }
        else
        {
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapPdfEndpoints();
        app.MapCloudEndpoints();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
        return app;
    }
}
