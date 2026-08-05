using System.Windows;
using FileComparerWindows.Configuration;
using FileComparerWindows.ViewModels;
using Microsoft.Win32;

namespace FileComparerWindows;

public partial class MainWindow : Window, IUserInteraction
{
    private readonly MainViewModel _viewModel;
    private readonly string[] _args;

    public MainWindow(string[] args)
    {
        InitializeComponent();

        _args = args;
        _viewModel = new MainViewModel(this);
        DataContext = _viewModel;

        _viewModel.Initialise(args);
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshFilesAsync();

        if (CommandLine.IsAutoRunRequested(_args))
            await _viewModel.RunAtStartupAsync();
    }

    /// <summary>Mirrors the console tool's exit codes, so a script that launches the window still learns the outcome.</summary>
    protected override void OnClosed(EventArgs e)
    {
        Environment.ExitCode = _viewModel.ExitCode;
        base.OnClosed(e);
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- drag and drop

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: LoadedFile file })
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            file.Path = paths[0];
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- IUserInteraction

    public string? BrowseForOpen(string title, string filter, string? currentPath)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            InitialDirectory = DirectoryOf(currentPath)
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? BrowseForSave(string title, string filter, string suggestedFileName, string? currentPath)
    {
        SaveFileDialog dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = suggestedFileName,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = DirectoryOf(currentPath)
        };

        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static string DirectoryOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            string? directory = Path.GetDirectoryName(path);
            return directory is not null && Directory.Exists(directory) ? directory : string.Empty;
        }
        catch (ArgumentException)
        {
            // A half-typed path is not a reason to refuse to open the dialog.
            return string.Empty;
        }
    }

    public IReadOnlyList<string>? PickColumns(string title, string prompt, IReadOnlyList<ColumnChoice> choices)
    {
        ColumnPickerWindow picker = new ColumnPickerWindow(title, prompt, choices) { Owner = this };
        return picker.ShowDialog() == true ? picker.SelectedColumns : null;
    }

    public void ShowMessage(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowText(string title, string text) =>
        new TextWindow(title, text) { Owner = this }.ShowDialog();

    public void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another application had the clipboard open; nothing here is worth an error dialog.
            MessageBox.Show(this, "The clipboard was busy. Try again.", "Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
