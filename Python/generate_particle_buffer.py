"""generate_particle_buffer.py  --  Script 4 of the CMB particle pipeline.

Converts classified alm arrays, K_curvature map, and frame_vectors into
a flat binary particle_buffer.bin of 65 536 GPU-aligned ParticleRecord
structs for the HolographicParticleLayer ComputeShader.

Design decisions
----------------
Positions are stored in galactic unit-sphere coordinates.  The galactic
-> Unity world-space rotation is NOT baked into the buffer -- it is
applied at runtime via the _GalToUnity 4x4 shader uniform so that the
ZaTaOa zenith frame can be updated each epoch tick without regenerating
the buffer.

The first-zenith interpolates logarithmically along the T axis:

    zenith(T) = slerp(z0, z1, ln(1+T) / ln(1+T_max))

where:
    z0  = inverse-monopole  = antipode of CMB dipole direction
          (direction of maximum isotropy at initial conditions, T=0)
    z1  = consensus ZaTaOa zenith from frame_vectors.json
          (lon=302, lat=-11, Great Attractor basin, current epoch)

Slerp is used rather than lerp because positions live on S^2.

ParticleRecord layout (96 bytes, std430 aligned)
------------------------------------------------
offset  0  : float3  position       (galactic unit sphere)
offset 12  : float   radius         (0..1, normalised distance from origin)
offset 16  : float3  velocity_seed  (galactic, unit)
offset 28  : float   speed          (0..1)
offset 32  : float4  colour_seed    (rgba, class-derived base colour)
offset 48  : float   kappa          (local Heegner curvature weight)
offset 52  : float   parity_weight  (local soliton/void competition ratio)
offset 56  : uint    class_flags    (see ClassFlags below)
offset 60  : uint    heegner_ell    (0 if not Heegner class)
offset 64  : float3  anchor_dir     (direction to nearest HEEGNER_ANCHOR)
offset 76  : float   anchor_dist    (angular distance to nearest anchor, rad)
offset 80  : float4  _pad           (reserved, zero)

Outputs
-------
Assets/StreamingAssets/CMB/particle_buffer.bin  -- raw struct array
Assets/StreamingAssets/CMB/epoch_frame.json     -- zenith vectors + matrix

Usage
-----
    python generate_particle_buffer.py
    python generate_particle_buffer.py --t 0.72 --tmax 1.0
"""

from __future__ import annotations

import argparse
import json
import math
import struct
from pathlib import Path
from typing import Dict, List, NamedTuple, Tuple

import healpy as hp
import numpy as np

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

HEEGNER_NUMBERS: List[int] = sorted([1, 2, 3, 7, 11, 19, 43, 67, 163])

N_TOTAL       = 65_536
N_HEEGNER     = 512        # never culled, one block per mode
N_SOLITON     = 24_576     # ~37.5 %
N_VOID        = N_TOTAL - N_HEEGNER - N_SOLITON   # ~40 448

RECORD_SIZE   = 96         # bytes, std430
EPSILON       = 1e-12

# ClassFlags bitmask (matches HolographicParticleLayer.cs)
CLASS_HEEGNER_ANCHOR      = 1 << 0
CLASS_HEEGNER_L2_BLUESHIFTED = 1 << 1   # ell=2 anomalous suppression
CLASS_SOLITON             = 1 << 2
CLASS_VOID                = 1 << 3
CLASS_NEVER_CULL          = 1 << 4      # combined with HEEGNER_ANCHOR

# Base colours (rgba float, linear)
COLOUR_HEEGNER    = (0.85, 0.92, 1.00, 1.0)   # cold white-blue
COLOUR_HEEGNER_L2 = (0.55, 0.70, 1.00, 1.0)   # stronger blue-shift for ell=2
COLOUR_SOLITON    = (0.72, 0.88, 0.78, 0.8)   # pale soliton green
COLOUR_VOID       = (0.18, 0.12, 0.22, 0.4)   # deep void purple


# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

def default_paths(repo_root: Path) -> Dict[str, Path]:
    processed      = repo_root / "Assets" / "CMB" / "Processed"
    streaming      = repo_root / "Assets" / "StreamingAssets" / "CMB"
    return {
        "alm":            processed / "alm_by_class.npz",
        "k":              processed / "K_curvature.npz",
        "fv":             processed / "frame_vectors.json",
        "scalars":        processed / "field_scalars.json",
        "buffer_out":     streaming  / "particle_buffer.bin",
        "frame_out":      streaming  / "epoch_frame.json",
    }


# ---------------------------------------------------------------------------
# Vector utilities
# ---------------------------------------------------------------------------

def unit(v: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(v)
    return v / n if n > EPSILON else v


def slerp(a: np.ndarray, b: np.ndarray, t: float) -> np.ndarray:
    """Spherical linear interpolation between unit vectors a and b."""
    a, b = unit(a), unit(b)
    dot = float(np.clip(np.dot(a, b), -1.0, 1.0))
    theta = math.acos(dot)
    if abs(theta) < EPSILON:
        return a
    return (math.sin((1 - t) * theta) * a + math.sin(t * theta) * b) / math.sin(theta)


def vec_to_dict(v: np.ndarray) -> Dict:
    v = unit(v)
    lat = math.degrees(math.asin(float(np.clip(v[2], -1.0, 1.0))))
    lon = math.degrees(math.atan2(float(v[1]), float(v[0]))) % 360.0
    return {"x": float(v[0]), "y": float(v[1]), "z": float(v[2]),
            "lon_deg": lon, "lat_deg": lat}


def lonlat_to_vec(lon_deg: float, lat_deg: float) -> np.ndarray:
    lon = math.radians(lon_deg)
    lat = math.radians(lat_deg)
    return np.array([
        math.cos(lat) * math.cos(lon),
        math.cos(lat) * math.sin(lon),
        math.sin(lat),
    ], dtype=np.float64)


# ---------------------------------------------------------------------------
# Zenith interpolation
# ---------------------------------------------------------------------------

def zenith_at_epoch(T: float, T_max: float,
                    z0: np.ndarray, z1: np.ndarray) -> np.ndarray:
    """Return the slerp'd first-zenith unit vector at epoch T.

    zenith(T) = slerp(z0, z1, ln(1+T) / ln(1+T_max))

    z0  -- inverse-monopole: antipode of CMB dipole (T=0, max isotropy)
    z1  -- consensus ZaTaOa zenith (T=T_max, current epoch)
    T   -- epoch parameter in [0, T_max]
    """
    if T_max < EPSILON:
        return unit(z1)
    alpha = math.log1p(max(0.0, T)) / math.log1p(T_max)
    alpha = float(np.clip(alpha, 0.0, 1.0))
    return unit(slerp(z0, z1, alpha))


def build_gal_to_unity(zenith: np.ndarray) -> np.ndarray:
    """Build a 3x3 rotation matrix that maps galactic +Z to zenith.

    The Unity Y-up convention: galactic zenith -> Unity world +Y.
    Returns column-major 4x4 (last row/col identity) for shader upload.
    """
    up    = unit(zenith)
    # Choose an arbitrary perpendicular as 'east'
    ref   = np.array([0.0, 0.0, 1.0])
    if abs(np.dot(up, ref)) > 0.99:
        ref = np.array([1.0, 0.0, 0.0])
    east  = unit(np.cross(ref, up))
    north = unit(np.cross(up, east))

    # Row vectors: galactic basis mapped to Unity XYZ
    # Unity +Y = zenith (up), +X = east, +Z = north (into screen)
    R = np.array([
        [east[0],  east[1],  east[2],  0.0],
        [up[0],    up[1],    up[2],    0.0],
        [north[0], north[1], north[2], 0.0],
        [0.0,      0.0,      0.0,      1.0],
    ], dtype=np.float32)
    return R


# ---------------------------------------------------------------------------
# Heegner anchor directions
# ---------------------------------------------------------------------------

def compute_anchor_directions(heegner_alm: np.ndarray,
                               lmax: int, nside: int) -> Dict[int, np.ndarray]:
    """For each Heegner ell, return the unit vector of max |Y_lm| amplitude."""
    anchors: Dict[int, np.ndarray] = {}
    for ell in HEEGNER_NUMBERS:
        if ell > lmax:
            continue
        single_alm = np.zeros_like(heegner_alm)
        for m in range(ell + 1):
            idx = hp.Alm.getidx(lmax, ell, m)
            single_alm[idx] = heegner_alm[idx]
        ell_map = hp.alm2map(single_alm, nside=nside, lmax=lmax)
        pix = int(np.argmax(np.abs(ell_map)))
        theta, phi = hp.pix2ang(nside, pix)
        anchors[ell] = np.array([
            math.sin(theta) * math.cos(phi),
            math.sin(theta) * math.sin(phi),
            math.cos(theta),
        ], dtype=np.float32)
    return anchors


def nearest_anchor(pos: np.ndarray,
                   anchors: Dict[int, np.ndarray]) -> Tuple[np.ndarray, float]:
    """Return (anchor_dir, angular_distance_rad) for the nearest anchor."""
    best_dot  = -2.0
    best_dir  = np.zeros(3, dtype=np.float32)
    for a in anchors.values():
        d = float(np.dot(unit(pos), unit(a)))
        if d > best_dot:
            best_dot = d
            best_dir = a
    best_dot  = float(np.clip(best_dot, -1.0, 1.0))
    dist_rad  = math.acos(best_dot)
    return unit(best_dir), dist_rad


# ---------------------------------------------------------------------------
# Particle record packing
# ---------------------------------------------------------------------------

# struct ParticleRecord { float3 pos; float radius;
#   float3 vel; float speed; float4 colour;
#   float kappa; float parity_weight; uint flags; uint ell;
#   float3 anchor_dir; float anchor_dist; float4 pad; }
_FMT = "<3ff3ff4ff2I3ff4f"   # 24 floats + 2 uints = 96 bytes

def pack_record(pos: np.ndarray, radius: float,
                vel: np.ndarray, speed: float,
                colour: Tuple[float,float,float,float],
                kappa: float, parity_weight: float,
                flags: int, ell: int,
                anchor_dir: np.ndarray, anchor_dist: float) -> bytes:
    return struct.pack(
        _FMT,
        float(pos[0]), float(pos[1]), float(pos[2]), float(radius),
        float(vel[0]), float(vel[1]), float(vel[2]), float(speed),
        float(colour[0]), float(colour[1]), float(colour[2]), float(colour[3]),
        float(kappa), float(parity_weight),
        int(flags), int(ell),
        float(anchor_dir[0]), float(anchor_dir[1]), float(anchor_dir[2]),
        float(anchor_dist),
        0.0, 0.0, 0.0, 0.0,   # _pad
    )


# ---------------------------------------------------------------------------
# Particle generators
# ---------------------------------------------------------------------------

def generate_heegner_particles(
    anchors: Dict[int, np.ndarray],
    k_heegner: np.ndarray,
    nside: int,
    parity_asymmetry: float,
    rng: np.random.Generator,
) -> List[bytes]:
    """512 Heegner particles -- clustered around anchor directions."""
    records: List[bytes] = []
    ells = [e for e in HEEGNER_NUMBERS if e in anchors]
    per_ell = N_HEEGNER // len(ells)
    remainder = N_HEEGNER - per_ell * len(ells)

    for i, ell in enumerate(ells):
        n = per_ell + (1 if i < remainder else 0)
        anchor = anchors[ell]
        flags = CLASS_HEEGNER_ANCHOR | CLASS_NEVER_CULL
        colour = COLOUR_HEEGNER_L2 if ell == 2 else COLOUR_HEEGNER
        if ell == 2:
            flags |= CLASS_HEEGNER_L2_BLUESHIFTED

        # Scatter particles in a von Mises-Fisher cap around the anchor
        # Concentration kappa_vmf proportional to ell power
        ell_pix  = hp.ang2pix(nside, *hp.vec2ang(anchor))
        k_local  = float(np.abs(k_heegner[ell_pix]))
        kappa_vmf = float(np.clip(k_local * 50.0, 2.0, 80.0))

        for _ in range(n):
            pos = unit(_vmf_sample(anchor, kappa_vmf, rng))
            # Velocity: tangential drift along great circle toward anchor
            vel = unit(np.cross(pos, anchor))
            speed = float(rng.uniform(0.002, 0.008))
            radius = float(rng.uniform(0.85, 1.0))
            anc_dir, anc_dist = nearest_anchor(pos, anchors)
            records.append(pack_record(
                pos, radius, vel, speed,
                colour, k_local,
                float(np.clip(1.0 - parity_asymmetry * 0.5, 0.0, 1.0)),
                flags, ell, anc_dir, anc_dist,
            ))
    return records


def generate_soliton_particles(
    anchors: Dict[int, np.ndarray],
    soliton_map: np.ndarray,
    nside: int,
    parity_asymmetry: float,
    rng: np.random.Generator,
) -> List[bytes]:
    """~24k Soliton particles -- distributed by soliton power map,
    velocity seeds drifting toward nearest HEEGNER_ANCHOR weighted by kappa."""
    records: List[bytes] = []
    # Importance-sample pixel positions from soliton power map
    weights = np.clip(np.abs(soliton_map), 0, None).astype(np.float64)
    weights /= weights.sum()
    pixels = rng.choice(len(weights), size=N_SOLITON, p=weights)

    for pix in pixels:
        theta, phi = hp.pix2ang(nside, int(pix))
        pos = np.array([
            math.sin(theta) * math.cos(phi),
            math.sin(theta) * math.sin(phi),
            math.cos(theta),
        ], dtype=np.float32)
        anc_dir, anc_dist = nearest_anchor(pos, anchors)
        # Drift toward anchor, scaled by proximity
        kappa  = float(np.abs(soliton_map[pix]))
        attract = unit(anc_dir - pos)
        vel    = unit(attract + rng.standard_normal(3).astype(np.float32) * 0.15)
        speed  = float(np.clip(kappa * 0.6, 0.001, 0.05))
        radius = float(rng.uniform(0.5, 1.0))
        # Parity: soliton brightens when parity_asymmetry < 1 (soliton dominant)
        pw     = float(np.clip(2.0 - parity_asymmetry, 0.0, 1.0))
        colour = (
            COLOUR_SOLITON[0],
            COLOUR_SOLITON[1],
            COLOUR_SOLITON[2],
            float(np.clip(COLOUR_SOLITON[3] * pw, 0.05, 1.0)),
        )
        records.append(pack_record(
            pos, radius, vel, speed,
            colour, kappa, pw,
            CLASS_SOLITON, 0, anc_dir, anc_dist,
        ))
    return records


def generate_void_particles(
    anchors: Dict[int, np.ndarray],
    void_map: np.ndarray,
    nside: int,
    parity_asymmetry: float,
    rng: np.random.Generator,
) -> List[bytes]:
    """~40k Void particles -- pressure field, velocity seeds pushing away
    from nearest HEEGNER_ANCHOR weighted by void pressure."""
    records: List[bytes] = []
    weights = np.clip(np.abs(void_map), 0, None).astype(np.float64)
    weights /= weights.sum()
    pixels = rng.choice(len(weights), size=N_VOID, p=weights)

    for pix in pixels:
        theta, phi = hp.pix2ang(nside, int(pix))
        pos = np.array([
            math.sin(theta) * math.cos(phi),
            math.sin(theta) * math.sin(phi),
            math.cos(theta),
        ], dtype=np.float32)
        anc_dir, anc_dist = nearest_anchor(pos, anchors)
        # Push away from anchor
        repel = unit(pos - anc_dir)
        vel   = unit(repel + rng.standard_normal(3).astype(np.float32) * 0.2)
        pressure = float(np.abs(void_map[pix]))
        speed    = float(np.clip(pressure * 0.4, 0.001, 0.04))
        radius   = float(rng.uniform(0.3, 1.0))
        # Parity: void brightens when parity_asymmetry > 1 (void dominant)
        pw       = float(np.clip(parity_asymmetry, 0.0, 2.0))
        colour   = (
            COLOUR_VOID[0],
            COLOUR_VOID[1],
            COLOUR_VOID[2],
            float(np.clip(COLOUR_VOID[3] * pw, 0.02, 0.8)),
        )
        records.append(pack_record(
            pos, radius, vel, speed,
            colour, pressure, pw,
            CLASS_VOID, 0, anc_dir, anc_dist,
        ))
    return records


# ---------------------------------------------------------------------------
# Von Mises-Fisher sampler (3D)
# ---------------------------------------------------------------------------

def _vmf_sample(mu: np.ndarray, kappa: float,
                rng: np.random.Generator) -> np.ndarray:
    """Sample one unit vector from vMF(mu, kappa) distribution."""
    mu = unit(mu)
    # Sample cos(theta) from the marginal
    xi   = rng.uniform(0.0, 1.0)
    W    = 1.0 + (1.0 / kappa) * math.log(xi + (1.0 - xi) * math.exp(-2.0 * kappa))
    # Random azimuth around mu
    phi  = rng.uniform(0.0, 2.0 * math.pi)
    # Orthonormal frame around mu
    ref  = np.array([0.0, 0.0, 1.0])
    if abs(np.dot(mu, ref)) > 0.99:
        ref = np.array([1.0, 0.0, 0.0])
    v1   = unit(np.cross(mu, ref))
    v2   = unit(np.cross(mu, v1))
    sin_theta = math.sqrt(max(0.0, 1.0 - W * W))
    return (sin_theta * math.cos(phi)) * v1 + \
           (sin_theta * math.sin(phi)) * v2 + \
           W * mu


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def generate(
    alm_path: Path, k_path: Path, fv_path: Path, scalars_path: Path,
    buffer_out: Path, frame_out: Path,
    T: float = 1.0, T_max: float = 1.0,
    seed: int = 42,
) -> None:
    rng = np.random.default_rng(seed)

    print(f"Loading alm         : {alm_path}")
    alm_data    = np.load(alm_path)
    lmax        = int(alm_data["lmax"])
    nside       = int(alm_data["nside"])
    heegner_alm = alm_data["heegner_alm"]
    soliton_alm = alm_data["soliton_alm"]

    print(f"Loading K_curvature : {k_path}")
    k_data      = np.load(k_path)
    k_heegner   = k_data["K_heegner_map"].astype(np.float64)

    print(f"Loading frame_vectors: {fv_path}")
    with open(fv_path, "r", encoding="utf-8") as f:
        fv = json.load(f)

    # Void map: reconstruct from all non-heegner, non-soliton alm
    # Approximate as full_alm minus heegner_alm minus soliton_alm
    full_alm    = alm_data["full_alm"]
    void_alm    = full_alm - heegner_alm - soliton_alm

    print("Reconstructing pixel maps ...")
    soliton_map = hp.alm2map(soliton_alm, nside=nside, lmax=lmax)
    void_map    = hp.alm2map(void_alm,    nside=nside, lmax=lmax)

    # Parity asymmetry from field_scalars.json (default 1.0 if missing)
    parity_asymmetry = 1.0
    if scalars_path.exists():
        with open(scalars_path, "r", encoding="utf-8") as f:
            scalars = json.load(f)
        parity_asymmetry = float(scalars.get("parity_asymmetry", 1.0))
    print(f"Parity asymmetry    : {parity_asymmetry:.4f}")

    # Zenith vectors
    dipole = np.array([
        fv["dipole_direction"]["x"],
        fv["dipole_direction"]["y"],
        fv["dipole_direction"]["z"],
    ], dtype=np.float64)
    z0 = unit(-dipole)   # inverse-monopole = antipode of dipole

    consensus = fv["zatao_first_zenith"]["consensus"]
    z1 = np.array([consensus["x"], consensus["y"], consensus["z"]], dtype=np.float64)

    z_epoch = zenith_at_epoch(T, T_max, z0, z1)
    M       = build_gal_to_unity(z_epoch)

    print(f"Zenith at T={T:.3f}  : lon={math.degrees(math.atan2(float(z_epoch[1]), float(z_epoch[0]))) % 360:.2f}  "
          f"lat={math.degrees(math.asin(float(np.clip(z_epoch[2], -1, 1)))):.2f}")

    # Compute Heegner anchor directions
    print("Computing Heegner anchor directions ...")
    anchors = compute_anchor_directions(heegner_alm, lmax, nside)
    print(f"  {len(anchors)} anchors: ells {sorted(anchors.keys())}")

    # Generate particles
    print(f"Generating {N_HEEGNER} Heegner particles ...")
    h_records = generate_heegner_particles(
        anchors, k_heegner, nside, parity_asymmetry, rng)

    print(f"Generating {N_SOLITON} Soliton particles ...")
    s_records = generate_soliton_particles(
        anchors, soliton_map, nside, parity_asymmetry, rng)

    print(f"Generating {N_VOID} Void particles ...")
    v_records = generate_void_particles(
        anchors, void_map, nside, parity_asymmetry, rng)

    all_records = h_records + s_records + v_records
    assert len(all_records) == N_TOTAL, f"Expected {N_TOTAL}, got {len(all_records)}"
    assert all(len(r) == RECORD_SIZE for r in all_records), "Record size mismatch"

    # Write binary buffer
    buffer_out.parent.mkdir(parents=True, exist_ok=True)
    with open(buffer_out, "wb") as f:
        for r in all_records:
            f.write(r)
    print(f"Written {buffer_out}  ({buffer_out.stat().st_size / 1024:.1f} KB)")

    # Write epoch_frame.json for shader consumption
    frame = {
        "T":                T,
        "T_max":            T_max,
        "parity_asymmetry": parity_asymmetry,
        "inverse_monopole": vec_to_dict(z0),
        "consensus_zenith": vec_to_dict(z1),
        "zenith_at_T":      vec_to_dict(z_epoch),
        "M_gal_to_unity":   M.flatten().tolist(),
        "class_flags": {
            "HEEGNER_ANCHOR":       CLASS_HEEGNER_ANCHOR,
            "HEEGNER_L2_BLUESHIFTED": CLASS_HEEGNER_L2_BLUESHIFTED,
            "SOLITON":              CLASS_SOLITON,
            "VOID":                 CLASS_VOID,
            "NEVER_CULL":           CLASS_NEVER_CULL,
        },
        "n_particles": {
            "heegner": len(h_records),
            "soliton": len(s_records),
            "void":    len(v_records),
            "total":   N_TOTAL,
        },
        "record_size_bytes": RECORD_SIZE,
        "heegner_ells": sorted(anchors.keys()),
        "anchor_directions": {
            str(ell): vec_to_dict(v) for ell, v in anchors.items()
        },
    }
    with open(frame_out, "w", encoding="utf-8") as f:
        json.dump(frame, f, indent=2)
    print(f"Written {frame_out}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Generate holographic particle buffer")
    parser.add_argument("--t",    type=float, default=1.0,
                        help="Current epoch T (default: 1.0 = present)")
    parser.add_argument("--tmax", type=float, default=1.0,
                        help="Maximum epoch T (default: 1.0)")
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    script_dir = Path(__file__).resolve().parent
    repo_root  = script_dir.parent
    paths      = default_paths(repo_root)

    generate(
        alm_path=paths["alm"],
        k_path=paths["k"],
        fv_path=paths["fv"],
        scalars_path=paths["scalars"],
        buffer_out=paths["buffer_out"],
        frame_out=paths["frame_out"],
        T=args.t,
        T_max=args.tmax,
        seed=args.seed,
    )
