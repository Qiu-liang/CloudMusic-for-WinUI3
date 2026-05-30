using Microsoft.UI.Xaml;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace music.Pages
{
    public class SongItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _artistNames = string.Empty;
        private string _albumName = string.Empty;
        private string _coverUrl = string.Empty;
        private string _durationFormatted = string.Empty;
        private int _index;
        private bool _isLiked;
        private bool _canPlay = true;
        private int _fee;
        private bool _isVip;
        private bool _isPaid;
        private string _feeText = string.Empty;

        public string Id { get => _id; set => SetField(ref _id, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public string ArtistNames { get => _artistNames; set => SetField(ref _artistNames, value); }
        public string AlbumName { get => _albumName; set => SetField(ref _albumName, value); }
        public string CoverUrl { get => _coverUrl; set => SetField(ref _coverUrl, value); }
        public string DurationFormatted { get => _durationFormatted; set => SetField(ref _durationFormatted, value); }
        public int Index { get => _index; set => SetField(ref _index, value); }
        public bool IsLiked { get => _isLiked; set => SetField(ref _isLiked, value); }
        public bool CanPlay { get => _canPlay; set => SetField(ref _canPlay, value); }
        public int Fee { get => _fee; set { SetField(ref _fee, value); OnPropertyChanged(nameof(IsVip)); OnPropertyChanged(nameof(IsPaid)); OnPropertyChanged(nameof(FeeText)); OnPropertyChanged(nameof(VipVisibility)); OnPropertyChanged(nameof(PaidVisibility)); } }
        public bool IsVip { get => _isVip; set { SetField(ref _isVip, value); OnPropertyChanged(nameof(VipVisibility)); } }
        public bool IsPaid { get => _isPaid; set { SetField(ref _isPaid, value); OnPropertyChanged(nameof(PaidVisibility)); } }
        public string FeeText { get => _feeText; set => SetField(ref _feeText, value); }

        public Visibility CoverVisibility => string.IsNullOrEmpty(CoverUrl) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility VipVisibility => IsVip ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PaidVisibility => IsPaid ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class PlaylistItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public long PlayCount { get; set; }
        public int TrackCount { get; set; }
        public string PlayCountFormatted { get; set; } = string.Empty;
    }

    public class CloudSongItem
    {
        public string SongId { get; set; } = string.Empty;
        public string SongName { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string FileSizeFormatted { get; set; } = string.Empty;
        public string DurationFormatted { get; set; } = string.Empty;
        public string AddTimeFormatted { get; set; } = string.Empty;
        public Visibility CoverVisibility => string.IsNullOrEmpty(CoverUrl) ? Visibility.Collapsed : Visibility.Visible;
    }

    public class ArtistItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public int SongCount { get; set; }
        public string SongCountText => $"{SongCount} 首歌曲";
        public Visibility PicVisibility => string.IsNullOrEmpty(PicUrl) ? Visibility.Collapsed : Visibility.Visible;
    }

    public class AlbumItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string PublishTime { get; set; } = string.Empty;
        public int Size { get; set; }
        public string SizeText => $"{Size} 首";
        public Visibility PicVisibility => string.IsNullOrEmpty(PicUrl) ? Visibility.Collapsed : Visibility.Visible;
    }
}