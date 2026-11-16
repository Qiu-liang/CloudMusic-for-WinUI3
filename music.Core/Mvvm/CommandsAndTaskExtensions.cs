using System;
using System.Threading.Tasks;
using System.Windows.Input;
using music.Core.Abstractions;

namespace music.Core.Mvvm
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private readonly IApiLog? _log;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, IApiLog? log = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _log = log;
        }

        public event EventHandler? CanExecuteChanged;

        public bool IsExecuting => _isExecuting;

        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log?.Write($"AsyncRelayCommand error: {ex.Message}");
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public static class TaskExtensions
    {
        public static async void Forget(this Task task, IApiLog? log = null, string context = "")
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                log?.Write(string.IsNullOrEmpty(context)
                    ? $"Background task failed: {ex.Message}"
                    : $"{context} failed: {ex.Message}");
            }
        }
    }
}
