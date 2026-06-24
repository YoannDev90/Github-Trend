using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Serilog;

namespace Github_Trend;

public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, bool>? _canExecute;
    private readonly Func<object?, Task>? _executeAsync;
    private readonly Action<object?>? _executeSync;
    private int _isExecuting;

    private EventHandler? _canExecuteChangedHandlers;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _executeSync = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _executeAsync = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return (_canExecute?.Invoke(parameter) ?? true) && _isExecuting == 0;
    }

    public async void Execute(object? parameter)
    {
        if (Interlocked.Exchange(ref _isExecuting, 1) != 0)
            return;

        try
        {
            RaiseCanExecuteChanged();

            if (_executeAsync is not null)
                await _executeAsync(parameter);
            else
                _executeSync?.Invoke(parameter);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception in RelayCommand");
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => _canExecuteChangedHandlers += value;
        remove => _canExecuteChangedHandlers -= value;
    }

    public void RaiseCanExecuteChanged()
    {
        _canExecuteChangedHandlers?.Invoke(this, EventArgs.Empty);
    }
}
