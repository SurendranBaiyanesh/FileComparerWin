using System.Text;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Readers;

/// <summary>Reads separated-value text files (.csv, .txt, .tsv, .psv) with RFC 4180 style quoting.</summary>
public sealed class DelimitedTableReader : ITableReader
{
	#region Fields
	// What detection tries. |" is deliberately not among them: every |" is also a |, so the two look
	// alike in a header, and a comma file holding one quoted field that ends in a pipe looks like it
	// too. Guessing it would misread files that are read correctly today, so it is chosen instead.
	private static readonly string[] CANDIDATE_DELIMITERS = [";", ",", "\t", "|"];
	private static readonly string[] EXTENSIONS = [".csv", ".txt", ".tsv", ".psv", ".dat", ".text"];
	#endregion

	#region Properties
	public string FormatName => "Delimited text";
	#endregion

	#region Public methods
	public bool CanRead(string path)
	{
		return EXTENSIONS.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
	}

	public DataTable Read(string path, ComparisonOptions options)
	{
		TextContent content = TextFile.Read(path, options.Encoding);
		string[] liLines = content.Lines();

		// A line cut by position has no delimiter and no header: the names are made up from the
		// positions, and the first line is a record like every other.
		if(options.IsDynamic)
		{
			int[] liPositions = SplitPositions.Parse(options.SplitIndexes);
			if(liPositions.Length == 0)
			{

				throw new InvalidDataException($"The delimiter is '{ComparisonOptions.DYNAMIC_DELIMITER}', which cuts the line at fixed positions, " +
				                               "but no positions were given." + Environment.NewLine + "  Fill in Split at, e.g. 1;2;5;13, or choose a delimiter to read the file by.");
			}

			return ReadByPosition(path, content, liLines, liPositions);
		}

		int nHeaderIndex = Array.FindIndex(liLines, l => !string.IsNullOrWhiteSpace(l));
		if(nHeaderIndex < 0) throw new InvalidDataException($"'{path}' is empty.");

		string strDelimiter = ResolveDelimiter(options.Delimiter, liLines[nHeaderIndex]);

		// Without a header row the first line is a record too, and the names are made up to match
		// whatever the other file made up.
		List<string> liHeader = options.NoHeaderRow ? TableBuilder.GeneratedHeader(SplitLine(liLines[nHeaderIndex], strDelimiter).Count) : SplitLine(liLines[nHeaderIndex], strDelimiter);

		List<(int, List<string>)> records = new();
		for(int i = options.NoHeaderRow ? nHeaderIndex : nHeaderIndex + 1; i < liLines.Length; i++)
		{
			if(string.IsNullOrWhiteSpace(liLines[i])) continue;

			records.Add((i + 1, SplitLine(liLines[i], strDelimiter)));
		}

		return TableBuilder.Build(path, $"{this.FormatName} ('{Describe(strDelimiter)}' separated, {content.EncodingName})", liHeader, records, strDelimiter, options.NoHeaderRow);
	}
	#endregion

	#region Private methods
	/// <summary>
	/// Reads a file whose columns are marked out by position. Every line is a record - there is no
	/// header to read names from - so the columns are called column1, column2 and so on, one for each
	/// position given.
	/// </summary>
	private DataTable ReadByPosition(string path, TextContent content, string[] liLines, int[] liPositions)
	{
		List<string> liHeader = TableBuilder.GeneratedHeader(liPositions.Length);

		List<(int LineNumber, List<string> Values)> records = new();
		for(int i = 0; i < liLines.Length; i++)
		{
			if(string.IsNullOrWhiteSpace(liLines[i])) continue;

			records.Add((i + 1, SplitPositions.Split(liLines[i], liPositions)));
		}

		if(records.Count == 0) throw new InvalidDataException($"'{path}' is empty.");

		return TableBuilder.Build(path, $"{this.FormatName} (split at {liPositions.Length} position(s), {content.EncodingName})", liHeader, records, generatedColumnNames: true);
	}

	private static string ResolveDelimiter(string configured, string headerLine)
	{
		// Taken whole rather than one character of it: a separator is not always a single character,
		// and a longer one typed into the box used to be cut down to its first letter without a word.
		if(!string.IsNullOrEmpty(configured)) return configured switch { "\\t" or "tab" or "TAB" => "\t", _ => configured };

		(string Delimiter, int Count) best = CANDIDATE_DELIMITERS.Select(d => (Delimiter: d, Count: CountOutsideQuotes(headerLine, d))).OrderByDescending(x => x.Count).First();

		return best.Count > 0 ? best.Delimiter : ";";
	}

	/// <summary>
	/// A delimiter with a quote in it leaves no quotes over to quote anything with: every quote on the
	/// line belongs to a separator. Such a file is cut on the separator and read exactly as written,
	/// rather than through the RFC 4180 rules the other delimiters are read by.
	/// </summary>
	private static bool QuotesAreLiteral(string strDelimiter)
	{
		return strDelimiter.Contains('"');
	}

	private static int CountOutsideQuotes(string line, string strDelimiter)
	{
		if(QuotesAreLiteral(strDelimiter)) return CountLiteral(line, strDelimiter);

		int nCount = 0;
		bool bInQuotes = false;
		for(int i = 0; i < line.Length; i++)
			if(line[i] == '"')
			{
				bInQuotes = !bInQuotes;
			}
			else if(!bInQuotes && StartsWith(line, i, strDelimiter))
			{
				nCount++;
				i += strDelimiter.Length - 1;
			}

		return nCount;
	}

	private static int CountLiteral(string line, string strDelimiter)
	{
		int nCount = 0;
		for(int i = line.IndexOf(strDelimiter, StringComparison.Ordinal);
		    i >= 0;
		    i = line.IndexOf(strDelimiter, i + strDelimiter.Length, StringComparison.Ordinal))
			nCount++;

		return nCount;
	}

	private static bool StartsWith(string line, int index, string strDelimiter)
	{
		return index + strDelimiter.Length <= line.Length && string.CompareOrdinal(line, index, strDelimiter, 0, strDelimiter.Length) == 0;
	}

	private static List<string> SplitLine(string line, string strDelimiter)
	{
		if(QuotesAreLiteral(strDelimiter)) return [.. line.Split(strDelimiter, StringSplitOptions.None)];

		List<string> liValues = new();
		StringBuilder field = new();
		bool bInQuotes = false;

		for(int i = 0; i < line.Length; i++)
		{
			char c = line[i];
			if(bInQuotes)
			{
				if(c != '"')
				{
					field.Append(c);
				}
				else if(i + 1 < line.Length && line[i + 1] == '"')
				{
					field.Append('"');
					i++;
				}
				else
				{
					bInQuotes = false;
				}
			}
			else if(c == '"')
			{
				bInQuotes = true;
			}
			else if(StartsWith(line, i, strDelimiter))
			{
				liValues.Add(field.ToString());
				field.Clear();
				i += strDelimiter.Length - 1;
			}
			else
			{
				field.Append(c);
			}
		}

		liValues.Add(field.ToString());
		return liValues;
	}

	private static string Describe(string strDelimiter)
	{
		return strDelimiter == "\t" ? "\\t" : strDelimiter;
	}
	#endregion
}