using System.Globalization;
using FileComparerWindows.Infrastructure;

namespace FileComparerWindows.ViewModels;

/// <summary>
/// One column's filter box, matching anywhere in the value and without regard to case.
/// </summary>
/// <remarks>
/// The text is used exactly as typed rather than trimmed. This application exists to show that a value
/// has a space on the end of it, so a filter that quietly dropped one could not find the very rows it
/// was opened for.
/// </remarks>
public sealed class ColumnFilter(Action changed) : ObservableObject
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value ?? string.Empty))
                return;

            RaisePropertyChanged(nameof(IsActive));
            changed();
        }
    }

    public bool IsActive => _text.Length > 0;

    public bool Matches(string value) =>
        _text.Length == 0 || value.Contains(_text, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Line numbers, matched as the digits they are shown as, so "4" finds line 4 and line 42.</summary>
    public bool Matches(int value) =>
        _text.Length == 0 || value.ToString(CultureInfo.InvariantCulture).Contains(_text, StringComparison.Ordinal);

    public void Clear() => Text = string.Empty;
}

/// <summary>The filter boxes over the value-differences grid.</summary>
public sealed class DifferenceFilters : ObservableObject
{
    public DifferenceFilters(Action changed)
    {
        void OnChanged()
        {
            RaisePropertyChanged(nameof(IsActive));
            changed();
        }

        Key = new ColumnFilter(OnChanged);
        Column = new ColumnFilter(OnChanged);
        InputValue = new ColumnFilter(OnChanged);
        OutputValue = new ColumnFilter(OnChanged);
        InputLine = new ColumnFilter(OnChanged);
        OutputLine = new ColumnFilter(OnChanged);
    }

    public ColumnFilter Key { get; }
    public ColumnFilter Column { get; }
    public ColumnFilter InputValue { get; }
    public ColumnFilter OutputValue { get; }
    public ColumnFilter InputLine { get; }
    public ColumnFilter OutputLine { get; }

    public bool IsActive => All.Any(filter => filter.IsActive);

    /// <summary>Filters narrow one another, so naming a column and a value shows the rows that are both.</summary>
    public bool Matches(DifferenceRow row) =>
        Key.Matches(row.Key)
        && Column.Matches(row.Column)
        && InputValue.Matches(row.InputValue)
        && OutputValue.Matches(row.OutputValue)
        && InputLine.Matches(row.InputLine)
        && OutputLine.Matches(row.OutputLine);

    public void Clear()
    {
        foreach (ColumnFilter filter in All)
            filter.Clear();
    }

    private IEnumerable<ColumnFilter> All => [Key, Column, InputValue, OutputValue, InputLine, OutputLine];
}

/// <summary>The filter boxes over the missing and extra grids, which list the same three columns.</summary>
public sealed class SingleSideFilters : ObservableObject
{
    public SingleSideFilters(Action changed)
    {
        void OnChanged()
        {
            RaisePropertyChanged(nameof(IsActive));
            changed();
        }

        Key = new ColumnFilter(OnChanged);
        Line = new ColumnFilter(OnChanged);
        Row = new ColumnFilter(OnChanged);
    }

    public ColumnFilter Key { get; }
    public ColumnFilter Line { get; }
    public ColumnFilter Row { get; }

    public bool IsActive => All.Any(filter => filter.IsActive);

    public bool Matches(SingleSideRow row) =>
        Key.Matches(row.Key) && Line.Matches(row.LineNumber) && Row.Matches(row.RowText);

    public void Clear()
    {
        foreach (ColumnFilter filter in All)
            filter.Clear();
    }

    private IEnumerable<ColumnFilter> All => [Key, Line, Row];
}
