# Cloud Music

一个基于 WinUI 3 的网易云音乐第三方客户端，使用 [NeteaseCloudMusicApi](https://github.com/Binaryify/NeteaseCloudMusicApi) 作为后端 API。

## 功能特性

- **音乐推荐** — 每日推荐、个性化歌单、雷达歌单、智能推荐（喜欢的+相似）、猜你喜欢
- **音乐搜索** — 统一搜索歌曲、歌手、专辑、歌单
- **播放控制** — 播放/暂停、上一首/下一首、进度拖拽、音量控制
- **播放模式** — 顺序播放、单曲循环、列表循环、随机播放
- **音质选择** — 标准、较高、极高、无损、Hi-Res、沉浸环绕、全景声、杜比、超清母带
- **歌词显示** — 全屏歌词页面，支持逐行同步高亮
- **歌单管理** — 查看创建的歌单和收藏的歌单
- **收藏功能** — 喜欢/取消喜欢歌曲
- **社交功能** — 关注/取关用户，查看粉丝和关注列表
- **云盘管理** — 上传、播放、删除云盘歌曲
- **本地音乐** — 扫描本地文件夹中的音乐文件 (mp3/flac/wav/aac/ogg/m4a)
- **下载管理** — 查看已下载的歌曲
- **登录方式** — 手机号密码登录、二维码扫码登录、匿名登录
- **主题切换** — 系统、浅色、深色三种主题
- **Mica 背景** — Windows 11 Mica 材质效果
- **用户信息** — 显示 VIP 状态、头像、昵称

## 截图

<img width="1798" height="1183" alt="57339c9393ba52db6a446c2d03d413af" src="https://github.com/user-attachments/assets/4d5a8426-36a7-41b5-8146-2d4e824365af" />


## 环境要求

- Windows 10 版本 1809 (10.0.17763.0) 或更高
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [NeteaseCloudMusicApi](https://github.com/Binaryify/NeteaseCloudMusicApi) 服务端（本地或远程部署）

## 快速开始

### 1. 启动 API 服务端

```bash
# 克隆并启动 NeteaseCloudMusicApi
git clone https://github.com/Binaryify/NeteaseCloudMusicApi.git
cd NeteaseCloudMusicApi
node app.js
```

默认监听地址：`http://localhost:3000`

### 2. 编译运行

```bash
git clone <your-repo-url>
cd mighty-forest

# 编译
dotnet build music/music.csproj -p:Platform=x64

# 运行
dotnet run --project music/music.csproj -p:Platform=x64
```

也可以在 Visual Studio 2022 中打开 `music.slnx`，选择 x64 平台后按 F5 运行。

### 3. 配置 API 地址

首次运行时，进入 **设置** 页面，将 API 服务器地址修改为你的 NeteaseCloudMusicApi 地址（如 `http://localhost:3000` 或 `http://192.168.31.205:3000`），然后点击 **测试连接** 验证。

## 项目结构

```
music/
├── App.xaml / .cs              # 应用程序入口
├── MainWindow.xaml / .cs       # 主窗口（导航、搜索栏、播放栏）
├── music.csproj                 # 项目文件
├── Package.appxmanifest         # 应用清单
│
├── Pages/                       # 页面
│   ├── RecommendPage            # 推荐页（默认首页）
│   ├── FollowPage               # 关注/粉丝页
│   ├── LikedPage                # 喜欢的歌曲
│   ├── RecentPage               # 最近播放
│   ├── DownloadedPage           # 下载管理 (容器)
│   ├── DownloadPage             # 已下载歌曲
│   ├── LocalPage                # 本地音乐
│   ├── CloudPage                # 云盘
│   ├── SearchPage               # 搜索结果
│   ├── SearchAllSongsPage       # 全部搜索结果-歌曲
│   ├── SearchAllResultsPage     # 全部搜索结果-歌单/专辑
│   ├── PlaylistDetailPage       # 歌单/专辑详情
│   ├── RecommendDailySongsPage  # 每日推荐/完整列表
│   ├── RecommendRadarPage       # 雷达歌单
│   ├── AllPlaylistsPage         # 全部推荐歌单
│   ├── LyricsPage               # 全屏歌词
│   ├── SettingsPage             # 设置
│   ├── HomePage                 # 主页（未使用）
│   ├── PlaylistPage             # 占位（未使用）
│   ├── CollectionPage           # 占位（未使用）
│   └── ViewModels.cs            # 视图模型类
│
├── Services/
│   ├── MusicApiService.cs       # 网易云音乐 API 客户端
│   └── PlaybackService.cs       # 播放服务
│
├── Models/
│   └── RecommendModels.cs       # 数据模型 (Song/Artist/Album)
│
├── Dialogs/
│   ├── LoginDialog.xaml / .cs   # 登录弹窗（手机/二维码）
│
└── Assets/                      # 应用图标和资源
```

## 技术栈

- **框架**: .NET 8 + Windows App SDK 2.1.3
- **UI**: WinUI 3 (Fluent Design)
- **媒体**: Windows.Media.Playback.MediaPlayer
- **网络**: System.Net.Http
- **序列化**: System.Text.Json
- **数据持久化**: ApplicationData.LocalSettings
- **构建工具**: Microsoft.Windows.SDK.BuildTools

## 数据存储

所有用户配置存储在 `ApplicationData.Current.LocalSettings` 中：

| 键 | 说明 |
|---|---|
| `ServerAddress` | API 服务器地址 |
| `Cookie` | 登录 Cookie |
| `UserId` / `Nickname` | 用户信息 |
| `Theme` | 主题模式 |
| `AudioQuality` | 音质设置 |
| `SidebarWidth` | 侧边栏宽度 |
| `LocalMusicFolders` | 本地音乐文件夹列表 |
| `DeviceId` | 设备标识 |

下载的音频文件保存在 `LocalFolder/Downloads/` 目录。

## 免责声明

本项目仅用于学习和研究目的，不得用于商业用途。音乐版权归网易云音乐所有。

## 致谢

- [Binaryify/NeteaseCloudMusicApi](https://github.com/Binaryify/NeteaseCloudMusicApi) — 网易云音乐 API
- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK)

