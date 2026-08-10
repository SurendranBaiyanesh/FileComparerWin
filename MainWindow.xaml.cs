using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
        AcceptDropsThroughout(FilesCard);

        await _viewModel.RefreshFilesAsync();

        if (CommandLine.IsAutoRunRequested(_args))
            await _viewModel.RunAtStartupAsync();
    }

    /// <summary>
    /// Windows will not let a process hand data to a window running with more privilege than itself,
    /// so a file dragged out of Explorer is refused by a window started from an elevated Visual Studio
    /// or an administrator's prompt - silently, with no cursor and no message to say why. These three
    /// messages are the ones a file drop is carried in, and letting them through is what the guard was
    /// given an exception list for. Nothing arrives by them but a path, and taking a path from anyone
    /// who can already reach the window is the whole point of the boxes.
    ///
    /// The call does nothing when the window is not elevated, which is how it should be run anyway.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        foreach (uint message in (uint[])[WmDropFiles, WmCopyData, WmCopyGlobalData])
            ChangeWindowMessageFilterEx(handle, message, MsgfltAllow, IntPtr.Zero);
    }

    private const uint WmCopyGlobalData = 0x0049;
    private const uint WmCopyData = 0x004A;
    private const uint WmDropFiles = 0x0233;
    private const uint MsgfltAllow = 1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr change);

    /// <summary>Mirrors the console tool's exit codes, so a script that launches the window still learns the outcome.</summary>
    protected override void OnClosed(EventArgs e)
    {
        Environment.ExitCode = _viewModel.ExitCode;
        base.OnClosed(e);
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- drag and drop

    /// <summary>
    /// Windows asks the element under the pointer whether it takes a drop and, when it says no, raises
    /// no drag event at all - it does not go on to ask the panel behind it. The word "Input", the line
    /// that reports what was read, the Browse button: each is a hole in a panel that was meant to take
    /// a file anywhere, and between them they account for most of it. Marking the whole panel through
    /// means the events are raised wherever a file is let go of, and they travel up to the handlers on
    /// the panel itself in the usual way.
    /// </summary>
    private static void AcceptDropsThroughout(DependencyObject element)
    {
        if (element is UIElement part)
            part.AllowDrop = true;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            AcceptDropsThroughout(VisualTreeHelper.GetChild(element, i));
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        // Kept cheap. This runs over and over as the pointer moves, so it asks only what is on offer
        // and leaves testing the paths themselves until something is actually let go of.
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCardDragEnter(object sender, DragEventArgs e) => ShowDropHighlight(true);

    private void OnCardDragLeave(object sender, DragEventArgs e)
    {
        // Moving from the panel onto one of the boxes inside it is reported as leaving the panel,
        // because the box is a drop target of its own. Only a pointer genuinely outside the panel
        // puts the panel back to normal, or the highlight would flicker on the way to a box.
        if (sender is FrameworkElement panel && Contains(panel, e.GetPosition(panel)))
            return;

        ShowDropHighlight(false);
    }

    private static bool Contains(FrameworkElement element, Point point) =>
        point.X >= 0 && point.Y >= 0 && point.X < element.ActualWidth && point.Y < element.ActualHeight;

    /// <summary>
    /// Runs ahead of both drop handlers, so the panel goes back to normal even when what was dropped
    /// turns out to hold no file either of them will take.
    /// </summary>
    private void OnCardPreviewDrop(object sender, DragEventArgs e) => ShowDropHighlight(false);

    /// <summary>A drop onto one of the boxes: that box was aimed at, so that box takes the file.</summary>
    private void OnFileDrop(object sender, DragEventArgs e)
    {
        string[] files = DroppedFiles(e);
        if (files.Length == 0)
            return;

        if (files.Length > 1)
            LoadPair(files);
        else if (sender is FrameworkElement { DataContext: LoadedFile box })
            box.Path = files[0];
        else
            return;

        e.Handled = true;
    }

    /// <summary>
    /// A drop on the panel but not on either box, so nothing says which side was meant: the empty box
    /// takes the file, and once both are filled the input box does, that being where a fresh
    /// comparison starts.
    /// </summary>
    private void OnCardDrop(object sender, DragEventArgs e)
    {
        string[] files = DroppedFiles(e);
        if (files.Length == 0)
            return;

        if (files.Length > 1)
            LoadPair(files);
        else if (_viewModel.Input.Path.Length > 0 && _viewModel.Output.Path.Length == 0)
            _viewModel.Output.Path = files[0];
        else
            _viewModel.Input.Path = files[0];

        e.Handled = true;
    }

    /// <summary>
    /// Two files at once say "compare these", wherever in the panel they were let go of, and they are
    /// taken in the order they were handed over - which is the order they were shown in, so the two
    /// boxes can be filled the other way round by dropping them one at a time.
    /// </summary>
    private void LoadPair(string[] files)
    {
        _viewModel.Input.Path = files[0];
        _viewModel.Output.Path = files[1];
    }

    /// <summary>
    /// The dropped paths that are files. A folder holds no rows to compare, and letting one through
    /// would replace a path that worked with a message about a path that cannot.
    /// </summary>
    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(File.Exists).ToArray()
            : [];

    private void ShowDropHighlight(bool on)
    {
        if (on)
        {
            FilesCard.BorderBrush = (Brush)FindResource("AccentBrush");
            FilesCard.Background = (Brush)FindResource("AccentSoftBrush");
        }
        else
        {
            // Back to whatever the Card style asks for, rather than to a colour repeated here.
            FilesCard.ClearValue(Border.BorderBrushProperty);
            FilesCard.ClearValue(Border.BackgroundProperty);
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
