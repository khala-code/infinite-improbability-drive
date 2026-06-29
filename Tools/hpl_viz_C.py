#!/usr/bin/env python3
"""
hpl_viz_C.py  --  Real-data HPL epoch stillframes
Reads particle_buffer.bin directly from your Unity StreamingAssets path.

Actual ParticleRecord struct layout (96 bytes = 24 x float32) as written
by generate_particle_buffer.py (verified by field-range inspection 2026-06-29):

  [0:3]   Position       (float3)  -- Galactic Cartesian, X→GC, Z→NGP
  [3:6]   Velocity       (float3)
  [6:10]  Colour         (float4)  -- RGBA baked at generation time
  [10]    HeegnerPower   (float)   range ~0.22–1.0
  [11]    SolitonDensity (float)   range ~0.51–1.0
  [12]    VoidPressure   (float)   range ~0–0.003
  [13]    Kappa          (float)   range ~0.32–1.36 (unnormalised — clamp to 1)
  [14:18] unknown/padding (5 floats)
  [18]    ParityWeight   (float)   range ~0.03–1.0
  [19]    ParticleClass  (float32 encoded int: 0=Soliton 1=Void 2=Heegner)
  [20:24] padding

NOTE: HolographicParticleLayer.cs struct spec has ParticleClass at offset 14.
      The Python writer placed it at offset 19. Unity-side fix pending —
      see Docs/data_index.txt. The viz script uses the verified Python offsets.

Mirrors the epoch physics in HolographicParticleLayer.cs DispatchComputeUpdate().

Usage:
  python hpl_viz_C.py
  python hpl_viz_C.py --bin "D:/custom/path/particle_buffer.bin" --epochs 12 --mode mollweide
  python hpl_viz_C.py --mode azimuthal --sheet
  python hpl_viz_C.py --mode radial --sheet --out ./frames
  python hpl_viz_C.py --mode all --sheet --xi 0.8 --out ./frames

Dependencies:  numpy  matplotlib  scipy
"""

import argparse, sys, os
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.colors as mcolors
from matplotlib.colors import Normalize

# ── Config ─────────────────────────────────────────────────────────────────────

DEFAULT_BIN = r"D:\Unity Projects\infinite-improbability-drive\Assets\StreamingAssets\CMB\particle_buffer.bin"
STRIDE      = 96
N_FLOATS    = 24
HEEGNER_THRESHOLDS = [0.15, 0.38, 0.61, 0.84]

# Verified field offsets (generate_particle_buffer.py layout)
F_HEEGNER_POWER   = 10
F_SOLITON_DENSITY = 11
F_VOID_PRESSURE   = 12
F_KAPPA           = 13
F_PARITY_WEIGHT   = 18
F_PARTICLE_CLASS  = 19

CLASS_COLOURS = {0: "#00e5ff", 1: "#ff6d00", 2: "#ffd600"}
CLASS_NAMES   = {0: "Soliton", 1: "Void",    2: "Heegner"}
H = 0.100; V = 0.550; S = 0.350   # field scalars (matches field_scalars.json)

# Radial phase field parameters
RADIAL_FREQ_OUTER = 6.0   # outer ring frequency (controls arm density)
RADIAL_FREQ_INNER = 18.0  # inner ring frequency (controls centre swirl tightness)
RADIAL_ARMS       = 3     # number of spiral arms (Heegner-number feel)


# ── Struct reader ───────────────────────────────────────────────────────────────

def load_particles(path):
    with open(path, "rb") as f:
        raw = f.read()
    n = len(raw) // STRIDE
    if n == 0:
        sys.exit("particle_buffer.bin is empty or path wrong.")
    data   = np.frombuffer(raw, dtype=np.float32).reshape(n, N_FLOATS).copy()
    pclass = np.round(data[:, F_PARTICLE_CLASS]).astype(np.int32)
    return data, pclass, n


# ── Projections ─────────────────────────────────────────────────────────────────

def gal_to_lonlat(xyz):
    """Galactic Cartesian (X→GC, Z→NGP) → (lon, lat) in radians."""
    r   = np.linalg.norm(xyz, axis=1).clip(1e-9)
    lat = np.arcsin(np.clip(xyz[:, 2] / r, -1, 1))
    lon = np.arctan2(xyz[:, 1], xyz[:, 0])
    return lon, lat

def mollweide(lon, lat):
    """Mollweide equal-area projection."""
    from scipy.optimize import brentq
    def th(b):
        if abs(b) >= np.pi/2 - 1e-9:
            return np.sign(b) * np.pi/2
        try:    return brentq(lambda t: 2*t + np.sin(2*t) - np.pi*np.sin(b), -np.pi/2, np.pi/2)
        except: return b
    tv  = np.vectorize(th)(lat)
    px  = (2*np.sqrt(2)/np.pi) * lon * np.cos(tv)
    py  = np.sqrt(2) * np.sin(tv)
    return px, py, np.ones(len(lon), bool)

def azimuthal(lon, lat):
    """Lambert azimuthal equal-area from North Galactic Pole."""
    rho = np.cos(lat).clip(0)
    return rho*np.cos(lon), rho*np.sin(lon), np.ones(len(lon), bool)

def orthographic(lon, lat, lon0=0., lat0=np.pi/4):
    """Orthographic (globe view) centred on (lon0, lat0)."""
    px  = np.cos(lat)*np.sin(lon - lon0)
    py  = np.cos(lat0)*np.sin(lat) - np.sin(lat0)*np.cos(lat)*np.cos(lon - lon0)
    vis = (np.sin(lat0)*np.sin(lat) + np.cos(lat0)*np.cos(lat)*np.cos(lon - lon0)) > 0
    return px, py, vis

PROJECTORS = {"mollweide": mollweide, "azimuthal": azimuthal, "ortho": orthographic}


# ── Radial phase field ──────────────────────────────────────────────────────────

def build_radial_phase_field(data, pclass, t, grid_res=600):
    """
    Build two 2D phase fields on a unit-disk grid using azimuthal projection.

    Outward field:  HeegnerPower * cos(r * freq_outer - t*2π*arms + azimuth*arms)
                  + VoidPressure_norm * sin(r * freq_inner + t*2π*2)
    Inward field:   mirror — freq signs flipped, creates counter-rotating interference

    Returns:
        outward  (H, W) float  -- raw interference amplitude [-1, 1]
        inward   (H, W) float
        px, py   particle screen coords (azimuthal)
        alpha    per-particle alpha
        size     per-particle size
    """
    lon, lat = gal_to_lonlat(data[:, :3])
    px_p, py_p, _ = azimuthal(lon, lat)

    hp  = data[:, F_HEEGNER_POWER]
    vp  = data[:, F_VOID_PRESSURE]
    vp_n = (vp / vp.max()).clip(0, 1)

    # Grid
    gv    = np.linspace(-1.0, 1.0, grid_res)
    gx, gy = np.meshgrid(gv, gv)
    gr    = np.sqrt(gx**2 + gy**2)
    gaz   = np.arctan2(gy, gx)   # azimuthal angle on disk
    mask  = gr <= 1.0

    # Phase field from scatter → grid via particle contributions
    # Each particle deposits a radial wave kernel weighted by its field values
    # We bin particles into the grid using a weighted histogram approach

    def scatter_to_grid(weights, px_arr, py_arr, res):
        """Weighted scatter accumulation onto a grid."""
        # Map [-1,1] → [0, res-1]
        xi = ((px_arr + 1.0) * 0.5 * (res - 1)).clip(0, res - 1).astype(int)
        yi = ((py_arr + 1.0) * 0.5 * (res - 1)).clip(0, res - 1).astype(int)
        grid = np.zeros((res, res), dtype=np.float64)
        np.add.at(grid, (yi, xi), weights)
        return grid

    # Heegner outward wave: particles at (px, py) emit cos waves radially outward
    # Each grid point samples the superposition from all particles
    # Approximation: use grid-distance from each particle — too slow for 65k pts,
    # so instead build the field analytically on the grid using particle lon/lat
    # as the source direction (angular distance from grid point to particle direction)

    # Fast analytical approach: build field directly on grid from per-particle fields
    # Downsample to 4000 representative particles for field construction
    rng = np.random.default_rng(42)

    def field_from_particles(sel_idx, field_vals, freq, phase_sign, arms, res):
        """Accumulate radial interference from selected particles onto grid."""
        f = np.zeros((res, res), dtype=np.float64)
        # Particle azimuthal screen positions
        ppx = px_p[sel_idx]
        ppy = py_p[sel_idx]
        fv  = field_vals[sel_idx]
        for k in range(len(sel_idx)):
            dr = np.sqrt((gx - ppx[k])**2 + (gy - ppy[k])**2)
            wave = fv[k] * np.cos(dr * freq * np.pi * 2 + phase_sign * t * np.pi * 2 * arms)
            f += wave * mask
        return f

    # Sample ~3000 Heegner + 1000 Void particles for field
    h_idx = np.where(pclass == 2)[0]
    v_idx = np.where(pclass == 1)[0]
    s_idx = np.where(pclass == 0)[0]

    h_sel = rng.choice(h_idx, min(1500, len(h_idx)), replace=False)
    v_sel = rng.choice(v_idx, min(800,  len(v_idx)), replace=False)
    s_sel = rng.choice(s_idx, min(800,  len(s_idx)), replace=False)

    # Epoch boost for Heegner near thresholds
    heegner_boost = 1.0 + sum(3.0 * np.exp(-200*(t - thr)**2) for thr in HEEGNER_THRESHOLDS)

    # Outward field: Heegner drives outward spiral, Void counter-modulates
    f_heegner_out = field_from_particles(h_sel, hp * heegner_boost, RADIAL_FREQ_OUTER, +1, RADIAL_ARMS, grid_res)
    f_void_out    = field_from_particles(v_sel, vp_n * V,           RADIAL_FREQ_INNER, +1, 2,           grid_res)
    f_sol_out     = field_from_particles(s_sel, data[:, F_SOLITON_DENSITY] * S, RADIAL_FREQ_OUTER * 0.5, +1, 1, grid_res)
    outward = f_heegner_out + f_void_out * 0.4 + f_sol_out * 0.3

    # Inward field: counter-rotating — phase_sign flipped
    f_heegner_in  = field_from_particles(h_sel, hp * heegner_boost, RADIAL_FREQ_OUTER, -1, RADIAL_ARMS, grid_res)
    f_void_in     = field_from_particles(v_sel, vp_n * V,           RADIAL_FREQ_INNER, -1, 2,           grid_res)
    inward = f_heegner_in + f_void_in * 0.4

    # Normalise to [-1, 1]
    def norm11(arr):
        m = np.abs(arr[mask]).max()
        return arr / (m + 1e-9)
    outward = norm11(outward)
    inward  = norm11(inward)

    # Mask outside disk
    outward[~mask] = np.nan
    inward[~mask]  = np.nan

    alpha, size = epoch_alpha(data, pclass, t, xi=0.5)
    return outward, inward, px_p, py_p, alpha, size, gr, mask


def render_radial_epoch(fig, axes, data, pclass, t, xi=0.5, grid_res=500, max_pts=20_000):
    """
    Render dual-panel radial phase interference for a single epoch.
    axes[0]: amplitude heatmap + particle overlay
    axes[1]: phase angle (inward vs outward) HSV wheel
    """
    outward, inward, px_p, py_p, alpha, size, gr, mask = \
        build_radial_phase_field(data, pclass, t, grid_res=grid_res)

    # ── Left panel: amplitude + particles ──────────────────────────────────────
    ax0 = axes[0]
    ax0.set_facecolor("#060614")

    # Heatmap
    cmap_amp = plt.cm.RdBu_r
    im = ax0.imshow(outward, origin="lower", extent=[-1, 1, -1, 1],
                    cmap=cmap_amp, vmin=-1, vmax=1, interpolation="bilinear",
                    alpha=0.88)
    plt.colorbar(im, ax=ax0, fraction=0.03, pad=0.02,
                 label="Phase field amplitude").ax.yaxis.label.set_color("white")

    # Disk boundary ring
    theta = np.linspace(0, 2*np.pi, 360)
    ax0.plot(np.cos(theta), np.sin(theta), color="#00e5ff", lw=0.8, alpha=0.4)

    # Particle overlay (sparse)
    rng = np.random.default_rng(int(t * 1000))
    vis_idx = np.arange(len(data))
    if len(vis_idx) > max_pts:
        vis_idx = rng.choice(vis_idx, max_pts, replace=False)

    for cls in [1, 0, 2]:
        m = pclass[vis_idx] == cls
        if not m.any(): continue
        i = vis_idx[m]
        base = mcolors.to_rgb(CLASS_COLOURS[cls])
        a    = (alpha[i] * 0.6).clip(0.03, 0.7)
        rgba = np.c_[np.tile(base, (len(i), 1)), a]
        ax0.scatter(px_p[i], py_p[i], s=(size[i]*0.8)**2, c=rgba,
                    linewidths=0, rasterized=True)

    ax0.set_xlim(-1.05, 1.05); ax0.set_ylim(-1.05, 1.05)
    ax0.set_aspect("equal"); ax0.axis("off")
    ax0.set_title("Nested boundary + radial interference  (particles overlaid)",
                  color="#aaaaaa", fontsize=8, pad=4)

    # ── Right panel: phase angle wheel ─────────────────────────────────────────
    ax1 = axes[1]
    ax1.set_facecolor("#060614")

    # Phase angle: arctan2(inward, outward) → [-π, π]
    phase = np.arctan2(inward, outward)  # NaN outside disk preserved

    # Map phase → HSV: hue from angle, saturation from amplitude magnitude, value=1
    hue = (phase / (2 * np.pi) + 0.5) % 1.0      # [0,1]
    amp_mag = np.sqrt(outward**2 + inward**2) * 0.5  # [0, ~1]
    sat = np.clip(amp_mag, 0.3, 1.0)
    val = np.ones_like(hue)

    hsv_img = np.stack([hue, sat, val], axis=-1)
    # Handle NaN (outside disk)
    outside = ~mask
    hsv_img[outside] = [0, 0, 0]

    rgb_img = mcolors.hsv_to_rgb(hsv_img)
    # Re-mask outside to background colour
    bg = np.array([0.024, 0.024, 0.078])
    for c in range(3):
        ch = rgb_img[:, :, c]
        ch[outside] = bg[c]
        rgb_img[:, :, c] = ch

    ax1.imshow(rgb_img, origin="lower", extent=[-1, 1, -1, 1], interpolation="bilinear")
    ax1.plot(np.cos(theta), np.sin(theta), color="#ffffff", lw=0.6, alpha=0.3)
    ax1.set_xlim(-1.05, 1.05); ax1.set_ylim(-1.05, 1.05)
    ax1.set_aspect("equal"); ax1.axis("off")
    ax1.set_title("Phase angle: inward vs outward radial projections",
                  color="#aaaaaa", fontsize=8, pad=4)

    # Colourbar for phase angle
    sm = plt.cm.ScalarMappable(cmap=plt.cm.hsv, norm=Normalize(-np.pi, np.pi))
    sm.set_array([])
    cb = plt.colorbar(sm, ax=ax1, fraction=0.03, pad=0.02,
                      label="Phase angle (in/out interference)")
    cb.ax.yaxis.label.set_color("white")
    cb.ax.tick_params(colors="white")

    # Heegner flash border
    if any(abs(t - thr) < 0.018 for thr in HEEGNER_THRESHOLDS):
        for ax in axes:
            for sp in ax.spines.values():
                sp.set_edgecolor("#ffd600"); sp.set_linewidth(2.5)


# ── Radial sheet ────────────────────────────────────────────────────────────────

def render_radial_sheet(data, pclass, epochs, out_path, xi=0.5, grid_res=400):
    """Contact sheet of radial dual-panel frames."""
    ncols = 4
    nrows = int(np.ceil(len(epochs) / ncols))
    fig, axes_grid = plt.subplots(nrows * 2, ncols,
                                  figsize=(ncols * 6, nrows * 6.5))
    fig.patch.set_facecolor("#060614")
    axes_grid = np.array(axes_grid)

    for i, t in enumerate(epochs):
        row = (i // ncols) * 2
        col = i % ncols
        ax_pair = [axes_grid[row, col], axes_grid[row + 1, col]]
        render_radial_epoch(fig, ax_pair, data, pclass, t,
                            xi=xi, grid_res=grid_res, max_pts=15_000)
        flash = any(abs(t - thr) < 0.018 for thr in HEEGNER_THRESHOLDS)
        axes_grid[row, col].set_title(
            f"t={t:.3f}" + (" ⚡" if flash else "") + "\n" +
            "Nested boundary + radial interference  (particles overlaid)",
            color="#cccccc", fontsize=8, pad=3)
        axes_grid[row + 1, col].set_title(
            "Phase angle: inward vs outward radial projections",
            color="#aaaaaa", fontsize=8, pad=3)

    # Hide unused axes
    total = nrows * ncols
    for j in range(len(epochs), total):
        row = (j // ncols) * 2
        col = j % ncols
        axes_grid[row, col].set_visible(False)
        axes_grid[row + 1, col].set_visible(False)

    fig.suptitle(
        f"HPL Phase Interference + Nested Projections  |  epoch sheet  |  "
        f"{len(data):,} particles  |  xi={xi:.2f}",
        color="white", fontsize=12, fontweight="bold", y=1.002)

    plt.tight_layout(h_pad=0.5, w_pad=0.5)
    fig.savefig(out_path, dpi=130, bbox_inches="tight", facecolor="#060614")
    plt.close(fig)
    print(f"  saved → {out_path}")


def render_radial_individual(data, pclass, epochs, out_dir, xi=0.5, grid_res=500):
    """Full-res dual-panel radial render per epoch."""
    for t in epochs:
        fig, axes = plt.subplots(1, 2, figsize=(18, 9))
        fig.patch.set_facecolor("#060614")
        render_radial_epoch(fig, axes, data, pclass, t,
                            xi=xi, grid_res=grid_res, max_pts=20_000)
        flash = any(abs(t - thr) < 0.018 for thr in HEEGNER_THRESHOLDS)
        fig.suptitle(
            f"HPL Phase Interference + Nested Projections  |  epoch={t:.4f}"
            + ("  ⚡ HEEGNER FLASH" if flash else ""),
            color="white", fontsize=13, fontweight="bold")
        plt.tight_layout()
        fname = os.path.join(out_dir, f"hpl_radial_t{t:.3f}.png")
        fig.savefig(fname, dpi=150, bbox_inches="tight", facecolor="#060614")
        plt.close(fig)
        print(f"  saved → {fname}")


# ── Epoch physics  (mirrors DispatchComputeUpdate in HolographicParticleLayer.cs) ─

def epoch_alpha(data, pclass, t, xi=0.5):
    """Per-particle alpha and point-size at epoch t, XiCoherence xi."""
    evb = np.interp(t, [0,1], [1.8, 1.0])   # epochVoidBoost
    esb = np.interp(t, [0,1], [0.3, 1.0])   # epochSolitonBoost
    xsm = np.interp(xi, [0,1], [0.5, 1.5])  # xiSolitonMod
    xvm = np.interp(xi, [0,1], [1.5, 0.5])  # xiVoidMod

    hp = data[:, F_HEEGNER_POWER]
    vp = data[:, F_VOID_PRESSURE]
    sd = data[:, F_SOLITON_DENSITY]

    is_s = (pclass==0).astype(float)
    is_v = (pclass==1).astype(float)
    is_h = (pclass==2).astype(float)

    bright = (is_s * S * sd * esb * xsm +
              is_v * V * vp * evb * xvm +
              is_h * H * hp * (1.0 + 0.5*np.sin(t*6.28*4)))

    # VoidPressure is very small (~0–0.003) — boost so Voids are visible
    bright = np.where(is_v > 0, is_v * V * (vp / vp.max()).clip(0,1) * evb * xvm, bright)

    # Heegner flash amplification near Omega threshold crossings
    for thr in HEEGNER_THRESHOLDS:
        bright += is_h * np.exp(-200*(t-thr)**2) * 2.0

    alpha = np.clip(bright, 0.0, 1.0)
    size  = 0.4 + 1.8 * alpha
    return alpha, size


# ── Core render ─────────────────────────────────────────────────────────────────

def render_epoch(ax, data, pclass, t, proj_fn, xi=0.5, max_pts=120_000, rng=None):
    lon, lat = gal_to_lonlat(data[:, :3])
    px, py, vis = proj_fn(lon, lat)
    alpha, size = epoch_alpha(data, pclass, t, xi)

    idx = np.where(vis)[0]
    if len(idx) > max_pts:
        if rng is None: rng = np.random.default_rng(42)
        idx = rng.choice(idx, max_pts, replace=False)

    for cls in [1, 0, 2]:   # draw order: Void → Soliton → Heegner
        m = pclass[idx] == cls
        if not m.any(): continue
        i = idx[m]
        base = mcolors.to_rgb(CLASS_COLOURS[cls])
        a    = alpha[i].clip(0.04, 1.0)
        rgba = np.c_[np.tile(base, (len(i),1)), a]
        ax.scatter(px[i], py[i], s=size[i]**2, c=rgba, linewidths=0, rasterized=True)

    # Gold border on Heegner flash frames
    if any(abs(t - thr) < 0.018 for thr in HEEGNER_THRESHOLDS):
        for sp in ax.spines.values():
            sp.set_edgecolor("#ffd600"); sp.set_linewidth(2.5)

    ax.set_aspect("equal"); ax.set_facecolor("#060614"); ax.axis("off")


# ── Contact sheet (non-radial) ──────────────────────────────────────────────────

def render_sheet(data, pclass, epochs, proj_name, out_path, xi=0.5, max_pts=80_000):
    fn    = PROJECTORS[proj_name]
    ncols = 4
    nrows = int(np.ceil(len(epochs)/ncols))
    fig, axes = plt.subplots(nrows, ncols, figsize=(ncols*5, nrows*5))
    fig.patch.set_facecolor("#060614")
    axes = np.array(axes).flatten()
    rng  = np.random.default_rng(42)

    for i, t in enumerate(epochs):
        ax = axes[i]
        render_epoch(ax, data, pclass, t, fn, xi=xi, max_pts=max_pts, rng=rng)
        flash = any(abs(t-thr)<0.018 for thr in HEEGNER_THRESHOLDS)
        ax.set_title(f"t={t:.3f}" + (" ⚡" if flash else ""),
                     color="#cccccc", fontsize=9, pad=3)

    for j in range(i+1, len(axes)):
        axes[j].set_visible(False)

    fig.suptitle(
        f"HPL particle_buffer.bin  |  {proj_name.title()} Projection  "
        f"|  {len(data):,} particles  |  xi={xi:.2f}",
        color="white", fontsize=12, fontweight="bold", y=1.005)

    handles = [plt.Line2D([0],[0], marker='o', color='w',
               markerfacecolor=CLASS_COLOURS[c], markersize=7, label=CLASS_NAMES[c])
               for c in [0,1,2]]
    fig.legend(handles=handles, loc="lower center", ncol=3, frameon=False,
               labelcolor="white", fontsize=10, bbox_to_anchor=(0.5, -0.015))
    plt.tight_layout()
    fig.savefig(out_path, dpi=150, bbox_inches="tight", facecolor="#060614")
    plt.close(fig)
    print(f"  saved → {out_path}")


def render_individual(data, pclass, epochs, proj_name, out_dir, xi=0.5, max_pts=120_000):
    fn  = PROJECTORS[proj_name]
    rng = np.random.default_rng(42)
    for t in epochs:
        fig, ax = plt.subplots(figsize=(10,10))
        fig.patch.set_facecolor("#060614")
        render_epoch(ax, data, pclass, t, fn, xi=xi, max_pts=max_pts, rng=rng)
        flash = any(abs(t-thr)<0.018 for thr in HEEGNER_THRESHOLDS)
        ax.set_title(
            f"HPL particle_buffer.bin  |  {proj_name.title()}  |  epoch={t:.4f}"
            + (" ⚡ HEEGNER FLASH" if flash else "") +
            f"\n{len(data):,} particles   xi={xi:.2f}",
            color="white", fontsize=11, pad=8)
        handles = [plt.Line2D([0],[0], marker='o', color='w',
                   markerfacecolor=CLASS_COLOURS[c], markersize=8, label=CLASS_NAMES[c])
                   for c in [0,1,2]]
        ax.legend(handles=handles, loc="lower left", frameon=False,
                  labelcolor="white", fontsize=9)
        fname = os.path.join(out_dir, f"hpl_real_{proj_name}_t{t:.3f}.png")
        fig.savefig(fname, dpi=150, bbox_inches="tight", facecolor="#060614")
        plt.close(fig)
        print(f"  saved → {fname}")


# ── CLI ─────────────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser(description="HPL real-data epoch stillframes")
    ap.add_argument("--bin",      default=DEFAULT_BIN,
                    help="Path to particle_buffer.bin")
    ap.add_argument("--epochs",   type=int,   default=8,
                    help="Number of evenly-spaced epoch frames (Heegner thresholds always included)")
    ap.add_argument("--mode",     choices=["mollweide","azimuthal","ortho","radial","all"],
                    default="all", help="Projection type")
    ap.add_argument("--sheet",    action="store_true",
                    help="Render a contact sheet instead of individual files")
    ap.add_argument("--xi",       type=float, default=0.5,
                    help="XiCoherence scalar: 0=void dominant, 1=soliton dominant")
    ap.add_argument("--out",      default=".",
                    help="Output directory for PNG files")
    ap.add_argument("--max-pts",  type=int,   default=120_000,
                    help="Max particles rendered per frame (trade speed vs density)")
    ap.add_argument("--grid-res", type=int,   default=450,
                    help="Phase field grid resolution for radial mode (higher = slower)")
    args = ap.parse_args()

    if not os.path.exists(args.bin):
        sys.exit(f"ERROR: particle_buffer.bin not found at:\n  {args.bin}\n"
                 f"Override with --bin <path>")

    print(f"Loading {args.bin} ...")
    data, pclass, n = load_particles(args.bin)
    mb = os.path.getsize(args.bin) / 1024**2
    print(f"  {n:,} particles  ({mb:.1f} MB)  stride={STRIDE}B")
    for c, name in CLASS_NAMES.items():
        cnt = (pclass==c).sum()
        print(f"  {name}: {cnt:,}  ({100*cnt/n:.1f}%)")

    os.makedirs(args.out, exist_ok=True)

    # Always include frames just after each Heegner threshold crossing
    base    = np.linspace(0.0, 1.0, args.epochs)
    flashes = [t + 0.01 for t in HEEGNER_THRESHOLDS]
    epochs  = sorted(set(np.round(np.concatenate([base, flashes]), 3)))

    projs = ["mollweide","azimuthal","ortho","radial"] if args.mode == "all" else [args.mode]

    for proj in projs:
        print(f"\n[{proj}]")
        if proj == "radial":
            if args.sheet:
                # Radial sheet: only use 4 frames (t=0, 0.15+, 0.5, 1.0) — each frame is expensive
                radial_epochs = sorted(set(np.round([0.0, 0.160, 0.390, 0.620], 3)))
                render_radial_sheet(data, pclass, radial_epochs,
                                    os.path.join(args.out, "hpl_radial_sheet.png"),
                                    xi=args.xi, grid_res=args.grid_res)
            else:
                render_radial_individual(data, pclass, epochs, args.out,
                                         xi=args.xi, grid_res=args.grid_res)
        else:
            if args.sheet:
                render_sheet(data, pclass, epochs, proj,
                             os.path.join(args.out, f"hpl_real_{proj}_sheet.png"),
                             xi=args.xi, max_pts=args.max_pts // 2)
            else:
                render_individual(data, pclass, epochs, proj, args.out,
                                  xi=args.xi, max_pts=args.max_pts)

    print("\nDone.")

if __name__ == "__main__":
    main()
