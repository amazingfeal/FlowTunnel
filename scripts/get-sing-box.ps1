param([switch]$IUnderstandThisDownloadsThirdPartySoftware)
if (-not $IUnderstandThisDownloadsThirdPartySoftware) { throw 'Re-run with -IUnderstandThisDownloadsThirdPartySoftware to explicitly consent to downloading sing-box.' }
$version = '1.14.4'
$expected = '99185d75d314a07dbba4a130c8ad986cbeb629a07927975636b341ca61f991df'
$target = Join-Path $PSScriptRoot "..\artifacts\downloads\sing-box-$version-windows-amd64.zip"
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
Invoke-WebRequest "https://github.com/SagerNet/sing-box/releases/download/v$version/sing-box-$version-windows-amd64.zip" -OutFile $target
$actual = (Get-FileHash -Algorithm SHA256 $target).Hash.ToLowerInvariant()
if ($actual -ne $expected) { Remove-Item $target; throw "Downloaded archive failed integrity verification: $actual" }
Write-Output $target
