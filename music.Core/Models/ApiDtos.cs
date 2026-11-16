using System;
using System.Collections.Generic;

namespace music.Services
{
    public class PlaylistInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CoverImgUrl { get; set; } = string.Empty;
        public int TrackCount { get; set; }
        public long PlayCount { get; set; }
        public string CreatorName { get; set; } = string.Empty;
        public long CreatorId { get; set; }

        public string PlayCountFormatted
        {
            get
            {
                if (PlayCount >= 100000000)
                    return $"{PlayCount / 100000000.0:F1}亿";
                if (PlayCount >= 10000)
                    return $"{PlayCount / 10000.0:F1}万";
                return PlayCount.ToString();
            }
        }
    }

    public class SearchSuggestion
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
    }

    public class UserInfo
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public int VipType { get; set; }
    }

    public class CloudSongInfo
    {
        public string SongId { get; set; } = string.Empty;
        public string SongName { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public long AddTime { get; set; }
        public long FileSize { get; set; }
        public long Duration { get; set; }

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

        public string DurationFormatted
        {
            get
            {
                var seconds = Duration / 1000;
                var minutes = seconds / 60;
                seconds = seconds % 60;
                return $"{minutes:D2}:{seconds:D2}";
            }
        }

        public string AddTimeFormatted
        {
            get
            {
                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(AddTime).LocalDateTime;
                return dateTime.ToString("yyyy-MM-dd");
            }
        }
    }

    public class RecommendedPlaylist
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public long PlayCount { get; set; }
        public int TrackCount { get; set; }

        public string PlayCountFormatted
        {
            get
            {
                if (PlayCount >= 100000000)
                    return $"{PlayCount / 100000000.0:F1}亿";
                if (PlayCount >= 10000)
                    return $"{PlayCount / 10000.0:F1}万";
                return PlayCount.ToString();
            }
        }
    }

    public class UserDetailInfo
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public int FollowCount { get; set; }
        public int FollowedCount { get; set; }
        public int EventCount { get; set; }
    }

    public class FollowUser
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    public class RecentSong
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AlbumName { get; set; } = string.Empty;
        public long PlayTime { get; set; }
        public string ResourceId { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public long Duration { get; set; }
        public int Fee { get; set; } = 0;

        public string DurationFormatted
        {
            get
            {
                var seconds = Duration / 1000;
                var minutes = seconds / 60;
                seconds = seconds % 60;
                return $"{minutes:D2}:{seconds:D2}";
            }
        }

        public string PlayTimeFormatted
        {
            get
            {
                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(PlayTime).LocalDateTime;
                var now = DateTime.Now;
                var diff = now - dateTime;

                if (diff.TotalMinutes < 1)
                    return "刚刚";
                if (diff.TotalHours < 1)
                    return $"{(int)diff.TotalMinutes}分钟前";
                if (diff.TotalDays < 1)
                    return $"{(int)diff.TotalHours}小时前";
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays}天前";
                return dateTime.ToString("MM-dd HH:mm");
            }
        }

        public string PlatformName
        {
            get
            {
                return ResourceType switch
                {
                    "android" => "手机",
                    "iphone" => "手机",
                    "pc" => "PC",
                    "web" => "PC",
                    "linux" => "PC",
                    _ => "手机"
                };
            }
        }

        public bool IsVip => Fee == 1;
    }

    public class LyricInfo
    {
        public string LrcLyric { get; set; } = string.Empty;
        public string TranslatedLyric { get; set; } = string.Empty;
    }

    public class SearchArtistInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public int SongCount { get; set; }
    }

    public class SearchAlbumInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string PublishTime { get; set; } = string.Empty;
        public int Size { get; set; }
    }

    public class PlaylistDetailInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CoverImgUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CreatorName { get; set; } = string.Empty;
        public string CreatorAvatarUrl { get; set; } = string.Empty;
        public long CreatorId { get; set; }
        public bool Subscribed { get; set; }
        public int TrackCount { get; set; }
        public long PlayCount { get; set; }
        public long SubscribedCount { get; set; }
        public long ShareCount { get; set; }
        public long CommentCount { get; set; }
        public int Privacy { get; set; }
        public List<string> Tags { get; set; } = new();

        public string PlayCountFormatted
        {
            get
            {
                if (PlayCount >= 100000000)
                    return $"{PlayCount / 100000000.0:F1}亿";
                if (PlayCount >= 10000)
                    return $"{PlayCount / 10000.0:F1}万";
                return PlayCount.ToString();
            }
        }

        public string SubscribedCountFormatted
        {
            get
            {
                if (SubscribedCount >= 100000000)
                    return $"{SubscribedCount / 100000000.0:F1}亿";
                if (SubscribedCount >= 10000)
                    return $"{SubscribedCount / 10000.0:F1}万";
                return SubscribedCount.ToString();
            }
        }

        public string TagsText => string.Join(" / ", Tags);
    }

    public class AlbumDetailInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string ArtistId { get; set; } = string.Empty;
        public string PublishTime { get; set; } = string.Empty;
        public int Size { get; set; }
        public string SubType { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
    }

    public class ArtistDetailInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string PicUrl { get; set; } = string.Empty;
        public List<string> Aliases { get; set; } = new();
        public string BriefDesc { get; set; } = string.Empty;

        public string BestImageUrl =>
            !string.IsNullOrEmpty(CoverUrl) ? CoverUrl :
            (!string.IsNullOrEmpty(AvatarUrl) ? AvatarUrl : PicUrl);
    }

    public enum QrLoginState
    {
        Expired = 800,
        Waiting = 801,
        Scanned = 802,
        Confirmed = 803,
        Failed = -1,
        Unknown = 0
    }

    public sealed record QrCodePayload(string Unikey, string Base64Image);

    public sealed record QrLoginStatusResult(
        QrLoginState State,
        int RawCode,
        string Cookie,
        string Message = "");
}
