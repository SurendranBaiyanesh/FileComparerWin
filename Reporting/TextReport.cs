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
	#region Public methods
	public static string Build(ComparisonResult result, ComparisonOptions options)
	{
		StringBuilder text = new();

		WriteHeader(text, result, options);
		WriteCounts(text, result);
		WriteWarnings(text, result);

		WriteNonMatchingRows(text, result);
		WriteVerdict(text, result);

		return text.ToString();
	}

	/// <summary>
	/// The warnings in report order, shared with the window's Warnings tab. Columns present in only one
	/// of the files are not among them: a comparison that got this far was run on two files carrying the
	/// same columns, since anything else stops as an error before a row is read.
	/// </summary>
	public static List<string> CollectWarnings(ComparisonResult result)
	{
		List<string> warnings = new();

		warnings.AddRange(result.OptionWarnings);
		warnings.AddRange(result.DuplicateKeyWarnings);

		return warnings;
	}
	#endregion

	#region Private methods
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

		// A run that passes only because 123.00 was read as 123, or because 99 was near enough to 100,
		// should say so.
		if(options.SimilarMatch)
		{
			text.AppendLine("  SimilarMatch  : on - numbers compared by value, not by how they are written");

			if(options.SimilarMatchRange > 0) text.AppendLine($"  Range         : ±{FormatRange(options.SimilarMatchRange)} - numbers this far apart still count as equal");
		}

		if(options.IgnoreCase) text.AppendLine("  IgnoreCase    : on - values compared without regard to case");

		if(!options.TrimValues) text.AppendLine("  TrimValues    : off - leading and trailing spaces count as a difference");

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
		if(warnings.Count == 0) return;

		text.AppendLine("  WARNINGS");
		foreach(string warning in warnings)
		{
			text.AppendLine($"    - {warning}");
		}

		text.AppendLine();
	}

	private static string FormatRange(decimal range)
	{
		return range.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
	}

	private static void WriteNonMatchingRows(StringBuilder text, ComparisonResult result)
	{
		if(result.IsMatch) return;

		text.AppendLine("  NON-MATCHING ROWS");

		if(result.ValueMismatches.Count > 0)
		{
			text.AppendLine($"    Value differences ({result.ValueMismatches.Count}):");
			foreach(RowMismatch mismatch in result.ValueMismatches)
			{
				text.AppendLine($"      [{mismatch.DisplayKey}]");
				foreach(ValueDifference difference in mismatch.Differences)
				{
					text.AppendLine($"        {difference.Column}: input='{difference.InputValue}'  output='{difference.OutputValue}'");
				}

				text.AppendLine($"        input  (line {mismatch.InputRow.LineNumber}): {mismatch.InputRow.ToDisplayString()}");
				text.AppendLine($"        output (line {mismatch.OutputRow.LineNumber}): {mismatch.OutputRow.ToDisplayString()}");
			}
		}

		WriteRowList(text, "Present in input but missing from output", result.MissingInOutput);
		WriteRowList(text, "Present in output but missing from input", result.ExtraInOutput);
		text.AppendLine();
	}

	private static void WriteRowList(StringBuilder text, string title, IReadOnlyList<KeyedRow> rows)
	{
		if(rows.Count == 0) return;

		text.AppendLine($"    {title} ({rows.Count}):");
		foreach(KeyedRow row in rows)
		{
			text.AppendLine($"      [{row.DisplayKey}] line {row.Row.LineNumber}: {row.Row.ToDisplayString()}");
		}
	}

	private static void WriteVerdict(StringBuilder text, ComparisonResult result)
	{
		WriteRule(text);
		text.AppendLine(result.IsMatch ? $"  RESULT: SUCCESS - all {result.MatchedRowCount} row(s) match." : $"  RESULT: FAILED - {result.NonMatchingRowCount} non-matching row(s).");

		WriteRule(text);
	}

	private static void WriteCount(StringBuilder text, string label, int value)
	{
		text.AppendLine($"    {label,-30}: {value}");
	}

	private static void WriteRule(StringBuilder text)
	{
		text.AppendLine(new string('=', 78));
	}
	#endregion
}