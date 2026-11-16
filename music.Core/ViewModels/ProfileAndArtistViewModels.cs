using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Services;
using music.Core.ViewModels.Items;

namespace music.Core.ViewModels
{
    public sealed class ProfileViewModel : PageViewModelBase
    {
        private readonly IProfileService _profileService;
        private readonly ISessionService _sessionService;
        private readonly INavigationService _navigation;
        private readonly IPageRequestFactory _requestFactory;

        private string _nickname = string.Empty;
        private string _avatarUrl = string.Empty;
        private bool _isVip;
        private bool _showStats;
        private string _statsText = string.Empty;
        private bool _showSignature;
        private string _signature = string.Empty;
        private bool _showForceLogout;

        public ProfileViewModel(
            IProfileService profileService,
            ISessionService sessionService,
            INavigationService navigation,
            IPageRequestFactory requestFactory)
        {
            _profileService = profileService;
            _sessionService = sessionService;
            _navigation = navigation;
            _requestFactory = requestFactory;
            ForceLogoutCommand = new RelayCommand(ForceLogout);
        }

        public NavigationRequest CreateChildRequest(PageKey key, object? parameter = null) =>
            _requestFactory.Create(key, parameter);

        public RelayCommand ForceLogoutCommand { get; }

        public string Nickname { get => _nickname; private set => SetProperty(ref _nickname, value); }
        public string AvatarUrl
        {
            get => _avatarUrl;
            private set
            {
                if (SetProperty(ref _avatarUrl, value))
                    OnPropertyChanged(nameof(HasAvatar));
            }
        }
        public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);
        public bool IsVip { get => _isVip; private set => SetProperty(ref _isVip, value); }
        public bool ShowStats { get => _showStats; private set => SetProperty(ref _showStats, value); }
        public string StatsText { get => _statsText; private set => SetProperty(ref _statsText, value); }
        public bool ShowSignature { get => _showSignature; private set => SetProperty(ref _showSignature, value); }
        public string Signature { get => _signature; private set => SetProperty(ref _signature, value); }
        public bool ShowForceLogout { get => _showForceLogout; private set => SetProperty(ref _showForceLogout, value); }

        public async Task LoadAsync()
        {
            var (version, token) = BeginLoad();
            if (!_sessionService.IsLoggedIn)
            {
                Nickname = "未登录";
                StatsText = "请先登录";
                ShowStats = true;
                IsVip = false;
                ShowContent();
                return;
            }

            ShowLoading();

            try
            {
                var userInfo = await _profileService.GetUserDetailAsync(_sessionService.UserId);
                if (!IsCurrentLoad(version, token)) return;

                if (userInfo != null)
                {
                    Nickname = userInfo.Nickname;
                    AvatarUrl = userInfo.AvatarUrl;
                    StatsText = $"关注 {userInfo.FollowCount}  |  粉丝 {userInfo.FollowedCount}  |  动态 {userInfo.EventCount}";
                    ShowStats = true;

                    if (!string.IsNullOrEmpty(userInfo.Signature))
                    {
                        Signature = userInfo.Signature;
                        ShowSignature = true;
                    }
                }

                IsVip = await _sessionService.CheckVipStatusAsync();
                if (!IsCurrentLoad(version, token)) return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profile] LoadError: {ex.Message}");
            }

            if (string.IsNullOrEmpty(Nickname))
            {
                Nickname = "无法加载用户信息";
                StatsText = "登录可能已失效，可退出登录后重新扫码";
                ShowStats = true;
                ShowSignature = false;
                ShowForceLogout = true;
            }

            ShowContent();
        }

        private void ForceLogout()
        {
            _sessionService.ResetLogin();
            if (_navigation.CurrentPage == PageKey.Profile)
            {
                _navigation.Navigate(PageKey.Home);
            }
        }
    }

    public sealed class ProfilePlaylistsViewModel : PageViewModelBase
    {
        private readonly IPlaylistService _playlistService;
        private readonly ISessionService _sessionService;
        private readonly INavigationService _navigation;

        public ProfilePlaylistsViewModel(
            IPlaylistService playlistService,
            ISessionService sessionService,
            INavigationService navigation)
        {
            _playlistService = playlistService;
            _sessionService = sessionService;
            _navigation = navigation;
            CreatedPlaylists = new ObservableCollection<PlaylistItem>();
            CollectedPlaylists = new ObservableCollection<PlaylistItem>();
        }

        public ObservableCollection<PlaylistItem> CreatedPlaylists { get; }
        public ObservableCollection<PlaylistItem> CollectedPlaylists { get; }

        public bool HasCreated => CreatedPlaylists.Count > 0;
        public bool HasCollected => CollectedPlaylists.Count > 0;

        public async Task LoadAsync()
        {
            var (version, token) = BeginLoad();
            if (!_sessionService.IsLoggedIn) return;

            ShowLoading();

            var allPlaylists = await _playlistService.GetUserPlaylistsAsync(_sessionService.UserId);
            if (!IsCurrentLoad(version, token)) return;

            var created = new List<PlaylistItem>();
            var collected = new List<PlaylistItem>();

            foreach (var p in allPlaylists)
            {
                var item = new PlaylistItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    PicUrl = p.CoverImgUrl,
                    TrackCount = p.TrackCount,
                    PlayCount = p.PlayCount,
                    PlayCountFormatted = p.PlayCountFormatted
                };

                if (p.CreatorId == _sessionService.UserId)
                    created.Add(item);
                else
                    collected.Add(item);
            }

            CreatedPlaylists.Clear();
            foreach (var item in created) CreatedPlaylists.Add(item);
            CollectedPlaylists.Clear();
            foreach (var item in collected) CollectedPlaylists.Add(item);

            OnPropertyChanged(nameof(HasCreated));
            OnPropertyChanged(nameof(HasCollected));

            if (CreatedPlaylists.Count == 0 && CollectedPlaylists.Count == 0)
                ShowEmpty();
            else
                ShowContent();
        }

        public void OpenPlaylist(PlaylistItem? playlist)
        {
            if (playlist != null)
                _navigation.Navigate(PageKey.PlaylistDetail, playlist.Id);
        }
    }

    public sealed class ProfileFollowListViewModel : PageViewModelBase
    {
        private readonly IProfileService _profileService;
        private readonly ISessionService _sessionService;
        private readonly INavigationService _navigation;
        private string _emptyText = "暂无数据";

        public ProfileFollowListViewModel(
            IProfileService profileService,
            ISessionService sessionService,
            INavigationService navigation)
        {
            _profileService = profileService;
            _sessionService = sessionService;
            _navigation = navigation;
            Users = new ObservableCollection<FollowUserItem>();
        }

        public ObservableCollection<FollowUserItem> Users { get; }

        public string EmptyText
        {
            get => _emptyText;
            private set => SetProperty(ref _emptyText, value);
        }

        public async Task LoadAsync(bool isFollowsMode)
        {
            var (version, token) = BeginLoad();
            if (!_sessionService.IsLoggedIn)
            {
                EmptyText = "请先登录";
                ShowEmpty();
                return;
            }

            ShowLoading();

            var users = isFollowsMode
                ? await _profileService.GetFollowsAsync(_sessionService.UserId)
                : await _profileService.GetFollowedsAsync(_sessionService.UserId);
            if (!IsCurrentLoad(version, token)) return;

            Users.Clear();
            foreach (var u in users)
            {
                Users.Add(new FollowUserItem
                {
                    UserId = u.UserId,
                    Nickname = u.Nickname,
                    AvatarUrl = u.AvatarUrl,
                    Signature = u.Signature
                });
            }

            if (Users.Count == 0)
            {
                EmptyText = isFollowsMode ? "暂无关注" : "暂无粉丝";
                ShowEmpty();
            }
            else
            {
                ShowContent();
            }
        }

        public void OpenUser(long userId)
        {
            _navigation.Navigate(PageKey.ArtistDetail, userId.ToString());
        }
    }

    public sealed class ArtistDetailViewModel : PageViewModelBase
    {
        private readonly IArtistService _artistService;
        private readonly IProfileService _profileService;
        private readonly IPlaybackCoordinator _playback;
        private readonly IPageRequestFactory _requestFactory;

        private string _title = "歌手详情";
        private string _artistName = string.Empty;
        private string _imageUrl = string.Empty;
        private bool _showAlias;
        private string _aliasText = string.Empty;
        private bool _showDescription;
        private string _description = string.Empty;
        private bool _showNav;
        private bool _showPlayAll;

        public ArtistDetailViewModel(
            IArtistService artistService,
            IProfileService profileService,
            IPlaybackCoordinator playback,
            IPageRequestFactory requestFactory)
        {
            _artistService = artistService;
            _profileService = profileService;
            _playback = playback;
            _requestFactory = requestFactory;
        }

        public NavigationRequest CreateChildRequest(PageKey key, object? parameter = null) =>
            _requestFactory.Create(key, parameter);

        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string ArtistName { get => _artistName; private set => SetProperty(ref _artistName, value); }
        public string ImageUrl
        {
            get => _imageUrl;
            private set
            {
                if (SetProperty(ref _imageUrl, value))
                    OnPropertyChanged(nameof(HasImage));
            }
        }
        public bool HasImage => !string.IsNullOrEmpty(ImageUrl);
        public bool ShowAlias { get => _showAlias; private set => SetProperty(ref _showAlias, value); }
        public string AliasText { get => _aliasText; private set => SetProperty(ref _aliasText, value); }
        public bool ShowDescription { get => _showDescription; private set => SetProperty(ref _showDescription, value); }
        public string Description { get => _description; private set => SetProperty(ref _description, value); }
        public bool ShowNav { get => _showNav; private set => SetProperty(ref _showNav, value); }
        public bool ShowPlayAll { get => _showPlayAll; set => SetProperty(ref _showPlayAll, value); }

        public async Task LoadAsync(string artistId)
        {
            var (version, token) = BeginLoad();
            ShowLoading();

            try
            {
                var info = await _artistService.GetArtistDetailAsync(artistId, token);
                if (!IsCurrentLoad(version, token)) return;

                if (info != null)
                {
                    Title = "歌手详情";
                    ArtistName = info.Name;
                    ImageUrl = !string.IsNullOrEmpty(info.CoverUrl) ? info.CoverUrl : info.AvatarUrl;

                    if (info.Aliases.Count > 0)
                    {
                        AliasText = string.Join("、", info.Aliases);
                        ShowAlias = true;
                    }

                    if (!string.IsNullOrEmpty(info.BriefDesc))
                    {
                        Description = info.BriefDesc;
                        ShowDescription = true;
                    }

                    ShowNav = true;
                    ShowPlayAll = true;
                    ShowContent();
                    return;
                }

                if (long.TryParse(artistId, out var uid))
                {
                    var userInfo = await _profileService.GetUserDetailAsync(uid);
                    if (!IsCurrentLoad(version, token)) return;

                    if (userInfo != null)
                    {
                        Title = "用户详情";
                        ArtistName = userInfo.Nickname;
                        ImageUrl = userInfo.AvatarUrl;
                        AliasText = $"关注 {userInfo.FollowCount}  |  粉丝 {userInfo.FollowedCount}";
                        ShowAlias = true;

                        if (!string.IsNullOrEmpty(userInfo.Signature))
                        {
                            Description = userInfo.Signature;
                            ShowDescription = true;
                        }

                        ShowNav = false;
                        ShowPlayAll = false;
                        ShowContent();
                        return;
                    }
                }

                Title = "未找到";
                ArtistName = "无法加载信息";
                ShowNav = false;
                ShowPlayAll = false;
                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistDetail] LoadArtistInfo Error: {ex.Message}");
                if (IsCurrentLoad(version, token))
                {
                    Title = "加载失败";
                    ArtistName = ex.Message;
                    ShowContent();
                }
            }
        }

        public Task PlaySongsAsync(IReadOnlyList<Models.Song>? songs)
        {
            if (songs == null || songs.Count == 0) return Task.CompletedTask;
            return _playback.PlayAsync(new List<Models.Song>(songs), 0);
        }
    }

    public sealed class ArtistAlbumsViewModel : PageViewModelBase
    {
        private readonly IArtistService _artistService;
        private readonly INavigationService _navigation;

        public ArtistAlbumsViewModel(IArtistService artistService, INavigationService navigation)
        {
            _artistService = artistService;
            _navigation = navigation;
            Albums = new ObservableCollection<AlbumItem>();
        }

        public ObservableCollection<AlbumItem> Albums { get; }

        public async Task LoadAsync(string artistId)
        {
            var (version, token) = BeginLoad();
            ShowLoading();

            var albums = await _artistService.GetArtistAlbumsAsync(artistId, 50, token);
            if (!IsCurrentLoad(version, token)) return;

            Albums.Clear();
            foreach (var a in albums)
            {
                Albums.Add(new AlbumItem
                {
                    Id = a.Id,
                    Name = a.Name,
                    PicUrl = a.PicUrl,
                    Size = a.Size
                });
            }

            ShowContent();
        }

        public void OpenAlbum(AlbumItem? album)
        {
            if (album != null)
                _navigation.Navigate(PageKey.PlaylistDetail, $"album_{album.Id}");
        }
    }

    public sealed class ArtistInfoViewModel : PageViewModelBase
    {
        private readonly IArtistService _artistService;

        private string _artistName = string.Empty;
        private string _imageUrl = string.Empty;
        private bool _showAlias;
        private string _aliasText = string.Empty;
        private bool _showDescription;
        private string _description = string.Empty;

        public ArtistInfoViewModel(IArtistService artistService)
        {
            _artistService = artistService;
        }

        public string ArtistName { get => _artistName; private set => SetProperty(ref _artistName, value); }
        public string ImageUrl
        {
            get => _imageUrl;
            private set
            {
                if (SetProperty(ref _imageUrl, value))
                    OnPropertyChanged(nameof(HasImage));
            }
        }
        public bool HasImage => !string.IsNullOrEmpty(ImageUrl);
        public bool ShowAlias { get => _showAlias; private set => SetProperty(ref _showAlias, value); }
        public string AliasText { get => _aliasText; private set => SetProperty(ref _aliasText, value); }
        public bool ShowDescription { get => _showDescription; private set => SetProperty(ref _showDescription, value); }
        public string Description { get => _description; private set => SetProperty(ref _description, value); }

        public async Task LoadAsync(string artistId)
        {
            var (version, token) = BeginLoad();
            ShowLoading();

            var info = await _artistService.GetArtistDetailAsync(artistId, token);
            if (!IsCurrentLoad(version, token)) return;

            if (info != null)
            {
                ArtistName = info.Name;
                ImageUrl = info.PicUrl;
                if (info.Aliases.Count > 0)
                {
                    AliasText = string.Join("、", info.Aliases);
                    ShowAlias = true;
                }
                if (!string.IsNullOrEmpty(info.BriefDesc))
                {
                    Description = info.BriefDesc;
                    ShowDescription = true;
                }
            }

            ShowContent();
        }
    }
}
