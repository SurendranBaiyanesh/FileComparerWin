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
	private static readonly string[] CandidateDelimiters = [";", ",", "\t", "|"];
	private static readonly string[] Extensions = [".csv", ".txt", ".tsv", ".psv", ".dat", ".text"];
	#endregion

	#region Properties
	public string FormatName => "Delimited text";
	#endregion

	#region Public methods
	public bool CanRead(string path)
	{
		return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
	}

	public DataTable Read(string path, ComparisonOptions options)
	{
		TextContent content = TextFile.Read(path, options.Encoding);
		string[] lines = content.Lines();

		// A line cut by position has no delimiter and no header: the names are made up from the
		// positions, and the first line is a record like every other.
		if(options.IsDynamic)
		{
			int[] positions = SplitPositions.Parse(options.SplitIndexes);
			if(positions.Length == 0)
			{

				throw new InvalidDataException($"The delimiter is '{ComparisonOptions.DynamicDelimiter}', which cuts the line at fixed positions, " +
				                               "but no positions were given." + Environment.NewLine + "  Fill in Split at, e.g. 1;2;5;13, or choose a delimiter to read the file by.");
			}

			return ReadByPosition(path, content, lines, positions);
		}

		int headerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
		if(headerIndex < 0) throw new InvalidDataException($"'{path}' is empty.");

		string delimiter = ResolveDelimiter(options.Delimiter, lines[headerIndex]);

		// Without a header row the first line is a record too, and the names are made up to match
		// whatever the other file made up.
		List<string> header = options.NoHeaderRow ? TableBuilder.GeneratedHeader(SplitLine(lines[headerIndex], delimiter).Count) : SplitLine(lines[headerIndex], delimiter);

		List<(int, List<string>)> records = new();
		for(int i = options.NoHeaderRow ? headerIndex : headerIndex + 1; i < lines.Length; i++)
		{
			if(string.IsNullOrWhiteSpace(lines[i])) continue;

			records.Add((i + 1, SplitLine(lines[i], delimiter)));
		}

		return TableBuilder.Build(path, $"{this.FormatName} ('{Describe(delimiter)}' separated, {content.EncodingName})", header, records, delimiter, options.NoHeaderRow);
	}
	#endregion

	#region Private methods
	/// <summary>
	/// Reads a file whose columns are marked out by position. Every line is a record - there is no
	/// header to read names from - so the columns are called column1, column2 and so on, one for each
	/// position given.
	/// </summary>
	private DataTable ReadByPosition(string path, TextContent content, string[] lines, int[] positions)
	{
		List<string> header = TableBuilder.GeneratedHeader(positions.Length);

		List<(int LineNumber, List<string> Values)> records = new();
		for(int i = 0; i < lines.Length; i++)
		{
			if(string.IsNullOrWhiteSpace(lines[i])) continue;

			records.Add((i + 1, SplitPositions.Split(lines[i], positions)));
		}

		if(records.Count == 0) throw new InvalidDataException($"'{path}' is empty.");

		return TableBuilder.Build(path, $"{this.FormatName} (split at {positions.Length} position(s), {content.EncodingName})", header, records, generatedColumnNames: true);
	}

	private static string ResolveDelimiter(string configured, string headerLine)
	{
		// Taken whole rather than one character of it: a separator is not always a single character,
		// and a longer one typed into the box used to be cut down to its first letter without a word.
		if(!string.IsNullOrEmpty(configured)) return configured switch { "\\t" or "tab" or "TAB" => "\t", _ => configured };

		(string Delimiter, int Count) best = CandidateDelimiters.Select(d => (Delimiter: d, Count: CountOutsideQuotes(headerLine, d))).OrderByDescending(x => x.Count).First();

		return best.Count > 0 ? best.Delimiter : ";";
	}

	/// <summary>
	/// A delimiter with a quote in it leaves no quotes over to quote anything with: every quote on the
	/// line belongs to a separator. Such a file is cut on the separator and read exactly as written,
	/// rather than through the RFC 4180 rules the other delimiters are read by.
	/// </summary>
	private static bool QuotesAreLiteral(string delimiter)
	{
		return delimiter.Contains('"');
	}

	private static int CountOutsideQuotes(string line, string delimiter)
	{
		if(QuotesAreLiteral(delimiter)) return CountLiteral(line, delimiter);

		int count = 0;
		bool inQuotes = false;
		for(int i = 0; i < line.Length; i++)
			if(line[i] == '"')
			{
				inQuotes = !inQuotes;
			}
			else if(!inQuotes && StartsWith(line, i, delimiter))
			{
				count++;
				i += delimiter.Length - 1;
			}

		return count;
	}

	private static int CountLiteral(string line, string delimiter)
	{
		int count = 0;
		for(int i = line.IndexOf(delimiter, StringComparison.Ordinal);
		    i >= 0;
		    i = line.IndexOf(delimiter, i + delimiter.Length, StringComparison.Ordinal))
			count++;

		return count;
	}

	private static bool StartsWith(string line, int index, string delimiter)
	{
		return index + delimiter.Length <= line.Length && string.CompareOrdinal(line, index, delimiter, 0, delimiter.Length) == 0;
	}

	private static List<string> SplitLine(string line, string delimiter)
	{
		if(QuotesAreLiteral(delimiter)) return [.. line.Split(delimiter, StringSplitOptions.None)];

		List<string> values = new();
		StringBuilder field = new();
		bool inQuotes = false;

		for(int i = 0; i < line.Length; i++)
		{
			char c = line[i];
			if(inQuotes)
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
					inQuotes = false;
				}
			}
			else if(c == '"')
			{
				inQuotes = true;
			}
			else if(StartsWith(line, i, delimiter))
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

	private static string Describe(string delimiter)
	{
		return delimiter == "\t" ? "\\t" : delimiter;
	}
	#endregion
}