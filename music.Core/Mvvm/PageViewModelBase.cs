using System;
using System.Threading;

namespace music.Core.Mvvm
{
    public enum LoadState
    {
        Loading,
        Content,
        Empty,
        Error
    }

    public abstract class PageViewModelBase : ObservableObject, IDisposable
    {
        private LoadState _state = LoadState.Loading;
        private string _errorMessage = string.Empty;
        private CancellationTokenSource? _loadCts;
        private long _loadVersion;
        private bool _disposed;

        public LoadState State
        {
            get => _state;
            protected set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(IsLoading));
                    OnPropertyChanged(nameof(IsContent));
                    OnPropertyChanged(nameof(IsEmpty));
                    OnPropertyChanged(nameof(IsError));
                }
            }
        }

        public bool IsLoading => State == LoadState.Loading;
        public bool IsContent => State == LoadState.Content;
        public bool IsEmpty => State == LoadState.Empty;
        public bool IsError => State == LoadState.Error;

        public string ErrorMessage
        {
            get => _errorMessage;
            protected set => SetProperty(ref _errorMessage, value);
        }

        protected bool IsDisposed => _disposed;

        protected (long Version, CancellationToken Token) BeginLoad()
        {
            CancelCurrentLoad();
            _loadCts = new CancellationTokenSource();
            var version = Interlocked.Increment(ref _loadVersion);
            return (version, _loadCts.Token);
        }

        protected bool IsCurrentLoad(long version, CancellationToken token) =>
            !_disposed && !token.IsCancellationRequested && version == Volatile.Read(ref _loadVersion);

        protected void ShowLoading()
        {
            ErrorMessage = string.Empty;
            State = LoadState.Loading;
        }

        protected void ShowContent()
        {
            ErrorMessage = string.Empty;
            State = LoadState.Content;
        }

        protected void ShowEmpty()
        {
            ErrorMessage = string.Empty;
            State = LoadState.Empty;
        }

        protected void ShowError(string message)
        {
            ErrorMessage = message;
            State = LoadState.Error;
        }

        private void CancelCurrentLoad()
        {
            var cts = _loadCts;
            _loadCts = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                cts.Dispose();
            }
        }

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Increment(ref _loadVersion);
            CancelCurrentLoad();
        }
    }
}
