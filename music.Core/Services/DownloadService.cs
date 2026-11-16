using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Models;
using music.Services;

namespace music.Core.Services
{
    public sealed record LocalAudioFileEntry(
        string Id,
        string Name,
        string Artist,
        string FilePath,
        string Folder,
        long FileSize);

    public static class AudioFileScanner
    {
        private static readonly string[] SupportedExtensions =
            [".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a"];

        public static bool IsSupportedAudioFile(string path) =>
            SupportedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        public static List<LocalAudioFileEntry> ScanDirectory(string folder, SearchOption searchOption)
        {
            var entries = new List<LocalAudioFileEntry>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return entries;

            var files = Directory.GetFiles(folder, "*.*", searchOption)
                .Where(IsSupportedAudioFile);

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var fileName = Path.GetFileNameWithoutExtension(file);
                var folderName = Path.GetDirectoryName(file) ?? string.Empty;
                var parts = fileName.Split(" - ", 2);
                var artist = parts.Length > 1 ? parts[0].Trim() : "未知艺术家";
                var name = parts.Length > 1 ? parts[1].Trim() : fileName;

                entries.Add(new LocalAudioFileEntry(
                    fileInfo.Name,
                    name,
                    artist,
                    file,
                    Path.GetFileName(folderName),
                    fileInfo.Length));
            }

            return entries;
        }
    }

    public interface IDownloadService
    {
        string GetDefaultFolder();
        string GetCurrentFolder();
        string SaveFolder(string? folderPath);
        string ResetFolder();
        bool IsDefaultFolder(string? folderPath);
        Task<ApiResult> DownloadSongAsync(Song song, CancellationToken cancellationToken = default);
        List<LocalAudioFileEntry> ScanDownloadedFiles();
        void DeleteDownloadedFile(string fileName);
    }

    public sealed class DownloadService : IDownloadService, IDisposable
    {
        private readonly ISongSourceResolver _sourceResolver;
        private readonly ISettingsStore _settings;
        private readonly string _localAppFolder;
        private readonly HttpClient _httpClient;

        public DownloadService(
            ISongSourceResolver sourceResolver,
            ISettingsStore settings,
            string localAppFolder,
            HttpMessageHandler? handler = null)
        {
            _sourceResolver = sourceResolver;
            _settings = settings;
            _localAppFolder = localAppFolder;
            _httpClient = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false })
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        public string GetDefaultFolder() =>
            DownloadFolderPolicy.GetDefaultDownloadFolder(localAppFolder: _localAppFolder);

        public string GetCurrentFolder() =>
            DownloadFolderPolicy.GetCurrentDownloadFolder(_settings.AsDictionary(), _localAppFolder);

        public string SaveFolder(string? folderPath) =>
            DownloadFolderPolicy.SaveDownloadFolder(_settings.AsDictionary(), folderPath, _localAppFolder);

        public string ResetFolder() =>
            DownloadFolderPolicy.ResetDownloadFolder(_settings.AsDictionary(), _localAppFolder);

        public bool IsDefaultFolder(string? folderPath) =>
            DownloadFolderPolicy.IsDefaultDownloadFolder(folderPath, GetDefaultFolder());

        public List<LocalAudioFileEntry> ScanDownloadedFiles()
        {
            var folder = GetCurrentFolder();
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                return new List<LocalAudioFileEntry>();
            }

            return AudioFileScanner.ScanDirectory(folder, SearchOption.TopDirectoryOnly);
        }

        public void DeleteDownloadedFile(string fileName)
        {
            var folder = GetCurrentFolder();
            var filePath = Path.Combine(folder, fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public async Task<ApiResult> DownloadSongAsync(Song song, CancellationToken cancellationToken = default)
        {
            var (url, urlResult) = await _sourceResolver.ResolvePlaybackUrlAsync(song.Id, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(url))
            {
                return urlResult;
            }

            var folder = string.Empty;
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return new ApiResult(false, ApiErrorKind.Http, $"下载服务器返回 HTTP {(int)response.StatusCode}。", (int)response.StatusCode);

                folder = GetCurrentFolder();
                Directory.CreateDirectory(folder);
                var invalid = Path.GetInvalidFileNameChars();
                var baseName = new string($"{song.ArtistNames} - {song.Name}".Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
                if (string.IsNullOrWhiteSpace(baseName)) baseName = "download";
                var extension = Path.GetExtension(new Uri(url).AbsolutePath);
                if (string.IsNullOrEmpty(extension) || extension.Length > 5) extension = ".mp3";
                var destination = Path.Combine(folder, baseName + extension);
                var temporary = destination + ".download";
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var target = File.Create(temporary))
                    await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, destination, true);
                return new ApiResult(true, ApiErrorKind.None, destination);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException or NotSupportedException)
            {
                System.Diagnostics.Debug.WriteLine($"[API] DownloadSong File Error ({folder}): {ex.Message}");
                return new ApiResult(false, ApiErrorKind.Business, $"无法保存到下载目录（{folder}）：{ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] DownloadSong Error: {ex.Message}");
                return ApiResult.NetworkError();
            }
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
