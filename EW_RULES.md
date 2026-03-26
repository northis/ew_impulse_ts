# Elliott Wave Rules — Generator Reference

Extracted from `TradeKit.Core/PatternGeneration/PatternGenerator.cs` and
`TradeKit.Core/AlgoBase/ElliottWavePatternHelper.cs`.

All rules here reflect the **generator implementation**, which may deviate from
classical Elliott Wave literature.

---

## Table of Contents

1. [Global Constants](#1-global-constants)
2. [Model Catalogue & Probability Coefficients](#2-model-catalogue--probability-coefficients)
3. [Correction Depth Classification](#3-correction-depth-classification)
4. [Fibonacci Ratio Maps](#4-fibonacci-ratio-maps)
5. [IMPULSE](#5-impulse)
6. [SIMPLE_IMPULSE](#6-simple_impulse)
7. [DIAGONAL (Contracting Initial / Contracting Ending)](#7-diagonal-contracting-initial--contracting-ending)
8. [DIAGONAL (Expanding Initial / Expanding Ending)](#8-diagonal-expanding-initial--expanding-ending)
9. [ZIGZAG](#9-zigzag)
10. [DOUBLE_ZIGZAG](#10-double_zigzag)
11. [TRIPLE_ZIGZAG](#11-triple_zigzag)
12. [FLAT_EXTENDED](#12-flat_extended)
13. [FLAT_REGULAR](#13-flat_regular)
14. [FLAT_RUNNING](#14-flat_running)
15. [TRIANGLE_CONTRACTING](#15-triangle_contracting)
16. [TRIANGLE_RUNNING](#16-triangle_running)
17. [TRIANGLE_EXPANDING](#17-triangle_expanding)
18. [COMBINATION](#18-combination)
19. [Duration (Bar) Distributions](#19-duration-bar-distributions)
20. [Shared Generator Behaviour](#20-shared-generator-behaviour)

---

## 1. Global Constants

| Constant | Value | Meaning |
|---|---|---|
| `SIMPLE_BARS_THRESHOLD` | 100 | Minimum bars for a full structural decomposition; below this a simple random set is used |
| `MAX_DEEP_LEVEL` | 10 | Maximum recursion depth for sub-wave generation |
| `MAIN_ALLOWANCE_MAX_RATIO` | 0.05 (5%) | Tolerance applied when placing wave ends near Fibonacci levels (waves don't land exactly on fibs) |

---

## 2. Model Catalogue & Probability Coefficients

The probability coefficient is used for **weighted random selection** when a parent
model picks sub-wave models. Higher value = more likely to be chosen.

| Model | Probability Coefficient | Notes |
|---|---|---|
| `IMPULSE` | **1.0** | |
| `SIMPLE_IMPULSE` | 0.25 | Same sub-wave rules as IMPULSE; generated as a simplified random set |
| `DIAGONAL_CONTRACTING_INITIAL` | 0.03 | Leading diagonal |
| `DIAGONAL_CONTRACTING_ENDING` | **1.0** | Ending diagonal; truncation allowed |
| `DIAGONAL_EXPANDING_INITIAL` | 0.01 | |
| `DIAGONAL_EXPANDING_ENDING` | 0.0001 | Extremely rare |
| `ZIGZAG` | **1.0** | |
| `DOUBLE_ZIGZAG` | **1.0** | |
| `TRIPLE_ZIGZAG` | 0.001 | Very rare |
| `FLAT_REGULAR` | 0.0005 | Very rare |
| `FLAT_EXTENDED` | **1.0** | |
| `FLAT_RUNNING` | **1.0** | |
| `COMBINATION` | **1.0** | |
| `TRIANGLE_CONTRACTING` | **1.0** | |
| `TRIANGLE_RUNNING` | 0.1 | |
| `TRIANGLE_EXPANDING` | 0.001 | Very rare |

---

## 3. Correction Depth Classification

Used to decide which Fibonacci retracement map to apply when a correction plays
the role of wave 2 / wave 4 / wave B / wave X etc.

| Category | Models |
|---|---|
| **Shallow** (retraces 23.6 – 50%) | `COMBINATION`, `FLAT_EXTENDED`, `FLAT_RUNNING`, `TRIANGLE_CONTRACTING`, `TRIANGLE_RUNNING` |
| **Deep** (retraces 50 – 95%) | `ZIGZAG`, `DOUBLE_ZIGZAG` |

> `TRIPLE_ZIGZAG`, `FLAT_REGULAR`, `TRIANGLE_EXPANDING` are not explicitly
> classified in either set; they fall back to the constraining parent's logic.

**Truncation-eligible models** (5 % probability of truncated 5th / ending wave):
`IMPULSE`, `DIAGONAL_CONTRACTING_ENDING`

---

## 4. Fibonacci Ratio Maps

Each map is a `SortedDictionary<byte, double>` where:
- **Key** (byte, 0–99) = probability weight (key / sum-of-all-keys gives the
  normalised probability for that Fibonacci level).
- **Value** (double) = Fibonacci ratio.
- Key = 0 is a sentinel (never selected directly).

Selection inside a min/max range: only map entries with value within [min, max]
participate; their keys are then used as weights.

A small random noise of up to 5% is added on top of every selected Fibonacci level
(`MAIN_ALLOWANCE_MAX_RATIO`), so the wave endpoint does not land exactly on the fib.

---

### 4.1 Shallow Correction Retracements — `MAP_SHALLOW_CORRECTION`

Used for wave 2, wave 4, wave B etc. when the corrective model is shallow.

| Ratio | Weight | Probability (no constraint) |
|---|---|---|
| 0.236 | 5 | 4.0 % |
| 0.382 | 35 | 28.0 % |
| 0.500 | 85 | **68.0 %** |

Sum = 125

---

### 4.2 Deep Correction Retracements — `MAP_DEEP_CORRECTION`

| Ratio | Weight | Probability (no constraint) |
|---|---|---|
| 0.500 | 5 | 2.5 % |
| 0.618 | 25 | 12.6 % |
| 0.786 | 70 | 35.2 % |
| 0.950 | 99 | **49.7 %** |

Sum = 199

---

### 4.3 Impulse — Wave 3 / Wave 1 Length Ratio — `IMPULSE_3_TO_1`

Wave 3 is never the shortest (enforced by min constraint in the generator).

| Ratio | Weight | Probability (no constraint) |
|---|---|---|
| 0.618 | 5  | 1.8 % |
| 0.786 | 10 | 3.6 % |
| 1.000 | 15 | 5.4 % |
| 1.618 | 25 | 8.9 % |
| 2.618 | 60 | 21.4 % |
| 3.618 | 75 | 26.8 % |
| 4.236 | 90 | **32.1 %** |

Sum = 280

---

### 4.4 Impulse — Wave 5 / Wave 1 Length Ratio — `IMPULSE_5_TO_1`

| Ratio | Weight | Probability (no constraint) |
|---|---|---|
| 0.382 | 5  | 1.2 % |
| 0.618 | 10 | 2.4 % |
| 0.786 | 20 | 4.8 % |
| 1.000 | 25 | 6.0 % |
| 1.618 | 75 | 18.1 % |
| 2.618 | 85 | 20.5 % |
| 3.618 | 95 | 22.9 % |
| 4.236 | 99 | **23.9 %** |

Sum = 414

---

### 4.5 Contracting Diagonal — Wave 3 / Wave 1 — `CONTRACTING_DIAGONAL_3_TO_1`

| Ratio | Weight | Probability |
|---|---|---|
| 0.500 | 5  | 12.5 % |
| 0.618 | 15 | 37.5 % |
| 0.786 | 20 | **50.0 %** |

Sum = 40

---

### 4.6 Zigzag — Wave C / Wave A — `ZIGZAG_C_TO_A`

Also used for DOUBLE_ZIGZAG Y/W ratio.

| Ratio | Weight | Probability (no constraint) |
|---|---|---|
| 0.618 | 5  | 1.2 % |
| 0.786 | 25 | 6.1 % |
| 0.786 | 35 | 8.5 % (second entry, combined 14.6 %) |
| 1.000 | 75 | 18.3 % |
| 1.618 | 85 | 20.7 % |
| 2.618 | 90 | 22.0 % |
| 3.618 | 95 | **23.2 %** |

Sum = 410

---

### 4.7 Triple Zigzag — Z / W ratio — `ZIGZAG_X_Z_TO_W`

| Ratio | Weight | Probability (no constraint) |
|---|---|---|
| 1.000 | 5  | 2.0 % |
| 1.618 | 25 | 10.0 % |
| 2.618 | 50 | 20.0 % |
| 3.618 | 80 | 32.0 % |
| 4.236 | 90 | **36.0 %** |

Sum = 250

---

### 4.8 Extended Flat — Wave C / Wave A — `MAP_EX_FLAT_WAVE_C_TO_A`

| Ratio | Weight | Probability |
|---|---|---|
| 1.618 | 20 | 10.3 % |
| 2.618 | 80 | 41.0 % |
| 3.618 | 95 | **48.7 %** |

Sum = 195

---

### 4.9 Regular Flat — Wave C / Wave A — `MAP_REG_FLAT_WAVE_C_TO_A`

| Ratio | Weight | Probability |
|---|---|---|
| 1.000 | 5  | 2.8 % |
| 1.272 | 80 | **44.4 %** |
| 1.618 | 95 | 52.8 % |

Sum = 180

---

### 4.10 Running Flat — Wave C / Wave A — `MAP_RUNNING_FLAT_WAVE_C_TO_A`

| Ratio | Weight | Probability |
|---|---|---|
| 0.500 | 5  | 1.7 % |
| 0.618 | 20 | 6.9 % |
| 1.000 | 80 | 27.6 % |
| 1.272 | 90 | 31.0 % |
| 1.618 | 95 | **32.8 %** |

Sum = 290

---

### 4.11 Contracting Triangle — Each Wave / Previous Wave — `MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV`

| Ratio | Weight | Probability |
|---|---|---|
| 0.500 | 5  | 1.7 % |
| 0.618 | 20 | 6.9 % |
| 0.786 | 80 | 27.6 % |
| 0.900 | 90 | 31.0 % |
| 0.950 | 95 | **32.8 %** |

Sum = 290

---

### 4.12 Expanding Triangle / Expanding Diagonal — Each Wave / Previous Wave — `MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV`

Also used for wave 3 / wave 2 of expanding diagonals.

| Ratio | Weight | Probability |
|---|---|---|
| 1.272 | 5  | 2.4 % |
| 1.618 | 30 | 14.3 % |
| 2.618 | 80 | 38.1 % |
| 3.618 | 95 | **45.2 %** |

Sum = 210

---

## 5. IMPULSE

**Structure:** 5 motive waves — 1 · 2 · 3 · 4 · 5

### Allowed sub-wave models

| Wave | Allowed models |
|---|---|
| 1 | `IMPULSE`, `DIAGONAL_CONTRACTING_INITIAL`, `DIAGONAL_EXPANDING_INITIAL` |
| 2 | `ZIGZAG`, `DOUBLE_ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING` |
| 3 | `IMPULSE` only |
| 4 | `ZIGZAG`, `DOUBLE_ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING`, `TRIANGLE_CONTRACTING`, `TRIANGLE_RUNNING` |
| 5 | `IMPULSE`, `DIAGONAL_CONTRACTING_ENDING`, `DIAGONAL_EXPANDING_ENDING` |

### Price rules

1. **Wave 1 length** as a fraction of the total impulse range:
   - Generator draws from a normal distribution: min 5%, max 75%, mean **25%**.
2. **Wave 2 retracement of Wave 1**:
   - ~45 % chance Wave 2 is a **deep** correction → uses `MAP_DEEP_CORRECTION` (50–95 %)
   - ~45 % chance Wave 2 is a **shallow** correction → uses `MAP_SHALLOW_CORRECTION` (23.6–50 %)
   - ~5–10 % chance both Wave 2 and Wave 4 are deep simultaneously
3. **Wave 3 is never the shortest** wave — enforced hard constraint.
   - Wave 3 / Wave 1 ratio: see `IMPULSE_3_TO_1` (§ 4.3)
4. **Wave 5 / Wave 1** ratio: see `IMPULSE_5_TO_1` (§ 4.4)
5. **Wave 4 / Wave 1 non-overlap** — Wave 4 must not overlap Wave 1 price territory.
   - Additional guard: back-limit of Wave 2 is set to `wave1 + (wave4−wave1)/2`,
     so the "running" sub-part of Wave 2 cannot reach Wave 4.
6. **Truncation** (Wave 5 < Wave 3 end): 5 % probability if the model is eligible.

### Extended-wave selection

| Condition | Wave 1 type | Wave 5 type | Extended wave |
|---|---|---|---|
| Wave 5 > Wave 3 > Wave 1 | Diagonal Initial (60 %) or IMPULSE (40 %) | `IMPULSE` | **5** |
| Wave 1 > Wave 3 (Wave 1 extended) | `IMPULSE` | Diagonal Ending (60 %) or `IMPULSE` (40 %) | **1** |
| Wave 3 extended (default) | Diagonal Initial (50 %) or `IMPULSE` (50 %) | Depends; opposite type preferred | **3** |

---

## 6. SIMPLE_IMPULSE

Same structural rules and sub-wave set as `IMPULSE`, but generated as a simple
random price path (no recursive sub-decomposition). Probability coefficient = 0.25.

---

## 7. DIAGONAL (Contracting Initial / Contracting Ending)

**Structure:** 5 waves — 1 · 2 · 3 · 4 · 5

### Sub-wave models

| | `DIAGONAL_CONTRACTING_INITIAL` | `DIAGONAL_CONTRACTING_ENDING` |
|---|---|---|
| Wave 1 | `IMPULSE`, `DIAGONAL_CONTRACTING_INITIAL`, `ZIGZAG`, `DOUBLE_ZIGZAG` | `ZIGZAG`, `DOUBLE_ZIGZAG` |
| Wave 2 | `ZIGZAG`, `DOUBLE_ZIGZAG` | `ZIGZAG`, `DOUBLE_ZIGZAG` |
| Wave 3 | `IMPULSE`, `ZIGZAG`, `DOUBLE_ZIGZAG` | `ZIGZAG`, `DOUBLE_ZIGZAG` |
| Wave 4 | `ZIGZAG`, `DOUBLE_ZIGZAG` | `ZIGZAG`, `DOUBLE_ZIGZAG` |
| Wave 5 | `IMPULSE`, `DIAGONAL_CONTRACTING_ENDING`, `ZIGZAG`, `DOUBLE_ZIGZAG` | `ZIGZAG`, `DOUBLE_ZIGZAG` |

### Price rules (shared for both contracting diagonals)

1. **Wave 1** length: normal distribution, min 70%, max 90% of total range, mean **78.6%**.
2. **Wave 2** retracement of Wave 1: normal distribution, min **50%**, max **75%**, mean **66%**.
3. **Wave 3 / Wave 1** ratio: `CONTRACTING_DIAGONAL_3_TO_1` (§ 4.5): 0.5–0.786.
   - Wave 3 must exceed Wave 2 retracement.
4. **Wave 4** length drawn from `[wave1–wave3 gap × 1.05, wave3Len − rest]`.
   - Wave 4 overlaps Wave 1 price territory (contracting diagonal rule).
5. **Wave 5** ends at the pattern's target price (may be truncated).
6. Each wave must be **shorter** than the previous same-direction wave
   (channels converge).
7. **Truncation** allowed (5 % probability) for `DIAGONAL_CONTRACTING_ENDING`.

### Probability coefficients

| Model | Coefficient |
|---|---|
| `DIAGONAL_CONTRACTING_INITIAL` | 0.03 |
| `DIAGONAL_CONTRACTING_ENDING` | 1.0 |

---

## 8. DIAGONAL (Expanding Initial / Expanding Ending)

**Structure:** 5 waves — 1 · 2 · 3 · 4 · 5

Sub-wave models are the same as the contracting equivalent.

### Price rules

1. **Wave 1** length: 10–40% of total range.
2. **Wave 2** retracement of Wave 1: `MAP_DEEP_CORRECTION` (50–95 %).
3. **Wave 3 / Wave 1**: uses `MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV` ratios (1.272–3.618),
   with min and max derived from remaining range.
4. **Wave 4 / Wave 2**: same expanding ratios.
5. Each wave must be **longer** than the previous same-direction wave
   (channels diverge / expand).
6. Bar duration: later waves get higher weights — 0.10 / 0.15 / 0.15 / 0.25 / 0.35.

### Probability coefficients

| Model | Coefficient |
|---|---|
| `DIAGONAL_EXPANDING_INITIAL` | 0.01 |
| `DIAGONAL_EXPANDING_ENDING` | 0.0001 |

---

## 9. ZIGZAG

**Structure:** 3 waves — A · B · C

### Allowed sub-wave models

| Wave | Allowed models |
|---|---|
| A | `IMPULSE`, `DIAGONAL_CONTRACTING_INITIAL`, `DIAGONAL_EXPANDING_INITIAL` |
| B | `ZIGZAG`, `DOUBLE_ZIGZAG`, `TRIPLE_ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING`, `FLAT_REGULAR`, `TRIANGLE_CONTRACTING`, `TRIANGLE_RUNNING`, `TRIANGLE_EXPANDING` |
| C | `IMPULSE`, `DIAGONAL_CONTRACTING_ENDING`, `DIAGONAL_EXPANDING_ENDING` |

### Price rules

1. **C / A** ratio: `ZIGZAG_C_TO_A` (§ 4.6): 0.618–3.618.
2. **B retracement of A**:
   - If B model is **shallow**: `MAP_SHALLOW_CORRECTION` (23.6–50 %), capped at min(C/A, 1).
   - If B model is **deep**: `MAP_DEEP_CORRECTION` (50–95 %), capped at min(C/A, 1).
3. **Alternation rule (A / C)** — implemented as 80 % probability:
   - If A is `IMPULSE` → C is chosen from C models **excluding** `IMPULSE`.
   - If A is a diagonal → C is `IMPULSE`.
   - Remaining 20 %: both waves use the natural model pool freely.

---

## 10. DOUBLE_ZIGZAG

**Structure:** 3 waves — W · X · Y

### Allowed sub-wave models

| Wave | Allowed models |
|---|---|
| W | `ZIGZAG` |
| X | `ZIGZAG`, `DOUBLE_ZIGZAG`, `TRIPLE_ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING`, `FLAT_REGULAR`, `TRIANGLE_CONTRACTING`, `TRIANGLE_RUNNING`, `TRIANGLE_EXPANDING` |
| Y | `ZIGZAG` |

### Price rules

1. **Y / W** ratio: `ZIGZAG_C_TO_A` (§ 4.6): 0.618–3.618.
2. **X retracement of W**:
   - Shallow X: `MAP_SHALLOW_CORRECTION`, capped at min(Y/W, 1).
   - Deep X: `MAP_DEEP_CORRECTION`, capped at min(Y/W, 1).

---

## 11. TRIPLE_ZIGZAG

**Structure:** 5 waves — W · X · Y · XX · Z

**Probability coefficient**: 0.001

### Allowed sub-wave models

| Wave | Allowed models |
|---|---|
| W | `ZIGZAG` |
| X | `ZIGZAG`, `DOUBLE_ZIGZAG`, `TRIPLE_ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING`, `FLAT_REGULAR` |
| Y | `ZIGZAG` |
| XX | `ZIGZAG`, `DOUBLE_ZIGZAG`, `TRIPLE_ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING`, `FLAT_REGULAR`, `TRIANGLE_CONTRACTING`, `TRIANGLE_RUNNING`, `TRIANGLE_EXPANDING` |
| Z | `ZIGZAG` |

### Price rules

1. **Z / W** (total extension): `ZIGZAG_X_Z_TO_W` (§ 4.7): 1.0–4.236.
2. **X / W** retracement: shallow or deep (based on X model), capped at min(Z/W, 1).
3. **XX / Y** retracement: based on X model type.
4. **Y** length must exceed X retracement + a small net progress (≥ 5 % of remaining range).

---

## 12. FLAT_EXTENDED

**Structure:** 3 waves — A · B · C

### Allowed sub-wave models

| Wave | Allowed models |
|---|---|
| A | `ZIGZAG`, `DOUBLE_ZIGZAG` |
| B | `ZIGZAG`, `DOUBLE_ZIGZAG` |
| C | `IMPULSE`, `DIAGONAL_CONTRACTING_ENDING`, `DIAGONAL_EXPANDING_ENDING` |

### Price rules

1. **Wave A** retraces 30–95 % of the total flat range.
2. **Wave B** retraces Wave A completely and then **extends beyond the origin**
   (B > A in absolute terms — the "extended" property).
3. **C / A** ratio: `MAP_EX_FLAT_WAVE_C_TO_A` (§ 4.8): 1.618–3.618.
4. Falls back to `ZIGZAG` generation if the B-limit (beyond origin) is not available
   in the given price bounds.
5. Classified as a **shallow** correction.

---

## 13. FLAT_REGULAR

Same sub-wave models and structure as `FLAT_EXTENDED`.

**Probability coefficient**: 0.0005

### Price rules (differences from FLAT_EXTENDED)

1. **Wave B** retraces 90–100 % of Wave A (nearly reaches the origin, does not exceed it).
2. **C / A** ratio: `MAP_REG_FLAT_WAVE_C_TO_A` (§ 4.9): 1.0–1.618; minimum = B/A ratio.

---

## 14. FLAT_RUNNING

Same sub-wave models as `FLAT_EXTENDED`.

### Price rules

1. **Wave A** exceeds the pattern starting value — Wave A moves further in the
   correction direction than the pattern's end point.
2. **C / A** ratio: `MAP_RUNNING_FLAT_WAVE_C_TO_A` (§ 4.10): 0.5–1.618.
3. Wave A length determined by normal distribution: mean ≈ range + 20 % of the
   available "running" space.
4. Falls back to `ZIGZAG` if running conditions cannot be satisfied.
5. Classified as a **shallow** correction.

---

## 15. TRIANGLE_CONTRACTING

**Structure:** 5 waves — A · B · C · D · E

### Allowed sub-wave models

All five waves: `ZIGZAG`, `DOUBLE_ZIGZAG`

**Special rule:** Wave E has a 1 % probability of being a `TRIANGLE_CONTRACTING`
itself (nested triangle).

### Price rules

1. **Wave A** is longer than the full pattern range — the first leg overshoots,
   then contracts back to the pattern endpoint.
   - A length drawn from approximately `range + 5%…95% of (aLimit − endValue)`.
2. Each subsequent wave is **shorter** than the previous:
   - B / A, C / B, D / C ratios: `MAP_CONTRACTING_TRIANGLE_WAVE_NEXT_TO_PREV` (§ 4.11): 0.5–0.95.
   - Each ratio is constrained by the requirement that subsequent waves converge
     toward the apex (E endpoint).
3. **Running variant** (`TRIANGLE_RUNNING`): Wave B exceeds the starting point
   (goes beyond the origin); see § 16.
4. Classified as a **shallow** correction.

### Duration distribution (base weights)

| Wave | Base weight |
|---|---|
| A | 0.35 |
| B | 0.25 |
| C | 0.15 |
| D | 0.15 |
| E | 0.10 |

Each weight is multiplied by a random factor in [0.7, 1.4].

---

## 16. TRIANGLE_RUNNING

**Probability coefficient**: 0.1

Structurally identical to `TRIANGLE_CONTRACTING`, with one additional constraint:

- **Wave B** must extend **beyond the origin** of the triangle (running property).
- If the available price bounds do not allow the running condition, degrades
  automatically to `TRIANGLE_CONTRACTING`.

---

## 17. TRIANGLE_EXPANDING

**Probability coefficient**: 0.001

Sub-wave models: same as `TRIANGLE_CONTRACTING`.

### Price rules

1. **Wave A** is 20–70 % of the total pattern range.
2. Each subsequent wave is **longer** than the previous:
   - B / A, C / B, D / C ratios: `MAP_EXPANDING_TRIANGLE_WAVE_NEXT_TO_PREV` (§ 4.12): 1.272–3.618.
3. Falls back to `ZIGZAG` if the D-limit (beyond origin) is not satisfied.

### Duration distribution (base weights)

| Wave | Base weight |
|---|---|
| A | 0.10 |
| B | 0.15 |
| C | 0.15 |
| D | 0.25 |
| E | 0.35 |

---

## 18. COMBINATION

**Structure:** 3 waves — W · X · Y

### Allowed sub-wave models

| Wave | Allowed models |
|---|---|
| W | `ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING` |
| X | `ZIGZAG`, `DOUBLE_ZIGZAG`, `TRIPLE_ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING`, `FLAT_REGULAR`, `TRIANGLE_CONTRACTING`, `TRIANGLE_RUNNING`, `TRIANGLE_EXPANDING` |
| Y | `ZIGZAG`, `FLAT_EXTENDED`, `FLAT_RUNNING`, `TRIANGLE_CONTRACTING`, `TRIANGLE_RUNNING` |

### Price rules

1. **Wave W** length: 30–95 % of the available range from the start value.
2. **Wave X** length: 30–95 % of the available range from Wave W.
3. Falls back to `DOUBLE_ZIGZAG` if the required price limits (running condition
   for W and Y) are not satisfiable.
4. Classified as a **shallow** correction.

### Duration weights

- Shallow sub-wave model for W/X/Y: base weight **0.35**
- Deep sub-wave model: base weight **0.20**

---

## 19. Duration (Bar) Distributions

### 3-wave patterns — `SplitByTree`

| Wave | Approximate share |
|---|---|
| First wave | ~15–35 % |
| Middle wave | ~40–60 % |
| Last wave | remainder |

(Exact split: `bars1 = 0.25 ± 0.10`, `bars2 = 0.50 ∓ 0.10`)

### IMPULSE bar weights

Base proportions are assigned per wave then multiplied by a random factor in [0.95, 1.05]:

| Wave | Base if extended or diagonal | Base otherwise |
|---|---|---|
| 1 | 0.20 | 0.10 |
| 2 | 0.20 (if shallow model) | 0.10 |
| 3 | 0.20 (if extended) | 0.10 |
| 4 | `wave2Dur × (0.70..2.50)` — see below | — |
| 5 | 0.20 (if extended or diagonal) | 0.10 |

**Wave 4 / Wave 2 duration ratio** depends on their respective correction types:

| 4th wave type | 2nd wave type | Ratio range |
|---|---|---|
| Same depth class as 2nd | Same | 0.70 – 1.50 |
| Shallow 4th | Deep 2nd | 0.70 – **2.50** |
| Mixed (deep 4th / shallow 2nd) | Mixed | 0.70 – 2.00 |

### Contracting Triangle bar weights

Later waves are progressively shorter in duration:
`0.35 / 0.25 / 0.15 / 0.15 / 0.10` × RandomBigRatio [0.7, 1.4]

### Expanding Triangle / Expanding Diagonal bar weights

Later waves take progressively more time:
`0.10 / 0.15 / 0.15 / 0.25 / 0.35` × RandomBigRatio [0.7, 1.4]

### Contracting Diagonal bar proportions

Wave 1 takes the dominant share, drawn from a normal distribution:
- bars₁ proportion: normal distribution, min 50 %, max 80 %, mean **61.8 %**.
- Remaining bars assigned to waves 2–5 in decreasing proportion.

---

## 20. Shared Generator Behaviour

### Fallback rules

Several models degrade gracefully when price constraints make the pattern impossible:

| Original model | Fallback |
|---|---|
| `FLAT_EXTENDED` | `ZIGZAG` (when B cannot go beyond the origin) |
| `FLAT_RUNNING` | `ZIGZAG` |
| `TRIANGLE_RUNNING` | `TRIANGLE_CONTRACTING` |
| `TRIANGLE_CONTRACTING` | `ZIGZAG` (when A-limit constraint is not satisfiable) |
| `TRIANGLE_EXPANDING` | `ZIGZAG` |
| `COMBINATION` | `DOUBLE_ZIGZAG` |

### Alternation (Zigzag A/C)

In 80 % of generated zigzags, Wave A and Wave C use **different** impulse types:
- A = IMPULSE → C picked from diagonals only
- A = diagonal → C = IMPULSE

### Fibonacci level noise

The generator never places a wave end exactly on a Fibonacci level.
A random noise of up to **5 %** of the current Fibonacci value is added,
capped at the hard limit (`MAIN_ALLOWANCE_MAX_RATIO = 0.05`).

### Recursion limit

Sub-wave decomposition stops at recursion depth **10** (`MAX_DEEP_LEVEL`).
Below a threshold of **100 bars** (`SIMPLE_BARS_THRESHOLD`), a simple random
price path is generated without further structural decomposition.

### Candle construction

- The open of each new candle equals the close of the previous candle.
- The high/low of the boundary candle (first bar of a wave) is set to
  the exact start-value of the wave.
- The close of the last candle of a wave is set to the target end-value.
- A small additional shadow may be added on the first candle if a
  `PrevCandleExtremum` context value is provided.
