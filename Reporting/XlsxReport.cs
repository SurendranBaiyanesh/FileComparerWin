using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FileComparerWindows.Comparison;
using FileComparerWindows.Configuration;
using FileComparerWindows.Model;

namespace FileComparerWindows.Reporting;

/// <summary>
/// Writes the comparison to a workbook of six sheets, straight into the Open XML package the same way
/// <see cref="Readers.XlsxTableReader"/> reads one, so neither side needs a spreadsheet library.
///
/// Values out of the files are written as text throughout. Excel would otherwise read 007 as seven and
/// 1-2 as a date, and a report that quietly alters the values it is reporting on is worse than useless.
/// Only the counts on the overview, which this class works out itself, are written as numbers.
/// </summary>
public static class XlsxReport
{
    #region Constants

    // A relationship's Type is a plain URI in an attribute, not a namespace: XNamespace + "name" would
    // be written out as "{namespace}name" and leave a package Excel will not open.
    private const string RelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Excel's own ceiling. A sheet with one row more than this will not open at all.</summary>
    private const int SheetRowLimit = 1_048_576;

    // Column widths are counted in characters. Narrow enough and a heading is cut off by its own
    // column; wide enough and one long value pushes everything after it off the screen.
    private const int MinColumnWidth = 9;
    private const int MaxColumnWidth = 60;

    /// <summary>
    /// The one format the report defines, and the index it therefore has in cellXfs. Everything else
    /// is left at the workbook's default.
    /// </summary>
    private const int HeaderStyle = 1;

    /// <summary>What holds the values apart when the column names are numbers and are left out.</summary>
    private const string GeneratedNameSeparator = ";";

    #endregion

    #region Fields

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace DocumentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>
    /// The five value sheets all carry these, so that a reader learns one shape and a filter or a
    /// formula written against one of them works against any of them.
    /// </summary>
    private static readonly string[] StandardHeader =
        ["Keys", "Input file value", "Output file value", "Difference", "Other column values"];

    #endregion

    #region Public methods

    public static void Write(string path, ComparisonResult result, ComparisonOptions options)
    {
        // The value sheets first, because each one counts its own lines and the overview reports those
        // counts alongside its own.
        List<Sheet> values =
        [
            MatchingValues(result),
            AdditionalInOutput(result),
            MissingFromOutput(result),
            SimilarMatches(result, options),
            DifferenceValues(result)
        ];

        List<Sheet> sheets = [Overview(result, options, values), .. values];

        using FileStream file = File.Create(path);
        using ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create);

        Write(archive, "[Content_Types].xml", ContentTypesPart(sheets.Count));
        Write(archive, "_rels/.rels", RootRelationships());
        Write(archive, "xl/workbook.xml", Workbook(sheets));
        Write(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships(sheets.Count));
        Write(archive, "xl/styles.xml", Styles());

        for (int i = 0; i < sheets.Count; i++)
            Write(archive, $"xl/worksheets/sheet{i + 1}.xml", Worksheet(sheets[i]));
    }

    // ---------------------------------------------------------------- the sheets

    #endregion

    #region Private methods

    private static Sheet Overview(ComparisonResult result, ComparisonOptions options, List<Sheet> values)
    {
        Sheet sheet = new Sheet("Overview");

        sheet.AddHeader("File comparison");
        sheet.Add("Written", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
        sheet.Add("Verdict", result.IsMatch ? "SUCCESS - the two files agree" : "FAILED - the two files differ");
        sheet.Blank();

        sheet.Add("Input", result.Input.SourcePath);
        sheet.Add("", result.Input.FormatName);
        sheet.Add("Output", result.Output.SourcePath);
        sheet.Add("", result.Output.FormatName);
        sheet.Blank();

        sheet.Add("Key columns", string.Join(", ", result.KeyColumns));
        sheet.Add("Compared columns", string.Join(", ", result.ComparedColumns));
        sheet.Blank();

        // Two counts, because they answer different questions and would otherwise look like a
        // contradiction: a record is one row of the files, a line is one value on the sheet, and a
        // record of four compared columns puts four lines on it. The number on each tab is the lines.
        sheet.AddHeader("Sheet", "Counted", "Records", "Lines on the sheet");
        sheet.Add(Cell.Text(""), Cell.Text("Rows read from the input"), Cell.Number(result.InputRowCount));
        sheet.Add(Cell.Text(""), Cell.Text("Rows read from the output"), Cell.Number(result.OutputRowCount));
        sheet.Add(Cell.Text("2"), Cell.Text("Matching values"), Cell.Number(result.MatchedRows.Count), Cell.Number(values[0].DataRowCount));
        sheet.Add(Cell.Text("3"), Cell.Text("Additional values in the output"), Cell.Number(result.ExtraInOutput.Count), Cell.Number(values[1].DataRowCount));
        sheet.Add(Cell.Text("4"), Cell.Text("Values missing from the output"), Cell.Number(result.MissingInOutput.Count), Cell.Number(values[2].DataRowCount));
        sheet.Add(Cell.Text("5"), Cell.Text(SimilarHeading(options)), Cell.Number(result.SimilarMatches.Count), Cell.Number(values[3].DataRowCount));
        sheet.Add(Cell.Text("6"), Cell.Text("Rows with differences"), Cell.Number(result.ValueMismatches.Count), Cell.Number(values[4].DataRowCount));
        sheet.Blank();

        sheet.AddHeader("Options");
        sheet.Add("  Ignore case", options.IgnoreCase ? "yes" : "no");
        sheet.Add("  Trim values", options.TrimValues ? "yes" : "no");
        sheet.Add("  Similar match", options.SimilarMatch ? "yes" : "no");
        sheet.Add("  Similar range", Describe(options.SimilarMatchRange));
        sheet.Add("  Delimiter", options.Delimiter.Length == 0 ? "detect" : options.Delimiter);
        sheet.Add("  Encoding", options.Encoding.Length == 0 ? "detect" : options.Encoding);
        sheet.Blank();

        // The window trims its lists to the "Max rows" setting; a report that quietly left rows out
        // would be a worse thing than a long one, so every row is written here whatever that says.
        sheet.Add("Note", "Every row is written to the sheets, whatever Max rows is set to.");

        List<string> warnings = [.. result.DuplicateKeyWarnings, .. result.OptionWarnings];
        sheet.Blank();
        sheet.Add("Warnings", warnings.Count == 0 ? "none" : $"{warnings.Count}");
        foreach (string warning in warnings)
            sheet.Add("", warning);

        return sheet;
    }

    /// <summary>Row 1 of a value sheet: highlighted, and carrying the filter arrows.</summary>
    private static Sheet ValueSheet(string name)
    {
        Sheet sheet = new Sheet(name) { Filtered = true };
        sheet.AddHeader(StandardHeader);
        return sheet;
    }

    /// <summary>
    /// The tab, with how many lines are on it. Excel allows 31 characters and cuts off anything past
    /// them, which on the longer names would cut off the count itself - the one part that has to
    /// survive - so a shorter wording is kept for each and used when the full one will not fit.
    /// </summary>
    private static void NameWithCount(Sheet sheet, string full, string shortened)
    {
        string suffix = $" ({sheet.DataRowCount.ToString("N0", CultureInfo.CurrentCulture)})";

        foreach (string candidate in (string[])[full, shortened])
            if (candidate.Length + suffix.Length <= 31)
            {
                sheet.Rename(candidate + suffix);
                return;
            }

        int room = Math.Max(0, 31 - suffix.Length);
        sheet.Rename(shortened[..Math.Min(shortened.Length, room)].TrimEnd() + suffix);
    }

    /// <summary>
    /// One line per value rather than per record: the line is about a single column, and the rest of
    /// the record follows it for context.
    /// </summary>
    private static Sheet MatchingValues(ComparisonResult result)
    {
        Sheet sheet = ValueSheet("Matching Values");

        foreach (MatchedRow row in result.MatchedRows)
            foreach (string column in result.ComparedColumns)
            {
                if (sheet.IsFull)
                    return sheet;

                AddValue(sheet, result, row.DisplayKey, column,
                    result.Input.GetValue(row.InputRow, column),
                    result.Output.GetValue(row.OutputRow, column),
                    result.Input, row.InputRow);
            }

        NameWithCount(sheet, "Matching Values", "Matching Values");
        return sheet;
    }

    private static Sheet AdditionalInOutput(ComparisonResult result)
    {
        Sheet sheet = ValueSheet("Additional values in the output");   // 31 characters: exactly the limit

        // The record is on the output side only, so the input value of every column of it is nothing.
        foreach (KeyedRow row in result.ExtraInOutput)
            foreach (string column in result.ComparedColumns)
            {
                if (sheet.IsFull)
                    return sheet;

                AddValue(sheet, result, row.DisplayKey, column,
                    string.Empty, result.Output.GetValue(row.Row, column),
                    result.Output, row.Row);
            }

        NameWithCount(sheet, "Additional values in the output", "Additional in the output");
        return sheet;
    }

    private static Sheet MissingFromOutput(ComparisonResult result)
    {
        Sheet sheet = ValueSheet("Values missing from the output");

        foreach (KeyedRow row in result.MissingInOutput)
            foreach (string column in result.ComparedColumns)
            {
                if (sheet.IsFull)
                    return sheet;

                AddValue(sheet, result, row.DisplayKey, column,
                    result.Input.GetValue(row.Row, column), string.Empty,
                    result.Input, row.Row);
            }

        NameWithCount(sheet, "Values missing from the output", "Missing from the output");
        return sheet;
    }

    private static Sheet SimilarMatches(ComparisonResult result, ComparisonOptions options)
    {
        // "Similar matches (+/- 0.5) in the output" cannot be a sheet name: a slash is forbidden in one
        // and the wording runs past the 31 characters Excel allows. The tab says it as short as a tab
        // can; the overview names the sheet in full.
        Sheet sheet = ValueSheet($"Similar matches (±{Describe(options.SimilarMatchRange)})");

        // Only the values that needed the range: the rest of the record matched outright and follows
        // each line under Other column values.
        foreach (SimilarMatch match in result.SimilarMatches)
            foreach (ValueDifference value in match.Values)
            {
                if (sheet.IsFull)
                    return sheet;

                AddValue(sheet, result, match.DisplayKey, value.Column,
                    value.InputValue, value.OutputValue, result.Input, match.InputRow);
            }

        // With the option off there is no range to name, and "(±0) (0)" on the tab reads as a fault.
        NameWithCount(sheet,
            options.SimilarMatch && options.SimilarMatchRange > 0
                ? $"Similar matches (±{Describe(options.SimilarMatchRange)})"
                : "Similar matches",
            "Similar matches");

        return sheet;
    }

    private static Sheet DifferenceValues(ComparisonResult result)
    {
        Sheet sheet = ValueSheet("Difference values");

        foreach (RowMismatch mismatch in result.ValueMismatches)
            foreach (ValueDifference difference in mismatch.Differences)
            {
                if (sheet.IsFull)
                    return sheet;

                AddValue(sheet, result, mismatch.DisplayKey, difference.Column,
                    difference.InputValue, difference.OutputValue, result.Input, mismatch.InputRow);
            }

        NameWithCount(sheet, "Difference values", "Difference values");
        return sheet;
    }

    private static void AddValue(Sheet sheet, ComparisonResult result, string key, string column,
                                 string inputValue, string outputValue, DataTable table, DataRow row) =>
        sheet.AddText(
            key,
            inputValue,
            outputValue,
            Difference(inputValue, outputValue),
            OtherValues(result, table, row, column));

    /// <summary>
    /// How far the output moved from the input: signed, so a value that fell is told apart from one
    /// that rose. Values that are equal are zero apart whether they are numbers or not; values that
    /// differ without both being numbers have no distance between them, and the cell is left empty
    /// rather than filled with a nought that would read as agreement.
    /// </summary>
    private static string Difference(string inputValue, string outputValue)
    {
        if (TryReadNumber(inputValue, out decimal left) && TryReadNumber(outputValue, out decimal right))
            return Describe(right - left);

        return string.Equals(inputValue.Trim(), outputValue.Trim(), StringComparison.Ordinal) ? "0" : string.Empty;
    }

    private static bool TryReadNumber(string value, out decimal number) =>
        decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    /// <summary>
    /// The rest of the record, so a line can be read without going back to the file. The column the
    /// line is about is left out - it is already in front of the reader - and so are the key columns,
    /// which are the first cell of every line.
    /// </summary>
    private static string OtherValues(ComparisonResult result, DataTable table, DataRow row, string subject)
    {
        IEnumerable<string> columns = table.Columns
            .Where(c => !SameColumn(c, subject))
            .Where(c => !result.KeyColumns.Any(k => SameColumn(k, c)));

        // Names the file never had say nothing worth the room: "column1=EI; column2=0; column3=VPA" is
        // mostly punctuation where "EI;0;VPA" is the record. Only the values, then, for a file whose
        // columns were numbered rather than named.
        if (table.HasGeneratedColumnNames)
            return string.Join(GeneratedNameSeparator, columns.Select(c => table.GetValue(row, c)));

        // A delimited file gets its own separator back, so the cell reads the way the row reads in the
        // file it came from: with a |" file, 70986830|"8111|" rather than Test=70986830; Test1=8111.
        // A workbook, an XML document or a JSON array has no separator to borrow, so for those each
        // value keeps the name of the column it came from - otherwise the cell is a row of bare values
        // with nothing to say which is which.
        return table.Delimiter is { Length: > 0 } delimiter
            ? string.Concat(columns.Select(c => table.GetValue(row, c) + delimiter))
            : string.Join("; ", columns.Select(c => $"{c}={table.GetValue(row, c)}"));
    }

    /// <summary>Names matched the way <see cref="DataTable"/> matches them, so an accent written two ways is one column.</summary>
    private static bool SameColumn(string left, string right) =>
        string.Equals(TextKey.Canonical(left), TextKey.Canonical(right), StringComparison.OrdinalIgnoreCase);

    private static string SimilarHeading(ComparisonOptions options) =>
        $"Similar matches ({PlusMinus(options)}) in the output";

    private static string PlusMinus(ComparisonOptions options) => $"+/-{Describe(options.SimilarMatchRange)}";

    private static string Describe(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- the package

    /// <summary>
    /// Excel reserves the first two fills - none, then gray125 - and renumbers or repairs a workbook
    /// that does not have them, so the header's own fill has to be the third.
    /// </summary>
    private static XDocument Styles() =>
        new XDocument(new XElement(Main + "styleSheet",
            new XElement(Main + "fonts", new XAttribute("count", 2),
                new XElement(Main + "font",
                    new XElement(Main + "sz", new XAttribute("val", 11)),
                    new XElement(Main + "name", new XAttribute("val", "Calibri"))),
                new XElement(Main + "font",
                    new XElement(Main + "b"),
                    new XElement(Main + "sz", new XAttribute("val", 11)),
                    new XElement(Main + "color", new XAttribute("rgb", "FF14532D")),
                    new XElement(Main + "name", new XAttribute("val", "Calibri")))),
            new XElement(Main + "fills", new XAttribute("count", 3),
                new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "gray125"))),
                new XElement(Main + "fill",
                    new XElement(Main + "patternFill", new XAttribute("patternType", "solid"),
                        new XElement(Main + "fgColor", new XAttribute("rgb", "FFD7ECDD")),
                        new XElement(Main + "bgColor", new XAttribute("indexed", 64))))),
            new XElement(Main + "borders", new XAttribute("count", 2),
                new XElement(Main + "border",
                    new XElement(Main + "left"), new XElement(Main + "right"),
                    new XElement(Main + "top"), new XElement(Main + "bottom"),
                    new XElement(Main + "diagonal")),
                new XElement(Main + "border",
                    new XElement(Main + "left"), new XElement(Main + "right"), new XElement(Main + "top"),
                    new XElement(Main + "bottom", new XAttribute("style", "thin"),
                        new XElement(Main + "color", new XAttribute("rgb", "FF9CBBA6"))),
                    new XElement(Main + "diagonal"))),
            new XElement(Main + "cellStyleXfs", new XAttribute("count", 1),
                new XElement(Main + "xf",
                    new XAttribute("numFmtId", 0), new XAttribute("fontId", 0),
                    new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
            new XElement(Main + "cellXfs", new XAttribute("count", 2),
                new XElement(Main + "xf",
                    new XAttribute("numFmtId", 0), new XAttribute("fontId", 0),
                    new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0)),
                new XElement(Main + "xf",
                    new XAttribute("numFmtId", 0), new XAttribute("fontId", 1),
                    new XAttribute("fillId", 2), new XAttribute("borderId", 1), new XAttribute("xfId", 0),
                    new XAttribute("applyFont", 1), new XAttribute("applyFill", 1), new XAttribute("applyBorder", 1)))));

    private static XDocument ContentTypesPart(int sheetCount)
    {
        XElement types = new XElement(ContentTypes + "Types",
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));

        for (int i = 1; i <= sheetCount; i++)
            types.Add(new XElement(ContentTypes + "Override",
                new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));

        return new XDocument(types);
    }

    private static XDocument RootRelationships() =>
        new XDocument(new XElement(PackageRelationships + "Relationships",
            new XElement(PackageRelationships + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", $"{RelationshipType}/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml"))));

    private static XDocument Workbook(List<Sheet> sheets)
    {
        XElement list = new XElement(Main + "sheets");
        for (int i = 0; i < sheets.Count; i++)
            list.Add(new XElement(Main + "sheet",
                new XAttribute("name", sheets[i].Name),
                new XAttribute("sheetId", i + 1),
                new XAttribute(DocumentRelationships + "id", $"rId{i + 1}")));

        return new XDocument(new XElement(Main + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", DocumentRelationships),
            list));
    }

    private static XDocument WorkbookRelationships(int sheetCount)
    {
        XElement relationships = new XElement(PackageRelationships + "Relationships");
        for (int i = 1; i <= sheetCount; i++)
            relationships.Add(new XElement(PackageRelationships + "Relationship",
                new XAttribute("Id", $"rId{i}"),
                new XAttribute("Type", $"{RelationshipType}/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{i}.xml")));

        // After the sheets, so that its id cannot collide with one of theirs.
        relationships.Add(new XElement(PackageRelationships + "Relationship",
            new XAttribute("Id", $"rId{sheetCount + 1}"),
            new XAttribute("Type", $"{RelationshipType}/styles"),
            new XAttribute("Target", "styles.xml")));

        return new XDocument(relationships);
    }

    private static XDocument Worksheet(Sheet sheet)
    {
        XElement data = new XElement(Main + "sheetData");

        for (int r = 0; r < sheet.Rows.Count; r++)
        {
            XElement row = new XElement(Main + "row", new XAttribute("r", r + 1));

            for (int c = 0; c < sheet.Rows[r].Length; c++)
            {
                Cell cell = sheet.Rows[r][c];
                if (cell.IsEmpty)
                    continue;

                string reference = $"{ColumnName(c)}{r + 1}";
                object[] style = sheet.HeaderRows.Contains(r) ? [new XAttribute("s", HeaderStyle)] : [];

                row.Add(cell.Amount is { } number
                    ? new XElement(Main + "c", new XAttribute("r", reference), style,
                        new XElement(Main + "v", number.ToString(CultureInfo.InvariantCulture)))
                    : new XElement(Main + "c", new XAttribute("r", reference), style, new XAttribute("t", "inlineStr"),
                        new XElement(Main + "is",
                            new XElement(Main + "t",
                                new XAttribute(XNamespace.Xml + "space", "preserve"),
                                Clean(cell.Value ?? string.Empty)))));
            }

            data.Add(row);
        }

        XElement worksheet = new XElement(Main + "worksheet");

        // The widths have to come before the data; Excel rejects the part the other way round.
        if (ColumnWidths(sheet) is { } widths)
            worksheet.Add(widths);

        worksheet.Add(data);

        // The schema wants this after the data, and Excel rejects the part if it comes before.
        if (sheet.Filtered && sheet.Rows.Count > 0)
            worksheet.Add(new XElement(Main + "autoFilter",
                new XAttribute("ref", $"A1:{ColumnName(sheet.Rows[0].Length - 1)}{sheet.Rows.Count}")));

        return new XDocument(worksheet);
    }

    /// <summary>
    /// Each column made as wide as the longest thing in it. Excel writes no width at all by default and
    /// leaves every column the same size, which on this report means keys and values alike are cut off
    /// behind the next column until the reader widens all of them by hand.
    /// </summary>
    private static XElement? ColumnWidths(Sheet sheet)
    {
        int count = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(r => r.Length);
        if (count == 0)
            return null;

        int[] longest = new int[count];
        foreach (Cell[] row in sheet.Rows)
            for (int c = 0; c < row.Length; c++)
                longest[c] = Math.Max(longest[c], Measure(row[c]));

        XElement columns = new XElement(Main + "cols");
        for (int c = 0; c < count; c++)
            columns.Add(new XElement(Main + "col",
                new XAttribute("min", c + 1),
                new XAttribute("max", c + 1),
                new XAttribute("width", Math.Clamp(longest[c] + 2, MinColumnWidth, MaxColumnWidth)),
                new XAttribute("customWidth", 1)));

        return columns;
    }

    private static int Measure(Cell cell) =>
        cell.Amount is { } number
            ? number.ToString(CultureInfo.InvariantCulture).Length
            : cell.Value?.Length ?? 0;

    /// <summary>
    /// Control characters are not valid in XML, and a stray one out of a data file would leave a
    /// workbook that Excel refuses to open rather than one with an odd character in it.
    /// </summary>
    private static string Clean(string value) =>
        value.Any(IsForbidden) ? new string([.. value.Where(c => !IsForbidden(c))]) : value;

    private static bool IsForbidden(char c) => c < 0x20 && c is not ('\t' or '\n' or '\r');

    private static string ColumnName(int index)
    {
        string name = string.Empty;
        for (int i = index; i >= 0; i = i / 26 - 1)
            name = (char)('A' + i % 26) + name;

        return name;
    }

    private static void Write(ZipArchive archive, string entryName, XDocument document)
    {
        using Stream stream = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }

    // ---------------------------------------------------------------- rows being built

    #endregion

    #region Nested types

    private sealed class Sheet
    {
        public Sheet(string name) => Name = SafeName(name);

        public string Name { get; private set; }

        public void Rename(string name) => Name = SafeName(name);

        /// <summary>Lines of data, the headers not counted: what the tab reports.</summary>
        public int DataRowCount => Rows.Count - HeaderRows.Count;

        public List<Cell[]> Rows { get; } = [];

        public bool IsFull => Rows.Count >= SheetRowLimit;

        public void Add(params Cell[] cells) => Rows.Add(cells);

        public void Add(string label) => Rows.Add([Cell.Text(label)]);

        public void Add(string label, string value) => Rows.Add([Cell.Text(label), Cell.Text(value)]);

        public void AddText(params string[] values) => Rows.Add([.. values.Select(Cell.Text)]);

        /// <summary>A row to be set apart from the data under it: bold, filled, and ruled off below.</summary>
        public void AddHeader(params string[] values)
        {
            HeaderRows.Add(Rows.Count);
            AddText(values);
        }

        public HashSet<int> HeaderRows { get; } = [];

        /// <summary>
        /// Whether row 1 heads a table, and so gets the filter arrows. Only the value sheets do; the
        /// overview is a page of labels rather than a table, and filtering it would sort it to pieces.
        /// </summary>
        public bool Filtered { get; set; }

        public void Blank() => Rows.Add([]);

        /// <summary>
        /// Excel allows 31 characters and forbids : \ / ? * [ ], and silently repairs a workbook that
        /// breaks either rule - which reads to the person opening it as a corrupt file.
        /// </summary>
        private static string SafeName(string name)
        {
            string cleaned = new string([.. name.Where(c => c is not (':' or '\\' or '/' or '?' or '*' or '[' or ']'))]);
            return cleaned.Length <= 31 ? cleaned : cleaned[..31];
        }
    }

    // The members are Value and Amount rather than Text and Number, which the two factories below have
    // taken: a positional record generates a property per parameter, and the names would collide.
    private readonly record struct Cell(string? Value, decimal? Amount)
    {
        public static Cell Text(string value) => new Cell(value, null);

        public static Cell Number(decimal value) => new Cell(null, value);

        public bool IsEmpty => Amount is null && string.IsNullOrEmpty(Value);
    }

    #endregion
}
