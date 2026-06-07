using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DebtManager.Reporting.Models;

namespace DebtManager.Desktop.Services;

/// <summary>
/// Renders a GeneratedReport to a FlowDocument and prints via PrintDialog.
/// No business logic — pure rendering.
/// </summary>
public static class ReportPrintService
{
    public static void Print(GeneratedReport report)
    {
        var doc = BuildFlowDocument(report);

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
        {
            doc.PageWidth = dialog.PrintableAreaWidth;
            doc.PageHeight = dialog.PrintableAreaHeight;
            doc.PagePadding = new Thickness(40);
            doc.ColumnWidth = dialog.PrintableAreaWidth;

            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            dialog.PrintDocument(paginator, $"Report: {report.Definition.Title}");
        }
    }

    private static FlowDocument BuildFlowDocument(GeneratedReport report)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            PagePadding = new Thickness(40)
        };

        // Title
        var title = new Paragraph(new Run(report.Definition.Title))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        doc.Blocks.Add(title);

        // Subtitle
        var subtitle = new Paragraph(new Run($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm}"))
        {
            FontSize = 10,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 16)
        };
        doc.Blocks.Add(subtitle);

        foreach (var section in report.Sections)
        {
            // Section header
            var sectionHeader = new Paragraph(new Run(section.Title))
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 6)
            };
            doc.Blocks.Add(sectionHeader);

            if (section.Data is ReportTable table)
            {
                var wpfTable = new Table { CellSpacing = 0 };

                foreach (var _ in table.Headers)
                    wpfTable.Columns.Add(new TableColumn());

                // Header row
                var headerGroup = new TableRowGroup();
                var headerRow = new TableRow { Background = Brushes.LightGray };
                foreach (var header in table.Headers)
                {
                    var cell = new TableCell(new Paragraph(new Run(header) { FontWeight = FontWeights.Bold }))
                    {
                        Padding = new Thickness(4, 2, 4, 2),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(0, 0, 0, 1)
                    };
                    headerRow.Cells.Add(cell);
                }
                headerGroup.Rows.Add(headerRow);
                wpfTable.RowGroups.Add(headerGroup);

                // Data rows
                var dataGroup = new TableRowGroup();
                foreach (var row in table.Rows)
                {
                    var dataRow = new TableRow();
                    foreach (var value in row)
                    {
                        var cell = new TableCell(new Paragraph(new Run(value ?? string.Empty)))
                        {
                            Padding = new Thickness(4, 2, 4, 2),
                            BorderBrush = Brushes.LightGray,
                            BorderThickness = new Thickness(0, 0, 0, 1)
                        };
                        dataRow.Cells.Add(cell);
                    }
                    dataGroup.Rows.Add(dataRow);
                }
                wpfTable.RowGroups.Add(dataGroup);

                doc.Blocks.Add(wpfTable);
            }
            else if (section.Data is SummaryData summary)
            {
                foreach (var line in summary.Lines)
                {
                    var para = new Paragraph();
                    para.Inlines.Add(new Run($"{line.Label}: ") { FontWeight = FontWeights.SemiBold });
                    para.Inlines.Add(new Run(line.Value));
                    para.Margin = new Thickness(0, 1, 0, 1);
                    doc.Blocks.Add(para);
                }
            }
        }

        return doc;
    }
}
