$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & ./build-native.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    dotnet publish Installer/Installer.csproj -c Release -r win-x64 --self-contained true -o dist/Installer
    if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }
    Copy-Item -LiteralPath dist/Installer/SinkingStarPainter-Setup.exe -Destination (Join-Path $PSScriptRoot 'SinkingStarPainter-Setup.exe') -Force
    Write-Output 'Ready: SinkingStarPainter-Setup.exe (standalone offline installer)'
} finally { Pop-Location }
