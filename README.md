# DyndDns

[Русский](#русский) | [English](#english)

Лёгкое приложение в системном трее Windows, которое синхронизирует управляемые домены с роутерами **Keenetic** и направляет их трафик через выбранные VPN-интерфейсы.

A lightweight Windows tray app that syncs managed domains to **Keenetic** routers and routes their traffic through selected VPN interfaces.

---

## Русский

### Что делает

- Живёт в системном трее и не мешает работе — постоянного окна нет.
- Хранит все настройки, профили роутеров, привязки доменов и журнал DNS в одной базе `config/dyndns.db` (SQLite).
- Поддерживает **несколько профилей роутеров**: у каждого свой адрес, логин, пароль и VPN-интерфейс по умолчанию.
- **Один домен можно привязать к нескольким роутерам** — у каждого со своим VPN.
- Синхронизирует **все включённые профили**; недоступный роутер не мешает остальным — ошибка попадает в уведомление с именем профиля.
- В окне **Поиск и мониторинг** одна таблица: домен, роутер, VPN, число обращений, последний запрос и состояние на роутере, с фильтрами по роутеру и по VPN.
- Записывает домены, которые запрашиваются на этом компьютере (DNS-мониторинг и история браузеров).

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

Скрипт принимает параметры `-Configuration`, `-Runtime`, `-Version` и `-Output` (по умолчанию `Release`, `win-x64`,
версия из проекта, `dist`). При выпуске релиза версию подставляет workflow из тега, поэтому собранный
`dyndns.exe` сообщает ту версию, под которой он выпущен.

Debug-сборка ложится в `src\DyndDns.TrayApp\bin\Debug\net10.0-windows\`, а Release собирается под `win-x64`
(self-contained, одним файлом) в `src\DyndDns.TrayApp\bin\Release\net10.0-windows\win-x64\`.

### Хранение данных

Все данные лежат в папке `config` рядом с исполняемым файлом:

| Файл | Назначение |
| --- | --- |
| `dyndns.db` | Единственная база SQLite: настройки, профили роутеров, привязки доменов и журнал DNS. |
| `dyndns.log` | Лог приложения (ошибки синхронизации, аутентификации и чтения состояния). |

Файлов конфигурации в JSON нет: настройки меняются в диалоге **Настройки**, роутеры и домены — в окне
**Поиск и мониторинг** и в подменю **Роутеры**. Таблицы базы:

| Таблица | Содержимое |
| --- | --- |
| `settings` | Глобальные настройки: мониторинг, история браузеров, автоочистка журнала. |
| `routers` | Профили роутеров: имя, адрес, логин, пароль, VPN-интерфейс. |
| `routes` | Привязки: домен, профиль, VPN-интерфейс. Ключ — пара `(router_id, domain)`, поэтому один домен живёт на нескольких роутерах. |
| `dns_queries` | Журнал DNS: домен, число обращений, время последнего запроса. |

Пароль роутера шифруется средствами Windows DPAPI (область текущего пользователя) и хранится в базе
в виде значения с префиксом `dpapi:`. Копия базы на другом компьютере или под другой учётной записью
не расшифруется — пароль придётся ввести заново.

#### Обновление с прежней версии

Данные прежних версий — файлы `dyndns.json`, `dns-list.json`, `dns-routes.json`, `local-routes.json` и
отдельная база журнала `dns.db` — **не переносятся**. Приложение создаёт `config\dyndns.db` заново, а
прежние файлы остаются в папке нетронутыми и больше не читаются. После обновления роутер нужно добавить
заново: адрес, логин и пароль спросит мастер первого запуска, а домены и привязки набираются в окне
**Поиск и мониторинг**.

### Несколько роутеров и привязки

- Профиль роутера — это адрес, логин, пароль и VPN-интерфейс по умолчанию; в синхронизации участвуют все
  профили. Домен привязывается к паре «роутер + VPN», поэтому один и тот же адрес может идти в туннель
  на двух роутерах сразу.
- Один роутер — один профиль: мастер откажется сохранить адрес, на котором уже отвечает другой профиль
  (схема, порт, путь и регистр при этом не важны). Два профиля для одного устройства писали бы на нём
  одни и те же группы `dyndns-<Интерфейс>` и стирали домены друг друга.
- Подменю **Роутеры** в трее: для каждого роутера — **Настроить (адрес, логин, пароль)...** и
  **Удалить роутер**; ниже — **Добавить роутер...**.
- Роутер везде называется одинаково — **имя, а в скобках адрес** (`Home (192.168.1.1)`): в подменю трея,
  в фильтрах окна, в списке привязки, в меню таблицы и в столбце «Роутер». Если у устройства нет своего
  имени или оно совпадает с адресом, остаётся только адрес — без повтора вида `192.168.1.1 (192.168.1.1)`.
- Синхронизация выполняется для всех профилей: каждый роутер получает свои группы
  `dyndns-<Интерфейс>` и свои маршруты DNS-прокси, лишние группы удаляются. У каждого профиля своя
  сессия, поэтому разные роутеры не мешают друг другу, а недоступный попадает в уведомление, не срывая
  прогон для остальных.
- Удаление роутера спрашивает, снять ли его группы `dyndns-*` с этого роутера.

### Первый запуск

Если профилей ещё нет, приложение предлагает мастер настройки:

1. Мастер сканирует сеть: сначала типовые адреса (`192.168.1.1`, `192.168.0.1`, `192.168.10.1`,
   `10.0.0.1`, `my.keenetic.net`), затем все локальные IPv4-подсети. Проверяются HTTP и HTTPS,
   а признаком Keenetic служит ответ `401` с заголовком `X-NDM-Challenge` или `X-NDM-Realm`.
2. Показывается список найденных устройств с именами, а также поле для ручного ввода адреса. Список
   показывается всегда, даже если найден один роутер или не найдено ничего. Имя роутер сообщает сам ещё до
   входа: в ответе на `/auth` приходят realm (`Keenetic Giga SE`) и модель. Поиск по UPnP/SSDP работает как
   дополнение — в сети, где шлюзом стоит ONT провайдера, на него отвечает ONT, а не сам роутер. После
   успешного входа имя уточняется у устройства (`show system`).
3. Введённые логин и пароль проверяются на роутере; при ошибке окно ввода повторяется, при успехе
   создаётся профиль роутера.
4. После входа приложение читает VPN-подключения роутера и записывает активное в профиль.
   Если подключений нет, появляется уведомление с просьбой создать VPN на роутере.

Мастер можно отменить: тогда он не появится сам, а профиль можно добавить позже пунктом
**Роутеры → Добавить роутер...**.

Приложение работает только с роутерами Keenetic: другие устройства в скане не появляются, а введённый
вручную адрес не пройдёт проверку логина. Терминал провайдера (ONT) остаётся шлюзом, а правила пишутся
на Keenetic.

### VPN-интерфейс

Приложение не создаёт VPN-подключения: оно только читает список интерфейсов роутера (`show interface`),
выбирает подключённое (или первое найденное) и сохраняет его имя в профиле. Так трафик доменов этого
профиля уходит в актуальный туннель.

В списках и в столбце «VPN» подключение подписано так, как оно названо на роутере, а в скобках стоит сам
интерфейс — `Латвия (SSTP0)`; если описания нет, остаётся только имя интерфейса.

- Подключения читаются при запуске приложения и при открытии окна «Поиск и мониторинг»: профили хранят
  то, что есть на роутерах, и отдельного действия «обновить» не требуется.
- Настроенный интерфейс сохраняется, пока он есть на роутере; если подключение пересоздали или
  переименовали, вместо него подставляется подключённое, профиль сохраняется, и синхронизация этого
  профиля запускается заново.
- Если подключений несколько, берётся настроенное, иначе подключённое; конкретное подключение для домена
  выбирается при привязке в окне «Поиск и мониторинг», а не в трее.
- Если подключение удалили на роутере, привязки, которые шли через него, снимаются (в уведомлении указано,
  сколько), а его группа `dyndns-<Интерфейс>` удаляется с роутера следующей синхронизацией.
- Обратное тоже работает: новое подключение на роутере появляется в списках VPN окна «Поиск и мониторинг»
  и подхватывается профилем, у которого интерфейс ещё не выбран, а домен, добавленный в группу `dyndns-*`
  в веб-интерфейсе роутера, становится привязкой в таблице.
- Если туннель не поднят, приходит уведомление «VPN <имя> не подключён. Проверьте настройки.».
- Если у профиля есть домены, но не выбран VPN-интерфейс, синхронизация этого профиля пропускается с
  предупреждением: иначе группы на роутере были бы удалены.

Секреты VPN (логины, пароли, ключи) приложением не читаются и не сохраняются — в профиле остаётся
только имя интерфейса.

### Окно «Поиск и мониторинг»

Открывается одноимённым пунктом трея. При открытии приложение заново читает профили и их VPN-подключения
с роутеров и отправляет им сохранённые маршруты, поэтому окно показывает то состояние, которое есть на
роутерах. После каждой привязки и отвязки маршруты уходят на роутер сами, поэтому отдельной кнопки
синхронизации у окна нет. Одна таблица, где **каждая строка — это привязка**:

| Домен | Роутер | VPN | Обращений | Последний раз | На роутере |
| --- | --- | --- | --- | --- | --- |

**Верхняя строка** — поиск по подстроке: таблица перестраивается прямо при вводе, а `Enter` и `F5`
перечитывают журнал и привязки.

**Фильтры** — роутер (**«Все роутеры»**, **«Не привязанные ни к одному роутеру»** или конкретный профиль) и
VPN (конкретный интерфейс выбранного роутера), галочка **Только привязанные**, автообновление раз в 5 секунд
и **корзина**: ею удаляются из журнала домены, не привязанные ни к одному роутеру (подсказка при наведении
напоминает, к чему она относится). Выбор конкретного роутера в фильтре переставляет на него и панель привязки:
список VPN и поле «к роутеру» следуют за фильтром. Фильтр **VPN** недоступен, пока роутер не выбран: у «всех
роутеров» нет общего списка подключений. Список меняет выбор только когда выбираете вы: прокрутка
колесом над закрытым списком его не переключает. Окно открывается на **«Все роутеры»**: видно и привязанное, и
то, что ещё без привязки, — то есть
маршруты роутеров не скрываются фильтром. Вариант **«Не привязанные ни к одному роутеру»** остаётся выбором:
им удобно найти домены, которым привязка ещё не задана. Фильтр, выбранный вручную, сохраняется при
перезагрузке списка (например, после добавления роутера). Пока выбран фильтр «Не привязанные ни к одному
роутеру», галочка **Только привязанные** недоступна — она описывает привязанные домены и противоречила бы
фильтру.

**Таблица.** Домен, привязанный к двум роутерам, занимает две строки; домен из журнала, который пока
никуда не привязан, показан строкой с пустым роутером. Домен, добавленный вручную (или подхваченный из
группы на роутере) и отсутствующий в журнале, тоже виден: он показывается без обращений и с прочерком
времени последнего запроса, чтобы его можно было найти фильтрами и увидеть его состояние на роутере.
Строка подсвечивается зелёным, когда домен на роутере идёт через VPN из привязки, и жёлтым, когда через
другой интерфейс. Щелчок по заголовку столбца упорядочивает таблицу по нему, повторный щелчок меняет
порядок, а сам заголовок показывает, по какому столбцу она отсортирована; выбранный порядок сохраняется при
автообновлении. Пока столбец не выбран, первыми идут недавние запросы. Если строк нет, внизу написано,
сколько записей в журнале, и что домен можно добавить вручную.

**Правая кнопка на таблице** повторяет действия панели привязки для выбранных строк: **Привязать выбранные**
раскрывает плоский список пар «роутер · VPN», каждая выбирается одним щелчком, а **Отвязать выбранные** снимает
привязки отмеченных строк. Меню открывает само окно, поэтому оно появляется с первого щелчка (даже когда окно
только открылось), и то же делает `Shift+F10` с клавиатуры. Строка под курсором выделяется перед показом меню,
даже если до щелчка не было выделено ничего; уже выделенные строки остаются выделенными — так один роутер можно
привязать сразу к нескольким доменам. Пока меню открыто, автообновление приостановлено: таблица не
перестраивается под курсором.

На одном роутере у домена **один** интерфейс: повторная привязка на другой VPN переносит домен (в строке
состояния появляется «Доменов переведено на другой VPN»), а не создаёт второй маршрут для того же имени —
Keenetic держит один маршрут на имя. Разные роутеры независимы: один и тот же домен можно привязать к
нескольким роутерам, и на каждом он пойдёт через свой туннель.

**Панель «Привязка доменов»:**

- поле **Домен** — можно ввести домен вручную (например, чтобы добавить то, что не попало в журнал),
  выбрать **роутер** и **VPN** из списка (в нём подключения роутера, интерфейс профиля — среди них, каждое
  подписано «имя (интерфейс)») и нажать **Привязать**;
- если поле пустое, **Привязать** привязывает выбранные в таблице строки;
- клавиша `Delete` на выбранных строках снимает привязки (то же есть в меню по правой кнопке);
- двойной щелчок по строке подставляет её домен в поле;
- клавиша `F5` перечитывает профили, привязки и журнал;
- **корзина** в строке фильтров удаляет из журнала домены, которых нет ни на одном роутере: список
  непривязанных растёт с каждым обращением, а нужны обычно те, что уже разведены по туннелям. Действие
  сначала спрашивает подтверждение и привязанные домены не трогает. То же самое автоочистка делает сама,
  когда непривязанных записей становится больше лимита из настроек.

Роутеры считаются источником данных: когда приложение читает их состояние, привязки сверяются с ним, и
расхождения (например, маршрут переключили в веб-интерфейсе Keenetic) переносятся в базу. Привязки,
которые ещё не успели синхронизироваться, при этом не удаляются.

### Мониторинг DNS

Приложение записывает, какие домены запрашиваются на этом компьютере, через встроенный ETW-провайдер
`Microsoft-Windows-DNS-Client`; сторонние драйверы не требуются. Обращения агрегируются по домену и
складываются в таблицу `dns_queries`: домен, число обращений, время последнего запроса.
Процесс и точное время обращений не сохраняются.

Браузеры резолвят адреса собственным резолвером, поэтому их запросы до ETW-провайдера Windows не
доходят. Чтобы посещённые сайты тоже были в списке, приложение дополнительно читает историю браузеров
(Chrome, Edge, Yandex, Brave, Vivaldi, Opera, Firefox) и агрегирует её по домену с числом посещений.
Импорт начинается вместе с записью, а дальше выполняется только когда браузер дописал историю.
Содержимое страниц не сохраняется: в базу попадают только домен, число посещений и время последнего.

Оба источника переключаются в диалоге **Настройки**. Пока запись выключена — пунктом меню
**Мониторинг DNS: выключен** или галочкой в настройках, — в журнал не попадает ничего: импорт истории
тоже останавливается.

Журнал не растёт бесконечно: в настройках есть автоочистка неиспользуемых имён и число записей, которое
нужно хранить (по умолчанию 10 000). Как только непривязанных доменов становится больше лимита, самые
старые записи удаляются автоматически; привязанные к роутерам домены не удаляются и в лимит не считаются.
Очистка выполняется во время записи журнала, поэтому смена лимита не требует перезапуска приложения.

### Настройки и автозапуск

Диалог **Настройки...** показывает путь к базе и открывает папку с данными, включает или выключает
мониторинг DNS и импорт истории браузеров, задаёт автоочистку журнала и её лимит, а также содержит галочку
**«Запускать при входе в систему»**:
приложение прописывает себя в автозапуск текущего пользователя (раздел `Run` в реестре) и снимает
запись при выключении. Так как приложению нужны права администратора, Windows попросит подтверждение
при входе в систему.

### Где должны работать правила

Приложение пишет правила на роутеры и **не меняет сетевые настройки Windows**. Правило роутера
действует только на тот трафик, который через него проходит, поэтому компьютер (или другое устройство),
чьи домены нужно заворачивать в туннель, должен выходить в интернет через этот роутер — то есть
VPN-роутер должен быть шлюзом. Если компьютер подключён к другому шлюзу (например, к ONT провайдера с
Keenetic рядом), домены этого компьютера пойдут мимо туннеля, а правила продолжат работать для
устройств, которые ходят через Keenetic.

### Использование

1. Запустите `dyndns.exe` — иконка появится в системном трее. Одновременно работает только один
   экземпляр: повторный запуск (например, автозапуск и ручной старт наложились) сразу завершается.
2. Кликните правой кнопкой по иконке:
   - **Поиск и мониторинг...** — окно с таблицей привязок и фильтрами;
   - **Мониторинг DNS: включён / выключен** — включить или выключить запись DNS-доменов (текст пункта
     показывает текущее состояние, а о переключении сообщает уведомление);
   - **Роутеры** — профили: настройка и удаление; там же **Добавить роутер...**. Отдельного пункта
     синхронизации нет: привязка и отвязка отправляют маршруты сами;
   - **Настройки...** — база данных, мониторинг DNS, импорт истории браузеров и автоочистка журнала;
   - **Выход** — завершить приложение.
3. После привязки или отвязки домена синхронизация запускается автоматически.

Вводить можно не только домен: `https://www.example.com/path?query` будет очищен до `example.com`.

### Как это работает

Приложение обращается к каждому роутеру по HTTP API (RCI) с аутентификацией Keenetic (MD5 + SHA-256
challenge-response), для каждого профиля — со своей сессией. Состояние читается через `show sc` (группы
`object-group fqdn` и маршруты `dns-proxy route`), а изменения применяются пакетом команд с последующей
записью конфигурации. Синхронизация выполняется в фоновом потоке, запросы объединяются и раскладываются
по профилям. Ошибки аутентификации и синхронизации пишутся в `config/dyndns.log`.

### Структура проекта

```
dyndn.slnx
publish.ps1
src/
  DyndDns.TrayApp/
    App.xaml(.cs)            # точка входа, каталог данных, логирование
    AppInfo.cs               # имя приложения и шаблон заголовка окна
    app.manifest             # запрос прав администратора (нужен real-time ETW)
    Models/                  # настройки, профиль роутера, привязки, состояние роутера, статистика доменов
      RouterLabel.cs         # «имя (адрес)» — как подписывается роутер в меню, списках и таблице
      VpnLabel.cs            # «имя (интерфейс)» — как подписывается VPN-подключение
    Services/
      AppDatabase.cs         # база SQLite: подключение и схема
      SettingsStore.cs       # настройки, профили, привязки доменов
      BrowserHistoryReader.cs # импорт посещённых доменов из истории браузеров
      DnsDatabase.cs         # журнал DNS-доменов
      DnsMonitor.cs          # real-time ETW-сессия Microsoft-Windows-DNS-Client
      DnsMonitorService.cs   # батч-запись в базу и импорт истории браузеров
      DomainNormalizer.cs    # нормализация ввода в домен
      KeeneticApiService.cs  # клиент HTTP API Keenetic (RCI) для одного роутера
      KeeneticAuth.cs        # вход в Keenetic: realm, challenge, хеш пароля
      KeeneticRci.cs         # сборка и разбор команд RCI (без сети)
      DnsRouting.cs          # группы доменов по VPN-интерфейсам
      RouterApiPool.cs       # сессия на каждый профиль
      PasswordProtector.cs   # защита пароля через DPAPI
      RouterAddress.cs       # нормализация адреса роутера (поддержка схемы)
      LocalNetwork.cs        # адаптеры, адреса и шлюзы, которыми пользуются скан и UPnP-поиск
      RouterDiscoveryService.cs # поиск роутеров Keenetic в локальной сети
      SingleInstanceGuard.cs # один экземпляр приложения (именованный мьютекс)
      SsdpDeviceLocator.cs   # имена устройств через UPnP/SSDP
      StartupRegistration.cs # автозапуск текущего пользователя в реестре
      SyncService.cs         # фоновая очередь синхронизации по профилям
      VpnInterfaceResolver.cs # выбор активного VPN-подключения
    ViewModels/
      MainViewModel.cs       # логика трея и меню
      RouterStateReader.cs   # состояние маршрутов на роутерах и сверка привязок
      SetupWizard.cs         # мастер настройки роутера
    Views/                   # диалоги мастера, окно поиска и мониторинга, диалог настроек (WinForms)
      DialogLayout.cs        # из чего собираются окна: таблица, группа, колонка
      Dialogs.cs             # вопросы и сообщения (заголовок, кнопки, значок — в одном месте)
      ViewDispatch.cs        # работа в потоке окон (и защита от закрытого окна)
      IMonitoringWindowHost.cs # то, что окну нужно от приложения
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

- Lives in the Windows system tray with no permanent window.
- Keeps every setting, router profile, domain binding and the DNS journal in one SQLite database,
  `config/dyndns.db`.
- Supports **several router profiles**: each has its own address, credentials and default VPN interface.
- **A domain can be bound to several routers**, each through its own VPN.
- Synchronizes **every enabled profile**; an unreachable router does not stop the others — the failure is
  reported with the profile name.
- The **Search and monitoring** window shows one table: domain, router, VPN, hit count, last lookup and
  the state on the router, filterable by router and by VPN.
- Records the domains queried on this machine (DNS monitor plus the browser history).

### Requirements

- Windows 10/11 (x64).
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build from source.
- A Keenetic router with the HTTP (RCI) interface reachable and a configured VPN connection (e.g. `OpenVPN0`).
- Run as administrator: the app requests elevation (UAC) because the DNS monitor reads a real-time ETW
  session.

### Build and run

From source:

```powershell
dotnet run --project .\src\DyndDns.TrayApp\DyndDns.TrayApp.csproj
```

Publish a single self-contained `dyndns.exe` into `dist`:

```powershell
.\publish.ps1
```

The script accepts `-Configuration`, `-Runtime`, `-Version` and `-Output` (defaults: `Release`, `win-x64`, the
version of the project, `dist`). The release workflow takes the version from the tag, so the published
`dyndns.exe` reports the version it was released under.

The Debug build lands in `src\DyndDns.TrayApp\bin\Debug\net10.0-windows\`, while Release targets `win-x64`
(self-contained, single file) and lands in `src\DyndDns.TrayApp\bin\Release\net10.0-windows\win-x64\`.

### Data storage

Everything lives in the `config` folder next to the executable:

| File | Purpose |
| --- | --- |
| `dyndns.db` | The only SQLite database: settings, router profiles, domain bindings and the DNS journal. |
| `dyndns.log` | Application log (sync, authentication and state-read errors). |

There are no JSON configuration files: settings are edited in the **Settings** dialog, routers and domains
in the **Search and monitoring** window and in the **Routers** submenu. The database tables:

| Table | Contents |
| --- | --- |
| `settings` | Global settings: the monitor, the browser history and the journal cleanup. |
| `routers` | Router profiles: name, address, username, password, VPN interface. |
| `routes` | Bindings: domain, profile, VPN interface. Keyed by `(router_id, domain)`, so one domain can live on several routers. |
| `dns_queries` | DNS journal: domain, hit count, the time of the last look-up. |

The router password is encrypted at rest with Windows DPAPI (current-user scope) and stored in the
database with a `dpapi:` prefix. A database copied to another machine or user account cannot be
decrypted — re-enter the password there.

#### Upgrading from an older version

The data of the earlier versions — the files `dyndns.json`, `dns-list.json`, `dns-routes.json`,
`local-routes.json` and the separate journal database `dns.db` — is **not carried over**: the app creates
`config\dyndns.db` from scratch, while the old files stay in the folder untouched and are never read
again. After an upgrade the router has to be added again (the first-run wizard asks for its address,
username and password), and the domains and bindings are collected in the **Search and monitoring**
window.

### Several routers and bindings

- A profile is an address, credentials and a default VPN interface. A domain is bound to a router plus a
  VPN interface, so the same address can go through the tunnels of two routers at once.
- One router is one profile: the wizard refuses an address that another profile already answers on (neither
  the scheme, nor the port, nor the path, nor the casing matters). Two profiles for one device would write
  the same `dyndns-<Interface>` groups on it and wipe the domains of each other.
- The **Routers** submenu holds, per router: **Configure (address, username, password)...** and **Delete
  router**; below them, **Add router...**.
- A router is named the same way everywhere — **its name with the address in parentheses**
  (`Home (192.168.1.1)`): in the tray submenu, in the filters of the window, in the binding list, in the menu
  of the table and in the "Router" column. A device without a name of its own, or whose name is its address, is
  shown by the address alone instead of `192.168.1.1 (192.168.1.1)`.
- Synchronization covers every profile: each router receives its own `dyndns-<Interface>` groups
  and dns-proxy routes, and stale groups are removed. Every profile has its own session, so routers do
  not interfere, and an unreachable one is reported without spoiling the run for the others.
- Deleting a router asks whether its `dyndns-*` groups should be removed from that router.

### First run

When no profile exists yet, the app offers the setup wizard:

1. The wizard scans the network: first the well-known addresses (`192.168.1.1`, `192.168.0.1`,
   `192.168.10.1`, `10.0.0.1`, `my.keenetic.net`), then every local IPv4 subnet, probing both HTTP
   and HTTPS. A Keenetic is recognized by a `401` response carrying the `X-NDM-Challenge` or
   `X-NDM-Realm` header.
2. A list of the discovered devices with their names is shown, along with a manual address entry. The list is
   always shown, even when one router (or none) was found. A router gives its name away before anyone signs
   in: the answer to `/auth` carries its realm (`Keenetic Giga SE`) and its model. The UPnP/SSDP lookup is an
   extra source — on a network whose gateway is a provider's ONT it is the ONT that answers it, not the
   router. The name is refined at the device (`show system`) once the credentials are accepted.
3. The entered login and password are verified against the router; on failure the prompt is shown
   again, and on success a profile is created.
4. After signing in, the app reads the router's VPN connections and stores the active one in the
   profile. When there are none, a notification asks you to create one.

The wizard can be cancelled: it will not appear on its own again, and a profile can be added later with
**Routers → Add router...**.

The app works with Keenetic routers only: no other device shows up in the scan, and an address typed by
hand has to pass the same credential check. A terminal installed by the provider (an ONT) stays the
gateway, while the rules are written to the Keenetic.

### VPN interface

The app never creates VPN connections: it only reads the router's interface list (`show interface`),
picks a connected one (or the first found) and saves its name in the profile, so that profile's domain
traffic leaves through the current tunnel.

- The connections are read at startup and when the "Search and monitoring" window opens: the profiles hold
  what the routers have, so there is no separate "refresh" action.
- A configured interface is kept while the router still has it; when a connection was recreated or renamed,
  the connected one takes its place, the profile is saved and that profile is synchronized again.
- When several connections exist, the configured one is kept, otherwise the connected one; the connection a
  domain uses is chosen when it is bound in the "Search and monitoring" window, not in the tray.
- When a connection is deleted on the router, the bindings that went through it are dropped (the notification
  says how many) and its `dyndns-<Interface>` group is removed from the router by the next synchronization.
- The other way round works as well: a new connection on the router shows up in the window's VPN lists and is
  adopted by a profile that has no interface yet, while a domain added to a `dyndns-*` group in the router's
  web UI becomes a binding in the table.
- When a tunnel is down, a notification says "VPN <name> is not connected. Check the settings."
- When a profile has domains but no VPN interface, its synchronization is skipped with a warning:
  otherwise the groups on that router would be wiped.

VPN secrets (logins, passwords, keys) are never read or stored — the profile keeps only the interface name.

A connection is written the same way wherever one is picked or listed: the name the router gives it, with its
interface in parentheses — `Латвия (SSTP0)`. A connection the router does not name stays its interface alone.

### The "Search and monitoring" window

Opened with the tray item of the same name. Opening it reads the profiles and their VPN connections from the
routers again and pushes the stored routes to them, so the window shows the state the routers actually have.
Every bind and unbind sends the routes on its own, which is why the window has no button of its own for that.
One table where **every row is a binding**:

| Domain | Router | VPN | Hits | Last lookup | On router |
| --- | --- | --- | --- | --- | --- |

**The top row** holds the substring search: the table follows the typing, while `Enter` and `F5` re-read the
journal and the bindings.

**The filters** are the router (**"all routers"**, **"not bound to any router"** or a specific profile) and the
VPN (a specific interface of the chosen router), the **bound only** checkbox, a 5-second auto-refresh and the
**bin** that drops the journal entries bound to no router (its tooltip says what it clears). Choosing a router
also points the binding panel at it: the connections it offers and the «к роутеру» drop-down follow the filter.
The **VPN** filter is disabled until a router is chosen, because "all routers" have no shared list of connections.
A list changes its choice only when you pick one: a wheel over a closed drop-down does not walk through it.
The window opens on **"all routers"**: bound domains and the ones without a binding are shown alike, so the
rules on the routers are not hidden by the filter. **"Not bound to any router"** stays a choice for finding the
domains that still need a binding. A filter chosen by hand survives a reload of the list. While "not bound to
any router" is selected, the **bound only** checkbox is disabled: it describes bound domains and would
contradict the filter.

**The table.** A domain bound to two routers takes two rows; a journal domain that is not bound anywhere
yet is shown with an empty router. A domain added by hand (or adopted from a group on the router) that the
journal does not know is shown as well: without hits and with a dash instead of a look-up time, so it can be
found by the filters and its state on the router is visible. A row is highlighted green when the domain on the
router goes through the bound VPN and yellow when it uses another interface. A click on a column header orders
the table by it, a second click turns the order around and the header shows which column it is; the chosen order
survives the auto-refresh, and until a column is chosen the newest lookups come first. When there are no rows,
the status line tells how many journal entries exist and that a domain can be added by hand.

**A right click on the table** repeats the actions of the binding panel for the selected rows: **Привязать
выбранные** opens a flat list of «router · VPN» entries, each picked with a single click, and **Отвязать
выбранные** removes the bindings of the marked rows. The window opens the menu itself, so it appears on the first
click (even right after the window opened), and `Shift+F10` does the same from the keyboard; the row under the
pointer is selected before the menu opens, even when nothing was selected before it. A selection that covers that
row is kept, so one router can be bound to several domains in a single go. The auto-refresh is held while the menu
is open, so the table does not
change under the pointer.

A domain has **one** interface per router: binding it again to another VPN moves it (the status line says
"moved to another VPN") instead of creating a second route for the same name — a Keenetic keeps one route per
name. Different routers are independent: the same domain can be bound to several routers, each through its own
tunnel.

**The "Domain bindings" panel:**

- the **Domain** field accepts a domain typed by hand (for a name that never reached the journal); pick
  the **router** and the **VPN** from the list (the connections of the router, its profile's interface among
  them, each written «имя (интерфейс)») and press **Bind**;
- with an empty field, **Bind** binds the rows selected in the table;
- the `Delete` key removes the bindings of the selected rows, and the menu of the table offers the same;
- a double click on a row puts its domain into the field;
- the `F5` key re-reads the profiles, the bindings and the journal;
- the **bin** in the row of the filters removes the journal entries that are bound to no router: the list of
  unbound domains grows with every lookup, while the ones already routed through a tunnel are the ones to keep.
  It asks for confirmation first and leaves the bound domains alone. The automatic cleanup does the same on
  its own once the unbound entries pass the limit set in the settings.

Routers are treated as the source of truth: whenever their state is read, the bindings are aligned with
it, so a change made in a router's web UI lands in the database. Bindings that have not been
synchronized yet are never dropped.

### DNS monitoring

The app records which domains are queried on this machine through the built-in
`Microsoft-Windows-DNS-Client` ETW provider; no third-party drivers are needed. Lookups are aggregated
per domain into the `dns_queries` table: domain, hit count and the time of the last look-up. The
process and exact lookup times are not stored.

Browsers resolve names with their own resolver, so their lookups never reach the Windows ETW provider.
To cover the sites actually visited, the app also imports the browser history (Chrome, Edge, Yandex,
Brave, Vivaldi, Opera, Firefox) and aggregates it per domain with a visit count. The import starts with
the recording and afterwards runs only when a browser appended to its history. Page contents are never
stored — only the domain, the visit count and the last visit time.

Both sources are switched in the **Settings** dialog. While the recording is off — through the
**DNS monitoring: off** menu item or the switch in the settings — nothing reaches the journal: the
browser history import stops as well.

The journal does not grow forever: the settings hold an automatic cleanup of the unused names and the number
of entries to keep (10 000 by default). Once more unbound domains are recorded than the limit allows, the
oldest of them are dropped; domains bound to a router are never removed and do not count towards the limit.
The cleanup runs while the journal is written, so a changed limit applies without restarting the app.

### Settings and autostart

The **Settings...** dialog shows the database path and opens the data folder, toggles the DNS monitor and
the browser history import, sets the automatic journal cleanup and its limit, and holds the **"Start with
Windows"** checkbox: the app registers itself in the autostart of the current user (the `Run` registry key)
and removes the entry when the checkbox is cleared. Because the app needs administrator rights, Windows asks
for confirmation when signing in.

### Where the rules have to apply

The app only writes rules to the routers and **does not touch the network settings of Windows**. A router
rule applies to the traffic that passes through that router, so the machine whose domains must go
through the tunnel has to reach the internet through that router — the VPN router has to be the gateway.
When a machine is connected to another gateway (an ISP ONT with the Keenetic next to it, for example),
that machine's domains bypass the tunnel, while the rules keep working for the devices that go through
the Keenetic.

### Usage

1. Run `dyndns.exe` — the tray icon appears. Only one copy runs at a time: a second launch (the autostart
   entry overlapping a manual start, for example) exits straight away.
2. Right-click the icon:
   - **Search and monitoring...** — the binding table with its filters;
   - **DNS monitoring: on / off** — toggle DNS recording (the item text shows the current state and a balloon
     reports the switch);
   - **Routers** — the profiles: configuration and deletion; **Add router...** lives there too. A bind or an
     unbind pushes the routes on its own, so synchronization needs no item of its own;
   - **Settings...** — the database, the DNS monitor, the browser history import and the journal cleanup;
   - **Exit** — quit the app.
3. After a domain is bound or unbound, a sync starts automatically.

Input does not have to be a bare domain: `https://www.example.com/path?query` is normalized to `example.com`.

### How it works

The app talks to each router over the HTTP API (RCI) using Keenetic authentication (MD5 + SHA-256
challenge-response), one session per profile. State is read through `show sc` (`object-group fqdn` groups
and `dns-proxy route` entries), and changes are applied as a batched command followed by a configuration
save. Syncing runs on a background worker; requests are coalesced and split per profile. Authentication
and sync errors are written to `config/dyndns.log`.

### Project structure

```
dyndn.slnx
publish.ps1
src/
  DyndDns.TrayApp/
    App.xaml(.cs)            # entry point, data directory, logging
    AppInfo.cs               # the app name and the window title pattern
    app.manifest             # requests administrator rights (needed for the real-time ETW session)
    Models/                  # settings, router profile, bindings, router state, domain statistics
      RouterLabel.cs         # «имя (адрес)» — how a router is written in menus, lists and the table
      VpnLabel.cs            # «имя (интерфейс)» — how a VPN connection is written
    Services/
      AppDatabase.cs         # SQLite database: connection and schema
      SettingsStore.cs       # settings, profiles, domain bindings
      BrowserHistoryReader.cs # imports visited domains from the browser history
      DnsDatabase.cs         # DNS journal
      DnsMonitor.cs          # real-time ETW session for Microsoft-Windows-DNS-Client
      DnsMonitorService.cs   # batched writes to the database and browser history import
      DomainNormalizer.cs    # normalizes input into a domain
      KeeneticApiService.cs  # Keenetic HTTP API (RCI) client for one router
      KeeneticAuth.cs        # Keenetic sign-in: realm, challenge, password hash
      KeeneticRci.cs         # RCI command building and parsing (no network)
      DnsRouting.cs          # domain groups per VPN interface
      RouterApiPool.cs       # one session per profile
      PasswordProtector.cs   # DPAPI password protection
      RouterAddress.cs       # router address normalization (scheme support)
      LocalNetwork.cs        # the adapters, addresses and gateways the scan and the UPnP lookup share
      RouterDiscoveryService.cs # Keenetic discovery on the local network
      SingleInstanceGuard.cs # a single running copy (named mutex)
      SsdpDeviceLocator.cs   # device names via UPnP/SSDP
      StartupRegistration.cs # autostart entry of the current user in the registry
      SyncService.cs         # background sync queue, per profile
      VpnInterfaceResolver.cs # picks the active VPN connection
    ViewModels/
      MainViewModel.cs       # tray and menu logic
      RouterStateReader.cs   # routing state of the routers and binding reconciliation
      SetupWizard.cs         # router setup wizard
    Views/                   # wizard dialogs, search and monitoring window, settings dialog (WinForms)
      DialogLayout.cs        # the shapes the windows are built from: table, group, column
      Dialogs.cs             # the questions and reports (title, buttons and icon in one place)
      ViewDispatch.cs        # work for the thread of the windows (and the guard of a closed one)
      IMonitoringWindowHost.cs # what the window asks the application for
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
