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

    void ShowMessage(string title, string message);

    void ShowText(string title, string text);

    void CopyToClipboard(string text);

    #endregion
}
