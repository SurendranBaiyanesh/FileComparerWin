using System.Globalization;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Comparison;

/// <summary>Pairs rows from the two files by their key-column values and compares the remaining columns.
/// Row order is irrelevant; only keys and values decide the outcome.</summary>
public sealed class FileComparisonEngine(ComparisonOptions options)
{
    // Unit separator: cannot occur in real data, so composite keys stay unambiguous.
    private const char KeySeparator = (char)0x1F;

    // What a decoder substitutes for bytes it could not make sense of.
    private const char ReplacementCharacter = (char)0xFFFD;

    public ComparisonResult Compare(DataTable input, DataTable output)
    {
        ValidateOptions();
        RequireMatchingColumns(input, output);
        List<string> keyColumns = ResolveKeyColumns(input, output);
        ColumnPlan columns = PlanColumns(input, output, keyColumns);
        List<string> comparedColumns = columns.Compared;

        Dictionary<string, List<DataRow>> inputGroups = GroupByKey(input, keyColumns);
        Dictionary<string, List<DataRow>> outputGroups = GroupByKey(output, keyColumns);

        List<RowMismatch> mismatches = new List<RowMismatch>();
        List<KeyedRow> missingInOutput = new List<KeyedRow>();
        List<KeyedRow> extraInOutput = new List<KeyedRow>();
        int matched = 0;

        foreach ((string key, List<DataRow> inputRows) in inputGroups)
        {
            if (!outputGroups.TryGetValue(key, out List<DataRow>? outputRows))
            {
                missingInOutput.AddRange(inputRows.Select(r => new KeyedRow(DisplayKey(input, r, keyColumns), r)));
                continue;
            }

            int pairCount = Math.Min(inputRows.Count, outputRows.Count);
            for (int i = 0; i < pairCount; i++)
            {
                List<ValueDifference> differences = CompareValues(input, inputRows[i], output, outputRows[i], comparedColumns);
                if (differences.Count == 0)
                    matched++;
                else
                    mismatches.Add(new RowMismatch(DisplayKey(input, inputRows[i], keyColumns), inputRows[i], outputRows[i], differences));
            }

            // Duplicate keys: whatever is left over on either side has no counterpart.
            missingInOutput.AddRange(inputRows.Skip(pairCount).Select(r => new KeyedRow(DisplayKey(input, r, keyColumns), r)));
            extraInOutput.AddRange(outputRows.Skip(pairCount).Select(r => new KeyedRow(DisplayKey(output, r, keyColumns), r)));
        }

        foreach ((string key, List<DataRow> outputRows) in outputGroups.Where(g => !inputGroups.ContainsKey(g.Key)))
            extraInOutput.AddRange(outputRows.Select(r => new KeyedRow(DisplayKey(output, r, keyColumns), r)));

        return new ComparisonResult
        {
            Input = input,
            Output = output,
            KeyColumns = keyColumns,
            ComparedColumns = comparedColumns,
            SkippedColumns = columns.Skipped,
            SkipColumnWarnings = columns.Warnings,
            MatchedRowCount = matched,
            ValueMismatches = mismatches,
            MissingInOutput = missingInOutput,
            ExtraInOutput = extraInOutput,
            DuplicateKeyWarnings = [.. DescribeDuplicates("Input", inputGroups), .. DescribeDuplicates("Output", outputGroups)],
            OptionWarnings = [.. DescribeOptionsThatChangedNothing()]
        };
    }

    private void ValidateOptions()
    {
        if (options.SimilarMatchRange < 0)
            throw new InvalidOperationException(
                $"SimilarMatchRange is {options.SimilarMatchRange}. A range is a distance and cannot be negative; " +
                "use 0 to require numbers to be exactly equal.");
    }

    /// <summary>
    /// A range without SimilarMatch does nothing at all, and silence would read as the range having been
    /// applied - a run that reported no differences would then look like agreement it had not tested for.
    /// </summary>
    private IEnumerable<string> DescribeOptionsThatChangedNothing()
    {
        if (options.SimilarMatchRange > 0 && !options.SimilarMatch)
            yield return $"SimilarMatchRange is {Describe(options.SimilarMatchRange)} but SimilarMatch is off, so values were " +
                         "compared as the text they are written as and the range was not applied.";
    }

    /// <summary>
    /// The two files have to carry the same columns. A column on one side only cannot be compared -
    /// there is nothing to compare it against - and leaving it out quietly would turn an export that
    /// has lost a column into a run reporting that every row matches.
    /// </summary>
    private static void RequireMatchingColumns(DataTable input, DataTable output)
    {
        if (FindColumnMismatch(input, output) is { } mismatch)
            throw new InvalidOperationException(mismatch.Detail);
    }

    /// <summary>
    /// The columns that are on one side only, or null when the two headers agree. Public because the
    /// window says so under the file boxes as soon as both files have been read, rather than leaving
    /// the user to name key columns and press Compare to learn what the two headers already said.
    /// </summary>
    public static ColumnMismatch? FindColumnMismatch(DataTable input, DataTable output)
    {
        List<string> onlyInInput = [.. input.Columns.Where(c => !output.HasColumn(c))];
        List<string> onlyInOutput = [.. output.Columns.Where(c => !input.HasColumn(c))];

        if (onlyInInput.Count == 0 && onlyInOutput.Count == 0)
            return null;

        List<string> sides = [];
        if (onlyInInput.Count > 0)
            sides.Add($"only in the input file: {string.Join(", ", onlyInInput)}");

        if (onlyInOutput.Count > 0)
            sides.Add($"only in the output file: {string.Join(", ", onlyInOutput)}");

        string headline = $"The two files do not have the same columns - {string.Join("; ", sides)}.";

        return new ColumnMismatch(headline,
            $"{headline}{Environment.NewLine}" +
            $"  Input columns : {string.Join(", ", input.Columns)}{Environment.NewLine}" +
            $"  Output columns: {string.Join(", ", output.Columns)}" +
            EncodingHint(input, output));
    }

    private List<string> ResolveKeyColumns(DataTable input, DataTable output)
    {
        if (options.KeyColumns.Count == 0)
            throw new InvalidOperationException("At least one key column is required. Type it into Key columns, or use Pick to choose from the file's header.");

        List<string> missing = options.KeyColumns.Where(c => !input.HasColumn(c) || !output.HasColumn(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Column(s) not present in both files: {string.Join(", ", missing)}.{Environment.NewLine}" +
                $"  Input columns : {string.Join(", ", input.Columns)}{Environment.NewLine}" +
                $"  Output columns: {string.Join(", ", output.Columns)}" +
                EncodingHint(input, output));

        return [.. options.KeyColumns.Select(input.ResolveColumnName)];
    }

    /// <summary>
    /// A replacement character in a header means the file was decoded in the wrong encoding, so the
    /// name on screen is not the name being matched. Worth saying, because the two look the same.
    /// </summary>
    private static string EncodingHint(DataTable input, DataTable output)
    {
        if (!input.Columns.Concat(output.Columns).Any(c => c.Contains(ReplacementCharacter)))
            return string.Empty;

        return $"{Environment.NewLine}  A column name above contains '{ReplacementCharacter}', so that file was not " +
               "read in the encoding it was written in. Try setting Encoding to windows-1252.";
    }

    /// <summary>
    /// Settles which columns are actually compared: the columns selected for comparison, less anything
    /// named as skipped. Skipping is subtractive and has the last word, so naming a column in both
    /// CompareColumns and SkipColumns leaves it out.
    /// </summary>
    private ColumnPlan PlanColumns(DataTable input, DataTable output, List<string> keyColumns)
    {
        List<string> selected = SelectComparableColumns(input, output, keyColumns);
        if (options.SkipColumns.Count == 0)
            return new ColumnPlan(selected, [], []);

        HashSet<string> skipped = new HashSet<string>(options.SkipColumns.Select(TextKey.Canonical), StringComparer.OrdinalIgnoreCase);
        List<string> compared = selected.Where(c => !skipped.Contains(TextKey.Canonical(c))).ToList();
        List<string> removed = selected.Where(c => skipped.Contains(TextKey.Canonical(c))).ToList();

        if (compared.Count == 0)
            throw new InvalidOperationException(
                $"Every comparable column was skipped ({string.Join(", ", selected)}), leaving nothing to compare.{Environment.NewLine}" +
                "  Rows would pair on the key columns and then match by definition.");

        return new ColumnPlan(compared, removed, [.. DescribeSkipsThatChangedNothing(input, keyColumns, removed)]);
    }

    private List<string> SelectComparableColumns(DataTable input, DataTable output, List<string> keyColumns)
    {
        if (options.CompareColumns.Count > 0)
        {
            List<string> missing = options.CompareColumns.Where(c => !input.HasColumn(c) || !output.HasColumn(c)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Compare column(s) not present in both files: {string.Join(", ", missing)}.");

            return [.. options.CompareColumns.Select(input.ResolveColumnName)];
        }

        List<string> common = input.Columns.Where(output.HasColumn).ToList();
        List<string> nonKey = common.Where(c => !keyColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();

        // With only key columns in common there is nothing left to compare, so the keys themselves are the comparison.
        return nonKey.Count > 0 ? nonKey : common;
    }

    /// <summary>
    /// A name that was skipped but was never going to be compared anyway. Saying nothing would read as
    /// the column having been excluded, when the comparison is exactly what it would have been. Only the
    /// input's header is consulted: by the time this runs, the two files are known to carry the same
    /// columns, so a name the input does not have is a name neither file has.
    /// </summary>
    private IEnumerable<string> DescribeSkipsThatChangedNothing(DataTable input, List<string> keyColumns, List<string> removed)
    {
        foreach (string name in options.SkipColumns)
        {
            if (removed.Any(r => SameColumn(r, name)))
                continue;

            if (keyColumns.Any(k => SameColumn(k, name)))
                yield return $"Skipped column '{name}' is a key column: it pairs the rows and is not compared in any case.";
            else if (!input.HasColumn(name))
                yield return $"Skipped column '{name}' is not a column in either file.";
            else
                yield return $"Skipped column '{name}' is not among the Compare columns, so it was not being compared in any case.";
        }
    }

    private static bool SameColumn(string left, string right) =>
        string.Equals(TextKey.Canonical(left), TextKey.Canonical(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>The columns compared, those held back, and anything worth saying about the latter.</summary>
    private sealed record ColumnPlan(List<string> Compared, List<string> Skipped, List<string> Warnings);

    /// <summary>
    /// Groups rows on their key values. Deliberately exact, even when SimilarMatchRange allows values to
    /// differ: "within a range of each other" is not an equivalence relation - with a range of 1, 100
    /// matches 101 and 101 matches 102 while 100 and 102 do not - so there is no such thing as the group
    /// a row belongs to. Rows therefore pair on keys that are equal, and the range applies afterwards, to
    /// the values being compared.
    /// </summary>
    private Dictionary<string, List<DataRow>> GroupByKey(DataTable table, List<string> keyColumns)
    {
        Dictionary<string, List<DataRow>> groups = new Dictionary<string, List<DataRow>>(StringComparer.Ordinal);

        foreach (DataRow row in table.Rows)
        {
            string key = string.Join(KeySeparator, keyColumns.Select(c => Normalize(table.GetValue(row, c))));
            if (!groups.TryGetValue(key, out List<DataRow>? rows))
                groups[key] = rows = [];

            rows.Add(row);
        }

        return groups;
    }

    private List<ValueDifference> CompareValues(DataTable input, DataRow inputRow, DataTable output, DataRow outputRow, List<string> columns)
    {
        List<ValueDifference> differences = new List<ValueDifference>();

        foreach (string column in columns)
        {
            string inputValue = input.GetValue(inputRow, column);
            string outputValue = output.GetValue(outputRow, column);

            if (!ValuesMatch(inputValue, outputValue))
                differences.Add(new ValueDifference(column, inputValue, outputValue));
        }

        return differences;
    }

    private bool ValuesMatch(string inputValue, string outputValue) =>
        string.Equals(Normalize(inputValue), Normalize(outputValue), StringComparison.Ordinal)
        || IsWithinRange(inputValue, outputValue);

    /// <summary>
    /// Two numbers no further apart than SimilarMatchRange. Both sides have to be numbers: a range is a
    /// distance, and there is no distance between a number and a word, so anything else stays the text
    /// comparison it already failed.
    /// </summary>
    private bool IsWithinRange(string inputValue, string outputValue)
    {
        if (!options.SimilarMatch || options.SimilarMatchRange <= 0)
            return false;

        if (!TryParseNumber(Prepare(inputValue), out decimal left) || !TryParseNumber(Prepare(outputValue), out decimal right))
            return false;

        try
        {
            return Math.Abs(left - right) <= options.SimilarMatchRange;
        }
        catch (OverflowException)
        {
            // Values at opposite ends of what a decimal can hold: further apart than any range.
            return false;
        }
    }

    private string Normalize(string value)
    {
        value = Prepare(value);

        if (options.SimilarMatch && TryReadNumber(value, out string number))
            return number;

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

        if (!TryParseNumber(value, out decimal parsed))
            return false;

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
    private static string Describe(decimal range) =>
        range.ToString("0.############################", CultureInfo.InvariantCulture);

    private string DisplayKey(DataTable table, DataRow row, List<string> keyColumns) =>
        string.Join(", ", keyColumns.Select(c => $"{c}={table.GetValue(row, c)}"));

    private static IEnumerable<string> DescribeDuplicates(string side, Dictionary<string, List<DataRow>> groups) =>
        groups.Where(g => g.Value.Count > 1)
            .Select(g => $"{side} file has {g.Value.Count} rows with key '{g.Key.Replace(KeySeparator, '|')}' (lines {string.Join(", ", g.Value.Select(r => r.LineNumber))}).");
}

/// <summary>
/// Two headers that disagree: named briefly enough for the line under the file boxes, and in full -
/// both headers, and the encoding hint when one of them was decoded wrongly - for the error the
/// comparison stops with.
/// </summary>
public sealed record ColumnMismatch(string Headline, string Detail);
