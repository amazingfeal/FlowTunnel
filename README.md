# FlowTunnel

FlowTunnel is a Windows 10/11 x64 desktop client for sing-box with persistent per-application routing. It discovers foreground and background processes, groups executable instances, and produces validated sing-box TUN configurations for either:

- only selected applications through the proxy, with everything else direct; or
- all applications through the proxy, with selected applications direct.

## Current implementation

The repository contains the clean-architecture solution, WinForms shell, VLESS/VMess/Trojan/Shadowsocks/SOCKS/HTTP URI import, VLESS + REALITY preservation, DPAPI-protected credentials, atomic JSON persistence, Windows process discovery, a restricted elevated Windows service, sing-box SHA-256/version/config validation, supervised start/stop, route generation, unit tests, and MSI packaging sources.

It does **not** claim a completed Windows acceptance test from this macOS workspace. See [docs/VERIFICATION.md](docs/VERIFICATION.md).

## One-command Windows build on macOS

Prerequisites are .NET SDK 10 and `msitools`:

```bash
brew install dotnet@10 msitools
./scripts/build-windows.sh 0.2.0
```

The script downloads and verifies the pinned sing-box archive, runs the unit tests, publishes self-contained Windows x64 executables, builds the MSI, and writes both `artifacts/FlowTunnel-x64.msi` and its `.sha256` file. Update `VERSION` when the version argument is omitted.

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
$zip = .\scripts\get-sing-box.ps1 -IUnderstandThisDownloadsThirdPartySoftware
.\scripts\package-windows.ps1 -SingBoxZip $zip -SingBoxZipSha256 99185d75d314a07dbba4a130c8ad986cbeb629a07927975636b341ca61f991df
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

- No Windows Filtering Platform kill switch is implemented. A failed proxy connection does not select the Direct outbound inside sing-box, but an engine crash can restore normal Windows routing; FlowTunnel does not claim OS-enforced leak prevention.
- DNS currently uses the Windows local resolver through sing-box DNS hijacking. Per-application DNS isolation is not claimed.
- Connection-to-process telemetry is intentionally hidden until a reliable attribution source is implemented.
- Subscription URLs, auto-start, live reconfiguration, icon extraction, and code signing remain unimplemented. Closing the main window hides it to the notification area; double-click the icon to restore it.
- Xray-only extensions outside sing-box 1.14.4 are rejected or unsupported; arbitrary Xray compatibility is not claimed.
- The service currently authorizes the active console user; Remote Desktop-only sessions require a future session-aware authorization implementation.

## Third-party software

FlowTunnel supervises sing-box 1.14.4, licensed GPL-3.0-or-later. Packaging preserves its license alongside the binary. The official source corresponding to the pinned release is available from the [sing-box v1.14.4 release](https://github.com/SagerNet/sing-box/releases/tag/v1.14.4).
