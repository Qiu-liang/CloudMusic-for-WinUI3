using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using music.Models;

namespace music.Services
{
    public enum RepeatMode
    {
        None,
        All,
        One
    }

    public class PlaybackService
    {
        private readonly MediaPlayer _mediaPlayer;
        private readonly DispatcherQueue _dispatcherQueue;
        private List<Song> _playlist = new();
        private int _currentIndex = -1;
        private bool _isShuffleEnabled = false;
        private RepeatMode _repeatMode = RepeatMode.None;
        private readonly Random _random = new();
        private bool _isPlaying = false;

        public event EventHandler<bool>? PlaybackStateChanged;
        public event EventHandler<Song>? CurrentSongChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler<TimeSpan>? DurationChanged;
        public event EventHandler<double>? VolumeChanged;

        public bool IsPlaying => _isPlaying;
        public Song? CurrentSong => _currentIndex >= 0 && _currentIndex < _playlist.Count ? _playlist[_currentIndex] : null;
        public double Volume
        {
            get => _mediaPlayer?.Volume ?? 0.8;
            set
            {
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Volume = Math.Clamp(value, 0, 1);
                    VolumeChanged?.Invoke(this, _mediaPlayer.Volume);
                }
            }
        }

        public PlaybackService()
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.Volume = 0.8;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            _mediaPlayer.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            _mediaPlayer.PlaybackSession.PositionChanged += OnPositionChanged;
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
        }

        public void SetPlaylist(List<Song> songs, int startIndex = 0)
        {
            _playlist = songs ?? new List<Song>();
            _currentIndex = startIndex;
        }

        public async Task PlayAsync(Song song)
        {
            if (song == null) return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[Playback] Getting URL for: {song.Name} (ID: {song.Id})");
                var url = await App.ApiService.GetSongUrlAsync(song.Id);
                
                if (string.IsNullOrEmpty(url))
                {
                    System.Diagnostics.Debug.WriteLine($"[Playback] No URL for song: {song.Name}");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[Playback] Playing: {url}");

                _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        var source = MediaSource.CreateFromUri(new Uri(url));
                        _mediaPlayer.Source = source;
                        _mediaPlayer.Play();
                        _isPlaying = true;
                        CurrentSongChanged?.Invoke(this, song);
                        PlaybackStateChanged?.Invoke(this, true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Playback] Play error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Playback] Error: {ex.Message}");
            }
        }

        public async Task PlayAsync(List<Song> songs, int index = 0)
        {
            if (songs == null || songs.Count == 0) return;
            
            SetPlaylist(songs, index);
            if (index >= 0 && index < songs.Count)
            {
                await PlayAsync(songs[index]);
            }
        }

        public void Play()
        {
            if (_mediaPlayer?.Source != null)
            {
                _mediaPlayer.Play();
                _isPlaying = true;
                PlaybackStateChanged?.Invoke(this, true);
            }
        }

        public void Pause()
        {
            _mediaPlayer?.Pause();
            _isPlaying = false;
            PlaybackStateChanged?.Invoke(this, false);
        }

        public void TogglePlayPause()
        {
            if (_mediaPlayer?.Source == null && _playlist.Count > 0)
            {
                _ = PlayAsync(_playlist[Math.Max(0, _currentIndex)]);
                return;
            }

            if (_isPlaying)
                Pause();
            else
                Play();
        }

        public async Task NextAsync()
        {
            if (_playlist.Count == 0) return;

            int nextIndex;
            if (_isShuffleEnabled)
            {
                nextIndex = _random.Next(_playlist.Count);
            }
            else
            {
                nextIndex = (_currentIndex + 1) % _playlist.Count;
            }

            _currentIndex = nextIndex;
            await PlayAsync(_playlist[_currentIndex]);
        }

        public async Task PreviousAsync()
        {
            if (_playlist.Count == 0) return;

            int prevIndex;
            if (_isShuffleEnabled)
            {
                prevIndex = _random.Next(_playlist.Count);
            }
            else
            {
                prevIndex = (_currentIndex - 1 + _playlist.Count) % _playlist.Count;
            }

            _currentIndex = prevIndex;
            await PlayAsync(_playlist[_currentIndex]);
        }

        public void ToggleShuffle()
        {
            _isShuffleEnabled = !_isShuffleEnabled;
        }

        public void ToggleRepeat()
        {
            _repeatMode = _repeatMode switch
            {
                RepeatMode.None => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                RepeatMode.One => RepeatMode.None,
                _ => RepeatMode.None
            };
        }

        public RepeatMode GetRepeatMode() => _repeatMode;
        public bool IsShuffleEnabled => _isShuffleEnabled;

        public void Seek(TimeSpan position)
        {
            if (_mediaPlayer?.PlaybackSession != null)
            {
                _mediaPlayer.PlaybackSession.Position = position;
            }
        }

        public TimeSpan GetCurrentPosition()
        {
            return _mediaPlayer?.PlaybackSession?.Position ?? TimeSpan.Zero;
        }

        public TimeSpan GetDuration()
        {
            return _mediaPlayer?.PlaybackSession?.NaturalDuration ?? TimeSpan.Zero;
        }

        private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                var playing = sender.PlaybackState == MediaPlaybackState.Playing;
                _isPlaying = playing;
                PlaybackStateChanged?.Invoke(this, playing);
            });
        }

        private void OnPositionChanged(MediaPlaybackSession sender, object args)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                PositionChanged?.Invoke(this, sender.Position);
                DurationChanged?.Invoke(this, sender.NaturalDuration);
            });
        }

        private async void OnMediaEnded(MediaPlayer sender, object args)
        {
            System.Diagnostics.Debug.WriteLine($"[Playback] Media ended, repeat mode: {_repeatMode}");
            
            switch (_repeatMode)
            {
                case RepeatMode.One:
                    await PlayAsync(CurrentSong);
                    break;
                case RepeatMode.All:
                    await NextAsync();
                    break;
                case RepeatMode.None:
                    if (_currentIndex < _playlist.Count - 1)
                        await NextAsync();
                    else
                    {
                        _isPlaying = false;
                        _dispatcherQueue.TryEnqueue(() =>
                        {
                            PlaybackStateChanged?.Invoke(this, false);
                        });
                    }
                    break;
            }
        }

        private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"[Playback] Media failed: {args.Error} - {args.ErrorMessage}");
            _isPlaying = false;
            _dispatcherQueue.TryEnqueue(() =>
            {
                PlaybackStateChanged?.Invoke(this, false);
            });
        }

        public void Dispose()
        {
            _mediaPlayer?.Dispose();
        }
    }
}