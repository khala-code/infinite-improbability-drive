# preprocess_milky_way.py
# Converts Gaia DR3 PSV.GZ → HEALPix stellar density map → equirectangular PNG
# 23.5M stars, processed in chunks to manage RAM

import numpy as np
import pandas as pd
import healpy as hp
from astropy.coordinates import SkyCoord, Galactocentric
import astropy.units as u
from astropy.coordinates import Galactic
from PIL import Image
import gzip
import os

# ── Paths ──────────────────────────────────────────────────────────────────────
BASE     = r"D:\Unity Projects\infinite-improbability-drive\Assets\CMB"
INPUT    = os.path.join(BASE, r"Data\MilkyWay\result_gaiadr3.psv.gz")
OUT_FULL = os.path.join(BASE, r"Textures\milkyway_stellar_density.png")
OUT_MASK = os.path.join(BASE, r"Textures\milkyway_stellar_density_masked.png")

# ── HEALPix config ─────────────────────────────────────────────────────────────
NSIDE      = 2048
NPIX       = hp.nside2npix(NSIDE)
CHUNK_SIZE = 500_000

density_map = np.zeros(NPIX, dtype=np.float64)

# ── Process in chunks ──────────────────────────────────────────────────────────
print("Reading Gaia DR3 data in chunks...")
total_processed = 0

reader = pd.read_csv(
    INPUT,
    sep='|',
    compression='gzip',
    usecols=['ra', 'dec', 'parallax'],
    chunksize=CHUNK_SIZE,
    comment='#'
)

for i, chunk in enumerate(reader):
    # Drop NaN and zero/negative parallax (already filtered but be safe)
    chunk = chunk.dropna(subset=['ra', 'dec', 'parallax'])
    chunk = chunk[chunk['parallax'] > 0]

    # Convert ra/dec → Galactic l, b
    coords = SkyCoord(
        ra=chunk['ra'].values * u.degree,
        dec=chunk['dec'].values * u.degree,
        frame='icrs'
    ).galactic

    l_rad = coords.l.rad
    b_rad = coords.b.rad

    # HEALPix pixel indices (RING scheme, Galactic coords)
    theta = np.pi / 2 - b_rad   # colatitude
    phi   = l_rad

    pix = hp.ang2pix(NSIDE, theta, phi, nest=False)
    np.add.at(density_map, pix, 1)

    total_processed += len(chunk)
    print(f"  Chunk {i+1}: {total_processed:,} stars processed...")

print(f"\nTotal stars mapped: {total_processed:,}")

# ── Normalise ──────────────────────────────────────────────────────────────────
print("Normalising density map...")
density_map = np.log1p(density_map)                     # log scale — huge dynamic range otherwise
density_map = (density_map / density_map.max() * 255).astype(np.uint8)

# ── HEALPix → equirectangular 4096×2048 ───────────────────────────────────────
print("Projecting to equirectangular...")
W, H = 4096, 2048

lon = np.linspace(0, 2 * np.pi, W, endpoint=False)     # Galactic longitude 0→360
lat = np.linspace(np.pi / 2, -np.pi / 2, H)            # Galactic latitude +90→-90

lon_grid, lat_grid = np.meshgrid(lon, lat)

theta_grid = np.pi / 2 - lat_grid
phi_grid   = lon_grid

pix_grid = hp.ang2pix(NSIDE, theta_grid, phi_grid, nest=False)
img_array = density_map[pix_grid].reshape(H, W)

# ── Save full PNG ──────────────────────────────────────────────────────────────
img_full = Image.fromarray(img_array, mode='L').convert('RGBA')
img_full.save(OUT_FULL)
print(f"Saved full:   {OUT_FULL}")

# ── Masked PNG (alpha = density, black where empty) ────────────────────────────
rgba = np.zeros((H, W, 4), dtype=np.uint8)
rgba[:, :, 0] = img_array   # R
rgba[:, :, 1] = img_array   # G
rgba[:, :, 2] = img_array   # B
rgba[:, :, 3] = img_array   # A — transparent where no stars

img_masked = Image.fromarray(rgba, mode='RGBA')
img_masked.save(OUT_MASK)
print(f"Saved masked: {OUT_MASK}")

print("\nDone.")
print("\nNext steps:")
print("  1. Import both PNGs into Unity (Assets/CMB/Textures/)")
print("  2. Texture Shape → Cube, Mapping → Latitude-Longitude Layout, Wrap Mode → Clamp")
print("  3. The masked PNG is the stellar density texture for MilkyWayBoundary.cs")
print("  4. The full PNG is useful for debugging / visualisation")