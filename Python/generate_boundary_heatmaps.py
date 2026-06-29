"""generate_boundary_heatmaps.py  --  Script 5 of the CMB particle pipeline.

Projects the classified alm field power onto equirectangular float32 EXR
textures for use in the boundary sphere shaders.  These are data textures,
not visual images -- Unity samples them in GLSL shaders via the .r channel.

Outputs
-------
Assets/Textures/Boundary/heegner_heatmap.exr
    Per-pixel HEEGNER_LOCKED power, normalised 0-1.
    HEEGNER_ANCHOR node positions painted as gaussian splashes on top
    (sigma=8px, amplitude=2.0) so anchors are spatially legible at
    boundary-sphere render distance in the headset.

Assets/Textures/Boundary/void_heatmap.exr
    Per-pixel VOID_PRIME power, normalised 0-1.

Projection
----------
Equirectangular 4096 x 2048, matching the CMB skybox UV space exactly:

    theta (colatitude) = (1 - v/H) * pi        v in [0, H)
    phi   (longitude)  = (u/W) * 2*pi          u in [0, W)

HEALPix intermediate resolution: Nside=512 captures all modes up to
ell ~ 1500 without aliasing into the EXR output resolution.

Normalisation
-------------
Percentile clipping (p1 / p99) to remove alm reconstruction tail artefacts
before mapping to [0, 1].  This keeps the dynamic range clean for the shader
without hard-clipping genuine bright anchor features.

Observer frame echo
-------------------
Loads the observer_frame block from epoch_frame.json (if present) and prints
MRS NESBITT's bearing (forward / delta_A=0) to stdout as a sanity check that
the coordinate frame is consistent across Scripts 4 and 5.

EXR backend
-----------
imageio >= 2.28 with the OpenEXR plugin is the primary writer.
If OpenEXR is unavailable the script falls back to tifffile (float32 TIFF)
and prints a warning -- Unity can import float TIFFs with manual texture
import settings (Format: R Float, sRGB: off).

Usage
-----
    python generate_boundary_heatmaps.py
    python generate_boundary_heatmaps.py --nside 256 --width 2048 --height 1024
"""

from __future__ import annotations

import argparse
import json
import math
import warnings
from pathlib import Path
from typing import Dict, Optional, Tuple

import healpy as hp
import numpy as np

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

DEFAULT_WIDTH  = 4096
DEFAULT_HEIGHT = 2048
DEFAULT_NSIDE  = 512

ANCHOR_SIGMA_PX  = 8.0    # gaussian splash radius in pixels
ANCHOR_AMPLITUDE = 2.0    # HDR boost for anchor splash (above normalised [0,1])

EPSILON = 1e-12

# Node class indices (must match causal-field-pipeline output)
NODE_CLASS_HEEGNER_ANCHOR = 0


# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

def default_paths(repo_root: Path) -> Dict[str, Path]:
    processed  = repo_root / "Assets" / "CMB" / "Processed"
    streaming  = repo_root / "Assets" / "StreamingAssets" / "CMB"
    textures   = repo_root / "Assets" / "Textures" / "Boundary"
    return {
        "alm":         processed / "alm_by_class.npz",
        "nodes":       processed / "nodes.npz",
        "epoch_frame": streaming  / "epoch_frame.json",
        "heegner_out": textures   / "heegner_heatmap.exr",
        "void_out":    textures   / "void_heatmap.exr",
    }


# ---------------------------------------------------------------------------
# EXR / fallback writer
# ---------------------------------------------------------------------------

def write_exr(path: Path, data: np.ndarray) -> None:
    """Write a 2D float32 array as a single-channel EXR.

    Falls back to float32 TIFF if imageio + OpenEXR plugin are unavailable.
    Unity can import float TIFFs with: Format=R Float, sRGB=off.
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    arr = data.astype(np.float32)

    # imageio path (preferred)
    try:
        import imageio
        # imageio expects HxWxC for colour; for single-channel write HxWx1
        imageio.imwrite(str(path), arr[:, :, np.newaxis], format="EXR-FI")
        print(f"  Written EXR : {path}  ({arr.shape[1]}x{arr.shape[0]})")
        return
    except Exception as exc:  # noqa: BLE001
        warnings.warn(
            f"imageio EXR write failed ({exc}); falling back to TIFF. "
            f"Rename output to .tiff and import in Unity with Format=R Float, sRGB=off."
        )

    # tifffile fallback
    try:
        import tifffile
        tiff_path = path.with_suffix(".tiff")
        tifffile.imwrite(str(tiff_path), arr, photometric="minisblack")
        print(f"  Written TIFF (fallback): {tiff_path}")
    except ImportError:
        raise RuntimeError(
            "Neither imageio+OpenEXR nor tifffile is available. "
            "Install one: pip install imageio[freeimage] OR pip install tifffile"
        ) from None


# ---------------------------------------------------------------------------
# Normalisation
# ---------------------------------------------------------------------------

def percentile_normalise(arr: np.ndarray,
                          p_low: float = 1.0,
                          p_high: float = 99.0) -> np.ndarray:
    """Clip to [p_low, p_high] percentiles and rescale to [0, 1]."""
    lo = float(np.percentile(arr, p_low))
    hi = float(np.percentile(arr, p_high))
    if hi - lo < EPSILON:
        return np.zeros_like(arr, dtype=np.float32)
    clipped = np.clip(arr, lo, hi)
    return ((clipped - lo) / (hi - lo)).astype(np.float32)


# ---------------------------------------------------------------------------
# HEALPix -> equirectangular reprojection
# ---------------------------------------------------------------------------

def healpix_to_equirect(healpix_map: np.ndarray,
                         nside: int,
                         width: int,
                         height: int) -> np.ndarray:
    """Reproject a HEALPix map onto an equirectangular grid.

    Projection convention (matches CMB skybox UV space):
        theta (colatitude) = (1 - v/H) * pi
        phi   (azimuth)    = (u/W) * 2*pi

    Returns float32 array of shape (height, width).
    """
    # Build index arrays for all (u, v) pixel centres
    u_idx = np.arange(width,  dtype=np.float64)
    v_idx = np.arange(height, dtype=np.float64)
    uu, vv = np.meshgrid(u_idx, v_idx)  # shape (H, W)

    theta = (1.0 - (vv + 0.5) / height) * math.pi
    phi   = ((uu + 0.5) / width)  * 2.0 * math.pi

    # Vectorised ang2pix
    pix = hp.ang2pix(nside, theta.ravel(), phi.ravel(), nest=False)
    output = healpix_map[pix].reshape(height, width).astype(np.float32)
    return output


# ---------------------------------------------------------------------------
# Gaussian splash for anchor nodes
# ---------------------------------------------------------------------------

def paint_anchor_splash(canvas: np.ndarray,
                         u: float, v: float,
                         sigma_px: float,
                         amplitude: float,
                         width: int, height: int) -> None:
    """Paint a 2D gaussian splash centred at (u, v) onto canvas in-place.

    Handles wraparound at the u (longitude) boundary.
    """
    # Bounding box to avoid iterating the whole image
    radius = int(math.ceil(sigma_px * 4))
    u0, v0 = int(round(u)), int(round(v))

    for dv in range(-radius, radius + 1):
        vi = v0 + dv
        if vi < 0 or vi >= height:
            continue
        for du in range(-radius, radius + 1):
            ui = (u0 + du) % width   # wrap longitude
            dist2 = float(du * du + dv * dv)
            val = amplitude * math.exp(-dist2 / (2.0 * sigma_px * sigma_px))
            canvas[vi, ui] = max(float(canvas[vi, ui]), val)


# ---------------------------------------------------------------------------
# Observer frame echo (MRS NESBITT bearing check)
# ---------------------------------------------------------------------------

def echo_observer_frame(epoch_frame_path: Path) -> None:
    """Load and print MRS NESBITT's bearing from epoch_frame.json.

    MRS NESBITT = CMB dipole apex = delta_A=0 forward bearing.
    Printed on each run so Scripts 4 and 5 are verifiably consistent.
    """
    if not epoch_frame_path.exists():
        print("  [observer_frame] epoch_frame.json not found -- run Script 4 first.")
        return

    with open(epoch_frame_path, "r", encoding="utf-8") as f:
        frame = json.load(f)

    obs = frame.get("observer_frame")
    if not obs:
        print("  [observer_frame] No observer_frame block in epoch_frame.json.")
        return

    fwd  = obs.get("forward", {})
    hc   = obs.get("handedness_check", {})
    print(f"  MRS NESBITT (delta_A=0 forward):")
    print(f"    lon={fwd.get('lon_deg', '?'):.3f}°  lat={fwd.get('lat_deg', '?'):.3f}°")
    print(f"    expected delta_A={hc.get('expected_delta_a_deg', '?')}°  "
          f"tolerance=±{hc.get('tolerance_deg', '?')}°")
    print(f"    {hc.get('description', '')}")

    quads = obs.get("quadrants", {})
    if quads:
        print("  Quadrants:")
        for name, q in quads.items():
            print(f"    {name.upper():6s}  delta_A {q['delta_a_min_deg']:5.0f}° – "
                  f"{q['delta_a_max_deg']:5.0f}°   {q['orientation']:16s}  {q['description']}")


# ---------------------------------------------------------------------------
# Single heatmap generation
# ---------------------------------------------------------------------------

def generate_heatmap(
    alm: np.ndarray,
    lmax: int,
    nside: int,
    width: int,
    height: int,
    label: str,
    anchor_nodes: Optional[np.ndarray] = None,
) -> np.ndarray:
    """Reconstruct alm -> HEALPix map -> equirectangular -> normalised float32.

    Parameters
    ----------
    alm          : complex128 alm array for the target class
    lmax         : band limit stored in alm_by_class.npz
    nside        : HEALPix resolution for reconstruction
    width/height : equirectangular output resolution
    label        : human-readable name for progress printing
    anchor_nodes : optional float32 (N, 3) unit vectors for HEEGNER_ANCHOR
                   gaussian splashes (heegner heatmap only)

    Returns
    -------
    float32 array of shape (height, width), values in [0, 1] except where
    anchor splashes push above 1.0 (intentional HDR for anchor visibility).
    """
    print(f"  Reconstructing {label} pixel map  (Nside={nside}) ...")
    pixel_map = hp.alm2map(alm, nside=nside, lmax=lmax)
    pixel_map = np.abs(pixel_map).astype(np.float64)

    print(f"  Normalising {label} (p1/p99) ...")
    normalised = percentile_normalise(pixel_map)

    print(f"  Reprojecting {label} to equirectangular {width}x{height} ...")
    canvas = healpix_to_equirect(normalised, nside, width, height)

    if anchor_nodes is not None and len(anchor_nodes) > 0:
        print(f"  Painting {len(anchor_nodes)} HEEGNER_ANCHOR splashes "
              f"(sigma={ANCHOR_SIGMA_PX}px, amp={ANCHOR_AMPLITUDE}) ...")
        for node_vec in anchor_nodes:
            # Convert unit vector to angular coords
            x, y, z = float(node_vec[0]), float(node_vec[1]), float(node_vec[2])
            z = float(np.clip(z, -1.0, 1.0))
            theta = math.acos(z)           # colatitude
            phi   = math.atan2(y, x) % (2 * math.pi)
            # Map to pixel coordinates
            u = (phi   / (2 * math.pi)) * width
            v = (1.0 - theta / math.pi)  * height
            paint_anchor_splash(canvas, u, v,
                                 ANCHOR_SIGMA_PX, ANCHOR_AMPLITUDE,
                                 width, height)

    return canvas


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def generate(
    alm_path: Path,
    nodes_path: Path,
    epoch_frame_path: Path,
    heegner_out: Path,
    void_out: Path,
    nside: int = DEFAULT_NSIDE,
    width: int = DEFAULT_WIDTH,
    height: int = DEFAULT_HEIGHT,
) -> None:

    # Observer frame sanity check first
    print("Observer frame (MRS NESBITT bearing check):")
    echo_observer_frame(epoch_frame_path)
    print()

    # Load alm data
    print(f"Loading alm_by_class : {alm_path}")
    alm_data    = np.load(alm_path)
    lmax        = int(alm_data["lmax"])
    heegner_alm = alm_data["heegner_alm"]

    # Void: prefer explicit void_prime_alm; fall back to void_alm; else zeros
    if "void_prime_alm" in alm_data:
        void_alm = alm_data["void_prime_alm"]
        void_label = "VOID_PRIME"
    elif "void_alm" in alm_data:
        void_alm = alm_data["void_alm"]
        void_label = "VOID (composite fallback)"
        print("  WARNING: void_prime_alm not found; using void_alm as fallback")
    else:
        soliton_alm = alm_data.get("soliton_alm", np.zeros_like(heegner_alm))
        full_alm    = alm_data.get("full_alm",    np.zeros_like(heegner_alm))
        void_alm    = full_alm - heegner_alm - soliton_alm
        void_label  = "VOID (derived from full - heegner - soliton)"
        print(f"  WARNING: deriving void_alm: {void_label}")

    # Load HEEGNER_ANCHOR node directions for gaussian splashes
    anchor_nodes: Optional[np.ndarray] = None
    if nodes_path.exists():
        print(f"Loading nodes        : {nodes_path}")
        nodes_data  = np.load(nodes_path)
        node_class  = nodes_data["node_class"]           # int8 (N,)
        directions  = nodes_data["directions"]            # float32 (N, 3)
        anchor_mask = (node_class == NODE_CLASS_HEEGNER_ANCHOR)
        anchor_nodes = directions[anchor_mask]
        print(f"  {int(anchor_mask.sum())} HEEGNER_ANCHOR nodes loaded")
    else:
        print(f"  WARNING: nodes.npz not found at {nodes_path}; "
              f"anchor splashes will be skipped")

    # ---- Heegner heatmap ------------------------------------------------
    print(f"\nGenerating HEEGNER heatmap ...")
    heegner_canvas = generate_heatmap(
        heegner_alm, lmax, nside, width, height,
        label="HEEGNER_LOCKED",
        anchor_nodes=anchor_nodes,
    )
    write_exr(heegner_out, heegner_canvas)

    # ---- Void heatmap ---------------------------------------------------
    print(f"\nGenerating VOID heatmap ({void_label}) ...")
    void_canvas = generate_heatmap(
        void_alm, lmax, nside, width, height,
        label=void_label,
        anchor_nodes=None,   # void map has no anchor overlays
    )
    write_exr(void_out, void_canvas)

    # ---- Summary --------------------------------------------------------
    print("\nDone.")
    print(f"  heegner_heatmap : {heegner_out}")
    print(f"  void_heatmap    : {void_out}")
    print(f"  Resolution      : {width} x {height}  (Nside={nside})")
    print(f"  Anchor splashes : {len(anchor_nodes) if anchor_nodes is not None else 0}")
    print()
    print("Unity import settings for both EXRs:")
    print("  Texture Type   : Default")
    print("  Format         : R Float  (single-channel float32)")
    print("  sRGB           : OFF")
    print("  Wrap Mode      : Repeat  (equirectangular wraparound)")
    print("  Filter Mode    : Bilinear")
    print("Sample in shader : float v = SAMPLE_TEXTURE2D(_HeegnerHeatmap, sampler_HeegnerHeatmap, uv).r;")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Generate boundary heatmap EXR textures (Script 5)"
    )
    parser.add_argument("--nside",  type=int, default=DEFAULT_NSIDE,
                        help=f"HEALPix Nside for alm reconstruction (default {DEFAULT_NSIDE})")
    parser.add_argument("--width",  type=int, default=DEFAULT_WIDTH,
                        help=f"EXR output width  (default {DEFAULT_WIDTH})")
    parser.add_argument("--height", type=int, default=DEFAULT_HEIGHT,
                        help=f"EXR output height (default {DEFAULT_HEIGHT})")
    args = parser.parse_args()

    script_dir = Path(__file__).resolve().parent
    repo_root  = script_dir.parent
    paths      = default_paths(repo_root)

    generate(
        alm_path=paths["alm"],
        nodes_path=paths["nodes"],
        epoch_frame_path=paths["epoch_frame"],
        heegner_out=paths["heegner_out"],
        void_out=paths["void_out"],
        nside=args.nside,
        width=args.width,
        height=args.height,
    )
