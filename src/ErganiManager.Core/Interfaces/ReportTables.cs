using System.Text;

namespace ErganiManager.Core.Interfaces;

/// <summary>The analysis reports that are shown as generic tables.</summary>
public enum TableReportKind
{
    RoundingImpact,
    SubmissionStatus,
    FailureLog,
    Consistency,
    Roster,
    OvertimeTrend
}

public enum RowStyle { Normal, Group, Total, Warning }

/// <summary>One cell of a report table (fixed width so the columns line up in the screen).</summary>
public sealed class ReportCell
{
    public string Text { get; init; } = string.Empty;
    public double Width { get; init; } = 100;
    public bool IsStrong { get; init; }
    public bool IsWarn { get; init; }
}

public sealed class ReportRow
{
    public List<ReportCell> Cells { get; init; } = new();
    public RowStyle Style { get; init; }
    public bool IsGroup => Style == RowStyle.Group;
    public bool IsTotal => Style == RowStyle.Total;
}

/// <summary>One table of a report: a title, a header and rows. A report may hold several sections.</summary>
public sealed class TableSection
{
    /// <summary>The screen shows at most this many rows of a section (the CSV export has all of them).</summary>
    public const int MaxDisplayRows = 1500;

    private readonly List<(string Header, double Width)> _columns;

    public TableSection(string title, IEnumerable<(string Header, double Width)> columns)
    {
        Title = title;
        _columns = columns.ToList();
    }

    public string Title { get; }
    public string Note { get; set; } = string.Empty;
    public List<ReportRow> Rows { get; } = new();

    public IReadOnlyList<(string Header, double Width)> Columns => _columns;
    public double TotalWidth => _columns.Sum(c => c.Width + 8);

    public ReportRow HeaderRow => new()
    {
        Style = RowStyle.Normal,
        Cells = _columns.Select(c => new ReportCell { Text = c.Header, Width = c.Width, IsStrong = true }).ToList()
    };

    public IEnumerable<ReportRow> DisplayRows => Rows.Take(MaxDisplayRows);

    public string DisplayNote =>
        Rows.Count > MaxDisplayRows
            ? ErganiManager.Data.UiText.F("Showing the first {0} of {1} rows — export to CSV to get all of them.", MaxDisplayRows, Rows.Count)
            : Note;

    public bool HasNote => DisplayNote.Length > 0;
    public bool HasRows => Rows.Count > 0;
    public bool HasTitle => Title.Length > 0;

    /// <summary>Adds a row. Missing cells are empty; a Group row is one wide cell holding the first text.</summary>
    public void Add(RowStyle style, params string[] cells)
    {
        var row = new List<ReportCell>();

        if (style == RowStyle.Group)
        {
            row.Add(new ReportCell { Text = cells.FirstOrDefault() ?? string.Empty, Width = TotalWidth - 8, IsStrong = true });
        }
        else
        {
            for (var i = 0; i < _columns.Count; i++)
                row.Add(new ReportCell
                {
                    Text = i < cells.Length ? cells[i] ?? string.Empty : string.Empty,
                    Width = _columns[i].Width,
                    IsStrong = style == RowStyle.Total,
                    IsWarn = style == RowStyle.Warning
                });
        }

        Rows.Add(new ReportRow { Cells = row, Style = style });
    }

    public void Add(params string[] cells) => Add(RowStyle.Normal, cells);
}

/// <summary>The result of one analysis report: a headline summary and one or more table sections.</summary>
public sealed class TableReport
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<TableSection> Sections { get; } = new();

    public bool HasRows => Sections.Any(s => s.Rows.Count > 0);

    public TableSection AddSection(string title, params (string Header, double Width)[] columns)
    {
        var section = new TableSection(title, columns);
        Sections.Add(section);
        return section;
    }

    /// <summary>CSV with semicolons (opens directly in Greek Excel); sections are separated by a blank line.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        static string Q(string? v)
        {
            var s = v ?? string.Empty;
            // A leading = + - @ makes Excel read the cell as a formula.
            if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = " " + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        foreach (var section in Sections.Where(s => s.Rows.Count > 0))
        {
            if (sb.Length > 0) sb.AppendLine();
            if (section.HasTitle) sb.AppendLine(Q(section.Title));
            sb.AppendLine(string.Join(";", section.Columns.Select(c => Q(c.Header))));
            foreach (var row in section.Rows)
                sb.AppendLine(string.Join(";", row.Cells.Select(c => Q(c.Text))));
        }

        return sb.ToString();
    }
}
