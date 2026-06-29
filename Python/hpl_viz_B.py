#!/usr/bin/env python3
"""
hpl_viz_B.py  —  HPL exotic phase-angle interference + nested Poincaré projections
Renders the radial projection layer (phase interference along depth axis)
on top of the nested boundary shell intersections, both directions.

Usage: python hpl_viz_B.py --t 0.38 [--out frame.png] [--shells 5] [--modes 7]
"""
import argparse
import numpy as np
import matplotlib.pyplot as plt
import matplotlib.colors as mcolors
from matplotlib.colors import LinearSegmentedColormap

PARITY = 1.0; H = 0.100; V = 0.550; S = 0.350
HEEGNER_TH = [0.15, 0.38, 0.61, 0.84]

def parse_args():
    p = argparse.ArgumentParser(description="HPL exotic phase-interference renderer")
    p.add_argument("--t",      type=float, default=0.50, help="Epoch [0,1]")
    p.add_argument("--out",    type=str,   default=None)
    p.add_argument("--n",      type=int,   default=65536)
    p.add_argument("--seed",   type=int,   default=42)
    p.add_argument("--shells", type=int,   default=5,    help="Nested boundary shell count")
    p.add_argument("--modes",  type=int,   default=7,    help="Radial interference modes")
    p.add_argument("--dpi",    type=int,   default=180)
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
    return np.stack([np.cos(b)*np.cos(l), np.cos(b)*np.sin(l), np.sin(b)], axis=-1)

def stereo_to_disk(xyz):
    x,y,z = xyz[:,0], xyz[:,1], xyz[:,2]
    denom  = np.where(1+z>1e-8, 1+z, 1e-8)
    u, v   = x/denom, y/denom
    r      = np.sqrt(u**2+v**2)
    scale  = r/(1+r)
    theta  = np.arctan2(v, u)
    return scale*np.cos(theta), scale*np.sin(theta)

def phase_field(px, py, t, n_modes, shells):
    """
    Radial phase interference field on the Poincare disk.
    Two components:
      1. Nested boundary shells: concentric rings at radii r_k = k/(shells+1)
         modulated by cos(m*theta + t*omega_k)  — boundary layer oscillation
      2. Radial projection: interference between inward/outward modes
         I(r,theta) = sum_m A_m * cos(m*theta + phi_m(r,t)) * cos(n*pi*r + t*nu_m)
    """
    r     = np.sqrt(px**2+py**2)
    theta = np.arctan2(py, px)
    field = np.zeros_like(r)

    # Component 1: nested boundary shells
    for k in range(1, shells+1):
        r_k   = k / (shells+1)
        omega = 2*np.pi * (k*0.381 + 0.1)
        heeg  = [163, 67, 43, 11, 7]  # Heegner numbers modulate shell amplitudes
        A_k   = 1.0 / (1 + abs(r - r_k) * heeg[k-1 if k<=5 else 0] * 0.002)
        phase_shell = np.cos((k+1)*theta + t*omega) * np.exp(-((r-r_k)**2)/(2*(0.06)**2))
        field += A_k * phase_shell * (V if k%2==0 else S)

    # Component 2: radial projections (inward + outward)
    pulse = float(sum(np.exp(-((t-th)**2)/(2*0.025**2)) for th in HEEGNER_TH))
    for m in range(1, n_modes+1):
        phi_m  = m * np.pi * (1 + H)
        nu_m   = m * 2.1 * (1 + S*0.3)
        A_m    = (1/m) * (1 + pulse * (1 if m<=2 else 0))
        # Outward
        field += A_m * np.cos(m*theta + phi_m*t) * np.cos(nu_m*np.pi*r)
        # Inward (reversed parity)
        field += A_m * PARITY * np.cos(-m*theta + phi_m*t + np.pi/m) * np.sin(nu_m*np.pi*r + t)

    return field

def main():
    args  = parse_args()
    t     = float(args.t)
    out   = args.out or f"hpl_exotic_t{t:.3f}.png"

    print(f"[HPL-exotic] epoch={t:.4f}  shells={args.shells}  modes={args.modes}")

    l, b, cid, ph, sp = init_particles(args.n, args.seed)

    # Epoch evolve
    pulse_ev = float(sum(np.exp(-((t-th)**2)/(2*0.018**2)) for th in HEEGNER_TH))
    m0=cid==0; m1=cid==1; m2=cid==2
    l[m0]+=sp[m0]*t*0.15; b[m0]+=np.sin(ph[m0]+t*2.1)*0.08*V
    b[m1]*=(1-t*0.25*H);  l[m1]-=sp[m1]*t*0.10
    l[m2]+=np.cos(ph[m2])*pulse_ev*0.12; b[m2]+=np.sin(ph[m2])*pulse_ev*0.12
    l=l%(2*np.pi); b=np.clip(b,-np.pi/2,np.pi/2)

    v1     = to_cartesian(l, b)
    px, py = stereo_to_disk(v1)

    # Phase field on grid
    res  = 800
    gx   = np.linspace(-1, 1, res)
    gy   = np.linspace(-1, 1, res)
    GX, GY = np.meshgrid(gx, gy)
    mask_disk = GX**2+GY**2 <= 1.0
    field = phase_field(GX.ravel(), GY.ravel(), t, args.modes, args.shells)
    field = field.reshape(res, res)
    field[~mask_disk] = np.nan
    fmax = np.nanmax(np.abs(field))
    field /= (fmax if fmax>0 else 1)

    # CMB-ish colormap
    cmb_colors = ['#0a0020','#0d1a4a','#1a3a7a','#2255aa',
                  '#e8e8e8','#ffcc55','#ff6600','#cc0000','#550000']
    cmb_cmap   = LinearSegmentedColormap.from_list('cmb', cmb_colors, N=512)

    fig, axes = plt.subplots(1, 2, figsize=(18, 9), facecolor='#06060f')
    fig.suptitle(
        f'HPL Phase Interference + Nested Projections  |  epoch={t:.4f}',
        color='#e0e0ff', fontsize=14, fontweight='bold', y=1.01)

    # Left: field heatmap + particle overlay
    ax = axes[0]
    ax.set_facecolor('#06060f')
    im = ax.imshow(field, origin='lower', extent=[-1,1,-1,1],
                   cmap=cmb_cmap, vmin=-1, vmax=1,
                   interpolation='bilinear', zorder=1, alpha=0.85)
    ring = plt.Circle((0,0), 1.0, color='#1a1a3a', fill=False, lw=1.2, zorder=5)
    ax.add_patch(ring)
    for cid_v, col, sz, al in [(1,'#ef9a9a',0.3,0.20),(0,'#4fc3f7',0.3,0.20),(2,'#fff176',1.0,0.70)]:
        m = cid==cid_v
        ax.scatter(px[m], py[m], s=sz, c=col, alpha=al, linewidths=0, rasterized=True, zorder=6)
    for k in range(1, args.shells+1):
        r_k = k/(args.shells+1)
        c   = plt.Circle((0,0), r_k, color='#ffffff', fill=False, lw=0.4, alpha=0.12, zorder=4)
        ax.add_patch(c)
    cb = plt.colorbar(im, ax=ax, fraction=0.04, pad=0.02)
    cb.set_label('Phase field amplitude', color='#aaaacc', fontsize=8)
    cb.ax.yaxis.set_tick_params(color='#aaaacc')
    plt.setp(cb.ax.yaxis.get_ticklabels(), color='#aaaacc')
    ax.set_title('Nested boundary + radial interference  (particles overlaid)',
                 color='#c0c0e0', fontsize=10, pad=8)
    ax.set_xlim(-1.08,1.08); ax.set_ylim(-1.08,1.08)
    ax.set_aspect('equal'); ax.axis('off')

    # Right: phase angle map (inward vs outward)
    ax2 = axes[1]
    ax2.set_facecolor('#06060f')
    field_r = np.zeros((res,res)); field_i = np.zeros((res,res))
    rx = GX.ravel(); ry = GY.ravel()
    rr = np.sqrt(rx**2+ry**2); th_g = np.arctan2(ry, rx)
    for m in range(1, args.modes+1):
        phi_m = m*np.pi*(1+H); nu_m = m*2.1*(1+S*0.3); A_m = 1/m
        field_r.ravel()[:] += A_m*np.cos(m*th_g+phi_m*t)*np.cos(nu_m*np.pi*rr)
        field_i.ravel()[:] += A_m*np.cos(-m*th_g+phi_m*t+np.pi/m)*np.sin(nu_m*np.pi*rr+t)
    field_r = field_r.reshape(res,res); field_i = field_i.reshape(res,res)
    phase_angle = np.arctan2(field_i, field_r)
    phase_angle[~mask_disk] = np.nan
    im2 = ax2.imshow(phase_angle, origin='lower', extent=[-1,1,-1,1],
                     cmap='hsv', vmin=-np.pi, vmax=np.pi,
                     interpolation='bilinear', zorder=1, alpha=0.9)
    ring2 = plt.Circle((0,0), 1.0, color='#1a1a3a', fill=False, lw=1.2, zorder=5)
    ax2.add_patch(ring2)
    cb2 = plt.colorbar(im2, ax=ax2, fraction=0.04, pad=0.02)
    cb2.set_label('Phase angle  (in/out interference)', color='#aaaacc', fontsize=8)
    cb2.ax.yaxis.set_tick_params(color='#aaaacc')
    plt.setp(cb2.ax.yaxis.get_ticklabels(), color='#aaaacc')
    ax2.set_title('Phase angle: inward vs outward radial projections',
                 color='#c0c0e0', fontsize=10, pad=8)
    ax2.set_xlim(-1.08,1.08); ax2.set_ylim(-1.08,1.08)
    ax2.set_aspect('equal'); ax2.axis('off')

    plt.tight_layout()
    plt.savefig(out, dpi=args.dpi, bbox_inches='tight', facecolor='#06060f')
    plt.close()
    print(f"[HPL-exotic] Saved -> {out}")

if __name__ == "__main__":
    main()
