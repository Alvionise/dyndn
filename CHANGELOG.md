# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
