# Causal Field Pipeline

> **Status:** Design / Pre-implementation
> **Depends on:** healpy, numpy, scipy — `conda activate D:\envs\cmb`

***

## Cosmological Framing

The model operates between two observational boundaries:

- **Outer boundary** — the CMB surface of last scattering. The frozen record of the earliest moment at which soliton modes refused to cancel. Every `a_ℓm` coefficient encodes a mode that either held its shape or failed to.
- **Inner boundary** — the gravitational lensing convergence (κ) map. Every peak in κ marks a location where matter successfully condensed — a physical soliton that survived into the present epoch.

The simulation volume lives between these two boundaries. The causal field is the ongoing competition between modes that persist (solitons) and modes that collapse toward parity cancellation (void). The CMB harmonic structure is the boundary condition. The κ map is the empirical result.

***

## Harmonic Classification

Every spherical harmonic mode ℓ is classified before any computation. Classification is deterministic and derived entirely from number theory — no free parameters.

```
heegner_set = {1, 2, 3, 7, 11, 19, 43, 67, 163}

classify(ℓ):
    IF ℓ ∈ heegner_set        → HEEGNER_LOCKED
    ELSE IF is_prime(ℓ)       → VOID_PRIME
    ELSE IF ℓ % 2 == 0        → SOLITON_COMPOSITE
    ELSE                      → VOID_COMPOSITE
```

| Class | Condition | Physical meaning |
|---|---|---|
| `HEEGNER_LOCKED` | ℓ ∈ {1,2,3,7,11,19,43,67,163} | Unique factorisation fields — structurally rigid, no alternative decomposition exists |
| `VOID_PRIME` | ℓ prime, ℓ ∉ Heegner set | Irreducible void pressure — maximum cancellation potential, no resonance path to lower modes |
| `SOLITON_COMPOSITE` | ℓ even, ℓ ∉ Heegner set | Reducible surviving structure — the bulk of the holographic surface |
| `VOID_COMPOSITE` | ℓ odd composite, ℓ ∉ Heegner set | Soft background void pressure — partially reducible, partially cancellable |

**ℓ=2 (quadrupole):** Heegner-locked and the only even prime. Observationally anomalous in Planck data — anomalously low power, unexpectedly aligned with the ecliptic. This is the model predicting its own anchor before the data was checked.

**Heegner boundary transitions:** Primes immediately adjacent to Heegner numbers (e.g. ℓ=41 adjacent to 43, ℓ=5 adjacent to 3 and 7) are transition modes — flag for special weighting in field dynamics.

***

## Parity Split

The even/odd parity split is the first-order signal of which side of the competition dominates.

```
even_alm = { a_ℓm : ℓ even }   ← soliton structure (survived)
odd_alm  = { a_ℓm : ℓ odd  }   ← void pressure (tried to cancel)
```

This maps onto the classification:
- Even non-Heegner → `SOLITON_COMPOSITE` (the bulk of surviving structure)
- Odd non-Heegner prime → `VOID_PRIME` (maximum cancellation pressure)
- Odd non-Heegner composite → `VOID_COMPOSITE` (soft background)
- Heegner (both parities) → `HEEGNER_LOCKED` (neither side, fixed skeleton)

The **parity asymmetry ratio** is the scalar summary of the whole competition:

```
parity_asymmetry = power(odd_alm) / power(even_alm)
                 = Σ |a_ℓm|² for odd ℓ  /  Σ |a_ℓm|² for even ℓ

    > 1  →  void pressure dominates in this epoch
    < 1  →  soliton structure dominates
    = 1  →  exact balance (expect never to observe this)
```

This ratio is a derived observable, not a tuning parameter. If the model is correct, the real Planck data should yield a specific value that feeds directly into the field dynamics.

***

## Script 1: `preprocess_cmb_harmonics.py`

**Purpose:** Decompose the CMB temperature map into classified harmonic components. Produce the four class-split alm arrays and the derived scalar outputs used by the rest of the pipeline.

**Input:** `data/COM_CMB_IQU-smica_2048_R3.00_full.fits`

**Outputs:**
- `processed/alm_classified.json` — mapping of every ℓ to its class
- `processed/alm_by_class.npz` — four complex128 alm arrays keyed by class name
- `processed/parity_asymmetry.json` — scalar summary statistics
- `processed/power_spectrum_validation.json` — residuals vs Planck TT

```
LOAD FITS map (field=0, temperature)
    nside = 2048

REMOVE monopole (ℓ=0) and dipole (ℓ=1) — these are observational artefacts

DECOMPOSE → alm
    lmax = 1000
    alm = map2alm(clean_map, lmax=1000)

CLASSIFY every ℓ in 0..lmax
    → emit alm_classified.json

SPLIT alm arrays by class:
    heegner_alm       = alm with all ℓ ∉ HEEGNER_LOCKED zeroed
    soliton_alm       = alm with all ℓ ∉ SOLITON_COMPOSITE zeroed
    void_prime_alm    = alm with all ℓ ∉ VOID_PRIME zeroed
    void_comp_alm     = alm with all ℓ ∉ VOID_COMPOSITE zeroed
    → save as processed/alm_by_class.npz

COMPUTE parity split:
    even_power = Σ |a_ℓm|² for even ℓ (excluding ℓ=0,1)
    odd_power  = Σ |a_ℓm|² for odd  ℓ (excluding ℓ=1)
    parity_asymmetry = odd_power / even_power

    per_class_power = {
        HEEGNER_LOCKED:    Σ |a_ℓm|² over heegner_alm,
        VOID_PRIME:        Σ |a_ℓm|² over void_prime_alm,
        SOLITON_COMPOSITE: Σ |a_ℓm|² over soliton_alm,
        VOID_COMPOSITE:    Σ |a_ℓm|² over void_comp_alm
    }
    → save as processed/parity_asymmetry.json

VALIDATE against published power spectrum:
    Cl_ours   = alm2cl(alm)
    Cl_planck = load data/COM_PowerSpect_CMB-TT-full_R3.01
    residuals = (Cl_ours - Cl_planck) / Cl_planck
    flag any ℓ where |residual| > 0.05
    note: expect ℓ=2,3 to show known anomalies — these are signal not noise
    → save as processed/power_spectrum_validation.json
```

***

## Script 2: `preprocess_lensing_seeds.py`

**Purpose:** Extract node positions from the κ map. Each peak is a location where the universe has already resolved the soliton/void competition in favour of solitons — matter condensed there. These are the empirical anchors of the field.

**Inputs:**
- `data/planck_lensing_kappa_MV.fits`
- `data/planck_lensing_mask.fits.gz`
- `processed/alm_classified.json` (from Script 1)
- `processed/alm_by_class.npz` (from Script 1)

**Outputs:**
- `processed/nodes.json` — full node list with classification and field samples
- `processed/nodes.npz` — numpy arrays for fast field computation

```
LOAD kappa alm from planck_lensing_kappa_MV.fits
CONVERT alm → pixel map at Nside=512
    (large-scale structure only — fine detail is noise at this stage)

APPLY mask
    valid = lensing_mask > 0.5
    kappa_map[~valid] = NaN

FIND PEAKS (soliton condensation sites):
    threshold  = percentile(kappa_map[valid], 95)
    hot_pixels = kappa_map > threshold
    clusters   = connected_component_labelling(hot_pixels)
    FOR each cluster:
        centroid = mean pixel position weighted by κ value
        θ, φ     = pix2ang(centroid)
        κ_peak   = max κ in cluster
        radius   = angular_extent(cluster)  [degrees]

CLASSIFY each node by harmonic field content:
    FOR each node at (θ, φ):
        heegner_power  = evaluate heegner_alm map at (θ, φ)
        void_prime_power = evaluate void_prime_alm map at (θ, φ)
        total_power    = evaluate full alm map at (θ, φ)

        heegner_affinity = heegner_power / total_power
        void_pressure    = void_prime_power / total_power

        node_class =
            HEEGNER_ANCHOR   if heegner_affinity > 0.3
            BIFURCATION_NODE if κ_peak > 95th percentile AND heegner_affinity ≤ 0.3
            AMBIENT_NODE     otherwise

        NOTE: thresholds (0.3, 95th pct) are initial estimates — recalibrate
              against validation_report after first run

CONVERT (θ, φ) → unit direction vectors:
    [x, y, z] = [sin θ cos φ, sin θ sin φ, cos θ]
    coordinate system: Galactic (IAU convention, same as healpy default)

EXPORT nodes.json:
    [{
        "id":               int,
        "direction":        [x, y, z],
        "theta":            float,  # radians
        "phi":              float,  # radians
        "kappa":            float,
        "radius_deg":       float,
        "node_class":       "HEEGNER_ANCHOR" | "BIFURCATION_NODE" | "AMBIENT_NODE",
        "heegner_affinity": float,
        "void_pressure":    float
    }, ...]

EXPORT nodes.npz:
    directions:        float32 (N, 3)
    kappa:             float32 (N,)
    heegner_affinity:  float32 (N,)
    void_pressure:     float32 (N,)
    node_class:        int8    (N,)    # 0=HEEGNER, 1=BIFURCATION, 2=AMBIENT
```

***

## Script 3: `validate.py`

**Purpose:** Verify the pipeline outputs are physically self-consistent before any downstream use. The key check is that the parity asymmetry ratio is a stable derived quantity — not sensitive to minor threshold choices.

**Inputs:** All outputs from Scripts 1 and 2.

**Outputs:** `processed/validation_report.txt`

```
LOAD processed/alm_by_class.npz
LOAD processed/parity_asymmetry.json
LOAD processed/power_spectrum_validation.json
LOAD processed/nodes.json

REPORT power spectrum fidelity:
    fraction of ℓ modes within 5% of Planck TT
    list flagged anomalies — note ℓ=2,3 are expected
    confirm ℓ=2,3 show in HEEGNER_LOCKED class (they should)

REPORT harmonic class breakdown:
    count and fractional power per class
    confirm: SOLITON_COMPOSITE + HEEGNER_LOCKED > 50% of total power
             (solitons must dominate for the model to be self-consistent)

REPORT parity asymmetry:
    parity_asymmetry value
    per-class power fractions
    sensitivity check: recompute excluding ℓ=2,3 — ratio should be stable

REPORT node classification:
    N total, N per class
    mean κ per class (HEEGNER_ANCHOR should have highest mean κ)
    mean heegner_affinity per class
    mean void_pressure per class

SENSITIVITY CHECK — threshold stability:
    rerun node classification at ±10% threshold variation
    report fraction of nodes that change class
    if > 5% of nodes change class → thresholds need recalibration

PRINT validation_report.txt with all of the above
```

***

## Data Flow

```
data/COM_CMB_IQU-smica_2048_R3.00_full.fits
data/COM_PowerSpect_CMB-TT-full_R3.01
    │
    └──► Script 1: preprocess_cmb_harmonics.py
              │
              ├──► processed/alm_classified.json
              ├──► processed/alm_by_class.npz
              ├──► processed/parity_asymmetry.json
              └──► processed/power_spectrum_validation.json

data/planck_lensing_kappa_MV.fits
data/planck_lensing_mask.fits.gz
    │
    └──► Script 2: preprocess_lensing_seeds.py  (reads alm_by_class.npz)
              │
              ├──► processed/nodes.json
              └──► processed/nodes.npz

processed/*
    │
    └──► Script 3: validate.py
              │
              └──► processed/validation_report.txt

processed/alm_by_class.npz
processed/nodes.npz
processed/parity_asymmetry.json
    │
    └──► field computation (downstream)
```

***

## Derived Scalars

These values emerge from the pipeline and characterise the field state. They are observable quantities, not free parameters.

| Scalar | Formula | Interpretation |
|---|---|---|
| `parity_asymmetry` | power(odd ℓ) / power(even ℓ) | Universe's current competition ratio. Expected ~1 with slight void-dominance bias. |
| `heegner_fraction` | power(HEEGNER_LOCKED) / total_power | Fraction of CMB energy locked in the skeleton. |
| `void_fraction` | power(VOID_PRIME + VOID_COMPOSITE) / total_power | Total void pressure as fraction of boundary energy. |
| `mean_heegner_affinity` | mean over HEEGNER_ANCHOR nodes | How strongly anchor nodes sit in Heegner-dominated sky regions. |
| `mean_void_pressure` | mean over all nodes | Background void pressure at condensation sites. |IyBDYXVzYWwgRmllbGQgUGlwZWxpbm