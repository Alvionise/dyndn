# DyndDns

[Русский](#русский) | [English](#english)

Лёгкое приложение в системном трее Windows, которое синхронизирует локальный список доменов с роутером **Keenetic** и направляет их трафик через выбранный VPN-интерфейс.

A lightweight Windows tray app that syncs a local domain list to a **Keenetic** router and routes their traffic through a selected VPN interface.

---

## Русский

### Что делает

- Живёт в системном трее и не мешает работе — отдельного окна нет.
- Хранит список доменов в файле `config/dns-list.json`.
- По горячей клавише или из меню трея добавляет/удаляет домены прямо во время работы.
- При первом запуске самостоятельно ищет роутер Keenetic в сети и предлагает ввести логин и пароль.
- При каждом изменении списка автоматически синхронизирует его с роутером Keenetic через HTTP API (RCI):
  - создаёт/обновляет группу FQDN (`object-group fqdn`);
  - создаёт маршрут DNS-прокси (`dns-proxy route`), направляя эти домены в указанный VPN-интерфейс.
- Показывает статус синхронизации иконкой в трее и всплывающим уведомлением.
- Записывает домены, которые запрашиваются на этом компьютере (DNS-мониторинг и история браузеров),
  и даёт искать их по списку, привязывать к VPN или снимать маршрут прямо из окна поиска.

### Требования

- Windows 10/11 (x64).
- [.NET 10 SDK](https://dotnet.microsoft.com/download) — для сборки из исходников.
- Роутер Keenetic с доступным HTTP-интерфейсом (RCI) и настроенным VPN-подключением (например, `OpenVPN0`).
- Запуск от имени администратора: приложение запрашивает права (UAC), так как мониторинг DNS читает
  real-time сессию ETW.

### Сборка и запуск

Из исходников:

```powershell
dotnet run --project .\src\DyndDns.TrayApp\DyndDns.TrayApp.csproj
```

Публикация одиночного self-contained `dyndns.exe` в папку `dist`:

```powershell
.\publish.ps1
```

Скрипт принимает параметры `-Configuration`, `-Runtime` и `-Output` (по умолчанию `Release`, `win-x64`, `dist`).

Debug-сборка ложится в `src\DyndDns.TrayApp\bin\Debug\net10.0-windows\`, а Release собирается под `win-x64`
(self-contained, одним файлом) в `src\DyndDns.TrayApp\bin\Release\net10.0-windows\win-x64\`.

### Конфигурация

Настройки и списки доменов лежат рядом с исполняемым файлом в папке `config`.
При первом запуске приложение создаёт `config/dyndns.json`, `config/dns-list.json` (классический список)
и `config/dns-routes.json` (привязки домен→VPN, изначально пустой); базу `config/dns.db` создаёт
DNS-мониторинг. Шаблоны-образцы — это `config/dyndns.example.json` и `config/dns-list.example.json`.
Рабочие файлы содержат локальные данные и не попадают в репозиторий.

#### `config/dyndns.json`

```json
{
  "Router": {
    "Address": "192.168.1.1",
    "Username": "admin",
    "Password": ""
  },
  "VpnInterface": "OpenVPN0",
  "Sync": {
    "AutoSync": true
  },
  "Hotkey": {
    "Enabled": true,
    "Key": "V",
    "Modifiers": "Control+Shift"
  },
  "Sqlite": {
    "DatabaseFile": "dns.db",
    "MonitorEnabled": true,
    "BrowserHistoryEnabled": true
  },
  "SetupDismissed": false
}
```

| Поле | Описание |
| --- | --- |
| `Router.Address` | Адрес роутера Keenetic. |
| `Router.Username` / `Password` | Учётные данные администратора роутера. |
| `VpnInterface` | Имя VPN-интерфейса, в который направляется трафик доменов по умолчанию. |
| `Sqlite.DatabaseFile` | Файл базы SQLite с записанными DNS-доменами (относительный путь — рядом с exe). |
| `Sqlite.MonitorEnabled` | Вести запись DNS-доменов при запуске приложения. |
| `Sqlite.BrowserHistoryEnabled` | Дополнительно импортировать посещённые домены из истории браузеров. |
| `Sync.AutoSync` | Следить за файлом списка и синхронизировать автоматически. |
| `Hotkey.Enabled` | Включить глобальную горячую клавишу. |
| `Hotkey.Key` | Клавиша (`A`–`Z`, `0`–`9`). |
| `Hotkey.Modifiers` | Модификаторы через `+`: `Control`, `Shift`, `Alt`, `Win`. |
| `SetupDismissed` | Мастер первого запуска был отменён и больше не показывается автоматически. |

> Пароль роутера шифруется при сохранении средствами Windows DPAPI (область текущего пользователя) и хранится в виде значения с префиксом `dpapi:`. Значение без этого префикса считается старым открытым паролем и принимается как есть. Копия конфига на другом компьютере или под другой учётной записью не расшифруется — пароль придётся ввести заново.

#### `config/dns-list.json`

```json
{
  "groupName": "default",
  "domains": [
    "example.com",
    "another.org"
  ]
}
```

#### `config/dns-routes.json`

Привязки доменов, добавленных из окна поиска: какой домен через какой VPN-интерфейс идёт. Классический
список из `dns-list.json` этот файл не затрагивает — у него своя группа на роутере.

```json
{
  "routes": [
    { "domain": "example.com", "interface": "SSTP0" }
  ]
}
```

Роутер считается источником истины: при чтении его состояния расхождения переносятся сюда (например,
если маршрут переключили в веб-интерфейсе Keenetic). Записи, которые ещё не успели синхронизироваться,
при этом сохраняются. Пустой список означает, что привязок нет.

### Первый запуск

Если в `config/dyndns.json` не указан пароль роутера, приложение предлагает мастер настройки:

1. Мастер сканирует сеть: сначала типовые адреса (`192.168.1.1`, `192.168.0.1`, `192.168.10.1`,
   `10.0.0.1`, `my.keenetic.net`), затем все локальные IPv4-подсети. Проверяются HTTP и HTTPS,
   а признаком Keenetic служит ответ `401` с заголовком `X-NDM-Challenge` или `X-NDM-Realm`.
2. Если найдено несколько роутеров, показывается список с именами устройств (best-effort через
   UPnP/SSDP) и возможность ввести адрес вручную.
3. Введённые логин и пароль проверяются на роутере; при ошибке окно ввода повторяется, при успехе
   данные сохраняются в `config/dyndns.json`.
4. После входа приложение читает VPN-подключения роутера и записывает активное в `VpnInterface`.
   Если подключений нет, появляется уведомление с просьбой создать VPN на роутере.

Мастер можно отменить: тогда он больше не появится сам, а конфиг можно заполнить вручную
(пункт **Настройки** в меню трея). Запустить мастер повторно можно пунктом **Настройка роутера**.

### VPN-интерфейс

Приложение не создаёт VPN-подключения: оно только читает список интерфейсов роутера (`show interface`),
выбирает подключённое (или первое найденное) и сохраняет его имя в `VpnInterface`. Так трафик доменов
уходит в актуальный туннель.

- Если ни одного VPN-подключения нет, появляется уведомление — создайте подключение на роутере
  (в веб-интерфейсе) и нажмите **Обновить VPN**.
- Пункт **Обновить VPN** в меню трея заново читает подключения и обновляет конфиг; при смене
  интерфейса запускается синхронизация.
- Если подключений несколько, появляется список с типом и состоянием каждого — можно выбрать
  конкретное. Автоматически (при старте) сохраняется уже настроенный интерфейс, пока он существует.
- Если найденное подключение не установлено, приложение предупредит об этом.

Секреты VPN (логины, пароли, ключи) приложением не читаются и в `config/dyndns.json` не сохраняются —
там остаётся только имя интерфейса.

### Мониторинг DNS и поиск доменов

Приложение записывает, какие домены запрашиваются на этом компьютере. Используется встроенный
ETW-провайдер `Microsoft-Windows-DNS-Client`, сторонние драйверы не требуются. Обращения
агрегируются по домену и складываются в локальную базу SQLite (`config/dns.db`): домен, число
обращений, время первого и последнего запроса. Процесс и точное время обращений не сохраняются.

Браузеры резолвят адреса собственным резолвером, поэтому их запросы до ETW-провайдера Windows не
доходят. Чтобы посещённые сайты тоже были в списке, приложение дополнительно читает историю
браузеров (Chrome, Edge, Yandex, Brave, Vivaldi, Opera, Firefox) и агрегирует её по домену с числом
посещений. Импорт выполняется при запуске, а дальше — только когда браузер дописал историю;
отключается флагом `Sqlite.BrowserHistoryEnabled`. Содержимое страниц не сохраняется: в базу попадают
только домен, число посещений и время последнего.

В меню трея:

- **Поиск доменов...** — окно поиска по подстроке, список VPN-подключений и кнопки
  **Добавить выбранные** / **Удалить маршруты**: домены привязываются к указанному VPN или
  освобождаются от маршрута (клавиша `Delete` делает то же для выделенных строк). Свежие обращения
  показываются первыми, а в колонке «Маршрут» указан интерфейс, через который домен идёт: зелёный —
  это выбранный VPN, жёлтый — другой. Галочка **«Только маршруты на роутере»** оставляет в списке
  лишь домены, у которых маршрут уже есть на роутере. Список и маршруты обновляются кнопками,
  клавишей F5, автоматически раз в 5 секунд и сразу после синхронизации.
- **Мониторинг DNS** — включить или выключить запись.

Маршруты группируются по VPN: на каждый интерфейс создаётся своя FQDN-группа `dyndns-<Интерфейс>` и
свой `dns-proxy route`; группы, которые больше не нужны, удаляются с роутера автоматически.

Роутер считается основным источником данных: когда приложение читает его состояние, локальные привязки
домен→VPN сверяются с ним, и расхождения (например, маршрут переключили в веб-интерфейсе) переносятся
в `dns-routes.json`. Записи, которые ещё не успели синхронизироваться, при этом не удаляются.

### Использование

1. Запустите `dyndns.exe` — иконка появится в системном трее.
2. Кликните правой кнопкой по иконке, чтобы открыть меню:
   - **Открыть список** — открыть `dns-list.json`;
   - **Добавить домен...** — добавить домен (или нажать горячую клавишу, по умолчанию `Ctrl+Shift+V`);
   - **Домены (N)** — просмотр и удаление отдельных доменов;
   - **Синхронизировать** — принудительная синхронизация;
   - **Мониторинг и маршруты** — подменю со всем, что появилось сверх классического списка:
     - **Поиск доменов...** — окно поиска записанных доменов с добавлением на роутер и выбором VPN;
     - **Мониторинг DNS** — включить или выключить запись DNS-доменов;
     - **Обновить VPN** — перечитать VPN-подключения роутера и обновить конфиг;
     - **Настройка роутера** — повторно найти роутер и ввести логин с паролем;
   - **Настройки** — открыть `dyndns.json`;
   - **Выход** — завершить приложение.
3. После изменения списка синхронизация запускается автоматически, если включён `AutoSync`.

Вводить можно не только домен: `https://www.example.com/path?query` будет очищен до `example.com`.

### Как это работает

Приложение обращается к роутеру по HTTP API (RCI) с аутентификацией Keenetic (MD5 + SHA-256 challenge-response). Состояние читается через `show sc` (группы `object-group fqdn` и маршруты `dns-proxy route`), а изменения применяются пакетом команд с последующей записью конфигурации. Синхронизация выполняется в фоновом потоке, запросы объединяются, а собственные записи файла списка игнорируются файловым наблюдателем, чтобы не запускать повторную синхронизацию. Ошибки аутентификации и синхронизации пишутся в `config/dyndns.log`.

### Структура проекта

```
dyndn.slnx
publish.ps1
src/
  DyndDns.TrayApp/
    App.xaml(.cs)            # точка входа, каталог конфигурации, логирование
    app.manifest             # запрос прав администратора (нужен real-time ETW)
    Models/                  # конфигурация, состояние роутера, маршруты, статистика доменов
    Services/
      BrowserHistoryReader.cs # импорт посещённых доменов из истории браузеров
      ConfigService.cs       # чтение/запись dyndns.json, dns-list.json и dns-routes.json
      DnsDatabase.cs         # база SQLite с записанными доменами
      DnsMonitor.cs          # real-time ETW-сессия Microsoft-Windows-DNS-Client
      DnsMonitorService.cs   # батч-запись в базу и импорт истории браузеров
      DomainNormalizer.cs    # нормализация ввода в домен
      KeeneticApiService.cs  # клиент HTTP API Keenetic (RCI): вход, проверка данных, VPN-интерфейсы
      PasswordProtector.cs   # защита пароля через DPAPI
      RouterAddress.cs       # нормализация адреса роутера (поддержка схемы)
      RouterDiscoveryService.cs # поиск Keenetic в локальной сети
      SsdpDeviceLocator.cs   # имена устройств через UPnP/SSDP
      SyncService.cs         # фоновая очередь синхронизации и наблюдение за файлом
      VpnInterfaceResolver.cs # выбор активного VPN-подключения
    Triggers/
      HotkeyManager.cs       # глобальная горячая клавиша
    ViewModels/
      MainViewModel.cs       # логика трея и меню
      SetupWizard.cs         # мастер первичной настройки
    Views/                   # диалоги мастера и окно поиска доменов (WinForms)
    config/                  # шаблоны dyndns.example.json и dns-list.example.json
tests/
  DyndDns.TrayApp.Tests/     # модульные тесты (xUnit)
```

### Разработка

```powershell
dotnet build .\dyndn.slnx -c Release
dotnet test .\tests\DyndDns.TrayApp.Tests\DyndDns.TrayApp.Tests.csproj -c Release
```

Сборка и тесты также выполняются в GitHub Actions (`.github/workflows/ci.yml`). Релиз
публикуется по тегу `v*` через `.github/workflows/release.yml`.

### Лицензия

[MIT](LICENSE)

---

## English

### What it does

- Lives in the Windows system tray with no visible window.
- Keeps the domain list in `config/dns-list.json`.
- Adds/removes domains at runtime via a hotkey or the tray menu.
- On first run it scans the network for a Keenetic router and asks for the administrator credentials.
- On every list change it automatically syncs the list to the Keenetic router over its HTTP API (RCI):
  - creates/updates an FQDN group (`object-group fqdn`);
  - creates a DNS-proxy route (`dns-proxy route`) pointing those domains at the chosen VPN interface.
- Reports sync status with the tray icon and a balloon notification.
- Records the domains queried on this machine (DNS monitor plus the browser history) and lets you
  search them, bind them to a VPN or drop their route right from the search window.

### Requirements

- Windows 10/11 (x64).
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source.
- A Keenetic router with the HTTP (RCI) interface reachable and a configured VPN connection (e.g. `OpenVPN0`).
- Run as administrator: the app requests elevation (UAC) because the DNS monitor reads a real-time
  ETW session.

### Build and run

From source:

```powershell
dotnet run --project .\src\DyndDns.TrayApp\DyndDns.TrayApp.csproj
```

Publish a single self-contained `dyndns.exe` into `dist`:

```powershell
.\publish.ps1
```

The script accepts `-Configuration`, `-Runtime`, and `-Output` (defaults: `Release`, `win-x64`, `dist`).

The Debug build lands in `src\DyndDns.TrayApp\bin\Debug\net10.0-windows\`, while Release targets `win-x64`
(self-contained, single file) and lands in `src\DyndDns.TrayApp\bin\Release\net10.0-windows\win-x64\`.

### Configuration

Settings and the domain lists live next to the executable in the `config` folder. On first run the app
creates `config/dyndns.json`, `config/dns-list.json` (the classic list) and `config/dns-routes.json`
(domain→VPN bindings, initially empty); the DNS monitor creates its database `config/dns.db`. The tracked
templates are `config/dyndns.example.json` and `config/dns-list.example.json`. Live files hold local data
and are not committed.

#### `config/dyndns.json`

```json
{
  "Router": {
    "Address": "192.168.1.1",
    "Username": "admin",
    "Password": ""
  },
  "VpnInterface": "OpenVPN0",
  "Sync": {
    "AutoSync": true
  },
  "Hotkey": {
    "Enabled": true,
    "Key": "V",
    "Modifiers": "Control+Shift"
  },
  "Sqlite": {
    "DatabaseFile": "dns.db",
    "MonitorEnabled": true,
    "BrowserHistoryEnabled": true
  },
  "SetupDismissed": false
}
```

| Field | Description |
| --- | --- |
| `Router.Address` | Keenetic router address. |
| `Router.Username` / `Password` | Router administrator credentials. |
| `VpnInterface` | Default VPN interface the domains are routed through. |
| `Sqlite.DatabaseFile` | SQLite file with the recorded DNS domains (relative paths resolve next to the exe). |
| `Sqlite.MonitorEnabled` | Record DNS domains while the app runs. |
| `Sqlite.BrowserHistoryEnabled` | Also import visited domains from the browser history. |
| `Sync.AutoSync` | Watch the list file and sync automatically. |
| `Hotkey.Enabled` | Enable the global hotkey. |
| `Hotkey.Key` | Key (`A`–`Z`, `0`–`9`). |
| `Hotkey.Modifiers` | `+`-separated modifiers: `Control`, `Shift`, `Alt`, `Win`. |
| `SetupDismissed` | The first-run wizard was declined and is no longer shown automatically. |

> The router password is encrypted at rest with Windows DPAPI (current-user scope) and stored
> with a `dpapi:` prefix. A value without that prefix is treated as legacy plaintext and accepted
> as-is. A config copied to another machine or user account cannot be decrypted — re-enter the
> password there.

#### `config/dns-list.json`

```json
{
  "groupName": "default",
  "domains": [
    "example.com",
    "another.org"
  ]
}
```

#### `config/dns-routes.json`

Bindings of the domains added from the search window: which domain goes through which VPN interface. The
classic list in `dns-list.json` is not affected by this file — it keeps its own group on the router.

```json
{
  "routes": [
    { "domain": "example.com", "interface": "SSTP0" }
  ]
}
```

The router is treated as the source of truth: when its state is read, differences are pulled in here (for
instance, a route switched in the Keenetic web UI). Entries that have not been synchronized yet are kept.
An empty list means there are no bindings.

### First run

When `config/dyndns.json` has no router password, the app offers a setup wizard:

1. The wizard scans the network: first the well-known addresses (`192.168.1.1`, `192.168.0.1`,
   `192.168.10.1`, `10.0.0.1`, `my.keenetic.net`), then every local IPv4 subnet, probing both HTTP
   and HTTPS. A Keenetic is recognized by a `401` response carrying the `X-NDM-Challenge` or
   `X-NDM-Realm` header.
2. When several routers answer, a list with device names (best-effort via UPnP/SSDP) is shown
   along with a manual address entry.
3. The entered login and password are verified against the router; on failure the prompt is shown
   again, and on success the credentials are saved to `config/dyndns.json`.
4. After signing in, the app reads the router's VPN connections and stores the active one in
   `VpnInterface`. When there are none, a notification asks you to create one.

The wizard can be cancelled: it will not appear on its own again, and the config can be filled in
by hand (the **Settings** tray item). Use the **Router setup** tray item to run it again.

### VPN interface

The app never creates VPN connections: it only reads the router's interface list (`show interface`),
picks a connected one (or the first found) and saves its name to `VpnInterface`, so domain traffic
leaves through the current tunnel.

- When no VPN connection exists, a notification asks you to create one on the router (in its web UI)
  and press **Refresh VPN**.
- The **Refresh VPN** tray item re-reads the connections and updates the config; a sync is triggered
  when the interface changed.
- When several connections exist, a list with each one's type and state is shown so a specific
  connection can be picked. On startup the configured interface is kept while it still exists.
- When the found connection is not established, the app warns about it.

VPN secrets (logins, passwords, keys) are never read or stored — `config/dyndns.json` keeps only the
interface name.

### DNS monitoring and domain search

The app records which domains are queried on this machine through the built-in
`Microsoft-Windows-DNS-Client` ETW provider; no third-party drivers are needed. Lookups are aggregated
per domain into a local SQLite database (`config/dns.db`): domain, hit count, and the first/last seen
timestamps. The process and exact lookup times are not stored.

Browsers resolve names with their own resolver, so their lookups never reach the Windows ETW provider.
To cover the sites actually visited, the app also imports the browser history (Chrome, Edge, Yandex,
Brave, Vivaldi, Opera, Firefox) and aggregates it per domain with a visit count. The import runs at
startup and afterwards only when a browser appended to its history; it can be switched off with
`Sqlite.BrowserHistoryEnabled`. Page contents are never stored — only the domain, the visit count and
the last visit time.

Tray menu:

- **Search domains...** — a window with substring search, a VPN interface drop-down and the buttons
  **Add selected** / **Remove routes**: domains are either bound to the selected VPN or released from
  their route (the `Delete` key does the same for the selected rows). The most recent lookups come
  first, and the "Маршрут" column holds the interface the domain is routed through: green for the
  selected VPN, yellow for another one. The **"Only routes on the router"** checkbox keeps just the
  domains the router already routes. The list and the routing state are refreshed by the buttons, with
  F5, every 5 seconds, and right after a synchronization.
- **DNS monitoring** — turn the recording on or off.

Routes are grouped per VPN: each interface gets its own FQDN group `dyndns-<Interface>` and its own
`dns-proxy route`; groups that are no longer needed are removed from the router automatically.

The router is treated as the source of truth: whenever its state is read, the local domain→VPN bindings
are aligned with it, so a change made in the router's web UI lands in `dns-routes.json`. Entries that
have not been synchronized yet are never dropped.

### Usage

1. Run `dyndns.exe` — the tray icon appears.
2. Right-click the icon to open the menu:
   - **Open list** — open `dns-list.json`;
   - **Add domain...** — add a domain (or press the hotkey, `Ctrl+Shift+V` by default);
   - **Domains (N)** — view and remove individual domains;
   - **Synchronize** — force a sync;
   - **Monitoring & routing** — submenu holding everything added on top of the classic list:
     - **Search domains...** — search recorded domains, add them to the router and pick a VPN;
     - **DNS monitoring** — toggle DNS recording;
     - **Refresh VPN** — re-read the router's VPN connections and update the config;
     - **Router setup** — find the router again and enter the login and password;
   - **Settings** — open `dyndns.json`;
   - **Exit** — quit the app.
3. After the list changes, a sync starts automatically when `AutoSync` is enabled.

Input does not have to be a bare domain: `https://www.example.com/path?query` is normalized to `example.com`.

### How it works

The app talks to the router over the HTTP API (RCI) using Keenetic authentication (MD5 + SHA-256 challenge-response). State is read through `show sc` (`object-group fqdn` groups and `dns-proxy route` entries), and changes are applied as a batched command followed by a configuration save. Syncing runs on a background worker, requests are coalesced, and self-writes to the list file are ignored by the file watcher to avoid redundant syncs. Authentication and sync errors are written to `config/dyndns.log`.

### Project structure

```
dyndn.slnx
publish.ps1
src/
  DyndDns.TrayApp/
    App.xaml(.cs)            # entry point, config directory, logging
    app.manifest             # requests administrator rights (needed for the real-time ETW session)
    Models/                  # config, router state, routes and domain statistics
    Services/
      BrowserHistoryReader.cs # imports visited domains from the browser history
      ConfigService.cs       # reads/writes dyndns.json, dns-list.json and dns-routes.json
      DnsDatabase.cs         # SQLite database of the recorded domains
      DnsMonitor.cs          # real-time ETW session for Microsoft-Windows-DNS-Client
      DnsMonitorService.cs   # batched writes to the database and browser history import
      DomainNormalizer.cs    # normalizes input into a domain
      KeeneticApiService.cs  # Keenetic HTTP API (RCI) client: auth, credential check, VPN interfaces
      PasswordProtector.cs   # DPAPI password protection
      RouterAddress.cs       # router address normalization (scheme support)
      RouterDiscoveryService.cs # Keenetic discovery on the local network
      SsdpDeviceLocator.cs   # device names via UPnP/SSDP
      SyncService.cs         # background sync queue and file watching
      VpnInterfaceResolver.cs # picks the active VPN connection
    Triggers/
      HotkeyManager.cs       # global hotkey
    ViewModels/
      MainViewModel.cs       # tray and menu logic
      SetupWizard.cs         # first-run setup wizard
    Views/                   # setup wizard dialogs and the domain search window (WinForms)
    config/                  # dyndns.example.json and dns-list.example.json templates
tests/
  DyndDns.TrayApp.Tests/     # unit tests (xUnit)
```

### Development

```powershell
dotnet build .\dyndn.slnx -c Release
dotnet test .\tests\DyndDns.TrayApp.Tests\DyndDns.TrayApp.Tests.csproj -c Release
```

Build and tests also run in GitHub Actions (`.github/workflows/ci.yml`). Releases are
published from `v*` tags through `.github/workflows/release.yml`.

## License

[MIT](LICENSE)
