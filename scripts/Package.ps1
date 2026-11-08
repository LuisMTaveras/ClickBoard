param([ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$distributionRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist'))
$stagePath = [IO.Path]::GetFullPath((Join-Path $distributionRoot ('.stage-' + [guid]::NewGuid().ToString('N'))))
$releasePath = [IO.Path]::GetFullPath((Join-Path $distributionRoot ('ClipForge-' + $Version)))
$archivePath = Join-Path $distributionRoot ('ClipForge-' + $Version + '.zip')
$prefix = $distributionRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$stagePath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or !$releasePath.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta de distribución inválida.' }
if ((Test-Path -LiteralPath $releasePath) -or (Test-Path -LiteralPath $archivePath)) { throw 'Ya existe esa versión; usa un número nuevo.' }
New-Item -ItemType Directory -Force -Path $distributionRoot | Out-Null
& dotnet publish (Join-Path $projectRoot 'src\ClickBoard.App\ClickBoard.App.csproj') --no-restore -c Release -o $stagePath "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar. Se conserva la carpeta temporal para inspección.' }
Move-Item -LiteralPath $stagePath -Destination $releasePath
Compress-Archive -LiteralPath $releasePath -DestinationPath $archivePath
Write-Output $archivePath
