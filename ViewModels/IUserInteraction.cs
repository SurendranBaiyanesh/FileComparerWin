namespace FileComparerWindows.ViewModels;

/// <summary>
/// The handful of things the view model needs a window for. Kept behind an interface so the view model
/// stays free of dialog code.
/// </summary>
public interface IUserInteraction
{
    #region Public methods

    string? BrowseForOpen(string title, string filter, string? currentPath);

    string? BrowseForSave(string title, string filter, string suggestedFileName, string? currentPath);

    /// <summary>Returns the ticked column names, or null when the user cancels.</summary>
    IReadOnlyList<string>? PickColumns(string title, string prompt, IReadOnlyList<ColumnChoice> choices);

    /// <summary>
    /// Shows a row to be marked up with a separator wherever a column should end, and returns the
    /// positions that marks out, or null when the user cancels.
    /// </summary>
    string? PickSplitPositions(string title, string prompt, string firstRow, string currentPositions);

    void ShowMessage(string title, string message);

    void ShowText(string title, string text);

    void CopyToClipboard(string text);

    #endregion
}
