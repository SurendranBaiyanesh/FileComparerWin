using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FileComparerWindows.ViewModels;

namespace FileComparerWindows;

/// <summary>
/// Ticks column names off the headers the two files actually have. Typing them is still possible and
/// still supported; this exists because a column called "Name des Versicherten/Begünstigten" is not a
/// thing anybody should have to type twice.
/// </summary>
public partial class ColumnPickerWindow : Window
{
    private readonly IReadOnlyList<ColumnChoice> _choices;

    public ColumnPickerWindow(string title, string prompt, IReadOnlyList<ColumnChoice> choices)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        _choices = choices;

        ColumnList.ItemsSource = choices;
        foreach (ColumnChoice choice in choices)
            choice.PropertyChanged += OnChoiceChanged;

        UpdateSelectionCount();
        Loaded += (_, _) => FilterBox.Focus();
    }

    /// <summary>The ticked names, in the order the files list them.</summary>
    public IReadOnlyList<string> SelectedColumns { get; private set; } = [];

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

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        SelectedColumns = [.. _choices.Where(c => c.IsSelected).Select(c => c.Name)];
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (ColumnChoice choice in _choices)
            choice.PropertyChanged -= OnChoiceChanged;

        base.OnClosed(e);
    }
}
