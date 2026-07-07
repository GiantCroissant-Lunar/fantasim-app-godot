#!/usr/bin/env python3
"""calibrate-from-rot.py — measure plate angular velocities from a PLATES4 .rot model.

Parses one or more GPlates/PLATES4 ``.rot`` files (the same format the engine's
``RotParser`` consumes), composes consecutive finite rotations as quaternions to get
true stage rotation angles (NOT pole-angle subtraction), and reports the distribution
of per-stage angular speed |omega| in deg/Ma across all (moving-plate, fixed-plate)
groups. Stats are duration-weighted and split into 0-540 Ma and >540 Ma windows.

Self-test: ``--selftest`` parses an embedded synthetic model (a plate rotating exactly
1 deg/Ma) and asserts the pipeline reproduces 1.000 deg/Ma.

Stdlib only.  Python 3.8+.
"""
from __future__ import annotations

import argparse
import math
import statistics
import sys
from dataclasses import dataclass
from typing import List, Tuple

# ---------------------------------------------------------------------- quaternion
# Quaternions stored as (w, x, y, z); unit magnitude. Hamilton product convention.
# A finite rotation (pole unit axis n, angle theta) maps to
#     q = (cos(theta/2), sin(theta/2) * n)
# which is exactly the convention used to compose rotations on the sphere.


def quat_from_euler_pole(lat_deg: float, lon_deg: float, angle_deg: float) -> Tuple[float, float, float, float]:
    """Build a unit quaternion from a GPlates Euler pole (lat, lon) and a rotation angle.

    The pole is interpreted as a geographic unit axis (lat in [-90,90], lon in degrees),
    converted to Cartesian (x,y,z) on the unit sphere. Angle may be any real number of
    degrees (negative / >360 are wrapped by sin/cos automatically).
    """
    lat = math.radians(lat_deg)
    lon = math.radians(lon_deg)
    theta = math.radians(angle_deg)
    # Geographic -> Cartesian unit vector. lon=0 at the Greenwich meridian.
    cx = math.cos(lat) * math.cos(lon)
    cy = math.cos(lat) * math.sin(lon)
    cz = math.sin(lat)
    half = 0.5 * theta
    s = math.sin(half)
    return (math.cos(half), s * cx, s * cy, s * cz)


def quat_conj(q: Tuple[float, float, float, float]) -> Tuple[float, float, float, float]:
    w, x, y, z = q
    return (w, -x, -y, -z)


def quat_mul(a: Tuple[float, float, float, float], b: Tuple[float, float, float, float]) -> Tuple[float, float, float, float]:
    aw, ax, ay, az = a
    bw, bx, by, bz = b
    return (
        aw * bw - ax * bx - ay * by - az * bz,
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
    )


def quat_normalize(q: Tuple[float, float, float, float]) -> Tuple[float, float, float, float]:
    w, x, y, z = q
    n = math.sqrt(w * w + x * x + y * y + z * z)
    if n == 0.0:
        return (1.0, 0.0, 0.0, 0.0)
    return (w / n, x / n, y / n, z / n)


def quat_rotation_angle_rad(q: Tuple[float, float, float, float]) -> Tuple[float, float]:
    """Return (shortest_angle_rad, raw_angle_rad) for the rotation a unit quaternion encodes.

    shortest_angle_rad is the magnitude of the rotation folded to [0, pi] -- the physically
    meaningful angular displacement. raw_angle_rad is 2*atan2(|v|, w) in [0, 2*pi) before
    folding; we return it so callers can count stages whose true rotation exceeded pi.
    """
    w, x, y, z = quat_normalize(q)
    vnorm = math.sqrt(x * x + y * y + z * z)
    raw = 2.0 * math.atan2(vnorm, w)  # [0, 2*pi)
    shortest = raw if raw <= math.pi else (2.0 * math.pi - raw)
    return shortest, raw


def stage_angle_deg(r1: Tuple[float, float, float, float], r2: Tuple[float, float, float, float]) -> Tuple[float, float]:
    """Relative rotation R2 * R1^-1, returns (shortest_angle_deg, raw_angle_deg)."""
    rel = quat_mul(r2, quat_conj(r1))
    shortest, raw = quat_rotation_angle_rad(rel)
    return math.degrees(shortest), math.degrees(raw)


# ---------------------------------------------------------------------- .rot parser
@dataclass
class RotRow:
    moving: str
    fixed: str
    time_ma: float
    pole_lat: float
    pole_lon: float
    angle_deg: float


def parse_rot(path: str) -> Tuple[List[RotRow], int]:
    """Parse a PLATES4 .rot file. Returns (rows, data_line_count).

    Mirrors the engine RotParser semantics:
      - 6 whitespace columns: Moving TimeMa PoleLat PoleLon Angle Fixed
      - '!' starts a trailing comment (stripped before column check)
      - '#' whole-line comments and blank lines skipped
      - moving plate id '999' = disabled row, skipped silently
      - non-6-column data rows are reported to stderr and skipped
    """
    rows: List[RotRow] = []
    data_lines = 0
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            stripped = line.strip()
            if not stripped or stripped.startswith("#"):
                continue
            bang = line.find("!")
            if bang >= 0:
                line = line[:bang]
            cols = line.split()
            if not cols:
                continue
            if len(cols) != 6:
                print(f"  [parse] {path}: skipping malformed row ({len(cols)} cols): {stripped[:70]!r}", file=sys.stderr)
                continue
            moving, t_str, lat_str, lon_str, ang_str, fixed = cols
            if moving == "999":
                continue
            try:
                t_ma = float(t_str)
                lat = float(lat_str)
                lon = float(lon_str)
                ang = float(ang_str)
            except ValueError:
                print(f"  [parse] {path}: skipping non-numeric row: {stripped[:70]!r}", file=sys.stderr)
                continue
            if not (math.isfinite(t_ma) and math.isfinite(lat) and math.isfinite(lon) and math.isfinite(ang)):
                continue
            data_lines += 1
            rows.append(RotRow(moving, fixed, t_ma, lat, lon, ang))
    return rows, data_lines


# ---------------------------------------------------------------------- stages
@dataclass
class Stage:
    moving: str
    fixed: str
    t_old: float       # older (larger Ma) endpoint of the interval
    t_young: float     # younger (smaller Ma) endpoint
    duration_ma: float  # t_old - t_young, > 0
    speed_deg_per_ma: float  # |omega|, shortest-angle / duration
    raw_speed_deg_per_ma: float  # raw-angle / duration (>= shortest); for diagnostics
    midpoint_ma: float  # (t_old + t_young) / 2 -- used for time-window binning


def compute_stages(rows: List[RotRow]) -> Tuple[List[Stage], int, int]:
    """Group by (moving, fixed); within each group sort by time and compose consecutive
    finite rotations. Returns (stages, n_moving_fixed_groups, n_plates_unique_moving)."""
    groups: dict = {}
    for r in rows:
        key = (r.moving, r.fixed)
        groups.setdefault(key, []).append(r)

    stages: List[Stage] = []
    for (moving, fixed), kf in groups.items():
        kf.sort(key=lambda rr: rr.time_ma)
        # Drop duplicate-time keyframes within a group (keep first) -- they yield div-by-zero.
        deduped: List[RotRow] = []
        seen_t = set()
        for rr in kf:
            if rr.time_ma in seen_t:
                continue
            seen_t.add(rr.time_ma)
            deduped.append(rr)
        for i in range(len(deduped) - 1):
            a = deduped[i]
            b = deduped[i + 1]
            dt = b.time_ma - a.time_ma
            if dt <= 0.0:
                continue  # defensive; sorting guarantees dt >= 0, dedupe guarantees > 0
            qa = quat_from_euler_pole(a.pole_lat, a.pole_lon, a.angle_deg)
            qb = quat_from_euler_pole(b.pole_lat, b.pole_lon, b.angle_deg)
            shortest_deg, raw_deg = stage_angle_deg(qa, qb)
            stages.append(Stage(
                moving=moving, fixed=fixed,
                t_old=max(a.time_ma, b.time_ma), t_young=min(a.time_ma, b.time_ma),
                duration_ma=dt,
                speed_deg_per_ma=shortest_deg / dt,
                raw_speed_deg_per_ma=raw_deg / dt,
                midpoint_ma=(a.time_ma + b.time_ma) / 2.0,
            ))
    n_plates = len({r.moving for r in rows})
    return stages, len(groups), n_plates


# ---------------------------------------------------------------------- weighted stats
def _weighted_percentile(values: List[float], weights: List[float], pct: float) -> float:
    """Linear-interpolated weighted percentile (pct in [0,100])."""
    if not values:
        return float("nan")
    order = sorted(range(len(values)), key=lambda i: values[i])
    vs = [values[i] for i in order]
    ws = [weights[i] for i in order]
    total = sum(ws)
    target = (pct / 100.0) * total
    cum = 0.0
    for i in range(len(vs)):
        next_cum = cum + ws[i]
        if next_cum >= target:
            # Interpolate within this step (handles weight>0 edge at the very end).
            if ws[i] <= 0.0 or i == 0:
                return vs[i]
            frac = (target - cum) / ws[i]
            frac = max(0.0, min(1.0, frac))
            return vs[i - 1] + frac * (vs[i] - vs[i - 1])
        cum = next_cum
    return vs[-1]


def weighted_stats(values: List[float], weights: List[float]) -> dict:
    """Duration-weighted summary of a list of per-stage |omega| values."""
    n = len(values)
    if n == 0:
        return {"n": 0}
    wsum = sum(weights)
    wmean = sum(v * w for v, w in zip(values, weights)) / wsum if wsum > 0 else statistics.mean(values)
    return {
        "n": n,
        "min": min(values),
        "p10": _weighted_percentile(values, weights, 10.0),
        "median": _weighted_percentile(values, weights, 50.0),
        "mean": wmean,
        "p90": _weighted_percentile(values, weights, 90.0),
        "p99": _weighted_percentile(values, weights, 99.0),
        "max": max(values),
    }


def fmt_stats(stats: dict, label: str) -> str:
    if stats.get("n", 0) == 0:
        return f"  {label:14s}  (no stages)"
    return (
        f"  {label:14s}  n={stats['n']:>5d}  "
        f"min={stats['min']:7.4f}  p10={stats['p10']:7.4f}  "
        f"median={stats['median']:7.4f}  mean={stats['mean']:7.4f}  "
        f"p90={stats['p90']:7.4f}  p99={stats['p99']:7.4f}  max={stats['max']:8.4f}"
    )


def ascii_histogram(values: List[float], weights: List[float], title: str, bins: int = 24) -> str:
    """Duration-weighted ASCII histogram. Returns the rendered text block."""
    if not values:
        return f"{title}: (no values)"
    lo = 0.0
    hi = max(values)
    if hi <= lo:
        return f"{title}: all zero (max={hi:.4f})"
    # Use a slightly padded upper edge; a few out-of-range values clamp to the top bin.
    edges = [lo + (hi - lo) * i / bins for i in range(bins + 1)]
    bin_w = [0.0] * bins
    for v, w in zip(values, weights):
        idx = int((v - lo) / (hi - lo) * bins)
        if idx >= bins:
            idx = bins - 1
        if idx < 0:
            idx = 0
        bin_w[idx] += w
    total_w = sum(bin_w)
    bar_w = 44
    lines = [title, "        deg/Ma       stage-duration-weighted share"]
    for i in range(bins):
        lo_e = edges[i]
        hi_e = edges[i + 1]
        share = bin_w[i] / total_w if total_w > 0 else 0.0
        nb = int(round(share * bar_w))
        lines.append(f"  [{lo_e:6.2f},{hi_e:6.2f}) |{'#' * nb}{' ' * (bar_w - nb)}| {share * 100:5.1f}%")
    return "\n".join(lines)


# ---------------------------------------------------------------------- selftest
SELFTEST_ROT = """\
# Synthetic PLATES4 rotation file for calibrate-from-rot.py self-test.
# Plate 001 rotates about a fixed Euler pole at exactly 1 deg/Ma:
# at time t Ma (going into the past), the finite rotation angle = t degrees.
# Plate 002 rotates at 2 deg/Ma about a different pole (variety).
# 999 disabled row and a ! trailing comment exercise parser paths.
001    0.0   30.0   45.0     0.0 000 ! present
001   10.0   30.0   45.0    10.0 000 ! 1 deg/Ma over 0-10 Ma
001  100.0   30.0   45.0   100.0 000 ! 1 deg/Ma over 10-100 Ma
002    0.0  -20.0  120.0     0.0 000
002   50.0  -20.0  120.0   100.0 000 ! 2 deg/Ma over 0-50 Ma
999  999.0    0.0    0.0    99.0 000 ! disabled, must be skipped
"""


def run_selftest() -> int:
    import tempfile, os
    print("=" * 72)
    print("SELF-TEST: synthetic plate rotating exactly 1.000 deg/Ma (and 2.000 deg/Ma)")
    print("=" * 72)
    with tempfile.NamedTemporaryFile("w", suffix=".rot", delete=False) as tf:
        tf.write(SELFTEST_ROT)
        tmp_path = tf.name
    try:
        rows, data_lines = parse_rot(tmp_path)
        stages, n_groups, n_plates = compute_stages(rows)
        print(f"  parsed data rows (excl. 999/comments): {data_lines}  (expected 5)")
        print(f"  unique moving plates: {n_plates}  (expected 2)")
        print(f"  (moving,fixed) groups: {n_groups}  (expected 2)")
        print(f"  stages: {len(stages)}  (expected 3: two for 001, one for 002)")
        print("  stage |omega| (deg/Ma):")
        expected = []
        for s in stages:
            exp = 1.0 if s.moving == "001" else 2.0
            expected.append((s.moving, s.speed_deg_per_ma, exp))
            print(f"    plate {s.moving}  [{s.t_young:.1f}-{s.t_old:.1f} Ma]  "
                  f"|omega|={s.speed_deg_per_ma:.6f}  (expected {exp:.3f})")
        ok = True
        for moving, got, exp in expected:
            if abs(got - exp) > 1e-9:
                print(f"  FAIL: plate {moving} got {got}, expected {exp}")
                ok = False
        if data_lines != 5 or n_plates != 2 or n_groups != 2 or len(stages) != 3:
            print("  FAIL: counts mismatch (see expected above).")
            ok = False
        if ok:
            print("  PASS: pipeline reproduces 1.000 and 2.000 deg/Ma to 1e-9.")
            return 0
        print("  FAIL: see above.")
        return 1
    finally:
        os.unlink(tmp_path)


# ---------------------------------------------------------------------- main
def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(description="Calibrate plate angular drift from a PLATES4 .rot model.")
    ap.add_argument("rotfiles", nargs="*", help="One or more .rot files to analyse.")
    ap.add_argument("--selftest", action="store_true", help="Run the embedded synthetic self-test and exit.")
    ap.add_argument("--phanerozoic", type=float, default=540.0,
                    help="Boundary (Ma) between the 'Phanerozoic-ish' and 'older' windows. Default 540.")
    args = ap.parse_args(argv)

    if args.selftest:
        return run_selftest()

    if not args.rotfiles:
        ap.error("provide at least one .rot file, or --selftest")

    all_rows: List[RotRow] = []
    total_data_lines = 0
    for path in args.rotfiles:
        print(f"[load] {path}")
        rows, dl = parse_rot(path)
        all_rows.extend(rows)
        total_data_lines += dl
        print(f"        {dl} data rows")

    stages, n_groups, n_plates = compute_stages(all_rows)
    print(f"\n[parsed] {total_data_lines} data rows  |  {n_plates} unique moving plates  "
          f"|  {n_groups} (moving,fixed) groups  |  {len(stages)} stages")

    if not stages:
        print("No stages computed; nothing to report.")
        return 1

    # Duration-weighted windows.
    phan = args.phanerozoic
    speeds = [s.speed_deg_per_ma for s in stages]
    durs = [s.duration_ma for s in stages]
    phan_stages = [s for s in stages if s.midpoint_ma <= phan]
    old_stages = [s for s in stages if s.midpoint_ma > phan]

    # The knob calibrates drift for plates that actually move, so exclude identity /
    # anchor stages (reference-frame plates 000/001/... with all-identity keyframes).
    # 1e-6 deg/Ma is far below any physical signal; it rejects only true zeros + dust.
    EPS = 1e-6
    nt_stages = [s for s in stages if s.speed_deg_per_ma > EPS]

    def win(subset):
        return weighted_stats([s.speed_deg_per_ma for s in subset], [s.duration_ma for s in subset])

    all_s = weighted_stats(speeds, durs)
    phan_s = win(phan_stages)
    old_s = win(old_stages)
    nt_all = win(nt_stages)
    nt_phan = win([s for s in nt_stages if s.midpoint_ma <= phan])
    nt_old = win([s for s in nt_stages if s.midpoint_ma > phan])

    # Diagnostic: stages whose raw rotation angle exceeded pi (folded into [0,pi]).
    n_raw_gt_pi = sum(1 for s in stages if s.raw_speed_deg_per_ma > 180.0 + 1e-9)
    n_zero = sum(1 for s in stages if s.speed_deg_per_ma <= EPS)

    print("\n" + "=" * 72)
    print(f"|omega| distribution (deg/Ma), duration-weighted   [window split at {phan:.0f} Ma]")
    print("=" * 72)
    print("-- All stages (includes anchor/reference identity plates) --")
    print(fmt_stats(all_s, "all stages"))
    print(fmt_stats(phan_s, f"<= {phan:.0f} Ma"))
    print(fmt_stats(old_s, f"> {phan:.0f} Ma"))
    print(f"\n-- Non-trivial stages (|omega| > {EPS:.0e} deg/Ma; plates that actually move) --")
    print(fmt_stats(nt_all, "all (movers)"))
    print(fmt_stats(nt_phan, f"<= {phan:.0f} Ma"))
    print(fmt_stats(nt_old, f"> {phan:.0f} Ma"))
    print(f"\n  diagnostics: stages with raw angle > 180 deg (folded): {n_raw_gt_pi}")
    print(f"               identity/anchor stages excluded as non-trivial: {n_zero} / {len(stages)} "
          f"({100.0 * n_zero / len(stages):.1f}%)")

    top = sorted(stages, key=lambda s: s.speed_deg_per_ma, reverse=True)[:10]
    print("\n  Top-10 fastest stages (deg/Ma) for audit:")
    print(f"    {'moving':>6} {'fixed':>6} {'t_young':>8} {'t_old':>8} {'dt':>7} {'|omega|':>9}  raw_deg")
    for s in top:
        print(f"    {s.moving:>6} {s.fixed:>6} {s.t_young:8.1f} {s.t_old:8.1f} {s.duration_ma:7.1f} "
              f"{s.speed_deg_per_ma:9.4f}  {s.raw_speed_deg_per_ma * s.duration_ma:7.2f}")

    print("\n" + ascii_histogram(speeds, durs, f"All stages |omega| histogram (n={len(stages)})"))
    print()
    print(ascii_histogram([s.speed_deg_per_ma for s in nt_stages],
                          [s.duration_ma for s in nt_stages],
                          f"Non-trivial |omega| histogram (n={len(nt_stages)})"))
    print()
    print(ascii_histogram([s.speed_deg_per_ma for s in nt_stages if s.midpoint_ma <= phan],
                          [s.duration_ma for s in nt_stages if s.midpoint_ma <= phan],
                          f"Non-trivial <= {phan:.0f} Ma |omega| histogram "
                          f"(n={sum(1 for s in nt_stages if s.midpoint_ma <= phan)})"))
    print()
    print(ascii_histogram([s.speed_deg_per_ma for s in nt_stages if s.midpoint_ma > phan],
                          [s.duration_ma for s in nt_stages if s.midpoint_ma > phan],
                          f"Non-trivial > {phan:.0f} Ma |omega| histogram "
                          f"(n={sum(1 for s in nt_stages if s.midpoint_ma > phan)})"))

    # Engine-knob bridge: print rad/Ma for headline percentiles so the report can cite them.
    print("\n" + "-" * 72)
    print("Knob conversion (deg/Ma -> rad/Ma; 1 deg/Ma = pi/180 = 0.0174533 rad/Ma):")
    print("  Recommended calibration source = non-trivial (movers) all-window stats.")
    for label, v in (("median", nt_all["median"]), ("p90", nt_all["p90"]),
                     ("mean", nt_all["mean"]), ("p99", nt_all["p99"])):
        if isinstance(v, float) and math.isfinite(v):
            print(f"  movers {label:7s}: {v:8.4f} deg/Ma = {math.radians(v):.6f} rad/Ma")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
