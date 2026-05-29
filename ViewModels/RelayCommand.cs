using System;
using System.Windows.Input;

namespace Github_Trend;

// Minimal ICommand implementation for simple command bindings
public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<object?> _execute;

    private EventHandler? _canExecuteChangedHandlers;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke(parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute(parameter);
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