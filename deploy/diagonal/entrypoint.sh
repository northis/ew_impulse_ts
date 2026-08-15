#!/bin/sh
set -e

# --- paths (bind-mounted) ---
ALGO="${ALGO_FILE:-/mnt/algo/DiagonalSignalerBot.algo}"
PARAMS="/mnt/params.cbotset"
PWD_FILE="${PWD_FILE:-/mnt/secrets/ctrader-cli.pwd}"

# --- required auth ---
: "${CTID:?CTID is required (set it in .env)}"
: "${ACCOUNT:?ACCOUNT is required (set it in .env)}"

# --- defaults ---
SYMBOL="${SYMBOL:-EURUSD}"
PERIOD="${PERIOD:-m5}"
MODE="${MODE:-live}"

if [ "$MODE" = "backtest" ]; then
    : "${BACKTEST_START:?BACKTEST_START is required in backtest mode (dd/MM/yyyy [hh:mm] UTC)}"
    : "${BACKTEST_END:?BACKTEST_END is required in backtest mode (dd/MM/yyyy [hh:mm] UTC)}"

    # Copy the .algo into a writable dir so the "Backtesting" results + reports
    # persist on the host (the /mnt/algo mount is read-only).
    WORK="${BACKTEST_WORK_DIR:-/mnt/reports}"
    mkdir -p "$WORK"
    BT_ALGO="$WORK/$(basename "$ALGO")"
    cp -f "$ALGO" "$BT_ALGO"

    set -- \
        "$BT_ALGO" "$PARAMS" \
        --start="$BACKTEST_START" \
        --end="$BACKTEST_END" \
        --data-mode="${BACKTEST_DATA_MODE:-m1}"

    [ -n "${BACKTEST_DATA_FILE:-}" ]  && set -- "$@" --data-file="$BACKTEST_DATA_FILE"
    [ -n "${BACKTEST_BALANCE:-}" ]    && set -- "$@" --balance="$BACKTEST_BALANCE"
    [ -n "${BACKTEST_COMMISSION:-}" ] && set -- "$@" --commission="$BACKTEST_COMMISSION"
    [ -n "${BACKTEST_SPREAD:-}" ]     && set -- "$@" --spread="$BACKTEST_SPREAD"

    set -- "$@" \
        --report="${BACKTEST_REPORT:-$WORK/report.html}" \
        --report-json="${BACKTEST_REPORT_JSON:-$WORK/report.json}" \
        --ctid="$CTID" --pwd-file="$PWD_FILE" --account="$ACCOUNT" \
        --symbol="$SYMBOL" --period="$PERIOD" --full-access

    exec dotnet ctrader-cli.dll backtest "$@"
fi

# --- live mode ---
exec dotnet ctrader-cli.dll run "$ALGO" "$PARAMS" \
    --ctid="$CTID" --pwd-file="$PWD_FILE" --account="$ACCOUNT" \
    --symbol="$SYMBOL" --period="$PERIOD" --full-access --exit-on-stop
