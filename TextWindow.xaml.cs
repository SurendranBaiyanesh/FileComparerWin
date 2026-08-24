using System.Windows;

namespace FileComparerWindows;

/// <summary>A block of text worth reading in full - the command-line summary, the notes about formats.</summary>
public partial class TextWindow : Window
{
    #region Constructor
    public TextWindow(string title, string text)
    {
        InitializeComponent();

        Title = title;
        Body.Text = text;
    }
    #endregion

    #region Private methods
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
    #endregion
}
