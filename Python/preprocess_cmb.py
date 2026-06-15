"""preprocess_cmb.py

Converts the Planck SMICA HEALPix FITS map to an equirectangular PNG
suitable for use as a Unity skybox texture.

Steps:
  1. Load FITS map
  2. Remove monopole (l=0) and dipole (l=1)
  3. Convert HEALPix -> equirectangular grid
  4. Normalise to [0, 1]
  5. Apply false-colour (Planck blue-red colourmap)
  6. Save PNG to Assets/CMB/Textures/

Run from repo root or Python/ folder:
  conda activate D:\\envs\\cmb
  cd "D:\\Unity Projects\\infinite-improbability-drive\\Python"
  python preprocess_cmb.py
"""

import os
import numpy as np
import healpy as hp
from PIL import Image
import matplotlib.cm as cm

# ---------------------------------------------------------------------------
# Paths  (relative to this script's location)
# ---------------------------------------------------------------------------
SCRIPT_DIR   = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT    = os.path.dirname(SCRIPT_DIR)

FITS_PATH    = os.path.join(REPO_ROOT, "Assets", "CMB", "Data",
                             "COM_CMB_IQU-smica_2048_R3.00_full.fits")
OUT_DIR      = os.path.join(REPO_ROOT, "Assets", "CMB", "Textures")
OUT_PNG      = os.path.join(OUT_DIR, "cmb_equirectangular.png")

# Output resolution  (width x height, must be 2:1 ratio)
# 4096x2048 is ideal for Quest 2 skybox quality
# Use 2048x1024 for a faster first run
OUT_W, OUT_H = 4096, 2048

# ---------------------------------------------------------------------------
# 1. Load
# ---------------------------------------------------------------------------
print(f"Loading FITS from:\n  {FITS_PATH}")
if not os.path.exists(FITS_PATH):
    raise FileNotFoundError(
        f"FITS file not found at:\n  {FITS_PATH}\n"
        "Check the path and re-run."
    )

# SMICA map is column 0 (temperature).  field=0 selects it.
# dtype=np.float64 keeps full precision during processing.
raw_map = hp.read_map(FITS_PATH, field=0, dtype=np.float64, verbose=True)
nside   = hp.get_nside(raw_map)
print(f"Loaded map  |  Nside={nside}  |  Npix={hp.nside2npix(nside)}")

# ---------------------------------------------------------------------------
# 2. Remove monopole (l=0) and dipole (l=1)
# ---------------------------------------------------------------------------
print("Removing monopole and dipole ...")
alm          = hp.map2alm(raw_map, lmax=3)   # only need low-l for removal
alm[0]       = 0.0                            # monopole
alm[1:4]     = 0.0                            # dipole  (m=0,1,-1)

# Reconstruct clean map at full resolution
clean_map = raw_map - hp.alm2map(alm, nside)
print(f"  Mean after removal: {clean_map.mean():.6e}  (should be ~0)")

# ---------------------------------------------------------------------------
# 3. HEALPix -> equirectangular
# ---------------------------------------------------------------------------
print(f"Reprojecting to {OUT_W}x{OUT_H} equirectangular ...")

# Build (theta, phi) grid
# theta: colatitude [0, pi],  phi: longitude [0, 2*pi]
lons = np.linspace(0,       2 * np.pi, OUT_W, endpoint=False)   # phi
lats = np.linspace(np.pi,  0,          OUT_H, endpoint=False)   # theta (N->S)
phi_grid, theta_grid = np.meshgrid(lons, lats)

# Query HEALPix map at each pixel
pix = hp.ang2pix(nside, theta_grid.ravel(), phi_grid.ravel())
equirect = clean_map[pix].reshape(OUT_H, OUT_W)

# ---------------------------------------------------------------------------
# 4. Normalise to [0, 1]
# ---------------------------------------------------------------------------
vmin, vmax = np.percentile(equirect, [0.5, 99.5])   # robust clip
print(f"  Temperature range (0.5-99.5 percentile): [{vmin:.4e}, {vmax:.4e}] K")
norm = np.clip((equirect - vmin) / (vmax - vmin), 0, 1)

# ---------------------------------------------------------------------------
# 5. False-colour  (RdBu_r: blue=cold, red=hot — standard Planck palette)
# ---------------------------------------------------------------------------
print("Applying false-colour (RdBu_r) ...")
cmap   = cm.get_cmap("RdBu_r")
colour = (cmap(norm)[:, :, :3] * 255).astype(np.uint8)   # drop alpha, -> uint8

# ---------------------------------------------------------------------------
# 6. Save
# ---------------------------------------------------------------------------
os.makedirs(OUT_DIR, exist_ok=True)
img = Image.fromarray(colour, mode="RGB")
img.save(OUT_PNG, format="PNG", optimize=False)
print(f"\nSaved: {OUT_PNG}")
print("Done. Open Unity and assign this texture to a skybox material.")
