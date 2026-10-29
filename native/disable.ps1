param([string]$GameDirectory = 'D:\Program Files (x86)\Steam1\steamapps\common\Order of the Sinking Star')
$ErrorActionPreference = 'Stop'
if (Get-Process sinking_star -ErrorAction SilentlyContinue) { throw 'Close the game before disabling.' }
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'sinking_star.exe'))) { throw 'This is not the game directory.' }
$target = Join-Path $gameRoot 'dxgi.dll'
$receiptPath = Join-Path $gameRoot 'sinking-star-painter-install.json'
if (-not (Test-Path -LiteralPath $target)) { Write-Output 'Already disabled.'; exit }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $target).Hash -ne $receipt.Sha256) { throw 'The DLL does not match this installation. No files were changed.' }
# Rename only this verified file within the explicitly resolved game directory.
Rename-Item -LiteralPath $target -NewName ('sinking-star-painter-disabled-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.dll')
Write-Output 'Painter disabled. The game will load the system DXGI library on next launch.'
