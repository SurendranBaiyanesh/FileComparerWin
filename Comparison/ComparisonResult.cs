using FileComparerWindows.Model;

namespace FileComparerWindows.Comparison;

public sealed class ComparisonResult
{
	#region Properties
	public required DataTable Input { get; init; }
	public required DataTable Output { get; init; }

	public required IReadOnlyList<string> KeyColumns { get; init; }
	public required IReadOnlyList<string> ComparedColumns { get; init; }

	/// <summary>Paired rows whose every compared value was equal outright.</summary>
	public required IReadOnlyList<MatchedRow> MatchedRows { get; init; }

	/// <summary>
	/// Paired rows that agreed only because SimilarMatchRange let them: nothing disagreed outright, and
	/// at least one value needed the range to be counted as equal. They are matches as far as the
	/// verdict goes, kept apart here because a match that rests on a tolerance is worth looking at.
	/// </summary>
	public required IReadOnlyList<SimilarMatch> SimilarMatches { get; init; }

	public int MatchedRowCount => this.MatchedRows.Count + this.SimilarMatches.Count;

	public required IReadOnlyList<RowMismatch> ValueMismatches { get; init; }
	public required IReadOnlyList<KeyedRow> MissingInOutput { get; init; }
	public required IReadOnlyList<KeyedRow> ExtraInOutput { get; init; }
	public required IReadOnlyList<string> DuplicateKeyWarnings { get; init; }

	/// <summary>Options that were set but could not take effect, such as a range without SimilarMatch.</summary>
	public required IReadOnlyList<string> OptionWarnings { get; init; }

	public int InputRowCount => this.Input.Rows.Count;
	public int OutputRowCount => this.Output.Rows.Count;
	public int NonMatchingRowCount => this.ValueMismatches.Count + this.MissingInOutput.Count + this.ExtraInOutput.Count;
	public bool IsMatch => this.NonMatchingRowCount == 0;
	#endregion
}

/// <summary>A row together with the key built from its key-column values.</summary>
public sealed record KeyedRow(string DisplayKey, DataRow Row);

/// <summary>A pair of rows that agreed on everything compared.</summary>
public sealed record MatchedRow(string DisplayKey, DataRow InputRow, DataRow OutputRow);

/// <summary>A pair of rows agreeing only within the range, with the values that needed it.</summary>
public sealed record SimilarMatch(string DisplayKey, DataRow InputRow, DataRow OutputRow, IReadOnlyList<ValueDifference> Values);

/// <summary>Rows that share a key but disagree on one or more compared columns.</summary>
public sealed record RowMismatch(string DisplayKey, DataRow InputRow, DataRow OutputRow, IReadOnlyList<ValueDifference> Differences);

public sealed record ValueDifference(string Column, string InputValue, string OutputValue);