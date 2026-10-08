using System.Globalization;

namespace PdfEdit.Templates;

// Lesson plans, worksheets and paper; planners, calendars, checklists and budgets.
public static partial class TemplateCatalog
{
    private static IEnumerable<PdfTemplate> EducationTemplates() =>
    [
        T("LessonPlan", "Lesson plan", Education, "Objectives, materials, activities with timings and assessment.", LessonPlan,
            tags: ["teacher", "school", "class"], fillable: true),
        T("Worksheet", "Worksheet", Education, "A student worksheet with name, questions and answer lines.", Worksheet,
            tags: ["school", "homework", "quiz", "test", "student"], fillable: true),
        T("Attendance", "Class attendance", Education, "A register with a column for each day of the week.", Attendance,
            tags: ["register", "roll call", "school"], fillable: true),
        T("LinedPaper", "Lined paper", Education, "Ruled writing paper with a margin.", LinedPaper, tags: ["notes", "writing", "ruled"]),
        T("GraphPaper", "Graph paper", Education, "A 5 mm squared grid.", GraphPaper, tags: ["grid", "squared", "maths", "math"]),
        T("DotGrid", "Dot grid paper", Education, "Dot grid for bullet journals and sketching.", DotGrid, tags: ["bullet journal", "dots", "sketch"]),
        T("CornellNotes", "Cornell notes", Education, "Cue column, notes area and summary.", CornellNotes, tags: ["study", "notes", "student"]),
    ];

    private static IEnumerable<PdfTemplate> PlanningTemplates() =>
    [
        T("WeeklyPlanner", "Weekly planner", Planning, "Seven days with priorities and notes.", WeeklyPlanner, tags: ["schedule", "week", "diary"],
            fillable: true),
        T("DailyPlanner", "Daily planner", Planning, "Hour-by-hour schedule, top priorities and to-dos.", DailyPlanner,
            tags: ["schedule", "day", "agenda"], fillable: true),
        T("MonthlyCalendar", "Monthly calendar", Planning, "This month on one landscape page.", MonthlyCalendar, landscape: true,
            tags: ["calendar", "month", "schedule"]),
        T("YearCalendar", "Year calendar", Planning, "All twelve months of this year at a glance.", YearCalendar,
            tags: ["calendar", "year"]),
        T("Checklist", "To-do checklist", Planning, "Tick boxes with priority and due dates.", Checklist,
            tags: ["todo", "to do", "tasks", "list"], fillable: true),
        T("Budget", "Monthly budget", Planning, "Income, expenses by category and what's left.", Budget,
            tags: ["money", "finance", "expenses", "household"], fillable: true),
        T("Goals", "Goal planner", Planning, "A goal broken into steps, with deadlines and progress.", Goals,
            tags: ["targets", "objectives", "habits"], fillable: true),
        T("MealPlanner", "Meal planner", Planning, "Breakfast, lunch and dinner for the week, plus a shopping list.", MealPlanner,
            tags: ["food", "recipes", "groceries", "shopping"], fillable: true),
        T("TravelItinerary", "Travel itinerary", Planning, "Flights, accommodation and a day-by-day plan.", TravelItinerary,
            tags: ["trip", "holiday", "vacation", "travel"], fillable: true),
    ];

    private static void LessonPlan(Kit k)
    {
        k.Accent = Kit.Rgb(234, 88, 12);
        double y = FormHeader(k, "Lesson Plan", "Plan the lesson, then note how it went.");
        y = Row(k, y, ("Teacher", "Teacher", 1), ("Subject", "Subject", 1), ("Class / year", "Class", 0.6), ("Date", "Date", 0.6));
        y = Row(k, y, ("Lesson title", "Title", 1.6), ("Duration", "Duration", 0.5));
        y = Memo(k, y, "Learning objectives", "Objectives", 50);
        y = Memo(k, y, "Materials and resources", "Materials", 34);
        k.Text(k.M, y, k.W - 2 * k.M, 18, "Lesson outline", 12, k.Accent, bold: true);
        y = k.Grid(k.M, y + 20, k.W - 2 * k.M, ["Time", "Activity", "Teacher does", "Students do"], [0.8, 2, 2.2, 2.2], 5, 30, "Step",
            cells: [["Starter"], ["Main"], ["Main"], ["Plenary"], ["Homework"]]) + 14;
        y = Memo(k, y, "Assessment and differentiation", "Assessment", 40);
        Memo(k, y, "Reflection", "Reflection", 40);
    }

    private static void Worksheet(Kit k)
    {
        k.Accent = Kit.Rgb(22, 163, 74);
        double m = k.M, w = k.W - 2 * m;
        k.Box(m, 30, w, 50, Kit.Tint(k.Accent, 0.88), radius: 6);
        k.Text(m + 14, 42, w - 28, 26, "Worksheet title", 18, k.Accent, bold: true);
        k.LineInput("Name:", "Name", m, 96, w * 0.55, 46);
        k.LineInput("Class:", "Class", m + w * 0.58, 96, w * 0.22, 40);
        k.LineInput("Date:", "Date", m + w * 0.82, 96, w * 0.18, 36);
        k.Para(m, 132, w, "Instructions: read each question carefully and write your answer on the lines.", 10, Kit.Muted, italic: true);
        double y = 160;
        for (int q = 1; q <= 6; q++)
        {
            k.Circle(m, y, 22, 22, k.Accent);
            k.Text(m, y + 4, 22, 14, q.ToString(), 11, Kit.White, bold: true, align: "Center");
            k.Text(m + 32, y + 3, w - 32, 16, "Question " + q + " goes here?", 11, Kit.Ink);
            k.Lines(m + 32, y + 18, w - 32, 3, 22, Kit.Faint);
            k.Field("Memo", "Answer" + q, m + 32, y + 24, w - 32, 64, "Answer " + q);
            y += 104;
        }
    }

    private static void Attendance(Kit k)
    {
        k.Accent = Kit.Rgb(79, 70, 229);
        double m = 36, w = k.W - 2 * m;
        k.M = m;
        double y = FormHeader(k, "Class Attendance", "P = present  ·  A = absent  ·  L = late  ·  E = excused");
        y = Row(k, y, ("Class", "Class", 1), ("Teacher", "Teacher", 1), ("Week of", "WeekOf", 0.7));
        k.Grid(m, y + 4, w, ["#", "Student name", "Mon", "Tue", "Wed", "Thu", "Fri", "Notes"], [0.4, 3, 0.7, 0.7, 0.7, 0.7, 0.7, 2], 25, 22,
            "Att", cells: Enumerable.Range(1, 25).Select(i => new[] { i.ToString() }).ToArray(),
            align: ["Center", "Left", "Center", "Center", "Center", "Center", "Center", "Left"]);
    }

    private static void LinedPaper(Kit k)
    {
        k.Text(56, 36, 300, 16, "Date: ____________", 10, Kit.Faint);
        k.VRule(80, 0, k.H, Kit.Rgb(248, 113, 113), 1);
        for (double y = 80; y < k.H - 30; y += 24) k.HRule(0, y, k.W, Kit.Rgb(191, 219, 254), 0.6);
    }

    private static void GraphPaper(Kit k)
    {
        double step = 14.17;   // 5 mm
        double x0 = 28, y0 = 28, w = Math.Floor((k.W - 56) / step) * step, h = Math.Floor((k.H - 56) / step) * step;
        for (int i = 0; i * step <= w + 0.1; i++)
            k.VRule(x0 + i * step, y0, h, i % 5 == 0 ? Kit.Rgb(147, 197, 253) : Kit.Rgb(219, 234, 254), i % 5 == 0 ? 0.8 : 0.4);
        for (int i = 0; i * step <= h + 0.1; i++)
            k.HRule(x0, y0 + i * step, w, i % 5 == 0 ? Kit.Rgb(147, 197, 253) : Kit.Rgb(219, 234, 254), i % 5 == 0 ? 0.8 : 0.4);
    }

    private static void DotGrid(Kit k)
    {
        double step = 14.17;
        for (double y = 36; y < k.H - 30; y += step)
            for (double x = 32; x < k.W - 28; x += step)
                k.Circle(x - 0.8, y - 0.8, 1.6, 1.6, Kit.Faint);
    }

    private static void CornellNotes(Kit k)
    {
        k.Accent = Kit.Rgb(71, 85, 105);
        double m = 36, w = k.W - 2 * m;
        k.Text(m, 30, w * 0.6, 22, "Topic:", 13, Kit.Ink, bold: true);
        k.Field("Text", "Topic", m + 50, 30, w * 0.6 - 50, 20, "Topic", fontSize: 13);
        k.LineInput("Date:", "Date", m + w * 0.65, 30, w * 0.35, 36);
        k.HRule(m, 60, w, Kit.Ink, 1.5);
        double cue = w * 0.3, bottom = k.H - 170;
        k.Text(m, 66, cue - 10, 14, "CUES / QUESTIONS", 8.5, Kit.Muted, bold: true);
        k.Text(m + cue + 10, 66, w - cue - 10, 14, "NOTES", 8.5, Kit.Muted, bold: true);
        k.VRule(m + cue, 60, bottom - 60, Kit.Ink, 1);
        k.Lines(m, 82, w, (int)((bottom - 90) / 22), 22, Kit.Rule);
        k.Field("Memo", "Cues", m + 2, 84, cue - 6, bottom - 90, "Cues and questions");
        k.Field("Memo", "Notes", m + cue + 6, 84, w - cue - 8, bottom - 90, "Notes");
        k.HRule(m, bottom, w, Kit.Ink, 1.5);
        k.Text(m, bottom + 6, w, 14, "SUMMARY", 8.5, Kit.Muted, bold: true);
        k.Lines(m, bottom + 20, w, 5, 22, Kit.Rule);
        k.Field("Memo", "Summary", m + 2, bottom + 22, w - 4, 110, "Summary");
    }

    private static void WeeklyPlanner(Kit k)
    {
        k.Accent = Kit.Rgb(13, 148, 136);
        double m = 32, w = k.W - 2 * m;
        k.Text(m, 28, w * 0.5, 30, "Weekly Planner", 22, k.Accent, bold: true);
        k.LineInput("Week of:", "WeekOf", m + w * 0.55, 32, w * 0.45, 56);
        string[] days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
        double col = (w - 12) / 2, bh = 160, y0 = 74;
        for (int i = 0; i < days.Length; i++)
        {
            double x = m + i % 2 * (col + 12), y = y0 + i / 2 * (bh + 10);
            k.Box(x, y, col, bh, Kit.White, Kit.Rule, 0.75, 4);
            k.Box(x, y, col, 22, Kit.Tint(k.Accent, 0.85), radius: 4);
            k.Text(x + 8, y + 5, col - 16, 14, days[i].ToUpperInvariant(), 9.5, k.Accent, bold: true);
            k.Lines(x + 8, y + 22, col - 16, 6, 22, Kit.Fill);
            k.Field("Memo", "Day" + (i + 1), x + 4, y + 26, col - 8, bh - 30, days[i]);
        }
        double nx = m + col + 12, ny = y0 + 3 * (bh + 10);
        k.Box(nx, ny, col, bh, Kit.Tint(k.Accent, 0.92), radius: 4);
        k.Text(nx + 8, ny + 6, col - 16, 14, "PRIORITIES & NOTES", 9.5, k.Accent, bold: true);
        k.Field("Memo", "Notes", nx + 4, ny + 26, col - 8, bh - 30, "Priorities and notes");
    }

    private static void DailyPlanner(Kit k)
    {
        k.Accent = Kit.Rgb(219, 39, 119);
        double m = 40, w = k.W - 2 * m, left = w * 0.56, rx = m + left + 20, rw = w - left - 20;
        k.Text(m, 30, w * 0.5, 30, "Daily Planner", 22, k.Accent, bold: true);
        k.LineInput("Date:", "Date", m + w * 0.55, 34, w * 0.45, 40);
        k.Text(m, 76, left, 14, "SCHEDULE", 9.5, k.Accent, bold: true);
        for (int i = 0; i < 15; i++)
        {
            double y = 94 + i * 42;
            k.Text(m, y + 4, 44, 14, $"{6 + i:00}:00", 9, Kit.Muted, bold: true);
            k.HRule(m + 48, y + 40, left - 48, Kit.Rule, 0.6);
            k.Field("Text", $"Hour{6 + i}", m + 50, y + 2, left - 52, 36, $"{6 + i}:00", fontSize: 9);
        }
        k.Text(rx, 76, rw, 14, "TOP 3 PRIORITIES", 9.5, k.Accent, bold: true);
        for (int i = 0; i < 3; i++)
        {
            k.Circle(rx, 96 + i * 30, 20, 20, Kit.Tint(k.Accent, 0.85));
            k.Text(rx, 99 + i * 30, 20, 14, (i + 1).ToString(), 10, k.Accent, bold: true, align: "Center");
            k.HRule(rx + 26, 116 + i * 30, rw - 26, Kit.Rule);
            k.Field("Text", "Priority" + (i + 1), rx + 28, 96 + i * 30, rw - 30, 18, "Priority " + (i + 1));
        }
        k.Text(rx, 200, rw, 14, "TO DO", 9.5, k.Accent, bold: true);
        for (int i = 0; i < 12; i++)
        {
            k.Check("", "Done" + (i + 1), rx, 222 + i * 26, 14);
            k.HRule(rx + 20, 236 + i * 26, rw - 20, Kit.Rule);
            k.Field("Text", "Todo" + (i + 1), rx + 20, 220 + i * 26, rw - 20, 16, "To do " + (i + 1));
        }
        k.Text(rx, 548, rw, 14, "NOTES", 9.5, k.Accent, bold: true);
        k.Box(rx, 566, rw, 150, Kit.Fill, radius: 4);
        k.Field("Memo", "Notes", rx + 4, 570, rw - 8, 142, "Notes");
        k.Text(rx, 730, rw, 14, "WATER", 9.5, k.Accent, bold: true);
        for (int i = 0; i < 8; i++) k.Circle(rx + i * (rw / 8), 748, 16, 16, Kit.Clear, Kit.Tint(k.Accent, 0.4), 1);
    }

    private static void MonthlyCalendar(Kit k)
    {
        k.Accent = Kit.Rgb(37, 99, 235);
        var first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        double m = 30, w = k.W - 2 * m;
        k.Text(m, 22, w, 34, first.ToString("MMMM", CultureInfo.InvariantCulture), 26, k.Accent, bold: true);
        k.Text(m, 26, w, 30, first.Year.ToString(), 20, Kit.Faint, bold: true, align: "Right");
        string[] names = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
        double cw = w / 7, top = 66;
        k.Box(m, top, w, 20, k.Accent);
        for (int c = 0; c < 7; c++) k.Text(m + c * cw, top + 4, cw, 12, names[c].ToUpperInvariant(), 8, Kit.White, bold: true, align: "Center");
        int offset = ((int)first.DayOfWeek + 6) % 7, days = DateTime.DaysInMonth(first.Year, first.Month);
        int weeks = (offset + days + 6) / 7;
        double rh = (k.H - top - 20 - 30) / weeks;
        for (int r = 0; r < weeks; r++)
            for (int c = 0; c < 7; c++)
            {
                double x = m + c * cw, y = top + 20 + r * rh;
                int day = r * 7 + c - offset + 1;
                bool inMonth = day >= 1 && day <= days;
                k.Box(x, y, cw, rh, inMonth ? (c >= 5 ? Kit.Fill : Kit.White) : Kit.Tint(Kit.Fill, 0.4), Kit.Rule, 0.5);
                if (inMonth) k.Text(x + 6, y + 4, cw - 12, 14, day.ToString(), 11, c >= 5 ? k.Accent : Kit.Ink, bold: true);
            }
    }

    private static void YearCalendar(Kit k)
    {
        k.Accent = Kit.Rgb(190, 18, 60);
        int year = DateTime.Today.Year;
        double m = 32, w = k.W - 2 * m;
        k.Text(m, 26, w, 40, year.ToString(), 32, k.Accent, bold: true, align: "Center");
        double cw = (w - 2 * 20) / 3, ch = 170;
        for (int mo = 0; mo < 12; mo++)
        {
            double x = m + mo % 3 * (cw + 20), y = 86 + mo / 3 * (ch + 12);
            var first = new DateTime(year, mo + 1, 1);
            k.Text(x, y, cw, 16, first.ToString("MMMM", CultureInfo.InvariantCulture).ToUpperInvariant(), 10.5, k.Accent, bold: true);
            double dw = cw / 7;
            string[] d = ["M", "T", "W", "T", "F", "S", "S"];
            for (int c = 0; c < 7; c++) k.Text(x + c * dw, y + 20, dw, 12, d[c], 7.5, Kit.Muted, bold: true, align: "Center");
            int offset = ((int)first.DayOfWeek + 6) % 7, days = DateTime.DaysInMonth(year, mo + 1);
            for (int day = 1; day <= days; day++)
            {
                int cell = offset + day - 1;
                k.Text(x + cell % 7 * dw, y + 36 + cell / 7 * 20, dw, 12, day.ToString(), 8.5, cell % 7 >= 5 ? k.Accent : Kit.Body, align: "Center");
            }
        }
    }

    private static void Checklist(Kit k)
    {
        k.Accent = Kit.Rgb(22, 163, 74);
        double m = k.M, w = k.W - 2 * m;
        k.Text(m, 34, w * 0.6, 30, "To-Do List", 24, k.Accent, bold: true);
        k.LineInput("Date:", "Date", m + w * 0.65, 40, w * 0.35, 40);
        k.Box(m, 80, w, 24, Kit.Tint(k.Accent, 0.85), radius: 3);
        k.Text(m + 30, 86, w * 0.6, 14, "TASK", 8.5, k.Accent, bold: true);
        k.Text(m + w * 0.68, 86, 70, 14, "PRIORITY", 8.5, k.Accent, bold: true);
        k.Text(m + w * 0.85, 86, 70, 14, "DUE", 8.5, k.Accent, bold: true);
        for (int i = 0; i < 24; i++)
        {
            double y = 112 + i * 27;
            k.Check("", "Done" + (i + 1), m + 6, y + 4, 14, 13);
            k.Field("Text", "Task" + (i + 1), m + 30, y, w * 0.62, 20, "Task " + (i + 1));
            k.Field("ComboBox", "Priority" + (i + 1), m + w * 0.68, y, w * 0.14, 20, "Priority", options: ",High,Medium,Low");
            k.Field("Text", "Due" + (i + 1), m + w * 0.85, y, w * 0.15, 20, "Due date");
            k.HRule(m, y + 23, w, Kit.Rule, 0.5);
        }
    }

    private static void Budget(Kit k)
    {
        k.Accent = Kit.Rgb(5, 150, 105);
        double m = 40, w = k.W - 2 * m, half = (w - 20) / 2;
        k.M = m;
        double y = FormHeader(k, "Monthly Budget", "Plan what comes in and goes out, then compare with what actually happened.");
        y = Row(k, y, ("Month", "Month", 1), ("Savings goal", "SavingsGoal", 1));
        k.SectionBar(m, y, w, "Income");
        y = k.Grid(m, y + 24, w, ["Source", "Planned", "Actual"], [3, 1, 1], 4, 20, "Income",
            cells: [["Salary"], ["Other income"], [""], [""]], headerFill: Kit.Fill, headerText: Kit.Ink) + 14;
        k.SectionBar(m, y, w, "Expenses", Kit.Rgb(220, 38, 38));
        string[] cats = ["Rent / mortgage", "Utilities", "Groceries", "Transport", "Insurance", "Phone and internet", "Eating out", "Entertainment", "Savings", "Debt repayments", "Other"];
        y = k.Grid(m, y + 24, w, ["Category", "Planned", "Actual", "Difference"], [3, 1, 1, 1], cats.Length, 20, "Expense",
            cells: cats.Select(c => new[] { c }).ToArray(), headerFill: Kit.Fill, headerText: Kit.Ink);
        k.Totals(k.W - m, y + 12, "Summary", "Total income", "Total expenses", "Left over");
    }

    private static void Goals(Kit k)
    {
        k.Accent = Kit.Rgb(124, 58, 237);
        double m = k.M, w = k.W - 2 * m;
        double y = FormHeader(k, "Goal Planner", "Write the goal, why it matters, and the steps to get there.");
        y = Memo(k, y, "My goal", "Goal", 40);
        y = Row(k, y, ("Start date", "Start", 1), ("Target date", "Target", 1), ("How I'll measure it", "Measure", 1.6));
        y = Memo(k, y, "Why it matters to me", "Why", 44);
        k.Text(m, y, w, 18, "Action steps", 12, k.Accent, bold: true);
        y = k.Grid(m, y + 20, w, ["Step", "Deadline", "Done"], [5, 1.3, 0.6], 8, 24, "Step") + 16;
        y = Memo(k, y, "Possible obstacles and how I'll handle them", "Obstacles", 44);
        k.Label(m, y, w, "Progress");
        for (int i = 0; i < 10; i++)
        {
            k.Box(m + i * (w / 10), y + 14, w / 10 - 4, 20, Kit.Tint(k.Accent, 0.9), Kit.Tint(k.Accent, 0.6), 0.75, 3);
            k.Field("Checkbox", "Progress" + (i + 1), m + i * (w / 10), y + 14, w / 10 - 4, 20, $"{(i + 1) * 10}%", labelPos: "None");
            k.Text(m + i * (w / 10), y + 38, w / 10 - 4, 12, $"{(i + 1) * 10}%", 7.5, Kit.Muted, align: "Center");
        }
    }

    private static void MealPlanner(Kit k)
    {
        k.Accent = Kit.Rgb(234, 88, 12);
        double m = 32, w = k.W - 2 * m;
        k.M = m;
        k.Text(m, 28, w * 0.5, 30, "Meal Planner", 22, k.Accent, bold: true);
        k.LineInput("Week of:", "WeekOf", m + w * 0.55, 32, w * 0.45, 56);
        string[] days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
        double y = k.Grid(m, 74, w, ["", "Breakfast", "Lunch", "Dinner", "Snacks"], [0.6, 2, 2, 2, 1.5], 7, 58, "Meal",
            cells: days.Select(d => new[] { d }).ToArray()) + 18;
        k.SectionBar(m, y, w, "Shopping list");
        double col = (w - 20) / 3;
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < 8; i++)
            {
                double x = m + c * (col + 10), ry = y + 30 + i * 22;
                k.Check("", $"Got{c * 8 + i + 1}", x, ry + 3, 14, 11);
                k.HRule(x + 18, ry + 17, col - 18, Kit.Rule, 0.5);
                k.Field("Text", $"Shop{c * 8 + i + 1}", x + 18, ry, col - 18, 16, "Item");
            }
    }

    private static void TravelItinerary(Kit k)
    {
        k.Accent = Kit.Rgb(2, 132, 199);
        double m = 40, w = k.W - 2 * m;
        k.M = m;
        k.Box(0, 0, k.W, 96, k.Accent);
        k.Text(m, 28, w, 30, "Travel Itinerary", 24, Kit.White, bold: true);
        k.Text(m, 60, w, 16, "Destination  ·  Dates  ·  Travellers", 10.5, Kit.Alpha(Kit.White, 210));
        double y = Row(k, 116, ("Destination", "Destination", 1.4), ("Depart", "Depart", 0.7), ("Return", "Return", 0.7));
        k.SectionBar(m, y, w, "Flights");
        y = k.Grid(m, y + 24, w, ["Date", "Flight", "From", "To", "Departs", "Arrives", "Ref"], [1, 1, 1.4, 1.4, 0.9, 0.9, 1.2], 4, 22, "Flight",
            headerFill: Kit.Fill, headerText: Kit.Ink) + 14;
        k.SectionBar(m, y, w, "Accommodation");
        y = k.Grid(m, y + 24, w, ["Hotel / address", "Check in", "Check out", "Booking ref"], [3, 1, 1, 1.4], 3, 22, "Stay",
            headerFill: Kit.Fill, headerText: Kit.Ink) + 14;
        k.SectionBar(m, y, w, "Day by day");
        k.Grid(m, y + 24, w, ["Day", "Plans", "Notes"], [0.7, 4, 2], 7, 30, "Day",
            cells: Enumerable.Range(1, 7).Select(i => new[] { "Day " + i }).ToArray(), headerFill: Kit.Fill, headerText: Kit.Ink);
    }
}
