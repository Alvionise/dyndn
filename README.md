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
- При каждом изменении списка автоматически синхронизирует его с роутером Keenetic через HTTP API (RCI):
  - создаёт/обновляет группу FQDN (`object-group fqdn`);
  - создаёт маршрут DNS-прокси (`dns-proxy route`), направляя эти домены в указанный VPN-интерфейс.
- Показывает статус синхронизации иконкой в трее и всплывающим уведомлением.

### Требования

- Windows 10/11 (x64).
- [.NET 10 SDK](https://dotnet.microsoft.com/download) — для сборки из исходников.
- Роутер Keenetic с доступным HTTP-интерфейсом (RCI) и настроенным VPN-подключением (например, `OpenVPN0`).

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

### Конфигурация

Настройки и список доменов лежат рядом с исполняемым файлом в папке `config`.
Приложение создаёт `config/dyndns.json` и `config/dns-list.json` при первом запуске;
шаблоны-образцы — это `config/dyndns.example.json` и `config/dns-list.example.json`.
Оба рабочих файла содержат локальные данные и не попадают в репозиторий.

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
  }
}
```

| Поле | Описание |
| --- | --- |
| `Router.Address` | Адрес роутера Keenetic. |
| `Router.Username` / `Password` | Учётные данные администратора роутера. |
| `VpnInterface` | Имя VPN-интерфейса, в который направляется трафик доменов. |
| `Sync.AutoSync` | Следить за файлом списка и синхронизировать автоматически. |
| `Hotkey.Enabled` | Включить глобальную горячую клавишу. |
| `Hotkey.Key` | Клавиша (`A`–`Z`, `0`–`9`). |
| `Hotkey.Modifiers` | Модификаторы через `+`: `Control`, `Shift`, `Alt`, `Win`. |

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

### Использование

1. Запустите `dyndns.exe` — иконка появится в системном трее.
2. Кликните правой кнопкой по иконке, чтобы открыть меню:
   - **Добавить домен...** — добавить домен (или нажать горячую клавишу, по умолчанию `Ctrl+Shift+V`);
   - **Домены (N)** — просмотр и удаление отдельных доменов;
   - **Синхронизировать** — принудительная синхронизация;
   - **Настройки** — открыть `dyndns.json`;
   - **Открыть список** — открыть `dns-list.json`;
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
    Models/                  # модели конфигурации и состояния роутера
    Services/
      ConfigService.cs       # чтение/запись dyndns.json и dns-list.json
      DomainNormalizer.cs    # нормализация ввода в домен
      KeeneticApiService.cs  # клиент HTTP API Keenetic (RCI)
      PasswordProtector.cs   # защита пароля через DPAPI
      SyncService.cs         # фоновая очередь синхронизации и наблюдение за файлом
    Triggers/
      HotkeyManager.cs       # глобальная горячая клавиша
    ViewModels/
      MainViewModel.cs       # логика трея и меню
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
- On every list change it automatically syncs the list to the Keenetic router over its HTTP API (RCI):
  - creates/updates an FQDN group (`object-group fqdn`);
  - creates a DNS-proxy route (`dns-proxy route`) pointing those domains at the chosen VPN interface.
- Reports sync status with the tray icon and a balloon notification.

### Requirements

- Windows 10/11 (x64).
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source.
- A Keenetic router with the HTTP (RCI) interface reachable and a configured VPN connection (e.g. `OpenVPN0`).

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

### Configuration

Settings and the domain list live next to the executable in the `config` folder.
The app creates `config/dyndns.json` and `config/dns-list.json` on first run; the tracked
templates are `config/dyndns.example.json` and `config/dns-list.example.json`. Both live
files hold local data and are not committed.

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
  }
}
```

| Field | Description |
| --- | --- |
| `Router.Address` | Keenetic router address. |
| `Router.Username` / `Password` | Router administrator credentials. |
| `VpnInterface` | Name of the VPN interface the domains are routed through. |
| `Sync.AutoSync` | Watch the list file and sync automatically. |
| `Hotkey.Enabled` | Enable the global hotkey. |
| `Hotkey.Key` | Key (`A`–`Z`, `0`–`9`). |
| `Hotkey.Modifiers` | `+`-separated modifiers: `Control`, `Shift`, `Alt`, `Win`. |

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

### Usage

1. Run `dyndns.exe` — the tray icon appears.
2. Right-click the icon to open the menu:
   - **Add domain...** — add a domain (or press the hotkey, `Ctrl+Shift+V` by default);
   - **Domains (N)** — view and remove individual domains;
   - **Synchronize** — force a sync;
   - **Settings** — open `dyndns.json`;
   - **Open list** — open `dns-list.json`;
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
    Models/                  # config and router state models
    Services/
      ConfigService.cs       # reads/writes dyndns.json and dns-list.json
      DomainNormalizer.cs    # normalizes input into a domain
      KeeneticApiService.cs  # Keenetic HTTP API (RCI) client
      PasswordProtector.cs   # DPAPI password protection
      SyncService.cs         # background sync queue and file watching
    Triggers/
      HotkeyManager.cs       # global hotkey
    ViewModels/
      MainViewModel.cs       # tray and menu logic
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
