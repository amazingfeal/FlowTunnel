# Verification matrix

## Automated

Run on Windows x64:

```powershell
dotnet test .\FlowTunnel.slnx -c Release -p:Platform=x64
```

Tests cover route modes, path/name matching, engine loop prevention, REALITY import, unsupported protocols, and domain validation. Engine crash and cancellation integration tests require the packaged binary and service and are not marked passed until run on Windows.

Authoring-environment result (2026-11-13): 14 portable unit tests passed (Core 2, Application 6, Networking 6). The Windows service and Windows process-discovery assemblies compiled with zero warnings and errors. WinUI XAML compilation, WiX packaging, actual `sing-box check`, TUN behavior, and the manual matrix below remain pending on Windows.

## Manual Windows acceptance tests

Record date, Windows build, sing-box version/hash, server protocol, and pass/fail evidence for each test.

1. Import a VLESS + REALITY profile and connect; confirm `sing-box check` succeeds and the dashboard reaches Connected only while the service reports the engine alive.
2. Set Discord to VPN and keep Chrome Default in “Only selected” mode. Compare public egress IPs.
3. Send Discord text messages.
4. Join a Discord voice channel and verify bidirectional audio and UDP behavior. If it fails, capture redacted sing-box diagnostics and server UDP capability.
5. Restart Discord and verify the path rule still matches all Discord processes.
6. Restart FlowTunnel and verify profiles/settings/rules persist.
7. Disconnect and verify the TUN interface/default routes/DNS return to normal.
8. Terminate sing-box unexpectedly and verify the dashboard reports `FT-ENGINE-EXIT` and does not claim Connected.
9. Switch routing mode and verify real egress reverses after the disclosed tunnel restart.
10. Exercise IPv4, IPv6, sleep/resume, Wi-Fi disconnect/reconnect, and DNS leak tools; document results without claiming untested protection.

None of these manual tests were executed in the macOS authoring environment.
