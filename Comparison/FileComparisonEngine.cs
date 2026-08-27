using System.Globalization;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Comparison;

/// <summary>Pairs rows from the two files by their key-column values and compares the remaining columns.
/// Row order is irrelevant; only keys and values decide the outcome.</summary>
public sealed class FileComparisonEngine(ComparisonOptions options)
{
	#region Constants
	// Unit separator: cannot occur in real data, so composite keys stay unambiguous.
	private const char KEY_SEPARATOR = (char) 0x1F;

	// What a decoder substitutes for bytes it could not make sense of.
	private const char REPLACEMENT_CHARACTER = (char) 0xFFFD;
	#endregion

	#region Public methods
	public ComparisonResult Compare(DataTable input, DataTable output)
	{
		ValidateOptions();
		RequireMatchingColumns(input, output);
		List<string> liKeyColumns = ResolveKeyColumns(input, output);
		List<string> liComparedColumns = SelectComparableColumns(input, output, liKeyColumns);

		Dictionary<string, List<DataRow>> inputGroups = GroupByKey(input, liKeyColumns);
		Dictionary<string, List<DataRow>> outputGroups = GroupByKey(output, liKeyColumns);

		List<RowMismatch> liMismatches = new();
		List<KeyedRow> liMissingInOutput = new();
		List<KeyedRow> liExtraInOutput = new();
		List<MatchedRow> liMatchedRows = new();
		List<SimilarMatch> liSimilarMatches = new();

		foreach((string strKey, List<DataRow> inputRows) in inputGroups)
		{
			if(!outputGroups.TryGetValue(strKey, out List<DataRow>? outputRows))
			{
				liMissingInOutput.AddRange(inputRows.Select(r => new KeyedRow(DisplayKey(input, r, liKeyColumns), r)));
				continue;
			}

			int nPairCount = Math.Min(inputRows.Count, outputRows.Count);
			for(int i = 0; i < nPairCount; i++)
			{
				(List<ValueDifference> liDifferences, List<ValueDifference> liSimilar) = CompareValues(input, inputRows[i], output, outputRows[i], liComparedColumns);

				string strDisplayKey = DisplayKey(input, inputRows[i], liKeyColumns);

				// A row with something genuinely wrong is a mismatch whatever else it also has, so the
				// three lists divide the paired rows between them rather than overlapping.
				if(liDifferences.Count > 0)
				{
					liMismatches.Add(new RowMismatch(strDisplayKey, inputRows[i], outputRows[i], liDifferences));
				}
				else if(liSimilar.Count > 0)
				{
					liSimilarMatches.Add(new SimilarMatch(strDisplayKey, inputRows[i], outputRows[i], liSimilar));
				}
				else
				{
					liMatchedRows.Add(new MatchedRow(strDisplayKey, inputRows[i], outputRows[i]));
				}
			}

			// Duplicate keys: whatever is left over on either side has no counterpart.
			liMissingInOutput.AddRange(inputRows.Skip(nPairCount).Select(r => new KeyedRow(DisplayKey(input, r, liKeyColumns), r)));
			liExtraInOutput.AddRange(outputRows.Skip(nPairCount).Select(r => new KeyedRow(DisplayKey(output, r, liKeyColumns), r)));
		}

		foreach((string strKey, List<DataRow> outputRows) in outputGroups.Where(g => !inputGroups.ContainsKey(g.Key)))
		{
			liExtraInOutput.AddRange(outputRows.Select(r => new KeyedRow(DisplayKey(output, r, liKeyColumns), r)));
		}

		return new ComparisonResult
		       {
			       Input = input,
			       Output = output,
			       KeyColumns = liKeyColumns,
			       ComparedColumns = liComparedColumns,
			       MatchedRows = liMatchedRows,
			       SimilarMatches = liSimilarMatches,
			       ValueMismatches = liMismatches,
			       MissingInOutput = liMissingInOutput,
			       ExtraInOutput = liExtraInOutput,
			       DuplicateKeyWarnings = [.. DescribeDuplicates("Input", inputGroups), .. DescribeDuplicates("Output", outputGroups)],
			       OptionWarnings = [.. DescribeOptionsThatChangedNothing()]
		       };
	}

	/// <summary>
	/// The columns that are on one side only, or null when the two headers agree. Public because the
	/// window says so under the file boxes as soon as both files have been read, rather than leaving
	/// the user to name key columns and press Compare to learn what the two headers already said.
	/// </summary>
	public static ColumnMismatch? FindColumnMismatch(DataTable input, DataTable output)
	{
		List<string> liOnlyInInput = [.. input.Columns.Where(c => !output.HasColumn(c))];
		List<string> liOnlyInOutput = [.. output.Columns.Where(c => !input.HasColumn(c))];

		if(liOnlyInInput.Count == 0 && liOnlyInOutput.Count == 0) return null;

		List<string> liSides = [];
		if(liOnlyInInput.Count > 0) liSides.Add($"only in the input file: {string.Join(", ", liOnlyInInput)}");

		if(liOnlyInOutput.Count > 0) liSides.Add($"only in the output file: {string.Join(", ", liOnlyInOutput)}");

		string strHeadline = $"The two files do not have the same columns - {string.Join("; ", liSides)}.";

		return new ColumnMismatch(strHeadline,
		                          $"{strHeadline}{Environment.NewLine}" +
		                          $"  Input columns : {string.Join(", ", input.Columns)}{Environment.NewLine}" +
		                          $"  Output columns: {string.Join(", ", output.Columns)}" +
		                          EncodingHint(input, output));
	}
	#endregion

	#region Private methods
	private void ValidateOptions()
	{
		if(options.SimilarMatchRange < 0) throw new InvalidOperationException($"SimilarMatchRange is {options.SimilarMatchRange}. A range is a distance and cannot be negative; " + "use 0 to require numbers to be exactly equal.");
	}

	/// <summary>
	/// A range without SimilarMatch does nothing at all, and silence would read as the range having been
	/// applied - a run that reported no differences would then look like agreement it had not tested for.
	/// </summary>
	private IEnumerable<string> DescribeOptionsThatChangedNothing()
	{
		if(options.SimilarMatchRange > 0 && !options.SimilarMatch) yield return $"SimilarMatchRange is {Describe(options.SimilarMatchRange)} but SimilarMatch is off, so values were " + "compared as the text they are written as and the range was not applied.";
	}

	/// <summary>
	/// The two files have to carry the same columns. A column on one side only cannot be compared -
	/// there is nothing to compare it against - and leaving it out quietly would turn an export that
	/// has lost a column into a run reporting that every row matches.
	/// </summary>
	private static void RequireMatchingColumns(DataTable input, DataTable output)
	{
		if(FindColumnMismatch(input, output) is { } mismatch) throw new InvalidOperationException(mismatch.Detail);
	}

	private List<string> ResolveKeyColumns(DataTable input, DataTable output)
	{
		if(options.KeyColumns.Count == 0) throw new InvalidOperationException("At least one key column is required. Type it into Key columns, or use Pick to choose from the file's header.");

		List<string> liMissing = options.KeyColumns.Where(c => !input.HasColumn(c) || !output.HasColumn(c)).ToList();
		if(liMissing.Count > 0)
		{
			throw new InvalidOperationException($"Column(s) not present in both files: {string.Join(", ", liMissing)}.{Environment.NewLine}" +
			                                    $"  Input columns : {string.Join(", ", input.Columns)}{Environment.NewLine}" +
			                                    $"  Output columns: {string.Join(", ", output.Columns)}" +
			                                    EncodingHint(input, output));
		}

		return [.. options.KeyColumns.Select(input.ResolveColumnName)];
	}

	/// <summary>
	/// A replacement character in a header means the file was decoded in the wrong encoding, so the
	/// name on screen is not the name being matched. Worth saying, because the two look the same.
	/// </summary>
	private static string EncodingHint(DataTable input, DataTable output)
	{
		if(!input.Columns.Concat(output.Columns).Any(c => c.Contains(REPLACEMENT_CHARACTER))) return string.Empty;

		return $"{Environment.NewLine}  A column name above contains '{REPLACEMENT_CHARACTER}', so that file was not " + "read in the encoding it was written in. Try setting Encoding to windows-1252.";
	}

	/// <summary>
	/// Settles which columns are compared: the ones named for comparison, or every column the two files
	/// share when none are named. Naming them is how a column is left out.
	/// </summary>
	private List<string> SelectComparableColumns(DataTable input, DataTable output, List<string> liKeyColumns)
	{
		if(options.CompareColumns.Count > 0)
		{
			List<string> liMissing = options.CompareColumns.Where(c => !input.HasColumn(c) || !output.HasColumn(c)).ToList();
			if(liMissing.Count > 0) throw new InvalidOperationException($"Compare column(s) not present in both files: {string.Join(", ", liMissing)}.");

			return [.. options.CompareColumns.Select(input.ResolveColumnName)];
		}

		List<string> liCommon = input.Columns.Where(output.HasColumn).ToList();
		List<string> liNonKey = liCommon.Where(c => !liKeyColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();

		// With only key columns in common there is nothing left to compare, so the keys themselves are the comparison.
		return liNonKey.Count > 0 ? liNonKey : liCommon;
	}

	/// <summary>
	/// Groups rows on their key values. Deliberately exact, even when SimilarMatchRange allows values to
	/// differ: "within a range of each other" is not an equivalence relation - with a range of 1, 100
	/// matches 101 and 101 matches 102 while 100 and 102 do not - so there is no such thing as the group
	/// a row belongs to. Rows therefore pair on keys that are equal, and the range applies afterwards, to
	/// the values being compared.
	/// </summary>
	private Dictionary<string, List<DataRow>> GroupByKey(DataTable table, List<string> liKeyColumns)
	{
		Dictionary<string, List<DataRow>> groups = new(StringComparer.Ordinal);

		foreach(DataRow row in table.Rows)
		{
			string strKey = string.Join(KEY_SEPARATOR, liKeyColumns.Select(c => Normalize(table.GetValue(row, c))));
			if(!groups.TryGetValue(strKey, out List<DataRow>? rows)) groups[strKey] = rows = [];

			rows.Add(row);
		}

		return groups;
	}

	/// <summary>
	/// What disagreed, and what only agreed because the range allowed it. The two are kept apart so a
	/// report can show which matches rest on a tolerance; note that values written differently but
	/// meaning the same number - 123.00 and 123 - are equal outright and are not among the second lot.
	/// </summary>
	private (List<ValueDifference> Differences, List<ValueDifference> Similar) CompareValues(DataTable input, DataRow inputRow, DataTable output, DataRow outputRow, List<string> columns)
	{
		List<ValueDifference> liDifferences = new();
		List<ValueDifference> liSimilar = new();

		foreach(string column in columns)
		{
			string strInputValue = input.GetValue(inputRow, column);
			string strOutputValue = output.GetValue(outputRow, column);

			if(string.Equals(Normalize(strInputValue), Normalize(strOutputValue), StringComparison.Ordinal)) continue;

			if(IsWithinRange(strInputValue, strOutputValue)) liSimilar.Add(new ValueDifference(column, strInputValue, strOutputValue));
			else liDifferences.Add(new ValueDifference(column, strInputValue, strOutputValue));
		}

		return (liDifferences, liSimilar);
	}

	/// <summary>
	/// Two numbers no further apart than SimilarMatchRange. Both sides have to be numbers: a range is a
	/// distance, and there is no distance between a number and a word, so anything else stays the text
	/// comparison it already failed.
	/// </summary>
	private bool IsWithinRange(string strInputValue, string strOutputValue)
	{
		if(!options.SimilarMatch || options.SimilarMatchRange <= 0) return false;

		if(!TryParseNumber(Prepare(strInputValue), out decimal left) || !TryParseNumber(Prepare(strOutputValue), out decimal right)) return false;

		try
		{
			return Math.Abs(left - right) <= options.SimilarMatchRange;
		}
		catch(OverflowException)
		{
			// Values at opposite ends of what a decimal can hold: further apart than any range.
			return false;
		}
	}

	private string Normalize(string value)
	{
		value = Prepare(value);

		if(options.SimilarMatch && TryReadNumber(value, out string number)) return number;

		return options.IgnoreCase ? value.ToUpperInvariant() : value;
	}

	/// <summary>The part of normalising that applies whether or not the value turns out to be a number.</summary>
	private string Prepare(string value)
	{
		value = TextKey.Canonical(value);
		return options.TrimValues ? value.Trim() : value;
	}

	/// <summary>
	/// Reduces a number to one form, so that only its value counts and not how it was written: 123.00
	/// and 123 arrive here as the same string, as do 110.60 and 110.6.
	/// </summary>
	/// <remarks>
	/// Deliberately strict about what counts as a number. Digit-grouping separators are not accepted,
	/// because '110,60' means one hundred and ten in half the world and eleven thousand in the other
	/// half; anything ambiguous is left to be compared as the text it is.
	/// </remarks>
	private static bool TryReadNumber(string value, out string number)
	{
		number = string.Empty;

		if(!TryParseNumber(value, out decimal parsed)) return false;

		// Trailing zeros carry no value, and a signed zero is still zero.
		number = parsed == decimal.Zero ? "0" : parsed.ToString("0.############################", CultureInfo.InvariantCulture);
		return true;
	}

	private static bool TryParseNumber(string value, out decimal parsed)
	{
		parsed = decimal.Zero;
		return value.Length > 0 && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
	}

	/// <summary>A range as the user wrote it, without the trailing zeros a decimal remembers.</summary>
	private static string Describe(decimal range)
	{
		return range.ToString("0.############################", CultureInfo.InvariantCulture);
	}

	private string DisplayKey(DataTable table, DataRow row, List<string> liKeyColumns)
	{
		return string.Join(", ", liKeyColumns.Select(c => $"{c}={table.GetValue(row, c)}"));
	}

	private static IEnumerable<string> DescribeDuplicates(string side, Dictionary<string, List<DataRow>> groups)
	{
		return groups.Where(g => g.Value.Count > 1)
		             .Select(g => $"{side} file has {g.Value.Count} rows with key '{g.Key.Replace(KEY_SEPARATOR, '|')}' (lines {string.Join(", ", g.Value.Select(r => r.LineNumber))}).");
	}
	#endregion
}

/// <summary>
/// Two headers that disagree: named briefly enough for the line under the file boxes, and in full -
/// both headers, and the encoding hint when one of them was decoded wrongly - for the error the
/// comparison stops with.
/// </summary>
public sealed record ColumnMismatch(string Headline, string Detail);