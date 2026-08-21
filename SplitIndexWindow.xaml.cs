using System.Windows;
using System.Windows.Controls;
using FileComparerWindows.Readers;

namespace FileComparerWindows;

/// <summary>
/// Builds the list of split positions by letting the row itself be marked up. The first row of the
/// file is shown as it was read; typing a separator wherever a column should end is a great deal
/// easier than counting characters and writing the numbers out by hand.
///
/// The separators are never part of the data. They are counted, turned back into positions, and
/// thrown away; the grid underneath shows the row cut at exactly those positions, by the same code
/// the reader will use, so the preview cannot disagree with the result.
/// </summary>
public partial class SplitIndexWindow : Window
{
    #region Fields

    private readonly string _originalRow;

    #endregion

    #region Constructor

    public SplitIndexWindow(string title, string prompt, string firstRow, string currentPositions)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        _originalRow = firstRow;

        MarkerBox.Text = SplitPositions.DefaultMarker.ToString();

        // Opened on the positions already in the box, so a list can be adjusted rather than started
        // from nothing every time. This assignment raises TextChanged, which draws the preview.
        RowBox.Text = SplitPositions.Mark(firstRow, SplitPositions.Parse(currentPositions), SplitPositions.DefaultMarker);

        Loaded += (_, _) => RowBox.Focus();
    }

    #endregion

    #region Properties

    /// <summary>The positions the row was marked up into, ready for the Split at box.</summary>
    public string Positions { get; private set; } = string.Empty;

    #endregion

    #region Private methods

    private char Marker => MarkerBox.Text.Length > 0 ? MarkerBox.Text[0] : SplitPositions.DefaultMarker;

    private void OnRowChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnMarkerChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnReset(object sender, RoutedEventArgs e) => RowBox.Text = _originalRow;

    /// <summary>
    /// Works the positions out from where the separators are and shows what they would cut. The row is
    /// stripped of its separators first, so the preview is of the data rather than of the mark-up.
    /// </summary>
    private void Refresh()
    {
        // TextChanged fires while the window is still being built, before the rest of it exists.
        if (RowBox is null || MarkerBox is null || PositionsText is null || PreviewGrid is null || PositionCount is null)
            return;

        char marker = Marker;
        string marked = RowBox.Text;
        string row = SplitPositions.Strip(marked, marker);
        int[] positions = SplitPositions.FromMarkedLine(marked, marker);

        Positions = SplitPositions.Describe(positions);
        PositionsText.Text = positions.Length == 0 ? "Split at: (nothing yet)" : $"Split at: {Positions}";

        MarkerWarning.Text = _originalRow.Contains(marker)
            ? $"The row contains '{marker}' itself, so it cannot mark the cuts. Choose another separator."
            : string.Empty;

        List<string> values = SplitPositions.Split(row, positions);
        PreviewGrid.ItemsSource = values
            .Select((value, i) => new PreviewRow($"column{i + 1}", positions[i], value.Length, value))
            .ToList();

        PositionCount.Text = positions.Length == 0
            ? "No cuts yet - type the separator wherever a column should end."
            : $"{positions.Length} column(s), {row.Length} character(s) in the row.";
    }

    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;

    #endregion

    #region Nested types

    private sealed record PreviewRow(string Name, int Position, int Width, string Value);

    #endregion
}
