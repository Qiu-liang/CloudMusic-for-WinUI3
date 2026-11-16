using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Mvvm;
using music.Core.Services;
using music.Core.ViewModels.Items;
using music.Models;

namespace music.Core.ViewModels
{
    public abstract class SongListViewModelBase : PageViewModelBase
    {
        private readonly IPlaybackCoordinator _playback;
        private List<Song> _songModels = new();
        private string _songsCountText = string.Empty;
        private bool _showPlayAll;

        protected SongListViewModelBase(IPlaybackCoordinator playback, IApiLog log)
        {
            _playback = playback;
            Songs = new ObservableCollection<SongItem>();
            PlayAllCommand = new AsyncRelayCommand(PlayAllAsync, () => _songModels.Count > 0, log);
        }

        public ObservableCollection<SongItem> Songs { get; }
        public IReadOnlyList<Song> SongModels => _songModels;
        public AsyncRelayCommand PlayAllCommand { get; }

        public string SongsCountText
        {
            get => _songsCountText;
            protected set => SetProperty(ref _songsCountText, value);
        }

        public bool ShowPlayAll
        {
            get => _showPlayAll;
            protected set => SetProperty(ref _showPlayAll, value);
        }

        protected void SetSongs(List<Song> songs, bool forceLiked = false)
        {
            _songModels = songs;
            Songs.Clear();
            foreach (var item in SongItemFactory.FromSongs(songs))
            {
                if (forceLiked) item.IsLiked = true;
                Songs.Add(item);
            }
            SongsCountText = $"共 {Songs.Count} 首";
            ShowPlayAll = Songs.Count > 0;
            PlayAllCommand.RaiseCanExecuteChanged();
        }

        protected void ClearSongs(string countText = "共 0 首")
        {
            _songModels = new List<Song>();
            Songs.Clear();
            SongsCountText = countText;
            ShowPlayAll = false;
            PlayAllCommand.RaiseCanExecuteChanged();
        }

        public Task PlaySongAsync(SongItem? item)
        {
            if (item == null) return Task.CompletedTask;
            var index = Songs.IndexOf(item);
            return index >= 0 && index < _songModels.Count
                ? _playback.PlayAsync(_songModels, index)
                : Task.CompletedTask;
        }

        public Task PlayAllAsync() =>
            _songModels.Count > 0 ? _playback.PlayAsync(_songModels, 0) : Task.CompletedTask;
    }
}
