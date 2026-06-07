namespace DebtManager.Reporting.Models;

/// <summary>
/// Defines a report that can be generated.
/// </summary>
public sealed record ReportDefinition(
    string ReportId,
    string Title,
    string Category,
    ReportParameterSet Parameters
);

/// <summary>
/// Parameters that can be applied to filter/scope a report.
/// </summary>
public sealed record ReportParameterSet
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public IReadOnlyList<Guid>? AccountIds { get; init; }
    public IReadOnlyList<string>? Categories { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? CurrencyCode { get; init; }
}

/// <summary>
/// A generated report produced deterministically from projection states.
/// </summary>
public sealed record GeneratedReport(
    ReportDefinition Definition,
    IReadOnlyList<ReportSection> Sections,
    DateTimeOffset GeneratedAt
);

/// <summary>
/// A single section within a generated report.
/// </summary>
public sealed record ReportSection(
    string Title,
    ReportSectionKind Kind,
    object Data
);

/// <summary>
/// The kind of data a report section contains.
/// </summary>
public enum ReportSectionKind
{
    Summary,
    Table,
    ChartData
}

/// <summary>
/// A table of data with headers and rows for rendering/export.
/// </summary>
public sealed record ReportTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows
);

/// <summary>
/// A single key-value summary line.
/// </summary>
public sealed record SummaryLine(string Label, string Value);

/// <summary>
/// A collection of summary lines.
/// </summary>
public sealed record SummaryData(IReadOnlyList<SummaryLine> Lines);

/// <summary>
/// A data point for chart rendering.
/// </summary>
public sealed record ChartDataPoint(string Label, decimal Value);

/// <summary>
/// A collection of chart data points.
/// </summary>
public sealed record ChartDataSeries(
    string SeriesName,
    IReadOnlyList<ChartDataPoint> Points
);
