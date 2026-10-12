using System.Windows.Input;

namespace LogiWheelForge.Commands;

internal sealed class ParameterCommand<T>(Action<T> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => parameter is T;
    public void Execute(object? parameter) { if (parameter is T value) execute(value); }
}
