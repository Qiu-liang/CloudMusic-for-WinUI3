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

# Build solution (x64)
dotnet build music.slnx -p:Platform=x64

# Run
dotnet run --project music/music.csproj -p:Platform=x64
```

Alternatively, open `music.slnx` in Visual Studio, select the x64 platform, and press F5.

### 3. Configure the API Address

The app defaults to `http://localhost:3000`. Use the **Settings** page to change the API address and click **Test Connection**. Local HTTP works out of the box; use HTTPS for remote deployments. The client will not send phone credentials over remote plain HTTP.

## Project Structure

```
CloudMusic-for-WinUI3/
├── music.slnx                          # Solution file
├── global.json                         # Microsoft.Testing.Platform runner config
├── .github/workflows/ci.yml            # GitHub Actions CI (x64 build & tests)
├── tests/music.Tests.csproj            # xUnit v3 unit & architecture guard tests
├── LICENSE                             # MIT License
├── README.md                           # Chinese documentation
├── README_EN.md                        # English documentation
│
├── music.Core/                         # Pure .NET 8 business core (WinUI-free)
│   ├── music.Core.csproj               # Core library project file
│   ├── Abstractions/                   # Platform & UI ports (IApiLog/IUiDispatcher/IMediaPlayer/INavigationService, etc.)
│   ├── Models/                         # Domain models & DTOs (Song/Artist/Album/PlaybackTypes/ApiDtos)
│   ├── Mvvm/                           # MVVM infrastructure (ObservableObject/PageViewModelBase/AsyncRelayCommand)
│   ├── Netease/                        # HTTP transport, endpoints, cookie jar, URL policy & JSON mappers
│   ├── Results/                        # Unified result types (ApiResult / ApiResult<T>)
│   ├── Services/                       # Domain services (Session/Library/Playlist/Artist/Recommendation/Search/Cloud/Profile/Playback/Download/Lyrics, etc.)
│   └── ViewModels/                     # Page & window ViewModels (Shell/PlayerBar/Recommend/Search/PlaylistDetail/LyricsWindow/Settings, etc.)
│
└── music/                              # WinUI 3 client application layer
    ├── App.xaml / .cs                  # Application entry point
    ├── MainWindow.xaml / .cs           # Main window (navigation, search bar, player bar, play queue)
    ├── LyricsWindow.xaml / .cs         # Standalone desktop lyrics window (Win2D rendering, click-through, translations)
    ├── music.csproj                    # Application project file
    ├── Package.appxmanifest            # Application manifest
    │
    ├── Composition/                    # Composition root
    │   └── AppCompositionRoot.cs       # Manual DI wiring, route registration, and window/service creation
    │
    ├── Infrastructure/Platform/        # Windows / WinUI 3 platform adapters
    │   ├── DialogService.cs            # Content dialogs & login dialog service
    │   ├── FileApiLog.cs               # Local debug log file writer
    │   ├── LocalSettingsStore.cs       # LocalSettings adapter
    │   ├── MediaPlayerAdapter.cs       # Windows MediaPlayer adapter
    │   ├── NavigationService.cs        # Frame navigation & route request resolution
    │   ├── PickerThemeAndLauncherServices.cs # File/folder pickers, theme service, and lyrics window launcher
    │   ├── WindowHost.cs               # Main window host reference
    │   ├── WindowsCredentialStore.cs   # Windows Credential Locker / DPAPI cookie storage
    │   └── WinUiDispatcher.cs          # DispatcherQueue adapter
    │
    ├── Controls/                       # Custom controls (MarqueeText, etc.)
    ├── Dialogs/                        # Dialogs (LoginDialog.xaml / .cs)
    ├── Pages/                          # XAML pages & lightweight UI code-behind
    └── Assets/                         # App icons and resources
```

## Tech Stack

| Category | Technology |
|----------|------------|
| Runtime | .NET 8 |
| Target Framework | Windows 10.0.19041.0 |
| UI Framework | WinUI 3 (Windows App SDK 2.1.3) |
| Design Language | Fluent Design System |
| Media Playback | Windows.Media.Playback.MediaPlayer |
| Lyrics Rendering | Win2D (Microsoft.Graphics.Win2D) |
| Networking | System.Net.Http |
| JSON Serialization | System.Text.Json |
| Data Persistence | ApplicationData.LocalSettings / PasswordVault + DPAPI |
| Build Tools | Microsoft.Windows.SDK.BuildTools |

## Architecture Overview

The project uses an **MVVM + Clean Core** architecture that decouples business logic from WinUI 3 views and platform APIs:

### Core Layers

- **music.Core** — Pure .NET 8 library containing HTTP transport (`NeteaseHttpTransport` with `SemaphoreSlim` throttling), JSON mappers, unified `ApiResult` error semantics, domain services (`SessionService`, `LibraryService`, `PlaylistService`, `PlaybackCoordinator`, etc.), and `ViewModels`.
- **music/Composition** — `AppCompositionRoot` wires services, platform adapters, and route factories without global static service access from Views.
- **music/Infrastructure/Platform** — Implements the ports defined in `music.Core.Abstractions` (`IUiDispatcher`, `IMediaPlayer`, `INavigationService`, `IDialogService`, `ICredentialStore`, `ISettingsStore`, etc.).
- **music/Pages & Windows** — Handles XAML `{x:Bind}` bindings, page navigation lifecycle, and UI-specific concerns such as Win2D lyrics rendering, Win32 window interop, and horizontal scroll animations.

### Page Navigation

- **NavigationView** — Left navigation pane containing Recommendations, Liked Songs, Downloads, Cloud Drive, and user playlists
- **Frame** — Content area navigation with forward/backward page support
- **Player Bar** — Floating playback control at the bottom with a translucent background

## Data Storage

All user configuration is stored in `ApplicationData.Current.LocalSettings`:

| Key | Description |
|---|---|
| `ServerAddress` | API server address |
| `UserId` / `Nickname` / `AvatarUrl` | User information |
| `IsVip` | VIP status |
| `Theme` | Theme mode (System/Light/Dark) |
| `AudioQuality` | Audio quality setting |
| `SidebarWidth` | Sidebar width |
| `LocalMusicFolders` | List of local music folders |
| `DownloadFolder` | Custom download folder |
| `FancyLyrics` | Desktop lyrics window toggle |
| `DeviceId` | Device identifier |

The login cookie is stored in Windows Credential Locker (falling back to current-user DPAPI encryption) and never as plaintext in `LocalSettings`. By default, downloaded audio files are saved to `CloudMusic for WinUI` inside the user's Music folder (falling back to `LocalFolder/Downloads/`).

## Tests

```bash
# Run all unit and architecture guard tests (via Microsoft.Testing.Platform configured in global.json)
dotnet test

# Or target the test project explicitly
dotnet test tests/music.Tests.csproj

# Alternatively run via the xUnit in-process runner
dotnet run --project tests/music.Tests.csproj
```

Tests cover queue navigation, local file paths, out-of-order lyrics, liked-list batching, error semantics, command UI-thread dispatching, theme restoration, ViewModel state machines, and layer architecture guards.

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
