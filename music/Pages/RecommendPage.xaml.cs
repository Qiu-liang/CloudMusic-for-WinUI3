using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using music.Models;
using music.Services;

namespace music.Pages
{
    public class TrackCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int count)
            {
                return $"{count}首";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed partial class RecommendPage : Page
    {
        private readonly ObservableCollection<SongItem> _songs = new();
        private readonly ObservableCollection<PlaylistItem> _playlists = new();
        private readonly ObservableCollection<SongItem> _personalizedSongs = new();
        private List<Song> _songModels = new();
        private List<Song> _personalizedSongModels = new();

        public RecommendPage()
        {
            this.InitializeComponent();
            SongsListView.ItemsSource = _songs;
            PlaylistItems.ItemsSource = _playlists;
            PersonalizedItems.ItemsSource = _personalizedSongs;
            Loaded += RecommendPage_Loaded;
        }

        private async void RecommendPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAllDataAsync();
        }

        private async System.Threading.Tasks.Task LoadAllDataAsync()
        {
            ShowLoading();

            try
            {
                var playlistsTask = App.ApiService.GetRecommendedPlaylistsAsync(10);
                var personalizedTask = App.ApiService.GetPersonalizedSongsAsync(10);
                var dailyTask = App.ApiService.GetDailyRecommendSongsAsync(30);

                await System.Threading.Tasks.Task.WhenAll(playlistsTask, personalizedTask, dailyTask);

                var playlists = playlistsTask.Result;
                var personalized = personalizedTask.Result;
                var daily = dailyTask.Result;

                _playlists.Clear();
                foreach (var playlist in playlists)
                {
                    _playlists.Add(new PlaylistItem
                    {
                        Id = playlist.Id,
                        Name = playlist.Name,
                        PicUrl = playlist.PicUrl,
                        PlayCount = playlist.PlayCount,
                        TrackCount = playlist.TrackCount,
                        PlayCountFormatted = playlist.PlayCountFormatted
                    });
                }

                _personalizedSongModels = personalized;
                _personalizedSongs.Clear();
                foreach (var song in personalized)
                {
                    _personalizedSongs.Add(new SongItem
                    {
                        Id = song.Id,
                        Name = song.Name,
                        ArtistNames = song.ArtistNames,
                        CoverUrl = song.CoverImgUrl,
                        CanPlay = song.CanPlay,
                        Fee = song.Fee,
                        IsVip = song.IsVip,
                        IsPaid = song.IsPaid,
                        FeeText = song.FeeText
                    });
                }

                _songModels = daily;
                _songs.Clear();
                if (daily != null)
                {
                    for (int i = 0; i < daily.Count; i++)
                    {
                        var song = daily[i];
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
                }

                ShowContent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Recommend] Load Error: {ex.Message}");
                ShowError("加载失败，请稍后重试");
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

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAllDataAsync();
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

        private void PlaylistItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PlaylistItem playlistItem)
            {
                // 导航到歌单详情页面
                var mainWindow = App.m_window as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.MainContentFrame.Navigate(typeof(PlaylistDetailPage), playlistItem.Id);
                }
            }
        }

        private void ViewAllPlaylistsButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.m_window as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.MainContentFrame.Navigate(typeof(AllPlaylistsPage));
            }
        }

        private async void PersonalizedItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SongItem songItem)
            {
                var index = _personalizedSongs.IndexOf(songItem);
                if (index >= 0 && index < _personalizedSongModels.Count)
                {
                    await MainWindow.PlaybackService.PlayAsync(_personalizedSongModels[index]);
                }
            }
        }

        private async void PlayAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_personalizedSongModels.Count > 0)
            {
                await MainWindow.PlaybackService.PlayAsync(_personalizedSongModels, 0);
            }
        }

        // 推荐歌单左右箭头
        private void PlaylistLeftButton_Click(object sender, RoutedEventArgs e)
        {
            PlaylistScroller.ChangeView(PlaylistScroller.HorizontalOffset - 300, null, null);
        }

        private void PlaylistRightButton_Click(object sender, RoutedEventArgs e)
        {
            PlaylistScroller.ChangeView(PlaylistScroller.HorizontalOffset + 300, null, null);
        }

        private void PlaylistScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            PlaylistLeftButton.Visibility = PlaylistScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            PlaylistRightButton.Visibility = PlaylistScroller.HorizontalOffset < PlaylistScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }

        // 精选歌曲左右箭头
        private void PersonalizedLeftButton_Click(object sender, RoutedEventArgs e)
        {
            PersonalizedScroller.ChangeView(PersonalizedScroller.HorizontalOffset - 300, null, null);
        }

        private void PersonalizedRightButton_Click(object sender, RoutedEventArgs e)
        {
            PersonalizedScroller.ChangeView(PersonalizedScroller.HorizontalOffset + 300, null, null);
        }

        private void PersonalizedScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            PersonalizedLeftButton.Visibility = PersonalizedScroller.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
            PersonalizedRightButton.Visibility = PersonalizedScroller.HorizontalOffset < PersonalizedScroller.ScrollableWidth ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}