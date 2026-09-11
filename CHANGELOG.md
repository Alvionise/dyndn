# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- A VPN connection is written as «имя (интерфейс)» wherever one is listed or picked — the drop-downs of the
  window, the menu of the table and its "VPN" column — because the name the connection carries on the router is
  what the user recognises, while the interface is what a binding refers to. A connection the router does not
  name stays its interface alone.
- Storage: everything the app keeps — the settings, the router profiles, the domains bound to each of them and
  the DNS journal — lives in the single SQLite database `config/dyndns.db`. The JSON files and the separate
  journal database of 0.1.0 are not read any more.
- Several routers can be stored, each with its own address, credentials and default VPN connection. A domain can
  be bound to several routers at once, each through its own connection, and every profile is pushed.
- Routers are the source of truth: reading their state aligns the stored bindings with it, so a change made in a
  router's web UI lands in the database, while bindings that have not been synchronized yet are never dropped.
- The domain list became the **`Поиск и мониторинг` / `Search and monitoring`** window: one table of bindings
  with filters by router and by VPN, search as you type, ordering by a click on a column header, and a panel
  that binds the typed or the selected domains to a router and its connection — the same actions on a right
  click, next to the selection they work on.
- The tray menu holds one switch for DNS recording that spells its state out, the **Роутеры** submenu
  (configure, delete, add), the settings dialog and the exit. Synchronizing has no item of its own: a bind, an
  unbind and every saved profile push the routes themselves.
- Every window is laid out by docking instead of coordinates and cannot be resized below what its content needs,
  on a scaled display as well, because the app asks for a DPI-aware mode.
- Code style is pinned in `.editorconfig` and checked by the CI job (`dotnet format --verify-no-changes`),
  including build warnings for code nobody reads. Every type is `internal`, and the pieces several places need
  live in one home: `AppInfo` (the name and the window titles), `RouterLabel` and `VpnLabel` (how a router and a
  connection are written), `DialogLayout` (the shapes the windows are built from) and `Dialogs` (the questions and
  reports), `ViewDispatch` (work for the thread of the windows) and `LocalNetwork` (the adapters a router can be
  reached through, shared by the scan and the UPnP lookup).

### Added

- DNS monitoring: the lookups of this machine are collected through the built-in
  `Microsoft-Windows-DNS-Client` ETW provider and aggregated per domain into `dns_queries`, and the browser
  history (Chrome, Edge, Yandex, Brave, Vivaldi, Opera, Firefox) is imported as well, because browsers resolve
  names with their own resolver. Both sources are switched in the settings dialog, and the tray switch stops
  both: while the recording is off, nothing is written to the journal at all.
- The journal is kept within a limit: the settings hold a switch for the automatic cleanup of the unused names
  and the number of entries to keep (10 000 by default), and once more domains that no binding mentions are
  recorded, the oldest of them are dropped. Bound domains are never removed and do not count towards the limit.
- A router the scan finds without a name is named after its own answer: the realm of its authentication
  challenge (`Keenetic Giga SE`) gives the device name away before anyone signs in, and `show system` refines it
  once the credentials are accepted. The UPnP lookup is only a fallback, because on a network whose gateway is a
  provider's ONT it is the ONT that answers it.
- The first-run wizard: the network scan (a Keenetic is recognized by its authentication challenge), the list of
  discovered devices with a manual address entry, the credential check and the automatic choice of the active
  VPN connection of the router.
- The settings dialog: the database path with a button that opens the data folder, the monitor and
  browser-history switches, the journal cleanup with its limit, and **"Start with Windows"** (the `Run`
  registry key of the current user).
- Only one copy of the app runs at a time (`SingleInstanceGuard`): a second launch — the autostart entry
  overlapping a manual start, for example — writes a line to `config/dyndns.log` and exits.
- The app manifest requests administrator rights, which the real-time ETW session needs, and the router password
  is protected at rest with Windows DPAPI (current-user scope).
- Unit tests for the RCI protocol, the bindings (one domain on two routers), the settings store, the journal and
  its cleanup, the router state reader, the browser history import, the discovery candidates, the dialog layout,
  the autostart registration, and the filters, labels and ordering of the window. A test that needs a database
  takes a throwaway one from one helper (`TempDatabase`), which also owns the temp folder and the
  pooled-connection cleanup.

### Removed

- Windows host routes for the routed domains (`LocalRouteService`, `LocalRoutePlanner`, `WindowsRouteTable`,
  the `applied_routes` table and the tray item): the VPN router is expected to be the gateway, and a route for
  one address cannot serve two routers at once.
- The file-based configuration (`ConfigService`), the manual domain list in the tray and the global hotkey:
  domains are managed in the window, where the journal is.
- The comparison of casings and vendors: a domain is one binding whatever its casing, and a Keenetic is the only
  device the app manages (no Huawei probing, no `vendor` column).
- The per-profile synchronization switch and the actions of the window that repeated what happens on its own
  (refresh from the routers, synchronize everything, add a router there): the routers are read when the window
  opens and after every change of a profile, and the wizard lives in the tray submenu.

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
