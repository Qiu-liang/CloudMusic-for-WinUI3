using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using music.Models;
using music.Services;

namespace music.Pages
{
    public sealed partial class PlaylistDetailPage : Page
    {
        private readonly ObservableCollection<SongItem> _songs = new();
        private List<Song> _songModels = new();
        private string _playlistId = string.Empty;

        public PlaylistDetailPage()
        {
            this.InitializeComponent();
            SongsListView.ItemsSource = _songs;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string playlistId)
            {
                _playlistId = playlistId;
                await LoadPlaylistAsync(playlistId);
            }
        }

        private async System.Threading.Tasks.Task LoadPlaylistAsync(string playlistId)
        {
            ShowLoading();

            List<Song> songs;
            string title = "歌单详情";

            if (playlistId.StartsWith("artist_"))
            {
                var artistId = playlistId.Substring("artist_".Length);
                songs = await App.ApiService.GetArtistSongsAsync(artistId);
                title = "歌手热门歌曲";
            }
            else if (playlistId.StartsWith("album_"))
            {
                var albumId = playlistId.Substring("album_".Length);
                songs = await App.ApiService.GetAlbumSongsAsync(albumId);
                title = "专辑歌曲";

                var albumInfo = await App.ApiService.GetAlbumInfoAsync(albumId);
                if (albumInfo != null)
                {
                    title = albumInfo.Name;
                    ShowAlbumInfo(albumInfo);
                }
            }
            else
            {
                songs = await App.ApiService.GetPlaylistDetailAsync(playlistId);
                title = "歌单详情";

                var playlistInfo = await App.ApiService.GetPlaylistInfoAsync(playlistId);
                if (playlistInfo != null)
                {
                    title = playlistInfo.Name;
                    ShowPlaylistInfo(playlistInfo);
                }
            }

            if (songs == null || songs.Count == 0)
            {
                ShowError("歌单为空或加载失败");
                return;
            }

            _songModels = songs;
            _songs.Clear();
            for (int i = 0; i < songs.Count; i++)
            {
                var song = songs[i];
                _songs.Add(new SongItem
                {
                    Id = song.Id,
                    Name = song.Name,
                    ArtistNames = song.ArtistNames,
                    AlbumName = song.Album.Name,
                    CoverUrl = song.CoverImgUrl,
                    DurationFormatted = song.DurationFormatted,
                    Index = i + 1,
                    IsLiked = song.Liked,
                    CanPlay = song.CanPlay,
                    Fee = song.Fee,
                    IsVip = song.IsVip,
                    IsPaid = song.IsPaid,
                    FeeText = song.FeeText
                });
            }

            TitleText.Text = title;
            SongsCountText.Text = $"共 {_songs.Count} 首";
            PlayAllButton.Visibility = Visibility.Visible;
            ShowContent();
        }

        private void ShowPlaylistInfo(PlaylistDetailInfo info)
        {
            InfoSection.Visibility = Visibility.Visible;

            // 封面
            if (!string.IsNullOrEmpty(info.CoverImgUrl))
            {
                CoverImage.Source = new BitmapImage(new System.Uri(info.CoverImgUrl));
                CoverImage.Visibility = Visibility.Visible;
            }

            // 标题
            InfoTitle.Text = info.Name;

            // 创建者
            if (!string.IsNullOrEmpty(info.CreatorName))
            {
                CreatorPanel.Visibility = Visibility.Visible;
                CreatorNameText.Text = info.CreatorName;
                if (!string.IsNullOrEmpty(info.CreatorAvatarUrl))
                {
                    CreatorAvatarBrush.ImageSource = new BitmapImage(new System.Uri(info.CreatorAvatarUrl));
                }
            }

            // 标签
            if (info.Tags.Count > 0)
            {
                TagsPanel.Visibility = Visibility.Visible;
                TagsText.Text = info.TagsText;
            }

            // 统计信息
            PlayCountText.Text = $"播放 {info.PlayCountFormatted}";
            if (info.SubscribedCount > 0)
            {
                SubscribedPanel.Visibility = Visibility.Visible;
                SubscribedCountText.Text = $"收藏 {info.SubscribedCountFormatted}";
            }

            // 描述
            if (!string.IsNullOrEmpty(info.Description))
            {
                DescriptionText.Visibility = Visibility.Visible;
                DescriptionText.Text = info.Description;
            }
        }

        private void ShowAlbumInfo(AlbumDetailInfo info)
        {
            InfoSection.Visibility = Visibility.Visible;

            // 封面
            if (!string.IsNullOrEmpty(info.PicUrl))
            {
                CoverImage.Source = new BitmapImage(new System.Uri(info.PicUrl));
                CoverImage.Visibility = Visibility.Visible;
            }

            // 标题
            InfoTitle.Text = info.Name;

            // 艺术家
            if (!string.IsNullOrEmpty(info.ArtistName))
            {
                CreatorPanel.Visibility = Visibility.Visible;
                CreatorNameText.Text = info.ArtistName;
            }

            // 发布时间和类型
            var metaText = new List<string>();
            if (!string.IsNullOrEmpty(info.PublishTime))
                metaText.Add(info.PublishTime);
            if (!string.IsNullOrEmpty(info.SubType))
                metaText.Add(info.SubType);
            if (metaText.Count > 0)
            {
                TagsPanel.Visibility = Visibility.Visible;
                TagsText.Text = string.Join(" · ", metaText);
            }

            // 歌曲数量
            PlayCountText.Text = $"{info.Size} 首歌曲";

            // 公司
            if (!string.IsNullOrEmpty(info.Company))
            {
                CompanyPanel.Visibility = Visibility.Visible;
                CompanyText.Text = info.Company;
            }

            // 描述
            if (!string.IsNullOrEmpty(info.Description))
            {
                DescriptionText.Visibility = Visibility.Visible;
                DescriptionText.Text = info.Description;
            }
        }

        private void ShowLoading()
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ContentPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = message;
        }

        private void ShowContent()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadPlaylistAsync(_playlistId);
        }

        private async void SongsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SongItem songItem)
            {
                var index = _songs.IndexOf(songItem);
                if (index >= 0 && index < _songModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_songModels, index);
                }
            }
        }

        private async void PlayAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_songModels.Count > 0)
            {
                await MainWindow.PlaybackService.PlayAsync(_songModels, 0);
            }
        }
    }
}
