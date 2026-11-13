# Security policy

## Design boundaries

- The desktop app runs as the signed-in user; TUN operations run in a LocalSystem Windows service.
- The named pipe allows a connection from authenticated users but the service authorizes the caller against the active console user's Windows SID before reading a command.
- IPC accepts only `start`, `stop`, and `status`; it never accepts shell commands or executable paths.
- The service writes configuration into an ACL-protected runtime directory accessible only to LocalSystem and Administrators.
- sing-box must be version 1.14.4 and match the SHA-256 stored in installer-generated `engine.json`. Every configuration is checked with `sing-box check -c` before start.
- Process arguments use `ProcessStartInfo.ArgumentList`; no command string is constructed.
- Proxy UUIDs and passwords are protected with current-user DPAPI at rest. Known secret fields are redacted from engine logs.
- TLS certificate verification remains enabled. Importers preserve REALITY keys and reject missing critical fields.

## Reporting

Do not open a public issue containing a server URI, credential, private log, or subscription token. Provide a minimal reproduction with secrets removed to the project maintainer through a private security channel once one is published.

## Known security limitations

The current release does not implement a Windows Filtering Platform kill switch and does not claim complete DNS leak prevention. Active-console authorization does not support Remote Desktop-only sessions. The installer is not code-signed.
