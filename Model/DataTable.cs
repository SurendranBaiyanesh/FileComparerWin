namespace FileComparerWindows.Model;

/// <summary>Format-independent view of a tabular file: named columns plus rows of string values.</summary>
public sealed class DataTable
{
    #region Fields

    private readonly Dictionary<string, int> _columnIndex;

    #endregion

    #region Constructor

    public DataTable(string sourcePath, string formatName, IReadOnlyList<string> columns, IReadOnlyList<DataRow> rows,
                     string? delimiter = null, bool generatedColumnNames = false)
    {
        SourcePath = sourcePath;
        FormatName = formatName;
        Columns = columns;
        Rows = rows;
        Delimiter = delimiter;
        HasGeneratedColumnNames = generatedColumnNames;

        // Indexed on the canonical form so a header written with combining accents still answers to
        // the same name typed with precomposed ones.
        _columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < columns.Count; i++)
            _columnIndex.TryAdd(TextKey.Canonical(columns[i]), i);
    }

    #endregion

    #region Properties

    public string SourcePath { get; }
    public string FormatName { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<DataRow> Rows { get; }

    /// <summary>
    /// What separated the values in the file, for the formats that separate them with anything. Null
    /// for a workbook, an XML document or a JSON array, which have no separator to speak of. Reporting
    /// keeps it so that a row can be shown again in the shape it was written in.
    /// </summary>
    public string? Delimiter { get; }

    /// <summary>
    /// True when the file carried no names of its own and the columns were called column1, column2 and
    /// so on. Reporting keeps it because such a name says nothing a reader does not already know:
    /// writing "column1=EI" next to "column2=0" is three quarters punctuation.
    /// </summary>
    public bool HasGeneratedColumnNames { get; }

    #endregion

    #region Public methods

    public bool HasColumn(string name) => _columnIndex.ContainsKey(TextKey.Canonical(name));

    public string GetValue(DataRow row, string column)
    {
        if (!_columnIndex.TryGetValue(TextKey.Canonical(column), out int index))
            return string.Empty;

        return index < row.Values.Count ? row.Values[index] : string.Empty;
    }

    /// <summary>Resolves a column name to the casing used in this file, so reports echo the file's own header.</summary>
    public string ResolveColumnName(string name) =>
        _columnIndex.TryGetValue(TextKey.Canonical(name), out int index) ? Columns[index] : name;

    #endregion
}

public sealed class DataRow(int lineNumber, IReadOnlyList<string> values)
{
    #region Properties

    /// <summary>1-based position of the record in its source file, used to point the user at the offending row.</summary>
    public int LineNumber { get; } = lineNumber;

    public IReadOnlyList<string> Values { get; } = values;

    #endregion

    #region Public methods

    public string ToDisplayString() => string.Join(";", Values);

    #endregion
}
