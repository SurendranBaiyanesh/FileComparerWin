using System.Text;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Readers;

/// <summary>Reads separated-value text files (.csv, .txt, .tsv, .psv) with RFC 4180 style quoting.</summary>
public sealed class DelimitedTableReader : ITableReader
{
    // What detection tries. |" is deliberately not among them: every |" is also a |, so the two look
    // alike in a header, and a comma file holding one quoted field that ends in a pipe looks like it
    // too. Guessing it would misread files that are read correctly today, so it is chosen instead.
    private static readonly string[] CandidateDelimiters = [";", ",", "\t", "|"];
    private static readonly string[] Extensions = [".csv", ".txt", ".tsv", ".psv", ".dat", ".text"];

    public string FormatName => "Delimited text";

    public bool CanRead(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public DataTable Read(string path, ComparisonOptions options)
    {
        TextContent content = TextFile.Read(path, options.Encoding);
        string[] lines = content.Lines();
        int headerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (headerIndex < 0)
            throw new InvalidDataException($"'{path}' is empty.");

        string delimiter = ResolveDelimiter(options.Delimiter, lines[headerIndex]);
        List<string> header = SplitLine(lines[headerIndex], delimiter);

        List<(int, List<string>)> records = new List<(int, List<string>)>();
        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            records.Add((i + 1, SplitLine(lines[i], delimiter)));
        }

        return TableBuilder.Build(path, $"{FormatName} ('{Describe(delimiter)}' separated, {content.EncodingName})", header, records);
    }

    private static string ResolveDelimiter(string configured, string headerLine)
    {
        // Taken whole rather than one character of it: a separator is not always a single character,
        // and a longer one typed into the box used to be cut down to its first letter without a word.
        if (!string.IsNullOrEmpty(configured))
            return configured switch
            {
                "\\t" or "tab" or "TAB" => "\t",
                _ => configured
            };

        (string Delimiter, int Count) best = CandidateDelimiters
            .Select(d => (Delimiter: d, Count: CountOutsideQuotes(headerLine, d)))
            .OrderByDescending(x => x.Count)
            .First();

        return best.Count > 0 ? best.Delimiter : ";";
    }

    /// <summary>
    /// A delimiter with a quote in it leaves no quotes over to quote anything with: every quote on the
    /// line belongs to a separator. Such a file is cut on the separator and read exactly as written,
    /// rather than through the RFC 4180 rules the other delimiters are read by.
    /// </summary>
    private static bool QuotesAreLiteral(string delimiter) => delimiter.Contains('"');

    private static int CountOutsideQuotes(string line, string delimiter)
    {
        if (QuotesAreLiteral(delimiter))
            return CountLiteral(line, delimiter);

        int count = 0;
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && StartsWith(line, i, delimiter))
            {
                count++;
                i += delimiter.Length - 1;
            }
        }

        return count;
    }

    private static int CountLiteral(string line, string delimiter)
    {
        int count = 0;
        for (int i = line.IndexOf(delimiter, StringComparison.Ordinal); i >= 0;
             i = line.IndexOf(delimiter, i + delimiter.Length, StringComparison.Ordinal))
            count++;

        return count;
    }

    private static bool StartsWith(string line, int index, string delimiter) =>
        index + delimiter.Length <= line.Length &&
        string.CompareOrdinal(line, index, delimiter, 0, delimiter.Length) == 0;

    private static List<string> SplitLine(string line, string delimiter)
    {
        if (QuotesAreLiteral(delimiter))
            return [.. line.Split(delimiter, StringSplitOptions.None)];

        List<string> values = new List<string>();
        StringBuilder field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (StartsWith(line, i, delimiter))
            {
                values.Add(field.ToString());
                field.Clear();
                i += delimiter.Length - 1;
            }
            else
            {
                field.Append(c);
            }
        }

        values.Add(field.ToString());
        return values;
    }

    private static string Describe(string delimiter) => delimiter == "\t" ? "\\t" : delimiter;
}
