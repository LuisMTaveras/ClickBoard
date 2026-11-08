param(
    [bool]$CreateDesktopShortcut = $true,
    [bool]$StartAfterInstall = $false
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\ClipForge'

Get-Process ClipForge -ErrorAction SilentlyContinue | Stop-Process -Force

$sourceDir = Join-Path $projectRoot 'dist\Standalone'
if (!(Test-Path (Join-Path $sourceDir 'ClipForge.exe'))) {
    & dotnet publish (Join-Path $projectRoot 'src\ClickBoard.App\ClickBoard.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $sourceDir --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Error al compilar ClipForge en modo Release.' }
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $installDir -Recurse -Force

$exePath = Join-Path $installDir 'ClipForge.exe'
$shell = New-Object -ComObject WScript.Shell

$startMenuPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'ClipForge.lnk'
$shortcut = $shell.CreateShortcut($startMenuPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = 'ClipForge · Gestor de portapapeles'
$shortcut.IconLocation = "$exePath,0"
$shortcut.Save()

if ($CreateDesktopShortcut) {
    $desktopPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ClipForge.lnk'
    $desktopShortcut = $shell.CreateShortcut($desktopPath)
    $desktopShortcut.TargetPath = $exePath
    $desktopShortcut.WorkingDirectory = $installDir
    $desktopShortcut.Description = 'ClipForge · Gestor de portapapeles'
    $desktopShortcut.IconLocation = "$exePath,0"
    $desktopShortcut.Save()
}

Write-Output "ClipForge instalado correctamente en: $exePath"
if ($StartAfterInstall) {
    Start-Process -FilePath $exePath -WorkingDirectory $installDir
}
