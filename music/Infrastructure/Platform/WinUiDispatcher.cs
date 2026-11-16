using System;
using Microsoft.UI.Dispatching;
using music.Core.Abstractions;

namespace music.Infrastructure.Platform
{
    public sealed class WinUiDispatcher : IUiDispatcher
    {
        private readonly DispatcherQueue _queue;

        public WinUiDispatcher(DispatcherQueue queue)
        {
            _queue = queue;
        }

        public bool TryEnqueue(Action action) => _queue.TryEnqueue(() => action());
    }
}
