using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using FileComparerWindows.Comparison;
using FileComparerWindows.Configuration;
using FileComparerWindows.Infrastructure;
using FileComparerWindows.Model;
using FileComparerWindows.Readers;
using FileComparerWindows.Reporting;

namespace FileComparerWindows.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int ExitMatch = 0;
    private const int ExitDifferences = 1;
    private const int ExitError = 2;

    private readonly IUserInteraction _interaction;
    private readonly RelayCommand _compareCommand;

    private string _settingsPath = ComparisonOptions.DefaultPath;

    private string _keyColumns = string.Empty;
    private string _compareColumns = string.Empty;
    private string _skipColumns = string.Empty;
    private bool _showNonMatchingRows = true;
    private string _maxRowsText = "0";
    private bool _ignoreCase;
    private bool _trimValues = true;
    private bool _similarMatch;
    private string _delimiter = string.Empty;
    private string _encoding = string.Empty;
    private string _sheetName = string.Empty;

    private bool _isBusy;
    private string _statusText = "Ready.";
    private string _errorMessage = string.Empty;

    private bool _hasResult;
    private VerdictKind _verdict = VerdictKind.None;
    private string _verdictHeadline = string.Empty;
    private string _verdictDetail = string.Empty;
    private string _comparisonSummary = string.Empty;
    private string _reportText = string.Empty;

    private int _inputRowCount;
    private int _outputRowCount;
    private int _matchedRowCount;
    private int _valueDifferenceRowCount;
    private int _missingRowCount;
    private int _extraRowCount;
    private int _nonMatchingRowCount;

    private IReadOnlyList<DifferenceRow> _differences = [];
    private IReadOnlyList<SingleSideRow> _missingRows = [];
    private IReadOnlyList<SingleSideRow> _extraRows = [];
    private IReadOnlyList<string> _warnings = [];

    public MainViewModel(IUserInteraction interaction)
    {
        _interaction = interaction;

        Input = new LoadedFile("Input");
        Output = new LoadedFile("Output");

        // A file is read as soon as its path settles, and again when a setting that changes how it is
        // read is altered - an encoding chosen after the file was opened has to take effect at once,
        // since seeing it take effect is the whole point of choosing it.
        Input.PropertyChanged += OnFilePropertyChanged;
        Output.PropertyChanged += OnFilePropertyChanged;
        PropertyChanged += OnReaderSettingChanged;

        _compareCommand = new RelayCommand(async () => await CompareAsync(), () => !IsBusy);

        BrowseInputCommand = new RelayCommand(() => Browse(Input));
        BrowseOutputCommand = new RelayCommand(() => Browse(Output));
        SwapFilesCommand = new RelayCommand(SwapFiles);
        PickKeyColumnsCommand = new RelayCommand(() => PickColumns("Key columns", "Rows are paired on these columns. They must exist in both files.", KeyColumns, v => KeyColumns = v));
        PickCompareColumnsCommand = new RelayCommand(() => PickColumns("Compare columns", "Columns compared once rows are paired. Leave empty to compare every column the two files share.", CompareColumns, v => CompareColumns = v));
        PickSkipColumnsCommand = new RelayCommand(() => PickColumns("Skip columns", "Differences in these columns are ignored.", SkipColumns, v => SkipColumns = v));
        ClearResultsCommand = new RelayCommand(ClearResults);
        LoadSettingsCommand = new RelayCommand(LoadSettings);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        SaveSettingsAsCommand = new RelayCommand(SaveSettingsAs);
        CopyReportCommand = new RelayCommand(CopyReport, () => ReportText.Length > 0);
        ExportReportCommand = new RelayCommand(ExportReport, () => ReportText.Length > 0);
        ExportDifferencesCommand = new RelayCommand(ExportDifferences, () => HasResult);
        ShowCommandLineHelpCommand = new RelayCommand(() => _interaction.ShowText("Command line", CommandLine.HelpText));
        ShowAboutCommand = new RelayCommand(ShowAbout);
    }

    public LoadedFile Input { get; }
    public LoadedFile Output { get; }

    public ICommand BrowseInputCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand SwapFilesCommand { get; }
    public ICommand PickKeyColumnsCommand { get; }
    public ICommand PickCompareColumnsCommand { get; }
    public ICommand PickSkipColumnsCommand { get; }
    public ICommand CompareCommand => _compareCommand;
    public ICommand ClearResultsCommand { get; }
    public ICommand LoadSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SaveSettingsAsCommand { get; }
    public RelayCommand CopyReportCommand { get; }
    public RelayCommand ExportReportCommand { get; }
    public RelayCommand ExportDifferencesCommand { get; }
    public ICommand ShowCommandLineHelpCommand { get; }
    public ICommand ShowAboutCommand { get; }

    /// <summary>
    /// The encodings offered, led by the detecting default. An empty entry at the top of a list reads
    /// as a fault rather than as a choice, so the default is spelt out and mapped back to the empty
    /// string the readers expect.
    /// </summary>
    public IReadOnlyList<string> EncodingChoices { get; } = [DetectLabel, .. TextFile.SupportedEncodings];

    public IReadOnlyList<string> DelimiterChoices { get; } = [DetectLabel, ";", ",", "\\t", "|"];

    private const string DetectLabel = "detect";

    public string SelectedEncoding
    {
        get => _encoding.Length == 0 ? DetectLabel : _encoding;
        set => Encoding = IsDetect(value) ? string.Empty : value;
    }

    public string SelectedDelimiter
    {
        get => _delimiter.Length == 0 ? DetectLabel : _delimiter;
        set => Delimiter = IsDetect(value) ? string.Empty : value;
    }

    private static bool IsDetect(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), DetectLabel, StringComparison.OrdinalIgnoreCase);

    /// <summary>The worksheets of whichever of the two files are workbooks.</summary>
    public IReadOnlyList<string> SheetChoices =>
        [.. Input.SheetNames.Concat(Output.SheetNames).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The exit code the process reports when the window closes, mirroring the console tool.</summary>
    public int ExitCode { get; private set; } = ExitMatch;

    public string KeyColumns
    {
        get => _keyColumns;
        set => SetProperty(ref _keyColumns, value);
    }

    public string CompareColumns
    {
        get => _compareColumns;
        set => SetProperty(ref _compareColumns, value);
    }

    public string SkipColumns
    {
        get => _skipColumns;
        set => SetProperty(ref _skipColumns, value);
    }

    public bool ShowNonMatchingRows
    {
        get => _showNonMatchingRows;
        set => SetProperty(ref _showNonMatchingRows, value);
    }

    /// <summary>Kept as text so a half-typed number does not turn into a binding error.</summary>
    public string MaxRowsText
    {
        get => _maxRowsText;
        set => SetProperty(ref _maxRowsText, value);
    }

    public bool IgnoreCase
    {
        get => _ignoreCase;
        set => SetProperty(ref _ignoreCase, value);
    }

    public bool TrimValues
    {
        get => _trimValues;
        set => SetProperty(ref _trimValues, value);
    }

    public bool SimilarMatch
    {
        get => _similarMatch;
        set => SetProperty(ref _similarMatch, value);
    }

    public string Delimiter
    {
        get => _delimiter;
        set
        {
            if (SetProperty(ref _delimiter, value))
                RaisePropertyChanged(nameof(SelectedDelimiter));
        }
    }

    public string Encoding
    {
        get => _encoding;
        set
        {
            if (SetProperty(ref _encoding, value))
                RaisePropertyChanged(nameof(SelectedEncoding));
        }
    }

    public string SheetName
    {
        get => _sheetName;
        set => SetProperty(ref _sheetName, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(IsIdle));
                _compareCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !_isBusy;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _errorMessage.Length > 0;

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    public VerdictKind Verdict
    {
        get => _verdict;
        private set => SetProperty(ref _verdict, value);
    }

    public string VerdictHeadline
    {
        get => _verdictHeadline;
        private set => SetProperty(ref _verdictHeadline, value);
    }

    public string VerdictDetail
    {
        get => _verdictDetail;
        private set => SetProperty(ref _verdictDetail, value);
    }

    /// <summary>The key, compared and skipped columns the run actually used.</summary>
    public string ComparisonSummary
    {
        get => _comparisonSummary;
        private set => SetProperty(ref _comparisonSummary, value);
    }

    public string ReportText
    {
        get => _reportText;
        private set
        {
            if (SetProperty(ref _reportText, value))
            {
                CopyReportCommand.RaiseCanExecuteChanged();
                ExportReportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int InputRowCount
    {
        get => _inputRowCount;
        private set => SetProperty(ref _inputRowCount, value);
    }

    public int OutputRowCount
    {
        get => _outputRowCount;
        private set => SetProperty(ref _outputRowCount, value);
    }

    public int MatchedRowCount
    {
        get => _matchedRowCount;
        private set => SetProperty(ref _matchedRowCount, value);
    }

    public int ValueDifferenceRowCount
    {
        get => _valueDifferenceRowCount;
        private set => SetProperty(ref _valueDifferenceRowCount, value);
    }

    public int MissingRowCount
    {
        get => _missingRowCount;
        private set => SetProperty(ref _missingRowCount, value);
    }

    public int ExtraRowCount
    {
        get => _extraRowCount;
        private set => SetProperty(ref _extraRowCount, value);
    }

    public int NonMatchingRowCount
    {
        get => _nonMatchingRowCount;
        private set => SetProperty(ref _nonMatchingRowCount, value);
    }

    public IReadOnlyList<DifferenceRow> Differences
    {
        get => _differences;
        private set => SetProperty(ref _differences, value);
    }

    public IReadOnlyList<SingleSideRow> MissingRows
    {
        get => _missingRows;
        private set => SetProperty(ref _missingRows, value);
    }

    public IReadOnlyList<SingleSideRow> ExtraRows
    {
        get => _extraRows;
        private set => SetProperty(ref _extraRows, value);
    }

    public IReadOnlyList<string> Warnings
    {
        get => _warnings;
        private set
        {
            if (SetProperty(ref _warnings, value))
                RaisePropertyChanged(nameof(WarningCount));
        }
    }

    public int WarningCount => _warnings.Count;

    // ---------------------------------------------------------------- reading the files

    private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoadedFile.Path) && sender is LoadedFile file)
            _ = file.RefreshLaterAsync(BuildOptions());

        if (e.PropertyName == nameof(LoadedFile.SheetNames))
            RaisePropertyChanged(nameof(SheetChoices));
    }

    private void OnReaderSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Encoding) or nameof(Delimiter) or nameof(SheetName)))
            return;

        ComparisonOptions options = BuildOptions();
        _ = Input.RefreshLaterAsync(options, delayMilliseconds: 50);
        _ = Output.RefreshLaterAsync(options, delayMilliseconds: 50);
    }

    // ---------------------------------------------------------------- start-up

    /// <summary>Fills the window from the settings file and whatever the command line added on top.</summary>
    public void Initialise(string[] args)
    {
        try
        {
            _settingsPath = CommandLine.GetConfigPath(args, ComparisonOptions.DefaultPath);
            ComparisonOptions options = ComparisonOptions.LoadFromFile(_settingsPath);
            CommandLine.ApplyTo(options, args);
            ApplyOptions(options);

            StatusText = File.Exists(_settingsPath)
                ? $"Settings loaded from {_settingsPath}"
                : "Ready. No settings file yet - Compare, then File > Save settings.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            ExitCode = ExitError;
        }
    }

    /// <summary>Reads both files so the format line and the column pickers are populated.</summary>
    public async Task RefreshFilesAsync()
    {
        ComparisonOptions options = BuildOptions();
        await Input.RefreshAsync(options);
        await Output.RefreshAsync(options);
    }

    public async Task RunAtStartupAsync()
    {
        if (Input.Path.Length > 0 && Output.Path.Length > 0 && KeyColumns.Trim().Length > 0)
            await CompareAsync();
    }

    // ---------------------------------------------------------------- comparing

    private async Task CompareAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            ComparisonOptions options = BuildOptions();

            if (options.InputFilePath.Length == 0 || options.OutputFilePath.Length == 0)
                throw new InvalidOperationException("Choose both an input file and an output file.");

            StatusText = "Reading files…";
            await Input.RefreshAsync(options);
            await Output.RefreshAsync(options);

            DataTable input = Input.Table ?? throw new InvalidDataException($"Input file could not be read. {Input.Description}");
            DataTable output = Output.Table ?? throw new InvalidDataException($"Output file could not be read. {Output.Description}");

            StatusText = "Comparing…";
            ComparisonResult result = await Task.Run(() => new FileComparisonEngine(options).Compare(input, output));
            (IReadOnlyList<DifferenceRow> differences, IReadOnlyList<SingleSideRow> missing, IReadOnlyList<SingleSideRow> extra, string report) =
                await Task.Run(() => Project(result, options));

            Differences = differences;
            MissingRows = missing;
            ExtraRows = extra;
            Warnings = TextReport.CollectWarnings(result);
            ReportText = report;

            InputRowCount = result.InputRowCount;
            OutputRowCount = result.OutputRowCount;
            MatchedRowCount = result.MatchedRowCount;
            ValueDifferenceRowCount = result.ValueMismatches.Count;
            MissingRowCount = result.MissingInOutput.Count;
            ExtraRowCount = result.ExtraInOutput.Count;
            NonMatchingRowCount = result.NonMatchingRowCount;

            ComparisonSummary = BuildComparisonSummary(result);
            Verdict = result.IsMatch ? VerdictKind.Success : VerdictKind.Failure;
            VerdictHeadline = result.IsMatch ? "SUCCESS" : "FAILED";
            VerdictDetail = result.IsMatch
                ? $"All {result.MatchedRowCount:N0} row(s) match."
                : $"{result.NonMatchingRowCount:N0} non-matching row(s).";

            HasResult = true;
            ExitCode = result.IsMatch ? ExitMatch : ExitDifferences;
            StatusText = $"Compared at {DateTime.Now:HH:mm:ss}.";
            ExportDifferencesCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            ClearResults();
            ErrorMessage = exception.Message;
            Verdict = VerdictKind.Error;
            VerdictHeadline = "ERROR";
            VerdictDetail = exception.Message;
            ExitCode = ExitError;
            StatusText = "The comparison could not be run.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Flattens the result into the rows the grids show. One line per differing column rather than per
    /// row, so the grid can be sorted by column name to see whether one field is behind every failure.
    /// </summary>
    private static (IReadOnlyList<DifferenceRow>, IReadOnlyList<SingleSideRow>, IReadOnlyList<SingleSideRow>, string) Project(
        ComparisonResult result, ComparisonOptions options)
    {
        int max = options.ShowNonMatchingRows ? options.MaxNonMatchingRowsToShow : 0;

        List<DifferenceRow> differences = [];
        foreach (RowMismatch mismatch in Limit(result.ValueMismatches, max))
        {
            string inputRowText = mismatch.InputRow.ToDisplayString();
            string outputRowText = mismatch.OutputRow.ToDisplayString();

            foreach (ValueDifference difference in mismatch.Differences)
                differences.Add(new DifferenceRow(
                    mismatch.DisplayKey,
                    difference.Column,
                    difference.InputValue,
                    difference.OutputValue,
                    mismatch.InputRow.LineNumber,
                    mismatch.OutputRow.LineNumber,
                    inputRowText,
                    outputRowText));
        }

        List<SingleSideRow> missing = [.. Limit(result.MissingInOutput, max)
            .Select(r => new SingleSideRow(r.DisplayKey, r.Row.LineNumber, r.Row.ToDisplayString()))];

        List<SingleSideRow> extra = [.. Limit(result.ExtraInOutput, max)
            .Select(r => new SingleSideRow(r.DisplayKey, r.Row.LineNumber, r.Row.ToDisplayString()))];

        return (differences, missing, extra, TextReport.Build(result, options));
    }

    private static IEnumerable<T> Limit<T>(IReadOnlyList<T> items, int maxRows) =>
        maxRows > 0 ? items.Take(maxRows) : items;

    private static string BuildComparisonSummary(ComparisonResult result)
    {
        string summary = $"Keys: {string.Join(", ", result.KeyColumns)}   ·   Compared: {string.Join(", ", result.ComparedColumns)}";
        if (result.SkippedColumns.Count > 0)
            summary += $"   ·   Skipped: {string.Join(", ", result.SkippedColumns)}";

        return summary;
    }

    private void ClearResults()
    {
        Differences = [];
        MissingRows = [];
        ExtraRows = [];
        Warnings = [];
        ReportText = string.Empty;
        ComparisonSummary = string.Empty;
        InputRowCount = OutputRowCount = MatchedRowCount = 0;
        ValueDifferenceRowCount = MissingRowCount = ExtraRowCount = NonMatchingRowCount = 0;
        HasResult = false;
        Verdict = VerdictKind.None;
        VerdictHeadline = string.Empty;
        VerdictDetail = string.Empty;
        ErrorMessage = string.Empty;
        ExportDifferencesCommand.RaiseCanExecuteChanged();
    }

    // ---------------------------------------------------------------- options

    public ComparisonOptions BuildOptions() => new ComparisonOptions
    {
        InputFilePath = Input.Path,
        OutputFilePath = Output.Path,
        KeyColumns = SplitList(KeyColumns),
        CompareColumns = SplitList(CompareColumns),
        SkipColumns = SplitList(SkipColumns),
        ShowNonMatchingRows = ShowNonMatchingRows,
        MaxNonMatchingRowsToShow = ParseMaxRows(MaxRowsText),
        IgnoreCase = IgnoreCase,
        TrimValues = TrimValues,
        SimilarMatch = SimilarMatch,
        Delimiter = Delimiter,
        SheetName = SheetName,
        Encoding = Encoding
    };

    private void ApplyOptions(ComparisonOptions options)
    {
        Input.Path = options.InputFilePath;
        Output.Path = options.OutputFilePath;
        KeyColumns = string.Join(", ", options.KeyColumns);
        CompareColumns = string.Join(", ", options.CompareColumns);
        SkipColumns = string.Join(", ", options.SkipColumns);
        ShowNonMatchingRows = options.ShowNonMatchingRows;
        MaxRowsText = options.MaxNonMatchingRowsToShow.ToString(CultureInfo.InvariantCulture);
        IgnoreCase = options.IgnoreCase;
        TrimValues = options.TrimValues;
        SimilarMatch = options.SimilarMatch;
        Delimiter = options.Delimiter;
        SheetName = options.SheetName;
        Encoding = options.Encoding;
    }

    private static List<string> SplitList(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static int ParseMaxRows(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0 ? parsed : 0;

    // ---------------------------------------------------------------- commands

    private void Browse(LoadedFile file)
    {
        string? chosen = _interaction.BrowseForOpen($"Choose the {file.Label.ToLowerInvariant()} file", TableReaderFactory.FileDialogFilter, file.Path);
        if (chosen is not null)
            file.Path = chosen;
    }

    private void SwapFiles()
    {
        (Input.Path, Output.Path) = (Output.Path, Input.Path);
        StatusText = "Input and output swapped.";
    }

    /// <summary>
    /// Offers the columns of both files, ticked where they are already named. A column present in only
    /// one file is still offered but says so, because that is exactly the case a user needs to see.
    /// </summary>
    private void PickColumns(string title, string prompt, string current, Action<string> assign)
    {
        List<ColumnChoice> choices = BuildColumnChoices(current);
        if (choices.Count == 0)
        {
            _interaction.ShowMessage(title, "Choose the files first - the columns are read from their headers.");
            return;
        }

        IReadOnlyList<string>? picked = _interaction.PickColumns(title, prompt, choices);
        if (picked is not null)
            assign(string.Join(", ", picked));
    }

    private List<ColumnChoice> BuildColumnChoices(string current)
    {
        HashSet<string> selected = new HashSet<string>(SplitList(current).Select(TextKey.Canonical), StringComparer.OrdinalIgnoreCase);
        List<ColumnChoice> choices = [];
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in Input.Columns.Concat(Output.Columns))
        {
            if (!seen.Add(TextKey.Canonical(name)))
                continue;

            bool inInput = Input.Table?.HasColumn(name) ?? false;
            bool inOutput = Output.Table?.HasColumn(name) ?? false;

            choices.Add(new ColumnChoice
            {
                Name = name,
                Availability = inInput && inOutput ? "both files" : inInput ? "input only" : "output only",
                IsSelected = selected.Contains(TextKey.Canonical(name))
            });
        }

        // A name typed by hand that matches no header should not vanish when the picker is confirmed.
        foreach (string name in SplitList(current).Where(n => seen.Add(TextKey.Canonical(n))))
            choices.Add(new ColumnChoice { Name = name, Availability = "not in either file", IsSelected = true });

        return choices;
    }

    private void LoadSettings()
    {
        string? path = _interaction.BrowseForOpen("Load settings", "Settings (*.json)|*.json|All files (*.*)|*.*", _settingsPath);
        if (path is null)
            return;

        try
        {
            ApplyOptions(ComparisonOptions.LoadFromFile(path));
            _settingsPath = path;
            ClearResults();
            StatusText = $"Settings loaded from {path}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not read '{path}': {exception.Message}";
        }
    }

    private void SaveSettings() => WriteSettings(_settingsPath);

    private void SaveSettingsAs()
    {
        string? path = _interaction.BrowseForSave("Save settings", "Settings (*.json)|*.json|All files (*.*)|*.*", "appsettings.json", _settingsPath);
        if (path is not null)
            WriteSettings(path);
    }

    private void WriteSettings(string path)
    {
        try
        {
            BuildOptions().SaveToFile(path);
            _settingsPath = path;
            StatusText = $"Settings saved to {path}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not save '{path}': {exception.Message}";
        }
    }

    private void CopyReport()
    {
        _interaction.CopyToClipboard(ReportText);
        StatusText = "Report copied to the clipboard.";
    }

    private void ExportReport()
    {
        string? path = _interaction.BrowseForSave("Export report", "Text file (*.txt)|*.txt|All files (*.*)|*.*", SuggestExportName("txt"), null);
        if (path is null)
            return;

        try
        {
            File.WriteAllText(path, ReportText, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText = $"Report written to {path}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not write '{path}': {exception.Message}";
        }
    }

    /// <summary>
    /// The differences as a spreadsheet-friendly file. A byte order mark is written deliberately: it is
    /// what tells Excel the file is UTF-8, and without it the accented column names this tool goes to
    /// such lengths to read correctly are mangled again on the way out.
    /// </summary>
    private void ExportDifferences()
    {
        string? path = _interaction.BrowseForSave("Export differences", "CSV file (*.csv)|*.csv|All files (*.*)|*.*", SuggestExportName("csv"), null);
        if (path is null)
            return;

        try
        {
            System.Text.StringBuilder csv = new System.Text.StringBuilder();
            csv.AppendLine("Category;Key;Column;InputValue;OutputValue;InputLine;OutputLine");

            foreach (DifferenceRow row in Differences)
                csv.AppendLine(string.Join(';', Quote("Value difference"), Quote(row.Key), Quote(row.Column),
                    Quote(row.InputValue), Quote(row.OutputValue), row.InputLine, row.OutputLine));

            foreach (SingleSideRow row in MissingRows)
                csv.AppendLine(string.Join(';', Quote("Missing in output"), Quote(row.Key), Quote(string.Empty),
                    Quote(row.RowText), Quote(string.Empty), row.LineNumber, string.Empty));

            foreach (SingleSideRow row in ExtraRows)
                csv.AppendLine(string.Join(';', Quote("Extra in output"), Quote(row.Key), Quote(string.Empty),
                    Quote(string.Empty), Quote(row.RowText), string.Empty, row.LineNumber));

            File.WriteAllText(path, csv.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText = $"Differences written to {path}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not write '{path}': {exception.Message}";
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private string SuggestExportName(string extension)
    {
        string name = Input.Path.Length > 0 ? Path.GetFileNameWithoutExtension(Input.Path) : "comparison";
        return $"{name}-comparison-{DateTime.Now:yyyyMMdd-HHmm}.{extension}";
    }

    private void ShowAbout() => _interaction.ShowText("About FileComparerWindows",
        """
        FileComparerWindows

        Compares an input file and an output file row by row, using one or more columns as the key.
        Row order does not matter - rows are paired by their key values and then every other shared
        column is compared.

        Formats
          Delimited text  .csv .txt .tsv .psv .dat   delimiter detected from the header, RFC 4180 quoting
          XML             .xml                       one element per record, attributes and leaf children as columns
          JSON            .json                      an array of objects, or the first array property of an object
          Excel           .xlsx .xlsm                read straight from the Open XML package, first row is the header

        The two files need not share a format or an encoding, and columns are matched by name rather
        than position.

        Encoding is detected from a byte order mark, else UTF-8, else Windows-1252, and text is
        normalised (NFC) before anything is matched.

        Dates in .xlsx files are read as their underlying serial number, since cell number formats are
        not interpreted. Two spreadsheets still compare correctly against each other; a spreadsheet
        compared against a text file needs the dates written as text.
        """);
}
