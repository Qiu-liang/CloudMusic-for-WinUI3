using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using music.Core.Abstractions;
using music.Core.Models;
using music.Core.Results;
using music.Core.Services;
using Xunit;

namespace music.Tests;

public class PlaybackCoordinatorTests
{
    private sealed class FakeMediaPlayer : IMediaPlayer
    {
        public double Volume { get; set; }
        public bool HasSource { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan NaturalDuration { get; set; } = TimeSpan.FromMinutes(3);
        public List<string> LoadedUris { get; } = new();
        public List<string> LoadedLocalPaths { get; } = new();
        public int PlayCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public bool Disposed { get; private set; }

        public event EventHandler<bool>? PlaybackStateChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
        public event EventHandler? MediaEnded;
        public event EventHandler<string>? MediaFailed;

        public Task LoadUriAsync(string uri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadedUris.Add(uri);
            HasSource = true;
            return Task.CompletedTask;
        }

        public Task LoadLocalFileAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadedLocalPaths.Add(filePath);
            HasSource = true;
            return Task.CompletedTask;
        }

        public void Play()
        {
            PlayCalls++;
            PlaybackStateChanged?.Invoke(this, true);
        }

        public void Pause()
        {
            PauseCalls++;
            PlaybackStateChanged?.Invoke(this, false);
        }

        public void RaiseEnded() => MediaEnded?.Invoke(this, EventArgs.Empty);
        public void RaiseFailed(string detail) => MediaFailed?.Invoke(this, detail);
        public void RaisePosition(TimeSpan pos) => PositionChanged?.Invoke(this, pos);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSourceResolver : ISongSourceResolver
    {
        private readonly Func<string, CancellationToken, Task<(string, ApiResult)>> _resolver;

        public FakeSourceResolver(Func<string, CancellationToken, Task<(string, ApiResult)>> resolver)
        {
            _resolver = resolver;
        }

        public Task<(string Url, ApiResult Result)> ResolvePlaybackUrlAsync(string songId, CancellationToken cancellationToken = default) =>
            _resolver(songId, cancellationToken);
    }

    [Fact]
    public async Task RapidTrackSwitchCancelsPreviousSourceAndStaleResponseDoesNotOverwriteNewSong()
    {
        var player = new FakeMediaPlayer();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = new FakeSourceResolver(async (id, ct) =>
        {
            if (id == "1")
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return ("http://cdn/1.mp3", new ApiResult(true, ApiErrorKind.None, string.Empty, 200));
            }
            return ($"http://cdn/{id}.mp3", new ApiResult(true, ApiErrorKind.None, string.Empty, 200));
        });

        using var coord = new PlaybackCoordinator(player, resolver, new ImmediateUiDispatcher(), new RecordingApiLog());
        var s1 = new Song { Id = "1", Name = "Song1" };
        var s2 = new Song { Id = "2", Name = "Song2" };

        var firstPlay = coord.PlayAsync(s1);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await coord.PlayAsync(s2);
        releaseFirst.SetResult();
        await firstPlay;

        Assert.Equal("2", coord.CurrentSong?.Id);
        Assert.Single(player.LoadedUris);
        Assert.Equal("http://cdn/2.mp3", player.LoadedUris[0]);
    }

    [Fact]
    public async Task LocalPathsSurviveNextAndPreviousNavigation()
    {
        var player = new FakeMediaPlayer();
        var resolver = new FakeSourceResolver((_, _) => Task.FromResult((string.Empty, ApiResult.NetworkError())));
        using var coord = new PlaybackCoordinator(player, resolver, new ImmediateUiDispatcher(), new RecordingApiLog());

        var s1 = LocalSongFactory.Create("A", "X", @"C:\Music\a.mp3");
        var s2 = LocalSongFactory.Create("B", "Y", @"C:\Music\b.flac");
        await coord.PlayLocalFilesAsync([s1.LocalFilePath!, s2.LocalFilePath!], [s1, s2], 0);
        await coord.NextAsync();
        await coord.PreviousAsync();

        Assert.Equal(3, player.LoadedLocalPaths.Count);
        Assert.EndsWith("a.mp3", player.LoadedLocalPaths[0]);
        Assert.EndsWith("b.flac", player.LoadedLocalPaths[1]);
        Assert.EndsWith("a.mp3", player.LoadedLocalPaths[2]);
    }

    [Fact]
    public async Task RepeatModesHandleMediaEndedAndVolumeClamps()
    {
        var player = new FakeMediaPlayer();
        var resolver = new FakeSourceResolver((id, _) =>
            Task.FromResult(($"http://cdn/{id}.mp3", new ApiResult(true, ApiErrorKind.None, string.Empty, 200))));
        using var coord = new PlaybackCoordinator(player, resolver, new ImmediateUiDispatcher(), new RecordingApiLog());

        Assert.Equal(0.8, coord.Volume, 3);
        coord.Volume = 2.5;
        Assert.Equal(1.0, coord.Volume, 3);
        coord.Volume = -1.0;
        Assert.Equal(0.0, coord.Volume, 3);

        var songs = new List<Song> { new() { Id = "1", Name = "S1" }, new() { Id = "2", Name = "S2" } };
        await coord.PlayAsync(songs, 0);

        // RepeatMode.None at index 0 -> advances to 2
        player.RaiseEnded();
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal("2", coord.CurrentSong?.Id);

        // RepeatMode.None at last song -> stops advancing
        player.RaiseEnded();
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal("2", coord.CurrentSong?.Id);

        // RepeatMode.All at last song -> wraps to 1
        coord.ToggleRepeat(); // All
        player.RaiseEnded();
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal("1", coord.CurrentSong?.Id);

        // RepeatMode.One -> replays 1
        coord.ToggleRepeat(); // One
        player.RaiseEnded();
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.Equal("1", coord.CurrentSong?.Id);
        Assert.Equal(4, player.LoadedUris.Count);
    }
}
