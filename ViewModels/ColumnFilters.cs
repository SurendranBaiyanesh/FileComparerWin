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
	#region Fields
	private string _text = string.Empty;
	#endregion

	#region Properties
	public string Text
	{
		get => _text;
		set
		{
			if(!SetProperty(ref _text, value ?? string.Empty)) return;

			RaisePropertyChanged(nameof(this.IsActive));
			changed();
		}
	}

	public bool IsActive => _text.Length > 0;
	#endregion

	#region Public methods
	public bool Matches(string value)
	{
		return _text.Length == 0 || value.Contains(_text, StringComparison.CurrentCultureIgnoreCase);
	}

	/// <summary>Line numbers, matched as the digits they are shown as, so "4" finds line 4 and line 42.</summary>
	public bool Matches(int value)
	{
		return _text.Length == 0 || value.ToString(CultureInfo.InvariantCulture).Contains(_text, StringComparison.Ordinal);
	}

	public void Clear()
	{
		this.Text = string.Empty;
	}
	#endregion
}

/// <summary>The filter boxes over the value-differences grid.</summary>
public sealed class DifferenceFilters : ObservableObject
{
	#region Constructor
	public DifferenceFilters(Action changed)
	{
		void OnChanged()
		{
			RaisePropertyChanged(nameof(this.IsActive));
			changed();
		}

		this.Key = new ColumnFilter(OnChanged);
		this.Column = new ColumnFilter(OnChanged);
		this.InputValue = new ColumnFilter(OnChanged);
		this.OutputValue = new ColumnFilter(OnChanged);
		this.InputLine = new ColumnFilter(OnChanged);
		this.OutputLine = new ColumnFilter(OnChanged);
	}
	#endregion

	#region Properties
	public ColumnFilter Key { get; }
	public ColumnFilter Column { get; }
	public ColumnFilter InputValue { get; }
	public ColumnFilter OutputValue { get; }
	public ColumnFilter InputLine { get; }
	public ColumnFilter OutputLine { get; }

	public bool IsActive => this.All.Any(filter => filter.IsActive);
	#endregion

	#region Public methods
	/// <summary>Filters narrow one another, so naming a column and a value shows the rows that are both.</summary>
	public bool Matches(DifferenceRow row)
	{
		return this.Key.Matches(row.Key)
	         && this.Column.Matches(row.Column)
	         && this.InputValue.Matches(row.InputValue)
	         && this.OutputValue.Matches(row.OutputValue)
	         && this.InputLine.Matches(row.InputLine)
	         && this.OutputLine.Matches(row.OutputLine);
	}

	public void Clear()
	{
		foreach(ColumnFilter filter in this.All)
		{
			filter.Clear();
		}
	}
	#endregion

	#region Private properties
	private IEnumerable<ColumnFilter> All => [this.Key, this.Column, this.InputValue, this.OutputValue, this.InputLine, this.OutputLine];
	#endregion
}

/// <summary>The filter boxes over the missing and extra grids, which list the same three columns.</summary>
public sealed class SingleSideFilters : ObservableObject
{
	#region Constructor
	public SingleSideFilters(Action changed)
	{
		void OnChanged()
		{
			RaisePropertyChanged(nameof(this.IsActive));
			changed();
		}

		this.Key = new ColumnFilter(OnChanged);
		this.Line = new ColumnFilter(OnChanged);
		this.Row = new ColumnFilter(OnChanged);
	}
	#endregion

	#region Properties
	public ColumnFilter Key { get; }
	public ColumnFilter Line { get; }
	public ColumnFilter Row { get; }

	public bool IsActive => this.All.Any(filter => filter.IsActive);
	#endregion

	#region Public methods
	public bool Matches(SingleSideRow row)
	{
		return this.Key.Matches(row.Key) && this.Line.Matches(row.LineNumber) && this.Row.Matches(row.RowText);
	}

	public void Clear()
	{
		foreach(ColumnFilter filter in this.All)
		{
			filter.Clear();
		}
	}
	#endregion

	#region Private properties
	private IEnumerable<ColumnFilter> All => [this.Key, this.Line, this.Row];
	#endregion
}