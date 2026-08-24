using System.Windows;
using System.Windows.Threading;

namespace FileComparerWindows;

public partial class App : Application
{
    #region Protected methods
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        MainWindow window = new MainWindow(e.Args);
        MainWindow = window;
        window.Show();
    }
    #endregion

    #region Private methods
    /// <summary>
    /// A comparison that goes wrong is reported in the window; this catches everything else, so that a
    /// fault in the interface itself does not close the window and lose the run behind it.
    /// </summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"{e.Exception.Message}\n\n{e.Exception.GetType().Name}",
            "FileComparerWindows",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Environment.ExitCode = 2;
        e.Handled = true;
    }
    #endregion
}
