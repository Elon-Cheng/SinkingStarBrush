param([switch]$AllowUnconfigured)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $version = (Get-Content version.json -Raw | ConvertFrom-Json).version
    & ./sync-version.ps1
    $config = Get-Content release-config.json -Raw | ConvertFrom-Json
    if (-not $AllowUnconfigured -and $config.repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Configure repository in release-config.json before creating an official release (or use -AllowUnconfigured for local preview).' }
    if ($config.baiduUrl -and $config.baiduUrl -notmatch '^https://pan\.baidu\.com/s/[^\s]+$') { throw 'Invalid Baidu share URL.' }
    $output = Join-Path $PSScriptRoot "releases/v$version"
    if (Test-Path $output) { throw "Release already exists: $output. Increment version.json; never overwrite a published version." }
    & ./build-installer.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $test = Start-Process -FilePath (Join-Path $PSScriptRoot 'SinkingStarPainter-Setup.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw "Installer tests failed. Read $env:TEMP/SinkingStarSetup-test-result.txt" }
    $stage = Join-Path $PSScriptRoot ('releases/.staging-' + [guid]::NewGuid().ToString('N'))
    $package = Join-Path $stage 'package'
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    Copy-Item SinkingStarPainter-Setup.exe,version.json -Destination $package
    Copy-Item native/vendor/imgui-src/LICENSE.txt -Destination (Join-Path $package 'IMGUI-LICENSE.txt')
    $instructions = "Sinking Star Painter v$version`r`n`r`n1. Exit the game.`r`n2. Run SinkingStarPainter-Setup.exe.`r`n3. Install, then start the game. F8: drawing; F9: visibility.`r`nSettings: gear button in toolbar. No F10 shortcut.`r`nBoth download channels must distribute this identical ZIP.`r`n"
    [IO.File]::WriteAllText((Join-Path $package 'README.txt'), $instructions)
    $exe = Join-Path $package 'SinkingStarPainter-Setup.exe'
    $manifest = [ordered]@{version=$version;fileName='SinkingStarPainter-Setup.exe';sha256=(Get-FileHash $exe -Algorithm SHA256).Hash;size=(Get-Item $exe).Length;baiduUrl=$config.baiduUrl;baiduCode=$config.baiduCode}
    $manifest | ConvertTo-Json | Set-Content (Join-Path $stage 'release.json') -Encoding UTF8
    Copy-Item (Join-Path $stage 'release.json') -Destination $package
    $innerHashes = Get-ChildItem $package -File | Sort-Object Name | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash + '  ' + $_.Name }
    $innerHashes | Set-Content (Join-Path $package 'SHA256SUMS.txt') -Encoding ASCII
    $zip = Join-Path $stage "SinkingStarPainter-v$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -CompressionLevel Optimal
    Copy-Item $exe -Destination $stage
    @($zip,(Join-Path $stage 'SinkingStarPainter-Setup.exe'),(Join-Path $stage 'release.json')) | ForEach-Object { (Get-FileHash $_ -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($_) } | Set-Content (Join-Path $stage 'SHA256SUMS.txt') -Encoding ASCII
    # Finalize by renaming the completed staging directory; never mutate an existing release.
    Move-Item -LiteralPath $stage -Destination $output
    Write-Output "Release ready: $output"
    if ($AllowUnconfigured) { Write-Warning 'Local preview only. Configure real links and increment version before official publication.' }
} finally { Pop-Location }
