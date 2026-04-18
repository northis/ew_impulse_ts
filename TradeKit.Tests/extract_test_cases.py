"""
Run once to regenerate ParseSignalTestCases.json from signals.json.
Usage: python extract_test_cases.py
"""
import json, re
from pathlib import Path

SRC = Path(__file__).parent / "TestData/signals.json"
DST = Path(__file__).parent / "TestData/ParseSignalTestCases.json"

with open(SRC, encoding="utf-8") as f:
    data = json.load(f)

msgs = data["messages"]

def get_text(m):
    t = m.get("text", "")
    if isinstance(t, list):
        return "".join(x if isinstance(x, str) else x.get("text", "") for x in t)
    return t

# ── patterns ──────────────────────────────────────────────────────────────────
xau_pat  = re.compile(r"(?i)(gold|xauusd)")
sig_pat  = re.compile(r"(?i)(buy|sell)\s*(now|market)?\s*[@\-\|:]\s*\d{3,5}")
lim_pat  = re.compile(r"(?i)\blimit\b")
tp_pat   = re.compile(r"(?i)tp\s*\d*\s*[@:\-]\s*\d{3,5}")
sl_pat   = re.compile(r"(?i)sl\s*[@:\-]\s*\d{3,5}")
be_pat   = re.compile(r"(?i)(entry point|break\s*-?\s*even|move sl to entry|b\.e\b|to the entry|running in profit)")
tp_hit_p = re.compile(r"(?i)(tp\s*\d*\s*hit|target\s*(hit|reached)|\U0001F3AF)")
sl_hit_p = re.compile(r"(?i)(sl\s*(was\s*)?hit|stop\s*out)")
close_p  = re.compile(r"(?i)(close[d\s]|close$)")
ad_pat   = re.compile(r"(?i)(vip|subscribe|join|signal.*(posted|sent|given)|profit.{0,20}running|running.{0,20}pips|\d+\s*pips)")
result_p = re.compile(r"(?i)(smashed|boom|1:\dRR|drawdown|zero drawdown|king of gold)")

# ── collect candidates ────────────────────────────────────────────────────────
def classify(m, t, is_reply):
    has_buysell = bool(sig_pat.search(t))
    has_limit   = bool(lim_pat.search(t))
    has_tp      = bool(tp_pat.search(t))
    has_sl      = bool(sl_pat.search(t))
    is_ad       = bool(ad_pat.search(t)) or bool(result_p.search(t))

    if is_reply:
        if be_pat.search(t):   return "reply_breakeven"
        if tp_hit_p.search(t): return "reply_tp_hit"
        if sl_hit_p.search(t): return "reply_sl_hit"
        if close_p.search(t):  return "reply_close"
        if is_ad:              return "reply_noise"
        return "reply_other"
    else:
        if has_buysell:
            if has_limit:
                return "limit_order"
            elif has_tp and has_sl:
                return "valid_market"
            else:
                return "incomplete"
        elif is_ad:
            return "ad_or_result"
        else:
            return "noise"

# build index for reply → original lookup
msg_by_id = {m["id"]: m for m in msgs}

# Per-year, per-category limits so every year gets represented
YEARS = ["2022", "2023", "2024", "2025", "2026"]
PER_YEAR_LIMITS = {
    "valid_market":     4,
    "limit_order":      2,
    "incomplete":       2,
    "ad_or_result":     2,
    "noise":            1,
    "reply_breakeven":  3,
    "reply_tp_hit":     2,
    "reply_sl_hit":     2,
    "reply_close":      2,
    "reply_noise":      1,
    "reply_other":      1,
}

# buckets keyed by (year, category)
year_buckets: dict[tuple[str, str], list] = {}
seen_texts: set[str] = set()

for m in msgs:
    if m.get("type") != "message":
        continue
    t = get_text(m)
    if not t.strip():
        continue
    if not xau_pat.search(t):
        continue

    year = (m.get("date") or "")[:4]
    if year not in YEARS:
        continue

    # de-duplicate nearly-identical texts (first 80 chars)
    key = re.sub(r"\s+", " ", t[:80]).lower()
    if key in seen_texts:
        continue

    is_reply = bool(m.get("reply_to_msg_id"))
    cat = classify(m, t, is_reply)
    limit = PER_YEAR_LIMITS.get(cat, 1)

    bucket = year_buckets.setdefault((year, cat), [])
    if len(bucket) >= limit:
        continue

    seen_texts.add(key)

    entry = {
        "id":       m["id"],
        "date":     m["date"],
        "replyTo":  m.get("reply_to_msg_id"),
        "text":     t,
        "category": cat,
    }

    # For reply tests, embed the referenced signal text so tests are self-contained
    if is_reply and m.get("reply_to_msg_id"):
        ref = msg_by_id.get(m["reply_to_msg_id"])
        if ref:
            entry["refText"] = get_text(ref)

    bucket.append(entry)

# flatten: sort by year then category order, so JSON is easy to read
order = list(PER_YEAR_LIMITS.keys())
cases = []
for year in YEARS:
    for cat in order:
        cases.extend(year_buckets.get((year, cat), []))

with open(DST, "w", encoding="utf-8") as f:
    json.dump(cases, f, ensure_ascii=False, indent=2)

print(f"Written {len(cases)} test cases to {DST}")
print(f"{'year':6s}  {'category':20s}  count")
for year in YEARS:
    for cat in order:
        n = len(year_buckets.get((year, cat), []))
        if n:
            print(f"  {year}  {cat:20s}  {n}")
