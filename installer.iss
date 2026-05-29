[Setup]
AppName=Cloud Music
AppVersion=1.0.0
DefaultDirName={autopf}\Cloud Music
DefaultGroupName=Cloud Music
OutputDir=installer
OutputBaseFilename=CloudMusicSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Cloud Music"; Filename: "{app}\music.exe"
Name: "{autodesktop}\Cloud Music"; Filename: "{app}\music.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\music.exe"; Description: "启动应用"; Flags: nowait postinstall skipifsilent