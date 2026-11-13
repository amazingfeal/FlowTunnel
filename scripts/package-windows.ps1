param(
  [Parameter(Mandatory=$true)][string]$SingBoxZip,
  [Parameter(Mandatory=$true)][string]$SingBoxZipSha256
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$payload = Join-Path $artifacts 'payload'
$desktop = Join-Path $artifacts 'publish\desktop'
$service = Join-Path $artifacts 'publish\service'

$actualZipHash = (Get-FileHash -Algorithm SHA256 $SingBoxZip).Hash
if ($actualZipHash -ne $SingBoxZipSha256) { throw "sing-box archive hash mismatch. Expected $SingBoxZipSha256, got $actualZipHash" }
New-Item -ItemType Directory -Force $payload | Out-Null
Expand-Archive -Path $SingBoxZip -DestinationPath $payload -Force
$engine = Get-ChildItem $payload -Recurse -Filter 'sing-box.exe' | Select-Object -First 1
if (-not $engine) { throw 'sing-box.exe was not found in the verified archive.' }
Copy-Item $engine.FullName (Join-Path $payload 'sing-box.exe') -Force
$engineHash = (Get-FileHash -Algorithm SHA256 (Join-Path $payload 'sing-box.exe')).Hash
@{ version = '1.14.4'; sha256 = $engineHash } | ConvertTo-Json | Set-Content (Join-Path $payload 'engine.json') -Encoding utf8
Copy-Item (Join-Path $engine.Directory.FullName 'LICENSE') (Join-Path $payload 'sing-box-LICENSE.txt') -ErrorAction Stop

dotnet restore (Join-Path $root 'FlowTunnel.slnx')
dotnet test (Join-Path $root 'FlowTunnel.slnx') -c Release --no-restore
dotnet publish (Join-Path $root 'src\FlowTunnel.Desktop\FlowTunnel.Desktop.csproj') -c Release -r win-x64 --self-contained true -o $desktop
dotnet publish (Join-Path $root 'src\FlowTunnel.Service\FlowTunnel.Service.csproj') -c Release -r win-x64 --self-contained true -o $service
wix build (Join-Path $root 'installer\FlowTunnel.Installer.wixproj') -d DesktopPublish=$desktop -d ServicePublish=$service -d PayloadRoot=$payload -o (Join-Path $artifacts 'FlowTunnel-x64.msi')
