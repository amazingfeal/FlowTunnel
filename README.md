# FlowTunnel

FlowTunnel is a Windows 10/11 x64 desktop client for Xray with persistent per-application routing. It discovers foreground and background processes, groups executable instances, and produces validated Xray TUN configurations for either:

- only selected applications through the proxy, with everything else direct; or
- all applications through the proxy, with selected applications direct.

## Current implementation

The repository contains the clean-architecture solution, polished WinForms shell, subscriptions with refresh, grouped profile management, real TCP latency tests, process search and explicit VPN/Direct rules, VLESS/VMess/Trojan/Shadowsocks/SOCKS/HTTP URI import, XHTTP, VLESS + REALITY preservation, DPAPI-protected credentials, Xray integrity/config validation, and MSI packaging sources.

It does **not** claim a completed Windows acceptance test from this macOS workspace. See [docs/VERIFICATION.md](docs/VERIFICATION.md).

## One-command Windows build on macOS

Prerequisites are .NET SDK 10 and `msitools`:

```bash
brew install dotnet@10 msitools
./scripts/build-windows.sh 0.2.0
```

The script downloads and verifies the pinned Xray archive, runs the unit tests, publishes self-contained Windows x64 executables, builds the MSI, and writes both `artifacts/FlowTunnel-x64.msi` and its `.sha256` file. Update `VERSION` when the version argument is omitted.

## Build on Windows

Prerequisites:

- Windows 10 1809+ or Windows 11 x64
- Visual Studio 2026 or .NET SDK 10.0.100+
- WiX Toolset 5 (`dotnet tool install --global wix`)

```powershell
dotnet restore .\FlowTunnel.slnx
dotnet build .\FlowTunnel.slnx -c Debug -p:Platform=x64
dotnet test .\FlowTunnel.slnx -c Debug -p:Platform=x64
```

Run the unpackaged desktop app from Visual Studio with `FlowTunnel.Desktop` as startup project. TUN connections require the installed `FlowTunnel.Service`; an ordinary unelevated desktop process intentionally cannot create the tunnel itself.

## Package

Download is an explicit operation and verifies the pinned archive before use:

```powershell
$zip = .\scripts\get-xray.ps1 -IUnderstandThisDownloadsThirdPartySoftware
.\scripts\package-windows.ps1 -XrayZip $zip -XrayZipSha256 1262522266337b824f07beb8e1375786b437683b3890105469088f8e17e0a69d
```

If successful, the unsigned installer is `artifacts\FlowTunnel-x64.msi`. Windows SmartScreen may warn because this project is not code-signed.

## Discord-only routing

1. Import a working VLESS + REALITY URI on **Servers**.
2. Open **Applications**, search for Discord, and choose **VPN**.
3. Leave Chrome and other applications on **Default** while the routing mode is “Only selected applications use VPN.”
4. Connect from **Dashboard**.
5. Validate text, voice/UDP, browser direct egress, and disconnect restoration using [docs/VERIFICATION.md](docs/VERIFICATION.md).

Discord commonly has several processes. FlowTunnel groups identical executable paths so the saved path rule continues to apply after Discord restarts.

## Important limitations

- No Windows Filtering Platform kill switch is implemented. An engine crash can restore normal Windows routing; FlowTunnel does not claim OS-enforced leak prevention.
- DNS uses the configured Xray TUN DNS path. Per-application DNS isolation is not claimed.
- Connection-to-process telemetry is intentionally hidden until a reliable attribution source is implemented.
- Auto-start, live reconfiguration without a brief reconnect, icon extraction, and code signing remain unimplemented. Closing the main window hides it to the notification area; double-click the icon to restore it.
- The service currently authorizes the active console user; Remote Desktop-only sessions require a future session-aware authorization implementation.

## Third-party software

FlowTunnel supervises Xray-core 26.11.9. Packaging preserves its license alongside the binary. The official source corresponding to the pinned release is available from the [Xray-core v26.11.9 release](https://github.com/XTLS/Xray-core/releases/tag/v26.11.9).
