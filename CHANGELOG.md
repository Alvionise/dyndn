# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The app now reads the router's VPN interfaces (`show interface`), picks the connected one (or the
  first found) and stores its name in `VpnInterface`. When no VPN connection exists, a tray
  notification asks the user to create one.
- `Обновить VPN` / `Refresh VPN` tray item to re-read the router's VPN connections and update the
  config; routing is re-synced when the interface changes.
- Unit tests for interface parsing, VPN interface detection, and picking the active connection.

- First-run setup wizard: scans the local network for Keenetic routers, lets the user pick a
  device when several answer, verifies the entered credentials against the router, and stores
  them in `config/dyndns.json`.
- Network discovery over HTTP and HTTPS using the Keenetic `X-NDM-Challenge` / `X-NDM-Realm`
  signature, with device names resolved best-effort through UPnP/SSDP.
- `Настройка роутера` tray menu item to re-run the setup wizard at any time.
- `SetupDismissed` config flag so a declined wizard does not reappear on every launch.
- Router address may now carry an explicit `http://` or `https://` scheme.
- Unit tests for router address normalization, discovery candidate generation, and the
  Keenetic challenge signature.

## [0.1.0] - 2026-09-10

### Added

- Windows tray app that syncs a local domain list to a Keenetic router over the RCI HTTP API.
- Automatic (file watcher) and manual synchronization of FQDN object-groups and DNS-proxy
  routes to a chosen VPN interface.
- Tray context menu and a global hotkey for managing the domain list.
- Router password protected at rest with Windows DPAPI (current-user scope).
- File-based error logging to `config/dyndns.log`.
- Unit tests covering domain normalization, RCI payload parsing/building, and password protection.

[Unreleased]: https://github.com/Alvionise/dyndn/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Alvionise/dyndn/releases/tag/v0.1.0
