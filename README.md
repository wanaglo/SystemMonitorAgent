# SystemMonitorAgent

`SystemMonitorAgent` — Windows-ориентированный агент мониторинга на .NET 8 Worker Service. Он собирает системные метрики по фиксированному расписанию, отправляет снимки состояния на HTTP API и остаётся работоспособным при временных сбоях сети или недоступности внешнего сервиса.

## Кратко о проекте

- **Основной режим работы** — Windows Service
- **Дополнительный режим** — запуск в консоли для локальной отладки и диагностики
- **Планирование цикла** — `PeriodicTimer` с фиксированным интервалом
- **Конфигурация** — `appsettings.json` + `IOptions` + fail-fast validation
- **Логирование** — Console, файл, Windows Event Log
- **Отказоустойчивость** — bounded in-memory retry queue, классификация ошибок, exponential backoff
- **Остановка** — graceful shutdown с полным `CancellationToken` flow

## Архитектура

Проект использует **pragmatic clean architecture**: поведение и orchestration отделены от инфраструктуры и host-слоя, но без лишних слоёв и framework-heavy абстракций.

| Проект | Ответственность |
| --- | --- |
| `SystemMonitorAgent.Core` | Модели данных и контракты конфигурации |
| `SystemMonitorAgent.Application` | Бизнес-flow одного цикла мониторинга, retry policy, validation rules, application abstractions |
| `SystemMonitorAgent.Infrastructure` | Сбор системных метрик Windows, HTTP-доставка, реализация retry queue |
| `SystemMonitorAgent.Worker` | Composition root, host lifecycle, Windows Service integration, Serilog |
| `SystemMonitorAgent.UnitTests` | Unit tests на validation и resilience behavior |

### Поток зависимостей

```text
Worker -> Application -> Core
Worker -> Infrastructure -> Application -> Core
UnitTests -> проекты под тестом
```

### Почему архитектура именно такая

- `Core` остаётся стабильным и не зависит от деталей окружения.
- `Application` управляет поведением, но не знает про `HttpClient`, WMI и Windows Service specifics.
- `Infrastructure` изолирует внешние интеграции и потенциально нестабильные точки.
- `Worker` остаётся тонким runtime-слоем и не превращается в место для бизнес-логики.
- Для тестового задания это даёт хорошее соотношение между чистотой архитектуры и практичностью.

## Структура репозитория

```text
.github\
  workflows\
    ci.yml
src\
  SystemMonitorAgent.Core\
  SystemMonitorAgent.Application\
  SystemMonitorAgent.DemoApi\
  SystemMonitorAgent.Infrastructure\
  SystemMonitorAgent.Worker\
tests\
  SystemMonitorAgent.UnitTests\
scripts\
  install-service.ps1
  uninstall-service.ps1
samples\
  system-snapshot.example.json
```

## Технологии

- .NET 8
- Worker Service / Generic Host
- Windows Service support
- Dependency Injection
- Options pattern
- `IHttpClientFactory`
- Serilog
- xUnit + Moq

## Требования к окружению

- Windows 10 / Windows Server
- .NET 8 SDK для сборки, тестов и publish
- Права локального администратора для установки и удаления службы
- Доступный HTTP API для приёма снимков

## Сборка и тесты

```powershell
dotnet restore .\SystemMonitorAgent.sln
dotnet build .\SystemMonitorAgent.sln -c Release
dotnet test .\SystemMonitorAgent.sln -c Release
```

## CI

В репозитории добавлен workflow `.\.github\workflows\ci.yml`.

Он запускается на:

- `push`
- `pull_request`

Pipeline выполняет:

1. `restore`
2. `build` в `Release`
3. `test`
4. `publish` Worker-проекта как `win-x64` (`self-contained = false`)
5. upload test results и publish artifact в GitHub Actions artifacts

### Почему pipeline именно такой

- Используется **один Windows job**, потому что решение содержит `net8.0-windows` проекты и не требует matrix build.
- Publish stage добавлен осознанно: для Windows Service полезно иметь готовый артефакт, который совпадает с documented deployment path.
- Включён `concurrency`, чтобы новые push/updates в той же ветке отменяли устаревшие runs.
- Workflow intentionally compact: он проверяет engineering discipline, но не разрастается в enterprise-монстра.

## Запуск в консольном режиме

Консольный режим нужен для локальной разработки, проверки конфигурации и troubleshooting.

```powershell
dotnet run --project .\src\SystemMonitorAgent.Worker\SystemMonitorAgent.Worker.csproj
```

После publish можно запускать и готовый `.exe`:

```powershell
.\artifacts\publish\SystemMonitorAgent\SystemMonitorAgent.exe
```

## Локальная проверка без внешнего API

Чтобы не поднимать отдельный HTTP-приёмник вручную, в solution добавлен проект `SystemMonitorAgent.DemoApi`.

Он:

- слушает `http://localhost:5000`
- принимает `POST /api/metrics`
- хранит последние полученные снимки в памяти
- позволяет быстро посмотреть результат через `GET /api/metrics/latest` и `GET /api/metrics/recent`

### Запуск demo API

```powershell
dotnet run --project .\src\SystemMonitorAgent.DemoApi\SystemMonitorAgent.DemoApi.csproj
```

После запуска можно открыть:

- `http://localhost:5000/`
- `http://localhost:5000/health`
- `http://localhost:5000/api/metrics/latest`

### Быстрый сценарий локальной проверки

1. Запусти `SystemMonitorAgent.DemoApi`
2. Убедись, что у агента `AgentSettings:ApiUrl = http://localhost:5000/api/metrics`
3. Запусти агент
4. Открой `http://localhost:5000/api/metrics/latest` или `http://localhost:5000/api/metrics/recent`

Так reviewer или разработчик может проверить end-to-end поведение без отдельной подготовки внешнего API.

## Publish

Для установки как Windows Service сначала нужно опубликовать Worker-проект:

```powershell
dotnet publish .\src\SystemMonitorAgent.Worker\SystemMonitorAgent.Worker.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\artifacts\publish\SystemMonitorAgent
```

## Установка как Windows Service

1. Выполните publish.
2. Проверьте и при необходимости отредактируйте `appsettings.json` в publish-папке.
3. Запустите install script из **elevated PowerShell**.

```powershell
.\scripts\install-service.ps1 `
  -ExecutablePath .\artifacts\publish\SystemMonitorAgent\SystemMonitorAgent.exe `
  -StartAfterInstall
```

### Базовые операции со службой

```powershell
Start-Service -Name SystemMonitorAgent
Stop-Service -Name SystemMonitorAgent
Get-Service -Name SystemMonitorAgent
```

## Удаление службы

```powershell
.\scripts\uninstall-service.ps1 -ServiceName SystemMonitorAgent
```

## Конфигурация

Конфигурационные файлы:

- `src\SystemMonitorAgent.Worker\appsettings.json` — локальные значения по умолчанию
- `src\SystemMonitorAgent.Worker\appsettings.example.json` — пример для развёртывания
- `appsettings.json` в publish-папке — рабочий конфиг конкретной установленной службы

### Параметры `AgentSettings`

| Параметр | Назначение | Значение по умолчанию |
| --- | --- | --- |
| `ApiUrl` | Адрес API для отправки снимков | `http://localhost:5000/api/metrics` |
| `IntervalSeconds` | Интервал между циклами мониторинга | `30` |
| `HttpTimeoutSeconds` | Таймаут одного HTTP-запроса | `10` |
| `LogFilePath` | Путь к лог-файлам. Относительный путь считается от директории опубликованного `.exe` | `logs\agent-.log` |
| `WatchedProcesses` | Список процессов, наличие которых нужно проверять | `notepad`, `explorer` |
| `RetryQueueMaxSize` | Максимальный размер очереди повторных отправок в памяти | `100` |
| `RetryMaxAttempts` | Максимальное число попыток доставки одного снимка | `8` |
| `RetryInitialDelaySeconds` | Базовая задержка для backoff | `15` |
| `RetryMaxDelaySeconds` | Верхняя граница задержки между повторами | `600` |

## Поведение retry и resilience

- Первый цикл выполняется **сразу при старте**.
- Дальше агент работает по фиксированному расписанию через `PeriodicTimer`.
- Перед отправкой нового снимка агент сначала обрабатывает **очередь повторных отправок**.
- Для retry item хранятся:
  - время первого создания
  - число уже выполненных попыток
  - время следующей попытки
  - причина последней ошибки
- **Retryable failures**:
  - timeout
  - DNS / network / transport errors
  - `408`
  - `429`
  - `5xx`
- **Non-retryable failures**:
  - остальные `4xx`
- Backoff — exponential с ограничением сверху.
- `NextRetryAtUtc` означает момент, раньше которого повторную отправку выполнять нельзя; фактическая отправка произойдёт на первом следующем цикле мониторинга после наступления этого времени.
- Очередь повторных отправок намеренно сделана **bounded и in-memory**, чтобы память оставалась предсказуемой, а решение — лёгким и подходящим для take-home scope.
- Если очередь достигла лимита, самый старый элемент выбрасывается с warning log, а не допускается бесконтрольный рост памяти.

## Логирование

Агент пишет логи в:

- **Console** — удобно в console mode
- **Файл** — основной operational log
- **Windows Event Log** — дополнительная observability surface для Windows

### Где лежат логи

Если `LogFilePath` задан относительным путём, он нормализуется относительно директории исполняемого файла. Для стандартного publish-пути логи будут находиться здесь:

```text
.\artifacts\publish\SystemMonitorAgent\logs\
```

Примеры файлов:

```text
agent-20260514.log
agent-20260515.log
```

## Пример отправляемого JSON

Полный пример лежит в `samples\system-snapshot.example.json`.

Сокращённая форма:

```json
{
  "CollectedAtUtc": "2026-05-14T14:22:31Z",
  "Hostname": "monitor-node-01",
  "IpAddresses": [ "10.20.30.40" ],
  "WindowsVersion": "Microsoft Windows Server 2022 Standard (10.0.20348)",
  "UptimeSeconds": 86400,
  "CpuUsagePercent": 23.4
}
```

## Troubleshooting

| Симптом | Вероятная причина | Что проверить |
| --- | --- | --- |
| Служба стартует и сразу завершается | Ошибка конфигурации или fail-fast validation | Event Log, file logs, publish-версию `appsettings.json` |
| Логи не появляются | Неправильный путь или нет прав на запись | `LogFilePath`, доступ на запись в publish-папку |
| Снимки не доходят до API | Сеть, DNS, firewall, timeout, неретрайбл ответ API | file logs, доступность API с целевой машины |
| Установка службы падает | PowerShell запущен без admin rights или указан неверный `.exe` | elevated session и путь к publish-артефакту |
| Очередь retry растёт | Длительная недоступность API | warnings в логах, доступность API, размер очереди |

## Operational notes

- Этот сервис рассчитан на **долгоживущие Windows-хосты**, а не на container orchestration.
- Console mode — это режим для разработки и диагностики; **основной runtime — Windows Service**.
- Retry queue не является durable storage. Это осознанный компромисс: память остаётся предсказуемой, а реализация — достаточно зрелой для задания без лишней инфраструктурной сложности.
- Если в окружении требуется автоматический restart процесса после host-level failures, настройте **Windows Service recovery options** отдельно от встроенной retry-логики.
