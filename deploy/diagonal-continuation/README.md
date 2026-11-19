# DiagonalContinuationBot — research cBot

Research cBot for the "diagonal as the entry impulse" continuation trade
(DIAGONAL.md §9.43). It is **not** a production bot: the h1 exact-corridor result is
positive but narrow, m15 is negative, the permissive corridor is near zero, and the
`1.272` target is long-biased. Use it to run the cTrader backtest with real spread/swap.

## What it does

- The finder detects a completed 0-1-2-3-4-5 diagonal (exact corridor:
  `W2/W1 >= 0.5`, `W4/W3 <= 0.382`).
- After the diagonal signal the bot tracks the running extreme and keeps a **limit**
  at `extreme - k*D` in the diagonal direction.
- The stop is `P0` (plus the optional `StopBuffer * D`).
- The target is `P0 + f*D` (default), `end + f*D` or `entry + f*D`.
- The deep correction itself is the entry trigger: this is the V3 dynamic re-price
  variant; no separate end confirmation is used.
- One continuation setup per symbol/timeframe at a time. A filled position is managed by
  the broker SL/TP; the bot also applies an optional time stop after `MaxHoldBars`.

## Defaults (best h1 exact cell)

| Parameter | Default |
|---|---|
| Timeframe | h1 (`H1 only = True`) |
| Initial diagonal | `LEADING` |
| Min W2 retrace / Max W4/W3 | `0.5` / `0.382` |
| Entry retrace `k` | `0.5` |
| Target anchor | `DIAGONAL_START` (`P0`) |
| TP `f` | `1.272` |
| Stop buffer | `0` |
| Max wait / max hold | `300` / `500` bars |

`1.0` (the diagonal end) is the more balanced alternative; `1.618` was rejected by the
research (fresh negative, short side negative). `AllowToTrade` is `False` in the code;
the cTrader backtest still trades because `IsBacktesting` is true. For demo/live set it
to `True`.

## Build the .algo

Requires Docker. From this folder:

```powershell
powershell -File .\build-algo.ps1
```

The staged files are `algo/DiagonalContinuationBot.algo` and `.algo.metadata`.
Copy `parameters.cbotset.example` to `parameters.cbotset`, adjust the account/symbol,
then run the cTrader CLI backtest with the `.algo` and the `.cbotset`.

The parameter metadata check uses `../diagonal/sync-params.ps1`; if the `.cbotset` is
stale, the build script warns. Run it with `-Fix` to actualize the file.

## Caveats

- The h1 exact result does **not** reproduce on m15 (negative) and is close to zero on
  the permissive corridor (5 bp negative). Keep `H1 only = True` and the exact corridor.
- The `1.272` cell is long-biased: on h1 exact long `+0.282R` vs short `+0.001R`;
  2023–2026 short is negative. The backtest should be run with and without a long-only
  filter before any production decision.
- The research used 2 bp; the cTrader backtest includes real spread, commission and swap.
  This is the actual validation step.
- The bot is a research tool; it does not place a stop/limit on the counter-trend signal.
