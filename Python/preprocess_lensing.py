"""preprocess_lensing.py

Converts the Planck 2018 Minimum Variance lensing convergence map (kappa)
from spherical harmonic coefficients (alm) to an equirectangular PNG
suitable for use as the INNER BOUNDARY texture of the observer bubble.

The lensing kappa map encodes all matter (dark + baryonic) between us
and the CMB surface (z=1090). It is the CMB encoding its own inner
boundary condition through gravitational lensing -- the outer boundary
produces the inner boundary. Self-referential and physically exact.

Input files (in Assets/CMB/Data/Lensing/):
  planck_lensing_kappa_MV.fits   -- kappa_lm spherical harmonic coefficients
  planck_lensing_mask.fits.gz    -- sky mask (optional but recommended)

Output:
  Assets/CMB/Textures/lensing_inner_boundary.png  -- 4096x2048 equirectangular
  Assets/CMB/Textures/lensing_inner_boundary_masked.png  -- mask applied

Steps:
  1. Load alm coefficients from FITS
  2. Convert alm -> HEALPix pixel map via alm2map()
  3. Apply sky mask (zero out contaminated pixels)
  4. Reproject HEALPix -> equirectangular grid
  5. Normalise to [0, 1] with robust percentile clip
  6. Apply false-colour (viridis: dark=low kappa, yellow=high kappa)
  7. Save PNGs

Run from repo root or Python/ folder:
  conda activate D:\\envs\\cmb
  cd "D:\\Unity Projects\\infinite-improbability-drive\\Python"
  python preprocess_lensing.py
"""

import os
import numpy as np
import healpy as hp
from PIL import Image
import matplotlib.cm as cm

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
SCRIPT_DIR  = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT   = os.path.dirname(SCRIPT_DIR)

LENSING_DIR = os.path.join(REPO_ROOT, "Assets", "CMB", "Data", "Lensing")
ALM_PATH    = os.path.join(LENSING_DIR, "planck_lensing_kappa_MV.fits")
MASK_PATH   = os.path.join(LENSING_DIR, "planck_lensing_mask.fits.gz")

OUT_DIR     = os.path.join(REPO_ROOT, "Assets", "CMB", "Textures")
OUT_PNG     = os.path.join(OUT_DIR, "lensing_inner_boundary.png")
OUT_MASKED  = os.path.join(OUT_DIR, "lensing_inner_boundary_masked.png")

# Output resolution -- same as CMB map for pixel-perfect double-zenith blending
OUT_W, OUT_H = 4096, 2048

# Target Nside for the pixel map reconstruction
# Nside=2048 matches the CMB map; use 1024 for a faster first run
NSIDE_OUT = 2048

# ---------------------------------------------------------------------------
# 1. Load alm coefficients
# ---------------------------------------------------------------------------
print(f"Loading lensing alm from:\n  {ALM_PATH}")
if not os.path.exists(ALM_PATH):
    raise FileNotFoundError(
        f"Lensing FITS not found at:\n  {ALM_PATH}\n"
        "Copy MV/dat_klm.fits to Assets/CMB/Data/Lensing/ and rename."
    )

# dat_klm.fits stores kappa_lm as spherical harmonic coefficients.
# hp.read_alm returns complex alm array ordered by healpy convention.
# The file may contain multiple columns (e.g. kappa + curl modes);
# field=0 selects kappa (the physical lensing convergence).
try:
    alm_kappa = hp.read_alm(ALM_PATH, hdu=1)
    print(f"Loaded alm  |  lmax={hp.Alm.getlmax(len(alm_kappa))}  |  nalm={len(alm_kappa)}")
except Exception as e:
    # Some releases store alm differently -- try field-based read
    print(f"  hdu=1 failed ({e}), trying read_map fallback...")
    alm_kappa = hp.read_alm(ALM_PATH)
    print(f"Loaded alm  |  lmax={hp.Alm.getlmax(len(alm_kappa))}  |  nalm={len(alm_kappa)}")

# ---------------------------------------------------------------------------
# 2. alm -> HEALPix pixel map
# ---------------------------------------------------------------------------
print(f"Converting alm -> HEALPix pixel map (Nside={NSIDE_OUT}) ...")
kappa_map = hp.alm2map(alm_kappa, nside=NSIDE_OUT, verbose=True)
print(f"  kappa range: [{kappa_map.min():.4e}, {kappa_map.max():.4e}]")
print(f"  kappa mean:  {kappa_map.mean():.4e}  (should be ~0)")

# ---------------------------------------------------------------------------
# 3. Load and apply sky mask
# ---------------------------------------------------------------------------
mask = None
if os.path.exists(MASK_PATH):
    print(f"Loading sky mask from:\n  {MASK_PATH}")
    mask_raw = hp.read_map(MASK_PATH, dtype=np.float64, verbose=False)

    # Upgrade or downgrade mask to match kappa Nside if needed
    mask_nside = hp.get_nside(mask_raw)
    if mask_nside != NSIDE_OUT:
        print(f"  Resampling mask from Nside={mask_nside} -> {NSIDE_OUT}")
        mask_raw = hp.ud_grade(mask_raw, NSIDE_OUT)

    # Binary mask: pixels with mask > 0.5 are valid
    mask = mask_raw > 0.5
    n_valid = mask.sum()
    fsky = n_valid / len(mask)
    print(f"  Valid pixels: {n_valid:,} / {len(mask):,}  (f_sky = {fsky:.3f})")
else:
    print("  No mask file found -- proceeding without mask.")

# ---------------------------------------------------------------------------
# 4. HEALPix -> equirectangular reprojection
# ---------------------------------------------------------------------------
print(f"Reprojecting to {OUT_W}x{OUT_H} equirectangular ...")

lons = np.linspace(0,      2 * np.pi, OUT_W, endpoint=False)
lats = np.linspace(np.pi, 0,          OUT_H, endpoint=False)
phi_grid, theta_grid = np.meshgrid(lons, lats)

pix      = hp.ang2pix(NSIDE_OUT, theta_grid.ravel(), phi_grid.ravel())
equirect = kappa_map[pix].reshape(OUT_H, OUT_W)

# Masked version: zero out invalid pixels
if mask is not None:
    mask_equirect = mask[pix].reshape(OUT_H, OUT_W)
else:
    mask_equirect = np.ones((OUT_H, OUT_W), dtype=bool)

equirect_masked = equirect.copy()
equirect_masked[~mask_equirect] = np.nan

# ---------------------------------------------------------------------------
# 5. Normalise to [0, 1]
# ---------------------------------------------------------------------------
def normalise(arr, label=""):
    """Robust percentile normalisation, NaN-safe."""
    finite = arr[np.isfinite(arr)]
    vmin, vmax = np.percentile(finite, [0.5, 99.5])
    print(f"  {label} range (0.5-99.5 pct): [{vmin:.4e}, {vmax:.4e}]")
    normed = np.clip((arr - vmin) / (vmax - vmin), 0, 1)
    normed = np.where(np.isfinite(arr), normed, 0.0)  # NaN -> 0 (masked black)
    return normed

print("Normalising full map ...")
norm_full   = normalise(equirect,        label="kappa (full)")
print("Normalising masked map ...")
norm_masked = normalise(equirect_masked, label="kappa (masked)")

# ---------------------------------------------------------------------------
# 6. False-colour
# Viridis: dark purple = low kappa (voids), yellow = high kappa (mass halos)
# Complements the CMB RdBu_r colourmap visually in the double-zenith blend.
# ---------------------------------------------------------------------------
print("Applying false-colour (viridis) ...")
cmap = cm.get_cmap("viridis")

def to_rgb(norm_arr):
    return (cmap(norm_arr)[:, :, :3] * 255).astype(np.uint8)

colour_full   = to_rgb(norm_full)
colour_masked = to_rgb(norm_masked)

# ---------------------------------------------------------------------------
# 7. Save
# ---------------------------------------------------------------------------
os.makedirs(OUT_DIR, exist_ok=True)

Image.fromarray(colour_full,   mode="RGB").save(OUT_PNG,    format="PNG")
Image.fromarray(colour_masked, mode="RGB").save(OUT_MASKED, format="PNG")

print(f"\nSaved full:   {OUT_PNG}")
print(f"Saved masked: {OUT_MASKED}")
print("""
Done.

Next steps:
  1. Import both PNGs into Unity (Assets/CMB/Textures/)
  2. Set Texture Shape -> 2D, Wrap Mode -> Clamp on both
  3. The masked PNG is the inner boundary texture for InnerBoundary.cs
  4. The full PNG is useful for debugging / visualisation

The lensing kappa map and the CMB temperature map are now both
4096x2048 equirectangular PNGs in Galactic coordinates.
The double-zenith volume sits between them.
""")
