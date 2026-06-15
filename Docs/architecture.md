# Architecture

## Core Principle

The engine does not simulate the universe. It simulates what it is like to be inside one.

Computational irreducibility (Wolfram) means the universe cannot be simulated faster than it runs. The escape: an observer at `(θ, φ, z, t)` is causally affected by exactly and only the wavefronts that intersect their worldline. Everything spacelike-separated is not approximated — it is physically absent.

## The Boundary Condition

The CMB is a two-layer interference pattern:

- **Layer 1 (Photon):** Decouples at z ≈ 1090, t ≈ 380,000 yrs. Fully mapped by Planck.
- **Layer 2 (Neutrino):** Decouples at z ≈ 6×10⁹, t ≈ 1 sec. Inferred from CMB phase shifts. Never directly observed.

Both fields are horocycles in hyperbolic space sharing the same ideal boundary point (the singularity). They are orthogonal because they decoupled under different force carriers at different epochs.

The Big Bang singularity is the unknown seed: null centroid, no causal access, every trajectory points toward it but none reaches it.

## The Observer Bubble (ΩaZaTa)

Each observer is described by a coordinate `(Ω, Za, Ta)` which defines:

- A causal diamond (Penrose volume) scaled by Ω
- A position along the zeta geodesic (hyperbolic radial depth)
- A temporal coordinate Ta

The Ω parameter is the conformal scale factor of the causal diamond. It determines which horocyclic wavefront shells intersect the observer bubble.

## Wavefront Propagation

Three wavefront classes intersect any observer bubble:

1. **BAO (Baryon Acoustic Oscillations):** Spherical shells at ~150 Mpc comoving. O(ℓ_max) per intersection.
2. **Sachs-Wolfe:** Gravitational potential imprint. O(1) lookup from precomputed table.
3. **CνB phase shift:** Neutrino imprint on acoustic peaks. O(1) correction factor.

Wavefront surfaces are horocycles — parallel to the zeta geodesic, orthogonal to the observer worldline, scaled by the Za coordinate.

## Epistemic Tiers

| Tier | Content | Status |
|------|---------|--------|
| 1 | Photon CMB | Observed |
| 2 | Neutrino CνB | Real, unobserved |
| 3 | Antiverse | Metaphorically coherent, physically absent |

Tier 2 is not the antiverse. It is a real physical field we cannot yet detect directly.

## Computational Complexity

```
Naive simulation:     O(10^80)  — impossible
Full CMB grid:        O(50M pixels) — wasteful
Observer bubble only: O(W_active × ℓ_relevant) ≈ O(50,000) per frame
```

On Quest 2 GPU: < 0.1ms per frame budget for field evaluation.
