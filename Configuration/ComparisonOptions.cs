using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileComparerWindows.Model;

namespace FileComparerWindows.Configuration;

public sealed class ComparisonOptions
{
    #region Properties
    public string InputFilePath { get; set; } = string.Empty;
    public string OutputFilePath { get; set; } = string.Empty;

    /// <summary>Columns whose values identify a row, e.g. ["PersonNumber"] or ["Name", "PersonNumber"].</summary>
    public List<string> KeyColumns { get; set; } = [];

    /// <summary>Columns to compare once rows are paired. Empty means every column the two files share.</summary>
    public List<string> CompareColumns { get; set; } = [];

    public bool IgnoreCase { get; set; }
    public bool TrimValues { get; set; } = true;

    /// <summary>
    /// Compares numbers by value rather than by how they were written, so 123.00 equals 123 and
    /// 110.60 equals 110.6. Values that are not plain numbers are compared as text, as always.
    /// </summary>
    public bool SimilarMatch { get; set; }

    /// <summary>
    /// How far apart two numbers may be and still count as equal. 0 requires them to be exactly equal;
    /// 1 lets 100 match anything from 99 to 101, and 2 anything from 98 to 102. Only has an effect
    /// while <see cref="SimilarMatch"/> is on, since that is what makes a value a number rather than
    /// the text it is written as, and only for values that are numbers on both sides.
    /// </summary>
    public decimal SimilarMatchRange { get; set; }

    /// <summary>
    /// The delimiter that means "there is no delimiter - cut the line at <see cref="SplitIndexes"/>
    /// instead". Spelt out in the settings file rather than inferred from the positions being filled
    /// in, so that a file can keep its positions while being read by an ordinary separator again.
    /// </summary>
    public const string DynamicDelimiter = "Dynamic";

    /// <summary>Delimiter for text files. Empty means detect it from the header line.</summary>
    public string Delimiter { get; set; } = string.Empty;

    /// <summary>True when the files are to be cut at fixed positions rather than on a separator.</summary>
    public bool IsDynamic =>
        string.Equals(Delimiter.Trim(), DynamicDelimiter, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where to cut a fixed-width line that has no delimiter to cut on, as a list of positions:
    /// "1;2;5;13". Each number is the position of the last character of its column, counting the first
    /// character of the line as 0, so 1;2;5 takes two characters, then one, then three. One column is
    /// produced per position, named column1, column2 and so on, every line of both files is cut the
    /// same way, and the files are read without a header line - a record laid out by position carries
    /// no names to read.
    ///
    /// Empty leaves the files to be read by their delimiter, which is what nearly every file wants.
    /// </summary>
    public string SplitIndexes { get; set; } = string.Empty;

    /// <summary>
    /// Reads both files as though every line or row were a record, naming the columns column1, column2
    /// and so on rather than taking the names from a first line that does not exist. Needed to compare
    /// a file laid out by position against a spreadsheet that likewise begins at its first record: the
    /// two sides have to arrive at the same column names or there is nothing to compare against.
    ///
    /// Implied for a file being cut by <see cref="SplitIndexes"/>, which has no header by definition.
    /// </summary>
    public bool NoHeaderRow { get; set; }

    /// <summary>
    /// Encoding of the text files, e.g. "windows-1252". Empty detects it, which is right unless a
    /// file happens to be valid UTF-8 while meaning something else.
    /// </summary>
    public string Encoding { get; set; } = string.Empty;

    /// <summary>The settings file the application reads at start-up and writes back to.</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    #endregion

    #region Public methods
    public static ComparisonOptions LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new ComparisonOptions();

        // Read the same tolerant way as the data files: a settings file saved as ANSI would otherwise
        // mangle the very column names it exists to specify.
        string json = TextFile.Read(path).Text;
        AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, ReadOptions);
        return settings?.FileComparer ?? new ComparisonOptions();
    }

    /// <summary>
    /// Writes the settings back under the same "FileComparer" section the console tool reads, so the
    /// two applications can share one file. UTF-8 without a byte order mark, since that is what the
    /// reader assumes when there is no mark to go on.
    /// </summary>
    public void SaveToFile(string path)
    {
        AppSettings settings = new AppSettings { FileComparer = this };
        string json = JsonSerializer.Serialize(settings, WriteOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
    #endregion

    #region Fields
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Column names carry accents; escaping them would leave a settings file no one can read.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    #endregion

    #region Nested types
    private sealed class AppSettings
    {
        [JsonPropertyName("FileComparer")]
        public ComparisonOptions? FileComparer { get; set; }
    }
    #endregion
}
