using System;
using System.Collections.Generic;
using System.Text.Json;
using music.Core.Abstractions;
using music.Core.Models;

namespace music.Core.Netease.Mapping
{
    internal static class CloudJsonMapper
    {
        public static List<CloudSongInfo> ParseCloudSongs(string json, IApiLog? log = null)
        {
            var songs = new List<CloudSongInfo>();
            var result = JsonSerializer.Deserialize<JsonElement>(json);
            if (result.ValueKind != JsonValueKind.Object)
                throw new JsonException("响应不是有效的 JSON 对象。");

            JsonElement dataArray;
            if (result.TryGetProperty("data", out dataArray))
            {
            }
            else if (result.TryGetProperty("cloudData", out dataArray))
            {
            }
            else
            {
                return songs;
            }

            foreach (var item in dataArray.EnumerateArray())
            {
                    try
                    {
                        var song = new CloudSongInfo();
                        JsonElement cloudItem = item;
                        if (item.TryGetProperty("privateCloud", out var privateCloud))
                        {
                            cloudItem = privateCloud;
                        }

                        if (cloudItem.TryGetProperty("songId", out var songId))
                        {
                            song.SongId = songId.ValueKind == JsonValueKind.Number
                                ? songId.GetInt64().ToString()
                                : songId.GetString() ?? string.Empty;
                        }

                        if (cloudItem.TryGetProperty("songName", out var songName))
                            song.SongName = songName.GetString() ?? string.Empty;
                        else if (cloudItem.TryGetProperty("song", out var song2))
                            song.SongName = song2.GetString() ?? string.Empty;
                        else if (item.TryGetProperty("simpleSong", out var ss) && ss.TryGetProperty("name", out var name))
                            song.SongName = name.GetString() ?? string.Empty;

                        if (cloudItem.TryGetProperty("artist", out var artist))
                            song.Artist = artist.GetString() ?? string.Empty;
                        else if (item.TryGetProperty("simpleSong", out var simpleSong) &&
                                 simpleSong.TryGetProperty("ar", out var ar) &&
                                 ar.GetArrayLength() > 0)
                        {
                            var artists = new List<string>();
                            foreach (var a in ar.EnumerateArray())
                            {
                                if (a.TryGetProperty("name", out var aName))
                                    artists.Add(aName.GetString() ?? string.Empty);
                            }
                            song.Artist = string.Join(" / ", artists);
                        }

                        if (cloudItem.TryGetProperty("album", out var album))
                            song.Album = album.GetString() ?? string.Empty;
                        else if (item.TryGetProperty("simpleSong", out var ss2) &&
                                 ss2.TryGetProperty("al", out var al))
                        {
                            song.Album = al.TryGetProperty("name", out var alName) ? alName.GetString() ?? string.Empty : string.Empty;
                            if (al.TryGetProperty("picUrl", out var picUrl))
                                song.CoverUrl = picUrl.GetString() ?? string.Empty;
                        }

                        if (cloudItem.TryGetProperty("fileName", out var fileName))
                            song.FileName = fileName.GetString() ?? string.Empty;

                        if (cloudItem.TryGetProperty("addTime", out var addTime))
                            song.AddTime = addTime.GetInt64();

                        if (cloudItem.TryGetProperty("fileSize", out var fileSize))
                            song.FileSize = fileSize.GetInt64();

                        if (item.TryGetProperty("simpleSong", out var simpleSong2) &&
                            simpleSong2.TryGetProperty("dt", out var dt))
                        {
                            song.Duration = dt.GetInt64();
                        }

                        songs.Add(song);
                    }
                    catch (Exception ex)
                    {
                        log?.Write($"Cloud item parse error: {ex.Message}");
                    }
                }

            return songs;
        }
    }
}
