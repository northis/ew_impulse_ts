"""
Simple webhook server to receive labeled zigzag segment data from TradingView alerts.
Stores data in a local SQLite database.

Usage:
    pip install flask
    python webhook_server.py

The server listens on http://localhost:5555/webhook
TradingView alert webhook URL (via ngrok): https://<your-ngrok-id>.ngrok.io/webhook
"""

import json
import sqlite3
import os
from datetime import datetime
from flask import Flask, request, jsonify

app = Flask(__name__)

DB_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "zigzag_labels.db")


def get_db():
    """Get a database connection with row factory."""
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    return conn


def init_db():
    """Initialize the database schema."""
    conn = get_db()
    conn.execute("""
        CREATE TABLE IF NOT EXISTS labeled_segments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            ticker TEXT NOT NULL,
            timeframe TEXT NOT NULL,
            broker TEXT,
            start_time TEXT NOT NULL,
            start_price REAL NOT NULL,
            end_time TEXT NOT NULL,
            end_price REAL NOT NULL,
            model TEXT NOT NULL,
            received_at TEXT NOT NULL,
            UNIQUE(ticker, timeframe, start_time, end_time, model)
        )
    """)
    conn.commit()
    conn.close()


@app.route("/webhook", methods=["POST"])
def webhook():
    """Receive labeled segment data from TradingView alert webhook."""
    try:
        data = request.get_json(force=True)
        if not isinstance(data, list):
            data = [data]

        conn = get_db()
        inserted = 0
        skipped = 0
        now = datetime.utcnow().isoformat()

        for entry in data:
            try:
                conn.execute("""
                    INSERT OR IGNORE INTO labeled_segments
                    (ticker, timeframe, broker, start_time, start_price, end_time, end_price, model, received_at)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """, (
                    entry.get("ticker", ""),
                    entry.get("tf", ""),
                    entry.get("broker", ""),
                    entry.get("startTime", ""),
                    float(entry.get("startPrice", 0)),
                    entry.get("endTime", ""),
                    float(entry.get("endPrice", 0)),
                    entry.get("model", ""),
                    now,
                ))
                if conn.total_changes > inserted + skipped:
                    inserted += 1
                else:
                    skipped += 1
            except (ValueError, KeyError) as e:
                print(f"  Skipped invalid entry: {e}")
                skipped += 1

        conn.commit()
        conn.close()

        print(f"[{now}] Received {len(data)} entries: {inserted} inserted, {skipped} skipped/duplicates")
        return jsonify({"status": "ok", "inserted": inserted, "skipped": skipped}), 200

    except Exception as e:
        print(f"Error processing webhook: {e}")
        return jsonify({"status": "error", "message": str(e)}), 400


@app.route("/segments", methods=["GET"])
def list_segments():
    """List all stored labeled segments. Optional query params: ticker, tf, model."""
    conn = get_db()
    query = "SELECT * FROM labeled_segments WHERE 1=1"
    params = []

    ticker = request.args.get("ticker")
    if ticker:
        query += " AND ticker = ?"
        params.append(ticker)

    tf = request.args.get("tf")
    if tf:
        query += " AND timeframe = ?"
        params.append(tf)

    model = request.args.get("model")
    if model:
        query += " AND model = ?"
        params.append(model)

    query += " ORDER BY received_at DESC"

    rows = conn.execute(query, params).fetchall()
    conn.close()

    result = [dict(row) for row in rows]
    return jsonify(result), 200


@app.route("/segments/export", methods=["GET"])
def export_csv():
    """Export all segments as CSV."""
    conn = get_db()
    rows = conn.execute("SELECT * FROM labeled_segments ORDER BY ticker, timeframe, start_time").fetchall()
    conn.close()

    lines = ["ticker,timeframe,broker,start_time,start_price,end_time,end_price,model,received_at"]
    for row in rows:
        lines.append(",".join(str(row[col]) for col in row.keys()))

    return "\n".join(lines), 200, {"Content-Type": "text/csv", "Content-Disposition": "attachment; filename=zigzag_labels.csv"}


@app.route("/health", methods=["GET"])
def health():
    """Health check endpoint."""
    return jsonify({"status": "ok"}), 200


if __name__ == "__main__":
    init_db()
    print(f"Database: {DB_PATH}")
    print("Webhook server starting on http://localhost:5555")
    print("Endpoints:")
    print("  POST /webhook        - receive TradingView alert data")
    print("  GET  /segments       - list stored segments (filter: ?ticker=&tf=&model=)")
    print("  GET  /segments/export - download CSV")
    print("  GET  /health         - health check")
    print()
    print("To expose to TradingView, run: ngrok http 5555")
    app.run(host="0.0.0.0", port=5555, debug=True)
