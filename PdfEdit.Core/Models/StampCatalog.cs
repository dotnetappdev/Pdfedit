namespace PdfEdit.Models;

/// <summary>A stamp you can place: its title, colour, group and whether it adds name + time.</summary>
public sealed record StampDefinition(string Category, string Title, string Color, bool Dynamic = false, string? PdfName = null)
{
    /// <summary>Second line of a dynamic stamp like other PDF editors' "By David at 2:15 pm, 4 Oct 2026".</summary>
    public string? MakeSubtitle(DateTime now) =>
        Dynamic ? $"By {Environment.UserName} at {now:t}, {now:d}" : null;

    public override string ToString() => Title;
}

/// <summary>
/// The stamp list: Business, Signing and Name & time stamps, plus more common
/// office stamps. Custom stamps (Stamps… dialog) are added under "Custom".
/// </summary>
public static class StampCatalog
{
    private const string Green = "#1B7A2E", Red = "#C62828", Blue = "#1F4FB5", Purple = "#6A1B9A", Orange = "#D35400", Grey = "#555555";

    public const string CustomCategory = "Custom";

    public static readonly IReadOnlyList<StampDefinition> BuiltIn = new List<StampDefinition>
    {
        // Business
        new("Business", "APPROVED", Green, PdfName: "Approved"),
        new("Business", "AS IS", Blue, PdfName: "AsIs"),
        new("Business", "COMPLETED", Green),
        new("Business", "CONFIDENTIAL", Red, PdfName: "Confidential"),
        new("Business", "DEPARTMENTAL", Blue, PdfName: "Departmental"),
        new("Business", "DRAFT", Blue, PdfName: "Draft"),
        new("Business", "EXPERIMENTAL", Blue, PdfName: "Experimental"),
        new("Business", "EXPIRED", Red, PdfName: "Expired"),
        new("Business", "FINAL", Green, PdfName: "Final"),
        new("Business", "FOR COMMENT", Blue, PdfName: "ForComment"),
        new("Business", "FOR PUBLIC RELEASE", Green, PdfName: "ForPublicRelease"),
        new("Business", "INFORMATION ONLY", Blue),
        new("Business", "NOT APPROVED", Red, PdfName: "NotApproved"),
        new("Business", "NOT FOR PUBLIC RELEASE", Red, PdfName: "NotForPublicRelease"),
        new("Business", "PRELIMINARY RESULTS", Blue),
        new("Business", "SOLD", Green, PdfName: "Sold"),
        new("Business", "TOP SECRET", Red, PdfName: "TopSecret"),
        new("Business", "VOID", Red),

        // Signing
        new("Signing", "SIGN HERE", Red),
        new("Signing", "INITIAL HERE", Red),
        new("Signing", "WITNESS", Red),
        new("Signing", "ACCEPTED", Green),
        new("Signing", "REJECTED", Red),

        // Name & time (adds who and when)
        new("Name & time", "APPROVED", Green, Dynamic: true, PdfName: "Approved"),
        new("Name & time", "CONFIDENTIAL", Red, Dynamic: true, PdfName: "Confidential"),
        new("Name & time", "RECEIVED", Blue, Dynamic: true),
        new("Name & time", "REVIEWED", Blue, Dynamic: true),
        new("Name & time", "REVISED", Purple, Dynamic: true),
        new("Name & time", "PAID", Green, Dynamic: true),
        new("Name & time", "VERIFIED", Green, Dynamic: true),
        new("Name & time", "CHECKED", Green, Dynamic: true),
        new("Name & time", "SCANNED", Grey, Dynamic: true),
        new("Name & time", "ENTERED", Blue, Dynamic: true),

        // More office stamps
        new("More", "PAID", Green),
        new("More", "NOT PAID", Red),
        new("More", "PAST DUE", Red),
        new("More", "URGENT", Red),
        new("More", "RUSH", Orange),
        new("More", "PRIORITY", Orange),
        new("More", "PENDING", Orange),
        new("More", "ON HOLD", Orange),
        new("More", "CANCELLED", Red),
        new("More", "VOIDED", Red),
        new("More", "DO NOT COPY", Red),
        new("More", "INTERNAL USE ONLY", Red),
        new("More", "PRIVATE", Red),
        new("More", "PERSONAL", Purple),
        new("More", "COPY", Grey),
        new("More", "ORIGINAL", Blue),
        new("More", "DUPLICATE", Grey),
        new("More", "SAMPLE", Purple),
        new("More", "SPECIMEN", Purple),
        new("More", "FOR APPROVAL", Blue),
        new("More", "FOR SIGNATURE", Blue),
        new("More", "FOR YOUR INFORMATION", Blue),
        new("More", "APPROVED AS NOTED", Green),
        new("More", "REVISE AND RESUBMIT", Orange),
        new("More", "RECEIVED", Blue),
        new("More", "REVIEWED", Blue),
        new("More", "REVISED", Purple),
        new("More", "CORRECTED", Purple),
        new("More", "FILED", Grey),
        new("More", "POSTED", Grey),
        new("More", "EMAILED", Grey),
        new("More", "FAXED", Grey),
        new("More", "DELIVERED", Green),
        new("More", "SHIPPED", Green),
        new("More", "BACK ORDER", Orange),
        new("More", "CLOSED", Grey),
        new("More", "OBSOLETE", Grey),
        new("More", "SUPERSEDED", Grey),
        new("More", "DO NOT USE", Red),
    };

    /// <summary>Built-in stamps followed by the user's custom ones.</summary>
    public static List<StampDefinition> All(IEnumerable<Services.CustomStampSetting> custom) =>
        BuiltIn.Concat(custom.Where(c => !string.IsNullOrWhiteSpace(c.Title))
                             .Select(c => new StampDefinition(CustomCategory, c.Title.Trim(), string.IsNullOrWhiteSpace(c.Color) ? Purple : c.Color, c.Dynamic)))
               .ToList();

    /// <summary>Default size of a placed stamp in points, from its text.</summary>
    public static (double Width, double Height) SizeFor(string title, string? subtitle)
    {
        double h = subtitle != null ? 46 : 34;
        double w = Math.Clamp(title.Length * 12.5 + 34, 90, 330);
        if (subtitle != null) w = Math.Max(w, subtitle.Length * 4.6 + 30);
        return (w, h);
    }

    /// <summary>"Category|Title": how the default stamp is remembered.</summary>
    public static string KeyOf(StampDefinition d) => $"{d.Category}|{d.Title}";

    /// <summary>Colour for a stamp title (custom / unknown stamps are purple).</summary>
    public static string ColorFor(string title) =>
        BuiltIn.FirstOrDefault(d => d.Title.Equals(title, StringComparison.OrdinalIgnoreCase))?.Color ?? Purple;
}
