# Causal Field Engine — Data Pipeline Pseudocode

> **Status:** Design / Pre-implementation
> **Location:** `Python/` (scripts), `Assets/CMB/Data/` (inputs), `Assets/CMB/Textures/` & `Assets/CMB/Data/Processed/` (outputs)
> **Depends on:** `conda activate D:\envs\cmb` — healpy, numpy, scipy, PIL, matplotlib

---

## Cosmological Model

The simulation operates within an **inverted hypersphere geometry**:

- The **CMB** (outer boundary) is the surface of last scattering — the frozen record of the first moment solitons refused to cancel
- The **lensing kappa map** (inner boundary) encodes all matter that successfully condensed between the CMB and us — the physical soliton legacy
- The **simulation volume** lives between these two boundaries
- The **observer** stands on Earth's surface looking inward toward the CMB core

The causal field is the ongoing computation of which modes hold their shape (solitons) and which collapse toward parity cancellation (void). The harmonic structure of the CMB is the boundary condition that governs this competition.

---

## Harmonic Classification

Every spherical harmonic mode ℓ is assigned to one of four classes before any processing:

| Class | Condition | Physical Role | Runtime Behaviour |
|---|---|---|---|
| `HEEGNER_LOCKED` | ℓ ∈ {1,2,3,7,11,19,43,67,163} | Structural anchors — irreducibly rigid | Fixed, precomputed, never evolve |
| `VOID_PRIME` | ℓ is odd prime, ℓ ∉ Heegner set | Maximum void pressure — irreducible cancellation | Drive the cancellation force field |
| `SOLITON_COMPOSITE` | ℓ is even, ℓ ∉ Heegner set | Bulk surviving structure | Dynamic hologram surface |
| `VOID_COMPOSITE` | ℓ is odd composite, ℓ ∉ Heegner set | Soft background void pressure | Ambient field noise |

**Note on ℓ=2:** The CMB quadrupole is Heegner-locked (ℓ=2 is a Heegner number and the only even prime). It is also observationally anomalous in Planck data — anomalously low power and unexpectedly aligned with the ecliptic. Treat as a fixed anchor, not a dynamic mode.

**Note on primes adjacent to Heegner numbers:** e.g. ℓ=41 (adjacent to 43) and ℓ=163 itself — these mark boundary transitions between the anchor skeleton and the dynamic field. Flag them for special handling in the field dynamics.

---

## Script 1: `preprocess_cmb_harmonics.py`

**Purpose:** Extract the harmonic basis of the outer boundary. Produce parity-split textures, the temporal animation sequence, and the binary asset for runtime field evaluation.

**Inputs:**
- `Assets/CMB/Data/COM_CMB_IQU-smica_2048_R3.00_full.fits`
- `Assets/CMB/Data/COM_PowerSpect_CMB-TT-full_R3.01` (validation only)

**Outputs:**
- `Assets/CMB/Textures/cmb_heegner_boundary.png`
- `Assets/CMB/Textures/cmb_soliton_boundary.png`
- `Assets/CMB/Textures/cmb_void_prime_boundary.png`
- `Assets/CMB/Textures/cmb_void_composite_boundary.png`
- `Assets/CMB/Textures/cmb_negated.png`
- `Assets/CMB/Textures/cmb_temporal_frames/frame_XXXX.png` (ℓ=2 → 1000)
- `Assets/CMB/Data/Processed/alm_basis.bin` (float32, Unity ComputeBuffer)
- `Assets/CMB/Data/Processed/alm_classified.json` (ℓ → class mapping)

```
LOAD FITS map (field=0, temperature)
    nside = 2048

REMOVE monopole and dipole  [same as existing preprocess_cmb.py]

DECOMPOSE → alm coefficients
    lmax = 1000
    alm = map2alm(clean_map, lmax=1000)

CLASSIFY each ℓ:
    heegner_set = {1, 2, 3, 7, 11, 19, 43, 67, 163}
    FOR ℓ in 0..lmax:
        IF ℓ in heegner_set       → class = HEEGNER_LOCKED
        ELSE IF ℓ == 2            → class = HEEGNER_LOCKED  (redundant, explicit)
        ELSE IF is_prime(ℓ)       → class = VOID_PRIME
        ELSE IF ℓ % 2 == 0        → class = SOLITON_COMPOSITE
        ELSE                      → class = VOID_COMPOSITE
    save alm_classified.json

SPLIT alm by class:
    heegner_alm    = zero all ℓ not in HEEGNER_LOCKED
    soliton_alm    = zero all ℓ not in SOLITON_COMPOSITE
    void_prime_alm = zero all ℓ not in VOID_PRIME
    void_comp_alm  = zero all ℓ not in VOID_COMPOSITE

NEGATE full alm:
    negated_alm = -1 * alm  (shadow universe — what the void wants)

BAKE TEXTURES (each: alm2map → reproject → normalise → false-colour → save PNG):
    heegner_boundary.png         ← viridis        (anchor skeleton)
    soliton_boundary.png         ← RdBu_r          (surviving structure)
    void_prime_boundary.png      ← inferno         (maximum pressure)
    void_composite_boundary.png  ← cividis         (soft background)
    cmb_negated.png              ← RdBu_r inverted (shadow map)

BUILD TEMPORAL SEQUENCE:
    accumulated_alm = zeros
    FOR ℓ from 2 to 1000:
        add this ℓ shell to accumulated_alm
        IF ℓ is a milestone (prime, Heegner, or every 10th):
            bake frame → cmb_temporal_frames/frame_{ℓ:04d}.png
            annotate frame with ℓ class label

VALIDATE against power spectrum:
    Cl_ours   = alm2cl(alm)
    Cl_planck = load COM_PowerSpect_CMB-TT-full_R3.01
    compute residuals — flag any ℓ where |residual| > 5%
    save validation_plot.png

EXPORT binary:
    pack alm as complex64 → alm_basis.bin
    header: lmax, nside, Npix, classification counts per class
```

---

## Script 2: `preprocess_lensing_seeds.py`

**Purpose:** Extract Heegner node seed positions from the lensing kappa map. These are the 3D anchor points of the holographic structure — where the universe already computed its solitons.

**Inputs:**
- `Assets/CMB/Data/Lensing/planck_lensing_kappa_MV.fits`
- `Assets/CMB/Data/Lensing/planck_lensing_mask.fits.gz`
- `Assets/CMB/Data/Processed/alm_classified.json` (from Script 1)

**Outputs:**
- `Assets/CMB/Data/Processed/seeds.bin` (float32, Unity ComputeBuffer)
- `Assets/CMB/Data/Processed/seeds.json` (human readable)
- `Assets/CMB/Textures/seeds_debug.png`

```
LOAD kappa alm from planck_lensing_kappa_MV.fits
CONVERT alm → pixel map at Nside=512
    (lower resolution — we want large-scale soliton structure, not fine detail)

APPLY mask
    binary_mask = lensing_mask > 0.5
    kappa_map[~binary_mask] = NaN  (masked = cosmologically contaminated)

FIND PEAKS (soliton candidates):
    threshold  = percentile(kappa_map[valid], 95)
    hot_pixels = kappa_map > threshold
    clusters   = connected_component_labelling(hot_pixels)
    FOR each cluster:
        centroid_pix           = mean pixel position weighted by kappa value
        centroid_theta, centroid_phi = pix2ang(centroid_pix)
        kappa_peak             = max kappa in cluster
        cluster_radius         = angular_extent(cluster)  [degrees]
        → record as seed candidate

CLASSIFY seeds by Heegner proximity:
    FOR each seed:
        sample heegner_alm map at (theta, phi)   ← from Script 1
        sample void_prime_alm map at (theta, phi)
        heegner_affinity = heegner_sample / total_power_at_position
        void_pressure    = void_prime_sample / total_power_at_position
        node_class = HEEGNER_ANCHOR   if heegner_affinity > threshold
                   else BIFURCATION_NODE if kappa_peak > high_threshold
                   else AMBIENT_NODE

CONVERT (theta, phi) → 3D unit vectors:
    x = sin(theta) * cos(phi)
    y = sin(theta) * sin(phi)
    z = cos(theta)
    → direction on the unit sphere in Galactic coordinates

EXPORT seeds.json:
    [{
        "id": int,
        "direction": [x, y, z],
        "theta": float, "phi": float,
        "kappa": float,
        "radius": float,
        "node_class": "HEEGNER_ANCHOR" | "BIFURCATION_NODE" | "AMBIENT_NODE",
        "heegner_affinity": float,
        "void_pressure": float
    }, ...]

EXPORT seeds.bin:
    flat float32 array, stride 8 per seed:
    [x, y, z, kappa, radius, heegner_affinity, void_pressure, node_class_int]

BAKE DEBUG TEXTURE:
    start from lensing_inner_boundary_masked.png
    overlay seed positions as coloured dots by node_class
    save seeds_debug.png
```

---

## Script 3: `preprocess_validate.py`

**Purpose:** Sanity check all outputs before Unity integration. Confirms the harmonic decomposition is physically correct and the seed positions are cosmologically meaningful.

**Inputs:** All outputs from Scripts 1 and 2.

**Outputs:**
- `Assets/CMB/Data/Processed/validation_report.txt`
- `Assets/CMB/Textures/validation_plots/` (PNG figures)

```
LOAD COM_PowerSpect_CMB-TT-full_R3.01
LOAD alm_basis.bin
COMPUTE Cl from our alm
PLOT: Cl_ours vs Cl_planck (log-log)
    → residuals should be < 5% for ℓ=2..1000
    → flag anomalous ℓ modes (especially ℓ=2,3 — known Planck anomalies)

LOAD seeds.json
COMPUTE:
    N_heegner_anchors     = count where node_class == HEEGNER_ANCHOR
    N_bifurcation         = count where node_class == BIFURCATION_NODE
    N_ambient             = count where node_class == AMBIENT_NODE
    parity_asymmetry      = power(VOID_PRIME modes) / power(SOLITON_COMPOSITE modes)
        → universe's built-in imbalance ratio
        → value > 1: void pressure dominates (leans toward cancellation)
        → value < 1: solitons dominate (leans toward persistence)
    mean_void_pressure    = mean(void_pressure) across all seeds
    mean_heegner_affinity = mean(heegner_affinity) across HEEGNER_ANCHOR seeds

PLOT: seed positions overlaid on cmb_soliton_boundary.png
PLOT: seed positions overlaid on lensing_inner_boundary_masked.png
PLOT: histogram of kappa values by node_class
PLOT: heegner_affinity vs void_pressure scatter (coloured by node_class)

PRINT validation_report.txt:
    - total seeds found
    - class breakdown
    - parity asymmetry ratio
    - power spectrum residual summary
    - any flagged anomalies
    - recommended kappa threshold for seed extraction
```

---

## Data Flow Summary

```
COM_CMB_IQU-smica_2048_R3.00_full.fits
    │
    └──► Script 1: preprocess_cmb_harmonics.py
              │
              ├──► cmb_heegner_boundary.png         (outer boundary — anchor skeleton)
              ├──► cmb_soliton_boundary.png          (outer boundary — soliton modes)
              ├──► cmb_void_prime_boundary.png       (outer boundary — void pressure)
              ├──► cmb_void_composite_boundary.png   (outer boundary — soft background)
              ├──► cmb_negated.png                   (shadow universe)
              ├──► cmb_temporal_frames/              (ℓ=2→1000 animation)
              ├──► alm_basis.bin                     ──► Unity ComputeBuffer
              └──► alm_classified.json               ──► Script 2

planck_lensing_kappa_MV.fits
planck_lensing_mask.fits.gz
    │
    └──► Script 2: preprocess_lensing_seeds.py
              │
              ├──► seeds.bin                         ──► Unity ComputeBuffer
              ├──► seeds.json                        ──► design iteration
              └──► seeds_debug.png

alm_basis.bin + seeds.bin + all PNGs
    │
    └──► Script 3: preprocess_validate.py
              │
              ├──► validation_report.txt
              └──► validation_plots/

                        │
                        ▼
               Unity Runtime:
               CausalFieldRenderer.cs reads
               seeds.bin + alm_basis.bin
               at startup — no Python at runtime
```

---

## Unity Asset Manifest

| File | Type | Unity Usage |
|---|---|---|
| `cmb_heegner_boundary.png` | Texture2D | Outer boundary shader — anchor layer |
| `cmb_soliton_boundary.png` | Texture2D | Outer boundary shader — soliton layer |
| `cmb_void_prime_boundary.png` | Texture2D | Outer boundary shader — void pressure layer |
| `cmb_negated.png` | Texture2D | Shadow universe overlay |
| `lensing_inner_boundary_masked.png` | Texture2D | Inner boundary shader (existing) |
| `cmb_temporal_frames/` | Texture2D[] | Animation sequence — ℓ timeline |
| `alm_basis.bin` | TextAsset → ComputeBuffer | Runtime harmonic field evaluation |
| `seeds.bin` | TextAsset → ComputeBuffer | Heegner node positions + properties |

---

## VFX Parameters

The following scalar values should be surfaced as live parameters in the VFX Graph and/or CustomHLSL inputs:

| Parameter | Source | Type | Notes |
|---|---|---|---|
| `__ParityAsymmetry` | Script 3 validation | Float | power(VOID_PRIME) / power(SOLITON_COMPOSITE). The universe's fundamental tension ratio. Drive void pressure intensity. |
| `__HeegnerAffinity` | seeds.json mean | Float | Mean Heegner affinity across anchor nodes. Modulate anchor rigidity. |
| `__MeanVoidPressure` | seeds.json mean | Float | Mean void pressure across all seeds. Scale ambient particle density. |
| `__BubbleScale` | existing | Float | Already wired — keep as-is. |
| `__Resolution` | existing | Int32 | Already wired — keep as-is. |
