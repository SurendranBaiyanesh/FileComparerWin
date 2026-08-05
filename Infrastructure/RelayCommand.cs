using System.Windows.Input;

namespace FileComparerWindows.Infrastructure;

/// <summary>An <see cref="ICommand"/> over a method, with an optional guard the view model re-asks about.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute is null || canExecute();

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
