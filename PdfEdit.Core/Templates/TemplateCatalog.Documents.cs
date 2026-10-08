namespace PdfEdit.Templates;

// Letters, memos and meeting papers; resumes and cover letters.
public static partial class TemplateCatalog
{
    private static IEnumerable<PdfTemplate> LetterTemplates() =>
    [
        new("Letter", "Formal letter", Letters, "A plain formal letter layout.", () => ClassicTemplates.Create("Letter")) { Tags = ["correspondence"] },
        T("Memo", "Memo", Letters, "An internal memo with To, From, Date and Subject.", Memo,
            tags: ["memorandum", "internal", "office"]),
        T("FaxCover", "Fax cover sheet", Letters, "Fax cover with sender, recipient, pages and urgency.", FaxCover,
            tags: ["fax", "cover"], fillable: true),
        T("Agenda", "Meeting agenda", Letters, "Agenda with times, topics, presenters and attendees.", Agenda,
            tags: ["meeting", "schedule"], fillable: true),
        T("Minutes", "Meeting minutes", Letters, "Attendees, discussion, decisions and action items.", Minutes,
            tags: ["meeting", "notes", "actions"], fillable: true),
        T("PressRelease", "Press release", Letters, "A press release with headline, dateline, quote and boilerplate.", PressRelease,
            tags: ["news", "media", "announcement", "pr"]),
    ];

    private static IEnumerable<PdfTemplate> ResumeTemplates() =>
    [
        new("Resume", "Resume (sidebar)", Resumes, "A two-column resume with a dark sidebar.", () => ClassicTemplates.Create("Resume")) { Tags = ["cv"] },
        T("ResumeClassic", "Resume (classic)", Resumes, "A traditional single-column resume.", ResumeClassic, tags: ["cv", "simple"]),
        T("ResumeModern", "Resume (modern)", Resumes, "A bold header and a skills column.", ResumeModern, tags: ["cv", "creative"]),
        T("CoverLetter", "Cover letter", Resumes, "A cover letter that matches the modern resume.", CoverLetter, tags: ["job", "application letter"]),
    ];

    private static void Memo(Kit k)
    {
        double m = 60, w = k.W - 2 * m;
        k.M = m;
        k.Text(m, 50, w, 40, "MEMO", 34, k.Accent, bold: true);
        k.HRule(m, 96, w, k.Accent, 2);
        string[] rows = ["To:", "From:", "CC:", "Date:", "Subject:"];
        string[] values = ["All staff", "Your Name, Job title", "", DateTime.Today.ToString("d MMMM yyyy"), "Subject of the memo"];
        for (int i = 0; i < rows.Length; i++)
        {
            k.Text(m, 114 + i * 24, 70, 16, rows[i], 10.5, Kit.Muted, bold: true);
            k.Text(m + 74, 114 + i * 24, w - 74, 16, values[i], 10.5, Kit.Ink, bold: i == 4);
        }
        k.HRule(m, 240, w);
        k.Para(m, 260, w, "Start with the main point of the memo in one or two sentences.\n\nAdd the details: background, what's changing, and what people need to do and by when.\n\nEnd with who to contact with questions.", 10.5);
    }

    private static void FaxCover(Kit k)
    {
        k.Accent = Kit.Rgb(15, 23, 42);
        double m = k.M, w = k.W - 2 * m;
        k.Box(m, 40, w, 70, k.Accent);
        k.Text(m + 20, 56, w - 40, 40, "FAX", 34, Kit.White, bold: true);
        k.Text(m + 20, 60, w - 40, 30, "Your Company\n+1 555 000 0001 (fax)", 10, Kit.Alpha(Kit.White, 200), align: "Right");
        double half = (w - 24) / 2;
        k.LineInput("To:", "To", m, 140, half, 60);
        k.LineInput("From:", "From", m + half + 24, 140, half, 60);
        k.LineInput("Fax:", "ToFax", m, 172, half, 60);
        k.LineInput("Fax:", "FromFax", m + half + 24, 172, half, 60);
        k.LineInput("Phone:", "ToPhone", m, 204, half, 60);
        k.LineInput("Phone:", "FromPhone", m + half + 24, 204, half, 60);
        k.LineInput("Pages:", "Pages", m, 236, half, 60);
        k.LineInput("Date:", "Date", m + half + 24, 236, half, 60);
        k.LineInput("Re:", "Re", m, 268, w, 60);
        string[] flags = ["Urgent", "For review", "Please comment", "Please reply", "Please recycle"];
        for (int i = 0; i < flags.Length; i++) k.Check(flags[i], "Flag" + (i + 1), m + i * (w / 5), 310, w / 5);
        k.Label(m, 350, w, "Comments");
        k.Box(m, 364, w, 300, Kit.FieldFill, Kit.Rule, 0.75);
        k.Field("Memo", "Comments", m + 4, 368, w - 8, 292, "Comments");
        k.Para(m, k.H - 90, w, "The information in this fax is confidential. If you received it in error, please tell the sender and destroy it.", 8, Kit.Muted);
    }

    private static void Agenda(Kit k)
    {
        k.Accent = Kit.Rgb(14, 116, 144);
        double m = k.M, w = k.W - 2 * m;
        k.Box(0, 0, k.W, 120, Kit.Tint(k.Accent, 0.88));
        k.Text(m, 36, w, 30, "Meeting Agenda", 24, k.Accent, bold: true);
        k.Field("Text", "MeetingTitle", m, 70, w * 0.6, 20, "Meeting title", fontSize: 13);
        k.HRule(m, 90, w * 0.6, k.Accent, 0.75);
        k.Text(m, 94, w * 0.6, 12, "Meeting title", 7.5, Kit.Muted);
        double y = Row(k, 140, ("Date", "Date", 1), ("Time", "Time", 0.8), ("Location / link", "Location", 1.6));
        y = Row(k, y, ("Chair", "Chair", 1), ("Note taker", "NoteTaker", 1));
        y = Memo(k, y, "Attendees", "Attendees", 36);
        k.Text(m, y, w, 18, "Agenda", 13, k.Accent, bold: true);
        y = k.Grid(m, y + 22, w, ["Time", "Topic", "Presenter", "Minutes"], [1, 4, 1.8, 0.9], 9, 26, "Item",
            cells: Enumerable.Range(1, 9).Select(_ => Array.Empty<string>()).ToArray());
        Memo(k, y + 14, "Notes / preparation", "Prep", 50);
    }

    private static void Minutes(Kit k)
    {
        k.Accent = Kit.Rgb(88, 28, 135);
        double m = k.M, w = k.W - 2 * m;
        double y = FormHeader(k, "Meeting Minutes", "Record of discussion, decisions and actions.");
        y = Row(k, y, ("Meeting", "Meeting", 1.6), ("Date", "Date", 0.7), ("Time", "Time", 0.5));
        y = Row(k, y, ("Present", "Present", 1.3), ("Apologies", "Apologies", 1));
        y = Memo(k, y, "Discussion", "Discussion", 140);
        y = Memo(k, y, "Decisions", "Decisions", 60);
        k.Text(m, y, w, 18, "Action items", 12, k.Accent, bold: true);
        k.Grid(m, y + 20, w, ["Action", "Owner", "Due date", "Done"], [4, 1.6, 1.2, 0.6], 6, 22, "Action");
    }

    private static void PressRelease(Kit k)
    {
        k.Accent = Kit.Rgb(190, 18, 60);
        double m = 60, w = k.W - 2 * m;
        k.Text(m, 46, w, 16, "FOR IMMEDIATE RELEASE", 10, k.Accent, bold: true);
        k.Text(m, 64, w, 14, "Contact: Name · press@yourcompany.com · +1 555 000 0000", 9, Kit.Muted);
        k.HRule(m, 86, w, k.Accent, 1.5);
        k.Text(m, 104, w, 60, "Headline That Announces Your News In A Single Line", 22, Kit.Ink, bold: true);
        k.Text(m, 170, w, 32, "A subheading that adds one more useful detail for journalists and readers.", 12, Kit.Muted, italic: true);
        k.Para(m, 214, w, $"CITY, {DateTime.Today:d MMMM yyyy} — Open with the most important facts: who, what, when, where and why. Keep this first paragraph short; many readers stop here.\n\nAdd supporting details, figures and context in the second and third paragraphs.", 10.5);
        k.Box(m, 330, 4, 70, k.Accent);
        k.Para(m + 16, 334, w - 16, "\"Include a short, human quote from a spokesperson that explains why this news matters.\"\n— Name, Job title, Your Company", 11, Kit.Ink, italic: true);
        k.Para(m, 420, w, "Explain availability, pricing or next steps, and where people can find out more.", 10.5);
        k.Text(m, 480, w, 16, "About Your Company", 11, Kit.Ink, bold: true);
        k.Para(m, 500, w, "A short standard paragraph describing your company, what it does and where it operates.", 9.5, Kit.Muted);
        k.Text(m, 560, w, 14, "###", 11, Kit.Muted, align: "Center");
    }

    private static void ResumeClassic(Kit k)
    {
        k.Accent = Kit.Rgb(31, 41, 55);
        double m = 56, w = k.W - 2 * m;
        k.Text(m, 46, w, 34, "YOUR NAME", 26, k.Accent, bold: true, align: "Center");
        k.Text(m, 82, w, 14, "City, Country  ·  you@example.com  ·  +1 555 000 0000  ·  linkedin.com/in/you", 9, Kit.Muted, align: "Center");
        k.HRule(m, 104, w, k.Accent, 1.25);
        double y = 120;
        void Sec(string t) { k.Text(m, y, w, 16, t.ToUpperInvariant(), 10.5, k.Accent, bold: true); k.HRule(m, y + 17, w, Kit.Rule); y += 26; }
        Sec("Profile");
        y = k.Para(m, y, w, "Results-focused professional with eight years' experience in operations and project delivery. Known for clear communication, calm problem solving and building teams that deliver.", 10).Y + 54;
        Sec("Experience");
        foreach (var (role, org, dates) in new[] { ("Operations Manager", "Company Name, City", "2021 – Present"), ("Project Coordinator", "Company Name, City", "2017 – 2021"), ("Administrator", "Company Name, City", "2015 – 2017") })
        {
            k.Text(m, y, w - 120, 16, role, 11, Kit.Ink, bold: true);
            k.Text(k.W - m - 120, y, 120, 16, dates, 9.5, Kit.Muted, align: "Right");
            k.Text(m, y + 16, w, 14, org, 9.5, Kit.Muted, italic: true);
            k.Para(m + 10, y + 34, w - 10, "• Led a key responsibility and the result it achieved, with a number if you can.\n• Improved a process, saving time or money.\n• Managed people, budget or stakeholders.", 9.5);
            y += 100;
        }
        Sec("Education");
        k.Text(m, y, w - 120, 16, "Degree, Subject", 11, Kit.Ink, bold: true);
        k.Text(k.W - m - 120, y, 120, 16, "2011 – 2015", 9.5, Kit.Muted, align: "Right");
        k.Text(m, y + 16, w, 14, "University Name", 9.5, Kit.Muted, italic: true);
        y += 44;
        Sec("Skills");
        k.Para(m, y, w, "Project management  ·  Budgeting  ·  Process improvement  ·  Stakeholder management  ·  Microsoft Office  ·  Reporting", 10);
    }

    private static void ResumeModern(Kit k)
    {
        k.Accent = Kit.Rgb(13, 148, 136);
        double m = 44, w = k.W - 2 * m;
        k.Box(0, 0, k.W, 130, k.Accent);
        k.Circle(m, 25, 80, 80, Kit.Alpha(Kit.White, 60), Kit.White, 2);
        k.Text(m + 100, 36, w - 100, 32, "Alex Morgan", 26, Kit.White, bold: true);
        k.Text(m + 100, 70, w - 100, 18, "Marketing Specialist", 13, Kit.Alpha(Kit.White, 220));
        k.Text(m + 100, 94, w - 100, 14, "alex@example.com   ·   +1 555 000 0000   ·   City, Country", 9, Kit.Alpha(Kit.White, 200));
        double side = 170, mx = m + side + 24, mw = k.W - mx - m;
        double y = 156;
        void SideHead(string t) { k.Text(m, y, side, 14, t.ToUpperInvariant(), 9.5, k.Accent, bold: true); y += 20; }
        SideHead("Skills");
        string[] skills = ["Content strategy", "SEO and SEM", "Social media", "Analytics", "Copywriting", "Email marketing"];
        foreach (var (s, i) in skills.Select((s, i) => (s, i)))
        {
            k.Text(m, y, side, 13, s, 9, Kit.Body);
            k.Box(m, y + 15, side, 4, Kit.Fill, radius: 2);
            k.Box(m, y + 15, side * (0.95 - i * 0.08), 4, k.Accent, radius: 2);
            y += 28;
        }
        y += 10;
        SideHead("Languages");
        k.Para(m, y, side, "English — native\nSpanish — fluent\nFrench — conversational", 9);
        y += 60;
        SideHead("Education");
        k.Para(m, y, side, "BA Marketing\nUniversity Name\n2014 – 2017", 9);

        y = 156;
        void Head(string t) { k.Text(mx, y, mw, 16, t.ToUpperInvariant(), 11, k.Accent, bold: true); k.HRule(mx, y + 18, mw, Kit.Tint(k.Accent, 0.6), 1); y += 28; }
        Head("About me");
        y = k.Para(mx, y, mw, "Creative marketer who turns data into campaigns people remember. I've grown audiences, launched brands and led small teams across B2B and consumer markets.", 10).Y + 66;
        Head("Experience");
        foreach (var (role, org, dates) in new[] { ("Senior Marketing Specialist", "Brightside Media", "2021 – now"), ("Marketing Executive", "Northwind Retail", "2018 – 2021"), ("Marketing Assistant", "Contoso Ltd", "2017 – 2018") })
        {
            k.Circle(mx - 14, y + 4, 7, 7, k.Accent);
            k.Text(mx, y, mw - 90, 16, role, 11, Kit.Ink, bold: true);
            k.Text(mx + mw - 90, y, 90, 14, dates, 9, Kit.Muted, align: "Right");
            k.Text(mx, y + 16, mw, 14, org, 9.5, k.Accent);
            k.Para(mx, y + 32, mw, "Grew organic traffic 140% in a year; ran campaigns across six channels; managed a $250k budget.", 9.5);
            y += 92;
        }
    }

    private static void CoverLetter(Kit k)
    {
        k.Accent = Kit.Rgb(13, 148, 136);
        double m = 56, w = k.W - 2 * m;
        k.Box(0, 0, k.W, 90, k.Accent);
        k.Text(m, 26, w, 30, "Alex Morgan", 24, Kit.White, bold: true);
        k.Text(m, 58, w, 14, "alex@example.com   ·   +1 555 000 0000   ·   City, Country", 9.5, Kit.Alpha(Kit.White, 210));
        k.Text(m, 120, w, 14, DateTime.Today.ToString("d MMMM yyyy"), 10, Kit.Body);
        k.Text(m, 148, w, 56, "Hiring Manager\nCompany Name\nAddress\nCity, Postcode", 10, Kit.Body);
        k.Text(m, 222, w, 16, "Re: Application for Job Title", 11, Kit.Ink, bold: true);
        k.Text(m, 250, w, 16, "Dear Hiring Manager,", 10.5, Kit.Ink);
        k.Para(m, 276, w, "Open with the role you're applying for and one sentence on why you're excited about it and the company.\n\nIn the middle paragraph, connect two or three of your strongest achievements to what the job needs. Use numbers where you can.\n\nShow you understand the company: what it does, what it values, and how you'd contribute in your first months.\n\nClose by thanking the reader and saying you'd welcome the chance to discuss the role.", 10.5);
        k.Text(m, 530, w, 16, "Yours sincerely,", 10.5, Kit.Ink);
        k.Text(m, 580, w, 16, "Alex Morgan", 11, Kit.Ink, bold: true);
    }
}
