using System.Text.Json;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Readers;

/// <summary>Reads a JSON array of objects, or an object whose first array property holds the records.</summary>
public sealed class JsonTableReader : ITableReader
{
    public string FormatName => "JSON";

    public bool CanRead(string path) =>
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    public DataTable Read(string path, ComparisonOptions options)
    {
        using JsonDocument document = JsonDocument.Parse(TextFile.Read(path, options.Encoding).Text, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        JsonElement array = FindRecordArray(document.RootElement)
                    ?? throw new InvalidDataException($"No array of records found in '{path}'.");

        List<string> columns = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<Dictionary<string, string>> cellsPerRow = new List<Dictionary<string, string>>();

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"'{path}' contains a non-object record; expected an array of objects.");

            Dictionary<string, string> cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in item.EnumerateObject())
            {
                cells[property.Name] = ToText(property.Value);
                if (seen.Add(property.Name))
                    columns.Add(property.Name);
            }

            cellsPerRow.Add(cells);
        }

        List<(int, List<string>)> records = cellsPerRow
            .Select((cells, index) => (index + 1, columns.Select(c => cells.GetValueOrDefault(c, string.Empty)).ToList()))
            .ToList();

        return TableBuilder.Build(path, FormatName, columns, records);
    }

    private static JsonElement? FindRecordArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root;

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        foreach (JsonProperty property in root.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Array)
                return property.Value;

        return null;
    }

    private static string ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
        _ => value.ToString()
    };
}
