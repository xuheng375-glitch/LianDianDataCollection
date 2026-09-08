param([string]$Compiler = "$PSScriptRoot\..\.tools\InnoSetup\ISCC.exe")
$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $PSScriptRoot
$packageOutput = Join-Path $packageRoot ('artifacts\Installer-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$packagePayload = Join-Path $packageOutput 'payload'
& dotnet build (Join-Path $packageRoot 'src\LianDian.UI\LianDian.UI.csproj') -c Release "-p:OutputPath=$packagePayload/" '-p:Version=1.1.0' --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
foreach ($packageDependency in @('LianDian.UI.exe','System.Data.SQLite.dll','e_sqlite3.dll','config\app.ini')) {
    if (!(Test-Path (Join-Path $packagePayload $packageDependency))) { throw "Missing: $packageDependency" }
}
& $Compiler "/DPayloadDir=$packagePayload" "/DPackageDir=$packageOutput" (Join-Path $PSScriptRoot 'Installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
Write-Output "Installer directory: $packageOutput"
