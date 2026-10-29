$ErrorActionPreference = 'Stop'
$version = (Get-Content (Join-Path $PSScriptRoot 'version.json') -Raw | ConvertFrom-Json).version
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'version.json must contain a stable major.minor.patch version.' }
$header = '#pragma once' + "`n" + '#define PAINTER_VERSION "' + $version + '"' + "`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'native/version.g.h'), $header, [Text.Encoding]::ASCII)
