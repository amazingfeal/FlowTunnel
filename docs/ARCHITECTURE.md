# Architecture

Dependency direction is `Desktop/Service/Windows/Infrastructure/Networking -> Application -> Core`.

- **Core** owns immutable domain models and interfaces.
- **Application** owns connection state transitions and routing use cases.
- **Infrastructure** owns atomic persistence, DPAPI protection, and untrusted URI parsing.
- **Networking** owns sing-box 1.14.4 JSON generation, validation, integrity checks, and process supervision.
- **Windows** owns process enumeration and restricted service IPC.
- **Service** owns the elevated TUN engine lifecycle and crash cleanup.
- **Desktop** is an MVVM WinUI 3 shell and does not generate networking JSON.

The desktop generates a configuration in its private LocalAppData directory, validates it locally with the verified engine, and sends the JSON over an authenticated named pipe. The service writes it to an administrator-only runtime directory, validates it again, and starts the verified engine. On service stop, sing-box is stopped before the service exits.

Routing order is deterministic: engine-loop prevention, DNS hijack, enabled application rules by ascending priority, then the selected mode's final outbound. Path rules are preferred; name rules are a documented fallback.
