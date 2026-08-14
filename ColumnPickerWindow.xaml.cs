using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FileComparerWindows.ViewModels;

namespace FileComparerWindows;

/// <summary>
/// Ticks column names off the headers the two files actually have. Typing them is still possible and
/// still supported; this exists because a column called "Name des Versicherten/Begünstigten" is not a
/// thing anybody should have to type twice.
/// </summary>
public partial class ColumnPickerWindow : Window
{
    /// <summary>
    /// Observable, because a drag rearranges it and the list has to follow. The order here is the
    /// order the chosen names are handed back in, which is what makes dragging worth doing.
    /// </summary>
    #region Fields

    private readonly ObservableCollection<ColumnChoice> _choices;

    private ColumnChoice? _dragging;

    #endregion

    #region Constructor

    public ColumnPickerWindow(string title, string prompt, IReadOnlyList<ColumnChoice> choices)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        _choices = new ObservableCollection<ColumnChoice>(choices);

        ColumnList.ItemsSource = _choices;
        foreach (ColumnChoice choice in _choices)
            choice.PropertyChanged += OnChoiceChanged;

        UpdateSelectionCount();
        Loaded += (_, _) => FilterBox.Focus();
    }

    #endregion

    #region Properties

    /// <summary>The ticked names, in the order they stand in the list.</summary>
    public IReadOnlyList<string> SelectedColumns { get; private set; } = [];

    #endregion

    #region Private methods

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e) => UpdateSelectionCount();

    private void UpdateSelectionCount()
    {
        int selected = _choices.Count(c => c.IsSelected);
        SelectionCount.Text = selected == 0 ? "nothing selected" : $"{selected} of {_choices.Count} selected";
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        string filter = FilterBox.Text.Trim();
        ICollectionView view = CollectionViewSource.GetDefaultView(ColumnList.ItemsSource);

        view.Filter = filter.Length == 0
            ? null
            : item => item is ColumnChoice choice && choice.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>All and None act on what the filter is showing, so they stay useful on a wide file.</summary>
    private void OnSelectAll(object sender, RoutedEventArgs e) => SetVisibleSelection(true);

    private void OnSelectNone(object sender, RoutedEventArgs e) => SetVisibleSelection(false);

    private void SetVisibleSelection(bool isSelected)
    {
        foreach (object item in CollectionViewSource.GetDefaultView(ColumnList.ItemsSource))
            if (item is ColumnChoice choice)
                choice.IsSelected = isSelected;
    }

    // ---------------------------------------------------------------- reordering

    /// <summary>
    /// The row is moved as the pointer passes over its neighbours rather than on release, so the list
    /// shows where the column is going to land while it is still being placed. Plain mouse capture
    /// does this: the drag never leaves the list, so there is nothing for the system's drag and drop
    /// to carry, and a captured pointer keeps reporting where it is even outside the window.
    /// </summary>
    private void OnGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ColumnChoice choice })
            return;

        _dragging = choice;
        ColumnList.SelectedItem = choice;   // the only mark the row is the one being moved

        // The grip sets this cursor on hover, but capture hands every move to the list, and the list
        // would go back to an arrow the moment the pointer left the grip it is dragging. Overriding
        // holds the cursor for as long as the row is being carried.
        Mouse.OverrideCursor = Cursors.SizeNS;

        ColumnList.CaptureMouse();
        e.Handled = true;
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag();
            return;
        }

        Point point = e.GetPosition(ColumnList);
        int from = _choices.IndexOf(_dragging);
        if (from < 0)
            return;

        // Above the list is the way to the top, and below it the way to the bottom. Without this a
        // column could only be moved as far as the rows that happen to be on screen.
        int to = point.Y < 0 ? 0
               : point.Y > ColumnList.ActualHeight ? _choices.Count - 1
               : IndexUnder(point);

        if (to >= 0 && to != from)
            _choices.Move(from, to);
    }

    private void OnListMouseUp(object sender, MouseButtonEventArgs e) => EndDrag();

    /// <summary>
    /// Capture can be taken away rather than given up - another window claiming it, the dialog closing
    /// under the pointer. Ending the drag from here as well means there is no way out of it that
    /// leaves the whole application stuck with a resize cursor.
    /// </summary>
    private void OnListLostCapture(object sender, MouseEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_dragging is null)
            return;

        // Cleared first, so that releasing the capture below comes back through here and stops.
        _dragging = null;
        Mouse.OverrideCursor = null;
        ColumnList.ReleaseMouseCapture();
    }

    /// <summary>The row under the pointer, or -1 where there is no row.</summary>
    private int IndexUnder(Point point)
    {
        DependencyObject? node = VisualTreeHelper.HitTest(ColumnList, point)?.VisualHit;

        while (node is not null and not ListBoxItem)
            node = VisualTreeHelper.GetParent(node);

        return node is ListBoxItem { DataContext: ColumnChoice choice } ? _choices.IndexOf(choice) : -1;
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        SelectedColumns = [.. _choices.Where(c => c.IsSelected).Select(c => c.Name)];
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        // A dialog closed by Escape in the middle of a drag would otherwise take the cursor with it.
        EndDrag();

        foreach (ColumnChoice choice in _choices)
            choice.PropertyChanged -= OnChoiceChanged;

        base.OnClosed(e);
    }

    #endregion
}
