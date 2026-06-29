# Holographic Projection — Particle Pipeline

> **Status:** Design / Pre-implementation  
> **Stage:** After `causal-field-pipeline.md` outputs are validated (`validation_report.txt` passes)  
> **Depends on:** `processed/alm_by_class.npz`, `processed/nodes.npz`, `processed/parity_asymmetry.json`  
> **Unity side:** `HolographicLayerController`, `EpochScrubber`, boundary shader pipeline

---

## Conceptual Frame

The holographic projection stage is where the precomputed field data becomes something an observer
can stand inside. The CMB boundary is not a skybox — it is an **interference pattern written on
a surface the observer cannot reach**. The particle pipeline makes that interference legible by:

1. Sampling the classified alm field at discrete points (particles)
2. Projecting their harmonic class, parity state, and void/soliton pressure onto visual variables
3. Binding particle lifetimes and trajectories to the causal field dynamics (not arbitrary noise)
4. Handing a GPU-ready particle buffer to Unity's `ComputeShader` for real-time rendering

The result is a particle layer that sits between the CMB skybox (boundary condition, frozen) and the
ObserverBubble (runtime ξ field). It is the **competition field made visible** — the live substrate
between what the universe started with and what it collapsed into.

---

## Data Contracts

### Inputs (from causal-field-pipeline outputs)

```
processed/alm_by_class.npz
    heegner_alm       — complex128 alm array, all non-HEEGNER_LOCKED ℓ zeroed
    soliton_alm       — complex128 alm array, SOLITON_COMPOSITE modes only
    void_prime_alm    — complex128 alm array, VOID_PRIME modes only
    void_comp_alm     — complex128 alm array, VOID_COMPOSITE modes only

processed/nodes.npz
    directions        — float32 (N, 3)   unit vectors in Galactic frame
    kappa             — float32 (N,)     lensing convergence at each node
    heegner_affinity  — float32 (N,)     fraction of local power from HEEGNER modes
    void_pressure     — float32 (N,)     fraction of local power from VOID_PRIME modes
    node_class        — int8    (N,)     0=HEEGNER_ANCHOR, 1=BIFURCATION_NODE, 2=AMBIENT

processed/parity_asymmetry.json
    parity_asymmetry  — float, global competition ratio (odd/even power)
    per_class_power   — dict of class → total power fraction
```

### Outputs (to Unity)

```
assets/StreamingAssets/particle_buffer.bin   — flat binary, N_particles × ParticleRecord
assets/StreamingAssets/field_scalars.json    — global scalars for shader uniform injection
assets/Textures/Boundary/heegner_heatmap.exr — equirectangular float32 HEEGNER power map
assets/Textures/Boundary/void_heatmap.exr    — equirectangular float32 VOID_PRIME power map
```

---

## Script 4: `generate_particle_buffer.py`

**Purpose:** Convert the classified field arrays and node list into a particle buffer ready for
Unity's `ParticleSystemRenderer` or a custom `ComputeShader` particle pass. Each particle carries
its harmonic class, initial sky position, velocity seed, lifetime, and colour encoding.

**Input:** all validated `processed/` outputs  
**Output:** `assets/StreamingAssets/particle_buffer.bin`, `field_scalars.json`

```
LOAD processed/alm_by_class.npz
LOAD processed/nodes.npz
LOAD processed/parity_asymmetry.json

────────────────────────────────────────────────
PHASE 1 — Sampling strategy
────────────────────────────────────────────────

DEFINE N_particles = 65536  (must be power-of-two for ComputeShader threadgroup alignment)
DEFINE N_heegner   = 512    (fixed — skeleton particles, never culled)
DEFINE N_soliton   = 24576  (even modes — survivors)
DEFINE N_void      = 40448  (odd modes — pressure field)
    NOTE: N_heegner + N_soliton + N_void = 65536

SAMPLE heegner particles:
    FOR each Heegner ℓ ∈ {2, 3, 7, 11, 19, 43, 67, 163}:
        power_at_ℓ = Σ |a_ℓm|² for m in -ℓ..ℓ  (from heegner_alm)
        N_ℓ = round(N_heegner × power_at_ℓ / total_heegner_power)
        → distribute N_ℓ particles uniformly on the sky via their alm phase angles:
              θ_p, φ_p = phase-weighted angular sampling of mode ℓ
              position = unit sphere point at (θ_p, φ_p)

SAMPLE soliton particles:
    FOR each SOLITON_COMPOSITE ℓ:
        weight = |a_ℓm|² / total_soliton_power
    → stratified sky sampling, weighted by per-mode power
    → cluster particles preferentially near HEEGNER_ANCHOR nodes
          bias = softmax(heegner_affinity of nearest anchor node, temperature=2.0)

SAMPLE void particles:
    SPLIT between VOID_PRIME (higher pressure) and VOID_COMPOSITE (soft background):
        N_void_prime = round(N_void × void_prime_fraction)   where void_prime_fraction
                                       = per_class_power["VOID_PRIME"] /
                                         (per_class_power["VOID_PRIME"] + per_class_power["VOID_COMPOSITE"])
        N_void_comp  = N_void - N_void_prime

    VOID_PRIME sampling: anti-cluster — preferentially sample SKY regions with LOW kappa
        (void pressure is strongest where matter did not condense)

    VOID_COMPOSITE sampling: uniform background with slight filamentary bias from full alm map

────────────────────────────────────────────────
PHASE 2 — Per-particle record construction
────────────────────────────────────────────────

ParticleRecord struct (96 bytes, GPU-aligned):
    position_xyz      : float32[3]   — unit sphere, Galactic frame
    velocity_seed_xyz : float32[3]   — drift direction, magnitude encodes competition pressure
    lifetime_base     : float32      — base lifetime in simulation seconds
    lifetime_variance : float32      — ±jitter fraction (0.0–0.5)
    colour_rgba       : float32[4]   — HDR colour, alpha = initial opacity
    harmonic_class    : int32        — 0=HEEGNER, 1=SOLITON, 2=VOID_PRIME, 3=VOID_COMP
    ell               : int32        — associated ℓ mode (for shader LOD decisions)
    heegner_affinity  : float32      — local Heegner field strength at spawn site
    void_pressure     : float32      — local void pressure at spawn site
    kappa             : float32      — lensing κ at spawn site (0.0 if no nearby node)
    _pad              : float32[3]   — alignment padding

FOR each sampled particle:

    COMPUTE velocity_seed:
        base_drift = radial outward unit vector × 0.02  (slow boundary drift)
        competition_push =
            if SOLITON → push toward nearest HEEGNER_ANCHOR node direction × kappa × 0.1
            if VOID    → push away from nearest HEEGNER_ANCHOR node × void_pressure × 0.15
            if HEEGNER → zero drift (skeleton is stationary)
        velocity_seed = base_drift + competition_push + gaussian_noise(σ=0.005)

    COMPUTE lifetime:
        if HEEGNER_LOCKED:  lifetime_base = ∞  (sentinel: -1.0)
        if SOLITON:         lifetime_base = lerp(8.0, 20.0, heegner_affinity)
            → solitons near anchors live longer (more stable)
        if VOID_PRIME:      lifetime_base = lerp(2.0, 6.0, 1.0 - void_pressure)
            → high void pressure = shorter individual lifetime, but higher respawn rate
        if VOID_COMPOSITE:  lifetime_base = lerp(4.0, 12.0, uniform_random)
        lifetime_variance = 0.3  (all classes)

    COMPUTE colour:
        HEEGNER_LOCKED  → colour = (#E8D5A3, gold-white)  α=1.0  HDR intensity=2.5
            NOTE: ℓ=2 quadrupole gets special treatment — slightly blue-shifted (#C8D8E8)
                  to reflect its anomalous low-power / ecliptic-aligned status
        SOLITON_COMP    → colour = lerp(#4A8FA8, #A8E0F0, heegner_affinity)  α=0.7
            → warmer cyan-white near Heegner anchors, cooler blue in open regions
        VOID_PRIME      → colour = lerp(#1A1A2E, #4A2070, void_pressure)  α=0.5
            → dark void blue-purple; higher pressure = more saturated
        VOID_COMPOSITE  → colour = (#2A2A3A, dim grey-blue)  α=0.3  (background only)

        PARITY MODULATION:
            if parity_asymmetry > 1.0:
                → tint all VOID particles: desaturate SOLITON by (parity_asymmetry - 1.0) × 0.1
                → void pressure is winning this epoch — make it visible
            else:
                → tint all SOLITON particles: add warm HDR boost × (1.0 - parity_asymmetry) × 0.15

WRITE all N_particles records to particle_buffer.bin in ParticleRecord struct order
    header: uint32 magic=0x484F4C4F ("HOLO"), uint32 version=1, uint32 N, uint32 struct_size

WRITE field_scalars.json:
    {
        "parity_asymmetry":    float,
        "heegner_fraction":    float,   — per_class_power["HEEGNER_LOCKED"] / total
        "void_fraction":       float,   — (VOID_PRIME + VOID_COMPOSITE) / total
        "N_particles":         65536,
        "N_heegner":           512,
        "N_soliton":           int,
        "N_void_prime":        int,
        "N_void_composite":    int,
        "competition_epoch":   "void_dominant" | "soliton_dominant" | "balanced"
            → void_dominant   if parity_asymmetry > 1.05
            → soliton_dominant if parity_asymmetry < 0.95
            → balanced        otherwise
    }
```

---

## Script 5: `generate_boundary_heatmaps.py`

**Purpose:** Project the classified alm field power onto equirectangular textures for use in the
boundary sphere shaders. These are `float32` EXR maps — not visual images but data textures sampled
in GLSL.

**Outputs:**
- `heegner_heatmap.exr` — per-pixel HEEGNER_LOCKED power, normalised 0–1
- `void_heatmap.exr`    — per-pixel VOID_PRIME power, normalised 0–1

```
DEFINE width=4096, height=2048   (equirectangular, same projection as CMB skybox)
DEFINE Nside_sample = 512        (HEALPix resolution for alm2map)

FOR each heatmap (heegner_alm, void_prime_alm):

    RECONSTRUCT pixel map from alm:
        pixel_map = alm2map(class_alm, Nside=512)
        NOTE: alm2map at Nside=512 captures all modes up to ℓ=1000 with aliasing buffer

    NORMALISE:
        map_min = percentile(pixel_map, 1)    — clip tails
        map_max = percentile(pixel_map, 99)
        normalised = clamp((pixel_map - map_min) / (map_max - map_min), 0, 1)

    REPROJECT HEALPix → equirectangular:
        FOR each pixel (u, v) in 4096×2048 output:
            θ = (1 - v/2048) × π
            φ = (u/4096) × 2π
            healpix_pixel = ang2pix(Nside=512, θ, φ, nest=False)
            output[v, u] = normalised[healpix_pixel]

    WRITE as float32 EXR (single channel R):
        imageio.imwrite(path, output.astype(float32), format="EXR")
        → Unity imports as single-channel float texture, sampled in shader via .r

NODE OVERLAY:
    For each HEEGNER_ANCHOR node in nodes.npz:
        θ, φ = node direction → angular coords
        u = φ / 2π × 4096
        v = (1 - θ/π) × 2048
        paint gaussian splash (σ=8px, amplitude=2.0) onto heegner_heatmap
            → anchors should be the brightest features by construction;
              the gaussian makes them spatially legible at boundary-sphere distance
```

---

## Unity Integration — `HolographicParticleLayer.cs`

The Unity side reads the particle buffer and drives a `ParticleSystem` or
`ComputeShader`-backed custom particle pass. Key bindings:

```
ON Awake:
    LOAD particle_buffer.bin from StreamingAssets
    PARSE header → validate magic, version, N, struct_size
    DESERIALIZE N ParticleRecord structs into NativeArray<ParticleRecord>

    LOAD field_scalars.json
    SET shader global properties:
        _ParityAsymmetry     = field_scalars.parity_asymmetry
        _HeegnerFraction     = field_scalars.heegner_fraction
        _VoidFraction        = field_scalars.void_fraction
        _CompetitionEpoch    = enum from competition_epoch string

    IF using Unity ParticleSystem:
        FOR each ParticleRecord:
            emit particle at record.position_xyz × BOUNDARY_RADIUS
            set startColor = record.colour_rgba
            set startLifetime = record.lifetime_base × (1 ± record.lifetime_variance)
            set velocity = record.velocity_seed_xyz × PARTICLE_SPEED_SCALE
            IF lifetime_base == -1.0 (HEEGNER sentinel) → startLifetime = float.MaxValue

ON Update (per-frame field coupling):
    READ current epoch state from EpochScrubber (z, ξ, Ω)
    COMPUTE epoch_scale = function of z: at high z → boost void fraction visibility
                                         at low z  → soliton structure dominates
    APPLY:
        → Scale VOID particle emission rate by epoch_scale × parity_asymmetry
        → Scale SOLITON particle HDR intensity by 1.0 / epoch_scale
        → HEEGNER particles are epoch-invariant (they are the skeleton, not the dynamics)

    IF Ω crosses a Heegner boundary (from EpochScrubber.OnHeegnerCrossing event):
        TRIGGER HeegnerFlash:
            → briefly boost all HEEGNER particle HDR intensity × 4.0 over 1.2 seconds
            → emit 512 burst particles at all Heegner anchor positions simultaneously
            → fade back to baseline

ON BubbleXiChanged (from ObserverBubbleRenderer):
    xi_value = event.xi_scalar
    → Modulate SOLITON particle alpha: alpha × lerp(0.5, 1.0, xi_value)
    → Modulate VOID_PRIME particle alpha: alpha × lerp(1.0, 0.4, xi_value)
        NOTE: as ξ increases (higher coherence), soliton structure becomes more visible
              and void pressure dims — ξ upward pressure is made legible in the particle layer
```

---

## Shader Variables Injected from `field_scalars.json`

These are set as global shader properties on session load and updated each epoch step.
All existing boundary shaders (`LensingBoundary`, `MilkyWayBoundary`, `ObserverBubble`)
can optionally read them for cross-layer coherence modulation.

| Property | Type | Source | Notes |
|---|---|---|---|
| `_ParityAsymmetry` | float | `parity_asymmetry` | Global competition ratio; > 1 = void dominant |
| `_HeegnerFraction` | float | `heegner_fraction` | Fraction of CMB power in skeleton modes |
| `_VoidFraction` | float | `void_fraction` | Total void pressure as CMB fraction |
| `_CompetitionEpoch` | int | `competition_epoch` | 0=balanced, 1=soliton, 2=void |
| `_HeegnerHeatmap` | Texture2D | `heegner_heatmap.exr` | Equirectangular float map |
| `_VoidHeatmap` | Texture2D | `void_heatmap.exr` | Equirectangular float map |

---

## Data Flow (full pipeline extension)

```
processed/alm_by_class.npz
processed/nodes.npz
processed/parity_asymmetry.json
    │
    ├──► Script 4: generate_particle_buffer.py
    │         │
    │         ├──► assets/StreamingAssets/particle_buffer.bin
    │         └──► assets/StreamingAssets/field_scalars.json
    │
    └──► Script 5: generate_boundary_heatmaps.py
              │
              ├──► assets/Textures/Boundary/heegner_heatmap.exr
              └──► assets/Textures/Boundary/void_heatmap.exr

assets/StreamingAssets/particle_buffer.bin
assets/StreamingAssets/field_scalars.json
assets/Textures/Boundary/*.exr
    │
    └──► HolographicParticleLayer.cs (Unity runtime)
              │
              ├── reads on Awake → drives ParticleSystem or ComputeShader particle pass
              ├── couples to EpochScrubber (epoch z, Ω, Heegner event triggers)
              └── couples to ObserverBubbleRenderer (ξ modulates soliton/void visibility)
```

---

## Open Design Questions

These should be resolved before implementation, not during:

1. **Particle count budget on Quest 2** — 65536 CPU-side `ParticleSystem` particles will
   exceed the Quest 2's fill-rate budget for a skybox-radius billboard layer. Options:
   - Drop to 16384 total with LOD culling (hide VOID_COMPOSITE entirely at ξ < 0.3)
   - Switch to a `ComputeShader` particle pass: N_particles in a structured buffer, rendered
     as point sprites via `DrawProceduralIndirect` — no CPU overhead per particle
   - **Recommendation:** ComputeShader path; particle_buffer.bin is already GPU-struct-aligned

2. **Boundary radius vs. particle depth layering** — HEEGNER particles should sit at
   `BOUNDARY_RADIUS × 1.0` (on the surface), SOLITON slightly inside (`× 0.98`), VOID further
   inside (`× 0.95`). This gives depth cues without parallax inconsistency in stereo VR.

3. **Reprojection from Galactic to Unity world coordinates** — the field is computed in
   Galactic frame (IAU); Unity's skybox uses a Unity-convention spherical map. Galactic north
   ≠ Unity world up. A one-time rotation matrix `M_gal_to_unity` needs to be established and
   applied consistently to all particle positions, node directions, and heatmap projections.
   See `omgazata-coordinate-system.md` for the ZaTaOa frame — this should be the bridge.

4. **Particle respawn vs. static buffer** — the current design writes a static buffer at
   precompute time. For void particles with short lifetimes, Unity's `ParticleSystem` will
   need a `Burst emitter + over-lifetime` setup, or the `ComputeShader` path needs an
   in-shader respawn random seed. The `heegner_affinity` and `void_pressure` per-particle
   values should persist across respawns (they are field properties, not instance properties).

5. **Heegner ℓ=2 visual treatment** — the quadrupole is anomalous: Heegner-locked, but also
   observationally suppressed (low power) and ecliptic-aligned. Its particle set should be
   visually distinguished from the other 8 Heegner modes. Proposed: render ℓ=2 particles as
   a faint, wide great-circle arc rather than point sprites — tracing the ecliptic plane
   alignment as a structural seam in the hologram.

---

*Last updated: 2026-06-29*  
*"The boundary is not a screen. It is the competition itself, made spatial."*
