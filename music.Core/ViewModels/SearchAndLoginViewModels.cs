using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Services;
using music.Core.ViewModels.Items;
using music.Models;
using music.Services;

namespace music.Core.ViewModels
{
    public sealed class SearchViewModel : PageViewModelBase
    {
        private readonly ISearchService _searchService;
        private readonly IPlaybackCoordinator _playback;
        private readonly INavigationService _navigation;
        private List<Song> _songModels = new();
        private string _currentQuery = string.Empty;
        private string _title = "搜索";

        public SearchViewModel(
            ISearchService searchService,
            IPlaybackCoordinator playback,
            INavigationService navigation)
        {
            _searchService = searchService;
            _playback = playback;
            _navigation = navigation;
            Songs = new ObservableCollection<SongItem>();
            Artists = new ObservableCollection<ArtistItem>();
            Playlists = new ObservableCollection<PlaylistItem>();
            Albums = new ObservableCollection<AlbumItem>();
        }

        public ObservableCollection<SongItem> Songs { get; }
        public ObservableCollection<ArtistItem> Artists { get; }
        public ObservableCollection<PlaylistItem> Playlists { get; }
        public ObservableCollection<AlbumItem> Albums { get; }

        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public bool HasArtists => Artists.Count > 0;
        public bool HasSongs => Songs.Count > 0;
        public bool HasPlaylists => Playlists.Count > 0;
        public bool HasAlbums => Albums.Count > 0;

        public async Task LoadAsync(string? query)
        {
            var (version, token) = BeginLoad();
            if (string.IsNullOrWhiteSpace(query))
            {
                _currentQuery = string.Empty;
                ShowEmpty();
                return;
            }

            _currentQuery = query;
            Title = $"搜索: {query}";
            ShowLoading();

            try
            {
                var artistsResult = await _searchService.SearchArtistsAsync(query, 10);
                if (!IsCurrentLoad(version, token)) return;
                var songsResult = await _searchService.SearchSongsAsync(query, 39);
                if (!IsCurrentLoad(version, token)) return;
                var playlistsResult = await _searchService.SearchPlaylistsAsync(query, 20);
                if (!IsCurrentLoad(version, token)) return;
                var albumsResult = await _searchService.SearchAlbumsAsync(query, 20);
                if (!IsCurrentLoad(version, token)) return;

                var artists = artistsResult.Value;
                var songs = songsResult.Value;
                var playlists = playlistsResult.Value;
                var albums = albumsResult.Value;

                var firstFailure = new[]
                {
                    artistsResult.Status,
                    songsResult.Status,
                    playlistsResult.Status,
                    albumsResult.Status
                }.FirstOrDefault(s => !s.Success);

                Artists.Clear();
                foreach (var artist in artists)
                {
                    Artists.Add(new ArtistItem
                    {
                        Id = artist.Id,
                        Name = artist.Name,
                        PicUrl = artist.PicUrl,
                        SongCount = artist.SongCount
                    });
                }

                _songModels = songs;
                Songs.Clear();
                var limitedSongs = songs.Count > 39 ? songs.GetRange(0, 39) : songs;
                foreach (var item in SongItemFactory.FromSongs(limitedSongs))
                {
                    Songs.Add(item);
                }

                Playlists.Clear();
                foreach (var playlist in playlists)
                {
                    Playlists.Add(new PlaylistItem
                    {
                        Id = playlist.Id,
                        Name = playlist.Name,
                        PicUrl = playlist.PicUrl,
                        PlayCount = playlist.PlayCount,
                        TrackCount = playlist.TrackCount,
                        PlayCountFormatted = playlist.PlayCountFormatted
                    });
                }

                Albums.Clear();
                foreach (var album in albums)
                {
                    Albums.Add(new AlbumItem
                    {
                        Id = album.Id,
                        Name = album.Name,
                        PicUrl = album.PicUrl,
                        ArtistName = album.ArtistName,
                        PublishTime = album.PublishTime,
                        Size = album.Size
                    });
                }

                OnPropertyChanged(nameof(HasArtists));
                OnPropertyChanged(nameof(HasSongs));
                OnPropertyChanged(nameof(HasPlaylists));
                OnPropertyChanged(nameof(HasAlbums));

                if (Artists.Count == 0 && Songs.Count == 0 && Playlists.Count == 0 && Albums.Count == 0)
                {
                    if (firstFailure != null)
                    {
                        Title = $"搜索 \"{query}\" 失败";
                        ShowError(firstFailure.DisplayMessage);
                    }
                    else
                    {
                        Title = $"未找到 \"{query}\" 的结果";
                        ShowEmpty();
                    }
                }
                else
                {
                    ShowContent();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Search] Error: {ex.Message}");
                if (IsCurrentLoad(version, token))
                {
                    Title = $"搜索 \"{query}\" 失败";
                    ShowError(ex.Message);
                }
            }
        }

        public Task PlaySongAtAsync(int index)
        {
            return index >= 0 && index < _songModels.Count
                ? _playback.PlayAsync(_songModels[index])
                : Task.CompletedTask;
        }

        public void OpenAllSongs() => _navigation.Navigate(PageKey.SearchAllSongs, _currentQuery);
        public void OpenArtist(ArtistItem? artist) { if (artist != null) _navigation.Navigate(PageKey.ArtistDetail, artist.Id); }
        public void OpenPlaylist(PlaylistItem? playlist) { if (playlist != null) _navigation.Navigate(PageKey.PlaylistDetail, playlist.Id); }
        public void OpenAlbum(AlbumItem? album) { if (album != null) _navigation.Navigate(PageKey.PlaylistDetail, $"album_{album.Id}"); }
        public void OpenAllPlaylists() => _navigation.Navigate(PageKey.SearchAllResults, new SearchAllResultsParams { Type = "playlist", Keywords = _currentQuery });
        public void OpenAllAlbums() => _navigation.Navigate(PageKey.SearchAllResults, new SearchAllResultsParams { Type = "album", Keywords = _currentQuery });
    }

    public sealed class LoginViewModel : ObservableObject, IDisposable
    {
        private readonly ISessionService _sessionService;
        private readonly IApiLog _log;
        private CancellationTokenSource? _qrCts;
        private int _qrGeneration;
        private bool _isClosed;
        private bool _isQrLoginActive;
        private string _qrKey = string.Empty;

        private string _phoneError = string.Empty;
        private bool _hasPhoneError;
        private string _qrStatusText = "正在生成二维码...";
        private bool _isQrLoading;
        private bool _showRefreshQr;
        private byte[]? _qrImageBytes;

        public LoginViewModel(ISessionService sessionService, IApiLog log)
        {
            _sessionService = sessionService;
            _log = log;
        }

        public event EventHandler? LoginSucceeded;

        public string PhoneError { get => _phoneError; private set => SetProperty(ref _phoneError, value); }
        public bool HasPhoneError { get => _hasPhoneError; private set => SetProperty(ref _hasPhoneError, value); }
        public string QrStatusText { get => _qrStatusText; private set => SetProperty(ref _qrStatusText, value); }
        public bool IsQrLoading { get => _isQrLoading; private set => SetProperty(ref _isQrLoading, value); }
        public bool ShowRefreshQr { get => _showRefreshQr; private set => SetProperty(ref _showRefreshQr, value); }
        public byte[]? QrImageBytes { get => _qrImageBytes; private set => SetProperty(ref _qrImageBytes, value); }
        public bool IsQrLoginActive => _isQrLoginActive;

        public async Task<(bool Success, string? ErrorMessage)> LoginWithPhoneAsync(string phone, string password)
        {
            phone = phone.Trim();
            if (string.IsNullOrEmpty(phone))
            {
                SetPhoneError("请输入手机号码");
                return (false, PhoneError);
            }
            if (string.IsNullOrEmpty(password))
            {
                SetPhoneError("请输入密码");
                return (false, PhoneError);
            }

            try
            {
                var (success, message, code, _) = await _sessionService.LoginWithPhoneAsync(phone, password);
                if (success)
                {
                    HasPhoneError = false;
                    PhoneError = string.Empty;
                    return (true, null);
                }

                SetPhoneError($"{message} (错误代码: {code})");
                return (false, PhoneError);
            }
            catch (Exception ex)
            {
                SetPhoneError($"登录出错: {ex.Message}");
                return (false, PhoneError);
            }
        }

        public void StopQrPolling()
        {
            _isQrLoginActive = false;
            var cts = _qrCts;
            _qrCts = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                cts.Dispose();
            }
        }

        public async Task GenerateQrCodeAsync()
        {
            StopQrPolling();
            var generation = ++_qrGeneration;
            var cts = new CancellationTokenSource();
            _qrCts = cts;
            var token = cts.Token;

            try
            {
                IsQrLoading = true;
                QrStatusText = "正在生成二维码...";
                ShowRefreshQr = false;

                var payload = await _sessionService.GenerateQrCodeAsync(token);
                if (_isClosed || generation != _qrGeneration || token.IsCancellationRequested) return;

                if (payload != null)
                {
                    _qrKey = payload.Unikey;
                    QrImageBytes = Convert.FromBase64String(payload.Base64Image);
                    QrStatusText = "请使用网易云音乐APP扫码登录";
                    IsQrLoading = false;

                    _isQrLoginActive = true;
                    PollQrStatusAsync(generation, token).Forget(_log, "Login.QrPoll");
                    return;
                }

                QrStatusText = "生成二维码失败";
                IsQrLoading = false;
                ShowRefreshQr = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!_isClosed && generation == _qrGeneration)
                {
                    QrStatusText = $"生成二维码出错: {ex.Message}";
                    IsQrLoading = false;
                    ShowRefreshQr = true;
                }
            }
        }

        private async Task PollQrStatusAsync(int generation, CancellationToken token)
        {
            var consecutiveFailures = 0;

            while (_isQrLoginActive && !_isClosed && generation == _qrGeneration && !token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, token);
                    if (!_isQrLoginActive || _isClosed || generation != _qrGeneration || token.IsCancellationRequested) break;

                    var statusResult = await _sessionService.CheckQrStatusAsync(_qrKey, token);
                    if (!_isQrLoginActive || _isClosed || generation != _qrGeneration || token.IsCancellationRequested) break;

                    if (!statusResult.Success)
                    {
                        throw new InvalidOperationException(statusResult.Status.DisplayMessage);
                    }

                    consecutiveFailures = 0;
                    var status = statusResult.Value;

                    switch (status.State)
                    {
                        case QrLoginState.Expired:
                            QrStatusText = "二维码已过期，请刷新";
                            ShowRefreshQr = true;
                            _isQrLoginActive = false;
                            break;
                        case QrLoginState.Waiting:
                            QrStatusText = "等待扫码...";
                            break;
                        case QrLoginState.Scanned:
                            QrStatusText = "已扫码，等待确认...";
                            break;
                        case QrLoginState.Confirmed:
                            _isQrLoginActive = false;
                            await CompleteQrLoginAsync(status.Cookie);
                            break;
                        default:
                            QrStatusText = $"登录校验失败（{status.RawCode}），请点击刷新重试";
                            ShowRefreshQr = true;
                            _isQrLoginActive = false;
                            break;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!_isQrLoginActive || _isClosed || generation != _qrGeneration) break;

                    consecutiveFailures++;
                    System.Diagnostics.Debug.WriteLine($"[QR] 状态轮询失败({consecutiveFailures}): {ex.Message}");

                    if (consecutiveFailures >= 5)
                    {
                        QrStatusText = "无法连接服务器，请检查 API 地址和网络";
                        ShowRefreshQr = true;
                        _isQrLoginActive = false;
                    }
                    else
                    {
                        QrStatusText = "网络异常，正在重试...";
                    }
                }
            }
        }

        private async Task CompleteQrLoginAsync(string cookieStr)
        {
            QrStatusText = "登录成功，正在获取用户信息...";

            if (string.IsNullOrEmpty(cookieStr))
            {
                QrStatusText = "授权成功，但服务器未返回登录 Cookie，请刷新重试";
                ShowRefreshQr = true;
                return;
            }

            var loggedIn = await _sessionService.SetLoginCookieAsync(cookieStr);
            if (_isClosed) return;
            if (!loggedIn)
            {
                QrStatusText = "授权成功，但获取用户信息失败，请点击刷新重试";
                ShowRefreshQr = true;
                return;
            }

            QrStatusText = "登录成功！";
            await Task.Delay(300);
            if (!_isClosed) LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }

        private void SetPhoneError(string message)
        {
            PhoneError = message;
            HasPhoneError = true;
        }

        public void Dispose()
        {
            _isClosed = true;
            StopQrPolling();
        }
    }
}
