"""preprocess_cmb_harmonics.py

Harmonic-first CMB preprocessing pipeline for the infinite-improbability-drive repo.

Outputs:
  - processed/alm_by_class.npz
  - processed/field_scalars.json
  - processed/K_curvature.npz
  - processed/K_stats.json
  - processed/frame_vectors.json

Pipeline:
  1. Load Planck SMICA temperature map
  2. Compute full alm decomposition
  3. Classify multipoles into HEEGNER / SOLITON / VOID_PRIME / SOLITON_COMPOSITE
  4. Reconstruct class-restricted maps and export harmonic buffers
  5. Compute conformal factor sigma and curvature field K(theta, phi)
  6. Extract frame-defining observables (dipole, quadrupole axis, parity ratios)
"""

from __future__ import annotations

import json
import math
import os
from pathlib import Path
from typing import Dict, Tuple

import healpy as hp
import numpy as np

HEEGNER_NUMBERS = {1, 2, 3, 7, 11, 19, 43, 67, 163}
EPSILON = 1e-10
DEFAULT_LMAX = 256
DEFAULT_NSIDE_RECON = 512


def repo_paths() -> Tuple[Path, Path, Path]:
    script_dir = Path(__file__).resolve().parent
    repo_root = script_dir.parent
    fits_path = repo_root / "Assets" / "CMB" / "Data" / "COM_CMB_IQU-smica_2048_R3.00_full.fits"
    out_dir = repo_root / "Assets" / "CMB" / "Processed"
    return repo_root, fits_path, out_dir


def alm_zero_like(lmax: int) -> np.ndarray:
    return np.zeros(hp.Alm.getsize(lmax), dtype=np.complex128)


def mode_class(ell: int) -> str:
    if ell in HEEGNER_NUMBERS:
        return "heegner"
    if ell >= 2 and is_prime(ell):
        return "void_prime" if ell % 2 == 1 else "heegner"
    if ell % 2 == 0:
        return "soliton"
    return "soliton_composite"


def is_prime(n: int) -> bool:
    if n < 2:
        return False
    if n == 2:
        return True
    if n % 2 == 0:
        return False
    r = int(n ** 0.5)
    for k in range(3, r + 1, 2):
        if n % k == 0:
            return False
    return True


def classify_alm(full_alm: np.ndarray, lmax: int) -> Dict[str, np.ndarray]:
    out = {
        "heegner": alm_zero_like(lmax),
        "soliton": alm_zero_like(lmax),
        "void_prime": alm_zero_like(lmax),
        "soliton_composite": alm_zero_like(lmax),
    }
    for ell in range(lmax + 1):
        bucket = mode_class(ell)
        for m in range(ell + 1):
            idx = hp.Alm.getidx(lmax, ell, m)
            out[bucket][idx] = full_alm[idx]
    return out


def alm_power_by_ell(alm: np.ndarray, lmax: int) -> np.ndarray:
    cls = hp.alm2cl(alm, lmax_out=lmax)
    return np.asarray(cls, dtype=np.float64)


def reconstruct_map(alm: np.ndarray, nside: int, lmax: int) -> np.ndarray:
    return hp.alm2map(alm, nside=nside, lmax=lmax, verbose=False)


def compute_sigma_and_curvature(
    temp_map: np.ndarray, t0: float, lmax: int
) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    delta_t = temp_map - np.mean(temp_map)
    sigma_map = 0.5 * np.log(np.abs(delta_t / t0) + EPSILON)
    sigma_alm = hp.map2alm(sigma_map, lmax=lmax, pol=False, use_weights=False)
    lap_alm = np.copy(sigma_alm)
    for ell in range(lmax + 1):
        factor = -ell * (ell + 1)
        for m in range(ell + 1):
            idx = hp.Alm.getidx(lmax, ell, m)
            lap_alm[idx] *= factor
    lap_sigma = hp.alm2map(lap_alm, nside=hp.get_nside(temp_map), lmax=lmax, verbose=False)
    k_map = -np.exp(-2.0 * sigma_map) * lap_sigma
    return sigma_map, lap_sigma, k_map


def dipole_from_alm(full_alm: np.ndarray, lmax: int, nside: int = 32) -> Dict[str, float]:
    dipole_alm = alm_zero_like(lmax)
    for m in range(2):
        idx = hp.Alm.getidx(lmax, 1, m)
        dipole_alm[idx] = full_alm[idx]
    dipole_map = hp.alm2map(dipole_alm, nside=nside, lmax=lmax, verbose=False)
    amp, lon, lat = hp.fit_dipole(dipole_map, gal_cut=0)
    direction = ang_to_vec(lon, lat)
    return {
        "amplitude_K": float(amp),
        "lon_deg": float(lon),
        "lat_deg": float(lat),
        "x": float(direction[0]),
        "y": float(direction[1]),
        "z": float(direction[2]),
    }


def quadrupole_axis_from_map(full_alm: np.ndarray, lmax: int, nside: int = 64) -> Dict[str, float]:
    quad_alm = alm_zero_like(lmax)
    ell = 2
    for m in range(ell + 1):
        idx = hp.Alm.getidx(lmax, ell, m)
        quad_alm[idx] = full_alm[idx]
    quad_map = hp.alm2map(quad_alm, nside=nside, lmax=lmax, verbose=False)
    pix = int(np.argmax(np.abs(quad_map)))
    theta, phi = hp.pix2ang(nside, pix)
    lon = math.degrees(phi)
    lat = 90.0 - math.degrees(theta)
    direction = ang_to_vec(lon, lat)
    return {
        "lon_deg": float(lon),
        "lat_deg": float(lat),
        "x": float(direction[0]),
        "y": float(direction[1]),
        "z": float(direction[2]),
    }


def ang_to_vec(lon_deg: float, lat_deg: float) -> np.ndarray:
    lon = np.deg2rad(lon_deg)
    lat = np.deg2rad(lat_deg)
    return np.array([
        np.cos(lat) * np.cos(lon),
        np.cos(lat) * np.sin(lon),
        np.sin(lat),
    ], dtype=np.float64)


def vec_to_serializable(v: np.ndarray) -> Dict[str, float]:
    return {"x": float(v[0]), "y": float(v[1]), "z": float(v[2])}


def compute_field_scalars(classified: Dict[str, np.ndarray], lmax: int) -> Dict[str, float]:
    power = {k: float(np.sum(np.abs(v) ** 2)) for k, v in classified.items()}
    total = sum(power.values()) or 1.0
    heegner_fraction = power["heegner"] / total
    soliton_fraction = power["soliton"] / total
    void_fraction = power["void_prime"] / total
    composite_fraction = power["soliton_composite"] / total
    parity_even = 0.0
    parity_odd = 0.0
    cls = hp.alm2cl(sum(classified.values()), lmax_out=lmax)
    for ell, c in enumerate(cls):
        if ell % 2 == 0:
            parity_even += float(c)
        else:
            parity_odd += float(c)
    parity_asymmetry = parity_odd / max(parity_even, 1e-30)
    return {
        "heegner_fraction": heegner_fraction,
        "soliton_fraction": soliton_fraction,
        "void_fraction": void_fraction,
        "soliton_composite_fraction": composite_fraction,
        "parity_even_power": parity_even,
        "parity_odd_power": parity_odd,
        "parity_asymmetry": parity_asymmetry,
    }


def main() -> None:
    repo_root, fits_path, out_dir = repo_paths()
    out_dir.mkdir(parents=True, exist_ok=True)

    if not fits_path.exists():
        raise FileNotFoundError(f"Planck SMICA FITS not found: {fits_path}")

    lmax = int(os.environ.get("IID_LMAX", DEFAULT_LMAX))
    nside_recon = int(os.environ.get("IID_NSIDE_RECON", DEFAULT_NSIDE_RECON))

    print(f"Loading temperature map from {fits_path}")
    raw_map = hp.read_map(str(fits_path), field=0, dtype=np.float64, verbose=False)
    nside_in = hp.get_nside(raw_map)
    t0 = float(np.mean(raw_map))
    print(f"Input nside={nside_in}  lmax={lmax}  t0={t0:.9f} K")

    print("Computing alm decomposition...")
    full_alm = hp.map2alm(raw_map, lmax=lmax, pol=False, use_weights=False)

    print("Classifying harmonic modes...")
    classified = classify_alm(full_alm, lmax)
    class_maps = {name: reconstruct_map(alm, nside_recon, lmax) for name, alm in classified.items()}

    print("Computing field scalars...")
    field_scalars = compute_field_scalars(classified, lmax)

    print("Computing curvature field...")
    full_map_recon = reconstruct_map(full_alm, nside_recon, lmax)
    sigma_map, lap_sigma, k_map = compute_sigma_and_curvature(full_map_recon, t0, lmax)
    _, _, k_heegner = compute_sigma_and_curvature(class_maps["heegner"], t0, lmax)

    print("Extracting frame vectors...")
    dipole = dipole_from_alm(full_alm, lmax)
    neck_axis = quadrupole_axis_from_map(full_alm, lmax)
    monopole_direction = -np.array([dipole["x"], dipole["y"], dipole["z"]], dtype=np.float64)
    monopole_direction /= np.linalg.norm(monopole_direction)
    dipole_direction = np.array([dipole["x"], dipole["y"], dipole["z"]], dtype=np.float64)
    dipole_direction /= np.linalg.norm(dipole_direction)
    delta_zenith_deg = float(np.degrees(
        np.arccos(np.clip(np.dot(monopole_direction, dipole_direction), -1.0, 1.0))
    ))
    dipole_magnitude_km_s = float((dipole["amplitude_K"] / max(t0, 1e-30)) * 299792.458)

    frame_vectors = {
        "monopole_direction": vec_to_serializable(monopole_direction),
        "dipole_direction": vec_to_serializable(dipole_direction),
        "dipole_lon_deg": dipole["lon_deg"],
        "dipole_lat_deg": dipole["lat_deg"],
        "dipole_magnitude_km_s": dipole_magnitude_km_s,
        "delta_zenith_deg": delta_zenith_deg,
        "neck_axis_galactic": {
            "lon_deg": neck_axis["lon_deg"],
            "lat_deg": neck_axis["lat_deg"],
            "direction": {"x": neck_axis["x"], "y": neck_axis["y"], "z": neck_axis["z"]},
        },
        "notes": {
            "monopole_definition": "Anti-dipole proxy for first zenith until a stricter ZaTaOa-origin estimator is implemented.",
            "delta_zenith_interpretation": "Angular separation between first and second zenith vectors; current implementation uses anti-dipole as first-zenith proxy.",
        },
    }

    print("Writing outputs...")
    np.savez_compressed(
        out_dir / "alm_by_class.npz",
        lmax=lmax,
        nside=nside_recon,
        full_alm=full_alm,
        heegner_alm=classified["heegner"],
        soliton_alm=classified["soliton"],
        void_prime_alm=classified["void_prime"],
        soliton_composite_alm=classified["soliton_composite"],
    )
    np.savez_compressed(
        out_dir / "K_curvature.npz",
        lmax=lmax,
        nside=nside_recon,
        sigma_map=sigma_map.astype(np.float32),
        laplacian_sigma=lap_sigma.astype(np.float32),
        K_map=k_map.astype(np.float32),
        K_heegner_map=k_heegner.astype(np.float32),
        K_competition_map=(k_map - k_heegner).astype(np.float32),
    )

    k_stats = {
        "K_max": float(np.max(k_map)),
        "K_min": float(np.min(k_map)),
        "K_mean": float(np.mean(k_map)),
        "K_std": float(np.std(k_map)),
        "K_heegner_mean": float(np.mean(k_heegner)),
        "K_competition_mean": float(np.mean(k_map - k_heegner)),
        "epsilon": EPSILON,
        "lmax": lmax,
        "nside": nside_recon,
        "heegner_numbers": sorted(HEEGNER_NUMBERS),
    }

    with open(out_dir / "field_scalars.json", "w", encoding="utf-8") as f:
        json.dump(field_scalars, f, indent=2)
    with open(out_dir / "K_stats.json", "w", encoding="utf-8") as f:
        json.dump(k_stats, f, indent=2)
    with open(out_dir / "frame_vectors.json", "w", encoding="utf-8") as f:
        json.dump(frame_vectors, f, indent=2)

    print("Done.")
    print(f"Outputs written to {out_dir}")
    print(f"  heegner_fraction   : {field_scalars['heegner_fraction']:.6f}")
    print(f"  soliton_fraction   : {field_scalars['soliton_fraction']:.6f}")
    print(f"  void_fraction      : {field_scalars['void_fraction']:.6f}")
    print(f"  parity_asymmetry   : {field_scalars['parity_asymmetry']:.6f}")
    print(f"  K_mean             : {k_stats['K_mean']:.6e}")
    print(f"  dipole_magnitude   : {frame_vectors['dipole_magnitude_km_s']:.1f} km/s")
    print(f"  delta_zenith (proxy): {frame_vectors['delta_zenith_deg']:.2f} deg")


if __name__ == "__main__":
    main()
