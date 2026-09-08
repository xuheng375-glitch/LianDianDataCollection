$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
$releaseOutput = Join-Path $releaseRoot ('artifacts\Release-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
# Fresh output only; never overwrite an active application or production Data folder.
& dotnet build (Join-Path $releaseRoot 'src\LianDian.UI\LianDian.UI.csproj') -c Release "-p:OutputPath=$releaseOutput/" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed' }
foreach ($releaseFile in @('LianDian.UI.exe','System.Data.SQLite.dll','e_sqlite3.dll','config\app.ini')) {
    if (!(Test-Path -LiteralPath (Join-Path $releaseOutput $releaseFile))) { throw "Missing dependency: $releaseFile" }
}
Copy-Item -LiteralPath (Join-Path $releaseRoot 'README.md') -Destination $releaseOutput
Write-Output "Release directory: $releaseOutput"
Write-Output 'Configure PLC and an absolute production database path before starting. No production data or images are copied.'
