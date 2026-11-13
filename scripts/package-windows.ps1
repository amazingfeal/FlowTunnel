param(
  [Parameter(Mandatory=$true)][string]$XrayZip,
  [Parameter(Mandatory=$true)][string]$XrayZipSha256
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$payload = Join-Path $artifacts 'payload'
$engineRoot = Join-Path $artifacts 'engine'
$desktop = Join-Path $artifacts 'publish\desktop'
$service = Join-Path $artifacts 'publish\service'

$actualZipHash = (Get-FileHash -Algorithm SHA256 $XrayZip).Hash
if ($actualZipHash -ne $XrayZipSha256) { throw "Xray archive hash mismatch. Expected $XrayZipSha256, got $actualZipHash" }
New-Item -ItemType Directory -Force $payload | Out-Null
New-Item -ItemType Directory -Force $engineRoot | Out-Null
Expand-Archive -Path $XrayZip -DestinationPath $engineRoot -Force
$engine = Get-ChildItem $engineRoot -Recurse -Filter 'xray.exe' | Select-Object -First 1
if (-not $engine) { throw 'xray.exe was not found in the verified archive.' }
Copy-Item $engine.FullName (Join-Path $payload 'xray.exe') -Force
$wintun = Get-ChildItem $engineRoot -Recurse -Filter 'wintun.dll' | Select-Object -First 1
if (-not $wintun) { throw 'wintun.dll was not found in the verified archive.' }
Copy-Item $wintun.FullName (Join-Path $payload 'wintun.dll') -Force
$engineHash = (Get-FileHash -Algorithm SHA256 (Join-Path $payload 'xray.exe')).Hash
@{ version = '26.11.9'; sha256 = $engineHash } | ConvertTo-Json | Set-Content (Join-Path $payload 'engine.json') -Encoding utf8
Copy-Item (Join-Path $engine.Directory.FullName 'LICENSE') (Join-Path $payload 'xray-LICENSE.txt') -ErrorAction Stop
Copy-Item (Join-Path $engine.Directory.FullName 'LICENSE-Wintun') (Join-Path $payload 'wintun-LICENSE.txt') -ErrorAction Stop

dotnet restore (Join-Path $root 'FlowTunnel.slnx')
dotnet test (Join-Path $root 'FlowTunnel.slnx') -c Release --no-restore
dotnet publish (Join-Path $root 'src\FlowTunnel.Desktop\FlowTunnel.Desktop.csproj') -c Release -r win-x64 --self-contained true -o $desktop
dotnet publish (Join-Path $root 'src\FlowTunnel.Service\FlowTunnel.Service.csproj') -c Release -r win-x64 --self-contained true -o $service
wix build (Join-Path $root 'installer\FlowTunnel.Installer.wixproj') -d DesktopPublish=$desktop -d ServicePublish=$service -d PayloadRoot=$payload -o (Join-Path $artifacts 'FlowTunnel-x64.msi')
