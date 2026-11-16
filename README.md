# CloudMusic for WinUI 3

**中文** | [English](README_EN.md)

基于 WinUI 3 的网易云音乐第三方客户端，使用 [NeteaseCloudMusicApiEnhanced](https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced) 作为后端 API。
注意，本项目不含有该后端API服务，请自行部署。

> 本项目仅用于学习和研究目的，不得用于商业用途。音乐版权归网易云音乐所有。

## 截图

<img width="1446" height="1057" alt="QQ20261109-175745" src="https://github.com/user-attachments/assets/6ac891b3-f5d4-451a-b231-2082eb89cc24" />


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

# 编译解决方案 (x64)
dotnet build music.slnx -p:Platform=x64

# 运行应用
dotnet run --project music/music.csproj -p:Platform=x64
```

也可以在 Visual Studio 中打开 `music.slnx`，选择 x64 平台后按 F5 运行。

### 3. 配置 API 地址

应用默认使用 `http://localhost:3000`。进入 **设置** 页面可修改 API 地址并点击 **测试连接**。本机 HTTP 可直接使用；远程部署建议配置 HTTPS，客户端不会通过远程明文 HTTP 发送手机号密码。

## 项目结构

```
CloudMusic-for-WinUI3/
├── music.slnx                          # 解决方案文件
├── global.json                         # Microsoft.Testing.Platform 测试运行器配置
├── .github/workflows/ci.yml            # GitHub Actions CI（x64 构建与单元测试）
├── tests/music.Tests.csproj            # xUnit v3 单元测试与架构守卫工程
├── LICENSE                             # MIT 许可证
├── README.md                           # 中文说明
├── README_EN.md                        # 英文说明
│
├── music.Core/                         # 纯 .NET 8 业务核心层（不依赖 WinUI）
│   ├── music.Core.csproj               # 核心类库项目文件
│   ├── Abstractions/                   # 平台与 UI 抽象接口（IApiLog/IUiDispatcher/IMediaPlayer/INavigationService 等）
│   ├── Models/                         # 领域模型与 DTO（Song/Artist/Album/PlaybackTypes/ApiDtos）
│   ├── Mvvm/                           # MVVM 基础设施（ObservableObject/PageViewModelBase/AsyncRelayCommand）
│   ├── Netease/                        # HTTP 传输、端点管理、Cookie 罐、URL 策略与 JSON Mapping
│   ├── Results/                        # 统一响应结果模型（ApiResult / ApiResult<T>）
│   ├── Services/                       # 业务服务（Session/Library/Playlist/Artist/Recommendation/Search/Cloud/Profile/Playback/Download/Lyrics 等）
│   └── ViewModels/                     # 页面与窗口 ViewModel（Shell/PlayerBar/Recommend/Search/PlaylistDetail/LyricsWindow/Settings 等）
│
└── music/                              # WinUI 3 客户端应用层
    ├── App.xaml / .cs                  # 应用程序入口
    ├── MainWindow.xaml / .cs           # 主窗口（导航框架、搜索框、播放栏、播放队列面板）
    ├── LyricsWindow.xaml / .cs         # 独立桌面歌词窗口（Win2D 绘制、点击穿透、译文同步）
    ├── music.csproj                    # 应用项目文件
    ├── Package.appxmanifest            # 应用清单
    │
    ├── Composition/                    # 组合根
    │   └── AppCompositionRoot.cs       # 手动依赖注入、路由注册与窗口/服务装配
    │
    ├── Infrastructure/Platform/        # Windows / WinUI 3 平台端口实现
    │   ├── DialogService.cs            # 弹窗与登录框服务
    │   ├── FileApiLog.cs               # 本地日志文件写入
    │   ├── LocalSettingsStore.cs       # LocalSettings 封装
    │   ├── MediaPlayerAdapter.cs       # Windows MediaPlayer 适配器
    │   ├── NavigationService.cs        # Frame 页面导航与路由请求解析
    │   ├── PickerThemeAndLauncherServices.cs # 文件选择器、主题切换与歌词窗启动器
    │   ├── WindowHost.cs               # 主窗口宿主引用
    │   ├── WindowsCredentialStore.cs   # Windows 凭据保管库 / DPAPI Cookie 存储
    │   └── WinUiDispatcher.cs          # DispatcherQueue 调度器适配
    │
    ├── Controls/                       # 自定义控件（MarqueeText 等）
    ├── Dialogs/                        # 弹窗（LoginDialog.xaml / .cs）
    ├── Pages/                          # 页面视图与 Code-Behind（XAML + 轻量交互/动效）
    └── Assets/                         # 应用图标和资源
```

## 技术栈

| 类别 | 技术 |
|------|------|
| 运行时 | .NET 8 |
| 目标框架 | Windows 10.0.19041.0 |
| UI 框架 | WinUI 3 (Windows App SDK 2.1.3) |
| 设计语言 | Fluent Design System |
| 媒体播放 | Windows.Media.Playback.MediaPlayer |
| 歌词渲染 | Win2D (Microsoft.Graphics.Win2D) |
| 网络请求 | System.Net.Http |
| JSON 序列化 | System.Text.Json |
| 数据持久化 | ApplicationData.LocalSettings / PasswordVault + DPAPI |
| 构建工具 | Microsoft.Windows.SDK.BuildTools |

## 架构概述

项目采用 **MVVM + Clean Core** 分层架构，将核心业务逻辑与 WinUI 3 平台/视图解耦：

### 核心分层

- **music.Core** — 纯 .NET 8 类库，包含网络传输（`NeteaseHttpTransport` + `SemaphoreSlim` 限流）、JSON 映射、统一 `ApiResult` 错误模型、各领域服务（`SessionService` / `LibraryService` / `PlaylistService` / `PlaybackCoordinator` 等）以及页面 `ViewModel`。
- **music/Composition** — `AppCompositionRoot` 负责统一装配服务、平台端口与页面路由工厂，View 不直接访问全局静态服务或 `HttpClient`。
- **music/Infrastructure/Platform** — 实现 `music.Core.Abstractions` 中定义的端口（`IUiDispatcher`、`IMediaPlayer`、`INavigationService`、`IDialogService`、`ICredentialStore`、`ISettingsStore` 等）。
- **music/Pages & Windows** — 负责 XAML 数据绑定（`{x:Bind}`）、页面生命周期管理，以及 Win2D 歌词绘制、Win32 窗口互操作与横向滚轮动画等纯 UI 逻辑。

### 页面导航

- **NavigationView** — 左侧导航栏，包含推荐、我喜欢的音乐、下载、云盘与歌单列表
- **Frame** — 内容区域导航，支持页面前进/后退
- **播放栏** — 底部悬浮播放控件，半透明背景

## 数据存储

所有用户配置存储在 `ApplicationData.Current.LocalSettings` 中：

| 键 | 说明 |
|---|---|
| `ServerAddress` | API 服务器地址 |
| `UserId` / `Nickname` / `AvatarUrl` | 用户信息 |
| `IsVip` | VIP 状态 |
| `Theme` | 主题模式 (System/Light/Dark) |
| `AudioQuality` | 音质设置 |
| `SidebarWidth` | 侧边栏宽度 |
| `LocalMusicFolders` | 本地音乐文件夹列表 |
| `DownloadFolder` | 自定义下载目录 |
| `FancyLyrics` | 独立桌面歌词窗口开关 |
| `DeviceId` | 设备标识 |

登录 Cookie 保存在 Windows 凭据保管库（不可用时回退到当前用户 DPAPI 加密），不会以明文写入 `LocalSettings`。默认下载目录为系统“音乐”文件夹下的 `CloudMusic for WinUI`（不可用时回退到 `LocalFolder/Downloads/`）。

## 测试

```bash
# 运行全部单元测试与架构守卫测试（使用 global.json 配置的 Microsoft.Testing.Platform）
dotnet test

# 或直接指定测试工程
dotnet test tests/music.Tests.csproj

# 也可使用 xUnit 进程内运行器直接运行
dotnet run --project tests/music.Tests.csproj
```

测试覆盖队列调度、本地路径安全、歌词时间戳对齐、喜欢列表分批、上传/业务错误语义、命令线程调度、主题恢复、ViewModel 状态机与分层架构守卫。

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
