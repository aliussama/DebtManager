using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Reporting.Models;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Reports view.
/// Renders deterministic reports from projection states.
/// No business logic — only orchestration and rendering.
/// </summary>
public sealed class ReportsViewModel : ObservableObject
{
    private readonly GetAvailableReportsHandler _availableHandler;
    private readonly GenerateReportHandler _generateHandler;
    private readonly IExportService? _exportService;
    private readonly IToastService? _toastService;

    public ReportsViewModel(
        GetAvailableReportsHandler availableHandler,
        GenerateReportHandler generateHandler,
        IExportService? exportService = null,
        IToastService? toastService = null)
    {
        _availableHandler = availableHandler;
        _generateHandler = generateHandler;
        _exportService = exportService;
        _toastService = toastService;

        AvailableReports = new ObservableCollection<AvailableReportDto>();

        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => SelectedReport != null && !IsGenerating);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => GeneratedReport != null && !IsGenerating);
        PrintCommand = new RelayCommand(Print, () => GeneratedReport != null);

        // Default date range: current month
        var today = DateTime.Today;
        FromDate = new DateOnly(today.Year, today.Month, 1);
        ToDate = DateOnly.FromDateTime(today);
    }

    public ObservableCollection<AvailableReportDto> AvailableReports { get; }

    private AvailableReportDto? _selectedReport;
    public AvailableReportDto? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetProperty(ref _selectedReport, value))
            {
                GeneratedReport = null;
                OnPropertyChanged(nameof(HasReports));
            }
        }
    }

    private DateOnly _fromDate;
    public DateOnly FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    private DateOnly _toDate;
    public DateOnly ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    private string _accountFilter = string.Empty;
    public string AccountFilter
    {
        get => _accountFilter;
        set => SetProperty(ref _accountFilter, value);
    }

    private string _tagFilter = string.Empty;
    public string TagFilter
    {
        get => _tagFilter;
        set => SetProperty(ref _tagFilter, value);
    }

    private GeneratedReport? _generatedReport;
    public GeneratedReport? GeneratedReport
    {
        get => _generatedReport;
        set
        {
            if (SetProperty(ref _generatedReport, value))
            {
                OnPropertyChanged(nameof(HasGeneratedReport));
                OnPropertyChanged(nameof(HasReports));
                RebuildDisplaySections();
            }
        }
    }

    public bool HasGeneratedReport => _generatedReport != null;
    public bool HasReports => AvailableReports.Count > 0;

    private bool _isGenerating;
    public bool IsGenerating
    {
        get => _isGenerating;
        set => SetProperty(ref _isGenerating, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ObservableCollection<ReportSectionDisplay> DisplaySections { get; } = new();

    public ICommand GenerateCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand PrintCommand { get; }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var reports = await _availableHandler.HandleAsync(CancellationToken.None);
            AvailableReports.Clear();
            foreach (var r in reports)
                AvailableReports.Add(r);

            OnPropertyChanged(nameof(HasReports));
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load reports", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task GenerateAsync()
    {
        if (SelectedReport == null) return;

        IsGenerating = true;
        try
        {
            var accountIds = ParseGuidList(AccountFilter);
            var tags = ParseStringList(TagFilter);

            var parameters = new ReportParameterSet
            {
                FromDate = FromDate,
                ToDate = ToDate,
                AccountIds = accountIds.Count > 0 ? accountIds : null,
                Tags = tags.Count > 0 ? tags : null
            };

            var definition = new ReportDefinition(
                SelectedReport.ReportId,
                SelectedReport.Title,
                SelectedReport.Category,
                parameters);

            var generatedAt = DateTimeOffset.UtcNow;
            var report = await _generateHandler.HandleAsync(definition, generatedAt, CancellationToken.None);

            GeneratedReport = report;
            _toastService?.Success("Report generated");
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to generate report", ex);
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private async Task ExportCsvAsync()
    {
        if (GeneratedReport == null || _exportService == null) return;

        try
        {
            var tableSections = GeneratedReport.Sections
                .Where(s => s.Kind == ReportSectionKind.Table && s.Data is ReportTable)
                .ToList();

            if (tableSections.Count == 0)
            {
                _toastService?.Info("No table data to export");
                return;
            }

            // Combine all table sections into one CSV
            var allHeaders = new List<string> { "Section" };
            var maxCols = tableSections
                .Select(s => ((ReportTable)s.Data).Headers.Count)
                .DefaultIfEmpty(0)
                .Max();

            // Use headers from first table, padded
            var firstTable = (ReportTable)tableSections[0].Data;
            allHeaders.AddRange(firstTable.Headers);
            for (int i = firstTable.Headers.Count; i < maxCols; i++)
                allHeaders.Add($"Column{i + 1}");

            var allRows = new List<IReadOnlyList<string?>>();

            foreach (var section in tableSections)
            {
                var table = (ReportTable)section.Data;
                foreach (var row in table.Rows)
                {
                    var paddedRow = new List<string?> { section.Title };
                    for (int i = 0; i < maxCols; i++)
                    {
                        if (i < row.Count)
                            paddedRow.Add(row[i]);
                        else
                            paddedRow.Add(null);
                    }
                    allRows.Add(paddedRow);
                }
            }

            var fileName = $"Report_{GeneratedReport.Definition.ReportId}_{FromDate:yyyyMMdd}_{ToDate:yyyyMMdd}.csv";
            await _exportService.ExportCsvAsync(fileName, allHeaders, allRows);
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to export CSV", ex);
        }
    }

    private void Print()
    {
        if (GeneratedReport == null) return;

        try
        {
            ReportPrintService.Print(GeneratedReport);
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to print report", ex);
        }
    }

    private void RebuildDisplaySections()
    {
        DisplaySections.Clear();

        if (GeneratedReport == null) return;

        foreach (var section in GeneratedReport.Sections)
        {
            DisplaySections.Add(new ReportSectionDisplay(section));
        }
    }

    private static List<Guid> ParseGuidList(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<Guid>();
        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();
    }

    private static List<string> ParseStringList(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<string>();
        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }
}

/// <summary>
/// Display wrapper for a ReportSection, pre-extracting typed data for XAML binding.
/// </summary>
public sealed class ReportSectionDisplay
{
    public ReportSectionDisplay(ReportSection section)
    {
        Title = section.Title;
        Kind = section.Kind;

        if (section.Data is ReportTable table)
        {
            Headers = table.Headers;
            Rows = table.Rows;
            IsTable = true;
        }
        else if (section.Data is SummaryData summary)
        {
            SummaryLines = summary.Lines;
            IsSummary = true;
        }
    }

    public string Title { get; }
    public ReportSectionKind Kind { get; }
    public bool IsTable { get; }
    public bool IsSummary { get; }
    public IReadOnlyList<string>? Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>>? Rows { get; }
    public IReadOnlyList<SummaryLine>? SummaryLines { get; }
}