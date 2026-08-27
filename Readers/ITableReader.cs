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
	/// <summary>
	/// <paramref name="delimiter"/> is what separated the values, for the readers that had one; it is
	/// carried on the table so that reporting can show a row in the shape the file wrote it.
	/// </summary>

	#region Public methods
	/// <summary>
	/// Column names for a file that carries none: column1, column2 and so on. Shared by the readers so
	/// that a file cut by position and a spreadsheet read without its first row agree on what to call
	/// their columns, which is what lets one be compared against the other.
	/// </summary>
	public static List<string> GeneratedHeader(int count)
	{
		return [.. Enumerable.Range(1, count).Select(n => $"column{n}")];
	}

	public static DataTable Build(string path, string formatName, IReadOnlyList<string> header, List<(int LineNumber, List<string> Values)> records, string? delimiter = null, bool generatedColumnNames = false)
	{
		List<string> liColumns = TrimTrailingEmpty(header);
		if(liColumns.Count == 0) throw new InvalidDataException($"No column headers found in '{path}'.");

		int nWidth = Math.Max(liColumns.Count, records.Count == 0 ? 0 : records.Max(r => TrimTrailingEmpty(r.Values).Count));
		for(int i = liColumns.Count; i < nWidth; i++)
			liColumns.Add($"Column{i + 1}");

		EnsureUniqueNames(liColumns);

		List<DataRow> liRows = new(records.Count);
		foreach((int lineNumber, List<string> values) in records)
		{
			string[] liPadded = new string[nWidth];
			for(int i = 0; i < nWidth; i++) liPadded[i] = i < values.Count ? values[i] : string.Empty;

			liRows.Add(new DataRow(lineNumber, liPadded));
		}

		return new DataTable(path, formatName, liColumns, liRows, delimiter, generatedColumnNames);
	}
	#endregion

	#region Private methods
	private static List<string> TrimTrailingEmpty(IReadOnlyList<string> values)
	{
		int nLast = values.Count - 1;
		while(nLast >= 0 && string.IsNullOrWhiteSpace(values[nLast])) nLast--;

		return [.. values.Take(nLast + 1).Select(v => v ?? string.Empty)];
	}

	private static void EnsureUniqueNames(List<string> liColumns)
	{
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		for(int i = 0; i < liColumns.Count; i++)
		{
			string strName = liColumns[i].Trim();
			if(strName.Length == 0) strName = $"Column{i + 1}";

			string strCandidate = strName;
			int nSuffix = 2;
			while(!seen.Add(strCandidate)) strCandidate = $"{strName}_{nSuffix++}";

			liColumns[i] = strCandidate;
		}
	}
	#endregion
}