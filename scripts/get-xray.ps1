param([switch]$IUnderstandThisDownloadsThirdPartySoftware)
if (-not $IUnderstandThisDownloadsThirdPartySoftware) { throw 'Re-run with -IUnderstandThisDownloadsThirdPartySoftware to explicitly consent to downloading Xray.' }
$version = '26.11.9'
$expected = '1262522266337b824f07beb8e1375786b437683b3890105469088f8e17e0a69d'
$target = Join-Path $PSScriptRoot "..\artifacts\downloads\Xray-windows-64-$version.zip"
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
Invoke-WebRequest "https://github.com/XTLS/Xray-core/releases/download/v$version/Xray-windows-64.zip" -OutFile $target
$actual = (Get-FileHash -Algorithm SHA256 $target).Hash.ToLowerInvariant()
if ($actual -ne $expected) { Remove-Item $target; throw "Downloaded archive failed integrity verification: $actual" }
Write-Output $target
