# CloudMusic for WinUI 3

[中文](README.md) | **English**

A third-party NetEase Cloud Music client built with WinUI 3, using [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) as its backend API.

> This project is intended for learning and research purposes only and must not be used commercially. All music copyrights belong to NetEase Cloud Music.

## Screenshots

<img width="1798" height="1183" alt="57339c9393ba52db6a446c2d03d413af" src="https://github.com/user-attachments/assets/4d5a8426-36a7-41b5-8146-2d4e824365af" />

## Features

### Music Discovery
- **Daily Recommendations** — Daily song recommendations based on your personal taste
- **Personalized Playlists** — Hand-picked playlist recommendations
- **Radar Playlists** — Automatically generated from your listening habits
- **Smart Recommendations** — A mix of liked songs and similar recommendations
- **Guess You Like** — Songs you might like
- **All Recommended Playlists** — Browse every recommended playlist

### Search & Browsing
- **Global Search** — Unified search across songs, artists, albums, and playlists
- **Search Suggestions** — Real-time suggestions while typing
- **Artist Details** — Artist page, top songs, and album list
- **Playlist Details** — Playlist info, song list, and subscribers
- **Album Details** — Album info and song list

### Playback Experience
- **Playback Controls** — Play/pause, previous/next, seek, and volume control
- **Playback Modes** — Sequential, single repeat, list loop, and shuffle
- **Audio Quality** — Standard, Higher, Ex-High, Lossless, Hi-Res, Immersive Surround, Dolby Atmos, and Master
- **Lyrics Display** — Full-screen lyrics page with line-by-line synchronized highlighting
- **Play Queue** — Sidebar playlist for quickly switching tracks
- **Mini Player Bar** — Floating bar at the bottom showing the current track

### My Music
- **Liked Songs** — View and manage your hearted songs
- **Recently Played** — Browse your listening history
- **Downloaded** — View downloaded songs
- **Local Music** — Scan local folders for music files (mp3/flac/wav/aac/ogg/m4a)
- **Cloud Drive** — Upload, play, and delete cloud drive songs

### Social & Interaction
- **Like Songs** — Like or unlike songs
- **Subscribe to Playlists** — Subscribe to or unsubscribe from playlists

### Account & Settings
- **Multiple Login Methods** — Phone number with password, QR code, and anonymous login
- **VIP Status** — Displays membership status and VIP badge
- **Theme Switching** — Follow system, light, and dark themes
- **Mica Background** — Windows 11 Mica material effect
- **API Configuration** — Custom backend API server address
- **Sidebar Width** — Freely adjustable sidebar width

## Requirements

| Component | Requirement |
|-----------|-------------|
| Operating System | Windows 10 version 1809 (10.0.17763.0) or later |
| .NET SDK | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Backend Service | [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) |

## Getting Started

### 1. Start the API Server

```bash
# Clone the API project
git clone https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced.git
cd api-enhanced

# Install dependencies and start
pnpm i
node app.js
```

The service runs on `http://localhost:3000` by default. For more deployment options, see the [NeteaseCloudMusicApiEnhanced documentation](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced).

### 2. Build and Run

```bash
# Clone the project
git clone <your-repo-url>
cd CloudMusic-for-WinUI3

# Build (x64)
dotnet build music/music.csproj -p:Platform=x64

# Run
dotnet run --project music/music.csproj -p:Platform=x64
```

Alternatively, open `music.slnx` in Visual Studio, select the x64 platform, and press F5.

### 3. Configure the API Address

On first launch, go to the **Settings** page, change the API server address to your NeteaseCloudMusicApi address (e.g. `http://localhost:3000`), then click **Test Connection** to verify.

## Project Structure

```
CloudMusic-for-WinUI3/
├── music.slnx                      # Solution file
├── LICENSE                          # MIT License
├── README.md                        # Chinese documentation
├── README_EN.md                     # English documentation
│
└── music/
    ├── App.xaml / .cs               # Application entry point
    ├── MainWindow.xaml / .cs        # Main window (navigation, search bar, player bar, playlist)
    ├── music.csproj                 # Project file
    ├── Package.appxmanifest         # Application manifest
    │
    ├── Pages/                       # Pages
    │   ├── RecommendPage            # Recommendations (default home page)
    │   ├── RecommendDailySongsPage  # Full daily recommendation list
    │   ├── RecommendRadarPage       # Radar playlists
    │   ├── AllPlaylistsPage         # All recommended playlists
    │   ├── LikedPage                # Liked songs
    │   ├── RecentPage               # Recently played
    │   ├── DownloadedPage           # Download management (container)
    │   ├── DownloadPage             # Downloaded songs
    │   ├── LocalPage                # Local music
    │   ├── CloudPage                # Cloud drive
    │   ├── SearchPage               # Search results
    │   ├── SearchAllSongsPage       # Search results - songs
    │   ├── SearchAllResultsPage     # Search results - playlists/albums
    │   ├── PlaylistDetailPage       # Playlist/album details
    │   ├── ArtistDetailPage         # Artist details
    │   ├── ArtistAlbumsPage         # Artist album list
    │   ├── ArtistInfoPage           # Artist biography
    │   ├── ArtistTopSongsPage       # Artist top songs
    │   ├── LyricsPage               # Full-screen lyrics
    │   ├── SettingsPage             # Settings
    │   └── ViewModels.cs            # View model classes (SongItem/PlaylistItem, etc.)
    │
    ├── Services/
    │   ├── MusicApiService.cs       # NetEase Cloud Music API client (login, search, recommendations, playlists, etc.)
    │   └── PlaybackService.cs       # Playback service (media player wrapper, playlist management)
    │
    ├── Models/
    │   └── RecommendModels.cs       # Data models (Song/Artist/Album)
    │
    ├── Dialogs/
    │   └── LoginDialog.xaml / .cs   # Login dialog (phone login/QR code login)
    │
    └── Assets/                      # App icons and resources
```

## Tech Stack

| Category | Technology |
|----------|------------|
| Runtime | .NET 8 |
| Target Framework | Windows 10.0.19041.0 |
| UI Framework | WinUI 3 (Windows App SDK 2.1.3) |
| Design Language | Fluent Design System |
| Media Playback | Windows.Media.Playback.MediaPlayer |
| Networking | System.Net.Http |
| JSON Serialization | System.Text.Json |
| Data Persistence | ApplicationData.LocalSettings |
| Build Tools | Microsoft.Windows.SDK.BuildTools |

## Architecture Overview

The project follows a **Code-Behind** pattern, with page logic written directly in the `.cs` code-behind files.

### Core Components

- **MusicApiService** — Wraps all NetEase Cloud Music API calls, manages login state and cookies, and handles the request queue (rate limited via SemaphoreSlim)
- **PlaybackService** — Wraps MediaPlayer and manages the playlist, playback modes (sequential/loop/shuffle), and volume control
- **ViewModels** — Lightweight view models (SongItem/PlaylistItem) implementing INotifyPropertyChanged

### Page Navigation

- **NavigationView** — Left navigation pane containing Recommendations, Following, My Music, and playlist list
- **Frame** — Content area navigation with forward/backward page support
- **Player Bar** — Floating playback control at the bottom with a translucent background

## Data Storage

All user configuration is stored in `ApplicationData.Current.LocalSettings`:

| Key | Description |
|---|---|
| `ServerAddress` | API server address |
| `Cookie` | Login credentials |
| `UserId` / `Nickname` / `AvatarUrl` | User information |
| `IsVip` | VIP status |
| `Theme` | Theme mode (System/Light/Dark) |
| `AudioQuality` | Audio quality setting |
| `SidebarWidth` | Sidebar width |
| `LocalMusicFolders` | List of local music folders |
| `DeviceId` | Device identifier |

Downloaded audio files are saved to the `LocalFolder/Downloads/` directory.

## Development Guide

### Build Platforms

The project supports the following platforms:
- x86
- x64
- ARM64

### Publishing

The Release configuration produces a self-contained standalone exe that does not require the .NET runtime to be installed:

```bash
dotnet publish music/music.csproj -c Release -p:Platform=x64
```

## License

This project is licensed under the [MIT License](LICENSE).

## Acknowledgements

- [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) — NetEase Cloud Music API
- [Binaryify/NeteaseCloudMusicApi](https://github.com/Binaryify/NeteaseCloudMusicApi) — Original NetEase Cloud Music API
- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) — WinUI 3 development framework
