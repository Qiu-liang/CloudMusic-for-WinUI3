using System.Collections.Generic;
using System.IO;

namespace music.Models
{
    public static class LocalSongFactory
    {
        public static Song Create(string name, string artist, string filePath)
        {
            return new Song
            {
                Id = "local:" + Path.GetFullPath(filePath),
                Name = name,
                Artists = new List<Artist> { new Artist { Name = artist } },
                LocalFilePath = Path.GetFullPath(filePath)
            };
        }
    }
}
