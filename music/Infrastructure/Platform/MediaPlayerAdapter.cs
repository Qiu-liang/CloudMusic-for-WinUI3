using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using music.Core.Abstractions;

namespace music.Infrastructure.Platform
{
    public sealed class MediaPlayerAdapter : IMediaPlayer
    {
        private readonly MediaPlayer _mediaPlayer;

        public event EventHandler<bool>? PlaybackStateChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? MediaEnded;
        public event EventHandler<string>? MediaFailed;

        public MediaPlayerAdapter()
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _mediaPlayer.PlaybackSession.PositionChanged += OnPositionChanged;
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
        }

        public double Volume
        {
            get => _mediaPlayer.Volume;
            set => _mediaPlayer.Volume = value;
        }

        public bool HasSource => _mediaPlayer.Source != null;

        public TimeSpan Position
        {
            get => _mediaPlayer.PlaybackSession?.Position ?? TimeSpan.Zero;
            set
            {
                if (_mediaPlayer.PlaybackSession != null)
                    _mediaPlayer.PlaybackSession.Position = value;
            }
        }

        public TimeSpan NaturalDuration => _mediaPlayer.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero;

        public Task LoadUriAsync(string uri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(uri));
            return Task.CompletedTask;
        }

        public async Task LoadLocalFileAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath).AsTask(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _mediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Playback] Local file API error: {ex.Message}");
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(filePath)) return;
                _mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(Path.GetFullPath(filePath)));
            }
        }

        public void Play() => _mediaPlayer.Play();
        public void Pause() => _mediaPlayer.Pause();

        private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args) =>
            PlaybackStateChanged?.Invoke(this, sender.PlaybackState == MediaPlaybackState.Playing);

        private void OnPositionChanged(MediaPlaybackSession sender, object args) =>
            PositionChanged?.Invoke(this, sender.Position);

        private void OnMediaEnded(MediaPlayer sender, object args) =>
            MediaEnded?.Invoke(this, EventArgs.Empty);

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            var detail = string.IsNullOrWhiteSpace(args.ErrorMessage)
                ? $"媒体加载失败（{args.Error}）。"
                : $"媒体加载失败：{args.ErrorMessage}";
            MediaFailed?.Invoke(this, detail);
        }

        public void Dispose()
        {
            _mediaPlayer.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
            _mediaPlayer.PlaybackSession.PositionChanged -= OnPositionChanged;
            _mediaPlayer.MediaEnded -= OnMediaEnded;
            _mediaPlayer.MediaFailed -= OnMediaFailed;
            _mediaPlayer.Dispose();
        }
    }
}
