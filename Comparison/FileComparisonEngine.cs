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
            ColumnsOnlyInInput = [.. input.Columns.Where(c => !output.HasColumn(c))],
            ColumnsOnlyInOutput = [.. output.Columns.Where(c => !input.HasColumn(c))],
            MatchedRowCount = matched,
            ValueMismatches = mismatches,
            MissingInOutput = missingInOutput,
            ExtraInOutput = extraInOutput,
            DuplicateKeyWarnings = [.. DescribeDuplicates("Input", inputGroups), .. DescribeDuplicates("Output", outputGroups)]
        };
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

        return new ColumnPlan(compared, removed, [.. DescribeSkipsThatChangedNothing(input, output, keyColumns, removed)]);
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
    /// the column having been excluded, when the comparison is exactly what it would have been.
    /// </summary>
    private IEnumerable<string> DescribeSkipsThatChangedNothing(DataTable input, DataTable output, List<string> keyColumns, List<string> removed)
    {
        foreach (string name in options.SkipColumns)
        {
            if (removed.Any(r => SameColumn(r, name)))
                continue;

            if (keyColumns.Any(k => SameColumn(k, name)))
                yield return $"Skipped column '{name}' is a key column: it pairs the rows and is not compared in any case.";
            else if (!input.HasColumn(name) && !output.HasColumn(name))
                yield return $"Skipped column '{name}' is not a column in either file.";
            else
                yield return $"Skipped column '{name}' is not in both files, so it was not being compared in any case.";
        }
    }

    private static bool SameColumn(string left, string right) =>
        string.Equals(TextKey.Canonical(left), TextKey.Canonical(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>The columns compared, those held back, and anything worth saying about the latter.</summary>
    private sealed record ColumnPlan(List<string> Compared, List<string> Skipped, List<string> Warnings);

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

            if (!string.Equals(Normalize(inputValue), Normalize(outputValue), StringComparison.Ordinal))
                differences.Add(new ValueDifference(column, inputValue, outputValue));
        }

        return differences;
    }

    private string Normalize(string value)
    {
        value = TextKey.Canonical(value);

        if (options.TrimValues)
            value = value.Trim();

        if (options.SimilarMatch && TryReadNumber(value, out string number))
            return number;

        return options.IgnoreCase ? value.ToUpperInvariant() : value;
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

        if (value.Length == 0 || !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
            return false;

        // Trailing zeros carry no value, and a signed zero is still zero.
        number = parsed == decimal.Zero ? "0" : parsed.ToString("0.############################", CultureInfo.InvariantCulture);
        return true;
    }

    private string DisplayKey(DataTable table, DataRow row, List<string> keyColumns) =>
        string.Join(", ", keyColumns.Select(c => $"{c}={table.GetValue(row, c)}"));

    private static IEnumerable<string> DescribeDuplicates(string side, Dictionary<string, List<DataRow>> groups) =>
        groups.Where(g => g.Value.Count > 1)
            .Select(g => $"{side} file has {g.Value.Count} rows with key '{g.Key.Replace(KeySeparator, '|')}' (lines {string.Join(", ", g.Value.Select(r => r.LineNumber))}).");
}
