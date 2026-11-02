using System.Threading;

namespace music.Services
{
    public sealed class LatestLyricRequest
    {
        private int _version;
        private string _songId = string.Empty;

        public int Next(string songId)
        {
            _songId = songId;
            return Interlocked.Increment(ref _version);
        }

        public bool IsCurrent(int version, string songId, string currentSongId) =>
            version == Volatile.Read(ref _version) && songId == Volatile.Read(ref _songId) && songId == currentSongId;
    }
}
