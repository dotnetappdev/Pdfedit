using PdfEdit.Models;

namespace PdfEdit.Templates;

/// <summary>
/// The first seven Design-canvas templates (Invoice, Letter, Form, Certificate, Business Card, Resume,
/// Flyer), as the Windows app has always laid them out. Part of <see cref="TemplateCatalog"/>.
/// </summary>
internal static class ClassicTemplates
{
    public static readonly string[] Names = ["Invoice", "Letter", "Form", "Certificate", "BusinessCard", "Resume", "Flyer"];

    /// <summary>The name to show for a template ("Business Card").</summary>
    public static string Title(string name) => name switch
    {
        "BusinessCard" => "Business Card",
        _ => name,
    };

    /// <summary>A new design laid out as the named template (an empty A4 page for an unknown name).</summary>
    public static DesignDocument Create(string name)
    {
        var doc = new DesignDocument { PageSize = "A4", BgColor = White, Elements = new() };
        switch (name)
        {
            case "Invoice": Invoice(doc); break;
            case "Letter": Letter(doc); break;
            case "BusinessCard": BusinessCard(doc); break;
            case "Certificate": Certificate(doc); break;
            case "Form": Form(doc); break;
            case "Resume": Resume(doc); break;
            case "Flyer": Flyer(doc); break;
        }
        return doc;
    }

    // Colours as the Windows app writes them (its Transparent is white with no alpha).
    private const string Black = "#FF000000", White = "#FFFFFFFF", Transparent = "#00FFFFFF";
    private static string Rgb(byte r, byte g, byte b) => DesignColor.ToHex(255, r, g, b);
    private static string Argb(byte a, byte r, byte g, byte b) => DesignColor.ToHex(a, r, g, b);

    private static DesignItem Text(double x, double y, double w, double h, string text, double size,
        string? color = null, bool bold = false, bool italic = false, string align = "Left") => new()
    {
        Type = "text", X = x, Y = y, W = w, H = h, Text = text, FontFamily = "Segoe UI", FontSize = Math.Max(6, size),
        Bold = bold, Italic = italic, Color = color ?? Black, BgColor = Transparent, Alignment = align, Wrap = true,
    };

    private static DesignItem Shape(string type, double x, double y, double w, double h, string fill, string stroke,
        double thick = 2, double radius = 0)
    {
        // A see-through fill becomes the shape's opacity: PDF fills are drawn without the colour's alpha.
        var f = DesignColor.Parse(fill);
        bool faded = f.A is > 0 and < 255 && DesignColor.Parse(stroke).A == 0;
        return new()
        {
            Type = "shape", ShapeType = type, X = x, Y = y, W = w, H = h, Opacity = faded ? f.A / 255.0 : 1,
            FillColor = faded ? DesignColor.ToHex(255, f.R, f.G, f.B) : fill, StrokeColor = stroke,
            StrokeThick = Math.Max(0.5, thick), CornerRadius = radius,
        };
    }

    private static DesignItem Line(double x, double y, double w, double h, string stroke, double thick) =>
        Shape("Line", x, y, w, h, Transparent, stroke, thick);

    private static void Invoice(DesignDocument d)
    {
        var (w, _) = d.Size;
        var e = d.Elements!;
        e.Add(Text(30, 30, w - 60, 50, "INVOICE", 36, Rgb(30, 80, 160), bold: true));
        e.Add(Text(30, 90, 250, 20, "Your Company Name", 13, Rgb(60, 60, 60), bold: true));
        e.Add(Text(30, 112, 250, 18, "123 Business Street, City, Country", 10, Rgb(100, 100, 100)));
        e.Add(Text(w - 200, 90, 170, 20, "Invoice #: 001", 11, Rgb(60, 60, 60), bold: true, align: "Right"));
        e.Add(Text(w - 200, 112, 170, 18, $"Date: {DateTime.Now:dd MMM yyyy}", 10, Rgb(100, 100, 100), align: "Right"));
        e.Add(Line(30, 145, w - 60, 2, Rgb(30, 80, 160), 2));

        var table = new DesignItem
        {
            Type = "table", X = 30, Y = 165, W = w - 60, H = 200, Rows = 6, Columns = 4,
            BorderColor = Black, HeaderBgColor = Rgb(220, 230, 245), CellBgColor = White, BorderThick = 1,
            Cells =
            [
                ["Description", "Qty", "Unit Price", "Total"],
                ["Service / Product 1", "1", "$100.00", "$100.00"],
                ["Service / Product 2", "2", "$50.00", "$100.00"],
            ],
        };
        table.FitCells();
        e.Add(table);

        e.Add(Text(w - 200, 385, 170, 22, "TOTAL: $200.00", 14, Rgb(30, 80, 160), bold: true, align: "Right"));
        e.Add(Text(30, 440, w - 60, 18, "Payment due within 30 days. Thank you for your business!", 10, Rgb(100, 100, 100)));
    }

    private static void Letter(DesignDocument d)
    {
        var (w, _) = d.Size;
        var e = d.Elements!;
        e.Add(Text(30, 30, 200, 25, "Your Name", 14, bold: true));
        e.Add(Text(30, 56, 200, 18, "Your Address", 10, Rgb(100, 100, 100)));
        e.Add(Text(30, 74, 200, 18, $"{DateTime.Now:dd MMMM yyyy}", 10));
        e.Add(Text(30, 120, 300, 20, "Recipient Name", 12, bold: true));
        e.Add(Text(30, 142, 300, 18, "Company Name", 10));
        e.Add(Text(30, 185, w - 60, 20, "Dear [Recipient Name],", 12));
        e.Add(Text(30, 215, w - 60, 80, "I am writing to you regarding...\n\nPlease feel free to contact me should you have any questions.", 11));
        e.Add(Text(30, 320, 200, 18, "Yours sincerely,", 11));
        e.Add(Text(30, 360, 200, 20, "Your Name", 12, bold: true));
    }

    private static void BusinessCard(DesignDocument d)
    {
        d.PageSize = "Custom";
        d.CustomWidth = 252; d.CustomHeight = 144;
        double w = 252, h = 144;
        var e = d.Elements!;
        e.Add(Shape("Rectangle", 0, 0, w, h, Rgb(25, 65, 140), Transparent));
        e.Add(Shape("Rectangle", 0, 0, 8, h, Rgb(255, 180, 0), Transparent));
        e.Add(Text(24, 28, w - 32, 28, "John Smith", 20, White, bold: true));
        e.Add(Text(24, 58, w - 32, 18, "Senior Designer", 11, Argb(200, 255, 255, 255)));
        e.Add(Line(24, 82, w - 48, 1, Argb(80, 255, 255, 255), 1));
        e.Add(Text(24, 90, w - 32, 16, "john@example.com  |  +1 555 000 0000", 9, Argb(180, 255, 255, 255)));
        e.Add(Text(24, 108, w - 32, 16, "www.yourwebsite.com", 9, Argb(180, 255, 255, 255)));
    }

    private static void Certificate(DesignDocument d)
    {
        d.PageSize = "Letter";
        var (w, h) = d.Size;
        var e = d.Elements!;
        var gold = Rgb(180, 140, 60);
        var brown = Rgb(80, 60, 30);
        e.Add(Shape("Rectangle", 0, 0, w, h, Rgb(253, 248, 230), Transparent));
        e.Add(Shape("Rectangle", 20, 20, w - 40, h - 40, Transparent, gold, 3));
        e.Add(Shape("Rectangle", 26, 26, w - 52, h - 52, Transparent, gold, 1));
        e.Add(Text(40, 60, w - 80, 30, "Certificate of Achievement", 11, Rgb(130, 100, 40), align: "Center"));
        e.Add(Text(40, 120, w - 80, 55, "Certificate of Excellence", 40, Rgb(100, 70, 20), bold: true, align: "Center"));
        e.Add(Text(40, 210, w - 80, 25, "This is to certify that", 14, brown, align: "Center"));
        e.Add(Text(40, 248, w - 80, 40, "Recipient Name", 28, Rgb(30, 80, 160), bold: true, italic: true, align: "Center"));
        e.Add(Line(120, 298, w - 240, 1, gold, 1.5));
        e.Add(Text(40, 310, w - 80, 25, "has successfully completed the requirements for", 12, brown, align: "Center"));
        e.Add(Text(40, 345, w - 80, 30, "Outstanding Performance Award", 16, Rgb(30, 80, 160), bold: true, align: "Center"));
        e.Add(Text(80, 660, 180, 20, "Authorized Signature", 10, brown, align: "Center"));
        e.Add(Text(w - 260, 660, 180, 20, $"Date: {DateTime.Now:dd MMMM yyyy}", 10, brown, align: "Center"));
    }

    private static void Form(DesignDocument d)
    {
        var (w, _) = d.Size;
        var e = d.Elements!;
        e.Add(Text(30, 30, w - 60, 35, "Application Form", 24, Rgb(30, 80, 160), bold: true, align: "Center"));
        e.Add(Line(30, 72, w - 60, 2, Rgb(30, 80, 160), 2));

        foreach (var (label, y) in new[] { ("Full Name", 100), ("Email Address", 150), ("Phone Number", 200), ("Address", 250), ("Date of Birth", 300) })
        {
            e.Add(Text(30, y, 200, 20, label + ":", 11, Rgb(60, 60, 60), bold: true));
            e.Add(Shape("Rectangle", 240, y, w - 270, 22, White, Rgb(180, 180, 180), 1));
        }

        e.Add(Text(30, 365, 200, 20, "Comments:", 11, Rgb(60, 60, 60), bold: true));
        e.Add(Shape("Rectangle", 30, 390, w - 60, 80, White, Rgb(180, 180, 180), 1));
    }

    private static void Resume(DesignDocument d)
    {
        var (w, h) = d.Size;
        var e = d.Elements!;
        var navy = Rgb(35, 55, 90);
        var gold = Rgb(200, 180, 100);

        // Left sidebar and photo placeholder
        e.Add(Shape("Rectangle", 0, 0, 175, h, navy, Transparent));
        e.Add(Shape("Ellipse", 37, 30, 100, 100, Argb(60, 255, 255, 255), Argb(120, 255, 255, 255), 2));
        e.Add(Text(10, 145, 155, 28, "Your Name", 18, White, bold: true, align: "Center"));
        e.Add(Text(10, 175, 155, 18, "UX / Product Designer", 10, Argb(180, 255, 255, 255), align: "Center"));
        e.Add(Line(20, 204, 135, 1, Argb(80, 255, 255, 255), 1));

        // Sidebar section headers
        foreach (var (text, y) in new[] { ("CONTACT", 218), ("SKILLS", 340), ("LANGUAGES", 480) })
            e.Add(Text(14, y, 148, 16, text, 8, gold, bold: true));

        e.Add(Text(14, 240, 148, 72, "you@example.com\n+1 555 000 0000\nlinkedin.com/in/you\nCity, Country", 9, Argb(200, 255, 255, 255)));

        var skills = new[] { "Figma", "Adobe XD", "Prototyping", "User Research" };
        for (int i = 0; i < skills.Length; i++)
        {
            e.Add(Text(14, 360 + i * 24, 100, 18, skills[i], 9, Argb(220, 255, 255, 255)));
            e.Add(Shape("Rectangle", 14, 375 + i * 24, 148, 5, Argb(40, 255, 255, 255), Transparent));
            e.Add(Shape("Rectangle", 14, 375 + i * 24, (float)(148 * (0.95 - i * 0.12)), 5, gold, Transparent));
        }

        // Main content area
        e.Add(Text(195, 30, w - 215, 30, "PROFESSIONAL SUMMARY", 10, navy, bold: true));
        e.Add(Line(195, 62, w - 215, 1, navy, 1.5));
        e.Add(Text(195, 70, w - 215, 55, "Passionate designer with 5+ years of experience crafting intuitive digital products. Focused on user-centered design and delivering measurable results.", 10, Rgb(60, 60, 60)));

        e.Add(Text(195, 145, w - 215, 24, "EXPERIENCE", 10, navy, bold: true));
        e.Add(Line(195, 170, w - 215, 1, navy, 1.5));
        foreach (var (company, role, dates, y2) in new[] {
            ("Acme Corp", "Lead UX Designer", "2021–Present", 180),
            ("Beta Studio", "Product Designer", "2018–2021", 250) })
        {
            e.Add(Text(195, y2, w - 215, 18, role, 11, Rgb(40, 40, 40), bold: true));
            e.Add(Text(195, y2 + 18, 200, 16, company, 9, navy));
            e.Add(Text(w - 215 - 80, y2 + 18, 80, 16, dates, 9, Rgb(120, 120, 120), align: "Right"));
            e.Add(Text(195, y2 + 36, w - 215, 36, "• Designed and iterated on key product features\n• Led cross-functional design sprints", 9, Rgb(80, 80, 80)));
        }
    }

    private static void Flyer(DesignDocument d)
    {
        var (w, h) = d.Size;
        var e = d.Elements!;
        var cyan = Rgb(0, 200, 255);
        var ink = Rgb(15, 15, 35);

        e.Add(Shape("Rectangle", 0, 0, w, h, ink, Transparent));
        // Accent circles
        e.Add(Shape("Ellipse", -60, -60, 240, 240, Argb(60, 0, 180, 255), Transparent));
        e.Add(Shape("Ellipse", w - 120, h - 160, 200, 200, Argb(50, 255, 80, 180), Transparent));

        e.Add(Text(30, 80, w - 60, 30, "SPECIAL EVENT", 12, cyan, bold: true, align: "Center"));
        e.Add(Text(30, 110, w - 60, 150, "AMAZING\nCONFERENCE\n2026", 38, White, bold: true, align: "Center"));
        e.Add(Shape("Rectangle", w / 2 - 45, 280, 90, 4, cyan, Transparent));

        e.Add(Text(30, 298, w - 60, 30, "The Future of Technology & Innovation", 14, Argb(200, 255, 255, 255), italic: true, align: "Center"));
        e.Add(Text(30, 330, w - 60, 25, "15–17 June 2026  ·  Convention Center, New York", 12, Argb(200, 255, 255, 255), align: "Center"));

        e.Add(Shape("Rectangle", w / 2 - 90, 400, 180, 44, cyan, Transparent, radius: 22));
        e.Add(Text(w / 2 - 90, 412, 180, 24, "REGISTER NOW", 13, ink, bold: true, align: "Center"));

        e.Add(Text(30, h - 80, w - 60, 20, "www.amazingconf.example.com", 11, Argb(160, 255, 255, 255), align: "Center"));
    }
}
