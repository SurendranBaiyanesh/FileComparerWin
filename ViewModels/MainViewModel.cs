using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
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
	#region Constants
	private const int ExitMatch = 0;
	private const int ExitDifferences = 1;
	private const int ExitError = 2;

	/// <summary>
	/// Stands in for the empty string in the encoding and delimiter lists. An empty entry at the top of
	/// a list reads as a fault rather than as a choice, so the default is spelt out and mapped back.
	/// </summary>
	private const string DetectLabel = "detect";
	#endregion

	#region Fields
	private readonly IUserInteraction _interaction;
	private readonly RelayCommand _compareCommand;

	private string _settingsPath = ComparisonOptions.DefaultPath;

	private string _keyColumns = string.Empty;
	private string _compareColumns = string.Empty;
	private bool _ignoreCase;
	private bool _trimValues = true;
	private bool _similarMatch;
	private string _similarMatchRangeText = "0";
	private string _delimiter = string.Empty;
	private string _splitIndexes = string.Empty;
	private bool _noHeaderRow;
	private string _encoding = string.Empty;

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

	private ColumnMismatch? _columnMismatch;

	// The last run, kept whole for the Excel report; the grids hold only a trimmed projection of it.
	private ComparisonResult? _result;
	private ComparisonOptions? _resultOptions;

	private ICollectionView? _differencesView;
	private ICollectionView? _missingRowsView;
	private ICollectionView? _extraRowsView;
	private string _differencesFilterSummary = string.Empty;
	private string _missingRowsFilterSummary = string.Empty;
	private string _extraRowsFilterSummary = string.Empty;
	#endregion

	#region Constructor
	public MainViewModel(IUserInteraction interaction)
	{
		_interaction = interaction;

		this.Input = new LoadedFile("Input");
		this.Output = new LoadedFile("Output");

		// A file is read as soon as its path settles, and again when a setting that changes how it is
		// read is altered - an encoding chosen after the file was opened has to take effect at once,
		// since seeing it take effect is the whole point of choosing it.
		this.Input.PropertyChanged += OnFilePropertyChanged;
		this.Output.PropertyChanged += OnFilePropertyChanged;
		this.PropertyChanged += OnReaderSettingChanged;

		this.DifferenceFilters = new DifferenceFilters(OnDifferenceFiltersChanged);
		this.MissingFilters = new SingleSideFilters(OnMissingFiltersChanged);
		this.ExtraFilters = new SingleSideFilters(OnExtraFiltersChanged);

		this.ClearDifferenceFiltersCommand = new RelayCommand(this.DifferenceFilters.Clear);
		this.ClearMissingFiltersCommand = new RelayCommand(this.MissingFilters.Clear);
		this.ClearExtraFiltersCommand = new RelayCommand(this.ExtraFilters.Clear);

		_compareCommand = new RelayCommand(async () => await CompareAsync(), () => !this.IsBusy);

		this.BrowseInputCommand = new RelayCommand(() => Browse(this.Input));
		this.BrowseOutputCommand = new RelayCommand(() => Browse(this.Output));
		this.SwapFilesCommand = new RelayCommand(SwapFiles);
		this.ConvertCommand = new RelayCommand(async () => await ConvertAsync(), () => this.IsDynamicDelimiter && this.SplitIndexes.Trim().Length > 0 && !this.IsBusy);
		this.PickSplitIndexesCommand = new RelayCommand(PickSplitIndexes, () => this.IsDynamicDelimiter);
		this.PickKeyColumnsCommand = new RelayCommand(() => PickColumns("Key columns", "Rows are paired on these columns. They must exist in both files. Drag a column by its grip to reorder them - the one at the top is the first key.", this.KeyColumns, v => this.KeyColumns = v));
		this.PickCompareColumnsCommand = new RelayCommand(() => PickColumns("Compare columns", "Columns compared once rows are paired. Leave empty to compare every column the two files share.", this.CompareColumns, v => this.CompareColumns = v));
		this.ClearResultsCommand = new RelayCommand(ClearResults);
		this.LoadSettingsCommand = new RelayCommand(LoadSettings);
		this.SaveSettingsCommand = new RelayCommand(SaveSettings);
		this.SaveSettingsAsCommand = new RelayCommand(SaveSettingsAs);
		this.CopyReportCommand = new RelayCommand(CopyReport, () => this.ReportText.Length > 0);
		this.ExportReportCommand = new RelayCommand(ExportReport, () => this.ReportText.Length > 0);
		this.ExportDifferencesCommand = new RelayCommand(ExportDifferences, () => this.HasResult);
		this.ExportExcelReportCommand = new RelayCommand(ExportExcelReport, () => this.HasResult);
		this.ShowCommandLineHelpCommand = new RelayCommand(() => _interaction.ShowText("Command line", CommandLine.HelpText));
		this.ShowAboutCommand = new RelayCommand(ShowAbout);
	}
	#endregion

	#region Properties
	public LoadedFile Input { get; }
	public LoadedFile Output { get; }

	public ICommand BrowseInputCommand { get; }
	public ICommand BrowseOutputCommand { get; }
	public ICommand SwapFilesCommand { get; }
	public RelayCommand ConvertCommand { get; }
	public RelayCommand PickSplitIndexesCommand { get; }
	public ICommand PickKeyColumnsCommand { get; }
	public ICommand PickCompareColumnsCommand { get; }
	public ICommand CompareCommand => _compareCommand;
	public ICommand ClearResultsCommand { get; }
	public ICommand LoadSettingsCommand { get; }
	public ICommand SaveSettingsCommand { get; }
	public ICommand SaveSettingsAsCommand { get; }
	public RelayCommand CopyReportCommand { get; }
	public RelayCommand ExportReportCommand { get; }
	public RelayCommand ExportDifferencesCommand { get; }
	public RelayCommand ExportExcelReportCommand { get; }
	public ICommand ShowCommandLineHelpCommand { get; }
	public ICommand ShowAboutCommand { get; }

	public ICommand ClearDifferenceFiltersCommand { get; }
	public ICommand ClearMissingFiltersCommand { get; }
	public ICommand ClearExtraFiltersCommand { get; }

	public DifferenceFilters DifferenceFilters { get; }
	public SingleSideFilters MissingFilters { get; }
	public SingleSideFilters ExtraFilters { get; }

	/// <summary>
	/// The encodings offered, led by the detecting default. An empty entry at the top of a list reads
	/// as a fault rather than as a choice, so the default is spelt out and mapped back to the empty
	/// string the readers expect.
	/// </summary>
	public IReadOnlyList<string> EncodingChoices { get; } = [DetectLabel, .. TextFile.SupportedEncodings];

	/// <summary>
	/// The box is editable, so any separator can be typed; these are the ones worth not typing. |" is
	/// offered but never detected - in a header line it cannot be told apart from a plain pipe.
	/// </summary>
	public IReadOnlyList<string> DelimiterChoices { get; } =
		[DetectLabel, ";", ",", "\\t", "|", "|\"", ComparisonOptions.DynamicDelimiter];

	public string SelectedEncoding
	{
		get => _encoding.Length == 0 ? DetectLabel : _encoding;
		set => this.Encoding = IsDetect(value) ? string.Empty : value;
	}

	public string SelectedDelimiter
	{
		get => _delimiter.Length == 0 ? DetectLabel : _delimiter;
		set => this.Delimiter = IsDetect(value) ? string.Empty : value;
	}

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

	/// <summary>
	/// How far apart two numbers may be and still count as equal. Kept as text like <see cref="MaxRowsText"/>,
	/// so that a half-typed number is a half-typed number rather than a binding error.
	/// </summary>
	public string SimilarMatchRangeText
	{
		get => _similarMatchRangeText;
		set => SetProperty(ref _similarMatchRangeText, value);
	}

	public string Delimiter
	{
		get => _delimiter;
		set
		{
			if(SetProperty(ref _delimiter, value))
			{
				RaisePropertyChanged(nameof(this.SelectedDelimiter));
				RaisePropertyChanged(nameof(this.IsDynamicDelimiter));
				this.ConvertCommand.RaiseCanExecuteChanged();
				this.PickSplitIndexesCommand.RaiseCanExecuteChanged();
			}
		}
	}

	/// <summary>
	/// Positions to cut fixed-width lines at, e.g. "1;2;5;13". Filling this in takes both files away
	/// from the delimiter altogether: the columns come from the positions and are named column1,
	/// column2 and so on, and every line is a record rather than the first being a header.
	/// </summary>
	public string SplitIndexes
	{
		get => _splitIndexes;
		set
		{
			if(SetProperty(ref _splitIndexes, value)) this.ConvertCommand.RaiseCanExecuteChanged();
		}
	}

	/// <summary>
	/// True while the delimiter is Dynamic, which is the one setting the split positions mean anything
	/// under. The Split at box is disabled otherwise, so that positions cannot be filled in where they
	/// would quietly do nothing.
	/// </summary>
	public bool IsDynamicDelimiter => string.Equals(_delimiter.Trim(), ComparisonOptions.DynamicDelimiter, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Treats the first line or row of both files as a record rather than as column names, naming the
	/// columns column1, column2 and so on. A file cut by position is read this way whatever this says.
	/// </summary>
	public bool NoHeaderRow
	{
		get => _noHeaderRow;
		set => SetProperty(ref _noHeaderRow, value);
	}

	public string Encoding
	{
		get => _encoding;
		set
		{
			if(SetProperty(ref _encoding, value)) RaisePropertyChanged(nameof(this.SelectedEncoding));
		}
	}

	public bool IsBusy
	{
		get => _isBusy;
		private set
		{
			if(SetProperty(ref _isBusy, value))
			{
				RaisePropertyChanged(nameof(this.IsIdle));
				_compareCommand.RaiseCanExecuteChanged();
				this.ConvertCommand.RaiseCanExecuteChanged();
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
			if(SetProperty(ref _errorMessage, value)) RaisePropertyChanged(nameof(this.HasError));
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

	/// <summary>The key and compared columns the run actually used.</summary>
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
			if(SetProperty(ref _reportText, value))
			{
				this.CopyReportCommand.RaiseCanExecuteChanged();
				this.ExportReportCommand.RaiseCanExecuteChanged();
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

	// The grids bind to the views rather than to the lists, so that the column filters can hide rows
	// without the counts above them - which report the comparison, not the view of it - moving.

	public ICollectionView? DifferencesView
	{
		get => _differencesView;
		private set => SetProperty(ref _differencesView, value);
	}

	public ICollectionView? MissingRowsView
	{
		get => _missingRowsView;
		private set => SetProperty(ref _missingRowsView, value);
	}

	public ICollectionView? ExtraRowsView
	{
		get => _extraRowsView;
		private set => SetProperty(ref _extraRowsView, value);
	}

	public string DifferencesFilterSummary
	{
		get => _differencesFilterSummary;
		private set => SetProperty(ref _differencesFilterSummary, value);
	}

	public string MissingRowsFilterSummary
	{
		get => _missingRowsFilterSummary;
		private set => SetProperty(ref _missingRowsFilterSummary, value);
	}

	public string ExtraRowsFilterSummary
	{
		get => _extraRowsFilterSummary;
		private set => SetProperty(ref _extraRowsFilterSummary, value);
	}

	public IReadOnlyList<string> Warnings
	{
		get => _warnings;
		private set
		{
			if(SetProperty(ref _warnings, value)) RaisePropertyChanged(nameof(this.WarningCount));
		}
	}

	public int WarningCount => _warnings.Count;

	/// <summary>
	/// Set while the two files carry different columns, which the comparison refuses to run on. Said as
	/// soon as both files have been read rather than only when Compare is pressed: a column missing from
	/// an export is a fault in the file, and the sooner it is seen the less there is to undo.
	/// </summary>
	public bool HasColumnMismatch => _columnMismatch is not null;

	/// <summary>The columns that are on one side only, short enough for a line under the file boxes.</summary>
	public string ColumnMismatchHeadline => _columnMismatch?.Headline ?? string.Empty;

	/// <summary>The same thing with both headers spelt out, as the tooltip and as the error Compare stops with.</summary>
	public string ColumnMismatchDetail => _columnMismatch?.Detail ?? string.Empty;
	#endregion

	#region Public methods
	/// <summary>Fills the window from the settings file and whatever the command line added on top.</summary>
	public void Initialise(string[] args)
	{
		try
		{
			_settingsPath = CommandLine.GetConfigPath(args, ComparisonOptions.DefaultPath);
			ComparisonOptions options = ComparisonOptions.LoadFromFile(_settingsPath);
			CommandLine.ApplyTo(options, args);
			ApplyOptions(options);

			this.StatusText = File.Exists(_settingsPath) ? $"Settings loaded from {_settingsPath}" : "Ready. No settings file yet - Compare, then File > Save settings.";
		}
		catch(Exception exception)
		{
			this.ErrorMessage = exception.Message;
			this.ExitCode = ExitError;
		}
	}

	/// <summary>Reads both files so the format line and the column pickers are populated.</summary>
	public async Task RefreshFilesAsync()
	{
		ComparisonOptions options = BuildOptions();
		await this.Input.RefreshAsync(options);
		await this.Output.RefreshAsync(options);
	}

	public async Task RunAtStartupAsync()
	{
		if(this.Input.Path.Length > 0 && this.Output.Path.Length > 0 && this.KeyColumns.Trim().Length > 0) await CompareAsync();
	}

	/// <summary>
	/// Cuts both files at the split positions there and then, rather than waiting for Compare, and
	/// puts the columns that came out into the Compare box. Until a file has been read nobody - the
	/// window included - knows what its columns are called, so this is what makes column1, column2 and
	/// the rest available to the Key and Compare boxes and their pickers.
	///
	/// The Key box is left alone: which column pairs the rows is a decision about the data that the
	/// application is in no position to make, and filling it with every column would pair rows on the
	/// whole record and report every difference as a row missing on one side.
	/// </summary>
	public async Task ConvertAsync()
	{
		if(this.IsBusy) return;

		this.IsBusy = true;
		this.ErrorMessage = string.Empty;

		try
		{
			this.StatusText = "Splitting…";
			await RefreshFilesAsync();

			if(this.Input.HasError || this.Output.HasError)
			{
				this.ErrorMessage = this.Input.HasError ? this.Input.Description : this.Output.Description;
				this.StatusText = "Could not split the files.";
				return;
			}

			IReadOnlyList<string> columns = this.Input.Columns.Count > 0 ? this.Input.Columns : this.Output.Columns;
			if(columns.Count == 0)
			{
				this.StatusText = "Nothing to split - choose the files first.";
				return;
			}

			this.CompareColumns = string.Join(", ", columns);
			this.StatusText = $"Split into {columns.Count} column(s): {columns[0]} to {columns[^1]}. Name a key column, then Compare.";
		}
		catch(Exception exception)
		{
			this.ErrorMessage = exception.Message;
			this.StatusText = "Could not split the files.";
		}
		finally
		{
			this.IsBusy = false;
		}
	}

	/// <summary>The window's settings as the readers and the engine expect them.</summary>
	public ComparisonOptions BuildOptions()
	{
		return new ComparisonOptions
		       {
			       InputFilePath = this.Input.Path,
			       OutputFilePath = this.Output.Path,
			       KeyColumns = SplitList(this.KeyColumns),
			       CompareColumns = SplitList(this.CompareColumns),
			       IgnoreCase = this.IgnoreCase,
			       TrimValues = this.TrimValues,
			       SimilarMatch = this.SimilarMatch,
			       SimilarMatchRange = ParseRange(this.SimilarMatchRangeText),
			       Delimiter = this.Delimiter,
			       SplitIndexes = this.SplitIndexes,
			       NoHeaderRow = this.NoHeaderRow,
			       Encoding = this.Encoding
		       };
	}
	#endregion

	#region Private methods
	private static bool IsDetect(string? value)
	{
		return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), DetectLabel, StringComparison.OrdinalIgnoreCase);
	}

	// ---------------------------------------------------------------- reading the files

	private void OnFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName == nameof(LoadedFile.Path) && sender is LoadedFile file) _ = file.RefreshLaterAsync(BuildOptions());

		if(e.PropertyName == nameof(LoadedFile.Table)) RefreshColumnMismatch();
	}

	/// <summary>Compares the two headers, once there are two of them to compare.</summary>
	private void RefreshColumnMismatch()
	{
		_columnMismatch = this.Input.Table is { } input && this.Output.Table is { } output ? FileComparisonEngine.FindColumnMismatch(input, output) : null;

		RaisePropertyChanged(nameof(this.HasColumnMismatch));
		RaisePropertyChanged(nameof(this.ColumnMismatchHeadline));
		RaisePropertyChanged(nameof(this.ColumnMismatchDetail));
	}

	private void OnReaderSettingChanged(object? sender, PropertyChangedEventArgs e)
	{
		if(e.PropertyName is not (nameof(this.Encoding) or nameof(this.Delimiter) or nameof(this.SplitIndexes) or nameof(this.NoHeaderRow))) return;

		ComparisonOptions options = BuildOptions();
		_ = this.Input.RefreshLaterAsync(options, 50);
		_ = this.Output.RefreshLaterAsync(options, 50);
	}

	// ---------------------------------------------------------------- comparing

	private async Task CompareAsync()
	{
		if(this.IsBusy) return;

		this.IsBusy = true;
		this.ErrorMessage = string.Empty;

		try
		{
			ComparisonOptions options = BuildOptions();

			if(options.InputFilePath.Length == 0 || options.OutputFilePath.Length == 0) throw new InvalidOperationException("Choose both an input file and an output file.");

			this.StatusText = "Reading files…";
			await this.Input.RefreshAsync(options);
			await this.Output.RefreshAsync(options);

			DataTable input = this.Input.Table ?? throw new InvalidDataException($"Input file could not be read. {this.Input.Description}");
			DataTable output = this.Output.Table ?? throw new InvalidDataException($"Output file could not be read. {this.Output.Description}");

			this.StatusText = "Comparing…";
			ComparisonResult result = await Task.Run(() => new FileComparisonEngine(options).Compare(input, output));
			_result = result;
			_resultOptions = options;
			(IReadOnlyList<DifferenceRow> differences, IReadOnlyList<SingleSideRow> missing, IReadOnlyList<SingleSideRow> extra, string report) = await Task.Run(() => Project(result, options));

			SetRows(differences, missing, extra);
			this.Warnings = TextReport.CollectWarnings(result);
			this.ReportText = report;

			this.InputRowCount = result.InputRowCount;
			this.OutputRowCount = result.OutputRowCount;
			this.MatchedRowCount = result.MatchedRowCount;
			this.ValueDifferenceRowCount = result.ValueMismatches.Count;
			this.MissingRowCount = result.MissingInOutput.Count;
			this.ExtraRowCount = result.ExtraInOutput.Count;
			this.NonMatchingRowCount = result.NonMatchingRowCount;

			this.ComparisonSummary = BuildComparisonSummary(result);
			this.Verdict = result.IsMatch ? VerdictKind.Success : VerdictKind.Failure;
			this.VerdictHeadline = result.IsMatch ? "SUCCESS" : "FAILED";
			this.VerdictDetail = result.IsMatch ? $"All {result.MatchedRowCount:N0} row(s) match." : $"{result.NonMatchingRowCount:N0} non-matching row(s).";

			this.HasResult = true;
			this.ExitCode = result.IsMatch ? ExitMatch : ExitDifferences;
			this.StatusText = $"Compared at {DateTime.Now:HH:mm:ss}.";
			this.ExportDifferencesCommand.RaiseCanExecuteChanged();
			this.ExportExcelReportCommand.RaiseCanExecuteChanged();
		}
		catch(Exception exception)
		{
			ClearResults();
			this.ErrorMessage = exception.Message;
			this.Verdict = VerdictKind.Error;
			this.VerdictHeadline = "ERROR";
			this.VerdictDetail = exception.Message;
			this.ExitCode = ExitError;
			this.StatusText = "The comparison could not be run.";
		}
		finally
		{
			this.IsBusy = false;
		}
	}

	/// <summary>
	/// Flattens the result into the rows the grids show. One line per differing column rather than per
	/// row, so the grid can be sorted by column name to see whether one field is behind every failure.
	/// </summary>
	private static (IReadOnlyList<DifferenceRow>, IReadOnlyList<SingleSideRow>, IReadOnlyList<SingleSideRow>, string) Project(
		ComparisonResult result, ComparisonOptions options)
	{
		List<DifferenceRow> differences = [];
		foreach(RowMismatch mismatch in result.ValueMismatches)
		{
			string inputRowText = mismatch.InputRow.ToDisplayString();
			string outputRowText = mismatch.OutputRow.ToDisplayString();

			foreach(ValueDifference difference in mismatch.Differences)
			{
				differences.Add(new DifferenceRow(mismatch.DisplayKey, difference.Column, difference.InputValue, difference.OutputValue, mismatch.InputRow.LineNumber, mismatch.OutputRow.LineNumber, inputRowText, outputRowText));
			}
		}

		List<SingleSideRow> missing = [.. result.MissingInOutput.Select(r => new SingleSideRow(r.DisplayKey, r.Row.LineNumber, r.Row.ToDisplayString()))];

		List<SingleSideRow> extra = [.. result.ExtraInOutput.Select(r => new SingleSideRow(r.DisplayKey, r.Row.LineNumber, r.Row.ToDisplayString()))];

		return (differences, missing, extra, TextReport.Build(result, options));
	}

	/// <summary>
	/// The columns the run used. The compared list runs to dozens of names on a real file and is trimmed
	/// to the width of the banner, so each list is counted as well as named: the count survives the
	/// trimming, and the names in full are on the tooltip.
	/// </summary>
	private static string BuildComparisonSummary(ComparisonResult result)
	{
		return $"Keys: {string.Join(", ", result.KeyColumns)}   ·   " + $"Compared ({result.ComparedColumns.Count}): {string.Join(", ", result.ComparedColumns)}";
	}

	/// <summary>
	/// Puts the rows behind the grids and wraps each list in a view the column filters can narrow. The
	/// filters themselves are left as they are: a filter typed to chase one column through a comparison
	/// is usually still the filter wanted when that comparison is run again.
	/// </summary>
	private void SetRows(IReadOnlyList<DifferenceRow> differences, IReadOnlyList<SingleSideRow> missing, IReadOnlyList<SingleSideRow> extra)
	{
		this.Differences = differences;
		this.MissingRows = missing;
		this.ExtraRows = extra;

		this.DifferencesView = CreateView(differences, row => this.DifferenceFilters.Matches((DifferenceRow) row));
		this.MissingRowsView = CreateView(missing, row => this.MissingFilters.Matches((SingleSideRow) row));
		this.ExtraRowsView = CreateView(extra, row => this.ExtraFilters.Matches((SingleSideRow) row));

		OnDifferenceFiltersChanged();
		OnMissingFiltersChanged();
		OnExtraFiltersChanged();
	}

	private static ICollectionView CreateView(System.Collections.IEnumerable rows, Predicate<object> matches)
	{
		ICollectionView view = CollectionViewSource.GetDefaultView(rows);
		view.Filter = matches;
		return view;
	}

	private void OnDifferenceFiltersChanged()
	{
		this.DifferencesView?.Refresh();
		this.DifferencesFilterSummary = DescribeFiltering(this.DifferencesView, this.Differences.Count);
	}

	private void OnMissingFiltersChanged()
	{
		this.MissingRowsView?.Refresh();
		this.MissingRowsFilterSummary = DescribeFiltering(this.MissingRowsView, this.MissingRows.Count);
	}

	private void OnExtraFiltersChanged()
	{
		this.ExtraRowsView?.Refresh();
		this.ExtraRowsFilterSummary = DescribeFiltering(this.ExtraRowsView, this.ExtraRows.Count);
	}

	/// <summary>
	/// How much of the grid the filters are letting through. Worth saying plainly: a filtered grid and
	/// an empty result look identical, and the counts above the grid deliberately do not move.
	/// </summary>
	private static string DescribeFiltering(ICollectionView? view, int total)
	{
		int shown = view switch
		{
			CollectionView collection => collection.Count,
			null => total,
			_ => view.Cast<object>().Count()
		};

		return $"Showing {shown:N0} of {total:N0} row(s)";
	}

	private void ClearResults()
	{
		SetRows([], [], []);
		this.Warnings = [];
		this.ReportText = string.Empty;
		this.ComparisonSummary = string.Empty;
		this.InputRowCount = this.OutputRowCount = this.MatchedRowCount = 0;
		this.ValueDifferenceRowCount = this.MissingRowCount = this.ExtraRowCount = this.NonMatchingRowCount = 0;
		this.HasResult = false;
		this.Verdict = VerdictKind.None;
		this.VerdictHeadline = string.Empty;
		this.VerdictDetail = string.Empty;
		this.ErrorMessage = string.Empty;
		this.ExportDifferencesCommand.RaiseCanExecuteChanged();
	}

	// ---------------------------------------------------------------- options

	private void ApplyOptions(ComparisonOptions options)
	{
		this.Input.Path = options.InputFilePath;
		this.Output.Path = options.OutputFilePath;
		this.KeyColumns = string.Join(", ", options.KeyColumns);
		this.CompareColumns = string.Join(", ", options.CompareColumns);
		this.IgnoreCase = options.IgnoreCase;
		this.TrimValues = options.TrimValues;
		this.SimilarMatch = options.SimilarMatch;
		this.SimilarMatchRangeText = options.SimilarMatchRange.ToString(CultureInfo.InvariantCulture);
		this.Delimiter = options.Delimiter;
		this.SplitIndexes = options.SplitIndexes;
		this.NoHeaderRow = options.NoHeaderRow;
		this.Encoding = options.Encoding;
	}

	private static List<string> SplitList(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? [] : [.. value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
	}

	/// <summary>
	/// A negative range is passed through rather than clamped away, so that typing one produces the
	/// engine's explanation instead of quietly comparing exactly and reporting differences.
	/// </summary>
	private static decimal ParseRange(string value)
	{
		return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : decimal.Zero;
	}

	// ---------------------------------------------------------------- commands

	private void Browse(LoadedFile file)
	{
		string? chosen = _interaction.BrowseForOpen($"Choose the {file.Label.ToLowerInvariant()} file", TableReaderFactory.FileDialogFilter, file.Path);
		if(chosen is not null) file.Path = chosen;
	}

	private void SwapFiles()
	{
		(this.Input.Path, this.Output.Path) = (this.Output.Path, this.Input.Path);
		this.StatusText = "Input and output swapped.";
	}

	/// <summary>
	/// Offers the columns of both files, ticked where they are already named. A column present in only
	/// one file is still offered but says so, because that is exactly the case a user needs to see.
	/// </summary>
	/// <summary>
	/// Hands the first row of the file to the picker to be marked up, and takes the positions back.
	/// The row is read as the plain text it is rather than through the reader: the reader would want
	/// the very positions this is here to work out.
	/// </summary>
	private void PickSplitIndexes()
	{
		string path = FirstReadablePath();
		if(path.Length == 0)
		{
			this.ErrorMessage = "Choose an input or output file first - the picker marks up a row from it.";
			return;
		}

		string firstRow;
		try
		{
			firstRow = TextFile.Read(path, this.Encoding).Lines().FirstOrDefault(l => l.Length > 0) ?? string.Empty;
		}
		catch(Exception exception)
		{
			this.ErrorMessage = $"Could not read '{path}': {exception.Message}";
			return;
		}

		if(firstRow.Length == 0)
		{
			this.ErrorMessage = $"'{Path.GetFileName(path)}' has no rows to mark up.";
			return;
		}

		string? positions = _interaction.PickSplitPositions("Pick split index",
		                                                    $"The first row of {Path.GetFileName(path)}. Type the separator wherever a column should end - " +
		                                                    "the separators are counted and thrown away, and what is left is the row cut at those positions.",
		                                                    firstRow, this.SplitIndexes);

		if(positions is null) return;

		this.SplitIndexes = positions;
		this.StatusText = positions.Length == 0 ? "No split positions chosen." : $"Split positions: {positions}";
	}

	/// <summary>
	/// The file to take a row from: the input, or the output when only that is set. A workbook has no
	/// rows of text to mark up, so it is passed over.
	/// </summary>
	private string FirstReadablePath()
	{
		foreach(string path in (string[]) [this.Input.Path, this.Output.Path])
		{
			if(path.Trim().Length > 0 && File.Exists(path) && !XlsxTableReader.IsWorkbook(path)) return path;
		}

		return string.Empty;
	}

	private void PickColumns(string title, string prompt, string current, Action<string> assign)
	{
		List<ColumnChoice> choices = BuildColumnChoices(current);
		if(choices.Count == 0)
		{
			_interaction.ShowMessage(title, "Choose the files first - the columns are read from their headers.");
			return;
		}

		IReadOnlyList<string>? picked = _interaction.PickColumns(title, prompt, choices);
		if(picked is not null) assign(string.Join(", ", picked));
	}

	private List<ColumnChoice> BuildColumnChoices(string current)
	{
		HashSet<string> selected = new(SplitList(current).Select(TextKey.Canonical), StringComparer.OrdinalIgnoreCase);
		List<ColumnChoice> choices = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

		foreach(string name in this.Input.Columns.Concat(this.Output.Columns))
		{
			if(!seen.Add(TextKey.Canonical(name))) continue;

			bool inInput = this.Input.Table?.HasColumn(name) ?? false;
			bool inOutput = this.Output.Table?.HasColumn(name) ?? false;

			choices.Add(new ColumnChoice
			            {
				            Name = name,
				            Availability = inInput && inOutput ? "both files" : inInput ? "input only" : "output only",
				            IsSelected = selected.Contains(TextKey.Canonical(name))
			            });
		}

		// A name typed by hand that matches no header should not vanish when the picker is confirmed.
		foreach(string name in SplitList(current).Where(n => seen.Add(TextKey.Canonical(n))))
		{
			choices.Add(new ColumnChoice { Name = name, Availability = "not in either file", IsSelected = true });
		}

		// The columns already chosen come first, in the order they are already in, and the rest of the
		// headers follow. The picker hands its list back in the order it ends up in, so opening it on a
		// list in file order would quietly undo an arrangement the moment it was confirmed.
		Dictionary<string, ColumnChoice> byName = choices.ToDictionary(c => TextKey.Canonical(c.Name), StringComparer.OrdinalIgnoreCase);
		List<ColumnChoice> ordered = [];
		HashSet<ColumnChoice> placed = [];

		foreach(string name in SplitList(current))
		{
			if(byName.TryGetValue(TextKey.Canonical(name), out ColumnChoice? chosen) && placed.Add(chosen)) ordered.Add(chosen);
		}

		ordered.AddRange(choices.Where(c => !placed.Contains(c)));
		return ordered;
	}

	private void LoadSettings()
	{
		string? path = _interaction.BrowseForOpen("Load settings", "Settings (*.json)|*.json|All files (*.*)|*.*", _settingsPath);
		if(path is null) return;

		try
		{
			ApplyOptions(ComparisonOptions.LoadFromFile(path));
			_settingsPath = path;
			ClearResults();
			this.StatusText = $"Settings loaded from {path}";
		}
		catch(Exception exception)
		{
			this.ErrorMessage = $"Could not read '{path}': {exception.Message}";
		}
	}

	private void SaveSettings()
	{
		WriteSettings(_settingsPath);
	}

	private void SaveSettingsAs()
	{
		string? path = _interaction.BrowseForSave("Save settings", "Settings (*.json)|*.json|All files (*.*)|*.*", "appsettings.json", _settingsPath);
		if(path is not null) WriteSettings(path);
	}

	private void WriteSettings(string path)
	{
		try
		{
			BuildOptions().SaveToFile(path);
			_settingsPath = path;
			this.StatusText = $"Settings saved to {path}";
		}
		catch(Exception exception)
		{
			this.ErrorMessage = $"Could not save '{path}': {exception.Message}";
		}
	}

	private void CopyReport()
	{
		_interaction.CopyToClipboard(this.ReportText);
		this.StatusText = "Report copied to the clipboard.";
	}

	/// <summary>
	/// The whole run as a workbook, a sheet to each kind of outcome. It works from the result itself
	/// rather than from the grids, so it carries every row rather than the Max rows the window lists,
	/// and the options it reports are the ones the run actually used rather than whatever the boxes
	/// have been changed to since.
	/// </summary>
	private void ExportExcelReport()
	{
		if(_result is null || _resultOptions is null) return;

		string? path = _interaction.BrowseForSave("Export report as Excel", "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*", SuggestExportName("xlsx"), null);

		if(path is null) return;

		try
		{
			XlsxReport.Write(path, _result, _resultOptions);
			this.StatusText = $"Report written to {path}";
		}
		catch(Exception exception)
		{
			this.ErrorMessage = $"Could not write '{path}': {exception.Message}";
		}
	}

	private void ExportReport()
	{
		string? path = _interaction.BrowseForSave("Export report", "Text file (*.txt)|*.txt|All files (*.*)|*.*", SuggestExportName("txt"), null);
		if(path is null) return;

		try
		{
			File.WriteAllText(path, this.ReportText, new System.Text.UTF8Encoding(true));
			this.StatusText = $"Report written to {path}";
		}
		catch(Exception exception)
		{
			this.ErrorMessage = $"Could not write '{path}': {exception.Message}";
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
		if(path is null) return;

		try
		{
			System.Text.StringBuilder csv = new();
			csv.AppendLine("Category;Key;Column;InputValue;OutputValue;InputLine;OutputLine");

			foreach(DifferenceRow row in this.Differences)
			{
				csv.AppendLine(string.Join(';', Quote("Value difference"), Quote(row.Key), Quote(row.Column), Quote(row.InputValue), Quote(row.OutputValue), row.InputLine, row.OutputLine));
			}

			foreach(SingleSideRow row in this.MissingRows)
			{
				csv.AppendLine(string.Join(';', Quote("Missing in output"), Quote(row.Key), Quote(string.Empty), Quote(row.RowText), Quote(string.Empty), row.LineNumber, string.Empty));
			}

			foreach(SingleSideRow row in this.ExtraRows)
			{
				csv.AppendLine(string.Join(';', Quote("Extra in output"), Quote(row.Key), Quote(string.Empty), Quote(string.Empty), Quote(row.RowText), string.Empty, row.LineNumber));
			}

			File.WriteAllText(path, csv.ToString(), new System.Text.UTF8Encoding(true));
			this.StatusText = $"Differences written to {path}";
		}
		catch(Exception exception)
		{
			this.ErrorMessage = $"Could not write '{path}': {exception.Message}";
		}
	}

	private static string Quote(string value)
	{
		return $"\"{value.Replace("\"", "\"\"")}\"";
	}

	private string SuggestExportName(string extension)
	{
		string name = this.Input.Path.Length > 0 ? Path.GetFileNameWithoutExtension(this.Input.Path) : "comparison";
		return $"{name}-comparison-{DateTime.Now:yyyyMMdd-HHmm}.{extension}";
	}

	private void ShowAbout()
	{
		_interaction.ShowText("About FileComparerWindows",
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
		                      than position. They must carry the same columns, though: a column on one side only stops the
		                      comparison as an error rather than being left out of it.

		                      Encoding is detected from a byte order mark, else UTF-8, else Windows-1252, and text is
		                      normalised (NFC) before anything is matched.

		                      Dates in .xlsx files are read as their underlying serial number, since cell number formats are
		                      not interpreted. Two spreadsheets still compare correctly against each other; a spreadsheet
		                      compared against a text file needs the dates written as text.
		                      """);
	}
	#endregion
}