# Deploy DiagonalSignalerBot (cBot) на Linux через cTrader CLI + Docker

Запуск cBot `DiagonalSignalerBot` headless (без десктопа cTrader) через официальный
образ **cTrader CLI** (`ghcr.io/spotware/ctrader-console`). Проверено на образе
`latest` = CLI **5.9.10** (внутри .NET 6 SDK/runtime — совпадает с `net6.0` проектов
репозитория, поэтому ретаргетинг на .NET 8 не требуется).

## 1. Что лежит в папке

| Файл | Назначение |
|------|------------|
| `docker-compose.yml` | запуск бота, собирает образ из `Dockerfile` |
| `Dockerfile` | производный образ: fontconfig + шрифты для SVG-отчётов |
| `entrypoint.sh` | переключение live/backtest по `MODE` |
| `fonts.conf` | fontconfig-алиас «Helvetica Neue LT *» → Liberation Sans |
| `.env.example` | шаблон авторизации (cTID/счёт/symbol/period) → скопируй в `.env` |
| `parameters.cbotset.example` | шаблон параметров бота → скопируй в `parameters.cbotset` |
| `build-algo.ps1` | собирает `.algo` из исходников и кладёт в `./algo/` |
| `algo/` *(gitignored)* | собранный `DiagonalSignalerBot.algo` |
| `secrets/ctrader-cli.pwd` *(gitignored)* | пароль cTID (одна строка) |
| `reports/` *(gitignored)* | вывод бектеста (`report.html`, `report.json`, `Backtesting/`) |

## 2. Как передаются credentials и параметры

| Данные | Где хранить |
|--------|-------------|
| cTID (логин/email), номер счёта, symbol, period | `.env` |
| Telegram bot token + chat id | `parameters.cbotset` |
| `AllowToTrade` и все параметры бота | `parameters.cbotset` |
| **Пароль cTID** | **`secrets/ctrader-cli.pwd`** (файл, не `.env`) |

Два слоя: `.env` — «как подключиться» (авторизация), `parameters.cbotset` — «что
делает бот» (все параметры инстанса). Это штатный формат cTrader (JSON).

Почему пароль отдельно: cTrader CLI в batch-режиме (`run`) принимает пароль **только
через `--pwd-file`** (env-переменной для сырого пароля нет), а сами доки cTrader
прямо предупреждают не пихать пароль в переменные окружения/аргументы (он светится
в `/proc/*/environ`, попадает в логи и т.д.). Отдельный файл с `chmod 600` — это и
есть «лучший вариант» вместо пароля в `.env`.

## 3. Пошагово (Windows, Docker + WSL)

```powershell
cd deploy/diagonal

# 3.1. Собрать .algo (Docker сам тянет образ, .NET SDK не нужен)
powershell -File .\build-algo.ps1

# 3.2. Создать secrets и записать пароль cTID (одна строка, без пробелов)
New-Item -ItemType Directory -Force secrets | Out-Null
Set-Content -NoNewline -Path secrets\ctrader-cli.pwd -Value "твой_пароль"

# 3.3. Создать .env и parameters.cbotset из шаблонов
Copy-Item .env.example .env
#    открой .env: CTID, ACCOUNT, SYMBOL, PERIOD
Copy-Item parameters.cbotset.example parameters.cbotset
#    открой parameters.cbotset: TelegramBotToken, ChatId, AllowToTrade, символы и т.д.

# 3.4. Проверить, что Compose видит все подстановки
docker compose config

# 3.5. Собрать образ (fontconfig + шрифты) и запустить (foreground — увидишь вывод)
docker compose up --build

# ...или в фоне:
docker compose up -d --build
docker compose logs -f
```

## 4. Перенос на Linux-машину

Теперь образ **производный** (собран из `Dockerfile`), поэтому либо переносишь
сборочный контекст и собираешь на месте, либо тащишь готовый образ:

**Вариант A — собрать на Linux** (переносишь папку целиком):
```bash
# на Windows:
#   scp -r deploy/diagonal user@linux-host:/opt/diagonal-signaler

# на Linux:
cd /opt/diagonal-signaler
docker compose build
docker compose up -d
```

**Вариант B — перенести готовый образ** (без сборки на Linux):
```powershell
# на Windows:
docker compose build
docker save diagonal-signaler:local | gzip > diagonal-signaler.tar.gz
#   scp diagonal-signaler.tar.gz user@linux-host:/opt/
```
```bash
# на Linux:
docker load < /opt/diagonal-signaler.tar.gz
docker compose up -d
```

`.env`, `parameters.cbotset` и `secrets/ctrader-cli.pwd` заливаются вместе с папкой
(они gitignored — переноси их вручную, в git их не будет).

## 5. Параметры бота (`.cbotset`)

Все параметры бота задаются в `parameters.cbotset` (JSON, значения — строки):

| Параметр | Дефолт | Что делает |
|----------|--------|------------|
| `TelegramBotToken`, `ChatId` | пусто | Telegram-алерты (пусто = выключено) |
| `AllowToTrade` | `False` | `False` = только сигналы, `True` = реальные ордера |
| `RiskPercentFromDeposit` | `1` | риск на сделку, % депозита |
| `MaxVolumeLots` | `1` | макс. объём, лоты |
| `SymbolsToProceed` | 30 пар | торгуемые символы |
| `TimeFramesToProceed` | `Minute30,Hour` | торгуемые таймфреймы |
| `TakeProfitRatio` | `1.0` | R:R диагонали |
| `MinSizePercent`, `Period`, `BarsCount` | — | параметры зигзага/диагонали |

Любой параметр можно убрать из файла — тогда бот возьмёт свой скомпилированный
дефолт. Полный список с дефолтами:

```bash
docker run --rm -v "$(pwd)/algo:/mnt/algo:ro" \
  ghcr.io/spotware/ctrader-console:latest metadata /mnt/algo/DiagonalSignalerBot.algo
```

Значения в `.cbotset` — **строки**: булевы `"True"`/`"False"`, числа `"1.0"`.
Desktop-версия cTrader сохраняет файл с блоком `"Chart"` (Symbol/Period) — CLI его
игнорирует, потому что `--symbol`/`--period` задаются флагами.

## 6. Шрифты SVG-отчёта

Бот рендерит отчёт о сделке из SVG (`tradeResultTemplate.svg`) в PNG через
Svg.Skia/SkiaSharp. В шаблоне указаны `Helvetica Neue LT Com/Pro` — это
**коммерческий шрифт Linotype**, его нет в apt, а в базовом образе cTrader CLI
вообще нет ни шрифтов, ни `libfontconfig` (без него SkiaSharp не рендерит текст).

`Dockerfile` решает это тремя шагами:

1. Ставит `fontconfig` + свободные шрифты (`fonts-liberation`, `fonts-dejavu`).
2. `fonts.conf` подменяет `Helvetica Neue LT Com/Pro` (и `Helvetica Neue`) на
   **Liberation Sans** — метрически совместимый с Helvetica/Arial.
3. Докачивает нативные `libSkiaSharp.so` + `libHarfBuzzSharp.so` (linux-x64) с
   NuGet и кладёт их в `/usr/lib/x86_64-linux-gnu/`. Без них на Linux падает
   `DllNotFoundException: libSkiaSharp` — пакет SkiaSharp 3.0.0-preview ссылается
   только на Win/macOS нативы, Linux-нативы надо тянуть отдельно (они добавлены и
   в `TradeKit.Core.csproj`: `SkiaSharp.NativeAssets.Linux.NoDependencies` +
   `HarfBuzzSharp.NativeAssets.Linux`).

Проверка: `fc-match 'Helvetica Neue LT Pro'` → `Liberation Sans`; smoke-тест
`new SKBitmap(...)` в контейнере проходит.

Если есть лицензионные файлы Helvetica Neue — положи их в папку `fonts/` рядом с
`Dockerfile` и добавь в `Dockerfile`:

```dockerfile
COPY fonts/ /usr/share/fonts/truetype/
RUN fc-cache -f
```

(тогда `fonts.conf` можно удалить — семейства найдутся по их настоящим именам).

## 7. Режим бектеста (отладка)

Для прогона всего процесса без реальных сделок — в `.env` есть `MODE`:

```ini
MODE=backtest
BACKTEST_START=01/01/2025 00:00   # dd/MM/yyyy [hh:mm], UTC
BACKTEST_END=31/01/2025 23:59
BACKTEST_DATA_MODE=m1             # open | m1 | m1-csv | tick-csv | ticks
# BACKTEST_BALANCE=10000
# BACKTEST_SPREAD=1
```

Запуск (одноразовый контейнер: логи в терминал, выходит по завершении):

```bash
docker compose run --rm diagonal-signaler
```

Результаты в `./reports/` на хосте: `report.html`, `report.json` и папка
`Backtesting/` с журналом сделок. Вернуться к боевому режиму — `MODE=live`.

`m1`/`ticks` качают историю с сервера cTrader (нужен доступ в сеть), `open` — самый
быстрый, по ценам открытия. Для своей истории — `BACKTEST_DATA_FILE` + режим
`m1-csv`/`tick-csv` (формат CSV cTrader).

## 8. Полезные команды

```bash
docker compose logs -f                    # логи
docker compose restart                    # перезапуск
docker compose down                       # остановить и удалить контейнер
# список запущенных инстансов бота на аккаунте:
docker run --rm -v "$(pwd)/secrets:/mnt/secrets:ro" \
  ghcr.io/spotware/ctrader-console:latest cbots \
  --ctid="$CTID" --password="$PW" --account="$ACCOUNT" -q
```

## Заметки / ограничения

- `ctrader-cli build` кладёт результат в `bin/Release/net6.0/src.algo` с внутренним
  именем `src` (косметика — скрипт переименовывает файл в `DiagonalSignalerBot.algo`,
  а класс `[Robot]` внутри — `DiagonalSignalerBot`). Если хочешь «чистое» имя в списке
  инстансов, собери `.algo` через cTrader desktop (Build) и положи его в `./algo/`.
- Образ `latest` = CLI 5.9.10 на **.NET 6** (совпадает с `net6.0` проекта). Если
  позже перейдёшь на образ на .NET 8 (CLI 5.10+), нужно ретаргетить проекты на
  `net8.0` (см. требование «.algo built for .NET 8» в доках cTrader CLI).
- `run` — long-running процесс, поэтому `restart: unless-stopped` + `--exit-on-stop`
  (контейнер перезапустится, если бот сам остановится).
- Для Telegram боту нужен доступ в интернет (`--full-access` уже включён).
- `parameters.cbotset` монтируется как bind-mount — файл **должен существовать** до
  `docker compose up` (иначе Docker создаст на его месте пустую папку). Сделай
  `Copy-Item parameters.cbotset.example parameters.cbotset` (шаг 3.3).
