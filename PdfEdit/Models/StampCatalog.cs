namespace PdfEdit.Models;

/// <summary>A stamp you can place: its title, colour, group and whether it adds name + time.</summary>
public sealed record StampDefinition(string Category, string Title, string Color, bool Dynamic = false, string? PdfName = null)
{
    /// <summary>Second line of a dynamic stamp, like Acrobat's "By David at 2:15 pm, 4 Oct 2026".</summary>
    public string? MakeSubtitle(DateTime now) =>
        Dynamic ? $"By {Environment.UserName} at {now:t}, {now:d}" : null;

    public override string ToString() => Title;
}

/// <summary>
/// The stamp list: Acrobat's Standard Business, Sign Here and Dynamic stamps, plus more common
/// office stamps. Custom stamps (Settings) are added under "Custom".
/// </summary>
public static class StampCatalog
{
    private const string Green = "#1B7A2E", Red = "#C62828", Blue = "#1F4FB5", Purple = "#6A1B9A", Orange = "#D35400", Grey = "#555555";

    public const string CustomCategory = "Custom";

    public static readonly IReadOnlyList<StampDefinition> BuiltIn = new List<StampDefinition>
    {
        // Acrobat — Standard Business
        new("Standard Business", "APPROVED", Green, PdfName: "Approved"),
        new("Standard Business", "AS IS", Blue, PdfName: "AsIs"),
        new("Standard Business", "COMPLETED", Green),
        new("Standard Business", "CONFIDENTIAL", Red, PdfName: "Confidential"),
        new("Standard Business", "DEPARTMENTAL", Blue, PdfName: "Departmental"),
        new("Standard Business", "DRAFT", Blue, PdfName: "Draft"),
        new("Standard Business", "EXPERIMENTAL", Blue, PdfName: "Experimental"),
        new("Standard Business", "EXPIRED", Red, PdfName: "Expired"),
        new("Standard Business", "FINAL", Green, PdfName: "Final"),
        new("Standard Business", "FOR COMMENT", Blue, PdfName: "ForComment"),
        new("Standard Business", "FOR PUBLIC RELEASE", Green, PdfName: "ForPublicRelease"),
        new("Standard Business", "INFORMATION ONLY", Blue),
        new("Standard Business", "NOT APPROVED", Red, PdfName: "NotApproved"),
        new("Standard Business", "NOT FOR PUBLIC RELEASE", Red, PdfName: "NotForPublicRelease"),
        new("Standard Business", "PRELIMINARY RESULTS", Blue),
        new("Standard Business", "SOLD", Green, PdfName: "Sold"),
        new("Standard Business", "TOP SECRET", Red, PdfName: "TopSecret"),
        new("Standard Business", "VOID", Red),

        // Acrobat — Sign Here
        new("Sign Here", "SIGN HERE", Red),
        new("Sign Here", "INITIAL HERE", Red),
        new("Sign Here", "WITNESS", Red),
        new("Sign Here", "ACCEPTED", Green),
        new("Sign Here", "REJECTED", Red),

        // Acrobat — Dynamic (adds who and when)
        new("Dynamic", "APPROVED", Green, Dynamic: true, PdfName: "Approved"),
        new("Dynamic", "CONFIDENTIAL", Red, Dynamic: true, PdfName: "Confidential"),
        new("Dynamic", "RECEIVED", Blue, Dynamic: true),
        new("Dynamic", "REVIEWED", Blue, Dynamic: true),
        new("Dynamic", "REVISED", Purple, Dynamic: true),
        new("Dynamic", "PAID", Green, Dynamic: true),
        new("Dynamic", "VERIFIED", Green, Dynamic: true),
        new("Dynamic", "CHECKED", Green, Dynamic: true),
        new("Dynamic", "SCANNED", Grey, Dynamic: true),
        new("Dynamic", "ENTERED", Blue, Dynamic: true),

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
    public static List<StampDefinition> All(IEnumerable<string> custom) =>
        BuiltIn.Concat(custom.Where(c => !string.IsNullOrWhiteSpace(c))
                             .Select(c => new StampDefinition(CustomCategory, c.Trim(), Purple))).ToList();

    /// <summary>Colour for a stamp title (custom / unknown stamps are purple).</summary>
    public static string ColorFor(string title) =>
        BuiltIn.FirstOrDefault(d => d.Title.Equals(title, StringComparison.OrdinalIgnoreCase))?.Color ?? Purple;
}
