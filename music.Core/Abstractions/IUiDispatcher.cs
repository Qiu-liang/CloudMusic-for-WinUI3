using System;

namespace music.Core.Abstractions
{
    public interface IUiDispatcher
    {
        bool TryEnqueue(Action action);
    }
}
