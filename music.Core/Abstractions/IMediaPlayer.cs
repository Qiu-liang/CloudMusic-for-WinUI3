using System;
using System.Threading;
using System.Threading.Tasks;

namespace music.Core.Abstractions
{
    public interface IMediaPlayer : IDisposable
    {
        double Volume { get; set; }
        bool HasSource { get; }
        TimeSpan Position { get; set; }
        TimeSpan NaturalDuration { get; }

        event EventHandler<bool>? PlaybackStateChanged;
        event EventHandler<TimeSpan>? PositionChanged;
        event EventHandler? MediaEnded;
        event EventHandler<string>? MediaFailed;

        Task LoadUriAsync(string uri, CancellationToken cancellationToken);
        Task LoadLocalFileAsync(string filePath, CancellationToken cancellationToken);
        void Play();
        void Pause();
    }
}
