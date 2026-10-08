namespace PdfEdit.Templates;

// Flyers, posters, menus, newsletters, brochures and invitations; certificates, cards and tickets.
public static partial class TemplateCatalog
{
    private static IEnumerable<PdfTemplate> MarketingTemplates() =>
    [
        new("Flyer", "Event flyer (dark)", Marketing, "A dark conference flyer with a call to action.", () => ClassicTemplates.Create("Flyer")) { Tags = ["event", "conference"] },
        T("SaleFlyer", "Sale flyer", Marketing, "A big, bright sale announcement.", SaleFlyer, tags: ["promotion", "discount", "shop", "retail"]),
        T("EventPoster", "Event poster", Marketing, "A music or community event poster with date, venue and tickets.", EventPoster,
            tags: ["concert", "festival", "gig", "party"]),
        T("Menu", "Restaurant menu", Marketing, "Starters, mains and desserts with prices.", Menu, tags: ["food", "cafe", "restaurant", "dinner"]),
        T("Newsletter", "Newsletter", Marketing, "A newsletter front page with a lead story and sidebar.", Newsletter,
            tags: ["bulletin", "update", "news"]),
        T("Brochure", "Tri-fold brochure", Marketing, "A landscape page laid out in three panels to fold.", Brochure,
            landscape: true, tags: ["leaflet", "pamphlet", "trifold"]),
        T("Invitation", "Party invitation", Marketing, "An invitation with RSVP details.", Invitation,
            tags: ["party", "birthday", "wedding", "celebration", "rsvp"]),
        T("Announcement", "Announcement poster", Marketing, "A clean poster for notices and announcements.", Announcement,
            tags: ["notice", "sign", "poster"]),
        T("RealEstate", "Property flyer", Marketing, "A property listing with photo space, features and agent details.", RealEstate,
            tags: ["house", "home", "for sale", "rent", "listing"]),
    ];

    private static IEnumerable<PdfTemplate> CardTemplates() =>
    [
        new("Certificate", "Certificate of excellence", Cards, "A classic gold-bordered certificate.", () => ClassicTemplates.Create("Certificate")) { Tags = ["award"] },
        T("Award", "Award certificate", Cards, "A modern landscape award with signatures.", AwardCertificate, landscape: true,
            tags: ["certificate", "achievement", "recognition", "diploma"]),
        T("CompletionCert", "Certificate of completion", Cards, "For courses and training, landscape.", CompletionCertificate, landscape: true,
            tags: ["course", "training", "certificate"]),
        T("GiftCertificate", "Gift certificate", Cards, "A gift voucher with value, recipient and expiry.", k => GiftCertificate(k),
            tags: ["voucher", "gift card"], fillable: true),
        new("BusinessCard", "Business card", Cards, "A business card (85 × 54 mm).", () => ClassicTemplates.Create("BusinessCard")) { Tags = ["contact"] },
        new("ThankYou", "Thank-you card", Cards, "A folded A5-style thank-you card (front and inside).",
            () => Kit.Custom(595, 420, ThankYou)) { Tags = ["card", "gratitude", "note"] },
        new("Ticket", "Event ticket", Cards, "An admission ticket with stub.", () => Kit.Custom(560, 200, Ticket)) { Tags = ["admission", "pass", "raffle"] },
        new("NameBadge", "Name badges", Cards, "Six name badges on a page to print and cut.", () => Kit.Build("A4", false, NameBadges))
            { Tags = ["badge", "name tag", "conference"] },
    ];

    private static void SaleFlyer(Kit k)
    {
        k.Accent = Kit.Rgb(220, 38, 38);
        double m = 40, w = k.W - 2 * m;
        k.Background(Kit.Rgb(254, 243, 199));
        k.Circle(k.W / 2 - 210, 120, 420, 420, k.Accent);
        k.Text(m, 70, w, 24, "LIMITED TIME ONLY", 16, k.Accent, bold: true, align: "Center");
        k.Text(m, 210, w, 70, "BIG", 64, Kit.White, bold: true, align: "Center");
        k.Text(m, 280, w, 70, "SALE", 64, Kit.White, bold: true, align: "Center");
        k.Text(m, 366, w, 50, "UP TO 50% OFF", 28, Kit.Rgb(254, 243, 199), bold: true, align: "Center");
        k.Text(m, 560, w, 22, "This weekend only  ·  Friday – Sunday", 15, Kit.Ink, bold: true, align: "Center");
        k.Para(m + 40, 594, w - 80, "Shop store-wide savings on our best-selling products. While stocks last.", 12, Kit.Body, "Center");
        k.Box(k.W / 2 - 110, 660, 220, 46, Kit.Ink, radius: 23);
        k.Text(k.W / 2 - 110, 674, 220, 20, "SHOP NOW", 15, Kit.White, bold: true, align: "Center");
        k.Text(m, k.H - 60, w, 30, "Store name  ·  123 High Street  ·  yourstore.com", 10, Kit.Muted, align: "Center");
    }

    private static void EventPoster(Kit k)
    {
        k.Accent = Kit.Rgb(250, 204, 21);
        double m = 44, w = k.W - 2 * m;
        k.Background(Kit.Rgb(30, 27, 75));
        for (int i = 0; i < 8; i++) k.Box(0, 120 + i * 26, k.W, 12, Kit.Alpha(Kit.Rgb(139, 92, 246), (byte)(30 + i * 12)));
        k.Text(m, 60, w, 20, "LIVE MUSIC  ·  ONE NIGHT ONLY", 12, k.Accent, bold: true);
        k.Text(m, 340, w, 140, "SUMMER\nNIGHTS", 60, Kit.White, bold: true);
        k.Box(m, 520, 80, 6, k.Accent);
        k.Text(m, 540, w, 26, "with The Headliners + special guests", 15, Kit.Alpha(Kit.White, 220));
        double y = 610;
        string[][] info = [["DATE", "Saturday 18 July"], ["DOORS", "7:00 pm"], ["VENUE", "City Park Stage"]];
        for (int i = 0; i < 3; i++)
        {
            k.Text(m + i * (w / 3), y, w / 3 - 10, 14, info[i][0], 9, k.Accent, bold: true);
            k.Text(m + i * (w / 3), y + 16, w / 3 - 10, 20, info[i][1], 14, Kit.White, bold: true);
        }
        k.Box(m, 700, w, 50, k.Accent, radius: 6);
        k.Text(m, 716, w, 20, "TICKETS FROM $25  ·  yourevent.com", 15, Kit.Rgb(30, 27, 75), bold: true, align: "Center");
    }

    private static void Menu(Kit k)
    {
        k.Accent = Kit.Rgb(146, 64, 14);
        double m = 64, w = k.W - 2 * m;
        k.Background(Kit.Rgb(255, 251, 235));
        k.Box(28, 28, k.W - 56, k.H - 56, Kit.Clear, k.Accent, 1.5);
        k.Text(m, 64, w, 40, "The Corner Bistro", 30, k.Accent, bold: true, align: "Center");
        k.Text(m, 106, w, 16, "SEASONAL KITCHEN  ·  EST. 2010", 9.5, Kit.Muted, bold: true, align: "Center");
        double y = 150;
        (string Title, string[] Items)[] courses =
        [
            ("Starters", ["Soup of the day|Fresh bread and butter|6.50", "Burrata|Heritage tomatoes, basil oil|9.00", "Crispy calamari|Lemon aioli|8.50"]),
            ("Mains", ["Roast chicken|Garlic potatoes, greens, jus|18.00", "Market fish|Crushed potatoes, salsa verde|21.00", "Wild mushroom risotto|Parmesan, truffle oil (v)|16.00", "Steak frites|Peppercorn sauce|24.00"]),
            ("Desserts", ["Sticky toffee pudding|Vanilla ice cream|7.50", "Lemon tart|Crème fraîche|7.00", "Cheese board|Chutney and crackers|10.00"]),
        ];
        foreach (var (title, items) in courses)
        {
            k.Text(m, y, w, 22, title, 16, k.Accent, bold: true, italic: true, align: "Center");
            k.HRule(k.W / 2 - 30, y + 26, 60, k.Accent, 1);
            y += 40;
            foreach (var it in items)
            {
                var p = it.Split('|');
                k.Text(m, y, w - 60, 16, p[0], 11.5, Kit.Ink, bold: true);
                k.Text(k.W - m - 60, y, 60, 16, p[2], 11.5, k.Accent, bold: true, align: "Right");
                k.Text(m, y + 16, w - 60, 14, p[1], 9.5, Kit.Muted, italic: true);
                y += 40;
            }
            y += 12;
        }
        k.Text(m, k.H - 72, w, 14, "Please tell us about any allergies  ·  (v) vegetarian", 9, Kit.Muted, align: "Center");
    }

    private static void Newsletter(Kit k)
    {
        k.Accent = Kit.Rgb(37, 99, 235);
        double m = 36, w = k.W - 2 * m;
        k.Text(m, 30, w, 44, "The Monthly Update", 30, Kit.Ink, bold: true);
        k.Text(m, 76, w, 14, $"ISSUE 12  ·  {DateTime.Today:MMMM yyyy}".ToUpperInvariant(), 9, k.Accent, bold: true);
        k.HRule(m, 96, w, Kit.Ink, 2);
        double col = w * 0.64, sx = m + col + 20, sw = w - col - 20;
        k.Box(m, 112, col, 200, Kit.Tint(k.Accent, 0.85), radius: 4);
        k.Text(m, 200, col, 20, "Add a photo here", 11, k.Accent, align: "Center");
        k.Text(m, 324, col, 50, "Lead story headline that draws readers in", 19, Kit.Ink, bold: true);
        k.Para(m, 378, col, "Write the main story of this issue here. Start with what's new and why it matters, then add details, quotes and what's coming next.\n\nKeep paragraphs short so the page is easy to scan.", 10);
        k.HRule(m, 500, col);
        k.Text(m, 512, col, 22, "Second story", 14, Kit.Ink, bold: true);
        k.Para(m, 536, col, "A shorter piece: an achievement, a new team member, or an upcoming change.", 10);
        k.Text(m, 600, col, 22, "Third story", 14, Kit.Ink, bold: true);
        k.Para(m, 624, col, "Another update, a tip of the month, or a customer story.", 10);

        k.Box(sx, 112, sw, 560, Kit.Fill, radius: 4);
        k.Text(sx + 12, 126, sw - 24, 16, "IN THIS ISSUE", 9.5, k.Accent, bold: true);
        k.Para(sx + 12, 146, sw - 24, "Lead story ........ 1\nSecond story ...... 1\nThird story ........ 1\nDates ............... 2", 9.5);
        k.Text(sx + 12, 240, sw - 24, 16, "DATES FOR YOUR DIARY", 9.5, k.Accent, bold: true);
        foreach (var (d, e, i) in new[] { ("12", "Team social", 0), ("19", "Quarterly review", 1), ("26", "Workshop", 2) })
        {
            k.Box(sx + 12, 264 + i * 46, 34, 34, k.Accent, radius: 4);
            k.Text(sx + 12, 272 + i * 46, 34, 18, d, 14, Kit.White, bold: true, align: "Center");
            k.Text(sx + 54, 273 + i * 46, sw - 66, 16, e, 10, Kit.Ink, bold: true);
        }
        k.Text(sx + 12, 420, sw - 24, 16, "QUOTE OF THE MONTH", 9.5, k.Accent, bold: true);
        k.Para(sx + 12, 440, sw - 24, "\"Alone we can do so little; together we can do so much.\"\n— Helen Keller", 10, Kit.Body, italic: true);
        k.Text(m, k.H - 44, w, 14, "Your Organisation  ·  newsletter@example.com", 8.5, Kit.Muted, align: "Center");
    }

    private static void Brochure(Kit k)
    {
        k.Accent = Kit.Rgb(5, 150, 105);
        double pw = k.W / 3, pad = 24;
        k.Box(pw * 2, 0, pw, k.H, k.Accent);
        for (int i = 1; i < 3; i++) k.VRule(pw * i, 0, k.H, Kit.Alpha(Kit.Faint, 120), 0.5);
        // Inside left: about us.
        k.Text(pad, 40, pw - 2 * pad, 22, "About us", 17, k.Accent, bold: true);
        k.Para(pad, 70, pw - 2 * pad, "Tell readers who you are, what you do and what makes you different. Two or three short paragraphs work best.\n\nAdd a photo, a key number or a short testimonial to bring it to life.", 9.5);
        k.Box(pad, 260, pw - 2 * pad, 130, Kit.Tint(k.Accent, 0.85), radius: 4);
        k.Text(pad, 318, pw - 2 * pad, 16, "Photo", 10, k.Accent, align: "Center");
        k.Para(pad, 410, pw - 2 * pad, "\"A short quote from a happy customer.\"\n— Customer name", 9.5, Kit.Body, italic: true);
        // Middle: services.
        double x = pw + pad;
        k.Text(x, 40, pw - 2 * pad, 22, "What we offer", 17, k.Accent, bold: true);
        string[] services = ["Service one", "Service two", "Service three", "Service four"];
        for (int i = 0; i < services.Length; i++)
        {
            k.Circle(x, 84 + i * 100, 26, 26, Kit.Tint(k.Accent, 0.75));
            k.Text(x, 89 + i * 100, 26, 16, (i + 1).ToString(), 11, k.Accent, bold: true, align: "Center");
            k.Text(x + 36, 84 + i * 100, pw - 2 * pad - 36, 16, services[i], 11.5, Kit.Ink, bold: true);
            k.Para(x + 36, 102 + i * 100, pw - 2 * pad - 36, "A sentence or two describing this service and its benefit.", 9);
        }
        // Right (front cover).
        x = pw * 2 + pad;
        k.Text(x, 150, pw - 2 * pad, 120, "Your\nBrochure\nTitle", 28, Kit.White, bold: true);
        k.Box(x, 280, 50, 4, Kit.White);
        k.Para(x, 296, pw - 2 * pad, "A short line about what's inside.", 11, Kit.Alpha(Kit.White, 220));
        k.Text(x, k.H - 80, pw - 2 * pad, 50, "Your Company\nyourcompany.com\n+1 555 000 0000", 9.5, Kit.Alpha(Kit.White, 220));
    }

    private static void Invitation(Kit k)
    {
        k.Accent = Kit.Rgb(190, 24, 93);
        double m = 70, w = k.W - 2 * m;
        k.Background(Kit.Rgb(253, 242, 248));
        k.Circle(-80, -80, 260, 260, Kit.Alpha(k.Accent, 40));
        k.Circle(k.W - 170, k.H - 200, 280, 280, Kit.Alpha(k.Accent, 30));
        k.Box(m - 20, 120, w + 40, 560, Kit.White, Kit.Tint(k.Accent, 0.6), 1, 8);
        k.Text(m, 170, w, 18, "YOU'RE INVITED", 13, k.Accent, bold: true, align: "Center");
        k.Text(m, 214, w, 40, "to celebrate", 20, Kit.Muted, italic: true, align: "Center");
        k.Text(m, 258, w, 90, "Sam's 30th\nBirthday", 38, Kit.Ink, bold: true, align: "Center");
        k.HRule(k.W / 2 - 40, 372, 80, k.Accent, 2);
        k.Text(m, 400, w, 22, "Saturday, 12 September", 16, Kit.Ink, bold: true, align: "Center");
        k.Text(m, 426, w, 20, "7:30 pm until late", 13, Kit.Body, align: "Center");
        k.Text(m, 466, w, 40, "The Garden Room\n42 Example Street, City", 12, Kit.Body, align: "Center");
        k.Text(m, 560, w, 16, "Kindly RSVP by 1 September", 11, k.Accent, bold: true, align: "Center");
        k.Text(m, 580, w, 16, "Alex · +1 555 000 0000", 11, Kit.Body, align: "Center");
    }

    private static void Announcement(Kit k)
    {
        k.Accent = Kit.Rgb(234, 88, 12);
        double m = 56, w = k.W - 2 * m;
        k.Box(0, 0, k.W, 18, k.Accent);
        k.Box(0, k.H - 18, k.W, 18, k.Accent);
        k.Text(m, 120, w, 20, "PLEASE NOTE", 14, k.Accent, bold: true, align: "Center");
        k.Text(m, 160, w, 130, "Office Closed\nfor Maintenance", 40, Kit.Ink, bold: true, align: "Center");
        k.HRule(k.W / 2 - 50, 312, 100, k.Accent, 3);
        k.Text(m, 340, w, 24, "Monday 4 – Wednesday 6 May", 18, Kit.Body, bold: true, align: "Center");
        k.Para(m + 30, 390, w - 60, "We're upgrading our building to serve you better. Our team is still available by phone and email during this time. We apologise for any inconvenience.", 13, Kit.Body, "Center");
        k.Box(m + 60, 520, w - 120, 80, Kit.Tint(k.Accent, 0.88), radius: 8);
        k.Text(m + 60, 538, w - 120, 50, "Questions? Call +1 555 000 0000\nor email hello@yourcompany.com", 12, Kit.Ink, bold: true, align: "Center");
    }

    private static void RealEstate(Kit k)
    {
        k.Accent = Kit.Rgb(15, 118, 110);
        double m = 36, w = k.W - 2 * m;
        k.Box(m, 36, w, 300, Kit.Tint(k.Accent, 0.85), radius: 4);
        k.Text(m, 176, w, 20, "Add a photo of the property", 13, k.Accent, align: "Center");
        k.Box(m, 36, 130, 34, k.Accent);
        k.Text(m, 45, 130, 18, "FOR SALE", 13, Kit.White, bold: true, align: "Center");
        k.Text(m, 356, w - 160, 30, "4 Bedroom Family Home", 22, Kit.Ink, bold: true);
        k.Text(m, 388, w - 160, 16, "12 Example Road, Town, Postcode", 11, Kit.Muted);
        k.Text(k.W - m - 160, 356, 160, 30, "$450,000", 22, k.Accent, bold: true, align: "Right");
        string[][] facts = [["4", "Bedrooms"], ["2", "Bathrooms"], ["1,850", "Sq ft"], ["2", "Car garage"]];
        double fw = w / 4;
        for (int i = 0; i < 4; i++)
        {
            k.Box(m + i * fw + 4, 420, fw - 8, 60, Kit.Fill, radius: 4);
            k.Text(m + i * fw + 4, 430, fw - 8, 22, facts[i][0], 18, k.Accent, bold: true, align: "Center");
            k.Text(m + i * fw + 4, 456, fw - 8, 14, facts[i][1], 9, Kit.Muted, align: "Center");
        }
        k.Text(m, 500, w, 18, "Property features", 13, Kit.Ink, bold: true);
        k.Para(m, 522, w / 2 - 10, "• Open-plan kitchen and dining\n• Large south-facing garden\n• Recently renovated bathrooms\n• Close to schools and transport", 10);
        k.Para(m + w / 2, 522, w / 2, "• Home office\n• Off-street parking\n• Energy rating B\n• No onward chain", 10);
        k.Box(m, k.H - 130, w, 90, k.Accent, radius: 6);
        k.Circle(m + 20, k.H - 115, 60, 60, Kit.Alpha(Kit.White, 70));
        k.Text(m + 96, k.H - 112, w - 110, 20, "Agent Name", 14, Kit.White, bold: true);
        k.Text(m + 96, k.H - 90, w - 110, 34, "Your Agency  ·  +1 555 000 0000\nagent@youragency.com", 10, Kit.Alpha(Kit.White, 220));
    }

    private static void AwardCertificate(Kit k)
    {
        k.Accent = Kit.Rgb(29, 78, 216);
        string gold = Kit.Rgb(202, 138, 4);
        double m = 60, w = k.W - 2 * m;
        k.Box(0, 0, 110, k.H, k.Accent);
        k.Box(110, 0, 8, k.H, gold);
        k.Circle(20, k.H / 2 - 45, 90, 90, gold);
        k.Circle(30, k.H / 2 - 35, 70, 70, Kit.Clear, Kit.White, 1.5);
        k.Text(20, k.H / 2 - 8, 90, 16, "AWARD", 11, Kit.White, bold: true, align: "Center");
        double x = 160, cw = k.W - x - m;
        k.Text(x, 80, cw, 20, "CERTIFICATE", 14, gold, bold: true);
        k.Text(x, 102, cw, 50, "of Achievement", 36, Kit.Ink, bold: true);
        k.Text(x, 176, cw, 18, "This certificate is proudly presented to", 13, Kit.Muted);
        k.Text(x, 206, cw, 50, "Recipient Name", 38, k.Accent, bold: true, italic: true);
        k.HRule(x, 262, cw * 0.8, gold, 1.5);
        k.Para(x, 280, cw * 0.85, "In recognition of outstanding dedication, hard work and exceptional results throughout the year.", 12, Kit.Body);
        foreach (var (label, i) in new[] { ("Presented by", 0), ("Date", 1) })
        {
            double sx = x + i * (cw / 2);
            k.HRule(sx, k.H - 110, cw / 2 - 40, Kit.Ink, 0.75);
            k.Text(sx, k.H - 104, cw / 2 - 40, 14, label, 10, Kit.Muted);
        }
        k.Text(x + cw / 2, k.H - 128, cw / 2 - 40, 16, DateTime.Today.ToString("d MMMM yyyy"), 11, Kit.Ink);
    }

    private static void CompletionCertificate(Kit k)
    {
        k.Accent = Kit.Rgb(17, 94, 89);
        string gold = Kit.Rgb(180, 140, 60);
        double m = 50, w = k.W - 2 * m;
        k.Background(Kit.Rgb(250, 250, 245));
        k.Box(24, 24, k.W - 48, k.H - 48, Kit.Clear, k.Accent, 6);
        k.Box(38, 38, k.W - 76, k.H - 76, Kit.Clear, gold, 1);
        k.Text(m, 80, w, 40, "Certificate of Completion", 32, k.Accent, bold: true, align: "Center");
        k.Text(m, 150, w, 18, "This is to certify that", 13, Kit.Muted, italic: true, align: "Center");
        k.Text(m, 180, w, 44, "Participant Name", 32, Kit.Ink, bold: true, align: "Center");
        k.HRule(k.W / 2 - 180, 230, 360, gold, 1);
        k.Text(m, 248, w, 18, "has successfully completed the course", 13, Kit.Muted, italic: true, align: "Center");
        k.Text(m, 276, w, 30, "Course Title", 22, k.Accent, bold: true, align: "Center");
        k.Text(m, 312, w, 16, "12 hours  ·  Completed " + DateTime.Today.ToString("d MMMM yyyy"), 11, Kit.Body, align: "Center");
        k.Circle(k.W / 2 - 40, k.H - 170, 80, 80, gold);
        k.Text(k.W / 2 - 40, k.H - 140, 80, 20, "SEAL", 12, Kit.White, bold: true, align: "Center");
        foreach (var (label, x) in new[] { ("Instructor", m + 40), ("Director", k.W - m - 240) })
        {
            k.HRule(x, k.H - 110, 200, Kit.Ink, 0.75);
            k.Text(x, k.H - 104, 200, 14, label, 10, Kit.Muted, align: "Center");
        }
    }

    private static void GiftCertificate(Kit k)
    {
        k.Accent = Kit.Rgb(159, 18, 57);
        double m = 40, w = k.W - 2 * m, h = 300;
        for (int c = 0; c < 2; c++)
        {
            double y = 40 + c * (h + 60);
            k.Box(m, y, w, h, Kit.White, k.Accent, 2, 10);
            k.Box(m, y, 150, h, k.Accent, radius: 10);
            k.Box(m + 140, y, 10, h, k.Accent);
            k.Text(m, y + 110, 150, 80, "GIFT\nCERTIFICATE", 18, Kit.White, bold: true, align: "Center");
            double x = m + 176, cw = w - 200;
            k.Text(x, y + 26, cw, 16, "Your Shop Name", 13, k.Accent, bold: true);
            k.Text(x, y + 52, cw, 40, "$50", 34, Kit.Ink, bold: true);
            string p = "Gift" + (c + 1);
            k.LineInput("To:", p + "To", x, y + 110, cw, 50);
            k.LineInput("From:", p + "From", x, y + 142, cw, 50);
            k.LineInput("Value:", p + "Value", x, y + 174, cw / 2 - 10, 50);
            k.LineInput("Expires:", p + "Expires", x + cw / 2, y + 174, cw / 2, 56);
            k.LineInput("No.:", p + "Number", x, y + 206, cw / 2 - 10, 50);
            k.Text(x, y + h - 40, cw, 24, "Redeemable in store or online. Not exchangeable for cash.", 8.5, Kit.Muted);
        }
        k.Text(m, 40 + h + 22, w, 14, "- - - - - - - - - - - - - - - - - - - - cut here - - - - - - - - - - - - - - - - - - - -", 8, Kit.Faint, align: "Center");
    }

    private static void ThankYou(Kit k)
    {
        k.Accent = Kit.Rgb(234, 88, 12);
        double half = k.W / 2;
        k.VRule(half, 0, k.H, Kit.Rule, 0.5);
        // Inside (left), front (right).
        k.Para(40, 120, half - 80, "Dear ______________,\n\nThank you so much for ...\n\n\n\nWith love,\n______________", 12, Kit.Body);
        k.Box(half, 0, half, k.H, Kit.Tint(k.Accent, 0.88));
        k.Circle(half + half / 2 - 60, 90, 120, 120, k.Accent);
        k.Circle(half + half / 2 - 40, 110, 80, 80, Kit.Clear, Kit.White, 2);
        k.Text(half + 20, 240, half - 40, 50, "Thank You", 34, k.Accent, bold: true, italic: true, align: "Center");
        k.Text(half + 20, 296, half - 40, 20, "for everything", 13, Kit.Body, align: "Center");
    }

    private static void Ticket(Kit k)
    {
        k.Accent = Kit.Rgb(126, 34, 206);
        double stub = 140;
        k.Box(0, 0, k.W - stub, k.H, k.Accent);
        k.Circle(k.W - stub - 120, -60, 220, 220, Kit.Alpha(Kit.White, 30));
        k.Text(24, 22, 300, 14, "ADMIT ONE", 10, Kit.Rgb(250, 204, 21), bold: true);
        k.Text(24, 42, k.W - stub - 48, 70, "Event Name\nGoes Here", 26, Kit.White, bold: true);
        k.Text(24, 128, 120, 14, "DATE", 8, Kit.Alpha(Kit.White, 180), bold: true);
        k.Text(24, 142, 120, 18, "Sat 18 July", 12, Kit.White, bold: true);
        k.Text(150, 128, 120, 14, "TIME", 8, Kit.Alpha(Kit.White, 180), bold: true);
        k.Text(150, 142, 120, 18, "7:30 pm", 12, Kit.White, bold: true);
        k.Text(260, 128, 140, 14, "VENUE", 8, Kit.Alpha(Kit.White, 180), bold: true);
        k.Text(260, 142, 160, 18, "City Hall", 12, Kit.White, bold: true);
        k.Box(k.W - stub, 0, stub, k.H, Kit.Tint(k.Accent, 0.88));
        for (int i = 0; i < 12; i++) k.Box(k.W - stub - 1, 8 + i * 16, 2, 8, Kit.White);
        k.Text(k.W - stub + 10, 30, stub - 20, 14, "SEAT", 8, k.Accent, bold: true, align: "Center");
        k.Text(k.W - stub + 10, 46, stub - 20, 30, "A12", 24, Kit.Ink, bold: true, align: "Center");
        k.Text(k.W - stub + 10, 100, stub - 20, 14, "No. 000123", 10, Kit.Muted, align: "Center");
        k.Text(k.W - stub + 10, 150, stub - 20, 14, "$25.00", 12, k.Accent, bold: true, align: "Center");
    }

    private static void NameBadges(Kit k)
    {
        k.Accent = Kit.Rgb(2, 132, 199);
        double bw = 243, bh = 153, gx = (k.W - 2 * bw) / 3, gy = 40;
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 2; c++)
            {
                double x = gx + c * (bw + gx), y = gy + r * (bh + 40);
                k.Box(x, y, bw, bh, Kit.White, Kit.Rule, 0.75, 8);
                k.Box(x, y, bw, 38, k.Accent, radius: 8);
                k.Box(x, y + 28, bw, 10, k.Accent);
                k.Text(x, y + 11, bw, 16, "HELLO, MY NAME IS", 10, Kit.White, bold: true, align: "Center");
                k.Field("Text", $"Name{r * 2 + c + 1}", x + 14, y + 56, bw - 28, 34, "Name", fontSize: 20);
                k.Field("Text", $"Org{r * 2 + c + 1}", x + 14, y + 100, bw - 28, 18, "Organisation", fontSize: 11);
                k.Text(x, y + bh - 22, bw, 12, "Event name · Date", 8, Kit.Muted, align: "Center");
            }
    }
}
