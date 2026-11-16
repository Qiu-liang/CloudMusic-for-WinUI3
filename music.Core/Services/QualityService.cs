using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Netease;
using music.Models;
using music.Services;

namespace music.Core.Services
{
    public interface IQualityService
    {
        SongQualityPrivilege? LastKnownPrivilege { get; }
        string CurrentQuality { get; }
        event EventHandler<string>? QualityChanged;

        void SetQuality(string quality);
        void CachePrivilege(string songId, SongQualityPrivilege? privilege);
        void ClearCache();
        Task<SongQualityPrivilege?> GetSongQualityPrivilegeAsync(
            string songId,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default);
    }

    public sealed class QualityService : IQualityService
    {
        private const string AudioQualityKey = "AudioQuality";
        private readonly NeteaseHttpTransport _transport;
        private readonly ISettingsStore _settings;
        private readonly object _privilegeLock = new();
        private readonly Dictionary<string, SongQualityPrivilege> _privilegeBySongId = new(StringComparer.Ordinal);
        private volatile SongQualityPrivilege? _lastKnownPrivilege;

        public QualityService(NeteaseHttpTransport transport, ISettingsStore settings)
        {
            _transport = transport;
            _settings = settings;
        }

        public SongQualityPrivilege? LastKnownPrivilege => _lastKnownPrivilege;

        public string CurrentQuality => _settings.GetString(AudioQualityKey) ?? "standard";

        public event EventHandler<string>? QualityChanged;

        public void SetQuality(string quality)
        {
            _settings.SetString(AudioQualityKey, quality);
            QualityChanged?.Invoke(this, quality);
        }

        public void CachePrivilege(string songId, SongQualityPrivilege? privilege)
        {
            if (privilege == null) return;
            _lastKnownPrivilege = privilege;
            if (string.IsNullOrWhiteSpace(songId)) return;
            lock (_privilegeLock)
            {
                _privilegeBySongId[songId] = privilege;
            }
        }

        public void ClearCache()
        {
            lock (_privilegeLock)
            {
                _privilegeBySongId.Clear();
            }
            _lastKnownPrivilege = null;
        }

        public async Task<SongQualityPrivilege?> GetSongQualityPrivilegeAsync(
            string songId,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(songId)) return _lastKnownPrivilege;

            if (!forceRefresh)
            {
                lock (_privilegeLock)
                {
                    if (_privilegeBySongId.TryGetValue(songId, out var cached))
                        return cached;
                }
            }

            try
            {
                var response = await _transport.GetAsync(
                    $"/song/detail?ids={Uri.EscapeDataString(songId)}",
                    cancellationToken).ConfigureAwait(false);

                if (response.Success)
                {
                    var privilege = SongQualityPrivilege.FromSongDetailJson(response.Body, songId);
                    if (privilege != null)
                    {
                        CachePrivilege(songId, privilege);
                        return privilege;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] GetSongQualityPrivilege Error: {ex.Message}");
            }

            return _lastKnownPrivilege;
        }
    }
}
