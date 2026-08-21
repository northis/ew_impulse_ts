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
| `.env.example` | шаблон авторизации и секретов (cTID/счёт/symbol/period/Telegram) → скопируй в `.env` |
| `parameters.cbotset.example` | шаблон параметров бота → скопируй в `parameters.cbotset` |
| `build-algo.ps1` | собирает `.algo` из исходников и кладёт в `./algo/` |
| `algo/` *(gitignored)* | собранный `DiagonalSignalerBot.algo` |
| `secrets/ctrader-cli.pwd` *(gitignored)* | пароль cTID (одна строка) |
| `reports/` *(gitignored)* | вывод бектеста (`report.html`, `report.json`, `Backtesting/`) |

## 2. Как передаются credentials и параметры

| Данные | Где хранить |
|--------|-------------|
| cTID (логин/email), номер счёта, symbol, period | `.env` |
| Telegram bot token + chat id | `.env` (`TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`) |
| `AllowToTrade` и все параметры бота | `parameters.cbotset` |
| **Пароль cTID** | **`secrets/ctrader-cli.pwd`** (файл, не `.env`) |

Два слоя: `.env` — «как подключиться» (авторизация + секреты), `parameters.cbotset` —
«что делает бот» (все параметры инстанса). Это штатный формат cTrader (JSON).

Почему токен Telegram НЕ в `.cbotset`: cTrader CLI при старте печатает таблицу
**всех** значений `.cbotset` в лог (значения лишь обрезаются до ~25 символов, т.е.
часть секрета всё равно утекает). Поэтому бот читает токен и chat id из
env-переменных `TELEGRAM_BOT_TOKEN` / `TELEGRAM_CHAT_ID` (override параметров
`TelegramBotToken`/`ChatId`, см. `CTraderBaseRobot`), а в `.cbotset` на их месте
стоит плейсхолдер `***` (пустые значения CLI отвергает: «All custom parameters
must have a value»). Плейсхолдер без env-переменной трактуется как «значения нет»
(Telegram выключен).

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
#    открой .env: CTID, ACCOUNT, SYMBOL, PERIOD, TELEGRAM_BOT_TOKEN, TELEGRAM_CHAT_ID
Copy-Item parameters.cbotset.example parameters.cbotset
#    открой parameters.cbotset: AllowToTrade, символы и т.д.

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
| `TelegramBotToken`, `ChatId` | пусто | Telegram-алерты (пусто = выключено). **Не заполняй здесь** — задавай через `TELEGRAM_BOT_TOKEN` / `TELEGRAM_CHAT_ID` в `.env`: значения `.cbotset` CLI печатает в стартовый лог, а env-переменные бот читает как override этих параметров |
| `AllowToTrade` | `False` | `False` = только сигналы, `True` = реальные ордера |
| `RiskPercentFromDeposit` | `1` | риск на сделку, % депозита |
| `MaxVolumeLots` | `1` | макс. объём, лоты |
| `SymbolsToProceed` | 30 пар | торгуемые символы |
| `TimeFramesToProceed` | `Minute30,Hour` | торгуемые таймфреймы |
| `TakeProfitRatio` | `1.0` | R:R диагонали |
| `RetraceAction` | `NONE` | что делать при достижении пересчитанного уровня 23.6% в прибыли (DIAGONAL.md §6.4): `NONE`, `BREAKEVEN`, `BREAKEVEN_AND_HALF`, `HALF`, `CLOSE` |
| `MinRiskRewardRatio` | `0` | мин. R:R для режима TP «23.6% диагонали» (DIAGONAL.md §6.5): если на пробое сетап невыгоден, бот не входит, а ждёт на закрытиях свечей, пересчитывая TP по свежему экстремуму волны 5. `0` = выключено |
| `Wave3RetraceRatio` | `0` | TP на уровне отката от волны 3 (DIAGONAL.md §6.6): `TP = W5 ∓ доля·|W3|`, например `0.382`. Перекрывает `TakeProfitRatio` и `TakeProfitAtRetrace`; `0` = выключено |
| `MinWave4Wave2Level` | `0.236` | насколько глубоко волна 4 должна зайти в диапазон волны 2 (D-W4-24, DIAGONAL.md §4); меньше — мягче фильтр, `0` — остаётся только D-OVERLAP |
| `RequireWave4Shorter` | `True` | требовать, чтобы волна 4 была короче волны 2 по числу баров (D-TIME-24). `False` пропускает и многодневные клины |
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
Svg.Skia/SkiaSharp. В базовом образе cTrader CLI нет ни шрифтов, ни
`libfontconfig` — SkiaSharp падал бы с `DllNotFoundException: libSkiaSharp` и не
рендерил бы текст.

`Dockerfile` решает это тремя шагами:

1. Ставит `fontconfig` + **TeX Gyre Heros** (пакет `fonts-texgyre`, бесплатный
   клон Helvetica) + `fonts-dejavu` (запасные глифы). Шрифты качаются при сборке
   образа из apt — локально ничего подкладывать не нужно.
2. Делает симлинк `tex-gyre` в `/usr/share/fonts/` — Debian ставит
   `fonts-texgyre` в `/usr/share/texmf/fonts/opentype/public/tex-gyre/`
   (fontconfig видит его через texmf-хук; симлинк — страховка, чтобы семейство
   было видно и в стандартном дереве шрифтов).
3. **Это критично.** Нативная сборка — обычный `SkiaSharp.NativeAssets.Linux`
   (слинкован с fontconfig), а **не** `.NoDependencies`. Font manager
   NoDependencies-сборки умеет матчить только по имени семейства
   (`MatchFamily`/`FromFamilyName` работают), но `SKFontManager.MatchCharacter()`
   там **всегда возвращает null** — а Svg.Skia 2.0.0.1 резолвит typeface каждого
   текстового рана именно через `MatchCharacter` (`SkiaAssetLoader.FindTypefaces`).
   Итог: любой `<text>` тихо рендерится дефолтным засечковым шрифтом
   (**DejaVu Serif**, «похожим на Georgia» — так выглядел баг), игнорируя
   `font-family` и `font-weight`. Проверено эмпирически в контейнере: на
   NoDependencies-сборке `MatchCharacter("TeX Gyre Heros", ..., 'A')` → `null`,
   на обычной → `TeX Gyre Heros w=400`. Регулярной сборке нужен только
   `libfontconfig` (никаких X-libs) — он ставится в шаге 1.
4. Докачивает нативные `libSkiaSharp.so` + `libHarfBuzzSharp.so` (linux-x64) с
   NuGet и кладёт их в `/usr/lib/x86_64-linux-gnu/`. Без них на Linux падает
   `DllNotFoundException: libSkiaSharp` — пакет SkiaSharp 3.0.0-preview ссылается
   только на Win/macOS нативы (Linux-нативы добавлены и в `TradeKit.Core.csproj`,
   тоже вариант `SkiaSharp.NativeAssets.Linux`).

Проверка (в контейнере): `ldd /usr/lib/x86_64-linux-gnu/libSkiaSharp.so` должен
показывать `libfontconfig.so.1`; `fc-list | grep -ci gyre` > 0. Если в отчёте
снова появились засечки — сначала смотри, какая сборка `libSkiaSharp.so` реально
подгрузилась (app-local копия из NuGet важнее системной в `/usr/lib`, и
`dotnet run` молча её перезаписывает):
`docker compose run --rm --entrypoint sh diagonal-signaler -c 'ldd /usr/lib/x86_64-linux-gnu/libSkiaSharp.so | grep fontconfig'`.

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
