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
	private static readonly XNamespace MAIN = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
	private static readonly XNamespace RELATIONSHIPS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
	private static readonly XNamespace PACKAGE_RELATIONSHIPS = "http://schemas.openxmlformats.org/package/2006/relationships";
	private static readonly string[] EXTENSIONS = [".xlsx", ".xlsm"];
	#endregion

	#region Properties
	public string FormatName => "Excel workbook";
	#endregion

	#region Public methods
	public bool CanRead(string path)
	{
		return IsWorkbook(path);
	}

	public static bool IsWorkbook(string path)
	{
		return EXTENSIONS.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
	}

	public DataTable Read(string path, ComparisonOptions options)
	{
		using ZipArchive archive = ZipFile.OpenRead(path);

		List<string> liSharedStrings = ReadSharedStrings(archive);
		(string sheetName, string sheetPath) = ResolveSheet(archive, string.Empty, path);
		XDocument sheet = LoadXml(archive, sheetPath) ?? throw new InvalidDataException($"Worksheet '{sheetPath}' is missing from '{path}'.");

		List<XElement> liRows = sheet.Root?.Element(MAIN + "sheetData")?.Elements(MAIN + "row").ToList() ?? [];
		List<(int LineNumber, List<string> Values)> cellRows = liRows
		                                                       .Select(r => (LineNumber: (int) (r.Attribute("r") is { } a && int.TryParse(a.Value, out int n) ? n : 0), Values: ReadRow(r, liSharedStrings)))
		                                                       .Where(r => r.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
		                                                       .ToList();

		if(cellRows.Count == 0) throw new InvalidDataException($"Worksheet '{sheetName}' in '{path}' is empty.");

		// Without a header row the first row is a record like any other, and the names are made up to
		// match whatever the other file made up - a sheet that starts at its data has none to give.
		if(options.NoHeaderRow)
		{
			return TableBuilder.Build(path, $"{this.FormatName} (sheet '{sheetName}', no header row)", TableBuilder.GeneratedHeader(cellRows.Max(r => r.Values.Count)), cellRows, generatedColumnNames: true);
		}

		List<string> liHeader = cellRows[0].Values;
		List<(int LineNumber, List<string> Values)> records = cellRows.Skip(1).Select(r => (r.LineNumber, r.Values)).ToList();

		return TableBuilder.Build(path, $"{this.FormatName} (sheet '{sheetName}')", liHeader, records);
	}

	/// <summary>The worksheet names in a workbook, so the user interface can offer them.</summary>
	public static IReadOnlyList<string> ListSheetNames(string path)
	{
		using ZipArchive archive = ZipFile.OpenRead(path);
		XDocument? workbook = LoadXml(archive, "xl/workbook.xml");

		return workbook?.Root?.Element(MAIN + "sheets")?.Elements(MAIN + "sheet").Select(s => s.Attribute("name")?.Value ?? string.Empty).Where(n => n.Length > 0).ToList() ?? [];
	}
	#endregion

	#region Private methods
	private static List<string> ReadRow(XElement row, IReadOnlyList<string> liSharedStrings)
	{
		List<string> liValues = new();

		foreach(XElement cell in row.Elements(MAIN + "c"))
		{
			int nColumnIndex = ColumnIndex(cell.Attribute("r")?.Value, liValues.Count);
			while(liValues.Count < nColumnIndex) liValues.Add(string.Empty);

			liValues.Add(ReadCell(cell, liSharedStrings));
		}

		return liValues;
	}

	private static string ReadCell(XElement cell, IReadOnlyList<string> liSharedStrings)
	{
		string? strType = cell.Attribute("t")?.Value;
		string strRaw = cell.Element(MAIN + "v")?.Value ?? string.Empty;

		return strType switch
		{
			"s" => int.TryParse(strRaw, out int nIndex) && nIndex < liSharedStrings.Count ? liSharedStrings[nIndex] : string.Empty,
			"inlineStr" => JoinText(cell.Element(MAIN + "is")),
			"b" => strRaw == "1" ? "TRUE" : "FALSE",
			_ => strRaw
		};
	}

	/// <summary>Converts the letter part of a cell reference ("C7" -> 2). Falls back to the running position
	/// when the reference is absent.</summary>
	private static int ColumnIndex(string? cellReference, int fallback)
	{
		if(string.IsNullOrEmpty(cellReference)) return fallback;

		int nIndex = 0;
		foreach(char c in cellReference)
		{
			if(!char.IsLetter(c)) break;

			nIndex = nIndex * 26 + (char.ToUpperInvariant(c) - 'A') + 1;
		}

		return nIndex > 0 ? nIndex - 1 : fallback;
	}

	private static List<string> ReadSharedStrings(ZipArchive archive)
	{
		XDocument? document = LoadXml(archive, "xl/sharedStrings.xml");
		return document?.Root?.Elements(MAIN + "si").Select(JoinText).ToList() ?? [];
	}

	/// <summary>Concatenates the text runs of a shared or inline string.</summary>
	private static string JoinText(XElement? element)
	{
		return element is null ? string.Empty : string.Concat(element.Descendants(MAIN + "t").Select(t => t.Value));
	}

	private static (string Name, string Path) ResolveSheet(ZipArchive archive, string requestedName, string filePath)
	{
		XDocument workbook = LoadXml(archive, "xl/workbook.xml") ?? throw new InvalidDataException($"'{filePath}' is not a valid .xlsx workbook.");

		List<XElement> liSheets = workbook.Root?.Element(MAIN + "sheets")?.Elements(MAIN + "sheet").ToList() ?? [];
		if(liSheets.Count == 0) throw new InvalidDataException($"'{filePath}' contains no worksheets.");

		XElement sheet = string.IsNullOrWhiteSpace(requestedName)
			? liSheets[0]
			: liSheets.FirstOrDefault(s => string.Equals(s.Attribute("name")?.Value, requestedName, StringComparison.OrdinalIgnoreCase))
			  ?? throw new InvalidDataException($"Worksheet '{requestedName}' not found in '{filePath}'. Available: {string.Join(", ", liSheets.Select(s => s.Attribute("strName")?.Value))}");

		string strName = sheet.Attribute("name")?.Value ?? "Sheet1";
		string? strRelationshipId = sheet.Attribute(RELATIONSHIPS + "id")?.Value;
		string strTarget = ResolveRelationshipTarget(archive, strRelationshipId) ?? "xl/worksheets/sheet1.xml";

		return (strName, strTarget);
	}

	private static string? ResolveRelationshipTarget(ZipArchive archive, string? strRelationshipId)
	{
		if(string.IsNullOrEmpty(strRelationshipId))
			return null;

		XDocument? rels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
		string? strTarget = rels?.Root?.Elements(PACKAGE_RELATIONSHIPS + "Relationship").FirstOrDefault(r => r.Attribute("Id")?.Value == strRelationshipId)?.Attribute("Target")?.Value;

		if(string.IsNullOrEmpty(strTarget)) return null;

		strTarget = strTarget.Replace('\\', '/').TrimStart('/');
		return strTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? strTarget : "xl/" + strTarget;
	}

	private static XDocument? LoadXml(ZipArchive archive, string entryPath)
	{
		ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), entryPath, StringComparison.OrdinalIgnoreCase));
		if(entry is null) return null;

		using Stream stream = entry.Open();
		return XDocument.Load(stream);
	}
	#endregion
}