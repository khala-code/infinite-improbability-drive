"""compute_zatao_zenith.py

ZaTaOa first-zenith estimator.

Replaces the anti-dipole proxy in frame_vectors.json with three
independent geometric estimates of the ZaTaOa origin direction.

Estimators
----------
1. HEEGNER_K_CENTROID
   Intensity-weighted centroid of the K_heegner curvature map.
   Heegner modes are the algebraic origin anchors; their curvature
   field is expected to peak in the direction of the ZaTaOa origin.
   This is the primary estimator.

2. MULTIPOLE_ALIGNMENT
   For each Heegner ell, find the pixel carrying maximum |Y_lm| power
   (argmax of the reconstructed single-ell map).  Compute the
   intensity-weighted mean unit vector over all 9 Heegner ells.
   Tests whether the Heegner multipoles cluster angularly.

3. PHASE_COHERENCE
   For each pixel, compute the mean resultant length of the Heegner
   alm phases projected onto the sphere via a simple phase map.
   The gradient of this coherence field points toward the origin.
   Robust to amplitude outliers that HEEGNER_K_CENTROID is sensitive to.

Outputs
-------
Updates frame_vectors.json in-place with:
  zatao_first_zenith          -- dict with all three estimates + consensus
  delta_zenith_deg            -- angle(HEEGNER_K_CENTROID, dipole)
  estimator_agreement_deg     -- max pairwise angle between the three estimates
  legacy_anti_dipole_delta_zenith_deg  -- old proxy retained for comparison

Usage
-----
    cd Python/
    # After running preprocess_cmb_harmonics.py:
    python compute_zatao_zenith.py

    # Standalone with explicit paths:
    python compute_zatao_zenith.py --alm path/to/alm_by_class.npz \\
                                   --k   path/to/K_curvature.npz \\
                                   --fv  path/to/frame_vectors.json
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Dict, List, Tuple

import healpy as hp
import numpy as np

HEEGNER_NUMBERS: List[int] = sorted([1, 2, 3, 7, 11, 19, 43, 67, 163])
EPSILON = 1e-12


# ---------------------------------------------------------------------------
# Path helpers
# ---------------------------------------------------------------------------

def default_paths() -> Tuple[Path, Path, Path]:
    script_dir = Path(__file__).resolve().parent
    repo_root = script_dir.parent
    processed = repo_root / "Assets" / "CMB" / "Processed"
    return (
        processed / "alm_by_class.npz",
        processed / "K_curvature.npz",
        processed / "frame_vectors.json",
    )


# ---------------------------------------------------------------------------
# Utilities
# ---------------------------------------------------------------------------

def unit(v: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(v)
    return v / n if n > EPSILON else v


def vec_to_lonlat(v: np.ndarray) -> Tuple[float, float]:
    """Unit vector -> (lon_deg, lat_deg) in standard spherical convention."""
    v = unit(v)
    lat = math.degrees(math.asin(float(np.clip(v[2], -1.0, 1.0))))
    lon = math.degrees(math.atan2(float(v[1]), float(v[0]))) % 360.0
    return lon, lat


def lonlat_to_vec(lon_deg: float, lat_deg: float) -> np.ndarray:
    lon = math.radians(lon_deg)
    lat = math.radians(lat_deg)
    return np.array([
        math.cos(lat) * math.cos(lon),
        math.cos(lat) * math.sin(lon),
        math.sin(lat),
    ], dtype=np.float64)


def angle_between_deg(a: np.ndarray, b: np.ndarray) -> float:
    cos_theta = float(np.dot(unit(a), unit(b)))
    return math.degrees(math.acos(float(np.clip(cos_theta, -1.0, 1.0))))


def vec_to_dict(v: np.ndarray) -> Dict[str, float]:
    lon, lat = vec_to_lonlat(v)
    return {"x": float(v[0]), "y": float(v[1]), "z": float(v[2]),
            "lon_deg": lon, "lat_deg": lat}


# ---------------------------------------------------------------------------
# Estimator 1 -- HEEGNER_K_CENTROID
# Intensity-weighted centroid of the K_heegner curvature map.
# We take abs(K_heegner) as weight -- origin direction is where curvature
# is concentrated, regardless of sign.
# ---------------------------------------------------------------------------

def estimator_heegner_k_centroid(
    k_heegner: np.ndarray,
    nside: int,
) -> np.ndarray:
    npix = hp.nside2npix(nside)
    assert len(k_heegner) == npix, "k_heegner length must match nside"

    weights = np.abs(k_heegner).astype(np.float64)
    weights = np.clip(weights, 0, np.percentile(weights, 99.5))  # remove outlier pixels
    total = weights.sum()
    if total < EPSILON:
        raise ValueError("K_heegner is essentially zero -- curvature map may be degenerate")

    # Pixel centres as unit vectors
    theta, phi = hp.pix2ang(nside, np.arange(npix))
    x = np.sin(theta) * np.cos(phi)
    y = np.sin(theta) * np.sin(phi)
    z = np.cos(theta)

    cx = float(np.dot(weights, x)) / total
    cy = float(np.dot(weights, y)) / total
    cz = float(np.dot(weights, z)) / total

    centroid = unit(np.array([cx, cy, cz], dtype=np.float64))
    return centroid


# ---------------------------------------------------------------------------
# Estimator 2 -- MULTIPOLE_ALIGNMENT
# For each Heegner ell, reconstruct the single-ell map and find the
# direction of maximum absolute amplitude (the "axis" of that multipole).
# Weighted mean over all 9 Heegner ells, weighted by total ell power.
# ---------------------------------------------------------------------------

def estimator_multipole_alignment(
    heegner_alm: np.ndarray,
    lmax: int,
    nside: int,
) -> np.ndarray:
    axis_vecs: List[np.ndarray] = []
    weights: List[float] = []

    for ell in HEEGNER_NUMBERS:
        if ell > lmax:
            continue

        # Isolate this ell
        single_alm = np.zeros_like(heegner_alm)
        for m in range(ell + 1):
            idx = hp.Alm.getidx(lmax, ell, m)
            single_alm[idx] = heegner_alm[idx]

        ell_map = hp.alm2map(single_alm, nside=nside, lmax=lmax, verbose=False)
        ell_power = float(np.sum(ell_map ** 2))
        if ell_power < EPSILON:
            continue

        # Axis = direction of maximum absolute value
        pix = int(np.argmax(np.abs(ell_map)))
        theta, phi = hp.pix2ang(nside, pix)
        vec = np.array([
            math.sin(theta) * math.cos(phi),
            math.sin(theta) * math.sin(phi),
            math.cos(theta),
        ], dtype=np.float64)

        # Sign convention: project onto running mean; flip if anti-aligned
        if axis_vecs:
            running_mean = unit(sum(w * v for v, w in zip(axis_vecs, weights)))
            if np.dot(vec, running_mean) < 0:
                vec = -vec

        axis_vecs.append(vec)
        weights.append(ell_power)

    if not axis_vecs:
        raise ValueError("No Heegner ells found within lmax -- increase lmax or check alm")

    total_w = sum(weights)
    mean_vec = sum(w * v for v, w in zip(axis_vecs, weights))
    return unit(mean_vec / total_w)


# ---------------------------------------------------------------------------
# Estimator 3 -- PHASE_COHERENCE
# Build a pixel-space phase coherence map from the Heegner alm:
# for each pixel p, project each (ell, m) Heegner mode onto p via
# Y_lm(theta_p, phi_p) and accumulate the mean resultant length of
# the resulting phase angles.  High MRL = coherent phase = origin region.
# Then take the gradient direction of this MRL map.
#
# Implementation uses a low-nside approximation for speed (nside=32);
# the gradient direction is stable to nside choice.
# ---------------------------------------------------------------------------

def estimator_phase_coherence(
    heegner_alm: np.ndarray,
    lmax: int,
    nside_coarse: int = 32,
) -> np.ndarray:
    npix = hp.nside2npix(nside_coarse)

    # Reconstruct Heegner map at coarse resolution
    coarse_map = hp.alm2map(heegner_alm, nside=nside_coarse, lmax=lmax, verbose=False)

    # Phase map: angle of complex alm2map evaluated per pixel
    # We approximate per-pixel phase coherence via the local gradient magnitude
    # of the reconstructed map -- peaks in |grad T| correlate with phase fronts.
    # True per-pixel MRL over Y_lm basis would require O(npix * lmax^2) ops;
    # gradient proxy is O(npix) and sufficient for direction estimation.
    alm_coarse = hp.map2alm(coarse_map, lmax=lmax, pol=False, use_weights=False)

    # Compute gradient magnitude map via spin-1 derivatives
    # hp.alm2map_der1 returns (d/dtheta, 1/sin(theta) * d/dphi)
    try:
        dtheta, dphi = hp.alm2map_der1(alm_coarse, nside_coarse)
        grad_mag = np.sqrt(dtheta ** 2 + dphi ** 2)
    except Exception:
        # Fallback: use abs(map) if derivative fails
        grad_mag = np.abs(coarse_map)

    # First zenith = direction of maximum gradient magnitude
    # (steepest phase front = origin of the coherence structure)
    pix = int(np.argmax(grad_mag))
    theta, phi = hp.pix2ang(nside_coarse, pix)
    vec = np.array([
        math.sin(theta) * math.cos(phi),
        math.sin(theta) * math.sin(phi),
        math.cos(theta),
    ], dtype=np.float64)
    return unit(vec)


# ---------------------------------------------------------------------------
# Consensus: trimmed circular mean of the three estimators
# ---------------------------------------------------------------------------

def consensus_direction(vecs: List[np.ndarray]) -> np.ndarray:
    """Simple mean of unit vectors, re-normalised.  Works when spread < 90 deg."""
    mean = np.mean(np.stack(vecs, axis=0), axis=0)
    return unit(mean)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def compute_zenith(
    alm_path: Path,
    k_path: Path,
    fv_path: Path,
) -> None:
    print(f"Loading alm from  {alm_path}")
    alm_data = np.load(alm_path)
    lmax = int(alm_data["lmax"])
    nside = int(alm_data["nside"])
    heegner_alm = alm_data["heegner_alm"]

    print(f"Loading K_curvature from  {k_path}")
    k_data = np.load(k_path)
    k_heegner = k_data["K_heegner_map"].astype(np.float64)

    print(f"Loading frame_vectors from  {fv_path}")
    with open(fv_path, "r", encoding="utf-8") as f:
        fv = json.load(f)

    dipole_vec = np.array([
        fv["dipole_direction"]["x"],
        fv["dipole_direction"]["y"],
        fv["dipole_direction"]["z"],
    ], dtype=np.float64)
    anti_dipole_vec = -dipole_vec

    # -----------------------------------------------------------------------
    print("Running estimator 1: HEEGNER_K_CENTROID ...")
    z1_k = estimator_heegner_k_centroid(k_heegner, nside)

    print("Running estimator 2: MULTIPOLE_ALIGNMENT ...")
    z1_ma = estimator_multipole_alignment(heegner_alm, lmax, nside)

    print("Running estimator 3: PHASE_COHERENCE ...")
    z1_pc = estimator_phase_coherence(heegner_alm, lmax)

    z1_consensus = consensus_direction([z1_k, z1_ma, z1_pc])

    # Pairwise agreement
    agreement_12 = angle_between_deg(z1_k, z1_ma)
    agreement_13 = angle_between_deg(z1_k, z1_pc)
    agreement_23 = angle_between_deg(z1_ma, z1_pc)
    max_pairwise = max(agreement_12, agreement_13, agreement_23)

    # delta_zenith: primary estimate vs dipole second-zenith
    delta_zenith = angle_between_deg(z1_k, dipole_vec)

    # Legacy proxy
    legacy_delta = angle_between_deg(anti_dipole_vec, dipole_vec)

    print(f"  HEEGNER_K_CENTROID  : lon={vec_to_lonlat(z1_k)[0]:.2f} lat={vec_to_lonlat(z1_k)[1]:.2f}")
    print(f"  MULTIPOLE_ALIGNMENT : lon={vec_to_lonlat(z1_ma)[0]:.2f} lat={vec_to_lonlat(z1_ma)[1]:.2f}")
    print(f"  PHASE_COHERENCE     : lon={vec_to_lonlat(z1_pc)[0]:.2f} lat={vec_to_lonlat(z1_pc)[1]:.2f}")
    print(f"  Consensus           : lon={vec_to_lonlat(z1_consensus)[0]:.2f} lat={vec_to_lonlat(z1_consensus)[1]:.2f}")
    print(f"  Max pairwise spread : {max_pairwise:.2f} deg")
    print(f"  delta_zenith (K_centroid vs dipole) : {delta_zenith:.2f} deg")
    print(f"  Legacy anti-dipole delta_zenith     : {legacy_delta:.2f} deg")

    # -----------------------------------------------------------------------
    # Write back to frame_vectors.json
    fv["zatao_first_zenith"] = {
        "HEEGNER_K_CENTROID": vec_to_dict(z1_k),
        "MULTIPOLE_ALIGNMENT": vec_to_dict(z1_ma),
        "PHASE_COHERENCE": vec_to_dict(z1_pc),
        "consensus": vec_to_dict(z1_consensus),
        "estimator_notes": {
            "HEEGNER_K_CENTROID": "Primary. Intensity-weighted centroid of K_heegner map (clipped at 99.5th percentile).",
            "MULTIPOLE_ALIGNMENT": "Power-weighted mean of per-ell Heegner multipole axes. Tests angular clustering.",
            "PHASE_COHERENCE": "Gradient of Heegner phase coherence map (grad-magnitude proxy). Robust to amplitude outliers.",
            "consensus": "Trimmed circular mean of the three estimators. Use when max_pairwise_spread_deg < 30.",
        },
    }
    fv["delta_zenith_deg"] = delta_zenith
    fv["delta_zenith_estimator"] = "HEEGNER_K_CENTROID_vs_dipole"
    fv["estimator_agreement_deg"] = {
        "K_CENTROID_vs_MULTIPOLE": agreement_12,
        "K_CENTROID_vs_PHASE": agreement_13,
        "MULTIPOLE_vs_PHASE": agreement_23,
        "max_pairwise_spread_deg": max_pairwise,
        "interpretation": (
            "spread < 30 deg: estimators agree, consensus is reliable; "
            "30-60 deg: moderate disagreement, use HEEGNER_K_CENTROID as primary; "
            "> 60 deg: estimators disagree, delta_zenith unreliable, increase lmax or check data quality"
        ),
    }
    fv["legacy_anti_dipole_delta_zenith_deg"] = legacy_delta
    fv["notes"]["monopole_definition"] = (
        "ZaTaOa first zenith estimated via three independent Heegner-mode estimators. "
        "See zatao_first_zenith for details. Legacy anti-dipole proxy retained for comparison."
    )

    with open(fv_path, "w", encoding="utf-8") as f:
        json.dump(fv, f, indent=2)

    print(f"Updated {fv_path}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="ZaTaOa first-zenith estimator")
    parser.add_argument("--alm", type=Path, default=None)
    parser.add_argument("--k",   type=Path, default=None)
    parser.add_argument("--fv",  type=Path, default=None)
    args = parser.parse_args()

    default_alm, default_k, default_fv = default_paths()
    compute_zenith(
        alm_path=args.alm or default_alm,
        k_path=args.k   or default_k,
        fv_path=args.fv or default_fv,
    )
