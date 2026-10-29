param([Parameter(Mandatory=$true)][string]$File, [Parameter(Mandatory=$true)][string]$Checksums)
$ErrorActionPreference = 'Stop'
$name = [IO.Path]::GetFileName($File)
$entries = @(Get-Content -LiteralPath $Checksums | Where-Object { $_ -match '^([A-Fa-f0-9]{64})  (.+)$' -and $Matches[2] -ceq $name })
if ($entries.Count -ne 1) { throw 'Checksum file must contain exactly one matching file name.' }
$expected = $entries[0].Substring(0,64)
$actual = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw 'SHA256 mismatch. Do not distribute or install this file.' }
Write-Output "PASS SHA256: $name"
