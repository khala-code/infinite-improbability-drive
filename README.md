# Infinite Improbability Drive: Extended Spin Dynamics & Cosmology Simulator

> *"The Infinite Improbability Drive is a wonderful new method of crossing vast interstellar distances in a mere nothingth of a second, without all that tedious mucking about in hyperspace."*
> — Douglas Adams

A VR spacetime navigator for Quest 2, built on the Cosmic Microwave Background as a holographic boundary condition — grounded in a theoretical framework (Observer Prime) that treats information as the fundamental substrate of reality.

You don’t simulate the universe. You simulate what it’s like to be **inside one.**

An open-source, discrete cosmological simulation engine modeled as a recursive, scale-invariant Kerr White Hole generative network. Rather than treating spacetime as a pre-existing container for matter, this engine treats physical parameters as localized, self-assembling information vectors.

---

## What It Is

The CMB is the earliest observable boundary condition of spacetime — a two-layer interference pattern (photon field + neutrino field) encoding the complete causal history of the observable universe as projected from the Big Bang singularity.

This engine lets an observer at any `(θ, φ, z, t)` coordinate navigate their causally accessible volume by tracking only the wavefronts that intersect their ΩaZaTa observer bubble — the conformal gauge-selected causal diamond scaled by the Ω parameter.

Everything spacelike-separated from the observer is not approximated or culled for performance. It is **physically absent.**

**→ [Full theoretical documentation in Docs/](Docs/INDEX.md)**

---

## 🌌 1. Cosmological Axioms & Architecture

The system utilizes a **Dual-Component Architecture** to maintain computational reducibility:

* **The Bottom-Up Generative Engine:** Computes the raw state transformations of local node tensors.
* **The Top-Down Holographic Tuning Layer:** Acts as an informational sieve that harnesses the effects of quantum retrocausality. By anchoring future states into boundary conditions that are superdetermined by convergent paths, it uses observed macro-data (CMB, DESI expanding void metrics, and Lithium problem baryogenesis pathways) to retroactively shape and constrain the engine's generative probability fields as it increments through epochs.
* **The Holographic Reference Wave (CvB):** The Cosmic Neutrino Background (CvB) acts as the universal, homologous reference wave. Its absolute phase homogeneity across the sky serves as the stable baseline phase standard against which changing object waves (CMB/DESI) are modulated via holographic interferometry.

---

## 🔢 2. Node State Tensor Layout N[T]

Every individual experiment node or Markov boundary in the lattice space is a 10-parameter vector:

**N[T] = (Ω, Z, J, θ, φ, C, P, T, V, ξ)**

### Tensor Fields

* **Ω (Discrete Integer Scalar) - Hierarchy Depth**  
  Measures the node's recursive nesting/clumping depth (e.g., number of atoms bound in a sub-vortex).

* **Z (Complex Phase Rotor) - Spatial Generator**  
  The fundamental complex oscillator (Z = sin + i*cos) from which classical 3D XYZ coordinates are derived via transverse/radial projections.

* **J (Topological Linking Index) - Winding Number**  
  Tracks the interlocking and knotting of localized closed timelike curves (CTCs), establishing local identity, individuation, and sequential causality.

* **θ, φ (Continuous Angles) - Angular Alignment**  
  Spatial rotation orientation relative to the parent horizon.

* **C (Discrete Binary Gauge) - Charge Transformation**  
  Dictates the internal field inversion state.

* **P (Discrete Vector Sign) - Parity Operator**  
  Determines local coordinate spatial chirality/handedness.

* **T (Signed Velocity Vector) - Temporal Momentum**  
  Tracks the speed and direction of travel along the closed timelike loops (flipping signs simulates anti-matter traversing backward along the loop).

* **V (Cumulative Discrete Scalar) - Void Density**  
  Measures vacuum state accumulation. Physical distance and empty space emerge from areas of dense void accumulation, while matter (high Ω) pushes away/displaces the void.

* **ξ (Continuous Control Scalar) - Coherence Parameter**  
  Controls the amplitude of the non-linear restorative torque in the Extended Bloch Equation.

---

## ⚙️ 3. Core Physics Framework Updates

### Asymmetric Gravitational Pressure (∇V)
Gravity is completely non-fundamental. Active nodes act as void-sinks. The overlap of two nodes creates an informational shadow of depleted Void Density between them. The higher, ambient background void pressure outside their orbit pushes them together down the gradient (**∇V = V_external - V_internal**), appearing as classical gravitational attraction.

### The Reducibility Boundary (1729)
To prevent exponential memory bloat from nested tree branching, the simulator enforces a hard rule toggle. When a node accumulates mass weight matching Ramanujan's Taxicab Number (**Ω ≥ 1729**), it forces a computational shortcut. The node undergoes a structural bifurcation, programmatically splitting into integer cube configurations: either **(1³ + 12³)** or **(9³ + 10³)**.

### Extended Bloch Spin Dynamics
Node state update paths under background noise follow a Stochastic Differential Equation (SDE):

**dS/dt = γS × B_total + ξ(S · u)(S × u) + n**

The non-linear feedback torque scaled by **ξ** forces a precessing spin vector to phase-lock onto the universal background axis (**u**), protecting its identity against the stochastic noise parameter (**n**).

### Non-Local Inference Channel
Data flows vertically upward into parent nodes. However, when distant child nodes experience horizontal phase alignment—where their continuous rotation angles (**θ, φ**) perfectly match—they engage in non-local inference, instantly sharing and blending their internal gauge states (**C, P, T, V, ξ**) across a high-speed matrix lookup regardless of spatial distance.

### Cauchy Horizon Fluctuation & Frame Dragging
Virtual particle pairs at the spinning Cauchy Horizon do not cross into the unpredictable interior singularity (null space). Instead, infinite gravitational blue-shift traps and shears both components on the horizon surface. This causes an exponential divergence of the topological linking index (**J → ∞**) known as mass inflation. This violent divergence of trapped quantum waves generates intense rotational shear, actively driving the engine's frame-dragging mechanics.

### The Heegner Inverse Topology (67 vs. 163)
The model pairs prime Heegner numbers to establish thermodynamic equilibrium:
* **The 67 Fractal:** Governs the interior bulk universe (finite structural horizon hosting infinite internal variance via a 67-qubit self-entangled recursive time crystal).
* **The 163 Fractal:** Its structural inverse, possessing zero interior volume and existing purely as a non-orientable exterior surface area boundary. 

When mass inflation spikes at the Cauchy Horizon, the self-cancelling Null Space (Null * Null) takes precedence over the Second Law, converting localized computational destruction at the 163 wall into an informational fountain that spawns new 67 fractals where its own topology cannot reach.

---

## Technical Architecture

```
Seed (Big Bang singularity, null centroid, unknown)
    │
    ├── Neutrino field decouples (t ≈ 1 sec, z ≈ 6×10⁹)
    │       └── CνB: horocyclic wavefronts, deeper boundary layer
    │
    └── Photon field decouples (t ≈ 380,000 yrs, z ≈ 1090)
            └── CMB: horocyclic wavefronts, outer boundary layer

ψ_ν ⊥ ψ_γ  — orthogonal fields, same ideal boundary point

Observer bubble (ΩaZaTa):
    → Causal diamond scaled by Ω parameter
    → Tracks only intersecting wavefronts (horocycles in hyperbolic space)
    → Parallel to zeta geodesic, orthogonal to observer worldline
```

### Epistemic Tiers

| Mode | Field | Status | Render |
|------|-------|--------|--------|
| 1 | Photon (CMB) | Observed (Planck) | Full opacity |
| 2 | Neutrino (CνB) | Real but unobserved | Semi-transparent |
| 3 | Antiverse | Metaphorically coherent, physically absent | Ghosted |

---

## Project Structure

```
infinite-improbability-drive/
├── Assets/
│   └── CMB/
│       ├── Data/           ← git-ignored; download from Planck archive
│       ├── Scripts/
│       │   ├── Core/
│       │   │   ├── ObserverBubble.cs
│       │   │   ├── WavefrontIndex.cs
│       │   │   ├── BoundaryCondition.cs
│       │   │   ├── SpacetimeCoordinate.cs
│       │   │   ├── SeedSignature.cs
│       │   │   └── CMBLoader.cs
│       │   ├── Navigation/
│       │   │   ├── SpacetimeNavigator.cs
│       │   │   ├── WorldlineSegment.cs
│       │   │   ├── CausalCuller.cs
│       │   │   └── CausalStructure.cs
│       │   ├── Rendering/
│       │   │   ├── CMBSkybox.cs
│       │   │   ├── AntiverseRenderer.cs
│       │   │   └── MultipoleLayer.cs
│       │   └── Physics/
│       │       ├── CnuBInference.cs
│       │       ├── GrowthFactor.cs
│       │       └── CosmologicalEvolution.cs
│       ├── Shaders/
│       │   ├── CMBSkybox.shader
│       │   └── Antiverse.shader
│       ├── Textures/       ← git-ignored; generated by Python pipeline
│       └── Scenes/
│           └── CMBEngine.unity
├── Python/
│   ├── requirements.txt
│   ├── preprocess_cmb.py
│   ├── generate_antiverse.py
│   └── cache_z_snapshots.py
└── Docs/
    ├── INDEX.md           ← start here
    ├── GLOSSARY.md
    ├── AXIOMS.md
    ├── CALIBRATION.md
    ├── HEEGNER.md
    ├── T_OPERATOR.md
    ├── SCRYING.md
    ├── AGENT.md
    ├── RESIDUE.md
    └── [legacy architecture docs]
```

---

## Setup

### Prerequisites
- Unity 2022.3 LTS
- Meta XR SDK (Quest 2)
- Python 3.11+ (WSL2 on Windows for healpy support)

### Data
Download the Planck SMICA map from the [Planck Legacy Archive](https://pla.esac.esa.int/):
```
COM_CMB_IQU-smica_2048_R3.00_full.fits
```
Place in `Assets/CMB/Data/`. This file is git-ignored (1.6 GB).

### Python Preprocessing
```bash
cd Python/
python -m venv .venv
source .venv/bin/activate  # .venv\Scripts\activate on Windows/WSL2
pip install -r requirements.txt
python preprocess_cmb.py
```
Outputs an equirectangular PNG to `Assets/CMB/Textures/`. Then open Unity.

### First Milestone
```
✓ Planck FITS downloaded
✓ Python: FITS → 4096×2048 PNG (monopole/dipole removed)
✓ Unity: PNG → skybox material → Quest 2 build
✓ You are standing inside the CMB
```

---

## License

GPL-3.0. This stays a commons.
