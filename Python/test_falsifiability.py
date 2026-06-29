"""test_falsifiability.py

Falsifiability test suite for the harmonic-classification pipeline.

Runs WITHOUT the Planck FITS file -- all tests use synthetic or analytic inputs.
A FAIL is a genuine falsification of a pipeline assumption.

Tests
-----
1. PARTITION                  -- classify_alm is a lossless partition of full_alm
2. HEEGNER_ISOLATION          -- mode_class boundaries are correct for key ells
3. PARITY_ASYMMETRY_DIRECTION -- parity_asymmetry sign is recoverable
4. CURVATURE_SIGN             -- K_competition_map has nonzero variance (non-degenerate)

Usage
-----
    cd Python/
    python test_falsifiability.py

Exit code 0 = all pass.  Exit code 1 = at least one failure.
"""

from __future__ import annotations

import sys
import traceback

import healpy as hp
import numpy as np

from preprocess_cmb_harmonics import (
    HEEGNER_NUMBERS,
    alm_zero_like,
    classify_alm,
    compute_field_scalars,
    compute_sigma_and_curvature,
    is_prime,
    mode_class,
    reconstruct_map,
)

LMAX = 64
NSIDE = 64
SEED = 42
PASSES: list[str] = []
FAILS: list[str] = []


def run(name: str, fn) -> None:
    try:
        result, reason = fn()
        if result:
            PASSES.append(f"  PASS  {name}: {reason}")
        else:
            FAILS.append(f"  FAIL  {name}: {reason}")
    except Exception:
        FAILS.append(f"  FAIL  {name}: raised exception\n{traceback.format_exc()}")


# ---------------------------------------------------------------------------
# Test 1 -- PARTITION
# classify_alm must be a lossless, non-overlapping partition of full_alm.
# Failure mode: a mode bucketed to two classes, or dropped entirely.
# ---------------------------------------------------------------------------
def test_partition():
    rng = np.random.default_rng(SEED)
    size = hp.Alm.getsize(LMAX)
    full_alm = rng.standard_normal(size) + 1j * rng.standard_normal(size)

    classified = classify_alm(full_alm, LMAX)
    reconstructed = sum(classified.values())

    max_residual = float(np.max(np.abs(reconstructed - full_alm)))
    if max_residual < 1e-12:
        return True, f"max residual = {max_residual:.2e} (lossless)"
    return False, f"max residual = {max_residual:.2e} -- partition is NOT lossless"


# ---------------------------------------------------------------------------
# Test 2 -- HEEGNER_ISOLATION
# Spot-check specific ells for correct bucket assignment.
# ell=163  -> heegner           (largest Heegner number in set)
# ell=167  -> void_prime        (prime, odd, not Heegner)
# ell=4    -> soliton           (even, not prime, not Heegner)
# ell=9    -> soliton_composite (odd, not prime, not Heegner)
# ell=2    -> heegner           (in HEEGNER_NUMBERS)
# ell=11   -> heegner           (in HEEGNER_NUMBERS)
# ---------------------------------------------------------------------------
def test_heegner_isolation():
    cases = [
        (163, "heegner"),
        (167, "void_prime"),
        (4,   "soliton"),
        (9,   "soliton_composite"),
        (2,   "heegner"),
        (11,  "heegner"),
    ]
    errors = []
    for ell, expected in cases:
        got = mode_class(ell)
        if got != expected:
            errors.append(f"ell={ell}: expected {expected}, got {got}")
    if not errors:
        return True, f"all {len(cases)} spot-checks correct"
    return False, "; ".join(errors)


# ---------------------------------------------------------------------------
# Test 3 -- PARITY_ASYMMETRY_DIRECTION
# Build a synthetic alm where odd-ell modes have 4x the power of even-ell modes.
# parity_asymmetry must come out > 1.0.
# Flip (even 4x odd) and verify parity_asymmetry < 1.0.
# Failure mode: sign inversion or normalisation error in compute_field_scalars.
# ---------------------------------------------------------------------------
def test_parity_asymmetry_direction():
    rng = np.random.default_rng(SEED + 1)
    size = hp.Alm.getsize(LMAX)

    def make_alm(odd_scale: float, even_scale: float) -> np.ndarray:
        base = rng.standard_normal(size) + 1j * rng.standard_normal(size)
        alm = np.zeros(size, dtype=np.complex128)
        for ell in range(LMAX + 1):
            scale = odd_scale if ell % 2 == 1 else even_scale
            for m in range(ell + 1):
                idx = hp.Alm.getidx(LMAX, ell, m)
                alm[idx] = base[idx] * scale
        return alm

    alm_a = make_alm(odd_scale=2.0, even_scale=0.5)
    classified_a = classify_alm(alm_a, LMAX)
    pa_a = compute_field_scalars(classified_a, LMAX)["parity_asymmetry"]

    alm_b = make_alm(odd_scale=0.5, even_scale=2.0)
    classified_b = classify_alm(alm_b, LMAX)
    pa_b = compute_field_scalars(classified_b, LMAX)["parity_asymmetry"]

    if pa_a > 1.0 and pa_b < 1.0:
        return True, f"odd-dominant PA={pa_a:.4f}>1, even-dominant PA={pa_b:.4f}<1"
    return False, f"parity_asymmetry sign wrong: odd-dominant={pa_a:.4f}, even-dominant={pa_b:.4f}"


# ---------------------------------------------------------------------------
# Test 4 -- CURVATURE_SIGN
# K_competition_map = K_full - K_heegner must have nonzero variance.
# A flat competition map would mean Heegner modes == full field (degenerate).
# Also verifies K_heegner != K_full (Heegner is a strict subset).
# ---------------------------------------------------------------------------
def test_curvature_sign():
    rng = np.random.default_rng(SEED + 2)
    size = hp.Alm.getsize(LMAX)
    full_alm = rng.standard_normal(size) + 1j * rng.standard_normal(size)
    full_alm[0] = 2000.0 + 0j  # set monopole so t0 is well-defined

    classified = classify_alm(full_alm, LMAX)
    full_map = reconstruct_map(full_alm, NSIDE, LMAX)
    heegner_map = reconstruct_map(classified["heegner"], NSIDE, LMAX)

    t0 = float(np.mean(full_map))
    if abs(t0) < 1e-6:
        return False, f"t0={t0:.3e} -- synthetic map mean too close to zero"

    _, _, k_full = compute_sigma_and_curvature(full_map, t0, LMAX)
    _, _, k_heegner = compute_sigma_and_curvature(heegner_map, t0, LMAX)
    k_competition = k_full - k_heegner

    std = float(np.std(k_competition))
    max_diff = float(np.max(np.abs(k_full - k_heegner)))

    if std > 1e-6 and max_diff > 1e-6:
        return True, f"K_competition std={std:.4e}, max_diff={max_diff:.4e}"
    return False, f"K_competition degenerate: std={std:.4e}, max_diff={max_diff:.4e}"


# ---------------------------------------------------------------------------
# Runner
# ---------------------------------------------------------------------------
if __name__ == "__main__":
    print("=" * 60)
    print("  Falsifiability Test Suite -- infinite-improbability-drive")
    print(f"  lmax={LMAX}  nside={NSIDE}  seed={SEED}")
    print("=" * 60)

    run("PARTITION",                  test_partition)
    run("HEEGNER_ISOLATION",          test_heegner_isolation)
    run("PARITY_ASYMMETRY_DIRECTION", test_parity_asymmetry_direction)
    run("CURVATURE_SIGN",             test_curvature_sign)

    print()
    for msg in PASSES:
        print(msg)
    for msg in FAILS:
        print(msg)

    print()
    total = len(PASSES) + len(FAILS)
    print(f"  {len(PASSES)}/{total} passed")

    if FAILS:
        print("  A FAIL is a genuine falsification -- investigate before merging.")
        sys.exit(1)
    else:
        print("  All assertions hold. Pipeline assumptions are internally consistent.")
        sys.exit(0)
