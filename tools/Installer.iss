#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef PackageDir
  #error PackageDir is required
#endif
#define AppVersion "1.2.1"
[Setup]
AppId={{F47CC08E-308D-45FD-A1DD-6FDDA408097A}
AppName=联电数据收集
AppVersion={#AppVersion}
AppPublisher=联电
DefaultDirName={localappdata}\Programs\LianDianDataCollection
DefaultGroupName=联电数据收集
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir={#PackageDir}
OutputBaseFilename=LianDianDataCollection-Setup-{#AppVersion}-x64
SetupIconFile=..\src\LianDian.UI\Assets\liandian.ico
UninstallDisplayIcon={app}\LianDian.UI.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "config\*,Data\*,Logs\*,ProductPIC\*,Instruction\*,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\config\app.ini"; DestDir: "{app}\config"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\assets\ProductImages\DH280GM.png"; DestDir: "{app}\ProductPIC"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\assets\ProductImages\DH280TM.png"; DestDir: "{app}\ProductPIC"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\assets\ProductImages\ES11.png"; DestDir: "{app}\ProductPIC"; Flags: onlyifdoesntexist uninsneveruninstall

[Dirs]
Name: "{app}\Data"; Flags: uninsneveruninstall
Name: "{app}\ProductPIC"; Flags: uninsneveruninstall
Name: "{app}\Instruction"; Flags: uninsneveruninstall

[Icons]
Name: "{group}\联电数据收集"; Filename: "{app}\LianDian.UI.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\联电数据收集"; Filename: "{app}\LianDian.UI.exe"; WorkingDir: "{app}"

[Code]
function InitializeSetup(): Boolean;
var
  ReleaseValue: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', ReleaseValue);
  if Result then Result := ReleaseValue >= 461808;
  if not Result then
    MsgBox('请先安装 Microsoft .NET Framework 4.7.2 或更高版本，再运行安装程序。', mbError, MB_OK);
end;
