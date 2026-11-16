using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Results;
using music.Core.Services;
using music.Core.ViewModels.Items;
using music.Models;
using music.Services;

namespace music.Core.ViewModels
{
    public sealed class PlaylistDetailViewModel : PageViewModelBase
    {
        private readonly IPlaylistService _playlistService;
        private readonly IArtistService _artistService;
        private readonly ILibraryService _libraryService;
        private readonly ISessionService _sessionService;
        private readonly IPlaybackCoordinator _playback;
        private readonly IDialogService _dialogs;
        private readonly IUiDispatcher _dispatcher;
        private readonly IApiLog _log;

        private string _playlistId = string.Empty;
        private List<Song> _songModels = new();
        private bool _isFavoritePlaylist;
        private bool _isSubscribed;
        private bool _canSubscribe;
        private bool _isSubscribing;

        private string _title = "歌单详情";
        private string _songsCountText = string.Empty;
        private bool _showPlayAll;
        private bool _showInfoSection;
        private string _coverImageUrl = string.Empty;
        private bool _isPrivate;
        private string _infoTitle = string.Empty;
        private bool _showCreator;
        private string _creatorName = string.Empty;
        private string _creatorAvatarUrl = string.Empty;
        private bool _showTags;
        private string _tagsText = string.Empty;
        private string _playCountText = string.Empty;
        private bool _showSubscribedCount;
        private string _subscribedCountText = string.Empty;
        private bool _showDescription;
        private string _description = string.Empty;
        private bool _showCompany;
        private string _company = string.Empty;

        public PlaylistDetailViewModel(
            IPlaylistService playlistService,
            IArtistService artistService,
            ILibraryService libraryService,
            ISessionService sessionService,
            IPlaybackCoordinator playback,
            IDialogService dialogs,
            IUiDispatcher dispatcher,
            IApiLog log)
        {
            _playlistService = playlistService;
            _artistService = artistService;
            _libraryService = libraryService;
            _sessionService = sessionService;
            _playback = playback;
            _dialogs = dialogs;
            _dispatcher = dispatcher;
            _log = log;

            Songs = new ObservableCollection<SongItem>();
            PlayAllCommand = new AsyncRelayCommand(PlayAllAsync, () => _songModels.Count > 0, log, dispatcher);
            RetryCommand = new AsyncRelayCommand(() => LoadAsync(_playlistId), log: log, dispatcher: dispatcher);
            ToggleSubscribeCommand = new AsyncRelayCommand(ToggleSubscribeAsync, () => _canSubscribe && !_isSubscribing, log, dispatcher);

            _libraryService.SongLikeChanged += OnSongLikeChanged;
        }

        public ObservableCollection<SongItem> Songs { get; }
        public AsyncRelayCommand PlayAllCommand { get; }
        public AsyncRelayCommand RetryCommand { get; }
        public AsyncRelayCommand ToggleSubscribeCommand { get; }

        public string Title { get => _title; private set => SetProperty(ref _title, value); }
        public string SongsCountText { get => _songsCountText; private set => SetProperty(ref _songsCountText, value); }
        public bool ShowPlayAll { get => _showPlayAll; private set => SetProperty(ref _showPlayAll, value); }
        public bool ShowInfoSection { get => _showInfoSection; private set => SetProperty(ref _showInfoSection, value); }
        public string CoverImageUrl
        {
            get => _coverImageUrl;
            private set
            {
                if (SetProperty(ref _coverImageUrl, value))
                    OnPropertyChanged(nameof(HasCoverImage));
            }
        }
        public bool HasCoverImage => !string.IsNullOrEmpty(CoverImageUrl);
        public bool IsPrivate { get => _isPrivate; private set => SetProperty(ref _isPrivate, value); }
        public string InfoTitle { get => _infoTitle; private set => SetProperty(ref _infoTitle, value); }
        public bool ShowCreator { get => _showCreator; private set => SetProperty(ref _showCreator, value); }
        public string CreatorName { get => _creatorName; private set => SetProperty(ref _creatorName, value); }
        public string CreatorAvatarUrl { get => _creatorAvatarUrl; private set => SetProperty(ref _creatorAvatarUrl, value); }
        public bool HasCreatorAvatar => !string.IsNullOrEmpty(CreatorAvatarUrl);
        public bool ShowTags { get => _showTags; private set => SetProperty(ref _showTags, value); }
        public string TagsText { get => _tagsText; private set => SetProperty(ref _tagsText, value); }
        public string PlayCountText { get => _playCountText; private set => SetProperty(ref _playCountText, value); }
        public bool ShowSubscribedCount { get => _showSubscribedCount; private set => SetProperty(ref _showSubscribedCount, value); }
        public string SubscribedCountText { get => _subscribedCountText; private set => SetProperty(ref _subscribedCountText, value); }
        public bool ShowDescription { get => _showDescription; private set => SetProperty(ref _showDescription, value); }
        public string Description { get => _description; private set => SetProperty(ref _description, value); }
        public bool ShowCompany { get => _showCompany; private set => SetProperty(ref _showCompany, value); }
        public string Company { get => _company; private set => SetProperty(ref _company, value); }

        public bool CanSubscribe
        {
            get => _canSubscribe;
            private set
            {
                if (SetProperty(ref _canSubscribe, value))
                    ToggleSubscribeCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsSubscribed
        {
            get => _isSubscribed;
            private set
            {
                if (SetProperty(ref _isSubscribed, value))
                    OnPropertyChanged(nameof(SubscribeButtonText));
            }
        }

        public bool IsSubscribeEnabled => !_isSubscribing;
        public string SubscribeButtonText => IsSubscribed ? "取消收藏" : "收藏歌单";

        public async Task LoadAsync(string playlistId)
        {
            _playlistId = playlistId ?? string.Empty;
            var (version, token) = BeginLoad();
            _isFavoritePlaylist = false;
            ResetInfoSection();
            ShowLoading();

            List<Song> songs;
            ApiResult? failure = null;
            string title = "歌单详情";

            if (_playlistId.StartsWith("artist_", StringComparison.Ordinal))
            {
                var artistId = _playlistId.Substring("artist_".Length);
                var artistSongsResult = await _artistService.GetArtistSongsAsync(artistId, cancellationToken: token);
                if (!IsCurrentLoad(version, token)) return;
                songs = artistSongsResult.Value;
                if (!artistSongsResult.Success) failure = artistSongsResult.Status;
                title = "歌手热门歌曲";
            }
            else if (_playlistId.StartsWith("album_", StringComparison.Ordinal))
            {
                var albumId = _playlistId.Substring("album_".Length);
                var albumSongsResult = await _playlistService.GetAlbumSongsAsync(albumId);
                if (!IsCurrentLoad(version, token)) return;
                songs = albumSongsResult.Value;
                if (!albumSongsResult.Success) failure = albumSongsResult.Status;
                title = "专辑歌曲";

                var albumInfo = await _playlistService.GetAlbumInfoAsync(albumId);
                if (!IsCurrentLoad(version, token)) return;
                if (albumInfo != null)
                {
                    title = albumInfo.Name;
                    ApplyAlbumInfo(albumInfo);
                }
            }
            else
            {
                var songsResult = await _playlistService.GetPlaylistDetailAsync(_playlistId, bypassCache: true);
                if (!IsCurrentLoad(version, token)) return;
                songs = songsResult.Value;
                if (!songsResult.Success) failure = songsResult.Status;
                title = "歌单详情";

                var playlistInfo = await _playlistService.GetPlaylistInfoAsync(_playlistId, bypassCache: true);
                if (!IsCurrentLoad(version, token)) return;
                if (playlistInfo != null)
                {
                    title = playlistInfo.Name;
                    ApplyPlaylistInfo(playlistInfo);
                }

                _isFavoritePlaylist = await _libraryService.IsFavoritePlaylistAsync(_playlistId);
                if (!IsCurrentLoad(version, token)) return;
            }

            if (songs.Count == 0)
            {
                Title = title;
                _songModels.Clear();
                Songs.Clear();
                SongsCountText = failure == null ? "共 0 首" : string.Empty;
                ShowPlayAll = false;
                PlayAllCommand.RaiseCanExecuteChanged();
                ShowError(failure != null ? failure.DisplayMessage : "歌单为空或加载失败");
                return;
            }

            _songModels = songs;
            Songs.Clear();
            foreach (var item in SongItemFactory.FromSongs(songs))
                Songs.Add(item);

            Title = title;
            SongsCountText = $"共 {Songs.Count} 首";
            ShowPlayAll = true;
            PlayAllCommand.RaiseCanExecuteChanged();
            ShowContent();
        }

        public Task PlaySongAsync(SongItem? item)
        {
            if (item == null) return Task.CompletedTask;
            var index = Songs.IndexOf(item);
            if (index >= 0 && index < _songModels.Count)
            {
                return _playback.PlayAsync(_songModels, index);
            }
            return Task.CompletedTask;
        }

        private Task PlayAllAsync()
        {
            return _songModels.Count > 0 ? _playback.PlayAsync(_songModels, 0) : Task.CompletedTask;
        }

        private async Task ToggleSubscribeAsync()
        {
            if (!CanSubscribe || _isSubscribing) return;

            if (!_sessionService.IsLoggedIn)
            {
                var loggedIn = await _dialogs.ShowLoginAsync();
                if (!loggedIn) return;
            }

            _isSubscribing = true;
            OnPropertyChanged(nameof(IsSubscribeEnabled));
            ToggleSubscribeCommand.RaiseCanExecuteChanged();

            try
            {
                var target = !IsSubscribed;
                var result = await _playlistService.SetPlaylistSubscriptionAsync(_playlistId, target);
                if (result.Success)
                {
                    IsSubscribed = target;
                }
                else
                {
                    await _dialogs.ShowMessageAsync("操作失败", result.DisplayMessage);
                }
            }
            finally
            {
                _isSubscribing = false;
                OnPropertyChanged(nameof(IsSubscribeEnabled));
                ToggleSubscribeCommand.RaiseCanExecuteChanged();
            }
        }

        private void OnSongLikeChanged(object? sender, SongLikeChangedEventArgs e)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (IsDisposed) return;

                foreach (var song in _songModels)
                {
                    if (string.Equals(song.Id, e.SongId, StringComparison.Ordinal))
                        song.Liked = e.IsLiked;
                }
                foreach (var item in Songs)
                {
                    if (string.Equals(item.Id, e.SongId, StringComparison.Ordinal))
                        item.IsLiked = e.IsLiked;
                }

                if (_isFavoritePlaylist && !string.IsNullOrEmpty(_playlistId))
                {
                    LoadAsync(_playlistId).Forget(_log, "PlaylistDetail.ReloadFavorite");
                }
            });
        }

        private void ResetInfoSection()
        {
            ShowInfoSection = false;
            CoverImageUrl = string.Empty;
            IsPrivate = false;
            InfoTitle = string.Empty;
            CanSubscribe = false;
            IsSubscribed = false;
            ShowCreator = false;
            CreatorName = string.Empty;
            CreatorAvatarUrl = string.Empty;
            OnPropertyChanged(nameof(HasCreatorAvatar));
            ShowTags = false;
            TagsText = string.Empty;
            PlayCountText = string.Empty;
            ShowSubscribedCount = false;
            SubscribedCountText = string.Empty;
            ShowDescription = false;
            Description = string.Empty;
            ShowCompany = false;
            Company = string.Empty;
        }

        private void ApplyPlaylistInfo(PlaylistDetailInfo info)
        {
            ShowInfoSection = true;
            CoverImageUrl = info.CoverImgUrl;
            IsPrivate = info.Privacy == 10;
            InfoTitle = info.Name;
            CanSubscribe = info.CreatorId > 0 && info.CreatorId != _sessionService.UserId;
            IsSubscribed = info.Subscribed;

            if (!string.IsNullOrEmpty(info.CreatorName))
            {
                ShowCreator = true;
                CreatorName = info.CreatorName;
                CreatorAvatarUrl = info.CreatorAvatarUrl;
                OnPropertyChanged(nameof(HasCreatorAvatar));
            }

            if (info.Tags.Count > 0)
            {
                ShowTags = true;
                TagsText = info.TagsText;
            }

            PlayCountText = $"播放 {info.PlayCountFormatted}";
            if (info.SubscribedCount > 0)
            {
                ShowSubscribedCount = true;
                SubscribedCountText = $"收藏 {info.SubscribedCountFormatted}";
            }

            if (!string.IsNullOrEmpty(info.Description))
            {
                ShowDescription = true;
                Description = info.Description;
            }
        }

        private void ApplyAlbumInfo(AlbumDetailInfo info)
        {
            ShowInfoSection = true;
            CoverImageUrl = info.PicUrl;
            InfoTitle = info.Name;

            if (!string.IsNullOrEmpty(info.ArtistName))
            {
                ShowCreator = true;
                CreatorName = info.ArtistName;
            }

            var metaText = new List<string>();
            if (!string.IsNullOrEmpty(info.PublishTime))
                metaText.Add(info.PublishTime);
            if (!string.IsNullOrEmpty(info.SubType))
                metaText.Add(info.SubType);
            if (metaText.Count > 0)
            {
                ShowTags = true;
                TagsText = string.Join(" · ", metaText);
            }

            PlayCountText = $"{info.Size} 首歌曲";

            if (!string.IsNullOrEmpty(info.Company))
            {
                ShowCompany = true;
                Company = info.Company;
            }

            if (!string.IsNullOrEmpty(info.Description))
            {
                ShowDescription = true;
                Description = info.Description;
            }
        }

        public override void Dispose()
        {
            _libraryService.SongLikeChanged -= OnSongLikeChanged;
            base.Dispose();
        }
    }
}
