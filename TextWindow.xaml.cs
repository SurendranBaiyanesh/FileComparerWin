using System.Windows;

namespace FileComparerWindows;

/// <summary>A block of text worth reading in full - the command-line summary, the notes about formats.</summary>
public partial class TextWindow : Window
{
    public TextWindow(string title, string text)
    {
        InitializeComponent();

        Title = title;
        Body.Text = text;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Body.Text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another application had the clipboard open; not worth an error dialog.
        }
    }
}
