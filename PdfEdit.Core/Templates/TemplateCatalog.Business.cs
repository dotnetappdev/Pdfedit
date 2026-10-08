namespace PdfEdit.Templates;

// Invoices, quotes, receipts, purchase orders, price lists, letterheads, proposals and reports.
public static partial class TemplateCatalog
{
    private static IEnumerable<PdfTemplate> BusinessTemplates() =>
    [
        new("Invoice", "Invoice (classic)", Business, "A clean invoice with a line-item table and total.",
            () => ClassicTemplates.Create("Invoice")) { Tags = ["bill", "payment"] },
        T("InvoiceModern", "Invoice", Business, "Fillable invoice: bill to, line items, tax, total due and payment details.",
            k => SalesDocument(k, "INVOICE", "Invoice #", "Due date", "Total due", "Payment details",
                "Bank: Your Bank  ·  Account: 00000000  ·  Sort code: 00-00-00\nPlease include the invoice number with your payment."),
            tags: ["bill", "payment", "billing"], fillable: true),
        T("Quote", "Quote / estimate", Business, "A priced quote with validity date, line items and acceptance signature.",
            k =>
            {
                k.Accent = Kit.Rgb(13, 110, 98);
                double y = SalesDocument(k, "QUOTE", "Quote #", "Valid until", "Total", "Terms",
                    "This quote is valid until the date shown. Prices exclude any items not listed.");
                k.SignatureLine("Accepted by (signature)", "AcceptedSignature", k.M, y + 10, 220);
                k.Input("Date", "AcceptedDate", k.W - k.M - 160, y + 10, 160);
            }, tags: ["estimate", "proposal", "pricing"], fillable: true),
        T("Receipt", "Payment receipt", Business, "A receipt for a payment received, with method and balance.", Receipt,
            tags: ["paid", "payment", "sales"], fillable: true),
        T("PurchaseOrder", "Purchase order", Business, "Vendor, ship-to, ordered items, totals and approval.", PurchaseOrder,
            tags: ["po", "order", "procurement", "vendor"], fillable: true),
        T("PriceList", "Price list", Business, "Products or services with prices, in sections.", PriceList,
            tags: ["catalogue", "rates", "menu", "services"]),
        T("Letterhead", "Letterhead", Business, "Company letterhead with a logo mark, contact strip and letter body.", Letterhead,
            tags: ["stationery", "company"]),
        T("ProposalCover", "Business proposal cover", Business, "A bold cover page for a proposal or pitch.", ProposalCover,
            tags: ["pitch", "cover page"]),
        T("ReportCover", "Report cover", Business, "A cover page for an annual or project report.", ReportCover,
            tags: ["annual report", "cover page"]),
        T("StatusReport", "Project status report", Business, "One-page project status: health, milestones, risks and next steps.",
            StatusReport, tags: ["project", "update", "weekly"], fillable: true),
    ];

    /// <summary>
    /// The shared layout of invoices and quotes: company, document title and numbers, bill-to, a
    /// fillable line-item grid, totals and notes. Returns the y below the notes.
    /// </summary>
    private static double SalesDocument(Kit k, string title, string numberLabel, string dateLabel2, string totalLabel,
        string notesTitle, string notes)
    {
        double m = k.M, w = k.W - 2 * m;
        k.Box(0, 0, k.W, 8, k.Accent);
        k.Box(m, 34, 34, 34, k.Accent, radius: 6);
        k.Text(m, 41, 34, 20, "Co", 13, Kit.White, bold: true, align: "Center");
        k.Text(m + 44, 32, 240, 22, "Your Company", 16, Kit.Ink, bold: true);
        k.Text(m + 44, 54, 240, 30, "123 Business Street, City, Postcode\nhello@yourcompany.com  ·  +1 555 000 0000", 8.5, Kit.Muted);
        k.Text(k.W - m - 220, 30, 220, 34, title, 26, k.Accent, bold: true, align: "Right");

        double rx = k.W - m - 220;
        k.Input(numberLabel, "Number", rx, 72, 105);
        k.Input("Date", "Date", rx + 115, 72, 105);
        k.Input(dateLabel2, "DueDate", rx + 115, 112, 105);

        k.Label(m, 112, 200, "Bill to", k.Accent);
        k.Box(m, 124, 240, 62, Kit.FieldFill, Kit.Rule, 0.75, 2);
        k.Field("Memo", "BillTo", m + 3, 126, 234, 58, "Name, company and address");

        double y = k.Grid(m, 210, w, ["Description", "Qty", "Unit price", "Amount"], [5, 1, 1.6, 1.6], 10, 22, "Item",
            align: ["Left", "Center", "Right", "Right"]);
        double ty = k.Totals(k.W - m, y + 10, "Total", "Subtotal", "Tax", "Discount", totalLabel);

        k.Label(m, y + 12, w - 240, notesTitle, k.Accent);
        k.Para(m, y + 26, w - 250, notes, 8.5, Kit.Muted);
        k.Text(m, k.H - 52, w, 14, "Thank you for your business!", 10, k.Accent, bold: true, align: "Center");
        k.HRule(m, k.H - 60, w, Kit.Rule);
        return ty + 16;
    }

    private static void Receipt(Kit k)
    {
        k.Accent = Kit.Rgb(22, 128, 61);
        double m = k.M, w = k.W - 2 * m;
        k.Box(0, 0, k.W, 110, k.Accent);
        k.Text(m, 34, 300, 30, "PAYMENT RECEIPT", 22, Kit.White, bold: true);
        k.Text(m, 66, 300, 16, "Your Company  ·  hello@yourcompany.com", 9.5, Kit.Alpha(Kit.White, 210));
        k.Box(k.W - m - 120, 36, 120, 40, Kit.White, radius: 20);
        k.Text(k.W - m - 120, 47, 120, 18, "PAID", 15, k.Accent, bold: true, align: "Center");

        k.Input("Receipt number", "ReceiptNo", m, 135, 150);
        k.Input("Date", "Date", m + 170, 135, 140);
        k.Input("Payment method", "Method", m + 330, 135, w - 330, kind: "ComboBox", options: "Card,Cash,Bank transfer,Cheque,PayPal,Other");
        k.Input("Received from", "ReceivedFrom", m, 182, w);
        k.Input("For", "For", m, 229, w);

        double y = k.Grid(m, 285, w, ["Item", "Qty", "Price", "Amount"], [5, 1, 1.6, 1.6], 6, 22, "Item",
            align: ["Left", "Center", "Right", "Right"]);
        y = k.Totals(k.W - m, y + 10, "Amt", "Subtotal", "Tax", "Amount paid", "Balance due");
        k.SignatureLine("Received by", "ReceivedBy", m, y - 40, 220);
        k.Para(m, k.H - 70, w, "Keep this receipt for your records. Questions? Contact hello@yourcompany.com.", 8.5, Kit.Muted, "Center");
    }

    private static void PurchaseOrder(Kit k)
    {
        k.Accent = Kit.Rgb(55, 65, 81);
        double m = k.M, w = k.W - 2 * m, half = (w - 20) / 2;
        k.Text(m, 36, 300, 30, "PURCHASE ORDER", 22, k.Accent, bold: true);
        k.Text(m, 64, 300, 28, "Your Company\n123 Business Street, City", 9, Kit.Muted);
        k.Input("PO number", "PONumber", k.W - m - 230, 36, 110, required: true);
        k.Input("Date", "Date", k.W - m - 110, 36, 110);
        k.Input("Delivery by", "DeliveryDate", k.W - m - 110, 78, 110);

        k.SectionBar(m, 120, half, "Vendor");
        k.Box(m, 140, half, 70, Kit.FieldFill, Kit.Rule, 0.75);
        k.Field("Memo", "Vendor", m + 3, 142, half - 6, 66, "Vendor name and address");
        k.SectionBar(m + half + 20, 120, half, "Ship to");
        k.Box(m + half + 20, 140, half, 70, Kit.FieldFill, Kit.Rule, 0.75);
        k.Field("Memo", "ShipTo", m + half + 23, 142, half - 6, 66, "Delivery name and address");

        k.Input("Requested by", "RequestedBy", m, 222, half);
        k.Input("Shipping method", "ShipVia", m + half + 20, 222, half, kind: "ComboBox", options: "Standard,Express,Courier,Collection,Freight");

        double y = k.Grid(m, 272, w, ["Item #", "Description", "Qty", "Unit price", "Total"], [1.3, 4, 0.9, 1.4, 1.4], 10, 21, "Line",
            align: ["Left", "Left", "Center", "Right", "Right"]);
        y = k.Totals(k.W - m, y + 10, "PO", "Subtotal", "Tax", "Shipping", "Total");
        k.SignatureLine("Authorised by", "AuthorisedBy", m, y - 50, 220);
    }

    private static void PriceList(Kit k)
    {
        k.Accent = Kit.Rgb(124, 58, 237);
        double m = k.M, w = k.W - 2 * m;
        k.Box(0, 0, k.W, 150, Kit.Tint(k.Accent, 0.9));
        k.Text(m, 46, w, 40, "Price List", 34, k.Accent, bold: true, align: "Center");
        k.Text(m, 92, w, 18, "Your Company  ·  Effective " + DateTime.Today.ToString("MMMM yyyy"), 11, Kit.Muted, align: "Center");

        string[][] sections =
        [
            ["Essentials", "Starter package|Everything you need to get going|$49", "Standard package|Our most popular choice|$99", "Premium package|All features and priority support|$199"],
            ["Services", "Consultation (1 hour)|One-to-one session|$75", "Setup and installation|On site or remote|$120", "Training workshop|Half day, up to 10 people|$450"],
            ["Add-ons", "Extra user|Per user, per month|$8", "Extended warranty|Two further years|$39", "Express delivery|Next working day|$15"],
        ];
        double y = 180;
        foreach (var section in sections)
        {
            k.Text(m, y, w, 20, section[0].ToUpperInvariant(), 11, k.Accent, bold: true);
            k.HRule(m, y + 20, w, k.Accent, 1.25);
            y += 30;
            foreach (var line in section.Skip(1))
            {
                var p = line.Split('|');
                k.Text(m, y, w - 100, 16, p[0], 11, Kit.Ink, bold: true);
                k.Text(m, y + 16, w - 100, 14, p[1], 9, Kit.Muted);
                k.Text(k.W - m - 100, y + 4, 100, 18, p[2], 13, k.Accent, bold: true, align: "Right");
                y += 40;
            }
            y += 14;
        }
        k.Para(m, k.H - 70, w, "Prices include tax where applicable and may change without notice.\nyourcompany.com  ·  +1 555 000 0000", 8.5, Kit.Muted, "Center");
    }

    private static void Letterhead(Kit k)
    {
        double m = k.M, w = k.W - 2 * m;
        k.Circle(m, 40, 40, 40, k.Accent);
        k.Text(m, 50, 40, 20, "YC", 13, Kit.White, bold: true, align: "Center");
        k.Text(m + 52, 42, 260, 22, "Your Company", 18, Kit.Ink, bold: true);
        k.Text(m + 52, 64, 260, 14, "Tagline or department", 9.5, Kit.Muted);
        k.Text(k.W - m - 200, 42, 200, 44, "123 Business Street\nCity, Postcode\n+1 555 000 0000  ·  yourcompany.com", 8.5, Kit.Muted, align: "Right");
        k.HRule(m, 98, w, k.Accent, 2);

        k.Text(m, 130, 220, 14, DateTime.Today.ToString("d MMMM yyyy"), 10, Kit.Body);
        k.Text(m, 160, 260, 56, "Recipient Name\nCompany\nAddress line\nCity, Postcode", 10, Kit.Body);
        k.Text(m, 236, w, 16, "Dear Recipient,", 10.5, Kit.Ink);
        k.Para(m, 262, w, "Write your letter here. This letterhead keeps your company's name, contact details and colours at the top of every page you send.\n\nReplace this text with your own, or delete it and type straight onto the page.", 10.5);
        k.Text(m, 400, 200, 16, "Kind regards,", 10.5, Kit.Ink);
        k.Text(m, 450, 200, 16, "Your Name", 10.5, Kit.Ink, bold: true);
        k.Text(m, 466, 200, 14, "Job title", 9.5, Kit.Muted);

        k.Box(0, k.H - 30, k.W, 30, k.Accent);
        k.Text(m, k.H - 22, w, 14, "Registered in Country · Company no. 0000000 · VAT GB000000000", 8, Kit.Alpha(Kit.White, 220), align: "Center");
    }

    private static void ProposalCover(Kit k)
    {
        k.Accent = Kit.Rgb(234, 88, 12);
        double m = 56, w = k.W - 2 * m;
        k.Background(Kit.Rgb(17, 24, 39));
        k.Box(0, 0, 14, k.H, k.Accent);
        k.Circle(k.W - 260, -120, 420, 420, Kit.Alpha(k.Accent, 40));
        k.Circle(k.W - 170, 120, 240, 240, Kit.Alpha(k.Accent, 60));
        k.Text(m, 300, w, 16, "BUSINESS PROPOSAL", 11, k.Accent, bold: true);
        k.Text(m, 324, w, 110, "Project Name\nGoes Here", 40, Kit.White, bold: true);
        k.Box(m, 440, 70, 4, k.Accent);
        k.Para(m, 462, w - 80, "A short summary of what you're proposing and the value it brings to the client.", 13, Kit.Alpha(Kit.White, 200));
        k.Label(m, k.H - 150, 200, "Prepared for", k.Accent);
        k.Text(m, k.H - 136, 220, 18, "Client Company", 13, Kit.White, bold: true);
        k.Label(m + 240, k.H - 150, 200, "Prepared by", k.Accent);
        k.Text(m + 240, k.H - 136, 220, 18, "Your Company", 13, Kit.White, bold: true);
        k.Text(m, k.H - 80, w, 14, DateTime.Today.ToString("MMMM yyyy"), 10, Kit.Alpha(Kit.White, 160));
    }

    private static void ReportCover(Kit k)
    {
        k.Accent = Kit.Rgb(3, 105, 161);
        double m = 56, w = k.W - 2 * m;
        k.Box(0, 0, k.W, k.H * 0.55, k.Accent);
        for (int i = 0; i < 6; i++) k.Box(k.W - 60 - i * 34, 70 + i * 34, 24 + i * 34, 6, Kit.Alpha(Kit.White, (byte)(40 + i * 25)));
        k.Text(m, 220, w, 16, DateTime.Today.Year + " ANNUAL REPORT", 12, Kit.Alpha(Kit.White, 220), bold: true);
        k.Text(m, 244, w, 100, "Growing\nTogether", 46, Kit.White, bold: true);
        k.Box(m, k.H * 0.55 + 40, 60, 4, k.Accent);
        k.Para(m, k.H * 0.55 + 60, w, "Highlights, results and the year ahead for Your Company.", 14, Kit.Body);
        double y = k.H * 0.55 + 130;
        string[][] stats = [["$12.4M", "Revenue"], ["38%", "Growth"], ["1,250", "Customers"]];
        double cw = w / 3;
        for (int i = 0; i < 3; i++)
        {
            k.Text(m + i * cw, y, cw - 10, 30, stats[i][0], 24, k.Accent, bold: true);
            k.Text(m + i * cw, y + 32, cw - 10, 14, stats[i][1].ToUpperInvariant(), 9, Kit.Muted, bold: true);
        }
        k.HRule(m, k.H - 70, w);
        k.Text(m, k.H - 58, w, 14, "Your Company  ·  yourcompany.com", 9, Kit.Muted);
    }

    private static void StatusReport(Kit k)
    {
        k.Accent = Kit.Rgb(37, 99, 235);
        double m = 40, w = k.W - 2 * m, half = (w - 16) / 2;
        k.M = m;
        k.Text(m, 32, w, 26, "Project Status Report", 20, Kit.Ink, bold: true);
        k.Input("Project", "Project", m, 66, half);
        k.Input("Project manager", "Manager", m + half + 16, 66, half / 2 - 8);
        k.Input("Report date", "ReportDate", m + half + 16 + half / 2 + 8, 66, half / 2 - 8);

        k.SectionBar(m, 116, w, "Overall status");
        string[] areas = ["Schedule", "Budget", "Scope", "Quality"];
        double cw = w / 4;
        for (int i = 0; i < 4; i++)
        {
            k.Text(m + i * cw + 6, 144, cw - 12, 14, areas[i], 10, Kit.Ink, bold: true);
            k.Box(m + i * cw + 6, 162, cw - 12, 22, Kit.FieldFill, Kit.Rule, 0.75, 2);
            k.Field("ComboBox", "Status" + areas[i], m + i * cw + 8, 163, cw - 16, 20, areas[i], options: "On track,At risk,Off track,Complete");
        }

        k.SectionBar(m, 204, w, "Summary");
        k.Box(m, 228, w, 70, Kit.FieldFill, Kit.Rule, 0.75);
        k.Field("Memo", "Summary", m + 3, 230, w - 6, 66, "Summary of progress");

        k.SectionBar(m, 314, w, "Milestones");
        double y = k.Grid(m, 338, w, ["Milestone", "Owner", "Due", "Status"], [4, 2, 1.4, 1.6], 5, 21, "Milestone",
            headerFill: Kit.Fill, headerText: Kit.Ink);

        k.SectionBar(m, y + 16, half, "Risks and issues", Kit.Rgb(220, 38, 38));
        k.Box(m, y + 40, half, 120, Kit.FieldFill, Kit.Rule, 0.75);
        k.Field("Memo", "Risks", m + 3, y + 42, half - 6, 116, "Risks and issues");
        k.SectionBar(m + half + 16, y + 16, half, "Next steps", Kit.Rgb(22, 163, 74));
        k.Box(m + half + 16, y + 40, half, 120, Kit.FieldFill, Kit.Rule, 0.75);
        k.Field("Memo", "NextSteps", m + half + 19, y + 42, half - 6, 116, "Next steps");
    }
}
