using System.IO.Compression;
using System.Xml.Linq;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Readers;

/// <summary>Reads the first (or named) worksheet of an .xlsx workbook straight from the Open XML package,
/// so no spreadsheet library is needed.</summary>
public sealed class XlsxTableReader : ITableReader
{
    #region Fields

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly string[] Extensions = [".xlsx", ".xlsm"];

    #endregion

    #region Properties

    public string FormatName => "Excel workbook";

    #endregion

    #region Public methods

    public bool CanRead(string path) => IsWorkbook(path);

    public static bool IsWorkbook(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public DataTable Read(string path, ComparisonOptions options)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);

        List<string> sharedStrings = ReadSharedStrings(archive);
        (string sheetName, string sheetPath) = ResolveSheet(archive, string.Empty, path);
        XDocument sheet = LoadXml(archive, sheetPath)
                    ?? throw new InvalidDataException($"Worksheet '{sheetPath}' is missing from '{path}'.");

        List<XElement> rows = sheet.Root?.Element(Main + "sheetData")?.Elements(Main + "row").ToList() ?? [];
        List<(int LineNumber, List<string> Values)> cellRows = rows
            .Select(r => (LineNumber: (int)(r.Attribute("r") is { } a && int.TryParse(a.Value, out int n) ? n : 0), Values: ReadRow(r, sharedStrings)))
            .Where(r => r.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
            .ToList();

        if (cellRows.Count == 0)
            throw new InvalidDataException($"Worksheet '{sheetName}' in '{path}' is empty.");

        // Without a header row the first row is a record like any other, and the names are made up to
        // match whatever the other file made up - a sheet that starts at its data has none to give.
        if (options.NoHeaderRow)
            return TableBuilder.Build(path, $"{FormatName} (sheet '{sheetName}', no header row)",
                TableBuilder.GeneratedHeader(cellRows.Max(r => r.Values.Count)), cellRows);

        List<string> header = cellRows[0].Values;
        List<(int LineNumber, List<string> Values)> records = cellRows.Skip(1).Select(r => (r.LineNumber, r.Values)).ToList();

        return TableBuilder.Build(path, $"{FormatName} (sheet '{sheetName}')", header, records);
    }

    /// <summary>The worksheet names in a workbook, so the user interface can offer them.</summary>
    public static IReadOnlyList<string> ListSheetNames(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        XDocument? workbook = LoadXml(archive, "xl/workbook.xml");

        return workbook?.Root?.Element(Main + "sheets")?.Elements(Main + "sheet")
            .Select(s => s.Attribute("name")?.Value ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToList() ?? [];
    }

    #endregion

    #region Private methods

    private static List<string> ReadRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        List<string> values = new List<string>();

        foreach (XElement cell in row.Elements(Main + "c"))
        {
            int columnIndex = ColumnIndex(cell.Attribute("r")?.Value, values.Count);
            while (values.Count < columnIndex)
                values.Add(string.Empty);

            values.Add(ReadCell(cell, sharedStrings));
        }

        return values;
    }

    private static string ReadCell(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        string? type = cell.Attribute("t")?.Value;
        string raw = cell.Element(Main + "v")?.Value ?? string.Empty;

        return type switch
        {
            "s" => int.TryParse(raw, out int index) && index < sharedStrings.Count ? sharedStrings[index] : string.Empty,
            "inlineStr" => JoinText(cell.Element(Main + "is")),
            "b" => raw == "1" ? "TRUE" : "FALSE",
            _ => raw
        };
    }

    /// <summary>Converts the letter part of a cell reference ("C7" -> 2). Falls back to the running position
    /// when the reference is absent.</summary>
    private static int ColumnIndex(string? cellReference, int fallback)
    {
        if (string.IsNullOrEmpty(cellReference))
            return fallback;

        int index = 0;
        foreach (char c in cellReference)
        {
            if (!char.IsLetter(c))
                break;

            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index > 0 ? index - 1 : fallback;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        XDocument? document = LoadXml(archive, "xl/sharedStrings.xml");
        return document?.Root?.Elements(Main + "si").Select(JoinText).ToList() ?? [];
    }

    /// <summary>Concatenates the text runs of a shared or inline string.</summary>
    private static string JoinText(XElement? element) =>
        element is null ? string.Empty : string.Concat(element.Descendants(Main + "t").Select(t => t.Value));

    private static (string Name, string Path) ResolveSheet(ZipArchive archive, string requestedName, string filePath)
    {
        XDocument workbook = LoadXml(archive, "xl/workbook.xml")
                       ?? throw new InvalidDataException($"'{filePath}' is not a valid .xlsx workbook.");

        List<XElement> sheets = workbook.Root?.Element(Main + "sheets")?.Elements(Main + "sheet").ToList() ?? [];
        if (sheets.Count == 0)
            throw new InvalidDataException($"'{filePath}' contains no worksheets.");

        XElement sheet = string.IsNullOrWhiteSpace(requestedName)
            ? sheets[0]
            : sheets.FirstOrDefault(s => string.Equals(s.Attribute("name")?.Value, requestedName, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidDataException(
                  $"Worksheet '{requestedName}' not found in '{filePath}'. Available: {string.Join(", ", sheets.Select(s => s.Attribute("name")?.Value))}");

        string name = sheet.Attribute("name")?.Value ?? "Sheet1";
        string? relationshipId = sheet.Attribute(Relationships + "id")?.Value;
        string target = ResolveRelationshipTarget(archive, relationshipId) ?? "xl/worksheets/sheet1.xml";

        return (name, target);
    }

    private static string? ResolveRelationshipTarget(ZipArchive archive, string? relationshipId)
    {
        if (string.IsNullOrEmpty(relationshipId))
            return null;

        XDocument? rels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        string? target = rels?.Root?.Elements(PackageRelationships + "Relationship")
            .FirstOrDefault(r => r.Attribute("Id")?.Value == relationshipId)?
            .Attribute("Target")?.Value;

        if (string.IsNullOrEmpty(target))
            return null;

        target = target.Replace('\\', '/').TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target;
    }

    private static XDocument? LoadXml(ZipArchive archive, string entryPath)
    {
        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), entryPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return null;

        using Stream stream = entry.Open();
        return XDocument.Load(stream);
    }

    #endregion
}
