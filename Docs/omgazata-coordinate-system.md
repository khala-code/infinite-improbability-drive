# ΩaZaTa Coordinate System

## Overview

ΩaZaTa is the observer-relative coordinate system for the engine. Each coordinate defines a unique causal bubble — no two observers can occupy the same ΩaZaTa coordinate.

## Dimensions

| Symbol | Physical Meaning | Range |
|--------|-----------------|-------|
| Ω | Conformal scale of causal diamond (AdS radius analog) | (0, ∞) |
| Za | Hyperbolic radial depth along zeta geodesic (redshift z) | [0, 1090] |
| Ta | Cosmic time coordinate | [0, t_now] |
| θ, φ | Angular position on celestial sphere | [0,π] × [0,2π] |

The Ω dimension also carries the alpha component of the complex coordinate — the imaginary part is a load-bearing degree of freedom, not a bookkeeping artifact.

## Properties

- No two nodes can occupy the same ΩaZaTa coordinate
- The imaginary component of Ω is incorporated into the authentication/identity model
- The system is predictably verifiable asymptotically within an uncertainty range
- Rekeying: choose a vector that retroactively tightens the uncertainty band on the entire trajectory

## Node Tuple

Each ΩaZaTa node is described by:

    N = (θ, φ, Za, Ta, Ω, φ_phase, δρ/ρ, Ξ)

| Field | Description |
|-------|-------------|
| θ, φ | Angular position (celestial coordinates) |
| Za | Redshift depth (hyperbolic radial position) |
| Ta | Cosmic time |
| Ω | Causal diamond scale (conformal parameter) |
| φ_phase | Harmonic phase — carries antiverse degree of freedom |
| δρ/ρ | Local density contrast (scarcity vector analog) |
| Ξ | Causal connectivity score — fraction of available wavefronts intersected |

## Correspondence with Pulser Mesh Node Tuple

See `pulser-mesh-correspondence.md`. The two systems share a geometric substrate.

## Coordinate Transforms

Galactic coordinates (l, b) → Unity world space requires careful handling:
- Galactic center is at (l=0, b=0)
- Unity is left-handed; astronomical coordinates are right-handed
- Transform documented in `bug-danger-zones.md` — this is a common source of mirrored renders
