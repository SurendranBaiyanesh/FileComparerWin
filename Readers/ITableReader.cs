using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Readers;

public interface ITableReader
{
    string FormatName { get; }

    bool CanRead(string path);

    DataTable Read(string path, ComparisonOptions options);
}

/// <summary>Turns the parsed cells of a file into a <see cref="DataTable"/>, applying the rules that are
/// shared by every format: the first record is the header, trailing empty header cells are dropped
/// (a header such as "A;B;C;" yields three columns), and ragged rows are squared off.</summary>
public static class TableBuilder
{
    public static DataTable Build(string path, string formatName, IReadOnlyList<string> header, List<(int LineNumber, List<string> Values)> records)
    {
        List<string> columns = TrimTrailingEmpty(header);
        if (columns.Count == 0)
            throw new InvalidDataException($"No column headers found in '{path}'.");

        int width = Math.Max(columns.Count, records.Count == 0 ? 0 : records.Max(r => TrimTrailingEmpty(r.Values).Count));
        for (int i = columns.Count; i < width; i++)
            columns.Add($"Column{i + 1}");

        EnsureUniqueNames(columns);

        List<DataRow> rows = new List<DataRow>(records.Count);
        foreach ((int lineNumber, List<string> values) in records)
        {
            string[] padded = new string[width];
            for (int i = 0; i < width; i++)
                padded[i] = i < values.Count ? values[i] : string.Empty;

            rows.Add(new DataRow(lineNumber, padded));
        }

        return new DataTable(path, formatName, columns, rows);
    }

    private static List<string> TrimTrailingEmpty(IReadOnlyList<string> values)
    {
        int last = values.Count - 1;
        while (last >= 0 && string.IsNullOrWhiteSpace(values[last]))
            last--;

        return [.. values.Take(last + 1).Select(v => v ?? string.Empty)];
    }

    private static void EnsureUniqueNames(List<string> columns)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
        {
            string name = columns[i].Trim();
            if (name.Length == 0)
                name = $"Column{i + 1}";

            string candidate = name;
            int suffix = 2;
            while (!seen.Add(candidate))
                candidate = $"{name}_{suffix++}";

            columns[i] = candidate;
        }
    }
}
