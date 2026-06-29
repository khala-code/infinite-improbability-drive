#!/usr/bin/env python3
"""
hpl_viz_A.py  —  HPL Poincaré-disk stillframe renderer
Usage: python hpl_viz_A.py --t 0.38 [--out frame.png] [--n 65536] [--seed 42]
"""
import argparse, sys
import numpy as np
import matplotlib.pyplot as plt
import matplotlib.colors as mcolors
from pathlib import Path

# ── Field scalars (match inspector) ──────────────────────────────────────────
PARITY = 1.0; H = 0.100; V = 0.550; S = 0.350
HEEGNER_TH = [0.15, 0.38, 0.61, 0.84]

def parse_args():
    p = argparse.ArgumentParser(description="HPL Poincaré-disk renderer")
    p.add_argument("--t",    type=float, default=0.50, help="Epoch [0,1]")
    p.add_argument("--out",  type=str,   default=None, help="Output PNG path")
    p.add_argument("--n",    type=int,   default=65536,help="Particle count")
    p.add_argument("--seed", type=int,   default=42,   help="RNG seed")
    p.add_argument("--dpi",  type=int,   default=180,  help="Output DPI")
    return p.parse_args()

def init_particles(n, seed):
    rng = np.random.default_rng(seed)
    l   = rng.uniform(0, 2*np.pi, n)
    b   = np.arcsin(rng.uniform(-1, 1, n))
    cw  = np.array([S, V, H]); cw /= cw.sum()
    cid = rng.choice([0,1,2], size=n, p=cw)
    ph  = rng.uniform(0, 2*np.pi, n)
    sp  = rng.uniform(0.5, 1.5, n)
    return l, b, cid, ph, sp

def to_cartesian(l, b):
    """Galactic (l,b) -> unit vector on S2"""
    x = np.cos(b)*np.cos(l)
    y = np.cos(b)*np.sin(l)
    z = np.sin(b)
    return np.stack([x,y,z], axis=-1)

def slerp(v0, v1, t):
    """Vectorised slerp between rows of v0 and v1 by scalar t"""
    dot = np.clip((v0*v1).sum(-1, keepdims=True), -1, 1)
    omega = np.arccos(dot)
    sin_o = np.sin(omega)
    safe  = sin_o > 1e-8
    w0    = np.where(safe, np.sin((1-t)*omega)/sin_o, 1-t)
    w1    = np.where(safe, np.sin(   t *omega)/sin_o,   t)
    out   = w0*v0 + w1*v1
    norm  = np.linalg.norm(out, axis=-1, keepdims=True)
    return out / np.where(norm>1e-12, norm, 1)

def stereo_to_disk(xyz):
    """Stereographic projection S2->R2, result compressed to unit disk"""
    x,y,z = xyz[:,0], xyz[:,1], xyz[:,2]
    denom = np.where(1+z > 1e-8, 1+z, 1e-8)   # project from south pole
    u = x / denom
    v = y / denom
    # Map R2 -> unit disk (Poincare conformal disk)
    r = np.sqrt(u**2+v**2)
    scale = r / (1+r)   # radial compression: inf -> boundary
    theta = np.arctan2(v, u)
    return scale*np.cos(theta), scale*np.sin(theta)

def evolve(l, b, cid, ph, sp, t):
    """Evolve particles: slerped zenith + class dynamics"""
    north = np.array([0.,0.,1.])
    v0    = to_cartesian(l, b)

    # Per-class zenith bias strength
    zenith_pull = np.where(cid==0, S*0.4, np.where(cid==1, V*0.15, H*0.8))
    zenith_tile = np.tile(north, (len(l),1))
    # Slerp each particle toward north pole by t*pull
    v1 = slerp(v0, zenith_tile, np.clip(t * zenith_pull, 0, 0.999)[:,None])

    # Class-specific phase perturbation (tangent-space nudge)
    # Soliton: longitudinal drift in tangent plane
    m = cid==0
    dl = sp[m]*t*0.15;  db = np.sin(ph[m]+t*2.1)*0.08*V
    v1[m] = to_cartesian(l[m]+dl, b[m]+db)

    # Void: equatorial compression (pull toward equatorial plane)
    m = cid==1
    b_void = b[m]*(1 - t*0.25*H)
    l_void = l[m] - sp[m]*t*0.10*PARITY
    v1[m]  = to_cartesian(l_void, b_void)

    # Heegner: radial pulse at threshold crossings
    m = cid==2
    pulse = sum(np.exp(-((t-th)**2)/(2*0.018**2)) for th in HEEGNER_TH)
    dl_h  = np.cos(ph[m])*pulse*0.12
    db_h  = np.sin(ph[m])*pulse*0.12
    v1[m] = to_cartesian(l[m]+dl_h, b[m]+db_h)

    return v1

def main():
    args = parse_args()
    t    = float(args.t)
    out  = args.out or f"hpl_frame_t{t:.3f}.png"

    print(f"[HPL-viz] epoch={t:.4f}  n={args.n}  seed={args.seed}")

    l, b, cid, ph, sp = init_particles(args.n, args.seed)
    v1 = evolve(l, b, cid, ph, sp, t)
    px, py = stereo_to_disk(v1)

    # pulse brightness for Heegner
    pulse = sum(np.exp(-((t-th)**2)/(2*0.018**2)) for th in HEEGNER_TH)
    pulse_norm = float(np.clip(pulse, 0, 1))

    # render
    fig, ax = plt.subplots(figsize=(9,9), facecolor='#06060f')
    ax.set_facecolor('#06060f')

    # Draw disk boundary
    theta_ring = np.linspace(0, 2*np.pi, 360)
    ax.plot(np.cos(theta_ring), np.sin(theta_ring), color='#1a1a3a', lw=1.2, zorder=1)

    # Plot order: Void (bg) -> Soliton -> Heegner (top)
    specs = [
        (1, '#ef9a9a', 0.5, 0.30),
        (0, '#4fc3f7', 0.7, 0.35),
        (2, '#fff176', 2.5, min(0.95, 0.4+pulse_norm*0.6)),
    ]
    for (c, col, sz, al) in specs:
        m = cid==c
        ax.scatter(px[m], py[m], s=sz, c=col, alpha=al,
                   linewidths=0, rasterized=True, zorder=c+2)

    # Heegner flash rings
    if pulse_norm > 0.05:
        flash_r = np.linspace(0, 2*np.pi, 360)
        for r_frac in [0.33, 0.61, 0.89]:
            ax.plot(r_frac*np.cos(flash_r), r_frac*np.sin(flash_r),
                    color='#fff176', alpha=pulse_norm*0.25, lw=0.8, zorder=5)

    flash_epochs = [th for th in HEEGNER_TH if abs(t-th)<0.02]
    flash_str = f"  ⚡ HeegnerFlash @ {flash_epochs[0]:.2f}" if flash_epochs else ""
    ax.set_title(f'HPL Poincaré Disk  |  epoch={t:.4f}{flash_str}',
                 color='#fff176' if flash_epochs else '#c0c0e0',
                 fontsize=13, fontweight='bold', pad=12)
    ax.text(0.01,0.01,
            f'S={S}  V={V}  H={H}  parity={PARITY}  |  n={args.n:,}  |  slerped-zenith + stereo projection',
            transform=ax.transAxes, color='#3a3a6a', fontsize=7, va='bottom')

    ax.set_xlim(-1.08,1.08); ax.set_ylim(-1.08,1.08)
    ax.set_aspect('equal'); ax.axis('off')
    plt.tight_layout()
    plt.savefig(out, dpi=args.dpi, bbox_inches='tight', facecolor='#06060f')
    plt.close()
    print(f"[HPL-viz] Saved -> {out}")

if __name__ == "__main__":
    main()
