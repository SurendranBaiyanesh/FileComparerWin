using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Readers;

/// <summary>Picks the reader for a file from its extension. Add a reader here to support another format.</summary>
public sealed class TableReaderFactory
{
    #region Constants

    /// <summary>Every extension a reader claims, as an Open dialog filter.</summary>
    public const string FileDialogFilter =
        "Data files|*.csv;*.txt;*.tsv;*.psv;*.dat;*.text;*.xml;*.json;*.xlsx;*.xlsm|" +
        "Delimited text (*.csv;*.txt;*.tsv;*.psv)|*.csv;*.txt;*.tsv;*.psv;*.dat;*.text|" +
        "XML (*.xml)|*.xml|" +
        "JSON (*.json)|*.json|" +
        "Excel workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|" +
        "All files (*.*)|*.*";

    #endregion

    #region Fields

    private readonly List<ITableReader> _readers =
    [
        new DelimitedTableReader(),
        new XmlTableReader(),
        new JsonTableReader(),
        new XlsxTableReader()
    ];

    #endregion

    #region Public methods

    public DataTable Load(string path, ComparisonOptions options)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");

        ITableReader reader = _readers.FirstOrDefault(r => r.CanRead(path))
                     ?? throw new NotSupportedException(
                         $"No reader for '{Path.GetExtension(path)}' files. Supported: .csv .txt .tsv .psv .xml .json .xlsx");

        return reader.Read(path, options);
    }

    #endregion
}
