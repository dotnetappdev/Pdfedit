using PdfEdit.Blazor.Components;
using PdfEdit.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Uploaded PDFs, read and filled with PdfEdit.Core and drawn with PdfEdit.Render (Pdfium).
builder.Services.AddSingleton<PdfDocumentStore>();
// OCR with the Tesseract program on the server (see Services/OcrEngine.cs).
builder.Services.AddSingleton<OcrEngine>();
// Google Drive / OneDrive: OAuth apps from configuration, sign-ins kept per browser session.
builder.Services.AddScoped<CloudConnections>();
builder.Services.AddSingleton<CloudSignIns>();
// Saved signatures and initials: SQLite in the app's folder (pdfedit.db, or PdfEdit:Database).
builder.Services.AddSingleton<SavedSignatures>();

// Office-to-PDF uses LibreOffice on the server; PdfEdit:Office:LibreOfficePath if it isn't on the PATH.
if (builder.Configuration["PdfEdit:Office:LibreOfficePath"] is { Length: > 0 } soffice)
    PdfEdit.Services.OfficeConversionService.LibreOfficePath = soffice;

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapPdfEndpoints();
app.MapCloudEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
