$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'SinkingStarAssistant/SinkingStarAssistant.csproj'
$output = Join-Path $PSScriptRoot 'dist/SinkingStarAssistant'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $output 'README.md') -Force
Write-Output "Ready: $output"
