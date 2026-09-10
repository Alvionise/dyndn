# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- DNS monitor: queried domains are collected through the built-in `Microsoft-Windows-DNS-Client` ETW
  provider (no third-party drivers) and aggregated per domain into a local SQLite database
  (`Sqlite.DatabaseFile`, default `config/dns.db`).
- Search window (`Поиск доменов...` / `Search domains...` tray item) that searches the recorded
  domains, allows multi-select and adds the chosen ones to the router with a selected VPN interface.
- Domains are now tracked together with their VPN interface; the router receives one FQDN group
  `dyndns-<Interface>` and one dns-proxy route per interface, and stale groups are removed.
- Tray submenu `Мониторинг и маршруты` / `Monitoring & routing` gathering everything added on top of
  the classic domain list: the domain search, the DNS monitor toggle, the VPN refresh and the router
  setup wizard.
- `Sqlite` configuration section in `dyndns.json`.
- The application manifest now requests administrator rights, which the real-time ETW session needs.
- Unit tests for route grouping, the route list format (including the legacy layout), and the SQLite
  aggregation/search.

- The app now reads the router's VPN interfaces (`show interface`), picks the connected one (or the
  first found) and stores its name in `VpnInterface`. When no VPN connection exists, a tray
  notification asks the user to create one.
- `Обновить VPN` / `Refresh VPN` tray item to re-read the router's VPN connections and update the
  config; routing is re-synced when the interface changes.
- When several VPN connections exist, the refresh action shows a pick list with each connection's
  type and state, while startup keeps the configured interface as long as it still exists.
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

- The search window gained an "only routes on the router" checkbox (`Только маршруты на роутере`) that
  narrows the list down to the domains already routed by the router.
- Route removal in the search window (`Удалить маршруты` / the `Delete` key): the selected domains
  leave `dns-routes.json` and the next sync rewrites the router groups without them. Reconciling with
  the router is suspended while a local change is still on its way, so removed routes stay removed.
- Visited domains are also imported from the browser history (Chrome, Edge, Yandex, Brave, Vivaldi,
  Opera, Firefox): browsers resolve names with their own resolver, so their lookups never reach the
  `Microsoft-Windows-DNS-Client` provider. Switchable with `Sqlite.BrowserHistoryEnabled`.
- The router is the source of truth for routing: every read of its state aligns the local domain→VPN
  bindings with it (domains routed through a managed group are added or updated locally, while
  not-yet-synced local entries are kept).
- The search window lists the most recently resolved domains first, refreshes the list and the routing
  state right after a synchronization, and shows only the routed interface in the "Маршрут" column.

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
