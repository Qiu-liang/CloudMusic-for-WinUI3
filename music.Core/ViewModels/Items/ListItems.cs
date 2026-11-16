using System.Collections.Generic;
using music.Core.Models;
using music.Core.Mvvm;

namespace music.Core.ViewModels.Items
{
    public sealed class SongItem : ObservableObject
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

        public string Id { get => _id; set => SetProperty(ref _id, value); }
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string ArtistNames { get => _artistNames; set => SetProperty(ref _artistNames, value); }
        public string AlbumName { get => _albumName; set => SetProperty(ref _albumName, value); }
        public string CoverUrl
        {
            get => _coverUrl;
            set
            {
                if (SetProperty(ref _coverUrl, value))
                    OnPropertyChanged(nameof(HasCover));
            }
        }
        public string DurationFormatted { get => _durationFormatted; set => SetProperty(ref _durationFormatted, value); }
        public int Index { get => _index; set => SetProperty(ref _index, value); }
        public bool IsLiked { get => _isLiked; set => SetProperty(ref _isLiked, value); }
        public bool CanPlay { get => _canPlay; set => SetProperty(ref _canPlay, value); }

        public int Fee
        {
            get => _fee;
            set
            {
                if (SetProperty(ref _fee, value))
                {
                    OnPropertyChanged(nameof(IsVip));
                    OnPropertyChanged(nameof(IsPaid));
                    OnPropertyChanged(nameof(FeeText));
                }
            }
        }

        public bool IsVip { get => _isVip; set => SetProperty(ref _isVip, value); }
        public bool IsPaid { get => _isPaid; set => SetProperty(ref _isPaid, value); }
        public string FeeText { get => _feeText; set => SetProperty(ref _feeText, value); }

        public bool HasCover => !string.IsNullOrEmpty(CoverUrl);
    }

    public static class SongItemFactory
    {
        public static SongItem FromSong(Song song, int index) => new()
        {
            Id = song.Id,
            Name = song.Name,
            ArtistNames = song.ArtistNames,
            AlbumName = song.Album.Name,
            CoverUrl = song.CoverImgUrl,
            DurationFormatted = song.DurationFormatted,
            Index = index,
            IsLiked = song.Liked,
            CanPlay = song.CanPlay,
            Fee = song.Fee,
            IsVip = song.IsVip,
            IsPaid = song.IsPaid,
            FeeText = song.FeeText
        };

        public static List<SongItem> FromSongs(IReadOnlyList<Song> songs)
        {
            var result = new List<SongItem>(songs.Count);
            for (var i = 0; i < songs.Count; i++)
                result.Add(FromSong(songs[i], i + 1));
            return result;
        }
    }

    public sealed class PlaylistItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public long PlayCount { get; set; }
        public int TrackCount { get; set; }
        public string PlayCountFormatted { get; set; } = string.Empty;
    }

    public sealed class CloudSongItem
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
        public bool HasCover => !string.IsNullOrEmpty(CoverUrl);
    }

    public sealed class ArtistItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public int SongCount { get; set; }
        public string SongCountText => $"{SongCount} 首歌曲";
        public bool HasPic => !string.IsNullOrEmpty(PicUrl);
    }

    public sealed class AlbumItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string PublishTime { get; set; } = string.Empty;
        public int Size { get; set; }
        public string SizeText => $"{Size} 首";
        public bool HasPic => !string.IsNullOrEmpty(PicUrl);
    }

    public sealed class FollowUserItem
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    public sealed class RecentSongItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AlbumName { get; set; } = string.Empty;
        public string DurationFormatted { get; set; } = string.Empty;
        public string PlayTimeFormatted { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
        public bool IsVip { get; set; }
    }

    public sealed class AudioFileItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public long FileSize { get; set; }

        public string FileSizeFormatted
        {
            get
            {
                if (FileSize >= 1073741824)
                    return $"{FileSize / 1073741824.0:F2} GB";
                if (FileSize >= 1048576)
                    return $"{FileSize / 1048576.0:F2} MB";
                if (FileSize >= 1024)
                    return $"{FileSize / 1024.0:F2} KB";
                return $"{FileSize} B";
            }
        }
    }
}
