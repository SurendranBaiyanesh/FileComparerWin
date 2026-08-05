using System.Text;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Readers;

/// <summary>Reads separated-value text files (.csv, .txt, .tsv, .psv) with RFC 4180 style quoting.</summary>
public sealed class DelimitedTableReader : ITableReader
{
    private static readonly char[] CandidateDelimiters = [';', ',', '\t', '|'];
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

        char delimiter = ResolveDelimiter(options.Delimiter, lines[headerIndex]);
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

    private static char ResolveDelimiter(string configured, string headerLine)
    {
        if (!string.IsNullOrEmpty(configured))
            return configured switch
            {
                "\\t" or "tab" or "TAB" => '\t',
                _ => configured[0]
            };

        (char Delimiter, int Count) best = CandidateDelimiters
            .Select(d => (Delimiter: d, Count: CountOutsideQuotes(headerLine, d)))
            .OrderByDescending(x => x.Count)
            .First();

        return best.Count > 0 ? best.Delimiter : ';';
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        int count = 0;
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == delimiter && !inQuotes) count++;
        }

        return count;
    }

    private static List<string> SplitLine(string line, char delimiter)
    {
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
            else if (c == delimiter)
            {
                values.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        values.Add(field.ToString());
        return values;
    }

    private static string Describe(char delimiter) => delimiter == '\t' ? "\\t" : delimiter.ToString();
}
