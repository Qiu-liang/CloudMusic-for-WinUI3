using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Netease;

namespace music.Core.Services
{
    public interface IServerConnectionTester
    {
        Task<(bool Success, HttpStatusCode? StatusCode, string? ErrorMessage)> TestConnectionAsync(
            string address,
            CancellationToken cancellationToken = default);
    }

    public sealed class ServerConnectionTester : IServerConnectionTester, IDisposable
    {
        private readonly HttpClient _httpClient;

        public ServerConnectionTester(HttpMessageHandler? handler = null)
        {
            _httpClient = handler != null ? new HttpClient(handler) : new HttpClient();
        }

        public async Task<(bool Success, HttpStatusCode? StatusCode, string? ErrorMessage)> TestConnectionAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                var response = await _httpClient.GetAsync(address, cts.Token).ConfigureAwait(false);
                return (true, response.StatusCode, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public void Dispose() => _httpClient.Dispose();
    }

    public interface ICoverArtService
    {
        Task<byte[]?> DownloadImageBytesAsync(string url, CancellationToken cancellationToken = default);
    }

    public sealed class CoverArtService : ICoverArtService, IDisposable
    {
        private readonly HttpClient _httpClient;

        public CoverArtService(HttpMessageHandler? handler = null)
        {
            _httpClient = handler != null ? new HttpClient(handler) : new HttpClient();
        }

        public async Task<byte[]?> DownloadImageBytesAsync(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return await _httpClient.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => _httpClient.Dispose();
    }

    public interface ILocalMusicService
    {
        List<string> GetFolders();
        void AddFolder(string folderPath);
        List<LocalAudioFileEntry> ScanFolders();
    }

    public sealed class LocalMusicService : ILocalMusicService
    {
        private const string SettingsKey = "LocalMusicFolders";
        private readonly ISettingsStore _settings;

        public LocalMusicService(ISettingsStore settings)
        {
            _settings = settings;
        }

        public List<string> GetFolders()
        {
            var foldersJson = _settings.GetString(SettingsKey);
            if (string.IsNullOrEmpty(foldersJson)) return new List<string>();
            return JsonSerializer.Deserialize<List<string>>(foldersJson) ?? new List<string>();
        }

        public void AddFolder(string folderPath)
        {
            var folders = GetFolders();
            if (!folders.Contains(folderPath))
            {
                folders.Add(folderPath);
                _settings.SetString(SettingsKey, JsonSerializer.Serialize(folders));
            }
        }

        public List<LocalAudioFileEntry> ScanFolders()
        {
            var folders = GetFolders();
            var all = new List<LocalAudioFileEntry>();
            foreach (var folder in folders)
            {
                all.AddRange(AudioFileScanner.ScanDirectory(folder, System.IO.SearchOption.AllDirectories));
            }
            return all;
        }
    }

    public interface ISettingsService
    {
        string ServerAddress { get; }
        bool AllowInsecureLogin { get; set; }
        bool FancyLyrics { get; set; }
        string Theme { get; set; }
        void UpdateServerAddress(string address);
    }

    public sealed class SettingsService : ISettingsService
    {
        private const string AllowInsecureLoginKey = "AllowInsecureLogin";
        private const string FancyLyricsKey = "FancyLyrics";
        private const string ThemeKey = "Theme";

        private readonly ISettingsStore _settings;
        private readonly ServerEndpoint _endpoint;

        public SettingsService(ISettingsStore settings, ServerEndpoint endpoint)
        {
            _settings = settings;
            _endpoint = endpoint;
        }

        public string ServerAddress => _settings.GetString("ServerAddress") ?? ServerEndpoint.DefaultServerAddress;

        public bool AllowInsecureLogin
        {
            get => _settings.GetBoolean(AllowInsecureLoginKey);
            set => _settings.SetBoolean(AllowInsecureLoginKey, value);
        }

        public bool FancyLyrics
        {
            get => _settings.GetBoolean(FancyLyricsKey);
            set => _settings.SetBoolean(FancyLyricsKey, value);
        }

        public string Theme
        {
            get => _settings.GetString(ThemeKey) ?? "System";
            set => _settings.SetString(ThemeKey, value);
        }

        public void UpdateServerAddress(string address) => _endpoint.UpdateBaseUrl(address);
    }
}
