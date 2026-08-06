using System.Text;
using FileComparerWindows.Comparison;
using FileComparerWindows.Configuration;

namespace FileComparerWindows.Reporting;

/// <summary>
/// The report the console tool prints, built as text instead. The window shows the same facts in its
/// grids, but a comparison is usually run to be passed on to somebody, and a block of text pastes
/// into a mail or a ticket where a grid does not.
/// </summary>
public static class TextReport
{
    public static string Build(ComparisonResult result, ComparisonOptions options)
    {
        StringBuilder text = new StringBuilder();

        WriteHeader(text, result, options);
        WriteCounts(text, result);
        WriteWarnings(text, result);

        if (options.ShowNonMatchingRows)
            WriteNonMatchingRows(text, result, options.MaxNonMatchingRowsToShow);
        else if (!result.IsMatch)
            text.AppendLine("Turn 'Show non-matching rows' on to list the differing rows.").AppendLine();

        WriteVerdict(text, result);

        return text.ToString();
    }

    private static void WriteHeader(StringBuilder text, ComparisonResult result, ComparisonOptions options)
    {
        WriteRule(text);
        text.AppendLine("  FILE COMPARISON");
        WriteRule(text);
        text.AppendLine($"  Run at        : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"  Input file    : {result.Input.SourcePath}");
        text.AppendLine($"                  {result.Input.FormatName}, {result.InputRowCount} data row(s)");
        text.AppendLine($"  Output file   : {result.Output.SourcePath}");
        text.AppendLine($"                  {result.Output.FormatName}, {result.OutputRowCount} data row(s)");
        text.AppendLine($"  Key column(s) : {string.Join(", ", result.KeyColumns)}");
        text.AppendLine($"  Compared      : {string.Join(", ", result.ComparedColumns)}");

        if (result.SkippedColumns.Count > 0)
            text.AppendLine($"  Skipped       : {string.Join(", ", result.SkippedColumns)}");

        // A run that passes only because 123.00 was read as 123, or because 99 was near enough to 100,
        // should say so.
        if (options.SimilarMatch)
        {
            text.AppendLine("  SimilarMatch  : on - numbers compared by value, not by how they are written");

            if (options.SimilarMatchRange > 0)
                text.AppendLine($"  Range         : ±{FormatRange(options.SimilarMatchRange)} - numbers this far apart still count as equal");
        }

        if (options.IgnoreCase)
            text.AppendLine("  IgnoreCase    : on - values compared without regard to case");

        if (!options.TrimValues)
            text.AppendLine("  TrimValues    : off - leading and trailing spaces count as a difference");

        text.AppendLine();
    }

    private static void WriteCounts(StringBuilder text, ComparisonResult result)
    {
        text.AppendLine("  COUNTS");
        WriteCount(text, "Rows in input", result.InputRowCount);
        WriteCount(text, "Rows in output", result.OutputRowCount);
        WriteCount(text, "Matching rows", result.MatchedRowCount);
        WriteCount(text, "Rows with value differences", result.ValueMismatches.Count);
        WriteCount(text, "Rows missing in output", result.MissingInOutput.Count);
        WriteCount(text, "Extra rows in output", result.ExtraInOutput.Count);
        WriteCount(text, "Non-matching rows (total)", result.NonMatchingRowCount);
        text.AppendLine();
    }

    private static void WriteWarnings(StringBuilder text, ComparisonResult result)
    {
        List<string> warnings = CollectWarnings(result);
        if (warnings.Count == 0)
            return;

        text.AppendLine("  WARNINGS");
        foreach (string warning in warnings)
            text.AppendLine($"    - {warning}");

        text.AppendLine();
    }

    /// <summary>The warnings in report order, shared with the window's Warnings tab.</summary>
    public static List<string> CollectWarnings(ComparisonResult result)
    {
        List<string> warnings = new List<string>();

        if (result.ColumnsOnlyInInput.Count > 0)
            warnings.Add($"Column(s) only in the input file, not compared: {string.Join(", ", result.ColumnsOnlyInInput)}");

        if (result.ColumnsOnlyInOutput.Count > 0)
            warnings.Add($"Column(s) only in the output file, not compared: {string.Join(", ", result.ColumnsOnlyInOutput)}");

        warnings.AddRange(result.SkipColumnWarnings);
        warnings.AddRange(result.OptionWarnings);
        warnings.AddRange(result.DuplicateKeyWarnings);

        return warnings;
    }

    private static string FormatRange(decimal range) =>
        range.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);

    private static void WriteNonMatchingRows(StringBuilder text, ComparisonResult result, int maxRows)
    {
        if (result.IsMatch)
            return;

        text.AppendLine("  NON-MATCHING ROWS");

        if (result.ValueMismatches.Count > 0)
        {
            text.AppendLine($"    Value differences ({result.ValueMismatches.Count}):");
            foreach (RowMismatch mismatch in Limit(result.ValueMismatches, maxRows))
            {
                text.AppendLine($"      [{mismatch.DisplayKey}]");
                foreach (ValueDifference difference in mismatch.Differences)
                    text.AppendLine($"        {difference.Column}: input='{difference.InputValue}'  output='{difference.OutputValue}'");

                text.AppendLine($"        input  (line {mismatch.InputRow.LineNumber}): {mismatch.InputRow.ToDisplayString()}");
                text.AppendLine($"        output (line {mismatch.OutputRow.LineNumber}): {mismatch.OutputRow.ToDisplayString()}");
            }

            WriteTruncationNote(text, result.ValueMismatches.Count, maxRows);
        }

        WriteRowList(text, "Present in input but missing from output", result.MissingInOutput, maxRows);
        WriteRowList(text, "Present in output but missing from input", result.ExtraInOutput, maxRows);
        text.AppendLine();
    }

    private static void WriteRowList(StringBuilder text, string title, IReadOnlyList<KeyedRow> rows, int maxRows)
    {
        if (rows.Count == 0)
            return;

        text.AppendLine($"    {title} ({rows.Count}):");
        foreach (KeyedRow row in Limit(rows, maxRows))
            text.AppendLine($"      [{row.DisplayKey}] line {row.Row.LineNumber}: {row.Row.ToDisplayString()}");

        WriteTruncationNote(text, rows.Count, maxRows);
    }

    private static void WriteVerdict(StringBuilder text, ComparisonResult result)
    {
        WriteRule(text);
        text.AppendLine(result.IsMatch
            ? $"  RESULT: SUCCESS - all {result.MatchedRowCount} row(s) match."
            : $"  RESULT: FAILED - {result.NonMatchingRowCount} non-matching row(s).");

        WriteRule(text);
    }

    private static IEnumerable<T> Limit<T>(IReadOnlyList<T> items, int maxRows) =>
        maxRows > 0 ? items.Take(maxRows) : items;

    private static void WriteTruncationNote(StringBuilder text, int total, int maxRows)
    {
        if (maxRows > 0 && total > maxRows)
            text.AppendLine($"      ... {total - maxRows} more (raise 'Max rows' to see them all)");
    }

    private static void WriteCount(StringBuilder text, string label, int value) =>
        text.AppendLine($"    {label,-30}: {value}");

    private static void WriteRule(StringBuilder text) =>
        text.AppendLine(new string('=', 78));
}
