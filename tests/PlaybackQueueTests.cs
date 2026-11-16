using music.Models;
using music.Services;
using Xunit;

namespace music.Tests;

public class PlaybackQueueTests
{
    [Fact]
    public void SinglePlayReplacesOldQueue()
    {
        var queue = new PlaybackQueue<Song>();
        queue.SetPlaylist(new[] { Song("1"), Song("2") }, 1);
        var current = queue.SetSingle(Song("3"));

        Assert.Equal("3", current.Id);
        Assert.Equal(1, queue.Count);
        Assert.False(queue.HasNext);
        Assert.Equal("3", queue.MoveNext()!.Id);
    }

    [Fact]
    public void ListPlayUsesSameQueueForNavigation()
    {
        var queue = new PlaybackQueue<Song>();
        var current = queue.SetPlaylist(new[] { Song("1"), Song("2"), Song("3") }, 1);

        Assert.Equal("2", current.Id);
        Assert.True(queue.HasNext);
        Assert.Equal("3", queue.MoveNext()!.Id);
        Assert.Equal("2", queue.MovePrevious()!.Id);
        Assert.Equal("1", queue.MovePrevious()!.Id);
    }

    [Fact]
    public void LocalFilePathIsPreservedAcrossQueueNavigation()
    {
        var local = Song("local:track");
        local.LocalFilePath = @"C:\Music\Artist - Track.flac";
        var queue = new PlaybackQueue<Song>();
        queue.SetPlaylist(new[] { local, Song("next") }, 1);

        Assert.Equal(@"C:\Music\Artist - Track.flac", queue.MovePrevious()!.LocalFilePath);
    }

    private static Song Song(string id) => new() { Id = id, Name = id };
}
