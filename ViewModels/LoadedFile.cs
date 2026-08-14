using FileComparerWindows.Configuration;
using FileComparerWindows.Infrastructure;
using FileComparerWindows.Model;
using FileComparerWindows.Readers;

namespace FileComparerWindows.ViewModels;

/// <summary>
/// One side of the comparison: the chosen path and, once it has been read, the table behind it. The
/// window reads a file as soon as it is chosen rather than waiting for Compare, so that the format,
/// encoding and row count show up straight away and the column pickers have something to offer - a
/// mis-detected encoding is far easier to see next to the file name than inside an error message.
/// </summary>
public sealed class LoadedFile(string label) : ObservableObject
{
    #region Fields

    private static readonly TableReaderFactory Factory = new TableReaderFactory();

    private string _path = string.Empty;
    private string _description = "No file chosen.";
    private bool _hasError;
    private DataTable? _table;
    private string _signature = string.Empty;
    private CancellationTokenSource? _pendingRefresh;

    #endregion

    #region Properties

    public string Label { get; } = label;

    public string Path
    {
        get => _path;
        set
        {
            // Paths pasted from Explorer usually arrive wrapped in quotes.
            if (SetProperty(ref _path, (value ?? string.Empty).Trim().Trim('"', '\'')))
                Clear();
        }
    }

    /// <summary>Format, encoding and row count once read; the reason it could not be read otherwise.</summary>
    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public DataTable? Table
    {
        get => _table;
        private set
        {
            if (SetProperty(ref _table, value))
                RaisePropertyChanged(nameof(Columns));
        }
    }

    public IReadOnlyList<string> Columns => Table?.Columns ?? [];

    #endregion

    #region Public methods

    /// <summary>
    /// Reads the file a moment from now, and drops the attempt if another one is asked for first. A path
    /// typed by hand is momentarily half a path, and reading each of those in turn would fill the
    /// window with errors about files that were never meant.
    /// </summary>
    public async Task RefreshLaterAsync(ComparisonOptions options, int delayMilliseconds = 350)
    {
        CancellationTokenSource refresh = new CancellationTokenSource();
        CancellationTokenSource? superseded = Interlocked.Exchange(ref _pendingRefresh, refresh);
        superseded?.Cancel();
        superseded?.Dispose();

        try
        {
            await Task.Delay(delayMilliseconds, refresh.Token);
            await RefreshAsync(options);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke; that attempt will do the reading.
        }
    }

    /// <summary>
    /// Reads the file unless the last read already used the same file and the same reader settings.
    /// The file's own timestamp is part of that test, so a file re-exported behind the window's back
    /// is picked up rather than silently compared in its old form.
    /// </summary>
    public async Task RefreshAsync(ComparisonOptions options)
    {
        if (_path.Length == 0)
        {
            Clear();
            return;
        }

        string signature = BuildSignature(options);
        if (Table is not null && signature == _signature)
            return;

        Description = "Reading…";
        HasError = false;

        try
        {
            string path = _path;
            DataTable table = await Task.Run(() => Factory.Load(path, options));

            Table = table;
            _signature = signature;
            Description = $"{table.FormatName} · {table.Rows.Count:N0} row(s) · {table.Columns.Count} column(s)";
        }
        catch (Exception exception)
        {
            Table = null;
            _signature = string.Empty;
            HasError = true;
            Description = exception.Message;
        }
    }

    #endregion

    #region Private methods

    private void Clear()
    {
        Table = null;
        _signature = string.Empty;
        HasError = false;
        Description = _path.Length == 0 ? "No file chosen." : "Reading…";
    }

    private string BuildSignature(ComparisonOptions options)
    {
        // Only the settings that change how the file is read belong here.
        string stamp = File.Exists(_path)
            ? File.GetLastWriteTimeUtc(_path).Ticks.ToString() + ":" + new FileInfo(_path).Length
            : "missing";

        return string.Join('|', _path, stamp, options.Encoding, options.Delimiter);
    }

    #endregion
}
