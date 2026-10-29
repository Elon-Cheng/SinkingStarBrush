$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & ./sync-version.ps1
    $source = Get-Content -LiteralPath native/overlay.cpp -Raw -Encoding UTF8
    if ($source -match '"[^"\r\n]*\?\?[^"\r\n]*"') { throw 'UI text contains damaged question-mark sequences. Check UTF-8 source before publishing.' }
    $vendor = Join-Path $PSScriptRoot 'native/vendor/imgui-src'
    if (-not (Test-Path -LiteralPath (Join-Path $vendor '.git'))) {
        git clone --depth 1 --branch v1.91.9b https://github.com/ocornut/imgui.git $vendor
        if ($LASTEXITCODE -ne 0) { throw 'Cannot acquire ImGui.' }
    }
    $commit = git -C $vendor rev-parse HEAD
    if ($commit -ne 'f5befd2d29e66809cd1110a152e375a7f1981f06') { throw 'Unexpected ImGui revision.' }
    dotnet restore native/sdk.csproj
    if ($LASTEXITCODE -ne 0) { throw 'SDK restore failed.' }
    cmd /c native\build-native.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    Copy-Item -LiteralPath native/install.ps1,native/disable.ps1,native/README.md -Destination dist/SinkingStarInGame -Force
    Copy-Item -LiteralPath version.json -Destination dist/SinkingStarInGame -Force
    Copy-Item -LiteralPath (Join-Path $vendor 'LICENSE.txt') -Destination dist/SinkingStarInGame/IMGUI-LICENSE.txt -Force
    Write-Output 'Ready: dist/SinkingStarInGame'
} finally { Pop-Location }
