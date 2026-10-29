param([string]$GameDirectory = 'D:\Program Files (x86)\Steam1\steamapps\common\Order of the Sinking Star', [switch]$StageForRestart)
$ErrorActionPreference = 'Stop'
if ((Get-Process sinking_star -ErrorAction SilentlyContinue) -and -not $StageForRestart) { throw 'Close the game before installing, or use -StageForRestart.' }
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'sinking_star.exe'))) { throw 'This is not the game directory.' }
$payload = Join-Path $PSScriptRoot '..\dist\SinkingStarInGame\dxgi.dll'
if (-not (Test-Path -LiteralPath $payload)) { $payload = Join-Path $PSScriptRoot 'dxgi.dll' }
$target = Join-Path $gameRoot 'dxgi.dll'
$receiptPath = Join-Path $gameRoot 'sinking-star-painter-install.json'
$backup = $null
$staged = Join-Path $gameRoot 'sinking-star-painter-new.dll'
Copy-Item -LiteralPath $payload -Destination $staged -Force
if (Test-Path -LiteralPath $target) {
    if (-not (Test-Path -LiteralPath $receiptPath)) { throw 'An existing dxgi.dll belongs to another mod. No files were changed.' }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $target).Hash -ne $receipt.Sha256) { throw 'Existing dxgi.dll has changed. No files were changed.' }
    $backup = Join-Path $gameRoot ('sinking-star-painter-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.dll')
    # Both resolved paths stay in the verified game directory. Loaded image remains valid after rename.
    Move-Item -LiteralPath $target -Destination $backup
}
try { Move-Item -LiteralPath $staged -Destination $target }
catch { if ($backup -and -not (Test-Path -LiteralPath $target)) { Move-Item -LiteralPath $backup -Destination $target }; throw }
$versionFile = Join-Path $PSScriptRoot 'version.json'
if (-not (Test-Path $versionFile)) { $versionFile = Join-Path $PSScriptRoot '../dist/SinkingStarInGame/version.json' }
@{ Product = 'SinkingStarInGame'; Version = (Get-Content $versionFile -Raw | ConvertFrom-Json).version; Sha256 = (Get-FileHash -LiteralPath $target).Hash } | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8
Write-Output "Installed native painter: $target"
Write-Output 'Start the game normally. F8 = drawing; F9 = visibility; Esc = exit drawing.'
