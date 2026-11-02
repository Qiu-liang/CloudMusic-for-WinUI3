using System;
using System.Collections.Generic;

namespace music.Services
{
    public sealed class PlaybackQueue<T>
    {
        private readonly List<T> _songs;
        private readonly Random _random;

        public PlaybackQueue(Random? random = null)
        {
            _songs = new List<T>();
            _random = random ?? new Random();
        }

        public int CurrentIndex { get; private set; } = -1;
        public bool IsShuffleEnabled { get; set; }
        public int Count => _songs.Count;
        public T? Current => CurrentIndex >= 0 && CurrentIndex < _songs.Count ? _songs[CurrentIndex] : default;

        public T SetSingle(T song)
        {
            _songs.Clear();
            _songs.Add(song);
            CurrentIndex = 0;
            return song;
        }

        public T SetPlaylist(IEnumerable<T> songs, int startIndex = 0)
        {
            var replacement = songs == null ? new List<T>() : new List<T>(songs);
            if (replacement.Count == 0)
                throw new ArgumentException("播放列表不能为空。", nameof(songs));
            if (startIndex < 0 || startIndex >= replacement.Count)
                throw new ArgumentOutOfRangeException(nameof(startIndex));

            _songs.Clear();
            _songs.AddRange(replacement);
            CurrentIndex = startIndex;
            return replacement[startIndex];
        }

        public T? MoveNext()
        {
            if (_songs.Count == 0) return default;
            CurrentIndex = IsShuffleEnabled ? _random.Next(_songs.Count) : (CurrentIndex + 1) % _songs.Count;
            return Current;
        }

        public T? MovePrevious()
        {
            if (_songs.Count == 0) return default;
            CurrentIndex = IsShuffleEnabled ? _random.Next(_songs.Count) : (CurrentIndex - 1 + _songs.Count) % _songs.Count;
            return Current;
        }

        public bool HasNext => CurrentIndex >= 0 && CurrentIndex < _songs.Count - 1;
        public IReadOnlyList<T> Snapshot() => new List<T>(_songs).AsReadOnly();
    }
}
