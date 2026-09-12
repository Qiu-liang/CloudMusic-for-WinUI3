# CloudMusic for WinUI 3

**中文** | [English](README_EN.md)

基于 WinUI 3 的网易云音乐第三方客户端，使用 [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) 作为后端 API。

> 本项目仅用于学习和研究目的，不得用于商业用途。音乐版权归网易云音乐所有。

## 截图

<img width="1798" height="1183" alt="57339c9393ba52db6a446c2d03d413af" src="https://github.com/user-attachments/assets/4d5a8426-36a7-41b5-8146-2d4e824365af" />

## 功能特性

### 音乐发现
- **每日推荐** — 基于个人口味的每日歌曲推荐
- **个性化歌单** — 精选歌单推荐
- **雷达歌单** — 根据听歌习惯自动生成
- **智能推荐** — 喜欢的歌曲 + 相似推荐混合播放
- **猜你喜欢** — 猜你喜欢的歌曲
- **全部推荐歌单** — 浏览所有推荐歌单

### 搜索与浏览
- **全局搜索** — 统一搜索歌曲、歌手、专辑、歌单
- **搜索建议** — 输入时实时显示搜索建议
- **歌手详情** — 歌手主页、热门歌曲、专辑列表
- **歌单详情** — 歌单信息、歌曲列表、收藏者
- **专辑详情** — 专辑信息、歌曲列表

### 播放体验
- **播放控制** — 播放/暂停、上一首/下一首、进度拖拽、音量控制
- **播放模式** — 顺序播放、单曲循环、列表循环、随机播放
- **音质选择** — 标准、较高、极高、无损、Hi-Res、沉浸环绕声、杜比全景声、超清母带
- **歌词显示** — 全屏歌词页面，支持逐行同步高亮
- **播放列表** — 侧边栏播放列表，快速切换歌曲
- **迷你播放栏** — 底部悬浮播放栏，显示当前播放信息

### 我的音乐
- **我喜欢的音乐** — 查看和管理红心歌曲
- **最近播放** — 查看播放历史记录
- **已下载** — 查看已下载的歌曲
- **本地音乐** — 扫描本地文件夹中的音乐文件 (mp3/flac/wav/aac/ogg/m4a)
- **音乐云盘** — 上传、播放、删除云盘歌曲

### 社交与互动
- **收藏歌曲** — 喜欢/取消喜欢歌曲
- **收藏歌单** — 收藏/取消收藏歌单

### 账户与设置
- **多种登录** — 手机号密码登录、二维码扫码登录、匿名登录
- **VIP 状态** — 显示会员状态和 VIP 标识
- **主题切换** — 跟随系统、浅色、深色三种主题
- **Mica 背景** — Windows 11 Mica 材质效果
- **API 配置** — 自定义后端 API 服务器地址
- **侧边栏宽度** — 自由调整侧边栏宽度

## 环境要求

| 组件 | 要求 |
|------|------|
| 操作系统 | Windows 10 版本 1809 (10.0.17763.0) 或更高 |
| .NET SDK | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 后端服务 | [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) |

## 快速开始

### 1. 启动 API 服务端

```bash
# 克隆 API 项目
git clone https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced.git
cd api-enhanced

# 安装依赖并启动
pnpm i
node app.js
```

服务默认运行在 `http://localhost:3000`。更多部署方式请参考 [NeteaseCloudMusicApiEnhanced 文档](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced)。

### 2. 编译运行

```bash
# 克隆项目
git clone <your-repo-url>
cd CloudMusic-for-WinUI3

# 编译 (x64)
dotnet build music/music.csproj -p:Platform=x64

# 运行
dotnet run --project music/music.csproj -p:Platform=x64
```

也可以在 Visual Studio 中打开 `music.slnx`，选择 x64 平台后按 F5 运行。

### 3. 配置 API 地址

首次运行时，进入 **设置** 页面，将 API 服务器地址修改为你的 NeteaseCloudMusicApi 地址（如 `http://localhost:3000`），然后点击 **测试连接** 验证。

## 项目结构

```
CloudMusic-for-WinUI3/
├── music.slnx                      # 解决方案文件
├── LICENSE                          # MIT 许可证
├── README.md                        # 中文说明
├── README_EN.md                     # 英文说明
│
└── music/
    ├── App.xaml / .cs               # 应用程序入口
    ├── MainWindow.xaml / .cs        # 主窗口（导航、搜索栏、播放栏、播放列表）
    ├── music.csproj                 # 项目文件
    ├── Package.appxmanifest         # 应用清单
    │
    ├── Pages/                       # 页面
    │   ├── RecommendPage            # 推荐页（默认首页）
    │   ├── RecommendDailySongsPage  # 每日推荐完整列表
    │   ├── RecommendRadarPage       # 雷达歌单
    │   ├── AllPlaylistsPage         # 全部推荐歌单
    │   ├── LikedPage                # 喜欢的歌曲
    │   ├── RecentPage               # 最近播放
    │   ├── DownloadedPage           # 下载管理（容器）
    │   ├── DownloadPage             # 已下载歌曲
    │   ├── LocalPage                # 本地音乐
    │   ├── CloudPage                # 音乐云盘
    │   ├── SearchPage               # 搜索结果
    │   ├── SearchAllSongsPage       # 搜索结果 - 歌曲
    │   ├── SearchAllResultsPage     # 搜索结果 - 歌单/专辑
    │   ├── PlaylistDetailPage       # 歌单/专辑详情
    │   ├── ArtistDetailPage         # 歌手详情
    │   ├── ArtistAlbumsPage         # 歌手专辑列表
    │   ├── ArtistInfoPage           # 歌手简介
    │   ├── ArtistTopSongsPage       # 歌手热门歌曲
    │   ├── LyricsPage               # 全屏歌词
    │   ├── SettingsPage             # 设置
    │   └── ViewModels.cs            # 视图模型类 (SongItem/PlaylistItem 等)
    │
    ├── Services/
    │   ├── MusicApiService.cs       # 网易云音乐 API 客户端（登录、搜索、推荐、歌单等）
    │   └── PlaybackService.cs       # 播放服务（播放器封装、播放列表管理）
    │
    ├── Models/
    │   └── RecommendModels.cs       # 数据模型 (Song/Artist/Album)
    │
    ├── Dialogs/
    │   └── LoginDialog.xaml / .cs   # 登录弹窗（手机登录/二维码登录）
    │
    └── Assets/                      # 应用图标和资源
```

## 技术栈

| 类别 | 技术 |
|------|------|
| 运行时 | .NET 8 |
| 目标框架 | Windows 10.0.19041.0 |
| UI 框架 | WinUI 3 (Windows App SDK 2.1.3) |
| 设计语言 | Fluent Design System |
| 媒体播放 | Windows.Media.Playback.MediaPlayer |
| 网络请求 | System.Net.Http |
| JSON 序列化 | System.Text.Json |
| 数据持久化 | ApplicationData.LocalSettings |
| 构建工具 | Microsoft.Windows.SDK.BuildTools |

## 架构概述

项目采用 **Code-Behind** 模式，页面逻辑直接写在 `.cs` 后台文件中。

### 核心组件

- **MusicApiService** — 封装所有网易云音乐 API 调用，管理登录状态和 Cookie，处理请求队列（SemaphoreSlim 限流）
- **PlaybackService** — 封装 MediaPlayer，管理播放列表、播放模式（顺序/循环/随机）、音量控制
- **ViewModels** — 轻量级视图模型（SongItem/PlaylistItem），实现 INotifyPropertyChanged

### 页面导航

- **NavigationView** — 左侧导航栏，包含推荐、关注、我的音乐、歌单列表
- **Frame** — 内容区域导航，支持页面前进/后退
- **播放栏** — 底部悬浮播放控件，半透明背景

## 数据存储

所有用户配置存储在 `ApplicationData.Current.LocalSettings` 中：

| 键 | 说明 |
|---|---|
| `ServerAddress` | API 服务器地址 |
| `Cookie` | 登录凭证 |
| `UserId` / `Nickname` / `AvatarUrl` | 用户信息 |
| `IsVip` | VIP 状态 |
| `Theme` | 主题模式 (System/Light/Dark) |
| `AudioQuality` | 音质设置 |
| `SidebarWidth` | 侧边栏宽度 |
| `LocalMusicFolders` | 本地音乐文件夹列表 |
| `DeviceId` | 设备标识 |

下载的音频文件保存在 `LocalFolder/Downloads/` 目录。

## 开发指南

### 构建平台

项目支持以下平台：
- x86
- x64
- ARM64

### 发布

Release 配置下会生成自包含 (SelfContained) 的独立 exe，无需安装 .NET 运行时：

```bash
dotnet publish music/music.csproj -c Release -p:Platform=x64
```

## 许可证

本项目采用 [MIT 许可证](LICENSE)。

## 致谢

- [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) — 网易云音乐 API
- [Binaryify/NeteaseCloudMusicApi](https://github.com/Binaryify/NeteaseCloudMusicApi) — 原始网易云音乐 API
- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) — WinUI 3 开发框架
