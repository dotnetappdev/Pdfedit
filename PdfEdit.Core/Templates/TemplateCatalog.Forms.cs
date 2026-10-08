namespace PdfEdit.Templates;

// Fillable forms (applications, registrations, surveys, HR forms) and agreements with signature fields.
public static partial class TemplateCatalog
{
    private static IEnumerable<PdfTemplate> FormTemplates() =>
    [
        new("Form", "Application form (simple)", Forms, "Name, contact details and comments.",
            () => ClassicTemplates.Create("Form")) { Tags = ["application"] },
        T("JobApplication", "Job application", Forms, "Personal details, position, availability, work history, references and signature.",
            JobApplication, tags: ["employment", "hr", "hiring", "careers"], fillable: true),
        T("Registration", "Event registration", Forms, "Attendee details, ticket type, sessions and dietary needs.", Registration,
            tags: ["sign up", "signup", "attendee", "conference"], fillable: true),
        T("ContactInfo", "Contact information", Forms, "Contact details with emergency contact and preferences.", ContactInfo,
            tags: ["customer", "client", "intake", "emergency"], fillable: true),
        T("Survey", "Feedback survey", Forms, "Rating scales, multiple choice and open questions.", Survey,
            tags: ["questionnaire", "feedback", "customer satisfaction", "poll"], fillable: true),
        T("OrderForm", "Order form", Forms, "Customer, items to order, delivery and payment.", OrderForm,
            tags: ["purchase", "sales"], fillable: true),
        T("Consent", "Consent and release form", Forms, "Participant consent with photo release and guardian signature.", Consent,
            tags: ["waiver", "permission", "release", "photo"], fillable: true),
        T("TimeOff", "Time-off request", Forms, "Leave type, dates, cover and manager approval.", TimeOff,
            tags: ["leave", "holiday", "vacation", "pto", "hr"], fillable: true),
        T("ExpenseReport", "Expense report", Forms, "Itemised expenses by category with totals and approval.", ExpenseReport,
            tags: ["reimbursement", "claim", "travel", "finance"], fillable: true),
        T("Timesheet", "Weekly timesheet", Forms, "Hours per day with breaks, overtime and sign-off.", Timesheet,
            tags: ["hours", "payroll", "work"], fillable: true),
        T("IncidentReport", "Incident report", Forms, "What happened, when and where, people involved and actions taken.", IncidentReport,
            tags: ["accident", "safety", "health"], fillable: true),
        T("SignIn", "Sign-in sheet", Forms, "Visitor or attendance sign-in with times.", SignIn,
            tags: ["attendance", "visitor", "register"], fillable: true),
        T("RentalApplication", "Rental application", Forms, "Applicant, current address, employment and references.", RentalApplication,
            tags: ["tenant", "lease", "property", "landlord"], fillable: true),
    ];

    private static IEnumerable<PdfTemplate> AgreementTemplates() =>
    [
        T("Nda", "Non-disclosure agreement", Agreements, "Mutual NDA with the main clauses and signature blocks.", Nda,
            tags: ["nda", "confidentiality", "contract", "legal"], fillable: true),
        T("ServiceAgreement", "Service agreement", Agreements, "Scope, fees, timeline and terms between a provider and a client.",
            ServiceAgreement, tags: ["contract", "freelance", "consulting", "legal"], fillable: true),
        T("RentalAgreement", "Rental agreement", Agreements, "Simple residential lease: property, rent, deposit, term and signatures.",
            RentalAgreement, tags: ["lease", "tenancy", "landlord", "tenant", "contract"], fillable: true),
        T("BillOfSale", "Bill of sale", Agreements, "Transfer of an item (vehicle or goods) from seller to buyer.", BillOfSale,
            tags: ["vehicle", "car", "sale", "contract"], fillable: true),
    ];

    /// <summary>A form's title band: title, subtitle and the accent rule.</summary>
    private static double FormHeader(Kit k, string title, string subtitle)
    {
        k.Box(0, 0, k.W, 6, k.Accent);
        k.Text(k.M, 30, k.W - 2 * k.M, 28, title, 20, Kit.Ink, bold: true);
        k.Text(k.M, 58, k.W - 2 * k.M, 14, subtitle, 9.5, Kit.Muted);
        return 88;
    }

    /// <summary>Inputs laid out in a row: (label, field name, share of the width).</summary>
    private static double Row(Kit k, double y, params (string Label, string Name, double Share)[] cols)
    {
        double w = k.W - 2 * k.M, gap = 14, total = cols.Sum(c => c.Share), x = k.M;
        double free = w - gap * (cols.Length - 1);
        foreach (var (label, name, share) in cols)
        {
            double cw = free * share / total;
            k.Input(label, name, x, y, cw);
            x += cw + gap;
        }
        return y + 44;
    }

    private static double Section(Kit k, double y, string title)
    {
        k.SectionBar(k.M, y, k.W - 2 * k.M, title);
        return y + 30;
    }

    private static double Memo(Kit k, double y, string label, string name, double h)
    {
        double w = k.W - 2 * k.M;
        k.Label(k.M, y, w, label);
        k.Box(k.M, y + 12, w, h, Kit.FieldFill, Kit.Rule, 0.75, 2);
        k.Field("Memo", name, k.M + 3, y + 14, w - 6, h - 4, label);
        return y + h + 22;
    }

    private static double Signatures(Kit k, double y, params string[] who)
    {
        double w = k.W - 2 * k.M, gap = 24, cw = (w - gap * (who.Length - 1)) / who.Length;
        for (int i = 0; i < who.Length; i++)
        {
            double x = k.M + i * (cw + gap);
            k.SignatureLine(who[i] + " signature", who[i].Replace(" ", "") + "Signature", x, y, cw * 0.62);
            k.Input("Date", who[i].Replace(" ", "") + "Date", x + cw * 0.66, y + 4, cw * 0.34);
        }
        return y + 64;
    }

    private static void JobApplication(Kit k)
    {
        k.M = 40;
        double y = FormHeader(k, "Job Application", "Please complete every section. Fields marked * are required.");
        y = Section(k, y, "Personal details");
        y = Row(k, y, ("First name *", "FirstName", 1), ("Last name *", "LastName", 1), ("Date of birth", "DOB", 0.7));
        y = Row(k, y, ("Email *", "Email", 1.3), ("Phone *", "Phone", 1), ("Postcode", "Postcode", 0.6));
        y = Row(k, y, ("Address", "Address", 1));
        y = Section(k, y + 4, "Position");
        y = Row(k, y, ("Position applied for *", "Position", 1.4), ("Available from", "StartDate", 0.8), ("Salary expectation", "Salary", 0.8));
        k.Label(k.M, y, 200, "Employment type");
        string[] types = ["Full time", "Part time", "Contract", "Temporary"];
        for (int i = 0; i < types.Length; i++) k.Check(types[i], "Type" + types[i].Replace(" ", ""), k.M + i * 120, y + 16, 115);
        y += 44;
        y = Section(k, y, "Employment history");
        y = k.Grid(k.M, y, k.W - 2 * k.M, ["Employer", "Job title", "From", "To", "Reason for leaving"], [2.2, 2, 1, 1, 2.4], 3, 22, "Job",
            headerFill: Kit.Fill, headerText: Kit.Ink) + 14;
        y = Section(k, y, "References");
        y = k.Grid(k.M, y, k.W - 2 * k.M, ["Name", "Relationship", "Phone or email"], [2, 1.6, 2.4], 2, 22, "Ref",
            headerFill: Kit.Fill, headerText: Kit.Ink) + 14;
        k.Para(k.M, y, k.W - 2 * k.M, "I confirm that the information I have given is true and complete.", 9, Kit.Muted);
        Signatures(k, y + 22, "Applicant");
    }

    private static void Registration(Kit k)
    {
        k.Accent = Kit.Rgb(219, 39, 119);
        double y = FormHeader(k, "Event Registration", "Event name  ·  Date  ·  Venue");
        y = Section(k, y, "Attendee");
        y = Row(k, y, ("Full name *", "Name", 1.4), ("Job title", "JobTitle", 1));
        y = Row(k, y, ("Organisation", "Organisation", 1.4), ("Email *", "Email", 1.2));
        y = Row(k, y, ("Phone", "Phone", 1), ("City", "City", 1), ("Country", "Country", 1));
        y = Section(k, y + 4, "Ticket");
        string[] tickets = ["Full pass", "Day pass", "Student", "Virtual"];
        for (int i = 0; i < tickets.Length; i++)
        {
            k.Box(k.M, y + i * 24, 12, 12, Kit.White, Kit.Faint, 0.75, 6);
            k.Field("Radio", "Ticket", k.M, y + i * 24, 12, 12, tickets[i], labelPos: "None");
            k.Text(k.M + 18, y - 1 + i * 24, 220, 14, tickets[i], 10, Kit.Body);
        }
        k.Label(k.M + 260, y, 200, "Sessions I'll attend");
        string[] sessions = ["Morning keynote", "Workshops", "Networking lunch", "Evening reception"];
        for (int i = 0; i < sessions.Length; i++) k.Check(sessions[i], "Session" + (i + 1), k.M + 260, y + 16 + i * 22, 200);
        y += 110;
        y = Row(k, y, ("Dietary requirements", "Dietary", 1.2), ("Accessibility needs", "Access", 1.2));
        y = Memo(k, y, "Anything else we should know?", "Notes", 56);
        k.Check("I agree to the event terms and privacy policy", "AgreeTerms", k.M, y, 360);
        k.Check("Send me news about future events", "OptIn", k.M, y + 22, 360);
    }

    private static void ContactInfo(Kit k)
    {
        k.Accent = Kit.Rgb(8, 145, 178);
        double y = FormHeader(k, "Contact Information", "Please keep your details up to date.");
        y = Section(k, y, "Your details");
        y = Row(k, y, ("Title", "Title", 0.5), ("First name *", "FirstName", 1), ("Last name *", "LastName", 1));
        y = Row(k, y, ("Email *", "Email", 1.4), ("Mobile", "Mobile", 1), ("Home phone", "HomePhone", 1));
        y = Row(k, y, ("Address line 1", "Address1", 1));
        y = Row(k, y, ("Address line 2", "Address2", 1));
        y = Row(k, y, ("City", "City", 1), ("County / state", "Region", 1), ("Postcode", "Postcode", 0.6));
        y = Section(k, y + 4, "Emergency contact");
        y = Row(k, y, ("Name", "EmergencyName", 1.2), ("Relationship", "EmergencyRelation", 0.8), ("Phone", "EmergencyPhone", 1));
        y = Section(k, y + 4, "How may we contact you?");
        string[] ways = ["Email", "Phone", "Text message", "Post"];
        for (int i = 0; i < ways.Length; i++) k.Check(ways[i], "Contact" + ways[i].Replace(" ", ""), k.M + i * 125, y, 120);
        Signatures(k, y + 40, "Your");
    }

    private static void Survey(Kit k)
    {
        k.Accent = Kit.Rgb(79, 70, 229);
        double m = k.M, w = k.W - 2 * m;
        double y = FormHeader(k, "Customer Feedback Survey", "Thank you for taking a few minutes to tell us about your experience.");
        string[] scale = ["Very poor", "Poor", "Okay", "Good", "Excellent"];
        double qx = m + 230, cw = (w - 230) / scale.Length;
        k.Box(m, y, w, 34, Kit.Tint(k.Accent, 0.9));
        for (int c = 0; c < scale.Length; c++) k.Text(qx + c * cw, y + 11, cw, 14, scale[c], 8.5, k.Accent, bold: true, align: "Center");
        y += 34;
        string[] questions = ["Overall satisfaction", "Quality of the product", "Value for money", "Ease of ordering", "Delivery speed", "Customer service"];
        for (int q = 0; q < questions.Length; q++)
        {
            double ry = y + q * 28;
            if (q % 2 == 1) k.Box(m, ry, w, 28, Kit.Fill);
            k.Text(m + 8, ry + 7, 220, 14, questions[q], 10, Kit.Body);
            for (int c = 0; c < scale.Length; c++)
            {
                double bx = qx + c * cw + cw / 2 - 6;
                k.Box(bx, ry + 8, 12, 12, Kit.White, Kit.Faint, 0.75, 6);
                k.Field("Radio", "Q" + (q + 1), bx, ry + 8, 12, 12, scale[c], labelPos: "None");
            }
        }
        y += questions.Length * 28 + 22;
        k.Label(m, y, w, "How did you hear about us?");
        string[] heard = ["Search engine", "Social media", "Friend or colleague", "Advert", "Other"];
        for (int i = 0; i < heard.Length; i++) k.Check(heard[i], "Heard" + (i + 1), m + (i % 3) * 165, y + 16 + i / 3 * 22, 160);
        y += 72;
        k.Label(m, y, w, "How likely are you to recommend us? (0 = not at all, 10 = extremely)");
        for (int n = 0; n <= 10; n++)
        {
            double bx = m + n * (w / 11);
            k.Box(bx, y + 16, w / 11 - 6, 22, Kit.FieldFill, Kit.Rule, 0.75, 3);
            k.Text(bx, y + 21, w / 11 - 6, 12, n.ToString(), 9, Kit.Muted, align: "Center");
            k.Field("Radio", "Recommend", bx, y + 16, w / 11 - 6, 22, n.ToString(), labelPos: "None");
        }
        y += 56;
        y = Memo(k, y, "What did we do well?", "DoneWell", 60);
        Memo(k, y, "What could we improve?", "Improve", 60);
    }

    private static void OrderForm(Kit k)
    {
        k.Accent = Kit.Rgb(194, 65, 12);
        double m = k.M, w = k.W - 2 * m;
        double y = FormHeader(k, "Order Form", "Your Company  ·  orders@yourcompany.com  ·  +1 555 000 0000");
        y = Row(k, y, ("Customer name *", "Customer", 1.4), ("Order date", "OrderDate", 0.7), ("Customer #", "CustomerNo", 0.7));
        y = Row(k, y, ("Delivery address", "Address", 1.6), ("Phone", "Phone", 0.8));
        y = k.Grid(m, y + 4, w, ["Product code", "Description", "Qty", "Price", "Total"], [1.4, 4, 0.9, 1.3, 1.3], 10, 21, "Order",
            align: ["Left", "Left", "Center", "Right", "Right"]);
        double ty = k.Totals(k.W - m, y + 10, "Order", "Subtotal", "Delivery", "Total");
        k.Label(m, y + 12, 240, "Payment");
        string[] pay = ["Card", "Bank transfer", "Invoice me", "Cash on delivery"];
        for (int i = 0; i < pay.Length; i++)
        {
            k.Box(m, y + 28 + i * 20, 12, 12, Kit.White, Kit.Faint, 0.75, 6);
            k.Field("Radio", "Payment", m, y + 28 + i * 20, 12, 12, pay[i], labelPos: "None");
            k.Text(m + 18, y + 27 + i * 20, 200, 14, pay[i], 9.5, Kit.Body);
        }
        Signatures(k, Math.Max(ty, y + 110) + 16, "Customer");
    }

    private static void Consent(Kit k)
    {
        k.Accent = Kit.Rgb(5, 150, 105);
        double m = k.M, w = k.W - 2 * m;
        double y = FormHeader(k, "Consent and Release Form", "Activity or event name  ·  Organiser");
        y = Row(k, y, ("Participant name *", "Participant", 1.4), ("Date of birth", "DOB", 0.6));
        y = Row(k, y, ("Address", "Address", 1.4), ("Phone", "Phone", 0.8));
        y = Section(k, y + 4, "Consent");
        y = k.Para(m, y, w, "I consent to taking part in the activity described above. I understand the nature of the activity and the risks involved, and I agree to follow the instructions of the organisers. I confirm that I have told the organisers about any medical conditions that may affect my participation.", 9.5).Y + 74;
        y = Memo(k, y, "Medical conditions, allergies or medication", "Medical", 44);
        y = Section(k, y, "Photography and media");
        k.Para(m, y, w, "Photos and video may be taken during the activity and used in our publications, website and social media.", 9.5);
        k.Check("I give permission for photos and video of the participant to be used", "PhotoYes", m, y + 34, w);
        k.Check("I do not give permission", "PhotoNo", m, y + 56, w);
        y += 88;
        y = Section(k, y, "Parent or guardian (if the participant is under 18)");
        y = Row(k, y, ("Parent / guardian name", "Guardian", 1.4), ("Relationship", "GuardianRelation", 0.8));
        Signatures(k, y + 6, "Participant", "Guardian");
    }

    private static void TimeOff(Kit k)
    {
        k.Accent = Kit.Rgb(13, 148, 136);
        double m = k.M;
        double y = FormHeader(k, "Time-off Request", "Submit to your manager at least two weeks before the first day of leave.");
        y = Row(k, y, ("Employee name *", "Employee", 1.4), ("Employee ID", "EmployeeId", 0.6), ("Department", "Department", 1));
        y = Section(k, y + 4, "Leave requested");
        string[] types = ["Holiday", "Sick leave", "Personal", "Parental", "Bereavement", "Unpaid", "Training", "Other"];
        for (int i = 0; i < types.Length; i++) k.Check(types[i], "Leave" + types[i], m + (i % 4) * 125, y + i / 4 * 22, 120);
        y += 54;
        y = Row(k, y, ("First day", "FromDate", 1), ("Last day", "ToDate", 1), ("Return to work", "ReturnDate", 1), ("Total days", "TotalDays", 0.6));
        y = Memo(k, y, "Reason / notes", "Reason", 50);
        y = Row(k, y, ("Cover arranged with", "Cover", 1));
        y = Signatures(k, y + 6, "Employee");
        y = Section(k, y + 4, "Manager approval");
        k.Check("Approved", "Approved", m, y, 120);
        k.Check("Not approved", "NotApproved", m + 130, y, 140);
        y = Memo(k, y + 26, "Comments", "ManagerComments", 40);
        Signatures(k, y + 4, "Manager");
    }

    private static void ExpenseReport(Kit k)
    {
        k.Accent = Kit.Rgb(71, 85, 105);
        double m = 36, w = k.W - 2 * m;
        k.M = m;
        double y = FormHeader(k, "Expense Report", "Attach receipts for every item over $25.");
        y = Row(k, y, ("Employee *", "Employee", 1.3), ("Department", "Department", 1), ("Period from", "PeriodFrom", 0.7), ("To", "PeriodTo", 0.7));
        y = Row(k, y, ("Purpose of expenses", "Purpose", 1.6), ("Cost centre", "CostCentre", 0.6));
        y = k.Grid(m, y + 4, w, ["Date", "Description", "Category", "Travel", "Meals", "Lodging", "Other", "Total"],
            [1.1, 3, 1.4, 1, 1, 1, 1, 1.1], 12, 20, "Exp", align: ["Left", "Left", "Left", "Right", "Right", "Right", "Right", "Right"]);
        double ty = k.Totals(k.W - m, y + 10, "Exp", "Subtotal", "Less advances", "Amount owed");
        Signatures(k, ty + 16, "Employee", "Approver");
    }

    private static void Timesheet(Kit k)
    {
        k.Accent = Kit.Rgb(2, 132, 199);
        double m = k.M, w = k.W - 2 * m;
        double y = FormHeader(k, "Weekly Timesheet", "Record start, finish and break times for each day.");
        y = Row(k, y, ("Employee *", "Employee", 1.4), ("Week starting", "WeekStart", 0.8), ("Manager", "Manager", 1));
        string[] days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
        y = k.Grid(m, y + 4, w, ["Day", "Date", "Start", "Finish", "Break", "Overtime", "Total hours"], [1.5, 1.1, 1, 1, 1, 1, 1.2], 7, 24, "Day",
            cells: days.Select(d => new[] { d }).ToArray());
        double ty = k.Totals(k.W - m, y + 10, "Week", "Regular hours", "Overtime hours", "Total hours");
        Memo(k, ty + 10, "Notes", "Notes", 46);
        Signatures(k, ty + 90, "Employee", "Manager");
    }

    private static void IncidentReport(Kit k)
    {
        k.Accent = Kit.Rgb(220, 38, 38);
        double m = k.M;
        double y = FormHeader(k, "Incident Report", "Complete as soon as possible after the incident.");
        y = Row(k, y, ("Reported by *", "ReportedBy", 1.2), ("Job title", "JobTitle", 1), ("Date reported", "DateReported", 0.7));
        y = Section(k, y + 4, "The incident");
        y = Row(k, y, ("Date of incident *", "IncidentDate", 0.8), ("Time", "IncidentTime", 0.5), ("Location *", "Location", 1.7));
        string[] types = ["Injury", "Near miss", "Property damage", "Security", "Environmental", "Other"];
        k.Label(m, y, 200, "Type");
        for (int i = 0; i < types.Length; i++) k.Check(types[i], "Type" + (i + 1), m + (i % 3) * 165, y + 16 + i / 3 * 22, 160);
        y += 66;
        y = Memo(k, y, "What happened?", "Description", 80);
        y = Memo(k, y, "People involved and witnesses", "People", 44);
        y = Memo(k, y, "Action taken", "Action", 44);
        k.Check("First aid given", "FirstAid", m, y, 140);
        k.Check("Emergency services called", "Emergency", m + 150, y, 200);
        k.Check("Follow-up needed", "FollowUp", m + 360, y, 140);
        Signatures(k, y + 32, "Reporter", "Supervisor");
    }

    private static void SignIn(Kit k)
    {
        k.Accent = Kit.Rgb(30, 64, 175);
        double m = 36, w = k.W - 2 * m;
        k.M = m;
        double y = FormHeader(k, "Sign-in Sheet", "Please sign in on arrival and sign out when you leave.");
        y = Row(k, y, ("Event / location", "Event", 1.6), ("Date", "Date", 0.6));
        k.Grid(m, y + 4, w, ["#", "Name", "Organisation", "Email or phone", "Time in", "Time out"], [0.4, 2.4, 2, 2.4, 0.9, 0.9], 24, 24, "Visitor",
            cells: Enumerable.Range(1, 24).Select(i => new[] { i.ToString() }).ToArray());
    }

    private static void RentalApplication(Kit k)
    {
        k.Accent = Kit.Rgb(101, 163, 13);
        double m = 40;
        k.M = m;
        double y = FormHeader(k, "Rental Application", "Property address  ·  Monthly rent  ·  Move-in date");
        y = Section(k, y, "Applicant");
        y = Row(k, y, ("Full name *", "Name", 1.4), ("Date of birth", "DOB", 0.6), ("Phone *", "Phone", 0.9));
        y = Row(k, y, ("Email *", "Email", 1.2), ("Number of occupants", "Occupants", 0.6), ("Pets", "Pets", 0.8));
        y = Section(k, y + 4, "Current address");
        y = Row(k, y, ("Address", "CurrentAddress", 1.8), ("Since", "CurrentSince", 0.5));
        y = Row(k, y, ("Landlord name", "Landlord", 1), ("Landlord phone", "LandlordPhone", 0.8), ("Monthly rent", "CurrentRent", 0.6));
        y = Section(k, y + 4, "Employment and income");
        y = Row(k, y, ("Employer", "Employer", 1.2), ("Job title", "Occupation", 1), ("Monthly income", "Income", 0.6));
        y = Section(k, y + 4, "References");
        y = k.Grid(m, y, k.W - 2 * m, ["Name", "Relationship", "Phone"], [2, 1.5, 1.5], 2, 22, "Ref", headerFill: Kit.Fill, headerText: Kit.Ink) + 12;
        k.Para(m, y, k.W - 2 * m, "I confirm the information above is correct and authorise the landlord to verify it, including credit and reference checks.", 9, Kit.Muted);
        Signatures(k, y + 30, "Applicant");
    }

    // ── Agreements ───────────────────────────────────────────────────────────

    /// <summary>Numbered clauses: (heading, text). Returns the y below them.</summary>
    private static double Clauses(Kit k, double y, double size, params (string Head, string Text)[] clauses)
    {
        double m = k.M, w = k.W - 2 * m;
        for (int i = 0; i < clauses.Length; i++)
        {
            k.Text(m, y, w, size * 1.6, $"{i + 1}. {clauses[i].Head}", size + 0.5, Kit.Ink, bold: true);
            var p = k.Para(m + 14, y + size * 1.7, w - 14, clauses[i].Text, size);
            y = p.Y + p.H + 6;
        }
        return y;
    }

    private static double PartyBlock(Kit k, double y, string first, string second)
    {
        double half = (k.W - 2 * k.M - 20) / 2;
        k.Input(first + " (name and address)", first.Replace(" ", ""), k.M, y, half, 34, kind: "Memo");
        k.Input(second + " (name and address)", second.Replace(" ", ""), k.M + half + 20, y, half, 34, kind: "Memo");
        return y + 60;
    }

    private static void Nda(Kit k)
    {
        k.Accent = Kit.Rgb(30, 41, 59);
        k.M = 54;
        double y = FormHeader(k, "Mutual Non-Disclosure Agreement", "This agreement is made on the date it is signed by both parties.");
        k.Input("Effective date", "EffectiveDate", k.W - k.M - 140, 30, 140);
        y = PartyBlock(k, y, "Party A", "Party B");
        y = Clauses(k, y, 9,
            ("Purpose", "The parties wish to share confidential information to evaluate a possible business relationship (the \"Purpose\")."),
            ("Confidential information", "Any information disclosed by either party, in any form, that is marked confidential or would reasonably be understood to be confidential."),
            ("Obligations", "Each party will use the other's confidential information only for the Purpose, keep it secret and protect it with at least reasonable care."),
            ("Exclusions", "These obligations do not apply to information that is public, already known, independently developed, or that must be disclosed by law."),
            ("Term", "This agreement lasts for two years from the effective date; obligations of confidentiality survive for a further three years."),
            ("Return of information", "On request, each party will return or destroy the other's confidential information."),
            ("Governing law", "This agreement is governed by the laws of the jurisdiction named here: ____________________."));
        Signatures(k, y + 14, "Party A", "Party B");
    }

    private static void ServiceAgreement(Kit k)
    {
        k.Accent = Kit.Rgb(67, 56, 202);
        k.M = 54;
        double y = FormHeader(k, "Service Agreement", "Between the service provider and the client named below.");
        y = PartyBlock(k, y, "Service provider", "Client");
        y = Memo(k, y, "Services to be provided", "Services", 50);
        y = Row(k, y, ("Start date", "StartDate", 1), ("End date", "EndDate", 1), ("Fee", "Fee", 1), ("Payment terms", "PaymentTerms", 1.3));
        y = Clauses(k, y, 8.5,
            ("Payment", "The client will pay the fee as set out above. Late payments may incur interest at the statutory rate."),
            ("Changes", "Changes to the services must be agreed in writing and may change the fee and timeline."),
            ("Ownership", "On full payment, the client owns the deliverables created for it under this agreement."),
            ("Termination", "Either party may end this agreement with 14 days' written notice; work done up to then will be paid for."),
            ("Liability", "Neither party is liable for indirect losses. Each party's total liability is limited to the fees paid."));
        Signatures(k, y + 12, "Provider", "Client");
    }

    private static void RentalAgreement(Kit k)
    {
        k.Accent = Kit.Rgb(21, 128, 61);
        k.M = 54;
        double y = FormHeader(k, "Residential Rental Agreement", "A simple agreement between a landlord and tenant.");
        y = PartyBlock(k, y, "Landlord", "Tenant");
        y = Row(k, y, ("Property address", "Property", 1));
        y = Row(k, y, ("Start date", "StartDate", 1), ("End date", "EndDate", 1), ("Monthly rent", "Rent", 1), ("Deposit", "Deposit", 1));
        y = Clauses(k, y, 8.5,
            ("Rent", "Rent is due on the first day of each month by bank transfer to the account the landlord provides."),
            ("Deposit", "The deposit is held for the tenancy and returned within 30 days of the end, less any agreed deductions."),
            ("Use of the property", "The tenant will use the property as a private home, keep it clean and in good condition, and not sublet it."),
            ("Repairs", "The landlord is responsible for structural repairs and appliances supplied; the tenant will report problems promptly."),
            ("Ending the tenancy", "Either party may end this agreement at the end of the term by giving one month's written notice."));
        Signatures(k, y + 12, "Landlord", "Tenant");
    }

    private static void BillOfSale(Kit k)
    {
        k.Accent = Kit.Rgb(180, 83, 9);
        k.M = 54;
        double y = FormHeader(k, "Bill of Sale", "Records the sale and transfer of ownership of the item below.");
        y = PartyBlock(k, y, "Seller", "Buyer");
        y = Section(k, y, "Item sold");
        y = Row(k, y, ("Description", "Item", 1.6), ("Make / model", "Model", 1));
        y = Row(k, y, ("Serial / VIN", "Serial", 1.2), ("Year", "Year", 0.5), ("Mileage / condition", "Condition", 1));
        y = Row(k, y, ("Sale price", "Price", 1), ("Date of sale", "SaleDate", 1), ("Payment method", "Method", 1));
        y = k.Para(k.M, y + 4, k.W - 2 * k.M, "The seller confirms they are the legal owner of the item, that it is free of any debts or claims, and sells it to the buyer as seen, without further warranty.", 9).Y + 50;
        Signatures(k, y, "Seller", "Buyer");
    }
}
