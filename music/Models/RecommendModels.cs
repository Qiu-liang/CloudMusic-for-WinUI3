using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace music.Models
{
    public class Song
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Duration { get; set; }
        public List<Artist> Artists { get; set; } = new();
        public Album Album { get; set; } = new();
        public string CoverImgUrl { get; set; } = string.Empty;
        public bool Liked { get; set; }
        
        // VIP相关字段
        public int Fee { get; set; } = 0;  // 0:免费 1:VIP 4:专辑 8:免费低音质
        public bool VipFlag { get; set; } = false;
        public bool VipPlayFlag { get; set; } = false;
        public bool PayPlayFlag { get; set; } = false;
        public bool CanPlay { get; set; } = true;
        public bool IsVip { get; set; } = false;
        public bool IsPaid { get; set; } = false;
        
        public string ArtistNames => string.Join(" / ", Artists.ConvertAll(a => a.Name));
        
        // VIP状态描述
        public string FeeText
        {
            get
            {
                return Fee switch
                {
                    1 => "VIP",
                    4 => "专辑",
                    8 => "试听",
                    _ => ""
                };
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
    }

    public class Artist
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class Album
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}