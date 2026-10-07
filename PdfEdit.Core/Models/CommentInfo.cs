namespace PdfEdit.Models;

/// <summary>Review status ("Set Status" on a comment).</summary>
public enum CommentStatus { None, Accepted, Rejected, Cancelled, Completed }

/// <summary>A reply in a comment thread ("Reply").</summary>
public class CommentReply
{
    public string Author { get; set; } = CommentInfo.DefaultAuthor;
    public string Text { get; set; } = string.Empty;
    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>
/// The comment side of an annotation, as the usual Comments list shows it: who made it, when,
/// its note, the reply thread, review status and the reviewer's checkmark. Saved into the PDF
/// as /T (author), /M, /Contents, /State replies and /IRT reply annotations.
/// </summary>
public class CommentInfo
{
    public static string DefaultAuthor => Environment.UserName;

    // Stable id, written to the PDF as /NM "pdfedit:<id>" so a re-save replaces this annotation
    // instead of adding a copy, and reopening doesn't draw it twice.
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Author { get; set; } = DefaultAuthor;
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;
    public string Note { get; set; } = string.Empty;
    public List<CommentReply> Replies { get; set; } = new();
    public CommentStatus Status { get; set; }
    public bool Checked { get; set; }
}
