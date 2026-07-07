# Plate-rate calibration from Cao et al. 2024 — 2026-07-07

**Status: complete.** Evidence-backed recommended values for FantaSim's
`DefaultAngularDriftPerMegaAnnum` knob, derived from real plate-motion data.

## TL;DR

- **Measured reality** (Cao et al. 2024, 1.8 Ga rotation model, 2,497 non-trivial plate-stages):
  moving plates have a duration-weighted **median |ω| ≈ 0.16°/Ma**, **p90 ≈ 0.99°/Ma**,
  with the Phanerozoic (≤540 Ma, best-constrained) window at **median ≈ 0.20°/Ma**, **p90 ≈ 1.21°/Ma**.
  This matches the textbook "real plates move ~0.1–1°/Ma".
- **Current engine value:** `DefaultAngularDriftPerMegaAnnum = 0.02 rad/Ma = 1.146°/Ma` —
  essentially the **p90** of moving plates. Every plate drifting at the 90th-percentile rate
  is why a 1 Gy sweep wraps the sphere ~3.2×.
- **How many × too fast:** **5.8×** vs the Phanerozoic movers median, **7.1×** vs the all-window
  movers median. ("Roughly an order of magnitude," as reported.)
- **Recommendation:**
  - **Default (median-ish): `0.0035 rad/Ma`** (≈ 0.20°/Ma). ≈ Phanerozoic movers median. A **5.7× reduction** from 0.02.
  - **Lively-but-plausible upper (p90-ish): `0.017 rad/Ma`** (≈ 0.97°/Ma). The all-window movers p90. The current `0.02` is at this level and is a fine *upper bound* — just not a *default*.

The deliverable is the dataset + measurement + recommendation. **Not** the engine change — the
lead applies it.

## Dataset provenance & license

- **Source:** Cao, X., Flament, P., Zahirovic, S., et al. (2024). *Earth's tectonic and plate
  boundary evolution over 1.8 billion years.* Geoscience Frontiers **15(6)** 101922.
  https://doi.org/10.1016/j.gsf.2024.101922
- **Supplementary data:** Zenodo record 13340841 — https://zenodo.org/records/13340841
  (DOI 10.5281/zenodo.13340841).
- **License:** **CC-BY 4.0** (Creative Commons Attribution 4.0 International). Attribution
  required — any shipped derived asset must cite Cao et al. 2024 + the Zenodo record.
- **Archive:** `1.8Ga_model_GSF.zip`, 20,668,049 bytes (20.7 MB). Contains the full GPlates
  project (continental polygons, coastlines, plate boundaries, static polygons, two `.rot`
  files, project file). Only the `.rot` files are extracted for calibration.
- **Rotation files used (both extracted by `fetch-cao2024.sh`):**

  | File | Size (bytes) | Spans | Data rows parsed |
  |---|---:|---|---:|
  | `1.8Ga_model_GSF/1000_0_rotfile.rot` | 625,128 | 0–1000 Ma | 5,925 |
  | `1.8Ga_model_GSF/1800_1000_rotfile.rot` | 36,539 | 1000–1800 Ma | 372 |
  | **total** | | **0–1800 Ma** | **6,297** |

- **Fetch script:** `tools/rates/fetch-cao2024.sh` (idempotent; validates the zip; extracts the
  two `.rot` files into gitignored `tools/rates/data/`). Dataset is gitignored — re-fetch to reproduce.

## Method

**Format.** PLATES4/GPlates `.rot` with six whitespace columns
`MovingPlateId TimeMa PoleLatDeg PoleLonDeg AngleDeg FixedPlateId`, `!` trailing comments,
moving-plate id `999` = disabled row. This is exactly what the engine's
`FantaSim.Geosphere.Plate.Rotation.Import.RotParser` consumes (semantics mirrored 1:1 in
`calibrate-from-rot.py`).

**Stage angular velocity.** For each `(moving, fixed)` group, keyframes are sorted by time.
For each consecutive pair `(t₁, t₂)` the finite rotations `R₁`, `R₂` are converted to unit
quaternions and the **stage rotation** is composed as `R₂ · R₁⁻¹` (Hamilton product). Its
angle θ (folded to `[0, π]` — the physically-meaningful angular displacement) gives the stage
speed `|ω| = θ / |t₂ − t₁|` in deg/Ma. This is true spherical composition — **not** the
incorrect shortcut of subtracting pole angles. (Diagnostic: 0 stages had a raw angle > 180°,
so the `[0, π]` fold affected nothing.)

**Weighting & windows.** All stats are **duration-weighted** (weight = stage length `Δt`);
percentiles use linear interpolation over the cumulative weight. Stages are classified into
the `≤540 Ma` and `>540 Ma` windows by **midpoint** `(t_old + t_young)/2`. "Phanerozoic-ish"
(≤540 Ma) is the best-constrained window.

**Non-trivial subset.** The raw model contains many **anchor / reference-frame plates**
(`000`, `001`, …) whose keyframes are all identity — these define the reference frame, not
real motion, and contribute exact-zero stages. Across all 4,586 stages, **2,089 (45.6%)** are
identity/numerical-dust (≤1e-6°/Ma). The calibration knob sets drift for plates that
*actually move*, so the headline stats are computed on the **2,497 non-trivial stages**
(`|ω| > 1e-6°/Ma`). The all-stages numbers are reported too, for honesty.

**Tooling.** `tools/rates/calibrate-from-rot.py` — Python 3.8+, **stdlib only**, deterministic,
no wall-clock-dependent output.

**Self-test.** `--selftest` parses an embedded synthetic `.rot` (a plate rotating exactly
1°/Ma and another at 2°/Ma, plus a `999` row and a `!` comment) and asserts the pipeline
reproduces 1.000 and 2.000 deg/Ma to **1e-9**. **PASS** — see the verbatim run below.

## Results (duration-weighted, deg/Ma)

### Self-test (verbatim)

```
$ python3 calibrate-from-rot.py --selftest
========================================================================
SELF-TEST: synthetic plate rotating exactly 1.000 deg/Ma (and 2.000 deg/Ma)
========================================================================
  parsed data rows (excl. 999/comments): 5  (expected 5)
  unique moving plates: 2  (expected 2)
  (moving,fixed) groups: 2  (expected 2)
  stages: 3  (expected 3: two for 001, one for 002)
  stage |omega| (deg/Ma):
    plate 001  [0.0-10.0 Ma]  |omega|=1.000000  (expected 1.000)
    plate 001  [10.0-100.0 Ma]  |omega|=1.000000  (expected 1.000)
    plate 002  [0.0-50.0 Ma]  |omega|=2.000000  (expected 2.000)
  PASS: pipeline reproduces 1.000 and 2.000 deg/Ma to 1e-9.
```

### Real model (verbatim)

```
$ python3 calibrate-from-rot.py data/1.8Ga_model_GSF/1000_0_rotfile.rot data/1.8Ga_model_GSF/1800_1000_rotfile.rot
[load] data/1.8Ga_model_GSF/1000_0_rotfile.rot
        5925 data rows
[load] data/1.8Ga_model_GSF/1800_1000_rotfile.rot
        372 data rows

[parsed] 6297 data rows  |  1212 unique moving plates  |  1663 (moving,fixed) groups  |  4586 stages

========================================================================
|omega| distribution (deg/Ma), duration-weighted   [window split at 540 Ma]
========================================================================
-- All stages (includes anchor/reference identity plates) --
  all stages      n= 4586  min= 0.0000  p10= 0.0000  median= 0.0000  mean= 0.0575  p90= 0.0620  p99= 1.1970  max= 34.5627
  <= 540 Ma       n= 3865  min= 0.0000  p10= 0.0000  median= 0.0000  mean= 0.0573  p90= 0.0216  p99= 1.3136  max= 34.5627
  > 540 Ma        n=  721  min= 0.0000  p10= 0.0000  median= 0.0000  mean= 0.0580  p90= 0.1482  p99= 0.9705  max=  6.5318

-- Non-trivial stages (|omega| > 1e-06 deg/Ma; plates that actually move) --
  all (movers)    n= 2497  min= 0.0000  p10= 0.0113  median= 0.1624  mean= 0.3962  p90= 0.9855  p99= 3.1303  max= 34.5627
  <= 540 Ma       n= 2144  min= 0.0000  p10= 0.0176  median= 0.1968  mean= 0.4911  p90= 1.2098  p99= 3.7310  max= 34.5627
  > 540 Ma        n=  353  min= 0.0001  p10= 0.0083  median= 0.1257  mean= 0.2707  p90= 0.7084  p99= 1.6086  max=  6.5318

  diagnostics: stages with raw angle > 180 deg (folded): 0
               identity/anchor stages excluded as non-trivial: 2089 / 4586 (45.6%)

  Top-10 fastest stages (deg/Ma) for audit:
    moving  fixed  t_young    t_old      dt   |omega|  raw_deg
      9006    701    275.0    276.0     1.0   34.5627    34.56
       969    901      5.1      5.9     0.8   21.0763    16.86
     90109    101      0.0     10.0    10.0   16.6359   166.36
       907    301     49.7     51.7     2.0   16.5606    33.12
       969    901      6.6      7.0     0.4   15.5864     6.23
     70844   3202     19.0     20.0     1.0   15.5000    15.50
       825    823      3.0     12.0     9.0   15.0022   135.02
       410    401    250.0    255.0     5.0   14.9749    74.87
      7230    503     65.0     75.0    10.0   12.3860   123.86
      1007    184    490.0    500.0    10.0   11.9232   119.23
```

The fastest stages (e.g. 34.6°/Ma over a 1 Ma interval) are **tiny-Δt transient spikes**
in the model — short intervals with large pole jumps, not sustained drift. They sit in the
p99+ tail and are correctly excluded from the recommendation by anchoring on median/p90.

### Histograms (duration-weighted share)

All-stages (n=4586) — dominated by identity/anchor plates:

```
        deg/Ma       stage-duration-weighted share
  [  0.00,  1.44) |############################################|  99.3%
  [  1.44,  2.88) |                                            |   0.5%
  [  2.88,  4.32) |                                            |   0.1%
  [  4.32,  34.56) | (all bins < 0.1%) |
```

Non-trivial movers, all (n=2497):

```
  [  0.00,  1.44) |##########################################  |  95.2%
  [  1.44,  2.88) |##                                          |   3.6%
  [  2.88,  4.32) |                                            |   0.8%
  [  4.32,  5.76) |                                            |   0.2%
  [  5.76, 34.56) | (all bins < 0.2%) |
```

Non-trivial movers, ≤540 Ma (n=2144, Phanerozoic-ish — the calibration sweet spot):

```
  [  0.00,  1.44) |#########################################   |  92.5%
  [  1.44,  2.88) |##                                          |   5.5%
  [  2.88,  4.32) |#                                           |   1.2%
  [  4.32,  5.76) |                                            |   0.4%
  [  5.76,  7.20) |                                            |   0.2%
  [  7.20, 34.56) | (all bins < 0.2%) |
```

Non-trivial movers, >540 Ma (n=353, Proterozoic — slower and sparser):

```
  [  0.00,  0.27) |###############################             |  69.5%
  [  0.27,  0.54) |######                                      |  14.1%
  [  0.54,  0.82) |####                                        |   9.9%
  [  0.82,  1.09) |#                                           |   3.3%
  [  1.09,  1.36) |#                                           |   1.9%
  [  1.36,  2.18) |                                            |   0.8%
  [  2.18,  6.53) | (all bins ≤ 0.3%) |
```

## Engine knob — current value, units, consumption site

**Name:** `DefaultAngularDriftPerMegaAnnum`
**Location:** `project/plugins/App.World.Composition/OnsetRoster.cs:29` (this repo,
`fantasim-app-godot`).
**Units:** **rad/Ma**. Converted to rad/tick at line 30–31 via
`UnitConverter.RadiansPerMegaAnnumToRadiansPerTick`, which divides by
`UnitConverter.TicksPerMegaAnnum = 100_000` (1 Ma = 100,000 canonical ticks).
**Current value:** `0.02 rad/Ma` → `0.02 / 100_000 = 2.0e-7 rad/tick`.
**Consumption:** the per-tick value feeds `ConvectionFieldConfig.AngularDriftPerTick`
(line 73), and the engine's `ConvectionCenters` rotates upwelling centers by
`tick * AngularDriftPerTick` (`Geosphere.Asthenosphere.Convection/ConvectionCenters.cs:69`).
Plate Euler poles are derived from the one-tick drift of those centers (`OnsetRoster` lines
82–91, 129–140), so the knob **directly scales how fast plate poles drift**. It is also the
fallback pole rate when a center doesn't move (lines 134, 139).

```
// OnsetRoster.cs
private const double DefaultAngularDriftPerMegaAnnum = 0.02;
private static readonly double DefaultAngularDriftPerTick =
    UnitConverter.RadiansPerMegaAnnumToRadiansPerTick(DefaultAngularDriftPerMegaAnnum);
```

### Current value vs measured reality

| Knob (rad/Ma) | Knob (deg/Ma) | vs Phan. movers median (0.1968) | vs all movers median (0.1624) | vs all movers p90 (0.9855) |
|---:|---:|---:|---:|---:|
| 0.02 | 1.1459 | **5.82× too fast** | **7.06× too fast** | 1.16× (≈ p90) |

The current `0.02 rad/Ma` is almost exactly the **p90** of moving plates — i.e. every plate
is assigned the drift speed of the 90th-percentile-fastest real plate. That is the structural
reason the generated motion looks ~an order of magnitude too fast. Over 1 Gy at 1.1459°/Ma a
plate covers 1145.9° ≈ **3.2 full rotations**, matching the reported "wrapping the sphere
several times."

## Recommendation

Conversion: `rad/Ma = deg/Ma × π/180`, with `π/180 = 0.0174533`. Per-tick value =
`rad/Ma ÷ 100_000`.

**Default (median-ish) — `0.0035 rad/Ma`:**
- Target: Phanerozoic movers median = 0.1968°/Ma.
- Arithmetic: `0.1968 × π/180 = 0.003435 rad/Ma` → round up to **`0.0035 rad/Ma`** (0.2005°/Ma).
- Per-tick: `0.0035 / 100_000 = 3.5e-8 rad/tick`.
- vs current: `0.0035 / 0.02 = 0.175×` → a **5.7× reduction**.
- Rationale: the Phanerozoic is the best-constrained window and the era users will mostly
  view; its median moving plate is the natural "typical plate" anchor.

**Lively-but-plausible upper (p90-ish) — `0.017 rad/Ma`:**
- Target: all-window movers p90 = 0.9855°/Ma.
- Arithmetic: `0.9855 × π/180 = 0.017200 rad/Ma` → round to **`0.017 rad/Ma`** (0.9741°/Ma).
- Per-tick: `0.017 / 100_000 = 1.7e-7 rad/tick`.
- Rationale: a real observed fast-plate rate (Pacific/Nazca/Indian-like). The current `0.02`
  is within 18% of this and is a reasonable *upper bound* for a "lively" preset — it is just
  not a reasonable *default*.

**Note for the lead:** applying the default (`0.0035`) requires changing exactly one constant
at `OnsetRoster.cs:29`. If the lively upper should be exposed as a second preset, the
fallback uses at lines 134/139 would also need to take it. No other consumption sites were
found in this repo or the `fantasim-world` engine repo.

## Files added

| File | Purpose |
|---|---|
| `tools/rates/fetch-cao2024.sh` | Idempotent Zenodo 13340841 download + `.rot` extraction. |
| `tools/rates/calibrate-from-rot.py` | PLATES4 parser + quaternion stage-velocity stats + `--selftest`. Stdlib-only. |
| `tools/rates/data/` | Downloaded dataset (gitignored — CC-BY-4.0, re-fetch to reproduce). |
| `.gitignore` (+3 lines) | Ignores `tools/rates/data/`. |
| `tools/rates/2026-07-07-rate-calibration-report.md` | This report. |
| `AGENT-SUMMARY.md` | Lead-facing summary at worktree root. |

## Reproduce

```sh
cd tools/rates
./fetch-cao2024.sh                                       # ~20 MB download into data/
python3 calibrate-from-rot.py --selftest                 # PASS to 1e-9
python3 calibrate-from-rot.py data/1.8Ga_model_GSF/1000_0_rotfile.rot \
                                  data/1.8Ga_model_GSF/1800_1000_rotfile.rot
```
