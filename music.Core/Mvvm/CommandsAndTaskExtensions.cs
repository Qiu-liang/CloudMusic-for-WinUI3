using System;
using System.Threading;
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
        private readonly IUiDispatcher? _dispatcher;
        private readonly SynchronizationContext? _creatorSyncContext;
        private bool _isExecuting;

        public AsyncRelayCommand(
            Func<Task> execute,
            Func<bool>? canExecute = null,
            IApiLog? log = null,
            IUiDispatcher? dispatcher = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _log = log;
            _dispatcher = dispatcher;
            _creatorSyncContext = SynchronizationContext.Current;
        }

        public event EventHandler? CanExecuteChanged;

        public bool IsExecuting => _isExecuting;

        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            var callerSyncContext = SynchronizationContext.Current ?? _creatorSyncContext;
            _isExecuting = true;
            RaiseCanExecuteChanged(callerSyncContext);
            try
            {
                await _execute();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log?.Write($"AsyncRelayCommand error: {ex}");
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged(callerSyncContext);
            }
        }

        public void RaiseCanExecuteChanged() => RaiseCanExecuteChanged(_creatorSyncContext);

        private void RaiseCanExecuteChanged(SynchronizationContext? targetSyncContext)
        {
            var handler = CanExecuteChanged;
            if (handler == null) return;

            var current = SynchronizationContext.Current;
            if (current != null && (targetSyncContext == null || ReferenceEquals(current, targetSyncContext)))
            {
                handler(this, EventArgs.Empty);
                return;
            }

            if (_dispatcher != null)
            {
                _dispatcher.TryEnqueue(() => handler(this, EventArgs.Empty));
                return;
            }

            if (targetSyncContext != null)
            {
                targetSyncContext.Post(_ => handler(this, EventArgs.Empty), null);
                return;
            }

            handler(this, EventArgs.Empty);
        }
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
                    ? $"Background task failed: {ex}"
                    : $"{context} failed: {ex}");
            }
        }
    }
}
