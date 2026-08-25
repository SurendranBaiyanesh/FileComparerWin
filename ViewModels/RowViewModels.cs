using FileComparerWindows.Infrastructure;

namespace FileComparerWindows.ViewModels;

/// <summary>One differing column of one paired row: the unit the Differences grid lists.</summary>
public sealed record DifferenceRow(string Key, string Column, string InputValue, string OutputValue, int InputLine, int OutputLine, string InputRowText, string OutputRowText);

/// <summary>A row that exists on one side only, for the Missing and Extra grids.</summary>
public sealed record SingleSideRow(string Key, int LineNumber, string RowText);

/// <summary>A column offered by the column picker, and whether the user has ticked it.</summary>
public sealed class ColumnChoice : ObservableObject
{
	#region Fields
	private bool _isSelected;
	#endregion

	#region Properties
	public required string Name { get; init; }

	/// <summary>Where the column occurs: both files, or only one of them.</summary>
	public required string Availability { get; init; }

	/// <summary>
	/// What the column holds in the first row, so that a name like column7 can be told from the others
	/// by what is under it. A number, a date and a padded code are recognisable on sight where a
	/// generated name says nothing at all.
	/// </summary>
	public string Sample { get; init; } = string.Empty;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
	#endregion
}

/// <summary>How the last run ended, so the banner can colour itself.</summary>
public enum VerdictKind
{
	None,
	Success,
	Failure,
	Error
}