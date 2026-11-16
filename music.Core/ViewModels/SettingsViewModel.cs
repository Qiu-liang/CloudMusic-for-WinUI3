using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Netease;
using music.Core.Services;
using music.Models;
using music.Services;

namespace music.Core.ViewModels
{
    public enum StatusTone
    {
        Neutral,
        Success,
        Caution,
        Critical
    }

    public sealed class QualityOptionDisplay
    {
        public string Tag { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public double Opacity { get; init; } = 1.0;
    }

    public sealed class SettingsViewModel : PageViewModelBase
    {
        private static readonly string[] QualityTags =
            ["standard", "higher", "exhigh", "lossless", "hires", "jyeffect", "sky", "dolby", "jymaster"];

        private readonly ISettingsService _settingsService;
        private readonly IThemeService _themeService;
        private readonly IQualityService _qualityService;
        private readonly ISessionService _sessionService;
        private readonly IDownloadService _downloadService;
        private readonly IServerConnectionTester _connectionTester;
        private readonly IPlaybackCoordinator _playback;
        private readonly IDialogService _dialogs;
        private readonly IFilePickerService _filePicker;
        private readonly IUiDispatcher _dispatcher;

        private string _serverAddress = string.Empty;
        private string _serverStatusText = string.Empty;
        private StatusTone _serverStatusTone = StatusTone.Neutral;
        private bool _isTestingConnection;
        private bool _allowInsecureLogin;
        private int _themeIndex;
        private int _qualityIndex;
        private bool _isUpdatingQuality;
        private string _downloadFolder = string.Empty;
        private bool _canResetDownloadFolder;
        private string _downloadFolderStatusText = string.Empty;
        private StatusTone _downloadFolderStatusTone = StatusTone.Neutral;
        private bool _showDownloadFolderStatus;
        private string _clearLoginStatusText = string.Empty;
        private bool _showClearLoginStatus;
        private int _fancyLyricsIndex;
        private IReadOnlyList<QualityOptionDisplay> _qualityOptions = Array.Empty<QualityOptionDisplay>();

        public SettingsViewModel(
            ISettingsService settingsService,
            IThemeService themeService,
            IQualityService qualityService,
            ISessionService sessionService,
            IDownloadService downloadService,
            IServerConnectionTester connectionTester,
            IPlaybackCoordinator playback,
            IDialogService dialogs,
            IFilePickerService filePicker,
            IUiDispatcher dispatcher)
        {
            _settingsService = settingsService;
            _themeService = themeService;
            _qualityService = qualityService;
            _sessionService = sessionService;
            _downloadService = downloadService;
            _connectionTester = connectionTester;
            _playback = playback;
            _dialogs = dialogs;
            _filePicker = filePicker;
            _dispatcher = dispatcher;

            _qualityService.QualityChanged += OnQualityChanged;
            _playback.CurrentSongChanged += OnCurrentSongChanged;
        }

        public string ServerAddress { get => _serverAddress; set => SetProperty(ref _serverAddress, value); }
        public string ServerStatusText { get => _serverStatusText; private set => SetProperty(ref _serverStatusText, value); }
        public StatusTone ServerStatusTone { get => _serverStatusTone; private set => SetProperty(ref _serverStatusTone, value); }
        public bool IsTestingConnection { get => _isTestingConnection; private set => SetProperty(ref _isTestingConnection, value); }
        public bool CanTestConnection => !IsTestingConnection;
        public bool AllowInsecureLogin { get => _allowInsecureLogin; private set => SetProperty(ref _allowInsecureLogin, value); }
        public int ThemeIndex { get => _themeIndex; private set => SetProperty(ref _themeIndex, value); }
        public int QualityIndex { get => _qualityIndex; private set => SetProperty(ref _qualityIndex, value); }
        public IReadOnlyList<QualityOptionDisplay> QualityOptions { get => _qualityOptions; private set => SetProperty(ref _qualityOptions, value); }
        public string DownloadFolder { get => _downloadFolder; private set => SetProperty(ref _downloadFolder, value); }
        public bool CanResetDownloadFolder { get => _canResetDownloadFolder; private set => SetProperty(ref _canResetDownloadFolder, value); }
        public string DownloadFolderStatusText { get => _downloadFolderStatusText; private set => SetProperty(ref _downloadFolderStatusText, value); }
        public StatusTone DownloadFolderStatusTone { get => _downloadFolderStatusTone; private set => SetProperty(ref _downloadFolderStatusTone, value); }
        public bool ShowDownloadFolderStatus { get => _showDownloadFolderStatus; private set => SetProperty(ref _showDownloadFolderStatus, value); }
        public string ClearLoginStatusText { get => _clearLoginStatusText; private set => SetProperty(ref _clearLoginStatusText, value); }
        public bool ShowClearLoginStatus { get => _showClearLoginStatus; private set => SetProperty(ref _showClearLoginStatus, value); }
        public int FancyLyricsIndex { get => _fancyLyricsIndex; private set => SetProperty(ref _fancyLyricsIndex, value); }

        public void Load()
        {
            ThemeIndex = _themeService.CurrentTheme switch
            {
                "Light" => 1,
                "Dark" => 2,
                _ => 0
            };

            ServerAddress = _settingsService.ServerAddress;
            AllowInsecureLogin = _settingsService.AllowInsecureLogin;

            var storedQuality = _qualityService.CurrentQuality;
            var isVip = _sessionService.IsLoggedIn && _sessionService.GetVipStatus();
            var privilege = GetCurrentSongPrivilege();
            var quality = privilege != null
                ? privilege.ResolveFallbackQuality(storedQuality, isVip)
                : SongUrlPolicy.NormalizeQuality(storedQuality);
            if (!string.Equals(storedQuality, quality, StringComparison.Ordinal))
            {
                _qualityService.SetQuality(quality);
            }
            SyncQualitySelection(quality);

            SyncDownloadFolderState();
            FancyLyricsIndex = _settingsService.FancyLyrics ? 1 : 0;
        }

        public void SyncDownloadFolderState()
        {
            var current = _downloadService.GetCurrentFolder();
            DownloadFolder = current;
            CanResetDownloadFolder = !_downloadService.IsDefaultFolder(current);
        }

        public void SelectTheme(int index)
        {
            if (index < 0) return;
            var theme = index switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "System"
            };
            ThemeIndex = index;
            _themeService.ApplyTheme(theme);
        }

        public void SaveServerAddress()
        {
            var address = ServerAddress.Trim();
            if (string.IsNullOrEmpty(address))
            {
                ServerStatusText = "请输入服务器地址";
                ServerStatusTone = StatusTone.Critical;
                return;
            }

            if (!ServerEndpoint.TryNormalizeBaseUrl(address, out var normalized) ||
                !Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                ServerStatusText = "请输入有效的 HTTP 或 HTTPS 地址";
                ServerStatusTone = StatusTone.Critical;
                return;
            }

            ServerAddress = normalized;

            try
            {
                _settingsService.UpdateServerAddress(normalized);
            }
            catch (Exception ex)
            {
                ServerStatusText = $"保存失败：{ex.Message}";
                ServerStatusTone = StatusTone.Critical;
                return;
            }

            ServerStatusText = uri.Scheme == Uri.UriSchemeHttps
                ? "已保存并立即生效，登录凭据将通过 HTTPS 传输。"
                : "已保存并立即生效。警告：HTTP 为明文传输，请勿用于不可信网络；手机号密码登录仅允许本机 HTTP。";
            ServerStatusTone = uri.Scheme == Uri.UriSchemeHttps ? StatusTone.Success : StatusTone.Caution;
        }

        public async Task TestConnectionAsync()
        {
            var address = ServerAddress.Trim();
            if (string.IsNullOrEmpty(address))
            {
                ServerStatusText = "请先输入服务器地址";
                ServerStatusTone = StatusTone.Critical;
                return;
            }

            IsTestingConnection = true;
            OnPropertyChanged(nameof(CanTestConnection));
            ServerStatusText = "正在测试连接...";
            ServerStatusTone = StatusTone.Neutral;

            try
            {
                var (success, statusCode, errorMessage) = await _connectionTester.TestConnectionAsync(address);
                if (success)
                {
                    ServerStatusText = $"连接成功 (状态码: {statusCode})";
                    ServerStatusTone = StatusTone.Success;
                }
                else
                {
                    ServerStatusText = $"连接失败: {errorMessage}";
                    ServerStatusTone = StatusTone.Critical;
                }
            }
            finally
            {
                IsTestingConnection = false;
                OnPropertyChanged(nameof(CanTestConnection));
            }
        }

        public void SyncQualitySelection(string quality)
        {
            _isUpdatingQuality = true;
            try
            {
                var isVip = _sessionService.IsLoggedIn && _sessionService.GetVipStatus();
                var privilege = GetCurrentSongPrivilege();
                var options = new List<QualityOptionDisplay>(QualityTags.Length);
                foreach (var tag in QualityTags)
                {
                    var baseName = SongUrlPolicy.GetQualityDisplayName(tag);
                    var requiresVip = privilege?.RequiresVip(tag) ?? false;
                    var canPlay = privilege?.CanUserPlay(tag, isVip) ?? true;
                    options.Add(new QualityOptionDisplay
                    {
                        Tag = tag,
                        Text = requiresVip ? $"{baseName} (VIP)" : baseName,
                        Opacity = canPlay ? 1.0 : 0.55
                    });
                }
                QualityOptions = options;
                QualityIndex = Array.IndexOf(QualityTags, quality) is var idx && idx >= 0 ? idx : 0;
            }
            finally
            {
                _isUpdatingQuality = false;
            }
        }

        public async Task SelectQualityByIndexAsync(int index)
        {
            if (_isUpdatingQuality || index < 0 || index >= QualityTags.Length) return;

            var quality = QualityTags[index];
            var previousQuality = _qualityService.CurrentQuality;
            var isVip = _sessionService.IsLoggedIn && _sessionService.GetVipStatus();
            var privilege = await EnsureCurrentSongPrivilegeAsync();

            if (privilege != null && !privilege.CanUserPlay(quality, isVip))
            {
                var fallback = privilege.ResolveFallbackQuality(quality, isVip, previousQuality);
                _qualityService.SetQuality(fallback);
                SyncQualitySelection(fallback);

                await _dialogs.ShowMessageAsync(
                    "VIP 专属音质",
                    $"当前歌曲的“{SongUrlPolicy.GetQualityDisplayName(quality)}”音质需要 VIP 会员权限，已为您退回“{SongUrlPolicy.GetQualityDisplayName(fallback)}”音质。");
                return;
            }

            _qualityService.SetQuality(quality);
            SyncQualitySelection(quality);
        }

        public void ClearLogin()
        {
            _sessionService.ResetLogin();
            ClearLoginStatusText = "已清除本地登录状态";
            ShowClearLoginStatus = true;
        }

        public void SetAllowInsecureLogin(bool allowed)
        {
            AllowInsecureLogin = allowed;
            _settingsService.AllowInsecureLogin = allowed;
        }

        public void SetFancyLyrics(int index)
        {
            FancyLyricsIndex = index;
            _settingsService.FancyLyrics = index == 1;
        }

        public async Task SelectDownloadFolderAsync()
        {
            try
            {
                var picked = await _filePicker.PickFolderAsync();
                if (string.IsNullOrEmpty(picked)) return;

                var saved = _downloadService.SaveFolder(picked);
                SyncDownloadFolderState();
                DownloadFolderStatusText = $"下载目录已更新为：{saved}";
                DownloadFolderStatusTone = StatusTone.Success;
                ShowDownloadFolderStatus = true;
            }
            catch (Exception ex)
            {
                DownloadFolderStatusText = $"更改目录失败：{ex.Message}";
                DownloadFolderStatusTone = StatusTone.Critical;
                ShowDownloadFolderStatus = true;
            }
        }

        public async Task OpenDownloadFolderAsync()
        {
            var folder = _downloadService.GetCurrentFolder();
            try
            {
                var launched = await _filePicker.LaunchFolderAsync(folder);
                if (!launched)
                {
                    DownloadFolderStatusText = $"无法打开目录：{folder}";
                    DownloadFolderStatusTone = StatusTone.Critical;
                    ShowDownloadFolderStatus = true;
                }
            }
            catch (Exception ex)
            {
                DownloadFolderStatusText = $"打开目录失败：{ex.Message}";
                DownloadFolderStatusTone = StatusTone.Critical;
                ShowDownloadFolderStatus = true;
            }
        }

        public void ResetDownloadFolder()
        {
            var defaultFolder = _downloadService.ResetFolder();
            SyncDownloadFolderState();
            DownloadFolderStatusText = $"已恢复默认下载目录：{defaultFolder}";
            DownloadFolderStatusTone = StatusTone.Neutral;
            ShowDownloadFolderStatus = true;
        }

        private SongQualityPrivilege? GetCurrentSongPrivilege() =>
            _playback.CurrentSong?.QualityPrivilege ?? _qualityService.LastKnownPrivilege;

        private async Task<SongQualityPrivilege?> EnsureCurrentSongPrivilegeAsync()
        {
            var song = _playback.CurrentSong;
            if (song != null && string.IsNullOrEmpty(song.LocalFilePath) && !string.IsNullOrWhiteSpace(song.Id))
            {
                if (song.QualityPrivilege == null)
                {
                    song.QualityPrivilege = await _qualityService.GetSongQualityPrivilegeAsync(song.Id);
                }
                return song.QualityPrivilege ?? _qualityService.LastKnownPrivilege;
            }
            return _qualityService.LastKnownPrivilege;
        }

        private void OnQualityChanged(object? sender, string quality)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (!IsDisposed) SyncQualitySelection(quality);
            });
        }

        private void OnCurrentSongChanged(object? sender, Song song)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (!IsDisposed) SyncQualitySelection(_qualityService.CurrentQuality);
            });
        }

        public override void Dispose()
        {
            _qualityService.QualityChanged -= OnQualityChanged;
            _playback.CurrentSongChanged -= OnCurrentSongChanged;
            base.Dispose();
        }
    }
}
