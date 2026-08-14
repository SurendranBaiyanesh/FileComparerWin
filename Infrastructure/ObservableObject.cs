using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FileComparerWindows.Infrastructure;

/// <summary>Minimal change notification, so the window can bind to plain properties.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    #region Events

    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Protected methods

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    #endregion
}
